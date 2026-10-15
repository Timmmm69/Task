using System.ComponentModel;
using Task.Desktop.Administration;
using Task.Desktop.Calendar;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.ViewModels;

public sealed partial class CalendarEventEditorViewModel : IDisposable
{
    private IDesktopEmployeeDirectory? _directory;
    private CancellationTokenSource? _peopleRequest, _validationRequest;
    private long _peopleGeneration;
    private bool _peopleDisposed, _directoryForbidden;
    private bool _canEditAttendees = true;
    internal event Action? AuthenticationRequired;
    private readonly HashSet<Guid> _newEmployees = [];
    public EmployeePickerViewModel? AttendeePicker { get; private set; }
    public bool UsesEmployeePicker { get; private set; }
    public bool ShowEmployeePicker => IsUserAttendee && UsesEmployeePicker;
    public bool ShowAttendeeId => !ShowEmployeePicker;
    public bool CanEditAttendees => !_peopleDisposed && _canEditAttendees && !IsSaving;
    public string AttendeeEntryHint => Attendees.Count(a => a.IsUser == IsUserAttendee) >= 500
        ? "Достигнут лимит: 500 участников этого типа. Удалите участника перед добавлением нового."
        : !IsUserAttendee ? "Укажите UUID контакта."
        : UsesEmployeePicker ? "Найдите сотрудника, подтвердите выбор и нажмите «Добавить»."
        : "Без User.Read каталог недоступен. Сохранён ввод UUID учётной записи.";
    public AsyncTask PeopleOperation { get; private set; } = AsyncTask.CompletedTask;
    private Guid? AttendeeAccountId => ShowEmployeePicker ? AttendeePicker?.SelectedUserAccountId
        : Guid.TryParse(AttendeeId, out var id) && id != Guid.Empty ? id : null;

    internal void InitializePeople(IDesktopEmployeeDirectory? directory, IEnumerable<string> capabilities, bool writable)
    {
        _directory = directory;
        if (directory is not null && AllowAttendees)
        {
            AttendeePicker = new(directory, []);
            AttendeePicker.PropertyChanged += PickerChanged;
        }
        Attendees.CollectionChanged += (_, _) => NotifyAttendeeCommands();
        UpdatePeopleAccess(capabilities, writable);
    }

    internal void UpdatePeopleAccess(IEnumerable<string> capabilities, bool writable)
    {
        _peopleGeneration++; _peopleRequest?.Cancel(); _validationRequest?.Cancel();
        _canEditAttendees = writable && AllowAttendees;
        UsesEmployeePicker = _directory is not null && AllowAttendees && capabilities.Contains("User.Read", StringComparer.OrdinalIgnoreCase);
        foreach (var row in Attendees) row.SetName(null);
        AttendeePicker?.UpdateAccess(UsesEmployeePicker && !_directoryForbidden ? ["User.Read"] : [],
            sessionReady: writable && IsUserAttendee);
        NotifyAttendeeCommands();
        if (writable && UsesEmployeePicker && !_directoryForbidden) PeopleOperation = ResolveNamesAsync();
    }

    private void ResetAttendeeEntry()
    {
        _peopleGeneration++; _peopleRequest?.Cancel(); _validationRequest?.Cancel(); AttendeeId = "";
        // Invalidates both the in-flight search and any previous confirmation.
        AttendeePicker?.UpdateAccess(UsesEmployeePicker && !_directoryForbidden ? ["User.Read"] : [],
            sessionReady: _canEditAttendees && IsUserAttendee);
        NotifyAttendeeCommands();
    }

    private void NotifyAttendeeCommands()
    {
        OnPropertyChanged(nameof(UsesEmployeePicker)); OnPropertyChanged(nameof(ShowEmployeePicker));
        OnPropertyChanged(nameof(ShowAttendeeId)); OnPropertyChanged(nameof(AttendeeEntryHint));
        OnPropertyChanged(nameof(CanEditAttendees)); OnPropertyChanged(nameof(CanAddAttendee));
        AddAttendeeCommand.RaiseCanExecuteChanged(); RemoveAttendeeCommand.RaiseCanExecuteChanged();
    }

