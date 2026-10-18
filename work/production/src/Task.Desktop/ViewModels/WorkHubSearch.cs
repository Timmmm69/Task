using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

// Both the search page and the palette use the same request, DTO and result store.
public sealed partial class WorkHubViewModel
{
    private CancellationTokenSource? _searchRequest;
    private CancellationTokenSource? _searchDebounce;
    private long _searchGeneration;
    private bool _paletteSearchActive;
    private bool _isSearchLoading;
    private string? _searchMessage;
    private long _searchObjectGeneration;
    public bool IsSearchLoading { get => _isSearchLoading; private set => SetProperty(ref _isSearchLoading, value); }
    public string? SearchMessage { get => _searchMessage; private set => SetProperty(ref _searchMessage, value); }
    public bool HasCapability(string capability) => _sessionAvailable && Has(capability);
    public bool CanOpenSearchResult(DesktopSearchResult result) => !_disposed && _sessionAvailable && _networkAvailable
        && CanSearch && SearchResults.Contains(result);

    public async System.Threading.Tasks.Task OpenSearchObjectAsync(string type, Guid id)
    {
        if (!_active || !_sessionAvailable || !_networkAvailable || _disposed) return;
        var generation = _activationGeneration;
        var openGeneration = ++_searchObjectGeneration;
        RefreshCommand.Cancel();
        ResetListPagination();
        var token = _activation?.Token ?? CancellationToken.None;
        bool Current() => !token.IsCancellationRequested && !_disposed && generation == _activationGeneration
            && openGeneration == _searchObjectGeneration && _sessionAvailable && _networkAvailable;
        if (type is "catalog_item" or "file_location")
        {
            SelectedCatalogItem = null;
            if (!Has("FileCatalog.Read")) { SetFeedback("Недостаточно прав для открытия файла.", WorkHubFeedbackKind.Warning); return; }
            var result = await _client.GetCatalogAsync(token);
            if (!Current() || !Has("FileCatalog.Read")) return;
            Apply(result, value => { ReplaceCatalog(value); _catalogSnapshotCurrent = true; });
            if (result is DesktopWorkResult<IReadOnlyList<DesktopCatalogItem>>.Succeeded)
            {
                if (!Catalog.Any(item => item.Id == id && item.LifecycleState == "active") || !await SelectCatalogItemAsync(id, token))
                {
                    SelectedCatalogItem = null;
                    SetFeedback("Файл больше не доступен. Обновите результаты поиска.", WorkHubFeedbackKind.Warning);
                }
            }
        }
        else if (type == "contact")
        {
            SelectedContact = null;
            if (!Has("Contact.Read")) { SetFeedback("Недостаточно прав для открытия контакта.", WorkHubFeedbackKind.Warning); return; }
            string? cursor = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            var contacts = new List<DesktopContact>();
            for (var pageNumber = 0; pageNumber < 500; pageNumber++)
            {
                var result = await _client.GetContactsAsync(cursor, token);
                if (!Current() || !Has("Contact.Read")) return;
                if (result is not DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded page)
                {
                    if (result is DesktopWorkResult<IReadOnlyList<DesktopContact>>.Forbidden or DesktopWorkResult<IReadOnlyList<DesktopContact>>.AuthenticationFailure)
                        Contacts = [];
                    Apply(result, _ => { }); return;
                }
                contacts.AddRange(page.Value);
                Contacts = contacts.DistinctBy(contact => contact.Id).ToArray();
                SelectedContact = Contacts.FirstOrDefault(contact => contact.Id == id && contact.LifecycleState == "active");
                _listCursor = page.NextCursor;
                if (_listCursor is not null) _listCursors.Add(_listCursor);
                NotifyPagination();
                if (SelectedContact is not null) { await ContactSelectionTask; if (Current()) SetFeedback(null, WorkHubFeedbackKind.None); return; }
                cursor = page.NextCursor;
                if (cursor is null || !cursors.Add(cursor)) break;
            }
            SetFeedback("Контакт больше не доступен. Обновите результаты поиска.", WorkHubFeedbackKind.Warning);
        }
    }

