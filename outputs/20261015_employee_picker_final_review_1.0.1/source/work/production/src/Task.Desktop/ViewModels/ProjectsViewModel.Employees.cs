using System.ComponentModel;
using Task.Desktop.Administration;
using Task.Desktop.Projects;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed partial class ProjectEditorViewModel
{
    private bool _initializingManager;
    public EmployeePickerViewModel? ManagerPicker { get; private set; }
    public bool UsesManagerPicker { get; private set; }
    public AsyncCommand ClearManagerCommand { get; private set; } = new((_, _) => AsyncTask.CompletedTask);

    public async AsyncTask InitializePeopleAsync(IDesktopEmployeeDirectory? directory, IEnumerable<string> capabilities,
        bool sessionReady, bool online, CancellationToken cancellationToken)
    {
        UsesManagerPicker = directory is not null && capabilities.Contains("User.Read", StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(UsesManagerPicker));
        // Keep an access-gated picker for live capability grants while this form stays open.
        if (directory is null) return;
        ManagerPicker = new(directory!, capabilities);
        ManagerPicker.UpdateAccess(capabilities, sessionReady, online);
        ManagerPicker.PropertyChanged += ManagerChanged;
        ClearManagerCommand.Dispose();
        ClearManagerCommand = new((_, _) => { ManagerId = ""; ManagerPicker.ClearSelection(); return AsyncTask.CompletedTask; });
        OnPropertyChanged(nameof(ManagerPicker)); OnPropertyChanged(nameof(ClearManagerCommand));
        if (!UsesManagerPicker || Source?.ManagerUserId is not { } manager) return;
        _initializingManager = true;
        try { await ManagerPicker.PreselectAsync(manager, cancellationToken); }
        finally { _initializingManager = false; }
    }

    private void ManagerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_initializingManager && args.PropertyName == nameof(EmployeePickerViewModel.SelectedEmployee)
            && ManagerPicker?.SelectedUserAccountId is { } id) ManagerId = id.ToString("D");
        else if (!_initializingManager && args.PropertyName == nameof(EmployeePickerViewModel.SelectedEmployee)
            && ManagerPicker?.State == EmployeePickerState.Idle) ManagerId = "";
        // Failed reads/access transitions must never silently clear a stored manager.
    }

    public void UpdatePeopleAccess(IEnumerable<string> capabilities, bool sessionReady, bool online)
    {
        if (ManagerPicker is null) return;
        var enablePicker = !UsesManagerPicker && capabilities.Contains("User.Read", StringComparer.OrdinalIgnoreCase);
        // Keep the stored ID when directory access changes; the UUID fallback remains explicit.
        _initializingManager = true;
        try
        {
            ManagerPicker.UpdateAccess(capabilities, sessionReady, online);
            UsesManagerPicker = capabilities.Contains("User.Read", StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(UsesManagerPicker));
        }
        finally { _initializingManager = false; }
        if (enablePicker && sessionReady && online && Guid.TryParse(ManagerId, out var manager) && manager != Guid.Empty)
            _ = ManagerPicker.PreselectAsync(manager);
    }

    public void Dispose()
    {
        if (ManagerPicker is not null) { ManagerPicker.PropertyChanged -= ManagerChanged; ManagerPicker.Dispose(); }
        ClearManagerCommand.Dispose();
    }
}

public sealed partial class ProjectsViewModel
{
    private readonly IDesktopEmployeeDirectory? _directory;
    private long _peopleGeneration;
    private bool _membersConfirmed;
    private bool _directoryForbidden;
    private bool _writesForbidden;
    public EmployeePickerViewModel? MemberPicker { get; private set; }
    public bool UsesEmployeePicker => _directory is not null && _capabilities.Contains("User.Read");
    public string MemberEntryHint => _directoryForbidden ? "Сервер запретил доступ к каталогу сотрудников (User.Read). Обновите права или выполните вход снова."
        : UsesEmployeePicker
        ? "Выберите активного сотрудника и проектную роль. Повторное участие не допускается."
        : "Каталог недоступен без User.Read. Укажите UUID активной учётной записи; сервер проверит доступ. Полный переход на выбор по имени требует решения по правам.";
    public Guid? NewMemberAccountId => UsesEmployeePicker ? MemberPicker?.SelectedUserAccountId
        : Guid.TryParse(NewMemberUserId.Trim(), out var id) && id != Guid.Empty ? id : null;
    private bool CanAddMember => CanManageMembers && !IsDetailLoading && _membersConfirmed
        && SelectedItem is not null && NewMemberRole is not null && NewMemberAccountId is { } id
        && !Members.Any(member => member.UserAccountId == id)
        && (!UsesEmployeePicker || MemberPicker?.State == EmployeePickerState.Ready);

