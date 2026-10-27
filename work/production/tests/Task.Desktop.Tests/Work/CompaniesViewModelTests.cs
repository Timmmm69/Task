using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class CompaniesViewModelTests
{
    [Fact]
    public async System.Threading.Tasks.Task ServerPaginationSearchSelectionAndDraftSurviveRefresh()
    {
        var first = new DesktopCompany(Guid.NewGuid(), 2, "Первая"); var second = new DesktopCompany(Guid.NewGuid(), 3, "Вторая");
        var api = new CompanyTestClient(first, second);
        api.List = (q, cursor, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopCompany>>>(
            new DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded(cursor is null ? [first] : [second]) { NextCursor = cursor is null ? "next" : null });
        using var vm = Create(api); await vm.RefreshCommand.ExecuteAsync(); Assert.True(vm.HasMore);
        await vm.MoreCommand.ExecuteAsync(); Assert.Equal(2, vm.Items.Count);
        vm.SelectedCompany = second; await vm.SelectionTask;
        await vm.EditCommand.ExecuteAsync(); vm.Name = "Черновик";
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(second.Id, vm.SelectedCompany?.Id); Assert.Equal("Черновик", vm.Name); Assert.True(vm.IsEditing);
        api.Records.Remove(second.Id);
        await vm.RefreshCommand.ExecuteAsync(); Assert.Null(vm.Details); Assert.Null(vm.SelectedCompany);
        Assert.DoesNotContain(vm.Items, c => c.Id == second.Id);
        vm.Query = new string('x', 201); var count = api.ListCalls; await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(count, api.ListCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task CreateRetryReusesFrozenPayloadAndKeyAndDoubleClickDoesNotDuplicate()
    {
        var api = new CompanyTestClient(); var pending = new TaskCompletionSource<DesktopWorkResult<DesktopCompany>>();
        api.Save = (_, _, _) => pending.Task;
        using var vm = Create(api); await vm.NewCommand.ExecuteAsync(); vm.Name = "Компания";
        var save = vm.SaveCommand.ExecuteAsync(); Assert.False(await vm.SaveCommand.ExecuteAsync()); Assert.False(vm.CanChangeDraft);
        pending.SetResult(new DesktopWorkResult<DesktopCompany>.ServerUnavailable()); await save;
        Assert.True(vm.IsEditing); Assert.False(vm.CanChangeDraft); var attempt = Assert.Single(api.Saves);
        vm.Name = "Не должно изменить повтор";
        api.Save = (c, _, _) => { var created = c with { Id = Guid.NewGuid(), Version = 1 }; api.Records.Add(created.Id, created); return System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(created)); };
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(attempt, api.Saves[1]); Assert.Single(vm.Items); Assert.Equal("Компания", vm.Details?.Name); Assert.False(vm.IsEditing);
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(2, api.Saves.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task EditingUsesCurrentVersionCancelRestoresAndStatusDoesNotChangeLifecycle()
    {
        var company = new DesktopCompany(Guid.NewGuid(), 4, "Исходное"); var api = new CompanyTestClient(company);
        using var vm = Create(api); await vm.OpenAsync(company.Id); await vm.EditCommand.ExecuteAsync(); vm.Name = "Не сохранять";
        await vm.CancelCommand.ExecuteAsync(); Assert.Equal("Исходное", vm.Name); Assert.Empty(api.Saves);
        await vm.EditCommand.ExecuteAsync(); vm.Status = "inactive"; await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(4, api.Saves.Single().Company.Version); Assert.Equal(5, vm.Details?.Version); Assert.Equal("active", vm.Details?.LifecycleState);
        Assert.Equal("inactive", vm.Details?.Status);
    }

    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("409")]
    [InlineData("412")]
    [InlineData("401")]
    [InlineData("validation")]
    [InlineData("offline")]
    public async System.Threading.Tasks.Task FailedWritePreservesDraftUnlessSessionEndsAndNeverReportsSuccess(string kind)
    {
        var company = new DesktopCompany(Guid.NewGuid(), 4, "Компания"); var api = new CompanyTestClient(company);
        api.Save = (_, _, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopCompany>>(kind switch
        {
            "403" => new DesktopWorkResult<DesktopCompany>.Forbidden(),
            "404" => new DesktopWorkResult<DesktopCompany>.NotFound(),
            "409" or "412" => new DesktopWorkResult<DesktopCompany>.Conflict(),
            "401" => new DesktopWorkResult<DesktopCompany>.AuthenticationFailure(),
            "validation" => new DesktopWorkResult<DesktopCompany>.ValidationFailure("Ошибка полей"),
            _ => new DesktopWorkResult<DesktopCompany>.ServerUnavailable()
        });
        using var vm = Create(api); await vm.OpenAsync(company.Id); await vm.EditCommand.ExecuteAsync(); vm.Notes = "Ввод";
        await vm.SaveCommand.ExecuteAsync(); Assert.DoesNotContain("Компания сохранена", vm.Message);
        Assert.Equal(kind == "401" ? "" : "Ввод", vm.Notes);
        if (kind != "validation") Assert.False(vm.SaveCommand.CanExecute(null));
        if (kind is "409" or "412")
        { await vm.CancelCommand.ExecuteAsync(); Assert.False(vm.EditCommand.CanExecute(null)); await vm.ReloadCommand.ExecuteAsync(); Assert.True(vm.EditCommand.CanExecute(null)); }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("legal")]
    [InlineData("industry")]
    [InlineData("tax")]
    [InlineData("notes")]
    [InlineData("url")]
    public async System.Threading.Tasks.Task InvalidDraftDoesNotReachServer(string kind)
    {
        var api = new CompanyTestClient(); using var vm = Create(api); await vm.NewCommand.ExecuteAsync(); vm.Name = "Компания";
        switch (kind)
        {
            case "name": vm.Name = " "; break;
            case "legal": vm.LegalName = new('x', 501); break;
            case "industry": vm.Industry = new('x', 201); break;
            case "tax": vm.TaxIdentifier = new('x', 101); break;
            case "notes": vm.Notes = new('x', 20001); break;
            default: vm.Website = "file:///c:/secret"; break;
        }
        await vm.SaveCommand.ExecuteAsync(); Assert.Empty(api.Saves); Assert.True(vm.IsEditing);
    }

    [Fact]
    public async System.Threading.Tasks.Task PermissionsOfflineAndScopeChangesDisableWritesAndErasePrivateData()
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания"); var api = new CompanyTestClient(company);
        var read = true; var online = true; var write = false;
        using var vm = new CompaniesViewModel(api, () => read, () => online, () => write, () => write);
        await vm.OpenAsync(company.Id); Assert.False(vm.EditCommand.CanExecute(null)); Assert.False(vm.NewCommand.CanExecute(null));
        write = true; vm.UpdateAccess(); await vm.EditCommand.ExecuteAsync(); vm.Name = "Ввод";
        online = false; vm.UpdateAccess(); Assert.False(vm.SaveCommand.CanExecute(null)); Assert.Equal("Ввод", vm.Name);
        read = false; vm.UpdateAccess(); Assert.Null(vm.Details); Assert.Empty(vm.Items); Assert.Empty(vm.Name);
        Assert.Empty(api.Saves);
    }

    [Fact]
    public async System.Threading.Tasks.Task LateReadAndLateWriteCannotReplaceNewCompany()
    {
        var first = new DesktopCompany(Guid.NewGuid(), 1, "Первая"); var second = new DesktopCompany(Guid.NewGuid(), 1, "Вторая"); var api = new CompanyTestClient(first, second);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopCompany>>();
        api.Read = (id, _) => id == first.Id ? pending.Task : System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(second));
        using var vm = Create(api); var old = vm.OpenAsync(first.Id); await vm.OpenAsync(second.Id);
        pending.SetResult(CompanyTestClient.Ok(first)); await old; Assert.Equal(second.Id, vm.Details?.Id);
        api.Read = (id, _) => System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(api.Records[id])); await vm.OpenAsync(first.Id); await vm.EditCommand.ExecuteAsync();
        var writing = new TaskCompletionSource<DesktopWorkResult<DesktopCompany>>(); api.Save = (_, _, _) => writing.Task;
        var save = vm.SaveCommand.ExecuteAsync(); await vm.OpenAsync(second.Id); writing.SetResult(CompanyTestClient.Ok(first with { Version = 2 })); await save;
        Assert.Equal(second.Id, vm.Details?.Id); Assert.DoesNotContain("сохранена", vm.Message ?? "");
    }

    [Fact]
    public async System.Threading.Tasks.Task ClearingSelectionDuringLoadRejectsTheLateCard()
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания"); var api = new CompanyTestClient(company);
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopCompany>>(); api.Read = (_, _) => pending.Task;
        using var vm = Create(api); vm.SelectedCompany = company; var selecting = vm.SelectionTask;
        vm.SelectedCompany = null; pending.SetResult(CompanyTestClient.Ok(company)); await selecting;
        Assert.Null(vm.Details); Assert.Null(vm.SelectedCompany); Assert.Empty(vm.RelatedContacts);
    }

    [Fact]
    public async System.Threading.Tasks.Task CancelledWriteRequiresRecheckAndDoesNotClaimSuccess()
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания"); var api = new CompanyTestClient(company);
        api.Save = async (_, _, ct) => { await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct); return CompanyTestClient.Ok(company); };
        using var vm = Create(api); await vm.OpenAsync(company.Id); await vm.EditCommand.ExecuteAsync(); vm.Name = "Ввод";
        var save = vm.SaveCommand.ExecuteAsync(); vm.SaveCommand.Cancel(); await save;
        Assert.Contains("не подтверждено", vm.Message); Assert.Equal("Ввод", vm.Name); Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task RelationsResolveOnlyReadableContactsLinkMetadataAndUnlinkWithoutDeletingContact()
    {
        var allowed = CompanyTestClient.Contact("Анна"); var hidden = Guid.NewGuid();
        var company = new DesktopCompany(Guid.NewGuid(), 4, "Компания") { Contacts = [new(hidden, "Скрытая должность")] };
        var api = new CompanyTestClient(company); api.Contacts.Add(allowed.Id, allowed);
        using var vm = Create(api); await vm.OpenAsync(company.Id); Assert.Empty(vm.RelatedContacts);
        vm.ContactQuery = "Анна"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = Assert.Single(vm.ContactCandidates);
        vm.JobTitle = "Директор"; vm.DepartmentName = "ИТ"; vm.IsPrimary = true; vm.ValidFrom = "2026-01-01"; vm.ValidTo = "2026-12-31";
        await vm.LinkContactCommand.ExecuteAsync(); var link = Assert.Single(vm.RelatedContacts);
        Assert.Equal("Анна", link.DisplayName); Assert.Equal("Директор", link.JobTitle); Assert.True(link.IsPrimary); Assert.Equal(5, vm.Details?.Version);
        Guid opened = Guid.Empty; vm.OpenContactRequested += id => { opened = id; return System.Threading.Tasks.Task.CompletedTask; };
        await vm.OpenContactCommand.ExecuteAsync(link); Assert.Equal(allowed.Id, opened);
        await vm.UnlinkContactCommand.ExecuteAsync(link); Assert.Empty(vm.RelatedContacts); Assert.Single(api.Contacts); Assert.Equal(6, vm.Details?.Version);
    }

    [Fact]
    public async System.Threading.Tasks.Task DuplicateAndRevokedContactsCannotProduceAnotherLink()
    {
        var contact = CompanyTestClient.Contact("Анна"); var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания") { Contacts = [new(contact.Id)] };
        var api = new CompanyTestClient(company); api.Contacts.Add(contact.Id, contact);
        using var vm = Create(api); await vm.OpenAsync(company.Id); vm.ContactQuery = "Анна"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = contact;
        await vm.LinkContactCommand.ExecuteAsync(); Assert.Equal(0, api.Links); Assert.Contains("уже связан", vm.Message);
        api.Records[company.Id] = company with { Contacts = [] }; await vm.ReloadCommand.ExecuteAsync(); vm.ContactQuery = "Анна"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = contact;
        api.Contacts.Clear(); await vm.LinkContactCommand.ExecuteAsync(); Assert.Equal(0, api.Links); Assert.Empty(vm.ContactCandidates);
    }

    [Fact]
    public async System.Threading.Tasks.Task LateListAndPickerResponsesCannotExposePreviousScope()
    {
        var first = new DesktopCompany(Guid.NewGuid(), 1, "Первая"); var second = new DesktopCompany(Guid.NewGuid(), 1, "Вторая");
        var api = new CompanyTestClient(first, second);
        var list = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopCompany>>>(); api.List = (_, _, _) => list.Task;
        using var vm = Create(api); var listing = vm.RefreshCommand.ExecuteAsync(); vm.Query = "Новый запрос";
        list.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded([first])); await listing; Assert.Empty(vm.Items);
        await vm.OpenAsync(first.Id); vm.ContactQuery = "Анна";
        var search = new TaskCompletionSource<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(); api.Find = (_, _, _) => search.Task;
        var finding = vm.SearchContactsCommand.ExecuteAsync(); await vm.OpenAsync(second.Id);
        search.SetResult(new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded([CompanyTestClient.Contact("Анна")])); await finding;
        Assert.Empty(vm.ContactCandidates); Assert.Equal(second.Id, vm.Details?.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task BusyMutationRemainsExclusiveAcrossSelectionChangeAndDirtyCardCannotOpenContact()
    {
        var contact = CompanyTestClient.Contact("Анна");
        var first = new DesktopCompany(Guid.NewGuid(), 1, "Первая") { Contacts = [new(contact.Id)] };
        var second = new DesktopCompany(Guid.NewGuid(), 1, "Вторая"); var api = new CompanyTestClient(first, second); api.Contacts.Add(contact.Id, contact);
        using var vm = Create(api); await vm.OpenAsync(first.Id); var link = Assert.Single(vm.RelatedContacts);
        await vm.EditCommand.ExecuteAsync(); Assert.False(vm.OpenContactCommand.CanExecute(link));
        var pending = new TaskCompletionSource<DesktopWorkResult<DesktopCompany>>(); api.Save = (_, _, _) => pending.Task;
        var saving = vm.SaveCommand.ExecuteAsync(); await vm.OpenAsync(second.Id);
        Assert.True(vm.IsBusy); Assert.False(vm.EditCommand.CanExecute(null)); Assert.False(vm.NewCommand.CanExecute(null));
        pending.SetResult(CompanyTestClient.Ok(first with { Version = 2 })); await saving;
        Assert.False(vm.IsBusy); Assert.Equal(second.Id, vm.Details?.Id); Assert.True(vm.EditCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("409")]
    [InlineData("412")]
    [InlineData("validation")]
    [InlineData("offline")]
    public async System.Threading.Tasks.Task FailedLinkKeepsMetadataAndDoesNotInventConfirmedRelation(string kind)
    {
        var contact = CompanyTestClient.Contact("Анна"); var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания");
        var api = new CompanyTestClient(company); api.Contacts.Add(contact.Id, contact);
        api.Link = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopCompany>>(kind switch
        {
            "403" => new DesktopWorkResult<DesktopCompany>.Forbidden(),
            "404" => new DesktopWorkResult<DesktopCompany>.NotFound(),
            "409" or "412" => new DesktopWorkResult<DesktopCompany>.Conflict(),
            "validation" => new DesktopWorkResult<DesktopCompany>.ValidationFailure("Проверьте поля"),
            _ => new DesktopWorkResult<DesktopCompany>.ServerUnavailable()
        });
        using var vm = Create(api); await vm.OpenAsync(company.Id); vm.ContactQuery = "Анна"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = contact;
        vm.JobTitle = "Директор"; await vm.LinkContactCommand.ExecuteAsync();
        Assert.Empty(vm.RelatedContacts); Assert.Equal("Директор", vm.JobTitle); Assert.DoesNotContain("Связь добавлена", vm.Message);
        if (kind != "validation") Assert.False(vm.LinkContactCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("2026-12-31", "2026-01-01")]
    [InlineData("2026-02-30", null)]
    [InlineData("18.10.2026", null)]
    public async System.Threading.Tasks.Task InvalidLinkDatesNeverReachServer(string? from, string? to)
    {
        var contact = CompanyTestClient.Contact("Анна"); var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания");
        var api = new CompanyTestClient(company); api.Contacts.Add(contact.Id, contact);
        using var vm = Create(api); await vm.OpenAsync(company.Id); vm.ContactQuery = "Анна"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = contact;
        vm.ValidFrom = from ?? ""; vm.ValidTo = to ?? ""; await vm.LinkContactCommand.ExecuteAsync(); Assert.Equal(0, api.Links);
    }

    internal static CompaniesViewModel Create(CompanyTestClient api) => new(api, () => true, () => true, () => true, () => true);
}

internal sealed class CompanyTestClient(params DesktopCompany[] companies) : IDesktopCompaniesClient
{
    public Dictionary<Guid, DesktopCompany> Records { get; } = companies.ToDictionary(c => c.Id);
    public Dictionary<Guid, DesktopContact> Contacts { get; } = [];
    public List<(DesktopCompany Company, string? Key)> Saves { get; } = [];
    public int ListCalls, Links;
    public Func<string, string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>>>? List;
    public Func<Guid, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>>>? Read;
    public Func<DesktopCompany, string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>>>? Save;
    public Func<string, string?, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>>>? Find;
    public Func<Guid, long, DesktopCompanyContact, string, CancellationToken, System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>>>? Link;
    public static DesktopWorkResult<DesktopCompany> Ok(DesktopCompany c) => new DesktopWorkResult<DesktopCompany>.Succeeded(c, c.Version);
    public static DesktopContact Contact(string name) => new(Guid.NewGuid(), 1, name, name, null, null, "active", "active");
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopCompany>>> GetCompaniesAsync(string query, string? cursor, CancellationToken ct = default)
    { ListCalls++; return List?.Invoke(query, cursor, ct) ?? System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopCompany>>>(new DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded(Records.Values.ToArray())); }
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> GetCompanyAsync(Guid id, CancellationToken ct = default) => Read?.Invoke(id, ct)
        ?? System.Threading.Tasks.Task.FromResult(Records.TryGetValue(id, out var c) ? Ok(c) : new DesktopWorkResult<DesktopCompany>.NotFound());
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> SaveCompanyAsync(DesktopCompany company, string? key, CancellationToken ct = default)
    {
        Saves.Add((company, key)); if (Save is not null) return Save(company, key, ct);
        var saved = company with { Id = company.Id == Guid.Empty ? Guid.NewGuid() : company.Id, Version = company.Version + 1 };
        Records[saved.Id] = saved; return System.Threading.Tasks.Task.FromResult(Ok(saved));
    }
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> GetContactAsync(Guid id, CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(Contacts.TryGetValue(id, out var c)
            ? new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(c, null)) : new DesktopWorkResult<DesktopContactDetails>.Forbidden());
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopContactDetails>> UpdateContactAsync(DesktopContactDetails details, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public System.Threading.Tasks.Task<DesktopWorkResult<IReadOnlyList<DesktopContact>>> FindCompanyContactsAsync(string query, string? cursor, CancellationToken ct = default) =>
        Find?.Invoke(query, cursor, ct) ?? System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopContact>>>(new DesktopWorkResult<IReadOnlyList<DesktopContact>>.Succeeded(Contacts.Values.Where(c => c.DisplayName.Contains(query)).ToArray()));
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> LinkCompanyContactAsync(Guid id, long version, DesktopCompanyContact link, string key, CancellationToken ct = default)
    { Links++; if (Link is not null) return Link(id, version, link, key, ct); Records[id] = Records[id] with { Version = version + 1, Contacts = [.. Records[id].Contacts, link] }; return System.Threading.Tasks.Task.FromResult(Ok(Records[id])); }
    public System.Threading.Tasks.Task<DesktopWorkResult<DesktopCompany>> UnlinkCompanyContactAsync(Guid id, long version, Guid contactId, CancellationToken ct = default)
    { Records[id] = Records[id] with { Version = version + 1, Contacts = Records[id].Contacts.Where(c => c.ContactId != contactId).ToArray() }; return System.Threading.Tasks.Task.FromResult(Ok(Records[id])); }
}
