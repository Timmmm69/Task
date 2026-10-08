using System.ComponentModel;
using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record PaletteCommand(string Id, string Title, Action Execute, Func<bool> IsAvailable);
public sealed record PaletteEntry(string Id, string Title, string TypeLabel, string SectionLabel,
    PaletteCommand? Command = null, DesktopSearchResult? Result = null, bool CanOpen = true)
{
    public string AutomationName => $"{TypeLabel}: {Title}";
}

/// <summary>A presentation of existing shell actions and the existing Search result store.</summary>
public sealed class CommandPaletteViewModel : ViewModelBase, IDisposable
{
    private readonly WorkHubViewModel? _search;
    private readonly Func<IEnumerable<PaletteCommand>> _commands;
    private readonly INotifyPropertyChanged[] _sources;
    private IReadOnlyList<PaletteEntry> _items = [];
    private PaletteEntry? _selected;
    private bool _isOpen;
    private bool _disposed;
    private string _query = "";

    public CommandPaletteViewModel(WorkHubViewModel? search, Func<IEnumerable<PaletteCommand>> commands,
        params INotifyPropertyChanged[] sources)
    {
        _search = search;
        _commands = commands;
        _sources = sources.Concat(search is null ? [] : new INotifyPropertyChanged[] { search }).Distinct().ToArray();
        foreach (var source in _sources) source.PropertyChanged += OnSourceChanged;
    }

    public event Action? FocusRequested;
    public event Action? Closed;
    public bool IsOpen { get => _isOpen; private set => SetProperty(ref _isOpen, value); }
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            if (_search is not null) _search.SearchQuery = value;
            Rebuild();
        }
    }
    public IReadOnlyList<PaletteEntry> Items { get => _items; private set => SetProperty(ref _items, value); }
    public PaletteEntry? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(SelectionText)); }
    }
    public bool IsLoading => _search?.IsSearchLoading == true;
    public string SelectionText => Selected is null ? "Нет выбранного действия" : $"{Selected.AutomationName}. {Items.ToList().IndexOf(Selected) + 1} из {Items.Count}";
    public string StatusText => _search?.IsOffline == true
        ? "Сервер недоступен. Доступны локальные переходы; серверный поиск недоступен."
        : !string.IsNullOrEmpty(_search?.SearchMessage) ? _search.SearchMessage!
        : _search?.CanSearch != true ? "Поиск недоступен в текущей области доступа."
        : IsLoading ? "Поиск…"
        : Query.Trim().Length < 2 ? "Выберите команду или введите от 2 до 200 символов для поиска."
        : Items.Count == 0 ? "Ничего не найдено. Измените запрос."
        : $"Доступно: {Items.Count}. ↑↓ выбрать · Enter выполнить · Esc закрыть";

    public void Open()
    {
        if (_disposed) return;
        if (!IsOpen)
        {
            IsOpen = true;
            _search?.BeginPaletteSearch();
            Query = "";
            Rebuild();
        }
        FocusRequested?.Invoke();
    }
    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _search?.EndPaletteSearch();
        Items = [];
        Selected = null;
        Closed?.Invoke();
    }
    public void MoveSelection(int direction)
    {
        if (Items.Count == 0) return;
        var index = Selected is null ? -1 : Items.ToList().IndexOf(Selected);
        Selected = Items[(index + direction + Items.Count) % Items.Count];
    }
    public void ExecuteSelected()
    {
        var entry = Selected;
        if (!IsOpen || entry is null || !entry.CanOpen || !Items.Contains(entry)) return;
        if (entry.Command is { } command)
        {
            if (!command.IsAvailable()) { Rebuild(); return; }
            Close();
            command.Execute();
        }
        else if (entry.Result is { } result && _search?.OpenSearchResultCommand.CanExecute(result) == true)
        {
            // Preserve the existing authorized object-opening flow (including task recheck).
            _search.OpenSearchResultCommand.Execute(result);
            Close();
        }
    }
    private void Rebuild()
    {
        if (!IsOpen || _disposed) return;
        var query = Query.Trim();
        var commands = _commands().Where(c => c.IsAvailable())
            .Where(c => query.Length == 0 || c.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.Title.Equals(query, StringComparison.CurrentCultureIgnoreCase) ? 0
                : c.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 1 : 2)
            .ToArray();
        var results = _search is { CanSearch: true, IsOffline: false } && query.Length >= 2
            ? _search.SearchResults.Select((r, i) => new PaletteEntry($"{r.ObjectType}:{r.ObjectId}", r.Title,
                _search.CanOpenSearchResult(r) ? r.TypeLabel : $"{r.TypeLabel} · переход недоступен в этом клиенте",
                i == 0 ? "Результаты" : "", Result: r, CanOpen: _search.CanOpenSearchResult(r))) : [];
        var selectedId = Selected?.Id;
        var preferred = commands.Where(c => c.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        var other = commands.Except(preferred);
        Items = preferred.Select((c, i) => new PaletteEntry(c.Id, c.Title, "Команда", i == 0 ? "Команды" : "", c))
            .Concat(results).Concat(other.Select((c, i) => new PaletteEntry(c.Id, c.Title, "Команда", i == 0 ? "Команды" : "", c))).ToArray();
        Selected = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsLoading));
    }
    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _search) && e.PropertyName is not (nameof(WorkHubViewModel.SearchResults)
            or nameof(WorkHubViewModel.IsSearchLoading) or nameof(WorkHubViewModel.SearchMessage)
            or nameof(WorkHubViewModel.IsOffline) or nameof(WorkHubViewModel.CanReadCurrentArea))) return;
        Rebuild();
    }
    public void Dispose()
    {
        if (_disposed) return;
        Close();
        _disposed = true;
        foreach (var source in _sources) source.PropertyChanged -= OnSourceChanged;
    }
}
