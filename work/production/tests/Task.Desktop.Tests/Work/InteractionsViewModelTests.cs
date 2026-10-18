using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class InteractionsViewModelTests
{
    internal sealed class Client : IDesktopInteractionsClient
    {
        public InteractionCounterparty Target = new(Guid.NewGuid(), "contact", "Анна");
        public List<DesktopInteraction> Values = [];
        public int PageCalls, Writes;
        public readonly List<(DesktopInteraction Value, string? Key)> Submitted = [];
        public Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>>>? Read;
        public Func<DesktopWorkResult<InteractionCounterparty>>? Scope;
        public Func<DesktopInteraction, DesktopWorkResult<DesktopInteraction>>? Save;
        public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>> GetInteractionsAsync(string? cursor, CancellationToken ct = default)
        { PageCalls++; return System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopInteraction>>>(new DesktopWorkResult<IReadOnlyList<DesktopInteraction>>.Succeeded(Values) { NextCursor = cursor is null ? "second" : null }); }
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> GetInteractionAsync(Guid id, CancellationToken ct = default) =>
            Read?.Invoke(id, ct) ?? System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopInteraction>>(new DesktopWorkResult<DesktopInteraction>.Succeeded(Values.Single(v => v.Id == id)));
        public System.Threading.Tasks.Task<DesktopWorkResult<DesktopInteraction>> SaveInteractionAsync(DesktopInteraction value, string? key, CancellationToken ct = default)
        { Writes++; Submitted.Add((value, key)); return System.Threading.Tasks.Task.FromResult(Save?.Invoke(value) ?? new DesktopWorkResult<DesktopInteraction>.Succeeded(value with { Id = value.Id == Guid.Empty ? Guid.NewGuid() : value.Id, Version = value.Version + 1 })); }
        public System.Threading.Tasks.Task<DesktopWorkResult<InteractionCounterparty>> GetInteractionCounterpartyAsync(Guid id, string type, CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(Scope?.Invoke() ?? (id == Target.Id && type == Target.Type ? new DesktopWorkResult<InteractionCounterparty>.Succeeded(Target) : (DesktopWorkResult<InteractionCounterparty>)new DesktopWorkResult<InteractionCounterparty>.NotFound()));
        public DesktopInteraction Record(Guid? target = null) => new(Guid.NewGuid(), 3, target ?? Target.Id, "call", new DateTimeOffset(2026, 10, 18, 10, 12, 34, TimeSpan.Zero).AddTicks(1234), "Тема", "Детали", "Шаг", DateTimeOffset.UtcNow, []);
        public InteractionsViewModel Model() { var vm = new InteractionsViewModel(this, () => true, () => true, () => true, () => true); vm.SetCounterparty(Target); return vm; }
    }

    [Fact]
    public async System.Threading.Tasks.Task OneExplicitPageContainsOnlySelectedCounterpartyAndNeverClaimsCompleteness()
    {
        var api = new Client(); var own = api.Record(); api.Values = [own, api.Record(Guid.NewGuid())];
        using var vm = api.Model(); await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal(own, Assert.Single(vm.Items)); Assert.Equal(1, api.PageCalls); Assert.Contains("не полная", vm.Message);
        await vm.OpenCommand.ExecuteAsync(own); Assert.Equal(own, vm.Details);
        await vm.MoreCommand.ExecuteAsync(); Assert.Equal(2, api.PageCalls); Assert.False(vm.MoreCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task UncertainCreateFreezesPayloadAndReusesKeyThenShowsOnlyConfirmedRecord()
    {
        var api = new Client(); api.Save = _ => new DesktopWorkResult<DesktopInteraction>.ServerUnavailable();
        using var vm = api.Model(); await vm.NewCommand.ExecuteAsync(); vm.Subject = "Новая запись";
        await vm.SaveCommand.ExecuteAsync(); Assert.True(vm.IsEditing); Assert.Null(vm.Details); Assert.False(vm.CanChangeDraft);
        var first = api.Submitted.Single(); Assert.Equal(api.Target.Id, first.Value.CounterpartyObjectId); Assert.NotNull(first.Key);
        vm.Subject = "Не отправлять эту замену";
        api.Save = v => new DesktopWorkResult<DesktopInteraction>.Succeeded(v with { Id = Guid.NewGuid(), Version = 1 });
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(first, api.Submitted[1]); Assert.False(vm.IsEditing);
        Assert.Equal("Новая запись", vm.Details?.Subject); Assert.Empty(vm.Items); Assert.Contains("подтверждены", vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task PatchPreservesIdentityParticipantsAndDatePrecisionAndConflictBlocksOverwrite()
    {
        var api = new Client(); var value = api.Record() with { ParticipantObjectIds = [Guid.NewGuid()] }; api.Values = [value];
        using var vm = api.Model(); await vm.OpenAsync(value.Id); await vm.EditCommand.ExecuteAsync(); vm.Subject = "Изменено";
        api.Save = _ => new DesktopWorkResult<DesktopInteraction>.Conflict(); await vm.SaveCommand.ExecuteAsync();
        var sent = api.Submitted.Single(); Assert.Equal(value.Id, sent.Value.Id); Assert.Equal(3, sent.Value.Version);
        Assert.Equal(value.OccurredAt, sent.Value.OccurredAt); Assert.Equal(value.NextStepDueAt, sent.Value.NextStepDueAt);
        Assert.Equal(value.ParticipantObjectIds, sent.Value.ParticipantObjectIds); Assert.Null(sent.Key);
        Assert.True(vm.IsEditing); Assert.Equal("Изменено", vm.Subject); Assert.False(vm.SaveCommand.CanExecute(null)); Assert.Contains("Версия", vm.Message);
    }

    [Theory]
    [InlineData("401")][InlineData("403")][InlineData("404")]
    public async System.Threading.Tasks.Task RelatedObjectDenialClearsAllSensitiveState(string code)
    {
        var api = new Client(); var value = api.Record(); api.Values = [value];
        using var vm = api.Model(); await vm.OpenAsync(value.Id);
        api.Scope = () => code switch { "401" => new DesktopWorkResult<InteractionCounterparty>.AuthenticationFailure(), "403" => new DesktopWorkResult<InteractionCounterparty>.Forbidden(), _ => new DesktopWorkResult<InteractionCounterparty>.NotFound() };
        await vm.RefreshCommand.ExecuteAsync(); Assert.Null(vm.Details); Assert.Empty(vm.Items); Assert.Null(vm.Counterparty); Assert.Empty(vm.Participants);
        Assert.False(vm.NewCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task LateResponseAndReassignedCounterpartyCannotLeakIntoAnotherCard()
    {
        var api = new Client(); var value = api.Record(); var pending = new TaskCompletionSource<DesktopWorkResult<DesktopInteraction>>();
        api.Read = (_, _) => pending.Task;
        using var vm = api.Model(); var load = vm.OpenAsync(value.Id); vm.SetCounterparty(new(Guid.NewGuid(), "company", "Другая"));
        pending.SetResult(new DesktopWorkResult<DesktopInteraction>.Succeeded(value)); await load; Assert.Null(vm.Details); Assert.Empty(vm.Items);
        vm.SetCounterparty(api.Target); api.Read = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopInteraction>>(new DesktopWorkResult<DesktopInteraction>.Succeeded(value with { CounterpartyObjectId = Guid.NewGuid() }));
        await vm.OpenAsync(value.Id); Assert.Null(vm.Details); Assert.Contains("Связь", vm.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task OfflineReadOnlySessionLossValidationAndCancelNeverWrite()
    {
        var api = new Client(); var online = true; var read = true;
        using var vm = new InteractionsViewModel(api, () => read, () => online, () => false, () => false); vm.SetCounterparty(api.Target);
        Assert.False(vm.NewCommand.CanExecute(null)); await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(0, api.Writes);
        online = false; vm.UpdateAccess(); Assert.False(vm.RefreshCommand.CanExecute(null)); read = false; vm.UpdateAccess(); Assert.Null(vm.Counterparty);
        using var writable = api.Model(); await writable.NewCommand.ExecuteAsync(); await writable.SaveCommand.ExecuteAsync(); Assert.Equal(0, api.Writes); // empty subject
        writable.Subject = "Запись"; writable.OccurredAt = "2026-10-18 12:00"; await writable.SaveCommand.ExecuteAsync(); Assert.Equal(0, api.Writes);
        await writable.CancelCommand.ExecuteAsync(); Assert.False(writable.IsEditing); Assert.Equal(0, api.Writes);
    }
}
