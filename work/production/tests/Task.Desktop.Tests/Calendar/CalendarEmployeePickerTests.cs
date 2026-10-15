using Task.Desktop.Administration;
using Task.Desktop.Calendar;
using Task.Desktop.Tests.Employees;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Calendar;

public sealed partial class CalendarEmployeePickerTests
{
    private static readonly string[] Grants = ["Calendar.Read", "CalendarEvent.Create", "CalendarEvent.Update", "User.Read"];
    private static readonly DesktopAdminUser Employee = new(Guid.NewGuid(), 1, "Иван Иванов", "ivan2", "Инженер", "active");
    private static EmployeePickerTests.Directory Directory() => new()
    {
        Search = (_, _, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([Employee])),
        Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee))
    };
    private static async AsyncTask Choose(CalendarEventEditorViewModel editor)
    {
        await editor.AttendeePicker!.SearchAsync();
        editor.AttendeePicker.Candidate = Assert.Single(editor.AttendeePicker.Results);
        Assert.True(editor.AttendeePicker.ConfirmSelection());
    }
    private static CalendarEventEditorViewModel Editor(EmployeePickerTests.Directory directory, DesktopCalendarEvent? source = null)
    {
        var editor = new CalendarEventEditorViewModel(source, TimeZoneInfo.Utc) { Title = "Встреча", IsAllDay = true };
        editor.InitializePeople(directory, Grants, true);
        return editor;
    }

    [Fact]
    public async AsyncTask EmployeesContactsAndResponseMetadataRoundTrip()
    {
        var directory = Directory(); using var editor = Editor(directory);
        await Choose(editor); await editor.AddAttendeeCommand.ExecuteAsync();
        var row = Assert.Single(editor.Attendees);
        Assert.Equal(Employee.Id, row.Id); Assert.True(row.IsUser);
        Assert.Equal("Иван Иванов (ivan2)", row.DisplayName);
        Assert.Equal("pending", row.ResponseStatus); Assert.Null(row.RespondedAtUtc);
        row.Role = "optional"; row.ResponseStatus = "accepted";
        var response = row.RespondedAtUtc;
        await Choose(editor); await editor.AddAttendeeCommand.ExecuteAsync();
        Assert.Single(editor.Attendees); Assert.Contains("уже добавлен", editor.ValidationMessage);
        editor.IsUserAttendee = false; editor.AttendeeId = Employee.Id.ToString();
        await editor.AddAttendeeCommand.ExecuteAsync();
        Assert.Equal(2, editor.Attendees.Count); Assert.False(editor.Attendees[1].IsUser);
        editor.Attendees[1].Role = "observer";
        Assert.True(editor.TryBuild(TimeZoneInfo.Utc, out var command));
        using var reopened = Editor(directory, Event(command.Attendees!)); await reopened.PeopleOperation;
        Assert.Equal(command.Attendees, reopened.Attendees.Select(a => a.ToAttendee()));
        Assert.Equal(response, reopened.Attendees[0].RespondedAtUtc);
        await reopened.RemoveAttendeeCommand.ExecuteAsync(reopened.Attendees[0]);
        Assert.False(Assert.Single(reopened.Attendees).IsUser);
    }

    [Theory]
    [InlineData("deactivated")]
    [InlineData("blocked")]
    [InlineData("missing")]
    [InlineData("offline")]
    [InlineData("forbidden")]
    [InlineData("wrongId")]
    public async AsyncTask StaleSelectionCannotBeAdded(string failure)
    {
        var directory = Directory(); using var editor = Editor(directory); await Choose(editor);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(Failure(failure));
        await editor.AddAttendeeCommand.ExecuteAsync();
        Assert.Empty(editor.Attendees); Assert.False(string.IsNullOrWhiteSpace(editor.ValidationMessage));
    }

    private static DesktopWorkResult<DesktopAdminUser> Failure(string failure) => failure switch
    {
        "missing" => new DesktopWorkResult<DesktopAdminUser>.NotFound(),
        "offline" => new DesktopWorkResult<DesktopAdminUser>.ServerUnavailable(),
        "forbidden" => new DesktopWorkResult<DesktopAdminUser>.Forbidden(),
        "wrongId" => new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee with { Id = Guid.NewGuid() }),
        _ => new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee with { AccountStatus = failure })
    };

    [Fact]
    public async AsyncTask NoUserReadMakesZeroDirectoryCallsAndPreservesUuidWorkflow()
    {
        var directory = Directory(); using var editor = new CalendarEventEditorViewModel(Event([new(Employee.Id, true, "observer", "accepted", DateTimeOffset.UtcNow)]));
        editor.InitializePeople(directory, ["Calendar.Read", "CalendarEvent.Update"], true);
        await editor.AttendeePicker!.SearchAsync(); await editor.PeopleOperation;
        Assert.False(editor.ShowEmployeePicker); Assert.True(editor.ShowAttendeeId);
        editor.AttendeeId = Guid.NewGuid().ToString(); await editor.AddAttendeeCommand.ExecuteAsync();
        Assert.Equal(2, editor.Attendees.Count); Assert.Equal(0, directory.GetCalls + directory.SearchCalls);
        Assert.DoesNotContain("Иван", editor.Attendees[0].DisplayName);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("close")]
    [InlineData("offline")]
    [InlineData("revoke")]
    public async AsyncTask LateSearchAndAddCannotLeakAcrossEditorTransitions(string transition)
    {
        var directory = Directory(); using var editor = Editor(directory); await Choose(editor);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        CancellationToken requestToken = default;
        directory.Get = (_, ct) => { requestToken = ct; return pending.Task; };
        var adding = editor.AddAttendeeCommand.ExecuteAsync();
        if (transition == "switch") editor.IsUserAttendee = false;
        else if (transition == "close") editor.Dispose();
        else editor.UpdatePeopleAccess(transition == "revoke" ? [] : Grants, transition != "offline");
        Assert.True(requestToken.IsCancellationRequested);
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee)); await adding;
        Assert.Empty(editor.Attendees); Assert.Null(editor.AttendeePicker!.SelectedUserAccountId);
        Assert.Empty(editor.AttendeePicker.Results);
    }

    [Fact]
    public async AsyncTask TypeSwitchDuringSearchClearsContactIdAndIgnoresLateResult()
    {
        var directory = Directory(); using var editor = Editor(directory);
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>();
        directory.Search = (_, _, _) => pending.Task;
        var searching = editor.AttendeePicker!.SearchAsync(); editor.IsUserAttendee = false;
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([Employee])); await searching;
        Assert.Empty(editor.AttendeePicker.Results); Assert.False(editor.CanAddAttendee);
        editor.AttendeeId = Employee.Id.ToString(); editor.IsUserAttendee = true;
        Assert.Equal("", editor.AttendeeId); Assert.Null(editor.AttendeePicker.SelectedUserAccountId);
    }

    [Fact]
    public async AsyncTask PerTypeLimitAndRemovalArePreserved()
    {
        var users = Enumerable.Range(0, 500).Select(_ => new DesktopCalendarAttendee(Guid.NewGuid(), true, "required", "pending", null)).ToArray();
        var directory = Directory(); using var editor = Editor(directory, Event(users)); await editor.PeopleOperation;
        await Choose(editor); Assert.False(editor.AddAttendeeCommand.CanExecute(null));
        editor.IsUserAttendee = false; editor.AttendeeId = Guid.NewGuid().ToString();
        await editor.AddAttendeeCommand.ExecuteAsync(); Assert.Equal(501, editor.Attendees.Count);
        await editor.RemoveAttendeeCommand.ExecuteAsync(editor.Attendees[0]);
        editor.IsUserAttendee = true; await Choose(editor); Assert.True(editor.CanAddAttendee);
    }

    [Fact]
    public async AsyncTask LateNamesAfterPermissionRevocationAreSuppressed()
    {
        var directory = Directory();
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        directory.Get = (_, _) => pending.Task;
        using var editor = Editor(directory, Event([new(Employee.Id, true, "required", "pending", null)]));
        var loading = editor.PeopleOperation; editor.UpdatePeopleAccess([], true);
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee)); await loading;
        Assert.DoesNotContain("Иван", Assert.Single(editor.Attendees).DisplayName);
        Assert.Equal(Employee.Id, editor.Attendees[0].ToAttendee().Id);
    }

    [Theory]
    [InlineData("CalendarEvent.Create", true)]
    [InlineData("CalendarEvent.Update", false)]
    [InlineData("User.Read", false)]
    public async AsyncTask HostRequiresCalendarReadAndMatchingWritePermission(string write, bool creates)
    {
        using var vm = new CalendarViewModel(new Client(), ["Calendar.Read", write], employeeDirectory: Directory());
        await vm.ActivateAsync(); Assert.Equal(creates, vm.NewEventCommand.CanExecute(null));
        vm.UpdateCapabilities([write, "User.Read"]); Assert.False(vm.NewEventCommand.CanExecute(null));
        Assert.Null(vm.Editor);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("revoke")]
    [InlineData("logout")]
    [InlineData("deactivate")]
    public async AsyncTask HostAccessChangesBlockAttendeeMutation(string transition)
    {
        using var vm = new CalendarViewModel(new Client(), Grants, employeeDirectory: Directory()); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync(); var editor = vm.Editor!; await Choose(editor);
        if (transition == "offline") vm.UpdateConnectivity(false);
        else if (transition == "revoke") vm.UpdateCapabilities(["Calendar.Read", "User.Read"]);
        else if (transition == "logout") vm.UpdateSessionState(false);
        else vm.Deactivate();
        Assert.False(editor.CanEditAttendees); Assert.False(editor.CanAddAttendee); Assert.False(vm.CanSave);
        Assert.False(editor.AttendeePicker!.CanQuery); Assert.Empty(editor.AttendeePicker.Results);
    }

    [Fact]
    public async AsyncTask SaveRechecksNewEmployeeAndKeepsDraftOnDeactivation()
    {
        var directory = Directory(); var client = new Client();
        using var vm = new CalendarViewModel(client, Grants, employeeDirectory: directory); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync(); var editor = vm.Editor!; editor.Title = "Событие"; editor.IsAllDay = true;
        await Choose(editor); await editor.AddAttendeeCommand.ExecuteAsync();
        directory.Get = (_, _) => AsyncTask.FromResult(Failure("deactivated"));
        await vm.SaveEventCommand.ExecuteAsync();
        Assert.Null(client.Command); Assert.Same(editor, vm.Editor); Assert.False(editor.IsSaving);
        Assert.Contains("неактивен", editor.ValidationMessage);
        directory.Get = (_, _) => AsyncTask.FromResult<DesktopWorkResult<DesktopAdminUser>>(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee));
        await vm.SaveEventCommand.ExecuteAsync(); Assert.Null(vm.Editor);
        Assert.Equal(Employee.Id, Assert.Single(client.Command!.Attendees!).Id);
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("deleted")]
    [InlineData("unavailable")]
    public async AsyncTask EditRetainsMixedAttendeesAndNeverAnnouncesFailedSave(string failure)
    {
        var timestamp = DateTimeOffset.Parse("2026-10-10T09:00:00Z");
        var source = Event([new(Employee.Id, true, "optional", "accepted", timestamp), new(Guid.NewGuid(), false, "observer", "tentative", timestamp)]);
        var client = new Client { Source = source, Failure = failure };
        using var vm = new CalendarViewModel(client, Grants, employeeDirectory: Directory()); await vm.ActivateAsync();
        await vm.OpenEventByIdAsync(source.Id); await vm.EditEventCommand.ExecuteAsync();
        var editor = vm.Editor!; await editor.PeopleOperation; await vm.SaveEventCommand.ExecuteAsync();
        Assert.Same(editor, vm.Editor); Assert.DoesNotContain("сохранено", vm.Announcement ?? "");
        Assert.Equal(source.Attendees, client.Command!.Attendees); Assert.Equal(7, client.ExpectedVersion);
        Assert.Equal(source.Attendees, editor.Attendees.Select(a => a.ToAttendee()));
        Assert.NotNull(editor.ValidationMessage);
    }

    [Fact]
    public async AsyncTask SaveWaitsForAddAndCancelsAccountValidationOnAccessLoss()
    {
        var directory = Directory(); var client = new Client();
        using var vm = new CalendarViewModel(client, Grants, employeeDirectory: directory); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync(); var editor = vm.Editor!; editor.Title = "Встреча"; editor.IsAllDay = true;
        await Choose(editor);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        directory.Get = (_, _) => pending.Task;
        var adding = editor.AddAttendeeCommand.ExecuteAsync();
        Assert.False(vm.SaveEventCommand.CanExecute(null)); Assert.False(await vm.SaveEventCommand.ExecuteAsync());
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee)); await adding;
        Assert.True(vm.SaveEventCommand.CanExecute(null));
        pending = new(); CancellationToken request = default;
        directory.Get = (_, token) => { request = token; return pending.Task; };
        var saving = vm.SaveEventCommand.ExecuteAsync(); vm.UpdateConnectivity(false);
        Assert.True(request.IsCancellationRequested);
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee)); await saving;
        Assert.Null(client.Command); Assert.Same(editor, vm.Editor); Assert.False(editor.IsSaving);
    }

    [Fact]
    public async AsyncTask DirectoryForbiddenClearsNamesButAllowsContactChangesAndExistingSave()
    {
        var source = Event([new(Employee.Id, true, "observer", "accepted", DateTimeOffset.UtcNow)]);
        var directory = Directory(); using var editor = Editor(directory, source); await editor.PeopleOperation;
        Assert.Contains("Иван", editor.Attendees[0].DisplayName);
        directory.Search = (_, _, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Forbidden());
        await editor.AttendeePicker!.SearchAsync();
        Assert.DoesNotContain("Иван", editor.Attendees[0].DisplayName); Assert.False(editor.AttendeePicker.CanQuery);
        editor.IsUserAttendee = false; editor.AttendeeId = Guid.NewGuid().ToString();
        await editor.AddAttendeeCommand.ExecuteAsync(); Assert.Equal(2, editor.Attendees.Count);
        Assert.True(await editor.ValidateNewEmployeesAsync(default));
        Assert.True(editor.TryBuild(TimeZoneInfo.Utc, out var command)); Assert.Equal(source.Attendees[0], command.Attendees![0]);
    }

    [Fact]
    public async AsyncTask SaveBuildsFinalDraftAfterAwaitingEmployeeValidation()
    {
        var directory = Directory(); var client = new Client();
        using var vm = new CalendarViewModel(client, Grants, employeeDirectory: directory); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync(); var editor = vm.Editor!; editor.Title = "До проверки"; editor.IsAllDay = true;
        await Choose(editor); await editor.AddAttendeeCommand.ExecuteAsync();
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopAdminUser>>();
        directory.Get = (_, _) => pending.Task;
        var saving = vm.SaveEventCommand.ExecuteAsync(); Assert.True(editor.IsSaving);
        editor.Title = "Финальный черновик";
        pending.SetResult(new DesktopWorkResult<DesktopAdminUser>.Succeeded(Employee)); await saving;
        Assert.Equal("Финальный черновик", client.Command!.Title); Assert.Null(vm.Editor);
    }

    [Fact]
    public async AsyncTask DirectoryAuthenticationFailureEndsHostSession()
    {
        var directory = Directory();
        using var vm = new CalendarViewModel(new Client(), Grants, employeeDirectory: directory); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync();
        directory.Search = (_, _, _) => AsyncTask.FromResult<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.AuthenticationFailure());
        await vm.Editor!.AttendeePicker!.SearchAsync();
        Assert.False(vm.CanSave); Assert.False(vm.Editor.CanEditAttendees);
        Assert.Equal(CalendarScreenState.SessionEnded, vm.State);
    }

    [Fact]
    public async AsyncTask ClosingHostEditorDisposesSearchAndRejectsLateResponse()
    {
        var directory = Directory();
        using var vm = new CalendarViewModel(new Client(), Grants, employeeDirectory: directory); await vm.ActivateAsync();
        await vm.NewEventCommand.ExecuteAsync(); var editor = vm.Editor!;
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>>(); CancellationToken request = default;
        directory.Search = (_, _, token) => { request = token; return pending.Task; };
        var searching = editor.AttendeePicker!.SearchAsync(); await vm.CancelEditorCommand.ExecuteAsync();
        Assert.True(request.IsCancellationRequested); Assert.Null(vm.Editor);
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopAdminUser>>.Succeeded([Employee])); await searching;
        Assert.Empty(editor.AttendeePicker.Results); Assert.False(editor.CanAddAttendee);
    }

    private static DesktopCalendarEvent Event(IReadOnlyList<DesktopCalendarAttendee> attendees) => new(Guid.NewGuid(), Guid.NewGuid(), 7,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "Событие", null, new(2026, 10, 15), true, null, null, "UTC", "scheduled", attendees, "\"v7\"");

    private sealed class Client : IDesktopCalendarApiClient
    {
        public DesktopCalendarEvent Source = Event([]);
        public DesktopCalendarEventCommand? Command;
        public long ExpectedVersion;
        public string? Failure;
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopSchedulePage>> GetScheduleAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string timeZoneId, CancellationToken cancellationToken) =>
            AsyncTask.FromResult<DesktopCalendarResult<DesktopSchedulePage>>(new DesktopCalendarResult<DesktopSchedulePage>.Succeeded(new([], fromUtc, toUtc)));
        public System.Threading.Tasks.Task<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>> GetConflictsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
            AsyncTask.FromResult<DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>>(new DesktopCalendarResult<IReadOnlyList<DesktopScheduleConflict>>.Succeeded([]));
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> GetEventAsync(Guid eventId, CancellationToken cancellationToken) =>
            AsyncTask.FromResult<DesktopCalendarResult<DesktopCalendarEvent>>(new DesktopCalendarResult<DesktopCalendarEvent>.Succeeded(Source));
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> CreateEventAsync(DesktopCalendarEventCommand command, CancellationToken cancellationToken)
        { Command = command; return AsyncTask.FromResult<DesktopCalendarResult<DesktopCalendarEvent>>(new DesktopCalendarResult<DesktopCalendarEvent>.Succeeded(Source)); }
        public System.Threading.Tasks.Task<DesktopCalendarResult<DesktopCalendarEvent>> UpdateEventAsync(Guid eventId, long expectedVersion, DesktopCalendarEventCommand command, CancellationToken cancellationToken)
        { Command = command; ExpectedVersion = expectedVersion; return AsyncTask.FromResult<DesktopCalendarResult<DesktopCalendarEvent>>(Failure switch {
            "conflict" => new DesktopCalendarResult<DesktopCalendarEvent>.VersionConflict(),
            "deleted" => new DesktopCalendarResult<DesktopCalendarEvent>.NotFound(),
            _ => new DesktopCalendarResult<DesktopCalendarEvent>.ServerUnavailable() }); }
    }
}
