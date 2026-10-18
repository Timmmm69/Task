using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed partial class WorkHubViewModel
{
    private string? _listCursor;
    private string? _searchCursor;
    private readonly HashSet<string> _listCursors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _searchCursors = new(StringComparer.Ordinal);
    private long _listGeneration;
    public AsyncCommand LoadMoreCommand { get; private set; } = null!;
    public bool HasMoreResults => (_paletteSearchActive || IsSearch) ? _searchCursor is not null
        : (Area != WorkHubArea.Contacts || !ShowCompanies) && Area is WorkHubArea.Contacts or WorkHubArea.Archive or WorkHubArea.Trash && _listCursor is not null;

    private void InitializePagination() => LoadMoreCommand = new(async (_, ct) =>
    {
        if (_paletteSearchActive || IsSearch) await SearchCoreAsync(ct, append: true);
        else await LoadListPageAsync(true, ct);
    }, _ => HasMoreResults && !_disposed && _sessionAvailable && _networkAvailable
        && (_paletteSearchActive ? CanSearch : _active && CanReadCurrentArea) && !RefreshCommand.IsExecuting && !IsLoading && !IsSearchLoading);

    private void NotifyPagination()
    {
        OnPropertyChanged(nameof(HasMoreResults));
        LoadMoreCommand.RaiseCanExecuteChanged();
    }

    private void ResetListPagination()
    {
        ++_listGeneration;
        _listCursor = null;
        _listCursors.Clear();
        LoadMoreCommand.Cancel();
        NotifyPagination();
    }

    private async System.Threading.Tasks.Task LoadListPageAsync(bool append, CancellationToken ct)
    {
        if (!append) ResetListPagination();
        var generation = _listGeneration;
        var activation = _activationGeneration;
        var area = Area;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _activation?.Token ?? CancellationToken.None);
        BeginOperation();
        try
        {
            if (area == WorkHubArea.Contacts)
            {
                var result = await _client.GetContactsAsync(append ? _listCursor : null, linked.Token);
                if (!Current()) return;
                var selectedBeforeRefresh = SelectedContact;
                if (!append && result is DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded page
                    && selectedBeforeRefresh is { } outsidePage && page.Value.All(item => item.Id != outsidePage.Id)
                    && !IsPersonal && _client is IDesktopContactDetailsClient detailsClient)
                {
                    // A missing row may be on a later page or may have lost object access.
                    // Preserve drafts only after an independent, current scope check.
                    var visible = await detailsClient.GetContactAsync(outsidePage.Id, linked.Token);
                    if (!Current() || SelectedContact?.Id != outsidePage.Id) return;
                    if (visible is DesktopWorkResult<DesktopContactDetails>.Succeeded { Value.Contact.LifecycleState: "active" } allowed)
                        selectedBeforeRefresh = allowed.Value.Contact;
                    else if (visible is DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure)
                    { UpdateSessionState(false); return; }
                    else if (visible is DesktopWorkResult<DesktopContactDetails>.Forbidden or DesktopWorkResult<DesktopContactDetails>.NotFound
                        or DesktopWorkResult<DesktopContactDetails>.Succeeded)
                        selectedBeforeRefresh = null;
                    else
                    { SetFeedback("Не удалось подтвердить доступ к выбранному контакту. Повторите обновление.", WorkHubFeedbackKind.Warning); return; }
                }
                ApplyListPage(result, append, values => Contacts = !append && values.Count > 0 && selectedBeforeRefresh is { } previous
                    && values.All(item => item.Id != previous.Id) ? [.. values, previous] : values,
                    Contacts, item => item.Id);
                if (!append && result is DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded
                    && ContactCard is { IsEditing: false, HasRelationDraft: false, IsLoading: false, IsSaving: false } card && SelectedContact is { } selected)
                    await card.LoadAsync(selected.Id, linked.Token);
            }
            else
            {
                var result = area == WorkHubArea.Archive
                    ? await _client.GetArchiveAsync(append ? _listCursor : null, linked.Token)
                    : await _client.GetTrashAsync(append ? _listCursor : null, linked.Token);
                if (!Current()) return;
                ApplyListPage(result, append, values => { LifecycleItems = values; SelectFirstVisibleLifecycleItem(); },
                    LifecycleItems, item => item.ObjectId);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { EndOperation(); NotifyPagination(); }

        bool Current() => !linked.IsCancellationRequested && !_disposed && _sessionAvailable
            && activation == _activationGeneration && generation == _listGeneration && area == Area && CanReadCurrentArea;
    }

    private void ApplyListPage<T>(DesktopWorkResult<IReadOnlyList<T>> result, bool append,
        Action<IReadOnlyList<T>> set, IReadOnlyList<T> previous, Func<T, Guid> id)
    {
        if (result is DesktopWorkResult<IReadOnlyList<T>>.Succeeded page)
        {
            Apply(result, values => set((append ? previous.Concat(values) : values).DistinctBy(id).ToArray()));
            _listCursor = page.NextCursor;
            if (_listCursor is not null && !_listCursors.Add(_listCursor))
            {
                _listCursor = null;
                SetFeedback("Сервер повторил страницу. Обновите раздел, чтобы продолжить.", WorkHubFeedbackKind.Warning);
            }
        }
        else
        {
            if (result is DesktopWorkResult<IReadOnlyList<T>>.MalformedResponse) _listCursor = null;
            if (result is DesktopWorkResult<IReadOnlyList<T>>.Forbidden or DesktopWorkResult<IReadOnlyList<T>>.AuthenticationFailure)
            {
                set([]);
                ResetListPagination();
            }
            Apply(result, _ => { });
        }
    }
}
