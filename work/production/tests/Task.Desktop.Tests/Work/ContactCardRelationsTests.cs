using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class ContactCardRelationsTests
{
    private static DesktopContactDetails Details(Guid? id = null, long version = 7) => new(new(id ?? Guid.NewGuid(), version, "Анна", "Анна", null, null, "active", "active"), null)
    {
        Channels = [new(Guid.NewGuid(), "phone", "value")], Addresses = [new(Guid.NewGuid(), "work", "Минск")]
    };
    private static DesktopWorkResult<DesktopContactDetails> Ok(DesktopContactDetails details) => new DesktopWorkResult<DesktopContactDetails>.Succeeded(details, details.Contact.Version);

    [Fact]
    public async System.Threading.Tasks.Task OneMutationAtATimeAndNextActionUsesConfirmedParentVersion()
    {
        var original = Details(); var api = new Client(original);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>(); api.Change = (_, _) => pending.Task;
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); Assert.Single(vm.Channels); Assert.Single(vm.Addresses);
        await vm.AddChannelCommand.ExecuteAsync(); vm.ChannelDraft!.Value = "draft";
        var save = vm.SaveRelationCommand.ExecuteAsync();
        Assert.Equal(original.Channels, vm.Channels); Assert.False(vm.CanChangeRelationDraft);
        Assert.False(await vm.SaveRelationCommand.ExecuteAsync()); Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.AddAddressCommand.CanExecute(null)); Assert.False(vm.CancelRelationCommand.CanExecute(null)); Assert.False(vm.ReloadCommand.CanExecute(null));
        var confirmed = original with { Contact = original.Contact with { Version = 8 }, Channels = [new(Guid.NewGuid(), "email", "server")] };
        pending.SetResult(Ok(confirmed)); await save;
        Assert.Equal("server", Assert.Single(vm.Channels).Value); Assert.False(vm.HasRelationDraft);
        api.Change = (_, _) => System.Threading.Tasks.Task.FromResult(Ok(confirmed with { Contact = confirmed.Contact with { Version = 9 } }));
        await vm.EditChannelCommand.ExecuteAsync(vm.Channels[0]); vm.ChannelDraft!.Value = "edited"; await vm.SaveRelationCommand.ExecuteAsync();
        Assert.Equal(new long[] { 7, 8 }, api.Versions); Assert.Equal(9, vm.Details!.Contact.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteNeedsConfirmationAndAddressOnlyOffersAdd()
    {
        var original = Details(); var api = new Client(original);
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id);
        await vm.RemoveChannelCommand.ExecuteAsync(vm.Channels[0]); Assert.True(vm.IsConfirmingChannelDelete); Assert.Empty(api.Versions);
        await vm.CancelRelationCommand.ExecuteAsync(); Assert.Single(vm.Channels);
        await vm.RemoveChannelCommand.ExecuteAsync(vm.Channels[0]); await vm.ConfirmChannelDeleteCommand.ExecuteAsync();
        Assert.Equal("delete", api.Operation); Assert.False(vm.IsConfirmingChannelDelete);
        await vm.AddAddressCommand.ExecuteAsync(); vm.AddressDraft!.Value = "Минск"; await vm.SaveRelationCommand.ExecuteAsync();
        Assert.Equal("address", api.Operation); Assert.Equal(new long[] { 7, 8 }, api.Versions);
    }

    [Theory]
    [InlineData("403")][InlineData("404")][InlineData("412")][InlineData("offline")][InlineData("malformed")][InlineData("duplicate")]
    public async System.Threading.Tasks.Task FailuresKeepDraftWithoutConfirmingOrUsingStaleVersion(string failure)
    {
        var original = Details(); var api = new Client(original) { Change = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(failure switch
        {
            "403" => new DesktopWorkResult<DesktopContactDetails>.Forbidden(), "404" => new DesktopWorkResult<DesktopContactDetails>.NotFound(),
            "412" => new DesktopWorkResult<DesktopContactDetails>.Conflict(), "offline" => new DesktopWorkResult<DesktopContactDetails>.ServerUnavailable(),
            "duplicate" => new DesktopWorkResult<DesktopContactDetails>.ValidationFailure("Проверьте дубликаты"), _ => new DesktopWorkResult<DesktopContactDetails>.MalformedResponse()
        }) };
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); await vm.AddChannelCommand.ExecuteAsync(); vm.ChannelDraft!.Value = "draft";
        await vm.SaveRelationCommand.ExecuteAsync(); Assert.DoesNotContain("подтверждены сервером", vm.Message);
        if (failure == "404") { Assert.Null(vm.Details); Assert.Null(vm.ChannelDraft); return; }
        Assert.Equal("draft", vm.ChannelDraft!.Value); Assert.Equal(7, vm.Details!.Contact.Version);
        if (failure != "duplicate")
        {
            Assert.False(vm.SaveRelationCommand.CanExecute(null)); await vm.CancelRelationCommand.ExecuteAsync();
            Assert.False(vm.AddChannelCommand.CanExecute(null)); await vm.ReloadCommand.ExecuteAsync(); Assert.True(vm.AddChannelCommand.CanExecute(null));
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task LocalValidationAndReadOnlyOfflineNeverSendChanges()
    {
        var original = Details(); var api = new Client(original); var write = false; var online = true;
        using var vm = new ContactCardViewModel(api, () => true, () => online, () => write);
        await vm.SelectAsync(original.Contact.Id); Assert.Single(vm.Channels); Assert.False(vm.AddChannelCommand.CanExecute(null));
        write = true; vm.UpdateAccess(); await vm.AddChannelCommand.ExecuteAsync(); await vm.SaveRelationCommand.ExecuteAsync(); Assert.Empty(api.Versions);
        Assert.True(vm.HasRelationDraft); vm.ChannelDraft!.Value = "draft";
        online = false; vm.UpdateAccess(); Assert.False(vm.SaveRelationCommand.CanExecute(null)); Assert.Empty(api.Versions);
        Assert.Equal("draft", vm.ChannelDraft.Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancellationBlocksRetryUntilReloadAndAccessRevocationClearsPrivateDraft()
    {
        var original = Details(); var api = new Client(original); var read = true;
        api.Change = async (_, ct) => { await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct); return Ok(original); };
        using var vm = new ContactCardViewModel(api, () => read, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); await vm.AddAddressCommand.ExecuteAsync(); vm.AddressDraft!.Value = "private draft";
        var save = vm.SaveRelationCommand.ExecuteAsync(); vm.SaveRelationCommand.Cancel(); await save;
        Assert.False(vm.SaveRelationCommand.CanExecute(null)); Assert.Equal("private draft", vm.AddressDraft.Value); Assert.Contains("не подтверждён", vm.Message);
        read = false; vm.UpdateAccess(); Assert.Null(vm.AddressDraft); Assert.Empty(vm.Addresses); Assert.Empty(vm.Channels);
    }

    [Fact]
    public async System.Threading.Tasks.Task ContactSwitchIgnoresLateChildResultEvenWhenClientIgnoresCancellation()
    {
        var first = Details(); var second = Details(); var api = new Client(first);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopContactDetails>>(); api.Change = (_, _) => pending.Task;
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(first.Contact.Id); await vm.AddChannelCommand.ExecuteAsync(); vm.ChannelDraft!.Value = "first private";
        var saving = vm.SaveRelationCommand.ExecuteAsync(); api.Current = second; await vm.SelectAsync(second.Contact.Id);
        Assert.Null(vm.ChannelDraft); pending.SetResult(Ok(first with { Contact = first.Contact with { Version = 8 } })); await saving;
        Assert.Equal(second, vm.Details); Assert.Equal(second.Channels, vm.Channels); Assert.True(vm.AddAddressCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task ParentEditingExcludesChildMutationsAndEmptyStateIsExplicit()
    {
        var original = Details() with { Channels = [], Addresses = [] }; var api = new Client(original);
        using var vm = new ContactCardViewModel(api, () => true, () => true, () => true);
        await vm.SelectAsync(original.Contact.Id); Assert.True(vm.ChannelsEmpty); Assert.True(vm.AddressesEmpty);
        await vm.EditCommand.ExecuteAsync(); Assert.False(vm.AddChannelCommand.CanExecute(null)); Assert.False(vm.AddAddressCommand.CanExecute(null));
        await vm.CancelCommand.ExecuteAsync(); await vm.AddChannelCommand.ExecuteAsync(); Assert.False(vm.EditCommand.CanExecute(null));
    }

    private sealed class Client(DesktopContactDetails current) : IDesktopContactDetailsClient, IDesktopContactRelationsClient
    {
        public DesktopContactDetails Current { get; set; } = current;
        public List<long> Versions { get; } = [];
        public string? Operation { get; private set; }
        public Func<long, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>>>? Change { get; set; }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(Ok(Current));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails d, CancellationToken cancellationToken = default) => ChangeAsync("parent", d.Contact.Version, cancellationToken);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> SaveChannelAsync(Guid id, long version, DesktopContactChannel c, CancellationToken ct = default) => ChangeAsync("channel", version, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> DeleteChannelAsync(Guid id, long version, Guid child, CancellationToken ct = default) => ChangeAsync("delete", version, ct);
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> AddAddressAsync(Guid id, long version, DesktopContactAddress a, CancellationToken ct = default) => ChangeAsync("address", version, ct);
        private System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> ChangeAsync(string operation, long version, CancellationToken ct)
        {
            Versions.Add(version); Operation = operation;
            return Change?.Invoke(version, ct) ?? System.Threading.Tasks.Task.FromResult(Ok(Current = Current with { Contact = Current.Contact with { Version = version + 1 } }));
        }
    }
}
