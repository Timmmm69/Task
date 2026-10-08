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
    public bool IsSearchLoading { get => _isSearchLoading; private set => SetProperty(ref _isSearchLoading, value); }
    public string? SearchMessage { get => _searchMessage; private set => SetProperty(ref _searchMessage, value); }
    public bool HasCapability(string capability) => _sessionAvailable && Has(capability);
    public bool CanOpenSearchResult(DesktopSearchResult result) => !_disposed && _sessionAvailable && _networkAvailable
        && CanSearch && SearchResults.Contains(result)
        && result.ObjectType is "task" or "project" or "calendar_event" or "catalog_item" or "file_location" or "contact" or "company";

    public async System.Threading.Tasks.Task OpenSearchObjectAsync(string type, Guid id)
    {
        if (!_active || !_sessionAvailable || !_networkAvailable || _disposed) return;
        var generation = _activationGeneration;
        var token = _activation?.Token ?? CancellationToken.None;
        if (type is "catalog_item" or "file_location")
        {
            SelectedCatalogItem = null;
            if (await ReloadCatalogAsync(token) && !token.IsCancellationRequested && generation == _activationGeneration)
                await SelectCatalogItemAsync(id, token);
        }
        else if (type == "contact")
        {
            SelectedContact = null;
            var result = await _client.GetContactsAsync(token);
            if (token.IsCancellationRequested || _disposed || generation != _activationGeneration) return;
            Apply(result, value => { Contacts = value; SelectedContact = value.FirstOrDefault(c => c.Id == id); });
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
        _searchDebounce?.Cancel();
        _searchRequest?.Cancel();
        SearchCommand.Cancel();
        IsSearchLoading = false;
        SearchResults = [];
        SelectedSearchHit = null;
        SearchMessage = null;
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

    private async System.Threading.Tasks.Task SearchCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_sessionAvailable || !_networkAvailable || !CanSearch) return;
        var query = SearchQuery.Trim();
        if (query.Length is < 2 or > 200) return;
        _searchRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _searchRequest = request;
        var generation = ++_searchGeneration;
        IsSearchLoading = true;
        SearchMessage = null;
        try
        {
            var result = await _client.SearchAsync(query, request.Token);
            if (request.IsCancellationRequested || _disposed || generation != _searchGeneration) return;
            // Never keep a previous authorized subset after an unsuccessful recheck.
            SearchResults = [];
            Apply(result, value => SearchResults = value);
            if (result is Work.DesktopWorkResult<IReadOnlyList<Work.DesktopSearchResult>>.AuthenticationFailure)
                UpdateSessionState(false);
            SearchMessage = Message;
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request)) _searchRequest = null;
            if (generation == _searchGeneration) IsSearchLoading = false;
        }
    }
}
