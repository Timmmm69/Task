using Npgsql;

namespace Task.Infrastructure.Persistence;

/// <summary>Atomic notification projection of existing product events; shares their transaction and authorization.</summary>
internal static class PostgresEventNotifications
{
    public static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid eventId)
    {
        var command = new NpgsqlCommand(
            """
            WITH source AS (
                SELECT e.*,o.created_by,o.object_type,task.card_content,task.status,e.occurred_at AS created_at,
                    coalesce(task.title,project.name) AS source_title,
                    CASE
                        WHEN e.event_type='TaskCreated' THEN 'task.assigned'
                        WHEN e.event_type='TaskUpdated' AND e.changed_fields && ARRAY['assigneeIds']::text[] THEN 'task.assignment_changed'
                        WHEN e.event_type='TaskUpdated' AND e.changed_fields && ARRAY['startAtUtc','deadlineAt']::text[] THEN 'task.rescheduled'
                        WHEN e.event_type='TaskStatusChanged' AND task.status='completed' THEN 'task.completed'
                        WHEN e.event_type='TaskStatusChanged' AND task.status='review' THEN 'task.review'
                        WHEN e.event_type='task.task-comment-add' THEN 'task.comment'
                        WHEN e.event_type='project.member-add' THEN 'project.invitation'
                    END AS notice_type
                FROM governance.domain_events e
                JOIN core.objects o ON o.organization_id=e.organization_id AND o.id=e.aggregate_id
                LEFT JOIN work.tasks task ON task.organization_id=o.organization_id AND task.id=o.id
                LEFT JOIN projects.projects project ON project.organization_id=o.organization_id AND project.id=o.id
                WHERE e.id=@event AND o.lifecycle_state='active'
            ), recipients AS (
                SELECT s.*,recipient.id AS recipient_id FROM source s
                CROSS JOIN LATERAL (
                    SELECT value::uuid AS id FROM jsonb_array_elements_text(coalesce(s.card_content->'assigneeIds','[]'::jsonb))
                    UNION SELECT value::uuid FROM jsonb_array_elements_text(coalesce(s.card_content->'watcherIds','[]'::jsonb)) WHERE s.notice_type<>'task.assigned'
                    UNION SELECT s.created_by WHERE s.object_type='task' AND s.notice_type<>'task.assigned'
                    UNION SELECT (s.card_content->>'requesterUserId')::uuid WHERE s.card_content->>'requesterUserId' IS NOT NULL AND s.notice_type<>'task.assigned'
                    UNION SELECT (s.payload->>'recipientUserId')::uuid WHERE s.notice_type='project.invitation'
                ) recipient
                JOIN iam.user_accounts account ON account.organization_id=s.organization_id AND account.id=recipient.id AND account.account_status='active'
                WHERE s.notice_type IS NOT NULL
                    AND (recipient.id<>s.actor_user_id OR s.notice_type='task.assigned')
                    AND iam.object_allowed(s.organization_id,s.aggregate_id,recipient.id,
                        CASE s.object_type WHEN 'task' THEN 'task.read' ELSE 'project.read' END,false)
            ), notifications AS (
                SELECT r.*,md5('task-notification:'||r.id::text||r.recipient_id::text)::uuid AS notification_id,
                    CASE notice_type WHEN 'task.assigned' THEN 'Вам назначена задача'
                        WHEN 'task.assignment_changed' THEN 'Изменены исполнители задачи'
                        WHEN 'task.rescheduled' THEN 'Изменено время задачи'
                        WHEN 'task.completed' THEN 'Задача завершена'
                        WHEN 'task.review' THEN 'Задача отправлена на проверку'
                        WHEN 'task.comment' THEN 'Новый комментарий к задаче'
                        ELSE 'Приглашение в проект' END AS notice_title
                FROM recipients r
            ), objects AS (
                INSERT INTO core.objects(id,organization_id,object_type,lifecycle_state,version,created_at,created_by,updated_at,updated_by)
                SELECT notification_id,organization_id,'notification','active',1,created_at,created_by,created_at,created_by FROM notifications
                ON CONFLICT(id) DO NOTHING RETURNING id
            )
            INSERT INTO notify.notifications(id,organization_id,recipient_user_id,notification_type,source_object_id,title,body,status,not_before,deduplication_key,action_payload)
            SELECT n.notification_id,n.organization_id,n.recipient_id,n.notice_type,n.aggregate_id,n.notice_title,
                coalesce(n.source_title,'Откройте Task, чтобы посмотреть событие.'),'pending',n.created_at,
                n.id::text||'|'||n.recipient_id::text,jsonb_build_object('sourceObjectType',n.object_type,'sourceEventId',n.id)
            FROM notifications n JOIN objects o ON o.id=n.notification_id
            ON CONFLICT(id) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("event", eventId); return command;
    }
}
