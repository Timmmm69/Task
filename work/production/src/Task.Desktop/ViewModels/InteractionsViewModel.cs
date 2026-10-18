using System.Globalization;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed class InteractionsViewModel : ViewModelBase, IDisposable
{
    private readonly IDesktopInteractionsClient _client;
    private readonly Func<bool> _canRead, _canRequest, _canCreate, _canUpdate;
    private CancellationTokenSource? _request;
    private long _generation;
    private bool _disposed, _busy, _editing, _blocked;
    private InteractionCounterparty? _counterparty;
    private DesktopInteraction? _details, _pendingCreate;
    private string? _cursor, _key, _message;
    private readonly HashSet<string> _cursors = new(StringComparer.Ordinal);
    private IReadOnlyList<DesktopInteraction> _items = [];
    private IReadOnlyList<string> _participants = [];
    private string _type = "call", _occurredAt = "", _subject = "", _detailText = "", _nextStep = "", _dueAt = "";

    public InteractionsViewModel(IDesktopInteractionsClient client, Func<bool> canRead, Func<bool> canRequest, Func<bool> canCreate, Func<bool> canUpdate)
    {
        _client = client; _canRead = canRead; _canRequest = canRequest; _canCreate = canCreate; _canUpdate = canUpdate;
        RefreshCommand = new((_, ct) => LoadPageAsync(false, ct), _ => CanRequest && !IsBusy && !IsEditing);
        MoreCommand = new((_, ct) => LoadPageAsync(true, ct), _ => CanRequest && !IsBusy && !IsEditing && _cursor is not null);
        OpenCommand = new((p, ct) => OpenAsync(((DesktopInteraction)p!).Id, ct), p => CanRequest && !IsBusy && !IsEditing && p is DesktopInteraction v && Items.Contains(v));
        NewCommand = new((_, _) =>
        {
            Details = null; _pendingCreate = null; _key = Guid.NewGuid().ToString("D"); _blocked = false;
            FillDraft(null); IsEditing = true; Message = null; return System.Threading.Tasks.Task.CompletedTask;
        }, _ => CanRequest && _canCreate() && !IsBusy && !IsEditing);
        EditCommand = new((_, _) => { FillDraft(Details); IsEditing = true; Message = null; return System.Threading.Tasks.Task.CompletedTask; },
            _ => CanRequest && _canUpdate() && Details?.LifecycleState == "active" && !_blocked && !IsBusy && !IsEditing);
        CancelCommand = new((_, _) =>
        {
            _pendingCreate = null; _key = null; IsEditing = false; FillDraft(null);
            Message = _blocked ? "Обновите список и откройте запись перед повторным изменением." : null;
            return System.Threading.Tasks.Task.CompletedTask;
        }, _ => IsEditing && !IsBusy);
        SaveCommand = new(SaveAsync, _ => CanRequest && IsEditing && !_blocked && !IsBusy && (Details is null ? _canCreate() : _canUpdate()));
        foreach (var command in Commands) command.ExecutionFailed += _ => Message = "Запрос не подтверждён. Повторите загрузку.";
    }

    public InteractionCounterparty? Counterparty { get => _counterparty; private set { SetProperty(ref _counterparty, value); OnPropertyChanged(nameof(HasCounterparty)); } }
    public bool HasCounterparty => Counterparty is not null;
    private bool CanRequest => !_disposed && _canRead() && _canRequest() && HasCounterparty;
    public IReadOnlyList<DesktopInteraction> Items { get => _items; private set => SetProperty(ref _items, value); }
    public DesktopInteraction? Details { get => _details; private set { SetProperty(ref _details, value); OnPropertyChanged(nameof(HasDetails)); Notify(); } }
    public bool HasDetails => Details is not null;
    public IReadOnlyList<string> Participants { get => _participants; private set => SetProperty(ref _participants, value); }
    public bool IsMutating => SaveCommand.IsExecuting;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); } }
    public bool IsEditing { get => _editing; private set { SetProperty(ref _editing, value); Notify(); } }
    public bool CanChangeDraft => IsEditing && !IsBusy && _pendingCreate is null;
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string OccurredAt { get => _occurredAt; set => SetProperty(ref _occurredAt, value); }
    public string Subject { get => _subject; set => SetProperty(ref _subject, value); }
    public string DetailText { get => _detailText; set => SetProperty(ref _detailText, value); }
    public string NextStep { get => _nextStep; set => SetProperty(ref _nextStep, value); }
    public string DueAt { get => _dueAt; set => SetProperty(ref _dueAt, value); }
    public IReadOnlyList<ContactStatusChoice> TypeChoices { get; } = [new("call", "Звонок"), new("meeting", "Встреча"), new("email", "Письмо"), new("agreement", "Договорённость"), new("note", "Заметка"), new("next_step", "Следующий шаг")];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand MoreCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand SaveCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, MoreCommand, OpenCommand, NewCommand, EditCommand, CancelCommand, SaveCommand];

    public void SetCounterparty(InteractionCounterparty? value)
    {
        if (Counterparty?.Id == value?.Id && Counterparty?.Type == value?.Type) { Counterparty = value; return; }
        InvalidateScope(); Counterparty = value; Notify();
    }

    private async System.Threading.Tasks.Task<bool> ConfirmCounterpartyAsync(long generation, CancellationToken ct)
    {
        if (Counterparty is not { } target) return false;
        var result = await _client.GetInteractionCounterpartyAsync(target.Id, target.Type, ct);
        if (!Current(generation, ct)) return false;
        if (result is DesktopWorkResult<InteractionCounterparty>.Succeeded s && s.Value.Id == target.Id && s.Value.Type == target.Type)
        { Counterparty = s.Value; return true; }
        Failure(result); return false;
    }

    private async System.Threading.Tasks.Task LoadPageAsync(bool more, CancellationToken ct)
    {
        CancelRequest();
        if (!more) { _cursor = null; _cursors.Clear(); }
        Items = []; Details = null; Participants = []; Message = null;
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _request = request; IsBusy = true;
        try
        {
            if (!await ConfirmCounterpartyAsync(generation, request.Token)) return;
            var result = await _client.GetInteractionsAsync(more ? _cursor : null, request.Token);
            if (!Current(generation, request.Token)) return;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopInteraction>>.Succeeded page)
            {
                if (page.Value.Count > 50) { _cursor = null; Failure(new DesktopWorkResult<IReadOnlyList<DesktopInteraction>>.MalformedResponse()); return; }
                Items = page.Value.Where(v => v.CounterpartyObjectId == Counterparty!.Id && v.LifecycleState == "active").DistinctBy(v => v.Id).ToArray();
                _cursor = page.NextCursor;
                if (_cursor is not null && !_cursors.Add(_cursor)) { _cursor = null; Message = "Сервер повторил страницу. Обновите просмотр."; }
                else Message = Items.Count == 0 ? "В этой странице API совпадений нет. Это не полная история контрагента." : "Показаны совпадения только в текущей странице API (до 50 записей). Это не полная история.";
                _blocked = false;
            }
            else { _cursor = null; Failure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, request.Token)) Message = "Не удалось загрузить взаимодействия. Повторите запрос."; }
        finally { EndRequest(generation); }
    }

    public async System.Threading.Tasks.Task OpenAsync(Guid id, CancellationToken ct = default)
    {
        if (!CanRequest || IsBusy || IsEditing) return;
        CancelRequest(); Details = null; Participants = []; Message = null;
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _request = request; IsBusy = true;
        try
        {
            var result = await _client.GetInteractionAsync(id, request.Token);
            if (!Current(generation, request.Token)) return;
            if (result is DesktopWorkResult<DesktopInteraction>.Succeeded s)
            {
                if (s.Value.CounterpartyObjectId != Counterparty!.Id || s.Value.Id != id || s.Value.LifecycleState != "active")
                { Items = Items.Where(v => v.Id != id).ToArray(); _blocked = true; Message = "Связь или состояние записи изменились. Обновите просмотр."; return; }
                if (!await ConfirmCounterpartyAsync(generation, request.Token)) return;
                // Resolve only a bounded subset; never expose raw participant IDs or names from stale objects.
                var names = new List<string>();
                foreach (var participant in s.Value.ParticipantObjectIds.Take(20))
                {
                    var person = await _client.GetInteractionCounterpartyAsync(participant, "contact", request.Token);
                    if (!Current(generation, request.Token)) return;
                    if (person is DesktopWorkResult<InteractionCounterparty>.NotFound or DesktopWorkResult<InteractionCounterparty>.Forbidden)
                        person = await _client.GetInteractionCounterpartyAsync(participant, "company", request.Token);
                    if (!Current(generation, request.Token)) return;
                    if (person is DesktopWorkResult<InteractionCounterparty>.AuthenticationFailure) { Failure(person); return; }
                    if (person is DesktopWorkResult<InteractionCounterparty>.Succeeded visible) names.Add(visible.Value.Name);
                }
                Details = s.Value; Participants = names; _blocked = false; FillDraft(s.Value);
                Message = s.Value.ParticipantObjectIds.Count == 0 ? "Участники не указаны."
                    : "Участники: только проверенные контакты и компании из первых 20 связей. Остальные сведения не загружены.";
            }
            else { Items = Items.Where(v => v.Id != id).ToArray(); Failure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current(generation, request.Token)) Message = "Не удалось открыть взаимодействие. Повторите запрос."; }
        finally { EndRequest(generation); }
    }

    private async System.Threading.Tasks.Task SaveAsync(object? parameter, CancellationToken ct)
    {
        var create = Details is null;
        if (_pendingCreate is null)
        {
            if (!ParseDate(OccurredAt, out var at) || (!string.IsNullOrWhiteSpace(DueAt) && !ParseDate(DueAt, out _)))
            { Message = "Дата: ГГГГ-ММ-ДД ЧЧ:ММ ±ЧЧ:ММ. Укажите явное смещение часового пояса."; return; }
            var value = new DesktopInteraction(Details?.Id ?? Guid.Empty, Details?.Version ?? 0, Counterparty!.Id, Type, Details is { } original && OccurredAt == original.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture) ? original.OccurredAt : at, Subject,
                DetailText, NextStep, string.IsNullOrWhiteSpace(DueAt) ? null : Details?.NextStepDueAt is { } originalDue && DueAt == originalDue.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture) ? originalDue : DateTimeOffset.ParseExact(DueAt, "yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture), Details?.ParticipantObjectIds ?? []);
            if (DesktopWorkApiClient.ValidateInteraction(value) is { } error) { Message = error; return; }
            if (create) _pendingCreate = value;
            else _draftUpdate = value;
        }
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _request = request; IsBusy = true; Message = null;
        try
        {
            if (!await ConfirmCounterpartyAsync(generation, request.Token) || !(create ? _canCreate() : _canUpdate())) return;
            var result = await _client.SaveInteractionAsync(create ? _pendingCreate! : _draftUpdate!, create ? _key : null, request.Token);
            if (!Current(generation, request.Token) || !(create ? _canCreate() : _canUpdate())) return;
            if (result is DesktopWorkResult<DesktopInteraction>.Succeeded saved)
            {
                if (saved.Value.CounterpartyObjectId != Counterparty!.Id || saved.Value.LifecycleState != "active"
                    || !create && saved.Value.Id != Details!.Id)
                { InvalidateScope(); Message = "Связь записи изменилась. Результат не показан; обновите карточку."; return; }
                if (!await ConfirmCounterpartyAsync(generation, request.Token)) return;
                Details = saved.Value; IsEditing = false; _pendingCreate = _draftUpdate = null; _key = null;
                // Do not pretend a just-created record belongs to the current list page.
                Items = Items.Select(v => v.Id == saved.Value.Id ? saved.Value : v).ToArray();
                Message = "Взаимодействие сохранено. Данные подтверждены сервером.";
            }
            else Failure(result);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { if (generation == _generation) { if (!create) _blocked = true; Message = "Запрос отменён. Сохранение не подтверждено. При повторе создания используется тот же ключ."; } }
        catch (Exception)
        { if (Current(generation, request.Token)) { if (!create) _blocked = true; Message = "Сохранение не подтверждено. Повтор создания отправит ту же запись с тем же ключом."; } }
        finally { EndRequest(generation); }
    }

    private DesktopInteraction? _draftUpdate;
    private static bool ParseDate(string text, out DateTimeOffset value) => DateTimeOffset.TryParseExact(text, "yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    private void FillDraft(DesktopInteraction? value)
    {
        Type = value?.InteractionType ?? "call"; OccurredAt = (value?.OccurredAt ?? DateTimeOffset.Now).ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
        Subject = value?.Subject ?? ""; DetailText = value?.Details ?? ""; NextStep = value?.NextStep ?? "";
        DueAt = value?.NextStepDueAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture) ?? "";
    }
    private void Failure<T>(DesktopWorkResult<T> result)
    {
        Message = result switch
        {
            DesktopWorkResult<T>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopWorkResult<T>.Forbidden => "Нет доступа к записи или контрагенту. Обновите карточку.",
            DesktopWorkResult<T>.NotFound => "Запись или контрагент больше не доступны. Обновите карточку.",
            DesktopWorkResult<T>.Conflict => "Версия изменилась. Ввод сохранён. Отмените редактирование, обновите список и откройте запись для сравнения.",
            DesktopWorkResult<T>.ValidationFailure => "Сервер отклонил данные. Проверьте поля и доступность связанных объектов.",
            _ => "Сервер недоступен или ответ некорректен. Результат не подтверждён. Повтор создания использует прежний ключ и данные."
        };
        if (result is DesktopWorkResult<T>.AuthenticationFailure or DesktopWorkResult<T>.Forbidden or DesktopWorkResult<T>.NotFound)
        { var message = Message; InvalidateScope(); Message = message; _blocked = true; }
        if (result is DesktopWorkResult<T>.Conflict || Details is not null && result is not DesktopWorkResult<T>.ValidationFailure) _blocked = true;
        // A rejected create was not persisted; a new draft payload may now use a new key.
        if (result is DesktopWorkResult<T>.ValidationFailure && _pendingCreate is not null) { _pendingCreate = null; _key = Guid.NewGuid().ToString("D"); }
        Notify();
    }
    public void UpdateAccess()
    {
        if (!_canRead()) InvalidateScope();
        else if (!_canRequest() || IsBusy && IsEditing && !(Details is null ? _canCreate() : _canUpdate()))
        { if (IsBusy && Details is not null && IsEditing) _blocked = true; CancelRequest(); }
        Notify();
    }
    public void InvalidateScope()
    {
        CancelRequest(); Items = []; Details = null; Participants = []; IsEditing = false; Counterparty = null;
        _pendingCreate = _draftUpdate = null; _key = _cursor = null; _cursors.Clear(); FillDraft(null); Message = null; Notify();
    }
    private bool Current(long generation, CancellationToken ct) => generation == _generation && !ct.IsCancellationRequested && CanRequest;
    private void CancelRequest() { ++_generation; _request?.Cancel(); _request = null; IsBusy = false; }
    private void EndRequest(long generation) { if (generation == _generation) { _request = null; IsBusy = false; } }
    private void Notify()
    {
        OnPropertyChanged(nameof(CanChangeDraft));
        if (SaveCommand is not null) foreach (var command in Commands) command.RaiseCanExecuteChanged();
    }
    public void Dispose() { _disposed = true; InvalidateScope(); foreach (var command in Commands) command.Dispose(); }
}
