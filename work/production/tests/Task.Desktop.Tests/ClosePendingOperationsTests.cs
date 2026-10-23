using Task.Desktop.Infrastructure;
using Task.Desktop.Work;
using Task.Desktop.Calendar;
using Task.Desktop.Projects;

namespace Task.Desktop.Tests;

public sealed class ClosePendingOperationsTests
{
    [Fact]
    public async System.Threading.Tasks.Task ActiveAndAmbiguousRequestsCannotBeClearedByAnotherSuccess()
    {
        var receipt = new ClosePendingOperations();
        var wait = new TaskCompletionSource<DesktopWorkResult<bool>>();
        var operation = receipt.TrackAsync(() => wait.Task);
        Assert.True(receipt.HasPending);
        await receipt.TrackAsync(() => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<bool>>(new DesktopWorkResult<bool>.Succeeded(true)));
        Assert.True(receipt.HasPending); // Another request cannot release an in-flight request.
        wait.SetResult(new DesktopWorkResult<bool>.ServerUnavailable()); await operation;
        Assert.True(receipt.HasPending);
        await receipt.TrackAsync(() => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<bool>>(new DesktopWorkResult<bool>.Succeeded(true)));
        Assert.True(receipt.HasPending); // Its identity does not prove the earlier outcome.
    }

    [Fact]
    public async System.Threading.Tasks.Task DefinitiveFailureReleasesReceiptWithoutChangingResult()
    {
        var receipt = new ClosePendingOperations();
        var failure = new DesktopWorkResult<bool>.ValidationFailure("Ошибка поля");
        Assert.Same(failure, await receipt.TrackAsync(() => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<bool>>(failure)));
        Assert.False(receipt.HasPending);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationOrExceptionRetainsUnknownOutcome()
    {
        var receipt = new ClosePendingOperations();
        await Assert.ThrowsAsync<OperationCanceledException>(() => receipt.TrackAsync<bool>(
            () => System.Threading.Tasks.Task.FromException<DesktopWorkResult<bool>>(new OperationCanceledException())));
        Assert.True(receipt.HasPending);
    }

    [Fact]
    public async System.Threading.Tasks.Task CalendarAndProjectMalformedRepliesRetainReceipt()
    {
        var calendar = new ClosePendingOperations();
        await calendar.TrackAsync(() => System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<bool>>(new DesktopCalendarResult<bool>.MalformedResponse()));
        Assert.True(calendar.HasPending);
        var projects = new ClosePendingOperations();
        await projects.TrackAsync(() => System.Threading.Tasks.Task.FromResult<DesktopProjectResult<object>>(new DesktopProjectResult<object>.MalformedResponse()));
        Assert.True(projects.HasPending);
    }
}
