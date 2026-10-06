using Microsoft.Data.Sqlite;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Personal;

/// <summary>Typed presentation adapter. The only dependency is local storage.</summary>
public sealed class PersonalTasksClient(PersonalTaskStore store) : IDesktopTasksApiClient
{
    public async System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskPage>> GetTasksAsync(string? cursor = null, CancellationToken cancellationToken = default)
    {
        if (cursor is not null) return new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor();
        try { var tasks = await System.Threading.Tasks.Task.Run(store.List, cancellationToken); return new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new(tasks, null, tasks.Count)); }
        catch (Exception e) when (e is SqliteException or System.IO.IOException or UnauthorizedAccessException) { return new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable(); }
    }
    public async System.Threading.Tasks.Task<DesktopTasksApiResult<DesktopTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try { var task = await System.Threading.Tasks.Task.Run(() => store.Get(id), cancellationToken); return task is null ? new DesktopTasksApiResult<DesktopTaskDto>.NotFound() : new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task); }
        catch (Exception e) when (e is SqliteException or System.IO.IOException or UnauthorizedAccessException) { return new DesktopTasksApiResult<DesktopTaskDto>.ServerUnavailable(); }
    }
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> CreateTaskAsync(DesktopCreateTaskCommand command, CancellationToken cancellationToken = default) => Write(() => store.Create(command), cancellationToken);
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> PatchTaskAsync(DesktopPatchTaskCommand command, CancellationToken cancellationToken = default) => Write(() => store.Patch(command), cancellationToken);
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> TransitionTaskAsync(DesktopTransitionTaskCommand command, CancellationToken cancellationToken = default) => Write(() => store.Transition(command), cancellationToken);
    public System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> WriteChecklistAsync(Guid id, long version, Guid? childId = null, string? text = null, bool? completed = null, bool remove = false, CancellationToken cancellationToken = default) => Write(() => store.WriteChecklist(id, version, childId, text, completed, remove), cancellationToken);
    public System.Threading.Tasks.Task<IReadOnlyList<TaskWorkspaceItem>> GetChecklistAsync(Guid id, CancellationToken token = default) => System.Threading.Tasks.Task.Run(() => store.Checklist(id), token);

    private static System.Threading.Tasks.Task<DesktopTaskWriteResult<DesktopTaskDto>> Write(Func<DesktopTaskDto> action, CancellationToken token) => System.Threading.Tasks.Task.Run<DesktopTaskWriteResult<DesktopTaskDto>>(() =>
    {
        // Cancellation can prevent a write from starting; after COMMIT report its real outcome.
        token.ThrowIfCancellationRequested();
        try { var task = action(); return new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task, task.Version, false); }
        catch (PersonalVersionConflictException) { return new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict(); }
        catch (PersonalTaskNotFoundException) { return new DesktopTaskWriteResult<DesktopTaskDto>.NotFound(); }
        catch (PersonalTransitionException) { return new DesktopTaskWriteResult<DesktopTaskDto>.InvalidTransition(); }
        catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException)
        { return new DesktopTaskWriteResult<DesktopTaskDto>.ValidationFailure("Проверьте поля задачи, планирование и родительскую задачу.", new Dictionary<string, IReadOnlyList<string>>()); }
        catch (Exception e) when (e is SqliteException or System.IO.IOException or UnauthorizedAccessException) { return new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(); }
    }, token);
}
