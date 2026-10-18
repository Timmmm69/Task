using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class CompaniesViewModel
{
    private IReadOnlyList<DesktopCompanyContact> _related = [];
    private IReadOnlyList<DesktopContact> _candidates = [];
    private DesktopContact? _candidate;
    private string _contactQuery = "", _jobTitle = "", _department = "", _validFrom = "", _validTo = "";
    private string? _contactCursor;
    private bool _primary, _searching;
    private readonly HashSet<string> _contactCursors = new(StringComparer.Ordinal);

    public IReadOnlyList<DesktopCompanyContact> RelatedContacts { get => _related; private set => SetProperty(ref _related, value); }
    public IReadOnlyList<DesktopContact> ContactCandidates { get => _candidates; private set => SetProperty(ref _candidates, value); }
    public DesktopContact? SelectedCandidate { get => _candidate; set { SetProperty(ref _candidate, value); Notify(); } }
    public string ContactQuery { get => _contactQuery; set { if (SetProperty(ref _contactQuery, value)) ClearPicker(); } }
    public string JobTitle { get => _jobTitle; set => SetProperty(ref _jobTitle, value); }
    public string DepartmentName { get => _department; set => SetProperty(ref _department, value); }
    public string ValidFrom { get => _validFrom; set => SetProperty(ref _validFrom, value); }
    public string ValidTo { get => _validTo; set => SetProperty(ref _validTo, value); }
    public bool IsPrimary { get => _primary; set => SetProperty(ref _primary, value); }
    public bool IsSearchingContacts { get => _searching; private set { SetProperty(ref _searching, value); Notify(); } }
    public AsyncCommand SearchContactsCommand { get; private set; } = null!;
    public AsyncCommand MoreContactsCommand { get; private set; } = null!;
    public AsyncCommand LinkContactCommand { get; private set; } = null!;
    public AsyncCommand UnlinkContactCommand { get; private set; } = null!;
    public AsyncCommand OpenContactCommand { get; private set; } = null!;
    private IEnumerable<AsyncCommand> RelationCommands => [SearchContactsCommand, MoreContactsCommand, LinkContactCommand, UnlinkContactCommand, OpenContactCommand];

    private void InitializeRelations()
    {
        SearchContactsCommand = new((_, ct) => SearchContactsAsync(false, ct), _ => CanChangeLinks && !IsSearchingContacts);
        MoreContactsCommand = new((_, ct) => SearchContactsAsync(true, ct), _ => CanChangeLinks && !IsSearchingContacts && _contactCursor is not null);
        LinkContactCommand = new((_, ct) => ChangeLinkAsync(null, ct), _ => CanChangeLinks && SelectedCandidate is not null && ContactCandidates.Contains(SelectedCandidate));
        UnlinkContactCommand = new((p, ct) => ChangeLinkAsync((DesktopCompanyContact)p!, ct), p => CanChangeLinks && p is DesktopCompanyContact link && RelatedContacts.Contains(link));
        OpenContactCommand = new(async (p, ct) =>
        {
            var link = (DesktopCompanyContact)p!; var generation = _selectionGeneration;
            var result = await _client.GetContactAsync(link.ContactId, ct);
            if (!Current(generation, ct)) return;
            if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" })
            { if (OpenContactRequested is { } open) await open(link.ContactId); }
            else
            { RelatedContacts = RelatedContacts.Where(c => c.ContactId != link.ContactId).ToArray(); Message = "Контакт больше не доступен. Обновите карточку компании."; }
        }, p => CanRequest && !IsBusy && !IsEditing && p is DesktopCompanyContact link && RelatedContacts.Contains(link));
    }

    private async System.Threading.Tasks.Task LoadRelatedAsync(DesktopCompany company, long generation, CancellationToken ct)
    {
        RelatedContacts = [];
        var visible = new List<DesktopCompanyContact>();
        foreach (var batch in company.Contacts.Chunk(4))
        {
            var results = await System.Threading.Tasks.Task.WhenAll(batch.Select(async link =>
                (Link: link, Result: await _client.GetContactAsync(link.ContactId, ct))));
            if (!Current(generation, ct)) return;
            foreach (var (link, result) in results)
            {
                if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" } contact)
                    visible.Add(link with { DisplayName = contact.Value.Contact.DisplayName });
                else if (result is DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure)
                { InvalidateScope(); Message = "Сессия завершена. Выполните вход снова."; return; }
                else if (result is not (DesktopWorkResult<DesktopContactDetails>.Forbidden or DesktopWorkResult<DesktopContactDetails>.NotFound))
                    Message = "Некоторые контактные лица не загружены. Обновите карточку.";
            }
        }
        RelatedContacts = visible;
    }

    private async System.Threading.Tasks.Task SearchContactsAsync(bool append, CancellationToken ct)
    {
        if (ContactQuery.Trim().Length is < 1 or > 200) { Message = "Введите имя контакта (1–200 символов)."; return; }
        CancelPicker();
        if (!append) { ContactCandidates = []; SelectedCandidate = null; _contactCursor = null; _contactCursors.Clear(); }
        var generation = _pickerGeneration; var selection = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _pickerRequest = request; IsSearchingContacts = true;
        try
        {
            var result = await _client.FindCompanyContactsAsync(ContactQuery.Trim(), append ? _contactCursor : null, request.Token);
            if (!Current(selection, request.Token) || generation != _pickerGeneration || !CanChangeLinks) return;
            if (result is DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded page)
            {
                ContactCandidates = (append ? ContactCandidates.Concat(page.Value) : page.Value).Where(c => c.LifecycleState == "active").DistinctBy(c => c.Id).ToArray();
                _contactCursor = page.NextCursor;
                if (_contactCursor is not null && !_contactCursors.Add(_contactCursor)) _contactCursor = null;
                Message = ContactCandidates.Count == 0 ? "Доступных контактов по запросу нет." : null;
            }
            else { ClearPicker(); Message = ListFailure(result); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally { if (generation == _pickerGeneration) { _pickerRequest = null; IsSearchingContacts = false; } }
    }

    private async System.Threading.Tasks.Task ChangeLinkAsync(DesktopCompanyContact? remove, CancellationToken ct)
    {
        if (Details is not { } company) return;
        static string? Optional(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var link = remove ?? new DesktopCompanyContact(SelectedCandidate!.Id, JobTitle, DepartmentName, IsPrimary, Optional(ValidFrom), Optional(ValidTo));
        if (DesktopWorkApiClient.ValidateCompanyContact(link) is { } error) { Message = error; return; }
        if (remove is null && company.Contacts.Any(c => c.ContactId == link.ContactId)) { Message = "Этот контакт уже связан с компанией."; return; }
        var generation = _selectionGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct); _detailRequest = request; IsBusy = true; Message = null;
        try
        {
            if (remove is null)
            {
                // A search result is not an authorization grant. Recheck immediately before linking.
                var contact = await _client.GetContactAsync(link.ContactId, request.Token);
                if (!Current(generation, request.Token) || !_canUpdate()) return;
                if (contact is not DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" })
                { ClearPicker(); Message = "Контакт больше не доступен. Повторите поиск."; return; }
            }
            var result = remove is null
                ? await _client.LinkCompanyContactAsync(company.Id, company.Version, link, Guid.NewGuid().ToString("D"), request.Token)
                : await _client.UnlinkCompanyContactAsync(company.Id, company.Version, link.ContactId, request.Token);
            if (!Current(generation, request.Token) || !_canUpdate()) return;
            if (result is DesktopWorkResult<DesktopCompany>.Succeeded saved)
            {
                Details = saved.Value; FillDraft(Details); ClearPicker(); JobTitle = DepartmentName = ValidFrom = ValidTo = ""; IsPrimary = false;
                Items = Items.Select(c => c.Id == saved.Value.Id ? saved.Value : c).ToArray();
                await LoadRelatedAsync(saved.Value, generation, request.Token);
                if (Current(generation, request.Token)) Message = remove is null ? "Связь добавлена." : "Связь удалена. Контакт сохранён.";
            }
            else ShowFailure(result);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        { if (generation == _selectionGeneration) { _blocked = true; Message = "Запрос отменён. Изменение связи не подтверждено. Обновите карточку перед повтором."; } }
        catch (Exception)
        { if (generation == _selectionGeneration) { _blocked = true; Message = "Изменение связи не подтверждено. Обновите карточку перед повтором."; } }
        finally { if (generation == _selectionGeneration) _detailRequest = null; IsBusy = false; }
    }

    private void CancelPicker() { ++_pickerGeneration; _pickerRequest?.Cancel(); _pickerRequest = null; IsSearchingContacts = false; }
    private void ClearPicker()
    { CancelPicker(); ContactCandidates = []; SelectedCandidate = null; _contactCursor = null; _contactCursors.Clear(); Notify(); }
    private void ClearLinkDraft()
    { ClearPicker(); JobTitle = DepartmentName = ValidFrom = ValidTo = ""; IsPrimary = false; }
}
