using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.TaskScreen;

public sealed partial class TasksViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task Completion_WaitsForServer_ThenRetainsFilteredRowWithoutBlockingInput()
    {
        var task = CreateTask(status: DesktopTaskStatus.InProgress);
        var completed = task with { Status = DesktopTaskStatus.Completed, Version = 2 };
        var response = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var finish = new TaskCompletionSource();
        var client = new FakeTasksApiClient { TransitionHandler = _ => response.Task };
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true, _ => finish.Task);
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = "В работе";
        await vm.TransitionCommand.ExecuteAsync("Completed");
        var request = vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Null(Assert.Single(vm.Items).CompletionFeedback);
        Assert.Equal(DesktopTaskStatus.InProgress, vm.SelectedItem!.Source.Status);
        response.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(completed, 2, false));
        await request;
        var row = Assert.Single(vm.Items);
        var feedback = Assert.IsType<TaskCompletionFeedback>(row.CompletionFeedback);
        Assert.Equal("Завершена", row.StatusText);
        Assert.False(vm.IsMutationBusy);
        Assert.Contains("Завершена", vm.Announcement);
        finish.SetResult();
        await WaitForAsync(() => vm.Items.Count == 0);
        Assert.False(feedback.IsActive);
        Assert.Null(vm.SelectedItem);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("conflict")]
    [InlineData("elsewhere")]
    public async global::System.Threading.Tasks.Task Completion_FailureOrOtherStatus_DoesNotPresentFeedback(string outcome)
    {
        var task = CreateTask(status: DesktopTaskStatus.InProgress);
        var client = new FakeTasksApiClient
        {
            TransitionResult = outcome switch
            {
                "error" => new DesktopTaskWriteResult<DesktopTaskDto>.ServerUnavailable(),
                "conflict" => new DesktopTaskWriteResult<DesktopTaskDto>.VersionConflict(),
                _ => new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(task with { Version = 2 }, 2, false)
            }
        };
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true);
        var count = 0;
        vm.CompletionPresented += _ => count++;
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Equal(0, count);
        Assert.Null(Assert.Single(vm.Items).CompletionFeedback);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Completion_ReplayedSuccessAndDoubleClick_PresentOnlyOnce()
    {
        var task = CreateTask(status: DesktopTaskStatus.InProgress);
        var completed = task with { Status = DesktopTaskStatus.Completed, Version = 2 };
        var response = new TaskCompletionSource<DesktopTaskWriteResult<DesktopTaskDto>>();
        var finish = new TaskCompletionSource();
        var client = new FakeTasksApiClient { TransitionHandler = _ => response.Task };
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true, _ => finish.Task);
        var count = 0;
        vm.CompletionPresented += _ => count++;
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("Completed");
        var request = vm.ConfirmTransitionCommand.ExecuteAsync();
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        response.SetResult(new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(completed, 2, true));
        await request;
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Equal(1, count);
        Assert.Equal(1, client.TransitionCallCount);
        // Even a stale page followed by the same idempotent response cannot replay the pulse.
        client.EnqueuePage(SucceededPage([task]));
        await vm.RefreshAsync();
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.Equal(1, count);
        finish.SetResult();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Completion_ReducedMotion_IsImmediateStaticAnnouncement()
    {
        var task = CreateTask(status: DesktopTaskStatus.InProgress);
        var client = new FakeTasksApiClient
        {
            TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
                task with { Status = DesktopTaskStatus.Completed, Version = 2 }, 2, false)
        };
        client.EnqueuePage(SucceededPage([task]));
        var delayed = false;
        using var vm = new TasksViewModel(client, WriteCapabilities, () => false, _ =>
        {
            delayed = true;
            return global::System.Threading.Tasks.Task.CompletedTask;
        });
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = "В работе";
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        Assert.False(delayed);
        Assert.Empty(vm.Items);
        Assert.Contains("Завершена", vm.Announcement);
    }

    [Fact]
    public void CompletionToken_IsIdentityBoundSingleUseAndResets()
    {
        var id = Guid.NewGuid();
        var feedback = new TaskCompletionFeedback(id, 2, true);
        Assert.False(feedback.TryPlay(Guid.NewGuid(), 2));
        Assert.False(feedback.TryPlay(id, 3));
        Assert.True(feedback.TryPlay(id, 2));
        Assert.False(feedback.TryPlay(id, 2));
        feedback.Reset();
        Assert.False(feedback.TryPlay(id, 2));
        Assert.False(new TaskCompletionFeedback(id, 2, false).TryPlay(id, 2));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Completion_RefreshRemovalAndDeactivate_DoNotResurrectRows()
    {
        var task = CreateTask(status: DesktopTaskStatus.InProgress);
        var finish = new TaskCompletionSource();
        var client = new FakeTasksApiClient
        {
            TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
                task with { Status = DesktopTaskStatus.Completed, Version = 2 }, 2, false)
        };
        client.EnqueuePage(SucceededPage([task]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true, _ => finish.Task);
        await vm.ActivateAsync();
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        var feedback = Assert.Single(vm.Items).CompletionFeedback!;
        client.EnqueuePage(SucceededPage([]));
        await vm.RefreshAsync();
        Assert.Empty(vm.Items);
        vm.Deactivate();
        Assert.False(feedback.IsActive);
        finish.SetResult();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CompletionExpiry_PreservesAnotherTasksPendingConfirmation()
    {
        var first = CreateTask("Первая", DesktopTaskStatus.InProgress);
        var second = CreateTask("Вторая", DesktopTaskStatus.InProgress);
        var finish = new TaskCompletionSource();
        var client = new FakeTasksApiClient
        {
            TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
                first with { Status = DesktopTaskStatus.Completed, Version = 2 }, 2, false)
        };
        client.EnqueuePage(SucceededPage([first, second]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true, _ => finish.Task);
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = "В работе";
        await vm.TransitionCommand.ExecuteAsync("Completed");
        await vm.ConfirmTransitionCommand.ExecuteAsync();
        vm.SelectedItem = vm.Items.Single(row => row.Id == second.Id);
        var selected = vm.SelectedItem;
        await vm.TransitionCommand.ExecuteAsync("Completed");
        finish.SetResult();
        await WaitForAsync(() => vm.Items.Count == 1);
        Assert.Same(selected, vm.SelectedItem);
        Assert.Equal(DesktopTaskStatus.Completed, vm.PendingTransition);
        Assert.True(vm.ConfirmTransitionCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Completion_DifferentTasks_HaveIndependentFeedbackAndPredictableSelection()
    {
        var first = CreateTask("Первая", DesktopTaskStatus.InProgress);
        var second = CreateTask("Вторая", DesktopTaskStatus.InProgress);
        var finishes = new Queue<TaskCompletionSource>();
        var client = new FakeTasksApiClient();
        client.EnqueuePage(SucceededPage([first, second]));
        using var vm = new TasksViewModel(client, WriteCapabilities, () => true, _ =>
        {
            var finish = new TaskCompletionSource();
            finishes.Enqueue(finish);
            return finish.Task;
        });
        await vm.ActivateAsync();
        vm.SelectedStatusFilter = "В работе";
        foreach (var task in new[] { first, second })
        {
            vm.SelectedItem = vm.Items.Single(row => row.Id == task.Id);
            client.TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
                task with { Status = DesktopTaskStatus.Completed, Version = 2 }, 2, false);
            await vm.TransitionCommand.ExecuteAsync("Completed");
            await vm.ConfirmTransitionCommand.ExecuteAsync();
        }
        Assert.Equal(2, vm.Items.Count(row => row.CompletionFeedback is not null));
        Assert.False(vm.IsMutationBusy);
        finishes.Dequeue().SetResult();
        await WaitForAsync(() => vm.Items.Count == 1);
        Assert.Equal(second.Id, vm.SelectedItem!.Id);
        finishes.Dequeue().SetResult();
        await WaitForAsync(() => vm.Items.Count == 0);
    }
}
