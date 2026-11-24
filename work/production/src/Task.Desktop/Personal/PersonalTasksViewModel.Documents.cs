using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed partial class PersonalTasksViewModel
{
    private readonly TaskDocumentAccess _documentAccess;
    private TaskDocumentsViewModel? _documents;

    public TaskDocumentsViewModel? Documents
    {
        get => _documents;
        private set => SetProperty(ref _documents, value);
    }

    private void UpdateDocuments()
    {
        var source = IsTasks ? Selected?.Source : null;
        if (source?.Id == Documents?.Source) return;
        Documents?.Dispose();
        Documents = source is null ? null : new(source.Id, _documentAccess, SearchDocumentsAsync,
            (id, attach, token) => ChangeDocumentLinkAsync(source.Id, id, attach, token), _ => !_disposed && IsTasks,
            () => CanEditTask && Editor is null && string.IsNullOrEmpty(CheckText),
            () => Selected?.Source.Status is DesktopTaskStatus.New or DesktopTaskStatus.InProgress or DesktopTaskStatus.Review);
        if (Documents is { } documents)
        {
            documents.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TaskDocumentsViewModel.IsMutating)) Notify(); };
            _ = LoadDocumentsAsync(documents);
        }
    }

    private async System.Threading.Tasks.Task LoadDocumentsAsync(TaskDocumentsViewModel documents)
    {
        try
        {
            var items = await _client.ReadDocumentsAsync(documents.Source, _lifetime.Token);
            if (!_disposed && ReferenceEquals(Documents, documents)) documents.SetSnapshot(items);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && ReferenceEquals(Documents, documents))
                documents.Message = "Не удалось прочитать связи. Обновите список задач.";
        }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<TaskChoice>?> SearchDocumentsAsync(string search, CancellationToken token)
    {
        var result = await _client.Work.GetCatalogAsync(token);
        return result is DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded items
            ? items.Value.Where(item => item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new TaskChoice(item.Id, item.Name)).ToArray() : null;
    }

    private async System.Threading.Tasks.Task<bool> ChangeDocumentLinkAsync(Guid source, Guid id, bool attach, CancellationToken token)
    {
        if (!CanEditTask || Editor is not null || Selected?.Source is not { } task || task.Id != source
            || Documents is not { } documents) return false;
        IsBusy = true;
        try
        {
            var result = attach
                ? await _client.Work.AddLinkAsync(source, task.Version, id, "task_file", token)
                : await _client.Work.RemoveLinkAsync(source, task.Version, id, token);
            var latest = await _client.GetTaskByIdAsync(source, token);
            if (!_disposed && latest is DesktopTasksApiResult<DesktopTaskDto>.Succeeded updated) UpdateTask(updated.Value);
            await LoadDocumentsAsync(documents);
            if (result is DesktopWorkResult<DesktopObjectLinks>.Succeeded) return true;
            documents.Message = result switch
            {
                DesktopWorkResult<DesktopObjectLinks>.Conflict => "Задача изменилась. Проверьте обновлённую карточку и повторите действие.",
                DesktopWorkResult<DesktopObjectLinks>.ValidationFailure failure => failure.Message,
                _ => "Не удалось изменить связь. Обновите карточку."
            };
            return false;
        }
        finally { IsBusy = false; }
    }
}