    private async AsyncTask OpenEditorAsync(DesktopProjectDto? source, CancellationToken cancellationToken)
    {
        var editor = new ProjectEditorViewModel(source?.OwnerUserId ?? _currentUserId, source);
        Editor = editor;
        await editor.InitializePeopleAsync(_directory, _directoryForbidden ? [] : _capabilities, _sessionAvailable, _networkAvailable, cancellationToken);
        if (!ReferenceEquals(Editor, editor)) return;
        if (editor.ManagerPicker is { } picker) { picker.PropertyChanged += ManagerAccessChanged; CheckDirectoryAccess(picker); }
    }

    private void ResetPeople()
    {
        _peopleGeneration++;
        if (MemberPicker is not null) { MemberPicker.PropertyChanged -= MemberChanged; MemberPicker.Dispose(); }
        MemberPicker = null; NewMemberUserId = "";
        foreach (var item in Items) item.SetPeopleNames(null, null);
        foreach (var member in Members) member.SetName(null);
        if (IsActive && UsesEmployeePicker && _sessionAvailable && _networkAvailable && CanManageMembers)
        {
            MemberPicker = new(_directory!, _directoryForbidden ? [] : _capabilities, allowClear: false);
            MemberPicker.PropertyChanged += MemberChanged;
        }
        OnPropertyChanged(nameof(MemberPicker)); OnPropertyChanged(nameof(UsesEmployeePicker));
        OnPropertyChanged(nameof(MemberEntryHint)); AddMemberCommand.RaiseCanExecuteChanged();
    }

    private void MemberChanged(object? sender, PropertyChangedEventArgs args)
    {
        AddMemberCommand.RaiseCanExecuteChanged();
        if (args.PropertyName == nameof(EmployeePickerViewModel.State) && sender is EmployeePickerViewModel picker) CheckDirectoryAccess(picker);
    }
    private void ManagerAccessChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EmployeePickerViewModel.State) && ReferenceEquals(sender, Editor?.ManagerPicker)) CheckDirectoryAccess(Editor!.ManagerPicker!);
    }
    private void CheckDirectoryAccess(EmployeePickerViewModel picker)
    {
        if (picker.State == EmployeePickerState.AuthenticationRequired && _sessionAvailable) UpdateSessionState(false);
        else if (picker.State == EmployeePickerState.Forbidden && !_directoryForbidden && UsesEmployeePicker)
        { _directoryForbidden = true; ResetPeople(); Editor?.UpdatePeopleAccess([], _sessionAvailable, _networkAvailable); Message = "Каталог сотрудников недоступен (User.Read)."; }
    }

    private async AsyncTask ResolvePeopleAsync(ProjectItemViewModel item, CancellationToken cancellationToken)
    {
        if (!CanRead || !UsesEmployeePicker || _directoryForbidden || !_sessionAvailable || !_networkAvailable || !IsActive) return;
        var generation = _peopleGeneration;
        var names = new Dictionary<Guid, string>();
        var ids = Members.Select(member => member.UserAccountId).Append(item.Source.OwnerUserId);
        if (item.Source.ManagerUserId is { } manager) ids = ids.Append(manager);
        foreach (var id in ids.Distinct())
        {
            if (!Current()) return;
            var result = await _directory!.GetUserAsync(id, cancellationToken);
            if (!Current()) return;
            if (result is DesktopWorkResult<DesktopAdminUser>.Succeeded found && found.Value.Id == id)
                names[id] = $"{found.Value.DisplayName} ({found.Value.Login})";
            else if (result is DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure)
            { UpdateSessionState(false); return; }
            else if (result is DesktopWorkResult<DesktopAdminUser>.Forbidden)
            { _directoryForbidden = true; ResetPeople(); Editor?.UpdatePeopleAccess([], _sessionAvailable, _networkAvailable); Message = "Каталог сотрудников недоступен (User.Read)."; return; }
        }
        if (!Current()) return;
        item.SetPeopleNames(names.GetValueOrDefault(item.Source.OwnerUserId), item.Source.ManagerUserId is { } mid ? names.GetValueOrDefault(mid) : null);
        foreach (var member in Members) member.SetName(names.GetValueOrDefault(member.UserAccountId));
        bool Current() => generation == _peopleGeneration && ReferenceEquals(SelectedItem, item) && IsActive
            && CanRead && _sessionAvailable && _networkAvailable && UsesEmployeePicker && !cancellationToken.IsCancellationRequested;
    }
}
