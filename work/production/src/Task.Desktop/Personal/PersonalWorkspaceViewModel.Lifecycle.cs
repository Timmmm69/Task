using System.Globalization;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Personal;

public sealed record PersonalLifecycleRow(DesktopLifecycleItem Source)
{
    public Guid Id => Source.ObjectId;
    public string Title => Source.Title;
    public string Type => Source.ObjectType switch { "task" => "Задача", "project" => "Проект", "contact" => "Контакт", _ => "Каталог" };
    public DateTimeOffset? Date => Source.LifecycleState == "archived" ? Source.ArchivedAt : Source.DeletedAt;
    public string DateText => Date?.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU")) ?? "Дата не указана";
    public string UpdatedText => Source.UpdatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
}

public sealed partial class PersonalWorkspaceViewModel
{
    private IReadOnlyList<PersonalLifecycleRow> _lifecycleRows = [], _visibleLifecycleRows = [];
    private PersonalLifecycleRow? _lifecycleSelection;
    private string _lifecycleQuery = "", _lifecycleType = "Все типы", _lifecycleSort = "Сначала новые";
    private string? _lifecycleError;
    private bool _replacingLifecycleRows;
    public bool IsLifecycle => Area.Area is WorkHubArea.Archive or WorkHubArea.Trash;
    public bool IsTrash => Area.Area == WorkHubArea.Trash;
    public bool IsSettings => Area.Area == WorkHubArea.Settings;
    public string LifecycleDescription => IsTrash ? "Восстановите нужное. Удаляйте навсегда только то, что больше не понадобится." : "Завершённые дела и сохранённые материалы — под рукой, когда понадобятся.";
    public string LifecycleDateLabel => IsTrash ? "Удалено" : "В архиве с";
    public IReadOnlyList<string> LifecycleTypes { get; } = ["Все типы", "Задача", "Проект", "Контакт", "Каталог"];
    public IReadOnlyList<string> LifecycleSortOrders { get; } = ["Сначала новые", "Сначала старые", "Название: А–Я", "Название: Я–А"];
    public IReadOnlyList<PersonalLifecycleRow> VisibleLifecycleRows => _visibleLifecycleRows;
    public string LifecycleQuery { get => _lifecycleQuery; set { if (SetProperty(ref _lifecycleQuery, value)) FilterLifecycle(); } }
    public string LifecycleType { get => _lifecycleType; set { if (value is not null && SetProperty(ref _lifecycleType, value)) FilterLifecycle(); } }
    public string LifecycleSort { get => _lifecycleSort; set { if (value is not null && SetProperty(ref _lifecycleSort, value)) FilterLifecycle(); } }
    public PersonalLifecycleRow? LifecycleSelection
    {
        get => _lifecycleSelection;
        set
        {
            if (_replacingLifecycleRows && value is null) return;
            if (SetProperty(ref _lifecycleSelection, value)) { Selected = Objects.FirstOrDefault(o => o.Id == value?.Id); OnPropertyChanged(nameof(HasLifecycleSelection)); }
        }
    }
    public bool HasLifecycleSelection => LifecycleSelection is not null;
    public string LifecycleCountText => $"Найдено: {VisibleLifecycleRows.Count} · Всего: {_lifecycleRows.Count}";
    public bool HasLifecycleFilters => LifecycleQuery.Length > 0 || LifecycleType != "Все типы" || LifecycleSort != "Сначала новые";
    public bool ShowLifecycleEmpty => _lifecycleRows.Count == 0 && _lifecycleError is null;
    public bool ShowLifecycleNoResults => _lifecycleRows.Count > 0 && VisibleLifecycleRows.Count == 0 && _lifecycleError is null;
    public string LifecycleEmptyTitle => IsTrash ? "Корзина пуста" : "Архив пока пуст";
    public string LifecycleEmptyText => IsTrash ? "Здесь появятся записи, перемещённые в корзину." : "Здесь появятся записи, которые вы отправите в архив.";
    public string? LifecycleError => _lifecycleError;
    public void ResetLifecycleFilters()
    {
        LifecycleQuery = ""; LifecycleType = "Все типы"; LifecycleSort = "Сначала новые";
    }
    public void RefreshLifecycleBrowser()
    {
        if (!IsLifecycle) return;
        try
        {
            _lifecycleRows = _store.WorkspaceLifecycle(IsTrash ? "trashed" : "archived").Select(o => new PersonalLifecycleRow(o)).ToArray();
            _lifecycleError = null;
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
        {
            _lifecycleRows = []; _lifecycleError = "Не удалось загрузить записи. Проверьте доступ к личной базе и обновите список.";
        }
        FilterLifecycle();
        foreach (var name in new[] { nameof(IsLifecycle), nameof(IsTrash), nameof(IsSettings), nameof(LifecycleDescription), nameof(LifecycleDateLabel), nameof(LifecycleEmptyTitle), nameof(LifecycleEmptyText), nameof(LifecycleError) }) OnPropertyChanged(name);
    }
    private void FilterLifecycle()
    {
        var selectedId = LifecycleSelection?.Id;
        var query = LifecycleQuery.Trim();
        var rows = _lifecycleRows.Where(r => (LifecycleType == "Все типы" || r.Type == LifecycleType) &&
            (query.Length == 0 || CultureInfo.GetCultureInfo("ru-RU").CompareInfo.IndexOf(r.Title, query, CompareOptions.IgnoreCase) >= 0));
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true);
        // Missing lifecycle dates remain at the end in either direction; no substitute date is invented.
        _visibleLifecycleRows = (LifecycleSort switch
        {
            "Название: А–Я" => rows.OrderBy(r => r.Title, comparer),
            "Название: Я–А" => rows.OrderByDescending(r => r.Title, comparer),
            "Сначала старые" => rows.OrderBy(r => r.Date is null).ThenBy(r => r.Date),
            _ => rows.OrderBy(r => r.Date is null).ThenByDescending(r => r.Date)
        }).ThenBy(r => r.Id).ToArray();
        _replacingLifecycleRows = true;
        try { OnPropertyChanged(nameof(VisibleLifecycleRows)); }
        finally { _replacingLifecycleRows = false; }
        LifecycleSelection = _visibleLifecycleRows.FirstOrDefault(r => r.Id == selectedId);
        OnPropertyChanged(nameof(LifecycleSelection));
        // Refresh object versions even when the same row remains selected.
        Selected = Objects.FirstOrDefault(o => o.Id == LifecycleSelection?.Id);
        foreach (var name in new[] { nameof(LifecycleCountText), nameof(HasLifecycleFilters), nameof(ShowLifecycleEmpty), nameof(ShowLifecycleNoResults) }) OnPropertyChanged(name);
    }
}
