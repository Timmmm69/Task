using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record SearchPageNotice(string Title, string Body);

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
    private bool _hasCompletedSearch;
    public bool IsSearchLoading
    {
        get => _isSearchLoading;
        private set
        {
            if (SetProperty(ref _isSearchLoading, value)) OnPropertyChanged(nameof(SearchPageNotice));
        }
    }

    public string? SearchMessage
    {
        get => _searchMessage;
        private set
        {
            if (SetProperty(ref _searchMessage, value)) OnPropertyChanged(nameof(SearchPageNotice));
        }
    }

    public bool CanUseSearchPage => _sessionAvailable && CanSearch;

    public SearchPageNotice? SearchPageNotice
    {
        get
        {
            if (!_sessionAvailable) return new("Сессия завершена", "Войдите в Task снова, чтобы искать по данным компании.");
            if (!CanSearch) return new("Поиск недоступен для вашей роли", "Если для работы нужен поиск, обратитесь к администратору.");
            if (IsOffline) return new("Сервер недоступен", HasSearchResults
                ? "Показаны подтверждённые результаты. После восстановления связи повторите запрос, чтобы открыть объект."
                : "Проверьте подключение к сети компании и повторите запрос.");
            if (IsSearchLoading) return new("Ищем совпадения…", "");
            if (SearchMessage is { } message) return new("Не удалось завершить поиск", message);
            if (HasSearchResults) return null;
            if (SearchQuery.Trim().Length is < 2 or > 200)
                return new("Введите поисковый запрос", "Введите от 2 до 200 символов: например, название проекта или имя сотрудника.");
            if (!_hasCompletedSearch) return new("Запрос готов", "Нажмите «Найти» или Enter.");
            if (HasMoreResults)
                return new("На загруженных страницах совпадений нет", "Загрузите ещё результаты или измените тип.");
            return SearchResults.Count > 0
                ? new("Нет результатов этого типа", "Выберите «Все типы» или другой тип результата.")
                : new("Ничего не найдено", "Попробуйте более короткий запрос или другое написание.");
        }
    }

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
            CatalogSearchQuery = string.Empty;
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
        else if (type == "interaction" && _client is IDesktopInteractionsClient interactions)
        {
            if (!Has("Contact.Read")) return;
            var result = await interactions.GetInteractionAsync(id, token);
            if (!Current()) return;
            if (result is not DesktopWorkResult<DesktopInteraction>.Succeeded found)
            { SetFeedback("Взаимодействие больше не доступно.", WorkHubFeedbackKind.Warning); return; }
            var target = await interactions.GetInteractionCounterpartyAsync(found.Value.CounterpartyObjectId, "contact", token);
            if (!Current()) return;
            if (target is DesktopWorkResult<InteractionCounterparty>.NotFound or DesktopWorkResult<InteractionCounterparty>.Forbidden)
                target = await interactions.GetInteractionCounterpartyAsync(found.Value.CounterpartyObjectId, "company", token);
            if (!Current()) return;
            if (target is not DesktopWorkResult<InteractionCounterparty>.Succeeded visible)
            { SetFeedback("Контрагент больше не доступен.", WorkHubFeedbackKind.Warning); return; }
            openGeneration = _searchObjectGeneration + 1;
            await OpenSearchObjectAsync(visible.Value.Type, visible.Value.Id);
            if (!Current()) return;
            var card = visible.Value.Type == "contact" ? ContactInteractions : CompanyInteractions;
            if (card?.Counterparty?.Id == visible.Value.Id) await card.OpenAsync(id, token);
        }
        else if (type == "company")
        {
            if (!Has("Contact.Read") || Companies is null) { SetFeedback("Недостаточно прав для открытия компании.", WorkHubFeedbackKind.Warning); return; }
            SetCompanySurface(true);
            await Companies.OpenAsync(id);
        }
        else if (type == "contact")
        {
            SetCompanySurface(false);
            SelectedContact = null;
            if (!Has("Contact.Read")) { SetFeedback("Недостаточно прав для открытия контакта.", WorkHubFeedbackKind.Warning); return; }
            if (IsPersonal)
            {
                var local = await _client.GetContactsAsync(token);
                if (!Current()) return;
                Apply(local, value => Contacts = value);
                SelectedContact = Contacts.FirstOrDefault(c => c.Id == id && c.LifecycleState == "active");
                if (SelectedContact is null) SetFeedback("Контакт больше не доступен. Обновите результаты поиска.", WorkHubFeedbackKind.Warning);
                return;
            }
            if (_client is IDesktopContactDetailsClient detailsClient)
            {
                if (ContactResultsQuery.Length > 0)
                {
                    ContactQuery = "";
                    await LoadListPageAsync(false, token, "");
                    if (!Current()) return;
                    // The deep link checks access separately if the directory could not be refreshed.
                    if (ContactResultsQuery.Length > 0)
                    {
                        ContactResultsQuery = "";
                        Contacts = [];
                    }
                }
                var result = await detailsClient.GetContactAsync(id, token);
                if (!Current() || !Has("Contact.Read")) return;
                if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" } found)
                {
                    Contacts = Contacts.Where(c => c.Id != id).Append(found.Value.Contact).ToArray();
                    SelectedContact = found.Value.Contact;
                    await ContactSelectionTask;
                    if (Current()) SetFeedback(null, WorkHubFeedbackKind.None);
                }
                else
                {
                    if (result is DesktopWorkResult<DesktopContactDetails>.Forbidden or DesktopWorkResult<DesktopContactDetails>.NotFound or DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure)
                        Contacts = Contacts.Where(c => c.Id != id).ToArray();
                    SetFeedback(result is DesktopWorkResult<DesktopContactDetails>.Forbidden ? "Недостаточно прав для открытия контакта."
                        : "Контакт больше не доступен. Обновите результаты поиска.", WorkHubFeedbackKind.Warning);
                }
            }

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
        _hasCompletedSearch = false;
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
            var keepConfirmedRows = IsSearch && !IsPersonal && !_paletteSearchActive
                && result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.ServerUnavailable;
            if ((!append && !keepConfirmedRows)
                || result is DesktopWorkResult<IReadOnlyList<DesktopSearchResult>>.Forbidden
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
                _hasCompletedSearch = true;
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
        catch
        {
            if (!request.IsCancellationRequested && generation == _searchGeneration)
                SearchMessage = "Повторите запрос.";
            throw;
        }
        finally
        {
            if (ReferenceEquals(_searchRequest, request)) _searchRequest = null;
            if (generation == _searchGeneration) IsSearchLoading = false;
            NotifyPagination();
        }
    }
}
