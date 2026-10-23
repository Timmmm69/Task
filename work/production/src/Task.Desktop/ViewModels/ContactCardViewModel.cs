using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record ContactStatusChoice(string Value, string Label);

public sealed partial class ContactCardViewModel : ViewModelBase, IDisposable
{
    private readonly Infrastructure.ClosePendingOperations _closeOperations = new();
    internal bool HasUnconfirmedMutation => _closeOperations.HasPending;

    private readonly IDesktopContactDetailsClient _client;
    private readonly Func<bool> _canRead, _canRequest, _canWrite;
    private CancellationTokenSource? _request;
    private long _generation;
    private Guid? _id;
    private bool _disposed, _loading, _editing, _conflict, _writeDenied;
    private DesktopContactDetails? _details;
    private string _firstName = "", _lastName = "", _middleName = "", _displayName = "", _notes = "", _status = "active";
    private string? _message;

    public ContactCardViewModel(IDesktopContactDetailsClient client, Func<bool> canRead, Func<bool> canRequest, Func<bool> canWrite)
    {
        _client = client; _canRead = canRead; _canRequest = canRequest; _canWrite = canWrite;
        InitializeRelations();
        EditCommand = new((_, _) => { FillDraft(); IsEditing = true; Message = null; return System.Threading.Tasks.Task.CompletedTask; },
            _ => CanEdit && !IsEditing && !HasRelationDraft);
        CancelCommand = new((_, _) => { FillDraft(); IsEditing = false; Message = _conflict ? "Обновите карточку перед повторным редактированием." : null; return System.Threading.Tasks.Task.CompletedTask; },
            _ => IsEditing && !IsSaving);
        SaveCommand = new(SaveAsync, _ => CanEdit && IsEditing && !_conflict);
        ReloadCommand = new((_, ct) => LoadAsync(_id, ct), _ => _id is not null && _canRequest() && !IsEditing && !HasRelationDraft && !IsLoading && !IsSaving);
        foreach (var command in Commands) command.ExecutionFailed += _ => Message = "Не удалось выполнить действие. Изменения формы сохранены; повторите запрос.";
        SaveCommand.CanExecuteChanged += (_, _) => { OnPropertyChanged(nameof(IsSaving)); OnPropertyChanged(nameof(CanChangeDraft)); CancelCommand.RaiseCanExecuteChanged(); EditCommand.RaiseCanExecuteChanged(); ReloadCommand.RaiseCanExecuteChanged(); foreach (var command in RelationCommands) command.RaiseCanExecuteChanged(); };
    }

