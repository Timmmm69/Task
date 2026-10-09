using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    private static DesktopReminder TestReminder() => new(Guid.NewGuid(),4,FakeClient.TaskId,"Task","before_deadline",15,null,DateTimeOffset.UtcNow,"delivered",
        new(Guid.NewGuid(),7,Guid.Empty,DateTimeOffset.UtcNow,"delivered"));
    [Fact]
    public async System.Threading.Tasks.Task CorporateReminders_ManageOwnDoesNotRequireNotificationReadAndConflictRetainsDraft()
    {
        var item = TestReminder();
        var client = new FakeClient { Reminders = [item], ReminderSaveConflict = true };
        using var hub = new WorkHubViewModel(client,["Reminder.ManageOwn"]);
        hub.Activate(WorkHubArea.Notifications);
        await Eventually(() => hub.CorporateReminders.Count == 1);
        Assert.True(hub.CanReadCurrentArea); Assert.False(hub.CanReadNotifications);
        hub.SelectedCorporateReminder = item;
        hub.ReminderOffset = 99;
        Assert.True(hub.HasReminderDraft);
        await hub.SaveReminderCommand.ExecuteAsync();
        Assert.Equal(99,hub.ReminderOffset); Assert.Equal(item,hub.SelectedCorporateReminder);
        Assert.True(hub.HasReminderDraft);
        Assert.Equal(WorkHubFeedbackKind.Warning,hub.FeedbackKind);
    }
    [Fact]
    public async System.Threading.Tasks.Task CorporateReminders_SnoozeUsesSavedDefaultAndOccurrenceVersionThenRefreshes()
    {
        var item = TestReminder();
        var client = new FakeClient { Reminders = [item] };
        using var hub = new WorkHubViewModel(client,["Reminder.ManageOwn","Settings.ReadOwn"]);
        hub.Activate(WorkHubArea.Notifications);
        await Eventually(() => hub.CorporateReminders.Count == 1);
        hub.SelectedCorporateReminder = item;
        hub.DefaultSnoozeMinutes = 99; // Unsaved UI draft must not change the saved 15-minute action.
        var before = DateTimeOffset.UtcNow;
        await hub.SnoozeReminderCommand.ExecuteAsync();
        Assert.Equal(7,client.ActedOccurrenceVersion);
        Assert.InRange(client.ActedUntil!.Value,before.AddMinutes(15),DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.Equal("snoozed",Assert.Single(hub.CorporateReminders).Status);
        Assert.Equal(99,hub.DefaultSnoozeMinutes);
        hub.UpdateSessionState(false);
        Assert.Empty(hub.CorporateReminders); Assert.False(hub.SnoozeReminderCommand.CanExecute(null));
    }
    [Fact]
    public async System.Threading.Tasks.Task CorporateReminders_RevocationDropsInFlightResponse()
    {
        var pending = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopReminder>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient { ReminderHandler = _ => pending.Task };
        using var hub = new WorkHubViewModel(client,["Reminder.ManageOwn"]);
        hub.Activate(WorkHubArea.Notifications);
        hub.UpdateCapabilities([]);
        pending.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopReminder>>.Succeeded([TestReminder()]));
        await System.Threading.Tasks.Task.Delay(40);
        Assert.Empty(hub.CorporateReminders);
        Assert.False(hub.SaveReminderCommand.CanExecute(null));
    }
    internal sealed partial class FakeClient
    {
        public IReadOnlyList<DesktopReminder> Reminders { get; set; } = [];
        public bool ReminderSaveConflict { get; init; }
        public long? ActedOccurrenceVersion { get; private set; }
        public DateTimeOffset? ActedUntil { get; private set; }
        public Func<CancellationToken,System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopReminder>>>>? ReminderHandler { get; init; }
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopReminder>>> GetRemindersAsync(CancellationToken ct = default) => ReminderHandler?.Invoke(ct) ?? Ok(Reminders);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> GetReminderAsync(Guid id,CancellationToken ct = default) => Ok(Reminders.Single(x=>x.Id==id));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopReminder>> SaveReminderAsync(Guid target,string trigger,int? offset,DateTimeOffset? absolute,DesktopReminder? existing,CancellationToken ct = default) =>
            ReminderSaveConflict ? System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopReminder>>(new DesktopWorkResult<DesktopReminder>.Conflict()) : Ok(existing!);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> CancelReminderAsync(DesktopReminder item,CancellationToken ct = default) => Ok(true);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> RestoreReminderAsync(DesktopReminder item,CancellationToken ct = default) => Ok(true);
        public System.Threading.Tasks.Task<DesktopWorkResult<bool>> ActOnReminderAsync(Guid id,long occurrenceVersion,DateTimeOffset? until,CancellationToken ct = default)
        { ActedOccurrenceVersion=occurrenceVersion; ActedUntil=until; Reminders=Reminders.Select(x=>x.Id==id ? x with { Status=until is null ? "cancelled" : "snoozed" } : x).ToArray(); return Ok(true); }
    }
}
