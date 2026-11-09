using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed record PersonalContactRow(DesktopContact Source)
{
    // Personal contacts have no separate channel fields: project the first usable
    // phone/email from the existing notes, preserving the original text verbatim.
    private static readonly Regex PhonePattern = new(@"(?<![\p{L}\p{N}])\+?\d[\d ()\u00A0-]*\d(?![\p{L}\p{N}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex EmailPattern = new(@"\b[A-Za-z0-9][A-Za-z0-9._%+-]*@[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public string Phone { get; } = FindPhone(Source.Notes ?? "");
    public string Email { get; } = EmailPattern.Match(Source.Notes ?? "").Value;
    public bool HasPhone => Phone.Length > 0;
    public bool HasEmail => Email.Length > 0;
    private static string FindPhone(string notes) => PhonePattern.Matches(notes).Cast<Match>()
        .FirstOrDefault(match => match.Value.Count(char.IsDigit) is >= 10 and <= 15)?.Value.Trim() ?? "";
    public Guid Id => Source.Id;
    public string Name => Source.DisplayName;
    public string Identity => string.Join(" ", new[] { Source.FirstName, Source.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public string Notes => string.IsNullOrWhiteSpace(Source.Notes) ? "Заметок пока нет" : Source.Notes;
    public string Preview => string.IsNullOrWhiteSpace(Source.Notes) ? Identity : Source.Notes.ReplaceLineEndings(" · ");
    public string LastName => string.IsNullOrWhiteSpace(Source.LastName) ? "Не указана" : Source.LastName;
}

/// <summary>Personal contact browser; mutations stay in the existing workspace and hub.</summary>
public sealed partial class PersonalWorkspaceViewModel
{
    private static readonly CultureInfo ContactCulture = CultureInfo.GetCultureInfo("ru-RU");
    private IReadOnlyList<PersonalContactRow> _contactRows = [], _visibleContacts = [];
    private PersonalContactRow? _contactSelection;
    private string _contactQuery = "", _contactFilter = "Все контакты", _contactSort = "Имя: А–Я";
    private bool _replacingContacts, _contactLoadFailed, _contactCreating;
    public bool CanSubmitContact => Hub.CreateContactCommand.CanExecute(null);
    public bool IsContacts => Area.Area == WorkHubArea.Contacts;
    public IReadOnlyList<PersonalContactRow> VisibleContacts => _visibleContacts;
    public IReadOnlyList<string> ContactFilters { get; } = ["Все контакты", "С заметками", "Без заметок", "Без фамилии"];
    public IReadOnlyList<string> ContactSortOrders { get; } = ["Имя: А–Я", "Имя: Я–А"];
    public string ContactQuery { get => _contactQuery; set { if (SetProperty(ref _contactQuery, value)) FilterContacts(); } }
    public string ContactFilter { get => _contactFilter; set { if (value is not null && SetProperty(ref _contactFilter, value)) FilterContacts(); } }
    public string ContactSort { get => _contactSort; set { if (value is not null && SetProperty(ref _contactSort, value)) FilterContacts(); } }
    public string ContactCountText => $"Найдено: {VisibleContacts.Count} · Всего: {_contactRows.Count}";
    public bool HasContactFilters => ContactQuery.Length > 0 || ContactFilter != "Все контакты" || ContactSort != "Имя: А–Я";
    public bool ContactLoadFailed => _contactLoadFailed && !Hub.IsLoading;
    public bool ShowContactsEmpty => !Hub.IsLoading && !ContactLoadFailed && VisibleContacts.Count == 0;
    public string ContactsEmptyTitle => _contactRows.Count == 0 ? "Контактов пока нет" : "Ничего не найдено";
    public string ContactsEmptyHint => _contactRows.Count == 0 ? "Добавьте первый контакт. Он будет доступен для поиска и связей с записями." : "Попробуйте другое имя или сбросьте поиск и фильтр.";
    public bool HasContactSelection => ContactSelection is not null;
    public bool ShowContactDetails => !IsContactCreating && HasContactSelection;
    public bool ShowContactSelectionHint => !IsContactCreating && !HasContactSelection;
    public bool IsContactSelectionHidden => ContactSelection is { } selected && VisibleContacts.All(r => r.Id != selected.Id);
    public bool IsContactCreating { get => _contactCreating; set { if (SetProperty(ref _contactCreating, value)) NotifyContactDetails(); } }
    public PersonalContactRow? ContactSelection
    {
        get => _contactSelection;
        set
        {
            // A filtered ItemsSource clears SelectedItem. Keep the inspector and creation draft.
            if (_replacingContacts || value is null || !SetProperty(ref _contactSelection, value)) return;
            Hub.SelectedContact = value.Source;
            SyncContactObject();
            NotifyContactDetails();
        }
    }

    private void InitializeContactsBrowser()
    {
        Hub.PropertyChanged += OnContactsHubChanged;
        Hub.CreateContactCommand.CanExecuteChanged += OnContactCreateAvailabilityChanged;
        Hub.RefreshCommand.ExecutionFailed += OnContactRefreshFailed;
        RebuildContactRows();
    }
    private void OnContactsHubChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hub.Contacts)) { _contactLoadFailed = false; RebuildContactRows(); }
        if (e.PropertyName == nameof(Hub.SelectedContact)) SyncContactSelection();
        if (e.PropertyName == nameof(Hub.IsLoading))
        {
            if (IsContacts && !Hub.IsLoading) _contactLoadFailed = Hub.FeedbackKind is WorkHubFeedbackKind.Warning or WorkHubFeedbackKind.Error;
            OnPropertyChanged(nameof(ContactLoadFailed)); OnPropertyChanged(nameof(ShowContactsEmpty));
        }
    }
    private void RebuildContactRows()
    {
        _contactRows = Hub.Contacts.Select(c => new PersonalContactRow(c)).ToArray();
        if (_contactSelection is { } previous) _contactSelection = _contactRows.FirstOrDefault(c => c.Id == previous.Id);
        FilterContacts();
        if (IsContacts) { Refresh(); SyncContactObject(); }
        NotifyContactDetails();
    }
    private void SyncContactSelection()
    {
        _contactSelection = _contactRows.FirstOrDefault(c => c.Id == Hub.SelectedContact?.Id);
        OnPropertyChanged(nameof(ContactSelection));
        if (IsContacts) SyncContactObject();
        NotifyContactDetails();
    }
    private void SyncContactObject() => Selected = Objects.FirstOrDefault(o => o.ObjectType == "contact" && o.Id == ContactSelection?.Id);
    public void ActivateContactsBrowser() { Refresh(); SyncContactSelection(); }
    private void FilterContacts()
    {
        var words = ContactQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = _contactRows.Where(row => words.All(word => ContactCulture.CompareInfo.IndexOf(
            $"{row.Name}\n{row.Source.FirstName}\n{row.Source.LastName}\n{row.Source.Notes}", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0));
        rows = ContactFilter switch
        {
            "С заметками" => rows.Where(r => !string.IsNullOrWhiteSpace(r.Source.Notes)),
            "Без заметок" => rows.Where(r => string.IsNullOrWhiteSpace(r.Source.Notes)),
            "Без фамилии" => rows.Where(r => string.IsNullOrWhiteSpace(r.Source.LastName)),
            _ => rows
        };
        var comparer = StringComparer.Create(ContactCulture, true);
        _visibleContacts = (ContactSort == "Имя: Я–А" ? rows.OrderByDescending(r => r.Name, comparer) : rows.OrderBy(r => r.Name, comparer)).ThenBy(r => r.Id).ToArray();
        _replacingContacts = true;
        try { OnPropertyChanged(nameof(VisibleContacts)); OnPropertyChanged(nameof(ContactSelection)); }
        finally { _replacingContacts = false; }
        foreach (var name in new[] { nameof(ContactCountText), nameof(HasContactFilters), nameof(ShowContactsEmpty), nameof(ContactsEmptyTitle), nameof(ContactsEmptyHint), nameof(IsContactSelectionHidden) }) OnPropertyChanged(name);
    }
    private void NotifyContactDetails()
    {
        foreach (var name in new[] { nameof(ContactSelection), nameof(HasContactSelection), nameof(ShowContactDetails), nameof(ShowContactSelectionHint), nameof(IsContactSelectionHidden) }) OnPropertyChanged(name);
    }
    public void ResetContactFilters()
    {
        _contactQuery = ""; _contactFilter = "Все контакты"; _contactSort = "Имя: А–Я";
        foreach (var name in new[] { nameof(ContactQuery), nameof(ContactFilter), nameof(ContactSort) }) OnPropertyChanged(name);
        FilterContacts();
    }
    private void OnContactRefreshFailed(Exception error)
    {
        if (!IsContacts) return;
        _contactLoadFailed = true;
        OnPropertyChanged(nameof(ContactLoadFailed)); OnPropertyChanged(nameof(ShowContactsEmpty));
    }
    private void OnContactCreateAvailabilityChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CanSubmitContact));
    private void DisposeContactsBrowser() { Hub.PropertyChanged -= OnContactsHubChanged; Hub.RefreshCommand.ExecutionFailed -= OnContactRefreshFailed; Hub.CreateContactCommand.CanExecuteChanged -= OnContactCreateAvailabilityChanged; }
}
