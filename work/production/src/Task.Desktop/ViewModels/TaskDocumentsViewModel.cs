using Task.Desktop.TaskApi;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed record TaskDocument(Guid Id, Guid TargetId, string Name, string? Type)
{
    public string Kind => Type switch
    {
        "folder_reference" => "Папка",
        "file_reference" => "Файл",
        "virtual_folder" => "Раздел каталога",
        _ => ""
    };
    public bool CanOpen => Type is "file_reference" or "folder_reference" or null;
}

public sealed class TaskDocumentsViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string, bool> _has;
    private readonly Func<bool> _canEdit;
    private readonly Func<bool> _isActiveTask;
    private IReadOnlyList<TaskDocument> _items = [];
    private IReadOnlyList<TaskChoice> _choices = [];
    private TaskChoice? _selected;
    private string _search = "", _path = "", _message = "";
    private bool _confirmed, _disposed;

    internal TaskDocumentsViewModel(Guid source, TaskDocumentAccess documents,
        Func<string, CancellationToken, System.Threading.Tasks.Task<IReadOnlyList<TaskChoice>?>> search,
        Func<Guid, bool, CancellationToken, System.Threading.Tasks.Task<bool>> changeLink,
        Func<string, bool> has, Func<bool> canEdit, Func<bool> isActiveTask)
    {
        Source = source;
        _has = has;
        _canEdit = canEdit;
        _isActiveTask = isActiveTask;
        SearchCommand = new(async (_, token) =>
        {
            var options = await search(Search.Trim(), token);
            token.ThrowIfCancellationRequested();
            Selected = null;
            Choices = options ?? [];
            Message = options is null ? "Не удалось прочитать каталог. Проверьте права и подключение."
                : options.Count == 0 ? "Объекты каталога не найдены." : "";
        }, _ => CanAdd && !IsBusy);
        AddCommand = new(async (_, token) =>
        {
            if (await changeLink(Selected!.Id!.Value, true, token) && !_disposed)
            { Selected = null; Message = "Связь добавлена."; }
        }, _ => CanAdd && !IsBusy && Selected?.Id is { } id && Choices.Contains(Selected) && !Contains(id));
        AddPathCommand = new(async (_, token) =>
        {
            var entered = PathText;
            var result = await documents.AddPathAsync(entered.Trim(), () => !_disposed && CanAddPath, token);
            token.ThrowIfCancellationRequested();
            if (!result.HasLocation || result.ItemId is not { } id) { Message = result.Message; return; }
            if (Contains(id)) { Message = "Эта ссылка уже есть у задачи."; return; }
            if (!CanAddPath) { Message = "Путь добавлен в каталог. Связывание сейчас недоступно."; return; }
            if (PathText == entered) PathText = "";
            if (await changeLink(id, true, token) && !_disposed)
                Message = "Связь добавлена.";
            else if (!_disposed)
            {
                if (string.IsNullOrEmpty(PathText)) PathText = entered;
                Message = "Путь доступен в каталоге. Проверьте, сохранилась ли связь, и при необходимости повторите.";
            }
        }, _ => CanAddPath && !IsBusy && !string.IsNullOrWhiteSpace(PathText));
        RemoveCommand = new(async (parameter, token) =>
        {
            if (await changeLink(((TaskDocument)parameter!).Id, false, token) && !_disposed)
                Message = "Связь убрана.";
        }, parameter => CanRemove && !IsBusy && parameter is TaskDocument item && Items.Contains(item));
        OpenCommand = new(async (parameter, token) =>
        {
            var item = (TaskDocument)parameter!;
            Message = "Проверяем доступ к пути…";
            var message = await documents.OpenAsync(source, item.TargetId, item.Type == "folder_reference", token);
            if (!_disposed && !token.IsCancellationRequested) Message = message;
        }, parameter => CanRead && Has("FileReference.Open") && !IsBusy && parameter is TaskDocument { CanOpen: true } item && Items.Contains(item));
        foreach (var command in Commands)
        {
            command.CanExecuteChanged += CommandStateChanged;
            command.ExecutionFailed += _ => Message = "Не удалось выполнить действие. Проверьте подключение и повторите попытку.";
        }
    }

    internal Guid Source { get; }
    public IReadOnlyList<TaskDocument> Items { get => _items; private set => SetProperty(ref _items, value); }
    public IReadOnlyList<TaskChoice> Choices { get => _choices; private set => SetProperty(ref _choices, value); }
    public TaskChoice? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) AddCommand.RaiseCanExecuteChanged(); } }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string PathText { get => _path; set { if (SetProperty(ref _path, value)) AddPathCommand.RaiseCanExecuteChanged(); } }
    public string Message { get => _message; internal set => SetProperty(ref _message, value); }
    public bool CanRead => !_disposed && Has("ObjectLink.Read") && Has("FileCatalog.Read");
    public bool ShowLinkEditor => CanRead && Has("ObjectLink.Create") && _isActiveTask();
    public bool ShowPathEditor => ShowLinkEditor && Has("FileCatalog.Create") && Has("FileCatalog.Update") && Has("FileLocation.Update");
    public bool CanAdd => _confirmed && ShowLinkEditor && _canEdit();
    public bool CanAddPath => _confirmed && ShowPathEditor && _canEdit();
    public bool CanRemove => _confirmed && CanRead && Has("ObjectLink.Delete") && _canEdit();
    public bool HasItems => Items.Count > 0;
    public string EmptyText => !CanRead ? "Связи недоступны с текущими правами или без подключения."
        : !_confirmed ? "Загружаем связи…" : "Связанных файлов и папок пока нет.";
    internal bool HasDraft => Selected is not null || !string.IsNullOrWhiteSpace(PathText);
    internal bool IsMutating => AddCommand.IsExecuting || AddPathCommand.IsExecuting || RemoveCommand.IsExecuting;
    private bool IsBusy => IsMutating || SearchCommand.IsExecuting || OpenCommand.IsExecuting;

    public AsyncCommand SearchCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand AddPathCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand OpenCommand { get; }
    private AsyncCommand[] Commands => [SearchCommand, AddCommand, AddPathCommand, RemoveCommand, OpenCommand];

    private bool Has(string code) => _has(code);
    private bool Contains(Guid id) => Items.Any(item => item.TargetId == id);

    internal void CopyDrafts(TaskDocumentsViewModel other)
    {
        Search = other.Search;
        PathText = other.PathText;
        if (!CanRead) return;
        Choices = other.Choices;
        Selected = other.Selected;
    }

    internal void SetSnapshot(IReadOnlyList<TaskDocument> items)
    {
        _confirmed = CanRead;
        Items = CanRead ? items : [];
        if (Selected?.Id is { } id && Contains(id)) Selected = null;
        Notify();
    }

    internal void Invalidate()
    {
        foreach (var command in Commands) command.Cancel();
        _confirmed = false;
        Items = [];
        Choices = [];
        Selected = null;
        Message = "";
        Notify();
    }

    private void CommandStateChanged(object? sender, EventArgs args)
    {
        if (_notifying) return;
        OnPropertyChanged(nameof(IsMutating));
        Notify();
    }

    private bool _notifying;
    internal void Notify()
    {
        if (_notifying) return;
        _notifying = true;
        try
        {
            foreach (var name in new[] { nameof(ShowLinkEditor), nameof(ShowPathEditor), nameof(CanAdd), nameof(CanAddPath), nameof(CanRemove), nameof(HasItems), nameof(EmptyText) })
                OnPropertyChanged(name);
            foreach (var command in Commands) command.RaiseCanExecuteChanged();
        }
        finally { _notifying = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var command in Commands)
        {
            command.CanExecuteChanged -= CommandStateChanged;
            command.Dispose();
        }
    }
}
