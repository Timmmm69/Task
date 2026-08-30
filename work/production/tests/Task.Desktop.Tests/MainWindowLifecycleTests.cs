namespace Task.Desktop.Tests;

public sealed class MainWindowLifecycleTests
{
    [Fact]
    public void UserClose_DirtyEditor_CanBeCancelled()
    {
        var confirmationCalls = 0;
        var cancelled = MainWindow.ShouldCancelClose(
            authenticationTransition: false,
            hasUnsavedChanges: true,
            () => { confirmationCalls++; return false; });

        Assert.True(cancelled);
        Assert.Equal(1, confirmationCalls);
    }

    [Fact]
    public void AuthenticationClose_DirtyEditor_CannotBeCancelled()
    {
        var confirmationCalls = 0;
        var cancelled = MainWindow.ShouldCancelClose(
            authenticationTransition: true,
            hasUnsavedChanges: true,
            () => { confirmationCalls++; return false; });

        Assert.False(cancelled);
        Assert.Equal(0, confirmationCalls);
    }
}
