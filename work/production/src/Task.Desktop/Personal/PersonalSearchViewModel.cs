using System.ComponentModel;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Personal;

/// <summary>Personal page presentation over the existing shared search engine.</summary>
public sealed class PersonalSearchViewModel : ViewModelBase, IDisposable
{
    private readonly PersonalTaskStore _store;
    private IReadOnlyList<PersonalSearchRow> _rows = [], _visible = [];
    private PersonalSearchRow? _selected;
    private bool _submitted, _replacing;
    private string? _openMessage;
    public PersonalSearchViewModel(WorkHubViewModel hub, PersonalTaskStore store)
    {
        Hub = hub; _store = store;
        SubmitCommand = new(async (_, ct) =>
        {
            _submitted = true; _openMessage = null; NotifyState();
            await Hub.SearchCommand.ExecuteAsync(cancellationToken: ct);
            NotifyState();
        }, _ => Hub.SearchCommand.CanExecute(null));
        ClearCommand = new((_, _) => { Query = ""; return System.Threading.Tasks.Task.CompletedTask; }, _ => Query.Length > 0);
        ResetCommand = new((_, _) => { Hub.SearchTypeFilter = "Все"; return System.Threading.Tasks.Task.CompletedTask; }, _ => Hub.SearchTypeFilter != "Все");
        OpenCommand = new(async (_, ct) =>
        {
            if (Selected is not { } row) return;
            try
            {
                if (!_store.WorkspaceObjects().Any(o => o.Id == row.Source.ObjectId && o.Lifecycle == "active"))
                { _openMessage = "Запись удалена, перемещена в архив или больше недоступна. Повторите поиск."; NotifyState(); return; }
                await Hub.OpenSearchResultCommand.ExecuteAsync(row.Source, ct);
            }
            catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException)
            { _openMessage = "Не удалось открыть запись. Проверьте доступ к личной базе и повторите поиск."; NotifyState(); }
        }, _ => Selected is { } row && Hub.CanOpenSearchResult(row.Source) && !IsLoading);
        Hub.PropertyChanged += OnHubChanged;
        Hub.SearchCommand.CanExecuteChanged += OnCommandChanged;
        Hub.SearchCommand.ExecutionFailed += OnSearchFailed;
    }
    public WorkHubViewModel Hub { get; }
    public AsyncCommand SubmitCommand { get; }
    public AsyncCommand ClearCommand { get; }
    public AsyncCommand ResetCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public string Query { get => Hub.SearchQuery; set => Hub.SearchQuery = value; }
    public IReadOnlyList<PersonalSearchRow> Results => _visible;
    public PersonalSearchRow? Selected { get => _selected; set { if ((!_replacing || value is not null) && SetProperty(ref _selected, value)) { _openMessage = null; NotifyState(); } } }
    public bool HasSelection => Selected is not null;
    public bool ShowSelectionHint => !HasSelection;
    public bool IsLoading => Hub.IsSearchLoading;
    public bool HasError => !IsLoading && (_openMessage is not null || _submitted && Hub.FeedbackKind is WorkHubFeedbackKind.Error or WorkHubFeedbackKind.Warning);
    public string? ErrorText => _openMessage ?? Hub.SearchMessage ?? Hub.Message;
    public bool ShowPrompt => !IsLoading && !HasError && !_submitted;
    public bool ShowEmpty => !IsLoading && !HasError && _submitted && Results.Count == 0;
    public string PromptTitle => Query.Trim().Length is < 2 or > 200 ? "Что хотите найти?" : "Запрос готов к поиску";
    public string PromptText => "Введите от 2 до 200 символов и нажмите Enter или «Найти». Поиск проверяет названия, описания, имена, заметки и пути каталога.";
    public string Summary => IsLoading ? "Ищем в личной базе…" : !_submitted ? "Только активные записи · архив и корзина не входят в поиск" : $"Найдено: {Results.Count} из {_rows.Count} · {Hub.SearchTypeFilter}";
    public string Constraint => Hub.SearchTypeFilter == "Все" ? "Все типы записей" : $"Тип: {Hub.SearchTypeFilter}";
    private void OnSearchFailed(Exception _) { _openMessage = "Не удалось выполнить поиск. Повторите запрос."; NotifyState(); }
    private void OnCommandChanged(object? sender, EventArgs e) => SubmitCommand.RaiseCanExecuteChanged();
    private void OnHubChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Hub.SearchQuery))
        {
            _submitted = false; _openMessage = null;
            OnPropertyChanged(nameof(Query)); ClearCommand.RaiseCanExecuteChanged();
        }
        if (e.PropertyName == nameof(Hub.IsSearchLoading) && Hub.IsSearchLoading && Hub.IsSearch) _submitted = true;
        if (e.PropertyName == nameof(Hub.SearchResults)) Rebuild();
        if (e.PropertyName == nameof(Hub.SearchTypeFilter)) Filter();
        if (e.PropertyName is nameof(Hub.IsSearchLoading) or nameof(Hub.SearchMessage) or nameof(Hub.FeedbackKind) or nameof(Hub.SearchQuery)) NotifyState();
    }
    private void Rebuild()
    {
        if (Hub.SearchResults.Count == 0) _rows = [];
        else
        {
            var context = _store.SearchContexts();
            _rows = Hub.SearchResults.Select(hit =>
            {
                context.TryGetValue(hit.ObjectId, out var detail);
                var updated = hit.UpdatedAt.Year > 1970 ? $" · Обновлено {hit.UpdatedAt.LocalDateTime:dd.MM.yyyy}" : "";
                return new PersonalSearchRow(hit, detail.Context ?? "", detail.Detail + updated,
                    hit.ObjectType == "catalog_item" ? "Каталог" : hit.TypeLabel, Query);
            }).ToArray();
        }
        Filter();
    }
    private void Filter()
    {
        _visible = _rows.Where(row => Hub.SearchTypeFilter == "Все" || (row.Source.GroupLabel == "CRM" ? "Контакты" : row.Source.GroupLabel) == Hub.SearchTypeFilter).ToArray();
        var id = Selected?.Source.ObjectId;
        _selected = _visible.FirstOrDefault(row => row.Source.ObjectId == id);
        _replacing = true;
        try { OnPropertyChanged(nameof(Results)); OnPropertyChanged(nameof(Selected)); }
        finally { _replacing = false; }
        ResetCommand.RaiseCanExecuteChanged(); NotifyState();
    }
    private void NotifyState()
    {
        foreach (var name in new[] { nameof(IsLoading), nameof(HasError), nameof(ErrorText), nameof(ShowPrompt), nameof(ShowEmpty), nameof(PromptTitle), nameof(Summary), nameof(Constraint), nameof(HasSelection), nameof(ShowSelectionHint) }) OnPropertyChanged(name);
        OpenCommand.RaiseCanExecuteChanged();
    }
    public void Dispose()
    {
        Hub.PropertyChanged -= OnHubChanged; Hub.SearchCommand.CanExecuteChanged -= OnCommandChanged;
        Hub.SearchCommand.ExecutionFailed -= OnSearchFailed;
        foreach (var command in new[] { SubmitCommand, ClearCommand, ResetCommand, OpenCommand }) command.Dispose();
    }
}