    private void PickerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_peopleDisposed) return;
        if (_canEditAttendees && IsUserAttendee && UsesEmployeePicker && !_directoryForbidden
            && args.PropertyName == nameof(EmployeePickerViewModel.State)
            && AttendeePicker?.State is EmployeePickerState.Forbidden or EmployeePickerState.AuthenticationRequired)
        {
            _directoryForbidden = true; _peopleGeneration++; _peopleRequest?.Cancel(); _validationRequest?.Cancel();
            ValidationMessage = AttendeePicker.StatusText;
            foreach (var row in Attendees) row.SetName(null);
            var authenticationFailure = AttendeePicker.State == EmployeePickerState.AuthenticationRequired;
            if (authenticationFailure) _canEditAttendees = false;
            AttendeePicker.UpdateAccess([], sessionReady: !authenticationFailure);
            if (authenticationFailure) AuthenticationRequired?.Invoke();
        }
        NotifyAttendeeCommands();
    }

    private async AsyncTask AddAttendeeAsync(CancellationToken token)
    {
        if (!CanAddAttendee || AttendeeAccountId is not { } id) return;
        if (Attendees.Any(a => a.IsUser == IsUserAttendee && a.Id == id))
        { ValidationMessage = "Этот участник уже добавлен."; return; }
        var isUser = IsUserAttendee;
        var generation = _peopleGeneration;
        string? name = null;
        if (ShowEmployeePicker)
        {
            await AttendeePicker!.PreselectAsync(id, token);
            if (_peopleDisposed || generation != _peopleGeneration || !CanEditAttendees || !IsUserAttendee) return;
            if (AttendeePicker.State != EmployeePickerState.Ready || AttendeePicker.SelectedEmployee is not { } employee || employee.Id != id)
            { ValidationMessage = AttendeePicker.StatusText; return; }
            name = $"{employee.DisplayName} ({employee.Login})";
        }
        if (Attendees.Count(a => a.IsUser == isUser) >= 500) return;
        var row = new CalendarAttendeeEditorRow(new(id, isUser, "required", "pending", null));
        row.SetName(name); Attendees.Add(row);
        if (ShowEmployeePicker) _newEmployees.Add(id);
        AttendeeId = ""; AttendeePicker?.ClearSelection(); ValidationMessage = null;
    }

    // Only new picker additions need active-account validation. Existing attendee data must round-trip unchanged.
    internal async System.Threading.Tasks.Task<bool> ValidateNewEmployeesAsync(CancellationToken token)
    {
        if (!UsesEmployeePicker) return true;
        var generation = _peopleGeneration;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
        _validationRequest = request;
        try
        {
            foreach (var row in Attendees.Where(a => a.IsUser && _newEmployees.Contains(a.Id)).ToArray())
            {
                if (_directoryForbidden) { ValidationMessage = "Не удалось проверить новых сотрудников (User.Read). Удалите их или восстановите доступ."; return false; }
                var result = await _directory!.GetUserAsync(row.Id, request.Token);
                if (_peopleDisposed || generation != _peopleGeneration || request.IsCancellationRequested) return false;
                if (result is DesktopWorkResult<DesktopAdminUser>.Succeeded found && found.Value.Id == row.Id && found.Value.AccountStatus == "active") continue;
                HandleDirectoryFailure(result);
                ValidationMessage = "Новый участник недоступен, неактивен или не прошёл проверку. Удалите его либо повторите попытку.";
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        catch (Exception) { ValidationMessage = "Не удалось проверить новых сотрудников. Повторите попытку."; return false; }
        finally { if (ReferenceEquals(_validationRequest, request)) _validationRequest = null; }
    }

    private async AsyncTask ResolveNamesAsync()
    {
        var generation = _peopleGeneration;
        using var request = new CancellationTokenSource(); _peopleRequest = request;
        try
        {
            foreach (var row in Attendees.Where(a => a.IsUser).ToArray())
            {
                if (!Current()) return;
                var result = await _directory!.GetUserAsync(row.Id, request.Token);
                if (!Current()) return;
                if (result is DesktopWorkResult<DesktopAdminUser>.Succeeded found && found.Value.Id == row.Id)
                    row.SetName($"{found.Value.DisplayName} ({found.Value.Login})");
                else if (HandleDirectoryFailure(result)) return;
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception) { /* Names are optional; preserve all stored attendee data. */ }
        finally { if (ReferenceEquals(_peopleRequest, request)) _peopleRequest = null; }
        bool Current() => !_peopleDisposed && generation == _peopleGeneration && !request.IsCancellationRequested;
    }

    private bool HandleDirectoryFailure(DesktopWorkResult<DesktopAdminUser> result)
    {
        if (result is not (DesktopWorkResult<DesktopAdminUser>.Forbidden or DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure)) return false;
        _directoryForbidden = true; _peopleGeneration++; _peopleRequest?.Cancel(); _validationRequest?.Cancel();
        if (result is DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure) _canEditAttendees = false;
        AttendeePicker?.UpdateAccess([], sessionReady: result is not DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure);
        foreach (var row in Attendees) row.SetName(null);
        if (result is DesktopWorkResult<DesktopAdminUser>.AuthenticationFailure) AuthenticationRequired?.Invoke();
        return true;
    }

    public void Dispose()
    {
        if (_peopleDisposed) return;
        _peopleDisposed = true; _peopleGeneration++; _peopleRequest?.Cancel(); _validationRequest?.Cancel();
        if (AttendeePicker is not null) { AttendeePicker.PropertyChanged -= PickerChanged; AttendeePicker.Dispose(); }
        foreach (var row in Attendees) row.SetName(null);
        AddAttendeeCommand.Dispose(); RemoveAttendeeCommand.Dispose();
    }
}
