using Task.Desktop.ViewModels;
using AsyncTask = System.Threading.Tasks.Task;
using LinkResult = Task.Desktop.Work.DesktopWorkResult<Task.Desktop.Work.DesktopObjectLinks>;

namespace Task.Desktop.Tests.Work;

public sealed class ObjectLinksAccessRegressionTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async AsyncTask CancelledNonCooperativeOperationCannotPublishSuccess(bool mutation)
    {
        var client = new ObjectLinksIntegrationTests.Client();
        using var vm = ObjectLinksIntegrationTests.Model(client);
        var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        AsyncCommand command;
        if (mutation)
        {
            await ObjectLinksIntegrationTests.Select(vm);
            client.Write = _ => pending.Task; command = vm.AddCommand;
        }
        else { client.Read = _ => pending.Task; command = vm.RefreshCommand; }
        var operation = command.ExecuteAsync(); command.Cancel();
        pending.SetResult(new LinkResult.Succeeded(new([new(Guid.NewGuid(), source, Guid.NewGuid(), "task_file")], 8)));
        await operation; Assert.Empty(vm.Items); Assert.DoesNotContain("обновлены", vm.Message);
        if (mutation) Assert.False(vm.AddCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async AsyncTask InvisibleSourceReadRemovesCachedRelationsAndConfirmedTarget(bool disappeared)
    {
        var client = new ObjectLinksIntegrationTests.Client();
        using var vm = ObjectLinksIntegrationTests.Model(client);
        var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        client.Rows = [new(Guid.NewGuid(), source, Guid.NewGuid(), "task_file")];
        await vm.RefreshCommand.ExecuteAsync(); await ObjectLinksIntegrationTests.Select(vm);
        client.Read = _ => AsyncTask.FromResult<LinkResult>(disappeared ? new LinkResult.NotFound() : new LinkResult.Forbidden());
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Empty(vm.Items); Assert.Null(vm.Selected); Assert.Null(vm.TargetPicker!.SelectedTarget);
        Assert.False(vm.AddCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async AsyncTask SearchSessionExpiryAlsoInvalidatesLinksAndPendingRead()
    {
        var client = new ObjectLinksIntegrationTests.Client();
        using var vm = ObjectLinksIntegrationTests.Model(client);
        var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        client.Rows = [new(Guid.NewGuid(), source, Guid.NewGuid(), "task_file")];
        await vm.RefreshCommand.ExecuteAsync();
        var pending = new System.Threading.Tasks.TaskCompletionSource<LinkResult>();
        client.Read = _ => pending.Task;
        var read = vm.RefreshCommand.ExecuteAsync();
        client.Search = _ => AsyncTask.FromResult<Task.Desktop.Work.DesktopWorkResult<IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>>(
            new Task.Desktop.Work.DesktopWorkResult<IReadOnlyList<Task.Desktop.Work.DesktopSearchResult>>.AuthenticationFailure());
        vm.TargetPicker!.SearchText = "План"; await vm.TargetPicker.CurrentOperation;
        pending.SetResult(new LinkResult.Succeeded(new(client.Rows, 7))); await read;
        Assert.Empty(vm.Items); Assert.Null(vm.Selected);
        Assert.False(vm.RefreshCommand.CanExecute(null)); Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.Contains("Сессия", vm.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async AsyncTask AuthenticationFailureClearsRowsAndDisablesEveryCommandUntilSessionRestored(bool mutation)
    {
        var client = new ObjectLinksIntegrationTests.Client();
        using var vm = ObjectLinksIntegrationTests.Model(client);
        var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        client.Rows = [new(Guid.NewGuid(), source, Guid.NewGuid(), "task_file")];
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Single(vm.Items);
        if (mutation)
        {
            await ObjectLinksIntegrationTests.Select(vm);
            client.Write = _ => AsyncTask.FromResult<LinkResult>(new LinkResult.AuthenticationFailure());
            await vm.AddCommand.ExecuteAsync();
        }
        else
        {
            client.Read = _ => AsyncTask.FromResult<LinkResult>(new LinkResult.AuthenticationFailure());
            await vm.RefreshCommand.ExecuteAsync();
        }
        Assert.Empty(vm.Items); Assert.Null(vm.Selected);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.TargetPicker!.CanQuery);
        client.Read = null;
        vm.UpdateAccess(ObjectLinksIntegrationTests.Access);
        Assert.True(await vm.RefreshCommand.ExecuteAsync()); Assert.Single(vm.Items);
    }

    [Fact]
    public async AsyncTask ScopeRefreshClearsCachedRowsEvenWhenCapabilityNamesAreUnchanged()
    {
        var client = new ObjectLinksIntegrationTests.Client();
        using var vm = ObjectLinksIntegrationTests.Model(client);
        var source = Guid.NewGuid(); vm.SetSource(source, 7, "task");
        client.Rows = [new(Guid.NewGuid(), source, Guid.NewGuid(), "task_file")];
        await vm.RefreshCommand.ExecuteAsync();
        vm.UpdateAccess(ObjectLinksIntegrationTests.Access);
        Assert.Empty(vm.Items); Assert.Null(vm.Selected);
        Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.True(await vm.RefreshCommand.ExecuteAsync());
        Assert.Single(vm.Items);
    }
}
