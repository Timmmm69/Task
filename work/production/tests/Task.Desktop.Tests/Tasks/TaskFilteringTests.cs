using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class TasksViewModelTests
{
    private sealed class FilterClock(DateTimeOffset? now = null, TimeZoneInfo? zone = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now ?? new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => zone ?? TimeZoneInfo.CreateCustomTimeZone("FilterUser", TimeSpan.FromHours(3), "FilterUser", "FilterUser");
    }

    [Theory]
    [InlineData(DesktopTaskStatus.New)]
    [InlineData(DesktopTaskStatus.InProgress)]
    [InlineData(DesktopTaskStatus.Review)]
    [InlineData(DesktopTaskStatus.Completed)]
    [InlineData(DesktopTaskStatus.Cancelled)]
    public async System.Threading.Tasks.Task Filter_UsesBusinessStatusEvenWhenDisplaySaysOverdue(DesktopTaskStatus status)
    {
        var tasks = Enum.GetValues<DesktopTaskStatus>().Select(value => CreateTask(status: value)
            with { DeadlineAtUtc = new FilterClock().GetUtcNow().AddDays(-1) }).ToArray();
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage(tasks));
        using var vm = new TasksViewModel(client, filterTimeProvider: new FilterClock());
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = status;
        Assert.Equal(status, Assert.Single(vm.Items).Source.Status);
        Assert.DoesNotContain(vm.StatusFilters, option => option.Name == "Просрочена");
        vm.SelectedStatusFilter = null;
        Assert.Equal(5, vm.Items.Count);
    }

    [Theory]
    [InlineData(DesktopTaskPriority.Low)]
    [InlineData(DesktopTaskPriority.Normal)]
    [InlineData(DesktopTaskPriority.High)]
    [InlineData(DesktopTaskPriority.Critical)]
    public async System.Threading.Tasks.Task Filter_AllPriorityValuesAndReset(DesktopTaskPriority priority)
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage(Enum.GetValues<DesktopTaskPriority>().Select(value => CreateTask(priority: value)).ToArray()));
        using var vm = new TasksViewModel(client);
        await vm.ActivateAsync();
        vm.SelectedPriorityFilter = priority;
        Assert.Equal(priority, Assert.Single(vm.Items).Source.Priority);
        await vm.ResetFiltersCommand.ExecuteAsync();
        Assert.Equal(4, vm.Items.Count);
        Assert.False(vm.HasActiveFilters);
    }

    private sealed class FilterOptionsClient : FakeTasksApiClient, IDesktopTaskWorkspaceClient
    {
        public JsonObject Options { get; set; } = new();
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetOptionsAsync(string query, CancellationToken token) =>
            System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(Options, 0, null));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> GetWorkspaceAsync(Guid id, CancellationToken token) =>
            System.Threading.Tasks.Task.FromResult(new TaskWorkspaceResult(null, 0, "Недоступно"));
        public System.Threading.Tasks.Task<TaskWorkspaceResult> WriteWorkspaceAsync(Guid id, long version, string path, HttpMethod method, JsonObject body, string key, CancellationToken token) =>
            throw new InvalidOperationException("Local filtering must never write.");
    }

    [Fact]
    public async System.Threading.Tasks.Task Filter_ProjectsUseIdsPreserveSelectionAndDoNotLeakMissingNames()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        var first = CreateTask() with { Card = new TaskCardContent { ProjectId = firstId } };
        var second = CreateTask() with { Card = new TaskCardContent { ProjectId = secondId } };
        var hidden = CreateTask() with { Card = new TaskCardContent { ProjectId = hiddenId } };
        var noProject = CreateTask();
        var client = new FilterOptionsClient
        {
            Options = new JsonObject { ["projects"] = new JsonArray(
                new JsonObject { ["id"] = firstId.ToString(), ["name"] = "Одинаковый" },
                new JsonObject { ["id"] = secondId.ToString(), ["name"] = "Одинаковый" }) }
        };
        client.EnqueuePage(SucceededPage([first, second, hidden, noProject], "next"));
        client.EnqueuePage(SucceededPage([first]));
        using var vm = new TasksViewModel(client);
        await vm.ActivateAsync();
        Assert.Equal(5, vm.ProjectFilters.Count);
        var names = vm.ProjectFilters.Where(option => option.Name.StartsWith("Одинаковый")).Select(option => option.Name).ToArray();
        Assert.Equal(2, names.Distinct().Count());
        vm.SelectedProjectFilter = vm.ProjectFilters.Single(option => option.ProjectId == firstId);
        Assert.Equal(first.Id, Assert.Single(vm.Items).Id);
        await vm.LoadNextPageAsync();
        Assert.Equal(firstId, vm.SelectedProjectFilter.ProjectId);
        Assert.Equal(first.Id, Assert.Single(vm.Items).Id);
        vm.SelectedProjectFilter = vm.ProjectFilters.Single(option => option.ProjectId == hiddenId);
        Assert.Equal("Проект назначен", vm.SelectedProjectFilter.Name);
        Assert.Equal(hidden.Id, Assert.Single(vm.Items).Id);
        Assert.DoesNotContain(hiddenId.ToString(), vm.SelectedProjectFilter.Name);
        vm.SelectedProjectFilter = vm.ProjectFilters.Single(option => !option.IsAll && option.ProjectId is null);
        Assert.Equal(noProject.Id, Assert.Single(vm.Items).Id);
        client.EnqueuePage(SucceededPage([first]));
        await vm.RefreshAsync();
        Assert.True(vm.SelectedProjectFilter.IsAll); // Removed option has an objective reason to reset.
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public async System.Threading.Tasks.Task Filter_SevenCalendarDatesIncludeTodayAndSixthDay(int offset, bool expected)
    {
        var day = new DateOnly(2026, 10, 17).AddDays(offset);
        var task = CreateTask() with { DeadlineAtUtc = null, Card = new TaskCardContent { DeadlineDate = day, DeadlineTimeZone = "UTC" } };
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, filterTimeProvider: new FilterClock());
        await vm.ActivateAsync();
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.NextSevenDays;
        Assert.Equal(expected, vm.Items.Count == 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task Filter_DeadlinePrecedenceTimezoneMidnightAndNulls()
    {
        var clock = new FilterClock(); // UTC Oct 16, user's calendar Oct 17.
        var today = CreateTask() with { DeadlineAtUtc = new DateTimeOffset(2026, 10, 16, 21, 0, 0, TimeSpan.Zero),
            Card = new TaskCardContent { DeadlineDate = new DateOnly(2026, 11, 1), DeadlineTimeZone = "UTC" } };
        var yesterday = CreateTask() with { DeadlineAtUtc = today.DeadlineAtUtc!.Value.AddTicks(-1) };
        var noDeadline = CreateTask() with { DeadlineAtUtc = null };
        var completed = today with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.Completed };
        var cancelled = today with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.Cancelled };
        var dateOnly = CreateTask() with { DeadlineAtUtc = null,
            Card = new TaskCardContent { DeadlineDate = new DateOnly(2026, 10, 16), DeadlineTimeZone = "UTC" } };
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([today, yesterday, noDeadline, completed, cancelled, dateOnly]));
        using var vm = new TasksViewModel(client, filterTimeProvider: clock);
        await vm.ActivateAsync();
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.Today;
        Assert.Equal(new[] { today.Id, completed.Id, cancelled.Id }, vm.Items.Select(item => item.Id));
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.Overdue;
        Assert.Equal(new[] { today.Id, yesterday.Id }, vm.Items.Select(item => item.Id));
        // Date-only Oct 16 is still today in its own UTC zone: domain rule says not overdue.
        Assert.False(TaskRules.IsOverdue(null, dateOnly.Card, clock.GetUtcNow()));
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.NoDeadline;
        Assert.Equal(noDeadline.Id, Assert.Single(vm.Items).Id);
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.NextSevenDays;
        Assert.DoesNotContain(vm.Items, item => item.Id == noDeadline.Id || item.Id == yesterday.Id);
    }

    [Theory]
    [InlineData("2026-03-08T06:59:59Z", true)]
    [InlineData("2026-03-08T07:00:00Z", true)]
    [InlineData("2026-03-09T03:59:59Z", true)]
    [InlineData("2026-03-09T04:00:00Z", false)]
    public async System.Threading.Tasks.Task Filter_UserCalendarHandlesDaylightSavingBoundary(string deadline, bool expected)
    {
        var clock = new FilterClock(DateTimeOffset.Parse("2026-03-08T05:00:00Z"), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        var task = CreateTask() with { DeadlineAtUtc = DateTimeOffset.Parse(deadline) };
        var client = new FakeTasksApiClient(); client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, filterTimeProvider: clock);
        await vm.ActivateAsync(); vm.SelectedDeadlineFilter = TaskDeadlineFilter.Today;
        Assert.Equal(expected, vm.Items.Count == 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task Filter_EmptyPageWithCursorStillAllowsContinuation()
    {
        var client = new FakeTasksApiClient(); client.EnqueuePage(SucceededPage([], "next"));
        client.EnqueuePage(SucceededPage([CreateTask(priority: DesktopTaskPriority.High)]));
        using var vm = new TasksViewModel(client); await vm.ActivateAsync();
        vm.SelectedPriorityFilter = DesktopTaskPriority.High;
        Assert.True(vm.IsFilteredEmpty); Assert.True(vm.LoadMoreCommand.CanExecute(null));
        await vm.LoadMoreCommand.ExecuteAsync(); Assert.Single(vm.Items);
    }

    [Fact]
    public async System.Threading.Tasks.Task Filter_CombinesAllConditionsPreservesSelectionAndRefreshes()
    {
        var project = Guid.NewGuid();
        var target = CreateTask(status: DesktopTaskStatus.InProgress, priority: DesktopTaskPriority.High) with
        { DeadlineAtUtc = null, Card = new TaskCardContent { ProjectId = project, DeadlineDate = new DateOnly(2026, 10, 17), DeadlineTimeZone = "UTC" } };
        var tasks = new[] { target, target with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.New },
            target with { Id = Guid.NewGuid(), Priority = DesktopTaskPriority.Low },
            target with { Id = Guid.NewGuid(), Card = target.Card with { ProjectId = Guid.NewGuid() } },
            target with { Id = Guid.NewGuid(), Card = target.Card with { DeadlineDate = null } } };
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage(tasks));
        client.EnqueuePage(SucceededPage(tasks));
        using var vm = new TasksViewModel(client, filterTimeProvider: new FilterClock());
        await vm.ActivateAsync();
        var selected = vm.SelectedItem;
        vm.SelectedStatusFilter = DesktopTaskStatus.InProgress;
        vm.SelectedPriorityFilter = DesktopTaskPriority.High;
        vm.SelectedProjectFilter = vm.ProjectFilters.Single(option => option.ProjectId == project);
        vm.SelectedDeadlineFilter = TaskDeadlineFilter.Today;
        Assert.Equal(target.Id, Assert.Single(vm.Items).Id);
        Assert.Same(selected, vm.SelectedItem);
        await vm.RefreshAsync();
        Assert.Equal(target.Id, Assert.Single(vm.Items).Id);
        Assert.Equal(DesktopTaskPriority.High, vm.SelectedPriorityFilter);
        Assert.Equal(TaskDeadlineFilter.Today, vm.SelectedDeadlineFilter);
        await vm.ResetFiltersCommand.ExecuteAsync();
        Assert.Null(vm.SelectedStatusFilter);
        Assert.Null(vm.SelectedPriorityFilter);
        Assert.True(vm.SelectedProjectFilter.IsAll);
        Assert.Equal(TaskDeadlineFilter.All, vm.SelectedDeadlineFilter);
        Assert.Equal(5, vm.Items.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task Filter_EmptyFirstPageCanContinueAndErrorRetainsResults()
    {
        var first = CreateTask(priority: DesktopTaskPriority.Low);
        var matching = CreateTask(priority: DesktopTaskPriority.Critical);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([first], "second"));
        client.EnqueuePage(SucceededPage([first, matching], "third"));
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.ServerUnavailable());
        client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.InvalidCursor());
        using var vm = new TasksViewModel(client);
        await vm.ActivateAsync();
        vm.SelectedPriorityFilter = DesktopTaskPriority.Critical;
        Assert.True(vm.IsFilteredEmpty);
        Assert.True(vm.LoadMoreCommand.CanExecute(null));
        Assert.StartsWith("Найдено среди загруженных задач: 0 из 1", vm.DisplayedCountText);
        Assert.Null(vm.SelectedItem);
        await vm.LoadNextPageAsync();
        Assert.Equal(matching.Id, Assert.Single(vm.Items).Id);
        await vm.LoadNextPageAsync();
        Assert.Equal(matching.Id, Assert.Single(vm.Items).Id);
        Assert.True(vm.HasNextPage);
        await vm.LoadNextPageAsync();
        Assert.Equal(TasksScreenState.InvalidCursor, vm.State);
        Assert.Contains("Результат неполный", vm.DisplayedCountText);
        vm.SelectedPriorityFilter = null;
        Assert.Equal(2, vm.Items.Count); // No duplicate from the second page.
        Assert.Equal(4, client.PageCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Filter_LocalOfflineReadOnlyDoesNotReadDetailsOrWrite(bool offline)
    {
        var first = CreateTask(priority: DesktopTaskPriority.Low);
        var second = CreateTask(priority: DesktopTaskPriority.High);
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([first, second]));
        using var vm = new TasksViewModel(client, ["Task.Read"]);
        await vm.ActivateAsync();
        var detailReads = client.DetailCallCount;
        if (offline) vm.UpdateConnectivity(false);
        vm.SelectedPriorityFilter = DesktopTaskPriority.High;
        Assert.Equal(second.Id, Assert.Single(vm.Items).Id);
        Assert.Null(vm.SelectedItem);
        await vm.ResetFiltersCommand.ExecuteAsync();
        Assert.Equal(1, client.PageCallCount);
        Assert.Equal(detailReads, client.DetailCallCount);
        Assert.Equal(0, client.CreateCallCount + client.PatchCallCount + client.TransitionCallCount);
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.NewTaskCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task Filter_CannotRestoreDataAfterReadDeniedOrSessionExpired(bool expired)
    {
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([CreateTask()]));
        client.EnqueuePage(expired ? new DesktopTasksApiResult<DesktopTaskPage>.AuthenticationFailure()
            : new DesktopTasksApiResult<DesktopTaskPage>.Forbidden());
        using var vm = new TasksViewModel(client, ["Task.Read"]);
        await vm.ActivateAsync();
        await vm.RefreshAsync();
        vm.SelectedPriorityFilter = DesktopTaskPriority.Critical;
        await vm.ResetFiltersCommand.ExecuteAsync();
        Assert.Empty(vm.Items);
        Assert.False(vm.IsFilteredEmpty);
        Assert.False(vm.HasNextPage);
        Assert.Equal(expired ? TasksScreenState.SessionEnded : TasksScreenState.Forbidden, vm.State);
        Assert.Equal(2, client.PageCallCount);
    }
}