    public event Action<DesktopContact>? Saved;
    public DesktopContactDetails? Details { get => _details; private set { SetProperty(ref _details, value); OnPropertyChanged(nameof(HasDetails)); OnPropertyChanged(nameof(StatusLabel)); NotifyRelations(); } }
    public bool HasDetails => Details is not null;
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); NotifyCommands(); } }
    public bool IsEditing { get => _editing; private set { SetProperty(ref _editing, value); OnPropertyChanged(nameof(CanChangeDraft)); NotifyCommands(); } }
    public bool IsSaving => SaveCommand.IsExecuting || _relationBusy;
    public bool CanChangeDraft => IsEditing && !IsSaving;
    public bool CanEdit => !_disposed && _canWrite() && _canRequest() && Details?.Contact.LifecycleState == "active" && !IsLoading && !_relationBusy && !_writeDenied && !_conflict;
    public string StatusLabel => Details?.Contact.Status == "inactive" ? "Неактивный" : "Активный";
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public string MiddleName { get => _middleName; set => SetProperty(ref _middleName, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public IReadOnlyList<ContactStatusChoice> StatusChoices { get; } = [new("active", "Активный"), new("inactive", "Неактивный")];
    public AsyncCommand EditCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    private IEnumerable<AsyncCommand> Commands => new[] { EditCommand, CancelCommand, SaveCommand, ReloadCommand }.Concat(RelationCommands);

    public async System.Threading.Tasks.Task SelectAsync(Guid? id)
    {
        if (_id == id) return; // List refresh must not replace an unsaved draft.
        _id = id;
        IsEditing = false; Details = null; ClearDraft(); ClearRelationDraft(); _conflict = _writeDenied = false;
        await LoadAsync(id);
    }

    public async System.Threading.Tasks.Task LoadAsync(Guid? id, CancellationToken ct = default)
    {
        CancelRequest();
        Message = id is null ? "Выберите контакт в списке. Если список пуст, создайте новый контакт." : null;
        if (id is null || !_canRead() || !_canRequest() || _disposed) return;
        _id = id;
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _request = request;
        IsLoading = true;
        try
        {
            var result = await _client.GetContactAsync(id.Value, request.Token);
            if (!Current(generation, request.Token)) return;
            if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded success)
            {
                Details = success.Value; _conflict = _writeDenied = false; FillDraft();
            }
            else { Details = null; ShowFailure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, request.Token)) Message = "Не удалось загрузить карточку. Повторите запрос."; }
        finally { if (generation == _generation) { _request = null; IsLoading = false; } }
    }

    private async System.Threading.Tasks.Task SaveAsync(object? _, CancellationToken ct)
    {
        if (Details is not { } original) return;
        var draft = original with
        {
            Contact = original.Contact with { FirstName = FirstName, LastName = LastName, DisplayName = DisplayName, Notes = Notes, Status = Status },
            MiddleName = MiddleName
        };
        if (DesktopWorkApiClient.ValidateContact(draft) is { } error) { Message = error; return; }
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _request = request;
        Message = null;
        try
        {
            var result = await _closeOperations.TrackAsync(() => _client.UpdateContactAsync(draft, request.Token));
            if (!Current(generation, request.Token) || !_canWrite()) return;
            if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded success)
            {
                Details = success.Value; FillDraft(); IsEditing = false;
                Message = "Изменения контакта сохранены.";
                Saved?.Invoke(success.Value.Contact);
            }
            else ShowFailure(result);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { if (generation == _generation) Message = "Запрос отменён. Результат сохранения не подтверждён. Перед повтором обновите карточку."; }
        finally { if (generation == _generation) _request = null; NotifyCommands(); }
    }

    private void ShowFailure(DesktopWorkResult<DesktopContactDetails> result)
    {
        Message = result switch
        {
            DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopWorkResult<DesktopContactDetails>.Forbidden => "Недостаточно прав для этого действия. Изменения формы сохранены.",
            DesktopWorkResult<DesktopContactDetails>.NotFound => "Контакт больше не доступен. Обновите список.",
            DesktopWorkResult<DesktopContactDetails>.Conflict => "Контакт изменён другим пользователем. Ввод сохранён. Отмените редактирование и обновите карточку для сравнения.",
            DesktopWorkResult<DesktopContactDetails>.ValidationFailure validation => validation.Message,
            DesktopWorkResult<DesktopContactDetails>.ServerUnavailable => "Сервер недоступен. Сохранение не подтверждено; ввод сохранён.",
            _ => "Сервер вернул некорректные данные. Сохранение не подтверждено; ввод сохранён."
        };
        if (result is DesktopWorkResult<DesktopContactDetails>.Conflict) _conflict = true;
        if (result is DesktopWorkResult<DesktopContactDetails>.Forbidden) _writeDenied = true;
        if (result is DesktopWorkResult<DesktopContactDetails>.NotFound) { Details = null; _writeDenied = true; }
        if (result is DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure) { Details = null; IsEditing = false; ClearDraft(); _writeDenied = true; }
        NotifyCommands();
    }

    public void UpdateAccess()
    {
        if (!_canRead())
        {
            CancelRequest(); Details = null; IsEditing = false; ClearDraft(); ClearRelationDraft();
            Message = "Нет доступа к карточке. Проверьте права и сессию.";
        }
        else if (!_canRequest() || (!_canWrite() && IsSaving)) { if (IsSaving) _conflict = true; CancelRequest(); }
        NotifyCommands();
    }

    public void InvalidateScope()
    {
        CancelRequest(); Details = null; IsEditing = false; ClearDraft(); ClearRelationDraft();
        Message = "Права доступа изменились. Обновите карточку.";
        NotifyCommands();
    }

    private bool Current(long generation, CancellationToken ct) => !_disposed && !ct.IsCancellationRequested && generation == _generation && _canRead() && _canRequest();
    private void CancelRequest() { ++_generation; _request?.Cancel(); _request = null; IsLoading = false; }
    private void FillDraft()
    {
        if (Details is not { } d) { ClearDraft(); return; }
        FirstName = d.Contact.FirstName; LastName = d.Contact.LastName ?? ""; MiddleName = d.MiddleName ?? "";
        DisplayName = d.Contact.DisplayName; Notes = d.Contact.Notes ?? ""; Status = d.Contact.Status;
    }
    private void ClearDraft() { FirstName = LastName = MiddleName = DisplayName = Notes = ""; Status = "active"; }
    private void NotifyCommands() { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanChangeRelationDraft)); OnPropertyChanged(nameof(RelationAccessMessage)); foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
    public void Dispose() { _disposed = true; CancelRequest(); foreach (var command in Commands) command.Dispose(); }
}
