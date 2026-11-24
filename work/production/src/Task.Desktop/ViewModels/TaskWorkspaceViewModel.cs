using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record TaskWorkspaceItem(Guid Id, Guid? TargetId, string Text, bool Completed = false);

public sealed class TaskWorkspaceViewModel : ViewModelBase
{
    private readonly IDesktopTaskWorkspaceClient _client;
    private readonly IDesktopTasksApiClient _tasks;
    private readonly DesktopTaskDto _source;
    private readonly Action<DesktopTaskDto> _apply;
    private readonly CancellationToken _lifetime;
    private readonly HashSet<string> _capabilities;
    private readonly Dictionary<string, string> _keys = [];
    private bool _busy, _loaded;
    private long _version;
    private string _checkText = "", _comment = "", _search = "";
    private string? _message;
    private TaskChoice? _dependency;
    private int _accessGeneration;
    private bool _allowsWrites = true;
    public TaskWorkspaceViewModel(IDesktopTasksApiClient tasks, IDesktopTaskWorkspaceClient client, DesktopTaskDto source,
        IEnumerable<string> capabilities, Action<DesktopTaskDto> apply, CancellationToken lifetime)
        : this(tasks, client, source, capabilities, apply, lifetime, null) { }

    internal TaskWorkspaceViewModel(IDesktopTasksApiClient tasks, IDesktopTaskWorkspaceClient client, DesktopTaskDto source,
        IEnumerable<string> capabilities, Action<DesktopTaskDto> apply, CancellationToken lifetime, TaskDocumentAccess? documents)
    {
        _tasks = tasks; _client = client; _source = source; _capabilities = capabilities.ToHashSet(); _apply = apply; _lifetime = lifetime; _version = source.Version;
        RefreshCommand = new AsyncCommand(async (_, _) => await LoadAsync(), _ => !IsBusy && !IsChangingDocuments);
        SearchCommand = new AsyncCommand(async (_, _) => await LoadOptionsAsync(), _ => !IsBusy && !IsChangingDocuments);
        AddCheckCommand = new AsyncCommand(async (_, _) => { await Write("checklist", HttpMethod.Post, new() { ["text"] = CheckText }); }, _ => CanEdit && !string.IsNullOrWhiteSpace(CheckText));
        ToggleCheckCommand = new AsyncCommand(async (p, _) => { if (p is TaskWorkspaceItem i) await Write($"checklist/{i.Id:D}", HttpMethod.Patch, new() { ["isCompleted"] = !i.Completed }); }, _ => CanEdit);
        RemoveCheckCommand = new AsyncCommand(async (p, _) => { if (p is TaskWorkspaceItem i) await Write($"checklist/{i.Id:D}", HttpMethod.Delete, new()); }, _ => CanEdit);
        AddCommentCommand = new AsyncCommand(async (_, _) => { await Write("comments", HttpMethod.Post, new() { ["body"] = Comment }); }, _ => CanComment && !string.IsNullOrWhiteSpace(Comment));
        if (documents is not null)
        {
            Documents = new(source.Id, documents, SearchFilesAsync, ChangeFileLinkAsync,
                code => _allowsWrites && _capabilities.Contains(code), () => CanEditTask, () => TaskIsActive);
            Documents.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TaskDocumentsViewModel.IsMutating))
                { OnPropertyChanged(nameof(IsChangingDocuments)); Notify(); }
            };
        }
        AddDependencyCommand = new AsyncCommand(async (_, _) => { if (Dependency?.Id is { } id) await Write("dependencies", HttpMethod.Post, new() { ["predecessorId"] = id }); }, _ => CanEdit && Dependency?.Id is not null);
        RemoveDependencyCommand = new AsyncCommand(async (p, _) => { if (p is TaskWorkspaceItem i) await Write($"dependencies/{i.Id:D}", HttpMethod.Delete, new()); }, _ => CanEdit);
    }
    internal bool HasUnconfirmedMutation => _keys.Count > 0;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); } }
    internal bool IsChangingDocuments => Documents?.IsMutating == true;
    public TaskDocumentsViewModel? Documents { get; }
    public bool CanEdit => CanEditTask && !IsChangingDocuments;
    private bool TaskIsActive => _source.Status is not (DesktopTaskStatus.Completed or DesktopTaskStatus.Cancelled) && _capabilities.Contains("Task.Update");
    private bool CanEditTask => _allowsWrites && _loaded && !IsBusy && TaskIsActive;
    public bool CanComment => _allowsWrites && _loaded && !IsBusy && !IsChangingDocuments && _capabilities.Contains("Comment.Create");
    public void UpdateAccess(IEnumerable<string> capabilities, bool allowsWrites) => UpdateAccess(capabilities, allowsWrites, false);

    internal void UpdateAccess(IEnumerable<string> capabilities, bool allowsWrites, bool refreshDocuments)
    {
        _capabilities.Clear(); _capabilities.UnionWith(capabilities); _allowsWrites = allowsWrites;
        if (refreshDocuments && Documents is not null)
        {
            _accessGeneration++;
            Documents.Invalidate();
            if (Documents.CanRead && !IsBusy) _ = LoadAsync();
        }
        Notify();
    }
    public void CopyDrafts(TaskWorkspaceViewModel other)
    {
        CheckText = other.CheckText; Comment = other.Comment; Search = other.Search; Dependency = other.Dependency;
        if (other.Documents is not null) Documents?.CopyDrafts(other.Documents);
    }
    public string CheckText { get => _checkText; set { SetProperty(ref _checkText, value); Notify(); } }
    public string Comment { get => _comment; set { SetProperty(ref _comment, value); Notify(); } }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string ContextText { get; private set; } = "";
    public string HistoryNotice { get; private set; } = "";
    public IReadOnlyList<TaskWorkspaceItem> Checklist { get; private set; } = [];
    public IReadOnlyList<TaskWorkspaceItem> Comments { get; private set; } = [];
    public IReadOnlyList<TaskWorkspaceItem> Subtasks { get; private set; } = [];
    public IReadOnlyList<TaskWorkspaceItem> Dependencies { get; private set; } = [];
    public IReadOnlyList<TaskWorkspaceItem> History { get; private set; } = [];
    public IReadOnlyList<TaskChoice> TaskOptions { get; private set; } = [];
    public TaskChoice? Dependency { get => _dependency; set { SetProperty(ref _dependency, value); Notify(); } }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand AddCheckCommand { get; }
    public AsyncCommand ToggleCheckCommand { get; }
    public AsyncCommand RemoveCheckCommand { get; }
    public AsyncCommand AddCommentCommand { get; }
    public AsyncCommand AddDependencyCommand { get; }
    public AsyncCommand RemoveDependencyCommand { get; }

    public async System.Threading.Tasks.Task LoadAsync()
    {
        if (IsBusy || _lifetime.IsCancellationRequested) return;
        IsBusy = true; _loaded = false;
        var access = _accessGeneration;
        try
        {
            var result = await _client.GetWorkspaceAsync(_source.Id, _lifetime);
            if (!result.Succeeded)
            {
                Message = result.Error;
                Documents?.Invalidate();
                if (Documents is not null) Documents.Message = result.Error ?? "Не удалось прочитать связи.";
                return;
            }
            _version = result.Version; var b = result.Body!;
            IReadOnlyList<TaskWorkspaceItem> Items(string key, Func<JsonNode, string> text) => (b[key]?.AsArray() ?? []).Select(n => new TaskWorkspaceItem(Guid.Parse(n!["id"]!.ToString()), Guid.TryParse(n["targetId"]?.ToString(), out var id) ? id : null, text(n), n["isCompleted"]?.GetValue<bool>() ?? false)).ToArray();
            Checklist = Items("checklist", n => n["text"]!.ToString());
            Comments = Items("comments", n => $"{n["authorName"]}: {n["body"]}");
            Subtasks = Items("subtasks", n => $"{n["name"]} · {Status(n["status"]?.ToString())}");
            Dependencies = Items("dependencies", n => $"{n["name"]} · {Status(n["status"]?.ToString())}");
            if (Documents is not null && access == _accessGeneration)
                Documents.SetSnapshot((b["files"]?.AsArray() ?? []).Select(n => new TaskDocument(
                    Guid.Parse(n!["id"]!.ToString()), Guid.Parse(n["targetId"]!.ToString()), n["name"]!.ToString(), n["itemType"]?.ToString())).ToArray());
            History = Items("history", n => $"{DateTimeOffset.Parse(n["occurredAt"]!.ToString()).ToLocalTime():g} · {ActionText(n["action"]?.ToString())}");
            HistoryNotice = b["historyVisible"]?.GetValue<bool>() == true ? "Последние 200 изменений" : "История недоступна с текущими правами.";
            _loaded = true; Message = null;
            foreach (var name in new[] { nameof(Checklist), nameof(Comments), nameof(Subtasks), nameof(Dependencies), nameof(History), nameof(HistoryNotice) }) OnPropertyChanged(name);
            await LoadOptionsAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { Message = "Не удалось загрузить связи задачи. Обновите карточку."; }
        finally
        {
            IsBusy = false;
            if (access != _accessGeneration && Documents?.CanRead == true) _ = LoadAsync();
        }
    }

    private async System.Threading.Tasks.Task<IReadOnlyList<TaskChoice>?> SearchFilesAsync(string search, CancellationToken token)
    {
        var result = await _client.GetOptionsAsync(search, token);
        if (!result.Succeeded) return null;
        return (result.Body!["files"]?.AsArray() ?? [])
            .Select(n => new TaskChoice(Guid.Parse(n!["id"]!.ToString()), n["name"]!.ToString())).ToArray();
    }

    private System.Threading.Tasks.Task<bool> ChangeFileLinkAsync(Guid id, bool attach, CancellationToken token) =>
        attach
            ? Write("links", HttpMethod.Post, new() { ["sourceObjectId"] = _source.Id, ["targetObjectId"] = id, ["linkType"] = "task_file" }, token)
            : Write($"links/{id:D}", HttpMethod.Delete, new(), token);

    private async System.Threading.Tasks.Task LoadOptionsAsync()
    {
        var result = await _client.GetOptionsAsync(Search, _lifetime);
        if (!result.Succeeded) { Message = result.Error; return; }
        var b = result.Body!;
        IReadOnlyList<TaskChoice> Options(string key) => (b[key]?.AsArray() ?? []).Select(n => new TaskChoice(Guid.Parse(n!["id"]!.ToString()), n["name"]!.ToString())).ToArray();
        TaskOptions = Options("tasks").Where(p => p.Id != _source.Id).ToArray();
        string Name(string key, Guid? id) => id is null ? "не указано" : Options(key).FirstOrDefault(p => p.Id == id)?.Name ?? "вне результатов поиска или недоступно";
        var card = _source.Card;
        ContextText = $"Автор: {Name("people", _source.AuthorUserId)}\nПостановщик: {Name("people", card?.RequesterUserId)}\nПроект: {Name("projects", card?.ProjectId)}\nКонтрагент: {Name("counterparties", card?.PrimaryCounterpartyObjectId)}\nРодитель: {Name("tasks", card?.ParentTaskId)}\nИсполнители: {string.Join(", ", _source.AssigneeIds.Select(id => Name("people", id)))}\nНаблюдатели: {string.Join(", ", _source.WatcherIds.Select(id => Name("people", id)))}";
        foreach (var name in new[] { nameof(TaskOptions), nameof(ContextText) }) OnPropertyChanged(name);
        if (b.Any(p => p.Key.EndsWith("HasMore") && p.Value?.GetValue<bool>() == true)) Message = "Показаны первые 200 вариантов. Уточните поиск.";
    }
    private async System.Threading.Tasks.Task<bool> Write(string path, HttpMethod method, JsonObject body, CancellationToken token = default)
    {
        if (IsBusy || _lifetime.IsCancellationRequested) return false;
        IsBusy = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime);
        var signature = $"{_version}:{method}:{path}:{body}";
        if (!_keys.TryGetValue(signature, out var key)) _keys[signature] = key = Guid.NewGuid().ToString("D");
        try
        {
            var result = await _client.WriteWorkspaceAsync(_source.Id, _version, path, method, body, key, operation.Token);
            if (!result.Succeeded) { Message = result.Error; return false; }
            _keys.Remove(signature);
            // Clear only the submitted draft if it was not changed while the request was in flight.
            if (path == "comments" && Comment == body["body"]?.ToString()) Comment = "";
            if (path == "checklist" && method == HttpMethod.Post && CheckText == body["text"]?.ToString()) CheckText = "";
            var task = await _tasks.GetTaskByIdAsync(_source.Id, _lifetime);
            if (task is DesktopTasksApiResult<DesktopTaskDto>.Succeeded ok) _apply(ok.Value);
            else { Message = "Изменение сохранено. Обновите карточку для актуальных данных."; _loaded = false; }
            return true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { return false; }
        catch (Exception) { Message = "Не удалось сохранить. Данные сохранены в форме; повторите действие."; return false; }
        finally { IsBusy = false; }
    }
    private void Notify() { Documents?.Notify(); OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanComment)); foreach (var command in new[] { RefreshCommand, SearchCommand, AddCheckCommand, ToggleCheckCommand, RemoveCheckCommand, AddCommentCommand, AddDependencyCommand, RemoveDependencyCommand }) command?.RaiseCanExecuteChanged(); }
    private static string Status(string? value) => value switch { "new" => "Новая", "in_progress" => "В работе", "review" => "На проверке", "completed" => "Выполнена", "cancelled" => "Отменена", _ => "" };
    private static string ActionText(string? value) => value switch { "TaskCreated" => "Задача создана", "TaskUpdated" => "Изменены поля задачи", "TaskStatusChanged" => "Изменён статус", "task.task-comment-add" => "Добавлен комментарий", "task.task-check-add" => "Добавлен пункт чек-листа", "task.task-check-patch" => "Изменён чек-лист", "task.link-add" => "Добавлена связь", _ => "Изменение задачи" };
}
