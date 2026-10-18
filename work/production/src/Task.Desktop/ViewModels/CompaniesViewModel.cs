using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class CompaniesViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopCompaniesClient _client;
    private readonly Func<bool> _canRead, _canRequest, _canCreate, _canUpdate;
    private CancellationTokenSource? _listRequest, _detailRequest, _pickerRequest;
    private long _listGeneration, _selectionGeneration, _pickerGeneration;
    private bool _disposed, _loadingList, _loadingCard, _editing, _creating, _blocked, _busy, _updatingItems;
    private DesktopCompany? _selected, _details, _pendingCreate;
    private string? _cursor, _createKey, _message;
    private string _query = "", _name = "", _legalName = "", _industry = "", _website = "", _taxIdentifier = "", _notes = "", _status = "active";
    private IReadOnlyList<DesktopCompany> _items = [];
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);

    public CompaniesViewModel(IDesktopCompaniesClient client, Func<bool> canRead, Func<bool> canRequest, Func<bool> canCreate, Func<bool> canUpdate)
    {
        _client = client; _canRead = canRead; _canRequest = canRequest; _canCreate = canCreate; _canUpdate = canUpdate;
        RefreshCommand = new((_, ct) => LoadListAsync(false, ct), _ => CanRequest && !IsLoadingList && !IsBusy);
        MoreCommand = new((_, ct) => LoadListAsync(true, ct), _ => CanRequest && HasMore && !IsLoadingList && !IsBusy);
        ReloadCommand = new((_, ct) => LoadCardAsync(SelectedCompany?.Id, ct), _ => CanRequest && SelectedCompany is not null && !IsBusy && !IsEditing && !IsLoadingCard);
        NewCommand = new((_, _) =>
        {
            CancelDetail(); Details = null; IsCreating = true; IsEditing = true; _blocked = false;
            _createKey = Guid.NewGuid().ToString("D"); _pendingCreate = null; FillDraft(null); ClearLinkDraft(); RelatedContacts = []; Message = null;
            return System.Threading.Tasks.Task.CompletedTask;
        }, _ => CanRequest && _canCreate() && !IsBusy && !IsEditing);
        EditCommand = new((_, _) => { FillDraft(Details); IsEditing = true; Message = null; return System.Threading.Tasks.Task.CompletedTask; },
            _ => CanWrite && !IsEditing);
        CancelCommand = new(async (_, ct) =>
        {
            var creating = IsCreating;
            IsEditing = false; IsCreating = false; _pendingCreate = null; _createKey = null; FillDraft(Details);
            if (creating && SelectedCompany is not null) await LoadCardAsync(SelectedCompany.Id, ct);
            else Message = _blocked ? "Обновите карточку перед повторным изменением." : null;
        }, _ => IsEditing && !IsBusy);
        SaveCommand = new(SaveAsync, _ => CanRequest && IsEditing && !IsBusy && (IsCreating ? _canCreate() : CanWrite));
        InitializeRelations();
        foreach (var command in Commands) command.ExecutionFailed += _ => Message = "Не удалось выполнить запрос. Результат записи не подтверждён; обновите карточку.";
    }

    public event Func<Guid, System.Threading.Tasks.Task>? OpenContactRequested;
    public IReadOnlyList<DesktopCompany> Items
    {
        get => _items;
        private set
        {
            _updatingItems = true;
            try { SetProperty(ref _items, value); }
            finally { _updatingItems = false; }
            OnPropertyChanged(nameof(SelectedCompany)); OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public DesktopCompany? SelectedCompany
    {
        get => _selected;
        set { if (!_updatingItems && SetProperty(ref _selected, value) && (value is null || value.Id != Details?.Id)) SelectionTask = SelectAsync(value?.Id); }
    }
    public System.Threading.Tasks.Task SelectionTask { get; private set; } = System.Threading.Tasks.Task.CompletedTask;
    public DesktopCompany? Details { get => _details; private set { SetProperty(ref _details, value); OnPropertyChanged(nameof(HasDetails)); Notify(); } }
    public bool HasDetails => Details is not null;
    public bool IsEmpty => Items.Count == 0 && !IsLoadingList;
    public bool IsLoadingList { get => _loadingList; private set { SetProperty(ref _loadingList, value); OnPropertyChanged(nameof(IsEmpty)); Notify(); } }
    public bool IsLoadingCard { get => _loadingCard; private set { SetProperty(ref _loadingCard, value); Notify(); } }
    public bool IsEditing { get => _editing; private set { SetProperty(ref _editing, value); Notify(); } }
    public bool IsCreating { get => _creating; private set { SetProperty(ref _creating, value); Notify(); } }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); } }
    public bool CanChangeDraft => IsEditing && !IsBusy && _pendingCreate is null;
    private bool CanRequest => !_disposed && _canRead() && _canRequest();
    public bool CanWrite => CanRequest && _canUpdate() && Details?.LifecycleState == "active" && !_blocked && !IsLoadingCard && !IsBusy;
    public bool CanChangeLinks => CanWrite && !IsEditing && !IsBusy;
    public bool HasMore => _cursor is not null;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string Query { get => _query; set { if (SetProperty(ref _query, value)) { CancelList(); _cursor = null; _cursors.Clear(); Notify(); } } }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string LegalName { get => _legalName; set => SetProperty(ref _legalName, value); }
    public string Industry { get => _industry; set => SetProperty(ref _industry, value); }
    public string Website { get => _website; set => SetProperty(ref _website, value); }
    public string TaxIdentifier { get => _taxIdentifier; set => SetProperty(ref _taxIdentifier, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public IReadOnlyList<ContactStatusChoice> StatusChoices { get; } = [new("active", "Активный"), new("inactive", "Неактивный")];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand MoreCommand { get; }
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand SaveCommand { get; }
    private IEnumerable<AsyncCommand> Commands => new[] { RefreshCommand, MoreCommand, ReloadCommand, NewCommand, EditCommand, CancelCommand, SaveCommand }.Concat(RelationCommands);

    public async System.Threading.Tasks.Task LoadListAsync(bool append = false, CancellationToken ct = default)
    {
        if (!CanRequest || IsBusy) return;
        if (Query.Trim().Length > 200) { Message = "Поиск — до 200 символов."; return; }
        CancelList();
        if (!append) { _cursor = null; _cursors.Clear(); }
        var generation = _listGeneration; var selection = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _listRequest = request; IsLoadingList = true;
        try
        {
            var result = await _client.GetCompaniesAsync(Query.Trim(), append ? _cursor : null, request.Token);
            if (!CanRequest || request.IsCancellationRequested || generation != _listGeneration) return;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded page)
            {
                var previous = SelectedCompany;
                var items = (append ? Items.Concat(page.Value) : page.Value).DistinctBy(c => c.Id).ToArray();
                // A selected record can be outside this server page. Recheck its object scope before retaining it.
                if (!append && previous is not null && !items.Any(c => c.Id == previous.Id))
                {
                    var visible = await _client.GetCompanyAsync(previous.Id, request.Token);
                    if (!CanRequest || request.IsCancellationRequested || generation != _listGeneration) return;
                    if (selection == _selectionGeneration)
                    {
                        if (visible is DesktopWorkResult<DesktopCompany>.Succeeded available) items = [.. items, available.Value];
                        else if (visible is DesktopWorkResult<DesktopCompany>.Forbidden or DesktopWorkResult<DesktopCompany>.NotFound or DesktopWorkResult<DesktopCompany>.AuthenticationFailure)
                            await SelectAsync(null);
                        else { Message = "Не удалось подтвердить доступность выбранной компании. Повторите обновление."; return; }
                    }
                }
                Items = items;
                if (selection == _selectionGeneration && previous is not null)
                { _selected = items.FirstOrDefault(c => c.Id == previous.Id); OnPropertyChanged(nameof(SelectedCompany)); }
                _cursor = page.NextCursor;
                Message = _cursor is not null && !_cursors.Add(_cursor) ? "Сервер повторил страницу. Обновите список." : null;
                if (Message is not null) _cursor = null;
                if (selection == _selectionGeneration && SelectedCompany is { } selected && !IsEditing && !IsLoadingCard)
                    await LoadCardAsync(selected.Id, request.Token);
            }
            else
            {
                if (result is DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Forbidden or DesktopWorkResult<IReadOnlyList<DesktopCompany>>.AuthenticationFailure) InvalidateScope();
                Message = ListFailure(result); _cursor = null;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (generation == _listGeneration) { _listRequest = null; IsLoadingList = false; Notify(); } }
    }

    public async System.Threading.Tasks.Task SelectAsync(Guid? id)
    {
        CancelDetail(); Details = null; IsEditing = false; IsCreating = false; _pendingCreate = null; _createKey = null;
        _blocked = false; FillDraft(null); ClearLinkDraft(); RelatedContacts = [];
        if (id is null) { _selected = null; OnPropertyChanged(nameof(SelectedCompany)); Message = "Выберите компанию или создайте новую."; return; }
        await LoadCardAsync(id);
    }

    public async System.Threading.Tasks.Task OpenAsync(Guid id)
    {
        if (!CanRequest) return;
        await SelectAsync(id);
        if (Details is { } company && company.Id == id)
        {
            if (!Items.Any(c => c.Id == id)) Items = [company, .. Items];
            _selected = company; OnPropertyChanged(nameof(SelectedCompany));
        }
    }

    private async System.Threading.Tasks.Task LoadCardAsync(Guid? id, CancellationToken ct = default)
    {
        CancelDetail(); RelatedContacts = [];
        if (id is null || !CanRequest) return;
        var generation = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _detailRequest = request; IsLoadingCard = true; Message = null;
        try
        {
            var result = await _client.GetCompanyAsync(id.Value, request.Token);
            if (!Current(generation, request.Token)) return;
            if (result is DesktopWorkResult<DesktopCompany>.Succeeded success)
            {
                Details = success.Value; _blocked = false; FillDraft(Details); ClearPicker();
                await LoadRelatedAsync(success.Value, generation, request.Token);
            }
            else { Details = null; ShowFailure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (generation == _selectionGeneration) { _detailRequest = null; IsLoadingCard = false; } }
    }

    private async System.Threading.Tasks.Task SaveAsync(object? _, CancellationToken ct)
    {
        var draft = _pendingCreate ?? new DesktopCompany(IsCreating ? Guid.Empty : Details!.Id, IsCreating ? 0 : Details!.Version,
            Name, LegalName, Industry, Website, TaxIdentifier, Notes, Status, Details?.LifecycleState ?? "active");
        if (DesktopWorkApiClient.ValidateCompany(draft) is { } error) { Message = error; return; }
        var create = IsCreating;
        if (create) _pendingCreate = draft; // Freeze payload and key across an uncertain POST retry.
        var generation = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _detailRequest = request; IsBusy = true; Message = null;
        try
        {
            var result = await _client.SaveCompanyAsync(draft, create ? _createKey : null, request.Token);
            if (!Current(generation, request.Token) || !(create ? _canCreate() : _canUpdate())) return;
            if (result is DesktopWorkResult<DesktopCompany>.Succeeded success)
            {
                Details = success.Value; _selected = success.Value; OnPropertyChanged(nameof(SelectedCompany));
                Items = [success.Value, .. Items.Where(c => c.Id != success.Value.Id)];
                _pendingCreate = null; _createKey = null; IsEditing = IsCreating = false; FillDraft(Details);
                Message = "Компания сохранена.";
                // PATCH response does not enrich contacts; obtain the authoritative links again.
                var confirmed = await _client.GetCompanyAsync(success.Value.Id, request.Token);
                if (!Current(generation, request.Token)) return;
                if (confirmed is DesktopWorkResult<DesktopCompany>.Succeeded refreshed)
                { Details = refreshed.Value; await LoadRelatedAsync(refreshed.Value, generation, request.Token); }
                else { RelatedContacts = []; ShowFailure(confirmed); }
            }
            else { if (create && result is DesktopWorkResult<DesktopCompany>.ValidationFailure) _pendingCreate = null; ShowFailure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { if (generation == _selectionGeneration) { _blocked = !create; Message = "Запрос отменён. Сохранение не подтверждено. Повтор создания использует тот же ключ; изменение требует обновления карточки."; } }
        catch (Exception)
        { if (generation == _selectionGeneration) { _blocked = !create; Message = "Результат сохранения не подтверждён. Ввод сохранён. Обновите карточку; повтор создания использует прежний ключ."; } }
        finally { if (generation == _selectionGeneration) _detailRequest = null; IsBusy = false; }
    }

    private void ShowFailure(DesktopWorkResult<DesktopCompany> result)
    {
        Message = result switch
        {
            DesktopWorkResult<DesktopCompany>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopWorkResult<DesktopCompany>.Forbidden => "Недостаточно прав. Обновите карточку и проверьте доступ.",
            DesktopWorkResult<DesktopCompany>.NotFound => "Компания или связь больше не доступна. Обновите список.",
            DesktopWorkResult<DesktopCompany>.Conflict => "Конфликт версии или дублирующая связь. Изменения не подтверждены. Отмените редактирование и обновите карточку.",
            DesktopWorkResult<DesktopCompany>.ValidationFailure validation => validation.Message,
            _ => "Сервер недоступен или ответ некорректен. Сохранение не подтверждено. Ввод сохранён; обновите карточку перед изменением."
        };
        if (result is not DesktopWorkResult<DesktopCompany>.ValidationFailure) _blocked = true;
        if (result is DesktopWorkResult<DesktopCompany>.Forbidden or DesktopWorkResult<DesktopCompany>.NotFound)
        { Details = null; RelatedContacts = []; ClearPicker(); }
        if (result is DesktopWorkResult<DesktopCompany>.AuthenticationFailure) { InvalidateScope(); Message = "Сессия завершена. Выполните вход снова."; }
        Notify();
    }

    private static string ListFailure<T>(DesktopWorkResult<T> result) => result switch
    {
        DesktopWorkResult<T>.Forbidden => "Нет доступа к списку.", DesktopWorkResult<T>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
        DesktopWorkResult<T>.ValidationFailure => "Проверьте запрос поиска.", _ => "Не удалось загрузить список. Повторите запрос."
    };
    public void UpdateAccess()
    {
        if (!_canRead()) InvalidateScope();
        else if (!CanRequest || IsBusy && !(IsCreating ? _canCreate() : _canUpdate()))
        { if (IsBusy) { _blocked = true; Message = "Запрос прерван. Результат записи не подтверждён. Обновите карточку перед изменением."; } CancelList(); CancelDetail(); ClearPicker(); }
        Notify();
    }
    public void InvalidateScope()
    {
        CancelList(); CancelDetail(); ClearLinkDraft(); Items = []; _selected = null; OnPropertyChanged(nameof(SelectedCompany));
        Details = null; RelatedContacts = []; IsEditing = IsCreating = false; _pendingCreate = null; _createKey = null;
        FillDraft(null); _cursor = null; _cursors.Clear(); Message = "Доступ изменился. Обновите список."; Notify();
    }
    private void CancelList() { ++_listGeneration; _listRequest?.Cancel(); _listRequest = null; IsLoadingList = false; }
    private void CancelDetail() { ++_selectionGeneration; _detailRequest?.Cancel(); _detailRequest = null; IsLoadingCard = false; }
    private bool Current(long generation, CancellationToken ct) => generation == _selectionGeneration && !ct.IsCancellationRequested && CanRequest;
    private void FillDraft(DesktopCompany? c)
    { Name = c?.Name ?? ""; LegalName = c?.LegalName ?? ""; Industry = c?.Industry ?? ""; Website = c?.Website ?? ""; TaxIdentifier = c?.TaxIdentifier ?? ""; Notes = c?.Notes ?? ""; Status = c?.Status ?? "active"; }
    private void Notify()
    {
        OnPropertyChanged(nameof(CanWrite)); OnPropertyChanged(nameof(CanChangeDraft)); OnPropertyChanged(nameof(CanChangeLinks)); OnPropertyChanged(nameof(HasMore));
        if (SaveCommand is not null && SearchContactsCommand is not null) foreach (var command in Commands) command.RaiseCanExecuteChanged();
    }
    public void Dispose() { _disposed = true; CancelList(); CancelDetail(); ClearPicker(); foreach (var command in Commands) command.Dispose(); }
}
