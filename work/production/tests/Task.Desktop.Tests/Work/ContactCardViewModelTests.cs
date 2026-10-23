using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class ContactCardViewModelTests
{
    private static DesktopContactDetails Contact(string name = "Анна", long version = 3) =>
        new(new(Guid.NewGuid(), version, name, name, "Иванова", "Заметки", "inactive", "active"), "Петровна");
    private static DesktopWorkResult<DesktopContactDetails> Ok(DesktopContactDetails d) => new DesktopWorkResult<DesktopContactDetails>.Succeeded(d, d.Contact.Version);

    [Fact]
    public async System.Threading.Tasks.Task OpensFullCardAndSavesOnlyServerConfirmedValues()
    {
        var original = Contact();
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var api = new Client { Read = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(original)), Write = (_, _) => pending.Task };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id);
        Assert.Equal("Петровна", vm.MiddleName); Assert.Equal("Неактивный", vm.StatusLabel);
        await vm.EditCommand.ExecuteAsync(); vm.FirstName = "Новое"; vm.MiddleName = "Новое отчество";
        var saving = vm.SaveCommand.ExecuteAsync();
        Assert.Equal(original, vm.Details);
        Assert.False(await vm.SaveCommand.ExecuteAsync());
        Assert.False(vm.CancelCommand.CanExecute(null));
        Assert.Equal(3, api.Submitted!.Contact.Version);
        Assert.Equal("Новое отчество", api.Submitted.MiddleName);
        var confirmed = api.Submitted with { Contact = api.Submitted.Contact with { Version = 4, FirstName = "Сервер" } };
        pending.SetResult(Ok(confirmed)); await saving;
        Assert.Equal(confirmed, vm.Details); Assert.False(vm.IsEditing); Assert.Contains("сохранены", vm.Message);
        await vm.EditCommand.ExecuteAsync(); vm.FirstName = "Отменено"; await vm.CancelCommand.ExecuteAsync();
        Assert.Equal("Сервер", vm.FirstName);
        Assert.Equal(1, api.Writes);
    }

    [Theory]
    [InlineData("validation")][InlineData("403")][InlineData("404")][InlineData("412")][InlineData("offline")][InlineData("malformed")]
    public async System.Threading.Tasks.Task SaveFailureKeepsDraftAndNeverClaimsSuccess(string failure)
    {
        var original = Contact();
        DesktopWorkResult<DesktopContactDetails> result = failure switch
        {
            "validation" => new DesktopWorkResult<DesktopContactDetails>.ValidationFailure("Проверьте поля"),
            "403" => new DesktopWorkResult<DesktopContactDetails>.Forbidden(),
            "404" => new DesktopWorkResult<DesktopContactDetails>.NotFound(),
            "412" => new DesktopWorkResult<DesktopContactDetails>.Conflict(),
            "offline" => new DesktopWorkResult<DesktopContactDetails>.ServerUnavailable(),
            _ => new DesktopWorkResult<DesktopContactDetails>.MalformedResponse()
        };
        var api = new Client { Read = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(original)), Write = (_, _) => System.Threading.Tasks.Task.FromResult(result) };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); await vm.EditCommand.ExecuteAsync(); vm.Notes = "Несохранённый ввод";
        await vm.SaveCommand.ExecuteAsync();
        Assert.True(vm.IsEditing); Assert.Equal("Несохранённый ввод", vm.Notes);
        Assert.Equal(failure is "offline" or "malformed", vm.HasUnconfirmedMutation);
        Assert.DoesNotContain("Изменения контакта сохранены", vm.Message);
        if (failure is "412" or "403" or "404") Assert.False(vm.SaveCommand.CanExecute(null));
        if (failure == "412")
        {
            await vm.CancelCommand.ExecuteAsync(); Assert.False(vm.EditCommand.CanExecute(null));
            await vm.ReloadCommand.ExecuteAsync(); Assert.True(vm.EditCommand.CanExecute(null));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task LateReadsAndSavesCannotReplaceNewSelectionEvenIfClientIgnoresCancellation()
    {
        var first = Contact("Первый"); var second = Contact("Второй");
        var pendingRead = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var pendingSave = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>();
        var delay = true;
        var api = new Client { Read = (id, _) => delay && id == first.Contact.Id ? pendingRead.Task : System.Threading.Tasks.Task.FromResult(Ok(id == first.Contact.Id ? first : second)), Write = (_, _) => pendingSave.Task };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        var older = vm.SelectAsync(first.Contact.Id); await vm.SelectAsync(second.Contact.Id);
        pendingRead.SetResult(Ok(first)); await older;
        Assert.Equal(second, vm.Details); Assert.False(vm.IsLoading);
        delay = false; await vm.SelectAsync(first.Contact.Id); await vm.EditCommand.ExecuteAsync();
        var saving = vm.SaveCommand.ExecuteAsync(); await vm.SelectAsync(second.Contact.Id);
        pendingSave.SetResult(Ok(first)); await saving; Assert.Equal(second, vm.Details);
    }

    [Fact]
    public async System.Threading.Tasks.Task AccessChangesCancelRequestsClearPrivateDataAndDisableWrites()
    {
        var original = Contact(); var read = true; var online = true; var write = false;
        var api = new Client { Read = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(original)) };
        using var vm = new ContactCardViewModel(api, () => read, () => online, () => write);
        await vm.SelectAsync(original.Contact.Id); Assert.False(vm.EditCommand.CanExecute(null));
        write = true; vm.UpdateAccess(); await vm.EditCommand.ExecuteAsync(); vm.Notes = "Черновик";
        online = false; vm.UpdateAccess(); Assert.False(vm.SaveCommand.CanExecute(null)); Assert.Equal("Черновик", vm.Notes);
        read = false; vm.UpdateAccess(); Assert.Null(vm.Details); Assert.Empty(vm.Notes); Assert.False(vm.IsEditing);
        Assert.Equal(0, api.Writes);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationAndAuthenticationFailureDoNotProduceSuccess()
    {
        var original = Contact();
        var api = new Client { Read = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(original)),
            Write = async (_, ct) => { await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct); return Ok(original); } };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); await vm.EditCommand.ExecuteAsync(); vm.Notes = "Ввод";
        var saving = vm.SaveCommand.ExecuteAsync(); vm.SaveCommand.Cancel(); await saving;
        Assert.Equal("Ввод", vm.Notes); Assert.Contains("не подтверждён", vm.Message);
        api.Write = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.AuthenticationFailure());
        await vm.SaveCommand.ExecuteAsync(); Assert.Null(vm.Details); Assert.Empty(vm.Notes); Assert.Contains("Сессия", vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task InvalidDraftNeverReachesServerAndSameSelectionPreservesIt()
    {
        var original = Contact(); var api = new Client { Read = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(original)) };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); await vm.EditCommand.ExecuteAsync(); vm.FirstName = " ";
        await vm.SelectAsync(original.Contact.Id); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(" ", vm.FirstName); Assert.Equal(0, api.Writes); Assert.True(vm.IsEditing);
    }

    private sealed class Client : IDesktopContactDetailsClient
    {
        public required Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>>> Read { get; init; }
        public Func<DesktopContactDetails, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>>>? Write { get; set; }
        public DesktopContactDetails? Submitted { get; private set; }
        public int Writes { get; private set; }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default) => Read(id, cancellationToken);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails d, CancellationToken cancellationToken = default)
        { Writes++; Submitted = d; return Write?.Invoke(d, cancellationToken) ?? System.Threading.Tasks.Task.FromResult(Ok(d)); }
    }
}
