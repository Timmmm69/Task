using Npgsql;
using Task.Domain;

namespace Task.Infrastructure.Persistence;

internal static class PostgresTaskCardValidation
{
    public static void EnsureVisible(NpgsqlConnection connection, NpgsqlTransaction? transaction, TaskAggregate task, Guid actor)
    {
        if (task.Metadata.CreatedBy == actor || task.Content.RequesterUserId == actor || task.Content.AssigneeIds.Contains(actor) || task.Content.WatcherIds.Contains(actor)) return;
        using var check = new NpgsqlCommand("SELECT work.task_project_visible($1,$2,$3);", connection, transaction);
        check.Parameters.AddWithValue(task.Metadata.OrganizationId);
        check.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid, Value = (object?)task.Content.ProjectId ?? DBNull.Value });
        check.Parameters.AddWithValue(actor);
        if (check.ExecuteScalar() is not true) throw new KeyNotFoundException("Task is not visible.");
    }

    public static void Validate(NpgsqlConnection connection, NpgsqlTransaction transaction, TaskAggregate task, Guid actor, TaskAggregate? previous = null)
    {
        var card = task.Content;
        if (previous?.Content.ProjectId is { } oldProject && oldProject != card.ProjectId)
        {
            using var sourceProject = new NpgsqlCommand("SELECT work.task_project_writable($1,$2,$3,'task.update');", connection, transaction);
            sourceProject.Parameters.AddWithValue(task.Metadata.OrganizationId);
            sourceProject.Parameters.AddWithValue(oldProject);
            sourceProject.Parameters.AddWithValue(actor);
            if (sourceProject.ExecuteScalar() is not true) throw new ArgumentException("Source project does not permit moving this task.");
        }
        if (card.ProjectId is not null && (previous is null || previous.Content.ProjectId != card.ProjectId))
        {
            using var project = new NpgsqlCommand("SELECT work.task_project_writable($1,$2,$3,$4);", connection, transaction);
            project.Parameters.AddWithValue(task.Metadata.OrganizationId); project.Parameters.AddWithValue(card.ProjectId.Value);
            project.Parameters.AddWithValue(actor); project.Parameters.AddWithValue(previous is null ? "task.create" : "task.update");
            if (project.ExecuteScalar() is not true) throw new ArgumentException("Target project does not permit this task action.");
        }
        foreach (var id in card.AssigneeIds.Concat(card.WatcherIds).Concat(card.RequesterUserId is { } requester ? [requester] : []).Distinct())
        {
            using var user = new NpgsqlCommand("SELECT 1 FROM iam.user_accounts u JOIN core.objects o ON o.id=u.id AND o.organization_id=u.organization_id " +
                "WHERE u.organization_id=$1 AND u.id=$2 AND u.account_status='active' AND o.lifecycle_state='active';", connection, transaction);
            user.Parameters.AddWithValue(task.Metadata.OrganizationId); user.Parameters.AddWithValue(id);
            if (user.ExecuteScalar() is null) throw new ArgumentException("Task participant is unavailable.");
        }
        if (card.PrimaryCounterpartyObjectId is { } counterparty)
        {
            using var target = new NpgsqlCommand("SELECT 1 FROM core.objects WHERE organization_id=$1 AND id=$2 AND object_type IN ('contact','company') AND lifecycle_state='active';", connection, transaction);
            target.Parameters.AddWithValue(task.Metadata.OrganizationId); target.Parameters.AddWithValue(counterparty);
            if (target.ExecuteScalar() is null) throw new ArgumentException("Counterparty is unavailable.");
        }
        if (card.ParentTaskId is { } parentId)
        {
            // The tenant transaction lock also protects concurrent moves from introducing a second level.
            var parent = PostgresTaskAggregateStore.Get(connection, transaction, parentId, task.Metadata.OrganizationId);
            if (parent is null || parentId == task.Metadata.Id || parent.Content.ParentTaskId is not null ||
                parent.Metadata.LifecycleState != EntityLifecycleState.Active || parent.WorkStatus is TaskWorkStatus.Completed or TaskWorkStatus.Cancelled)
                throw new ArgumentException("Subtasks require an active top-level parent.");
            EnsureVisible(connection, transaction, parent, actor);
            using var children = new NpgsqlCommand("SELECT 1 FROM work.tasks WHERE organization_id=$1 AND parent_task_id=$2 LIMIT 1;", connection, transaction);
            children.Parameters.AddWithValue(task.Metadata.OrganizationId); children.Parameters.AddWithValue(task.Metadata.Id);
            if (children.ExecuteScalar() is not null) throw new ArgumentException("Only one subtask level is supported.");
        }
    }
}
