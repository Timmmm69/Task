namespace Task.Desktop.ViewModels;

public enum ProjectSectionState
{
    NotLoaded, Loading, Loaded, Empty, Forbidden, Offline, ServerUnavailable, Error, PartialAccess, Cancelled
}

/// <summary>Session-local section context and request lifetime; never persists business data.</summary>
public sealed class ProjectDetailSectionViewModel : ViewModelBase
{
    private CancellationTokenSource? _request;
    private Guid? _projectId;
    private Guid? _organizationId;
    private ProjectSectionState _state;
    private bool _canRead;
    private bool _hasConfirmedData;
    private string? _message;

    internal ProjectDetailSectionViewModel(string key, bool isConnected = true)
    { Key = key; IsConnected = isConnected; }

    public string Key { get; }
    public bool IsConnected { get; }
    public Guid? ProjectId => _projectId;
    public Guid? OrganizationId => _organizationId;
    public bool CanRead => _canRead;
    public bool HasConfirmedData => _hasConfirmedData;
    public ProjectSectionState State => _state;
    public bool IsLoading => State == ProjectSectionState.Loading;
    public string StatusText => _message ?? State switch
    {
        ProjectSectionState.Loading => "Загрузка раздела…",
        ProjectSectionState.Loaded => "Данные подтверждены сервером.",
        ProjectSectionState.Empty => "Доступных данных нет.",
        ProjectSectionState.Forbidden => "Нет доступа к разделу.",
        ProjectSectionState.Offline => "Нет соединения. Подтверждённые данные доступны для просмотра.",
        ProjectSectionState.ServerUnavailable => "Сервер недоступен. Повторите загрузку раздела.",
        ProjectSectionState.Error => "Не удалось загрузить раздел. Повторите попытку.",
        ProjectSectionState.PartialAccess => "Часть сведений недоступна.",
        ProjectSectionState.Cancelled => "Загрузка остановлена.",
        _ => "Раздел ещё не загружен."
    };

    internal void SetContext(Guid? projectId, Guid? organizationId, bool canRead, bool online)
    {
        Cancel();
        _projectId = projectId; _organizationId = organizationId; _canRead = IsConnected && canRead;
        OnPropertyChanged(nameof(ProjectId)); OnPropertyChanged(nameof(OrganizationId)); OnPropertyChanged(nameof(CanRead));
        SetState(!IsConnected || projectId is null ? ProjectSectionState.NotLoaded
            : !CanRead ? ProjectSectionState.Forbidden : !online ? ProjectSectionState.Offline : ProjectSectionState.NotLoaded, false);
    }

    internal CancellationTokenSource BeginRequest(CancellationToken token)
    {
        Cancel();
        if (!CanRead || ProjectId is null) throw new InvalidOperationException("Section context is not readable.");
        _request = CancellationTokenSource.CreateLinkedTokenSource(token);
        SetState(ProjectSectionState.Loading);
        return _request;
    }

    internal bool IsCurrent(CancellationTokenSource request, Guid projectId) =>
        ReferenceEquals(_request, request) && !request.IsCancellationRequested && CanRead && ProjectId == projectId;

    internal void FinishRequest(CancellationTokenSource request)
    {
        if (!ReferenceEquals(_request, request)) return;
        _request = null;
        if (IsLoading) SetState(ProjectSectionState.Cancelled);
    }

    internal void Cancel()
    {
        var request = _request; _request = null;
        request?.Cancel(); // The request owner disposes after its async operation exits.
        if (IsLoading) SetState(ProjectSectionState.Cancelled);
    }

    internal void SetState(ProjectSectionState state, bool? hasConfirmedData = null, string? message = null)
    {
        _state = state; _message = message;
        if (hasConfirmedData.HasValue) _hasConfirmedData = hasConfirmedData.Value;
        OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(HasConfirmedData)); OnPropertyChanged(nameof(StatusText));
    }
}
