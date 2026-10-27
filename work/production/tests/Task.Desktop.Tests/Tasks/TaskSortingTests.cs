using System.Globalization;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class TasksViewModelTests
{
    private static readonly DateTimeOffset SortTime = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static DesktopTaskDto SortTask(int id, string title = "Задача") => CreateTask(title) with
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"),
        CreatedAtUtc = SortTime.AddDays(-1),
        UpdatedAtUtc = SortTime,
        DeadlineAtUtc = null,
    };

    [Theory]
    [InlineData(TaskSortOrder.UpdatedNewest, 3, 2, 1)]
    [InlineData(TaskSortOrder.UpdatedOldest, 1, 2, 3)]
    [InlineData(TaskSortOrder.DeadlineNearest, 1, 2, 3)]
    [InlineData(TaskSortOrder.DeadlineLatest, 3, 2, 1)]
    [InlineData(TaskSortOrder.PriorityHighest, 3, 2, 1)]
    [InlineData(TaskSortOrder.PriorityLowest, 1, 2, 3)]
    [InlineData(TaskSortOrder.TitleAscending, 1, 2, 3)]
    [InlineData(TaskSortOrder.TitleDescending, 3, 2, 1)]
    public async System.Threading.Tasks.Task Sort_AllEightDirections(TaskSortOrder order, int a, int b, int c)
    {
        var tasks = new[]
        {
            SortTask(1, "Арбуз") with { UpdatedAtUtc = SortTime.AddDays(-1), DeadlineAtUtc = SortTime, Priority = DesktopTaskPriority.Low },
            SortTask(2, "Ёлка") with { DeadlineAtUtc = SortTime.AddDays(1), Priority = DesktopTaskPriority.Normal },
            SortTask(3, "Яблоко") with { UpdatedAtUtc = SortTime.AddDays(1), DeadlineAtUtc = SortTime.AddDays(2), Priority = DesktopTaskPriority.Critical },
        };
        var client = new FakeTasksApiClient(); client.EnqueuePage(SucceededPage([tasks[1], tasks[2], tasks[0]]));
        using var vm = new TasksViewModel(client); await vm.ActivateAsync();
        vm.SelectedSortOrder = order;
        Assert.Equal(new[] { tasks[a - 1].Id, tasks[b - 1].Id, tasks[c - 1].Id }, vm.Items.Select(row => row.Id));
        Assert.Equal(8, vm.SortOrders.Count);
        Assert.Equal(1, client.PageCallCount);
    }

    [Theory]
    [InlineData(TaskSortOrder.UpdatedNewest)]
    [InlineData(TaskSortOrder.UpdatedOldest)]
    [InlineData(TaskSortOrder.DeadlineNearest)]
    [InlineData(TaskSortOrder.DeadlineLatest)]
    [InlineData(TaskSortOrder.PriorityHighest)]
    [InlineData(TaskSortOrder.PriorityLowest)]
    [InlineData(TaskSortOrder.TitleAscending)]
    [InlineData(TaskSortOrder.TitleDescending)]
    public void Sort_EqualValuesUseIdRegardlessOfInputOrder(TaskSortOrder order)
    {
        var first = SortTask(1); var second = SortTask(2);
        foreach (var input in new[] { new[] { second, first }, new[] { first, second } })
            Assert.Equal(new[] { first.Id, second.Id }, TaskSorting.Apply(input, order, TimeZoneInfo.Utc).Select(task => task.Id));
    }

    [Theory]
    [InlineData(TaskSortOrder.UpdatedNewest)]
    [InlineData(TaskSortOrder.UpdatedOldest)]
    [InlineData(TaskSortOrder.DeadlineNearest)]
    [InlineData(TaskSortOrder.DeadlineLatest)]
    public void Sort_NullDatesAlwaysLast(TaskSortOrder order)
    {
        var missing1 = SortTask(1) with { UpdatedAtUtc = null };
        var missing2 = SortTask(2) with { UpdatedAtUtc = null };
        var dated = SortTask(3) with { DeadlineAtUtc = SortTime };
        Assert.Equal(new[] { dated.Id, missing1.Id, missing2.Id },
            TaskSorting.Apply([missing2, dated, missing1], order, TimeZoneInfo.Utc).Select(task => task.Id));
    }

    [Fact]
    public void Sort_PriorityUsesBusinessOrderAndItsReverse()
    {
        var tasks = Enum.GetValues<DesktopTaskPriority>().Select((priority, i) => SortTask(i + 1) with { Priority = priority }).ToArray();
        Assert.Equal(new[] { DesktopTaskPriority.Critical, DesktopTaskPriority.High, DesktopTaskPriority.Normal, DesktopTaskPriority.Low },
            TaskSorting.Apply(tasks, TaskSortOrder.PriorityHighest, TimeZoneInfo.Utc).Select(task => task.Priority));
        Assert.Equal(tasks.Select(task => task.Priority), TaskSorting.Apply(tasks, TaskSortOrder.PriorityLowest, TimeZoneInfo.Utc).Select(task => task.Priority));
    }

    [Fact]
    public void Sort_RussianTitlesIgnoreOsCultureAndKeepUnusualInputUnmodified()
    {
        var before = CultureInfo.CurrentCulture;
        var tasks = new[] { SortTask(4, "Я"), SortTask(3, "ёж"), SortTask(2, "Арбуз"), SortTask(1, "арбуз"),
            SortTask(6, ""), SortTask(5, null!), SortTask(7, "🚀"), SortTask(8, new string('Ж', 10000)) };
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var ascending = TaskSorting.Apply(tasks, TaskSortOrder.TitleAscending, TimeZoneInfo.Utc).ToArray();
            Assert.Equal(new[] { tasks[5].Id, tasks[4].Id }, ascending.Take(2).Select(task => task.Id));
            Assert.True(Array.IndexOf(ascending, tasks[3]) < Array.IndexOf(ascending, tasks[2]));
            Assert.True(Array.IndexOf(ascending, tasks[2]) < Array.IndexOf(ascending, tasks[1]));
            Assert.True(Array.IndexOf(ascending, tasks[1]) < Array.IndexOf(ascending, tasks[0]));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(ascending, TaskSorting.Apply(tasks, TaskSortOrder.TitleAscending, TimeZoneInfo.Utc));
            Assert.Null(tasks[5].Title); Assert.Equal("арбуз", tasks[3].Title);
            Assert.Equal(tasks.Length, TaskSorting.Apply(tasks, TaskSortOrder.TitleDescending, TimeZoneInfo.Utc).Count());
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Sort_MixedDeadlinesUseNormativeInstantPrecedenceAndCalendarDates()
    {
        var zone = new FilterClock().LocalTimeZone; // UTC+3
        var instant = SortTask(1) with
        {
            DeadlineAtUtc = new DateTimeOffset(2026, 10, 16, 21, 30, 0, TimeSpan.Zero),
            Card = new TaskCardContent { DeadlineDate = new DateOnly(2026, 12, 1), DeadlineTimeZone = "UTC" }
        };
        var date = SortTask(2) with { Card = new TaskCardContent { DeadlineDate = new DateOnly(2026, 10, 17), DeadlineTimeZone = "UTC" } };
        var earlier = SortTask(3) with { Card = date.Card! with { DeadlineDate = new DateOnly(2026, 10, 16) } };
        var later = SortTask(4) with { DeadlineAtUtc = instant.DeadlineAtUtc!.Value.AddHours(1) };
        var missing = SortTask(5);
        var tasks = new[] { missing, date, later, earlier, instant };
        Assert.Equal(new[] { earlier.Id, instant.Id, later.Id, date.Id, missing.Id },
            TaskSorting.Apply(tasks, TaskSortOrder.DeadlineNearest, zone).Select(task => task.Id));
        Assert.Equal(new[] { date.Id, later.Id, instant.Id, earlier.Id, missing.Id },
            TaskSorting.Apply(tasks, TaskSortOrder.DeadlineLatest, zone).Select(task => task.Id));
        Assert.Equal(new DateOnly(2026, 12, 1), instant.Card!.DeadlineDate);
        Assert.Equal(new DateOnly(2026, 10, 17), date.Card!.DeadlineDate);
    }

    [Fact]
    public void Sort_ExactInstantsKeepOrderAcrossDstFoldAndDifferentOffsets()
    {
        var first = SortTask(1) with { DeadlineAtUtc = DateTimeOffset.Parse("2026-11-01T01:45:00-04:00") };
        var second = SortTask(2) with { DeadlineAtUtc = DateTimeOffset.Parse("2026-11-01T01:15:00-05:00") };
        var equal = SortTask(3) with { DeadlineAtUtc = second.DeadlineAtUtc!.Value.ToOffset(TimeSpan.FromHours(3)), UpdatedAtUtc = second.UpdatedAtUtc!.Value.ToOffset(TimeSpan.FromHours(-5)) };
        Assert.Equal(new[] { first.Id, second.Id, equal.Id }, TaskSorting.Apply([equal, second, first], TaskSortOrder.DeadlineNearest,
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).Select(task => task.Id));
        Assert.Equal(new[] { second.Id, equal.Id }, TaskSorting.Apply([equal, second], TaskSortOrder.UpdatedNewest, TimeZoneInfo.Utc).Select(task => task.Id));
    }

    [Fact]
    public async System.Threading.Tasks.Task Sort_FiltersResetSelectionDetailsAndSessionStayLocal()
    {
        var target = SortTask(2, "Яблоко") with { Priority = DesktopTaskPriority.High, Card = new TaskCardContent { ProjectId = SortTask(9).Id } };
        var other = target with { Id = SortTask(1).Id, Title = "Арбуз" };
        var excluded = SortTask(3, "Абрикос");
        var client = new FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(target) };
        client.EnqueuePage(SucceededPage([target, excluded, other]));
        client.EnqueuePage(SucceededPage([other, target, excluded]));
        using var vm = new TasksViewModel(client, ["Task.Read"]); await vm.ActivateAsync();
        vm.SelectedItem = vm.Items.Single(row => row.Id == target.Id);
        var selected = vm.SelectedItem; var details = vm.SelectedDetails; var reads = client.DetailCallCount;
        vm.SelectedPriorityFilter = DesktopTaskPriority.High;
        vm.SelectedProjectFilter = vm.ProjectFilters.Single(option => option.ProjectId == target.Card!.ProjectId);
        vm.UpdateConnectivity(false);
        foreach (var option in vm.SortOrders) vm.SelectedSortOrder = option.Value;
        vm.SelectedSortOrder = TaskSortOrder.TitleAscending;
        Assert.Equal(new[] { other.Id, target.Id }, vm.Items.Select(row => row.Id));
        Assert.Same(selected, vm.SelectedItem); Assert.Same(details, vm.SelectedDetails);
        Assert.Equal(reads, client.DetailCallCount); Assert.Equal(1, client.PageCallCount);
        Assert.True(vm.IsReadOnly);
        await vm.ResetFiltersCommand.ExecuteAsync();
        Assert.Equal(TaskSortOrder.TitleAscending, vm.SelectedSortOrder);
        Assert.Equal(3, vm.Items.Count); Assert.Same(selected, vm.SelectedItem);
        vm.UpdateConnectivity(true); await vm.RefreshAsync();
        Assert.Equal(target.Id, vm.SelectedItem?.Id); Assert.Equal(TaskSortOrder.TitleAscending, vm.SelectedSortOrder);
        vm.Deactivate(); await vm.ActivateAsync();
        Assert.Equal(TaskSortOrder.TitleAscending, vm.SelectedSortOrder);
        Assert.Equal(0, client.CreateCallCount + client.PatchCallCount + client.TransitionCallCount);
        vm.SelectedSortOrder = (TaskSortOrder)999; // Invalid input cannot erase the loaded list.
        Assert.Equal(3, vm.Items.Count); Assert.Equal(TaskSortOrder.TitleAscending, vm.SelectedSortOrder);
    }

    [Fact]
    public async System.Threading.Tasks.Task Sort_PageMergeDeduplicatesKeepsNewerVersionAndRecoversFromNetworkFailure()
    {
        var first = SortTask(2, "Б") with { Version = 5 }; var next = SortTask(1, "А");
        var client = new FakeTasksApiClient { DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(first) };
        client.EnqueuePage(SucceededPage([first, first with { Version = 1, Title = "Устарела" }], "next"));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        client.EnqueuePage(SucceededPage([first with { Version = 2, Title = "Устарела" }, next, next]));
        using var vm = new TasksViewModel(client); await vm.ActivateAsync();
        vm.SelectedSortOrder = TaskSortOrder.TitleAscending;
        var selected = vm.SelectedItem;
        await vm.LoadNextPageAsync();
        Assert.Equal(first.Id, Assert.Single(vm.Items).Id); Assert.True(vm.HasNextPage);
        vm.SelectedSortOrder = TaskSortOrder.TitleDescending;
        Assert.Same(selected, vm.SelectedItem);
        await vm.LoadNextPageAsync(); vm.SelectedSortOrder = TaskSortOrder.TitleAscending;
        Assert.Equal(new[] { next.Id, first.Id }, vm.Items.Select(row => row.Id));
        Assert.Equal(5, vm.Items.Single(row => row.Id == first.Id).Source.Version);
        Assert.Equal(first.Id, vm.SelectedItem?.Id); Assert.False(vm.HasNextPage);
        Assert.Equal(3, client.PageCallCount);
    }

    [Theory]
    [InlineData(TaskSortOrder.PriorityHighest)]
    [InlineData(TaskSortOrder.TitleDescending)]
    [InlineData(TaskSortOrder.DeadlineNearest)]
    [InlineData(TaskSortOrder.UpdatedNewest)]
    public async System.Threading.Tasks.Task Sort_ConfirmedSaveRepositionsWithoutDuplicates(TaskSortOrder order)
    {
        var edited = SortTask(1, "А") with { UpdatedAtUtc = SortTime.AddDays(-1), Priority = DesktopTaskPriority.Low, DeadlineAtUtc = SortTime.AddDays(3) };
        var other = SortTask(2, "Б") with { DeadlineAtUtc = SortTime.AddDays(2) };
        var saved = edited with { Version = 2, Title = "Я", Priority = DesktopTaskPriority.Critical, DeadlineAtUtc = SortTime, UpdatedAtUtc = SortTime.AddDays(1) };
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(edited),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(saved, 2, false)
        };
        client.EnqueuePage(SucceededPage([other, edited], "next"));
        client.EnqueuePage(SucceededPage([edited])); // A stale cursor snapshot after save.
        using var vm = new TasksViewModel(client, WriteCapabilities); await vm.ActivateAsync();
        vm.SelectedSortOrder = order;
        Assert.Equal(other.Id, vm.Items[0].Id);
        vm.SelectedItem = vm.Items.Single(row => row.Id == edited.Id);
        await vm.EditTaskCommand.ExecuteAsync(); vm.Editor!.Title = "Я";
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Equal(saved.Id, vm.Items[0].Id); Assert.Equal(2, vm.Items.Count);
        Assert.Equal(saved, vm.SelectedItem!.Source); Assert.Equal(saved.Id, vm.SelectedDetails?.Id);
        await vm.LoadNextPageAsync();
        Assert.Equal(saved, vm.Items[0].Source); Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task Sort_FilteredSaveAndRemovedSelectionAreSafe()
    {
        var task = SortTask(1) with { Priority = DesktopTaskPriority.High };
        var saved = task with { Version = 2, Priority = DesktopTaskPriority.Low, Card = new TaskCardContent { ProjectId = SortTask(8).Id }, Status = DesktopTaskStatus.InProgress };
        var client = new FakeTasksApiClient
        {
            DetailResult = new DesktopTasksApiResult<DesktopTaskDto>.Succeeded(task),
            PatchResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(saved, 2, false)
        };
        client.EnqueuePage(SucceededPage([task])); client.EnqueuePage(SucceededPage([]));
        using var vm = new TasksViewModel(client, WriteCapabilities); await vm.ActivateAsync();
        vm.SelectedPriorityFilter = DesktopTaskPriority.High; vm.SelectedSortOrder = TaskSortOrder.PriorityHighest;
        await vm.EditTaskCommand.ExecuteAsync(); vm.Editor!.Priority = vm.Editor.Priorities.Single(option => option.Value == DesktopTaskPriority.Low);
        await vm.SaveEditorCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem); Assert.Null(vm.SelectedDetails);
        await vm.ResetFiltersCommand.ExecuteAsync(); Assert.Equal(saved, Assert.Single(vm.Items).Source);
        vm.SelectedItem = vm.Items[0];
        await vm.RefreshAsync(); Assert.Empty(vm.Items); Assert.Null(vm.SelectedItem); Assert.Null(vm.SelectedDetails);
        Assert.Equal(TaskSortOrder.PriorityHighest, vm.SelectedSortOrder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Sort_CannotResurrectForbiddenOrExpiredData(bool expired)
    {
        var client = new FakeTasksApiClient(); client.EnqueuePage(SucceededPage([SortTask(1)], "next"));
        client.EnqueuePage(expired ? new DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure() : new DesktopTasksApiResult<DesktopTaskPage>.Forbidden());
        using var vm = new TasksViewModel(client, ["Task.Read"]); await vm.ActivateAsync();
        await vm.LoadNextPageAsync();
        foreach (var option in vm.SortOrders) { vm.SelectedSortOrder = option.Value; Assert.Empty(vm.Items); }
        Assert.Null(vm.SelectedItem); Assert.Null(vm.SelectedDetails); Assert.False(vm.HasNextPage);
        Assert.Equal(2, client.PageCallCount);
    }
}
