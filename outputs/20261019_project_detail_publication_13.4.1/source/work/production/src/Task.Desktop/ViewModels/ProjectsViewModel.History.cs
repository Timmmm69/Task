using Task.Desktop.Administration;
using Task.Desktop.Projects;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed record ProjectHistoryItemViewModel(DesktopProjectHistoryEvent Source, string AuthorText)
{
    public Guid Id => Source.Id;
    public string DateText => Source.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz");
    public string ActionText => Source.Action switch
    {
        "project.create" => "Проект создан",
        "project.patch" => "Изменены сведения проекта",
        "project.archive" => "Проект архивирован",
        "project.unarchive" => "Проект возвращён из архива",
        "project.member-add" => "Добавлен участник",
        "project.member-role" => "Изменена роль участника",
        "project.member-remove" => "Удалён участник",
        "project.link-add" => "Добавлена связь",
        "project.link-remove" => "Удалена связь",
        _ => Source.Action
    };
    public string VersionText => $"Версия {Source.Version}";
    public string FieldsText => Source.ChangedFields.Count == 0 ? "Изменённые поля не указаны."
        : "Изменённые поля: " + string.Join(", ", Source.ChangedFields);
}

public sealed partial class ProjectsViewModel
{
    private const int HistoryPageSize = 50;
    private bool _historyExpanded;
    private int _historyNextPage = 1;
    private bool _historyHasMore;
    private IReadOnlyList<ProjectHistoryItemViewModel> _history = [];
    public IReadOnlyList<ProjectHistoryItemViewModel> History
    { get => _history; private set => SetProperty(ref _history, value); }
    public bool CanReadHistory => CanRead && _capabilities.Contains("History.Read")
        && _sessionAvailable && _client is IDesktopProjectHistoryApiClient;
    private bool CanLoadHistory => CanReadHistory && _projectConfirmed && SelectedItem is not null
        && IsActive && _networkAvailable && !_disposed && !HistorySection.IsLoading;
    public AsyncCommand RefreshHistoryCommand { get; private set; } = null!;
    public AsyncCommand MoreHistoryCommand { get; private set; } = null!;
    public AsyncTask CurrentHistoryOperation { get; private set; } = AsyncTask.CompletedTask;

    private void InitializeHistory()
    {
        RefreshHistoryCommand = new((_, token) => StartHistoryLoad(true, token), _ => CanLoadHistory);
        MoreHistoryCommand = new((_, token) => StartHistoryLoad(false, token), _ => CanLoadHistory && _historyHasMore);
    }

    public AsyncTask OpenHistorySectionAsync()
    {
        _historyExpanded = true;
        return CanLoadHistory && !HistorySection.HasConfirmedData ? StartHistoryLoad(true) : AsyncTask.CompletedTask;
    }

    public void CloseHistorySection()
    { _historyExpanded = false; ResetHistory(); }

    private void ResetHistory()
    {
        HistorySection.Cancel(); History = []; _historyNextPage = 1; _historyHasMore = false;
        HistorySection.SetState(ProjectSectionState.NotLoaded, false);
        NotifyHistory();
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanReadHistory));
        RefreshHistoryCommand?.RaiseCanExecuteChanged(); MoreHistoryCommand?.RaiseCanExecuteChanged();
    }

    private AsyncTask StartHistoryLoad(bool refresh, CancellationToken token = default) =>
        CurrentHistoryOperation = LoadHistoryAsync(refresh, token);

    private async AsyncTask LoadHistoryAsync(bool refresh, CancellationToken token)
    {
        if (!CanLoadHistory || _client is not IDesktopProjectHistoryApiClient client || SelectedItem is not { } item) return;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _activation!.Token);
        using var request = HistorySection.BeginRequest(lifetime.Token);
        bool Current() => HistorySection.IsCurrent(request, item.Id) && ReferenceEquals(SelectedItem, item)
            && CanReadHistory && IsActive && _networkAvailable && !_disposed;
        var page = refresh ? 1 : _historyNextPage;
        if (refresh) { History = []; _historyHasMore = false; HistorySection.SetState(ProjectSectionState.Loading, false); }
        NotifyHistory();
        try
        {
            var result = await client.GetHistoryAsync(item.Id, page, HistoryPageSize, request.Token);
            if (!Current()) return;
            switch (result)
            {
                case DesktopProjectResult<DesktopProjectHistoryPage>.Succeeded success:
                    var rows = success.Value.Items.Select(e => new ProjectHistoryItemViewModel(e, "Автор не указан или недоступен")).ToArray();
                    // Names are optional and fetched only through User.Read; never infer them from IDs.
                    if (_directory is not null && _capabilities.Contains("User.Read") && !_directoryForbidden)
                    {
                        foreach (var actor in rows.Select(e => e.Source.ActorUserId).OfType<Guid>().Distinct())
                        {
                            try
                            {
                                var user = await _directory.GetUserAsync(actor, request.Token);
                                if (!Current()) return;
                                if (user is DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure)
                                { UpdateSessionState(false); return; }
                                if (user is DesktopWorkResult<DesktopAdminUser>.Forbidden)
                                {
                                    _directoryForbidden = true; ResetPeople();
                                    Editor?.UpdatePeopleAccess([], _sessionAvailable, _networkAvailable);
                                    History = History.Select(e => e with { AuthorText = "Автор не указан или недоступен" }).ToArray();
                                    rows = rows.Select(e => e with { AuthorText = "Автор не указан или недоступен" }).ToArray();
                                    break;
                                }
                                if (user is DesktopWorkResult<DesktopAdminUser>.Succeeded name && name.Value.Id == actor)
                                    rows = rows.Select(e => e.Source.ActorUserId == actor ? e with { AuthorText = name.Value.DisplayName } : e).ToArray();
                            }
                            catch (OperationCanceledException) when (request.IsCancellationRequested) { throw; }
                            catch (Exception) { /* Optional directory failure keeps neutral author text. */ }
                        }
                    }
                    if (!Current()) return;
                    History = (refresh ? rows : History.Concat(rows)).DistinctBy(e => e.Id).ToArray();
                    _historyNextPage = page + 1;
                    _historyHasMore = success.Value.Items.Count == HistoryPageSize && page < 10000;
                    HistorySection.SetState(History.Count == 0 ? ProjectSectionState.Empty : ProjectSectionState.Loaded, true);
                    break;
                case DesktopProjectResult<DesktopProjectHistoryPage>.AuthenticationFailure:
                    UpdateSessionState(false); break;
                case DesktopProjectResult<DesktopProjectHistoryPage>.Forbidden:
                case DesktopProjectResult<DesktopProjectHistoryPage>.NotFound:
                    History = []; _historyHasMore = false;
                    HistorySection.SetState(ProjectSectionState.Forbidden, false); break;
                case DesktopProjectResult<DesktopProjectHistoryPage>.ServerUnavailable:
                    HistorySection.SetState(ProjectSectionState.ServerUnavailable); break;
                default: HistorySection.SetState(ProjectSectionState.Error); break;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { if (Current()) HistorySection.SetState(ProjectSectionState.Error); }
        finally { HistorySection.FinishRequest(request); NotifyHistory(); }
    }
}
