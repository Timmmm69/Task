using System.IO;
using Task.Desktop.Calendar;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Domain.Reminders;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalNotificationBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-notification-browser", Guid.NewGuid().ToString("N"));
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async System.Threading.Tasks.Task ReadingWhileFiltered_RemovesRowAndClearsStaleDetails()
    {
        var clock = new Clock();
        using var store = new PersonalTaskStore(new(_root), clock);
        for (var i = 0; i < 3; i++)
        {
            var task = store.Create(new DesktopCreateTaskCommand("Смета проекта " + i, DesktopTaskPriority.Normal));
            store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: clock.Now.AddMinutes(-1));
        }
        store.ReconcileReminders();
        using var vm = new PersonalPlanningViewModel(store, clock);
        vm.NotificationQuery = "СМЕТА"; vm.NotificationReadFilter = "Новые"; vm.NotificationSourceFilter = "Задачи";
        Assert.Equal(3, vm.VisibleNotifications.Count);
        vm.SelectedNotificationRow = vm.VisibleNotifications[0];
        await vm.ReadNotificationCommand.ExecuteAsync();
        Assert.Equal(2, vm.VisibleNotifications.Count);
        Assert.False(vm.HasNotificationSelection); Assert.Null(vm.SelectedNotification);
        await vm.ReadAllNotificationsCommand.ExecuteAsync();
        Assert.True(vm.ShowNotificationEmpty);
        vm.ResetNotificationFilters();
        Assert.Equal(3, vm.VisibleNotifications.Count); Assert.Equal(0, vm.UnreadNotificationCount);
    }
    [Fact]
    public async System.Threading.Tasks.Task RefreshedDraftAndSelection_SurviveUnrelatedDelivery_AndSnoozeCannotRepeat()
    {
        var clock = new Clock();
        using var store = new PersonalTaskStore(new(_root), clock);
        var task = store.Create(new DesktopCreateTaskCommand("Договор", DesktopTaskPriority.Normal));
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: clock.Now.AddMinutes(-1));
        store.ReconcileReminders();
        using var vm = new PersonalPlanningViewModel(store, clock);
        vm.SelectedNotificationRow = vm.VisibleNotifications[0]; var id = vm.SelectedNotification!.Id;
        bool? busyWhenOpening = null;
        vm.OpenObjectRequested += (_, _) => busyWhenOpening = vm.IsBusy;
        await vm.OpenNotificationCommand.ExecuteAsync();
        Assert.False(busyWhenOpening); // The shell rejects navigation while Planning is busy.
        vm.Target = vm.Targets.Single(); vm.OffsetMinutes = "31"; vm.AbsoluteTime = "10.11.2026 10:30"; vm.Trigger = vm.Triggers.Last();
        var second = store.Create(new DesktopCreateTaskCommand("Материалы", DesktopTaskPriority.Normal));
        store.AddReminder(second.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: clock.Now.AddMinutes(-1)); store.ReconcileReminders();
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(id, vm.SelectedNotification?.Id); Assert.Equal(task.Id, vm.Target?.Id);
        Assert.Equal("31", vm.OffsetMinutes); Assert.Equal("10.11.2026 10:30", vm.AbsoluteTime); Assert.True(vm.IsAbsoluteReminder);
        vm.SnoozeChoice = "15 минут"; await vm.SnoozeNotificationCommand.ExecuteAsync();
        Assert.True(vm.SelectedNotification!.IsRead); Assert.False(vm.SnoozeNotificationCommand.CanExecute(null));
        Assert.Single(store.Reminders(), r => r.State == "pending");
    }
    [Fact]
    public void DateFilter_UsesLocalCalendarDays_AndNeverIncludesFutureReminderRows()
    {
        var clock = new Clock();
        using var store = new PersonalTaskStore(new(_root), clock);
        var task = store.Create(new DesktopCreateTaskCommand("История", DesktopTaskPriority.Normal));
        clock.Now = clock.Now.AddDays(-8);
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: clock.Now.AddMinutes(-1)); store.ReconcileReminders();
        clock.Now = clock.Now.AddDays(8);
        store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: clock.Now.AddDays(1));
        using var vm = new PersonalPlanningViewModel(store, clock);
        Assert.Single(vm.VisibleNotifications); Assert.Equal(2, vm.Reminders.Count);
        vm.NotificationPeriodFilter = "Последние 7 дней"; Assert.Empty(vm.VisibleNotifications);
        vm.ResetNotificationFilters(); Assert.Single(vm.VisibleNotifications);
        vm.NotificationQuery = "несуществующее"; Assert.Null(vm.SelectedNotificationRow); Assert.True(vm.ShowNotificationEmpty);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