    public void BeginPaletteSearch()
    {
        _paletteSearchActive = true;
        InvalidateSearch();
        SearchQuery = "";
    }

    public void EndPaletteSearch()
    {
        _paletteSearchActive = false;
        InvalidateSearch();
    }

    /// <summary>Call on every access-scope change, even if capability names are unchanged.</summary>
    public void InvalidateSearch()
    {
        ++_searchGeneration;
        _searchCursor = null;
        _searchCursors.Clear();
        LoadMoreCommand.Cancel();
        _searchDebounce?.Cancel();
        _searchRequest?.Cancel();
        SearchCommand.Cancel();
        IsSearchLoading = false;
        SearchResults = [];
        SelectedSearchHit = null;
        SearchMessage = null;
        NotifyPagination();
    }

    private void OnSearchQueryChanged()
    {
        InvalidateSearch();
        if (!_paletteSearchActive) return;
        if (SearchQuery.Trim().Length > 200)
        {
            SearchMessage = "Введите от 2 до 200 символов.";
            return;
        }
        if (SearchQuery.Trim().Length < 2 || !CanSearch || !_sessionAvailable || !_networkAvailable) return;
        IsSearchLoading = true;
        _ = DebounceSearchAsync();
    }

    private async System.Threading.Tasks.Task DebounceSearchAsync()
    {
        using var delay = new CancellationTokenSource();
        _searchDebounce = delay;
        try
        {
            await System.Threading.Tasks.Task.Delay(200, delay.Token);
            await SearchCoreAsync(delay.Token);
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested) { }
        catch (Exception) { if (!delay.IsCancellationRequested && !_disposed) SearchMessage = "Не удалось выполнить поиск. Повторите запрос."; }
        finally
        {
            if (ReferenceEquals(_searchDebounce, delay)) { _searchDebounce = null; IsSearchLoading = false; }
        }
    }

    private async System.Threading.Tasks.Task SearchCoreAsync(CancellationToken cancellationToken, bool append = false)
    {
        if (_disposed || !_sessionAvailable || !_networkAvailable || !CanSearch) return;
        var query = SearchQuery.Trim();
        if (query.Length is < 2 or > 200) return;
        _searchRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _searchRequest = request;
        var generation = ++_searchGeneration;
        if (!append) { _searchCursor = null; _searchCursors.Clear(); }
        IsSearchLoading = true;
        NotifyPagination();
        SearchMessage = null;
        try
        {
            var result = await _client.SearchAsync(query, append ? _searchCursor : null, request.Token);
            if (request.IsCancellationRequested || _disposed || generation != _searchGeneration) return;
            if (!append || result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Forbidden
                or DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.AuthenticationFailure
                or DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Conflict)
            {
                SearchResults = [];
                _searchCursor = null;
            }
            Apply(result, value => SearchResults = (append ? SearchResults.Concat(value) : value)
                .DistinctBy(item => (item.ObjectType, item.ObjectId)).ToArray());
            if (result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Succeeded page)
            {
                _searchCursor = page.NextCursor;
                if (_searchCursor is not null && !_searchCursors.Add(_searchCursor))
                {
                    _searchCursor = null;
                    SetFeedback("Сервер повторил страницу. Выполните поиск заново.", WorkHubFeedbackKind.Warning);
                }
            }
            else if (result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.MalformedResponse) _searchCursor = null;
            if (result is Work.DesktopWorkResult<IReadOnlyList<Work.DesktopSearchResult>>.AuthenticationFailure)
                UpdateSessionState(false);
            SearchMessage = Message;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_searchRequest, request)) _searchRequest = null;
            if (generation == _searchGeneration) IsSearchLoading = false;
            NotifyPagination();
        }
    }
}
