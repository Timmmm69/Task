using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class CompanyLifecycleReviewTests
{
    [Theory]
    [InlineData("archived")]
    [InlineData("trashed")]
    public async System.Threading.Tasks.Task SearchNavigationCannotReopenNonActiveCompany(string lifecycle)
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Недоступная компания", LifecycleState: lifecycle);
        var api = new CompanyTestClient(company);
        using var vm = CompaniesViewModelTests.Create(api);
        await vm.OpenAsync(company.Id);
        Assert.Null(vm.Details); Assert.Null(vm.SelectedCompany); Assert.Empty(vm.Items);
        Assert.Empty(vm.Name); Assert.Empty(vm.RelatedContacts); Assert.False(vm.CanWrite);
    }

    [Theory]
    [InlineData("archived", false)]
    [InlineData("trashed", false)]
    [InlineData("archived", true)]
    [InlineData("trashed", true)]
    public async System.Threading.Tasks.Task RefreshClearsNonActiveSelectionAndDraftOnOrOffPage(string lifecycle, bool offPage)
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания", Notes: "Секрет");
        var api = new CompanyTestClient(company);
        using var vm = CompaniesViewModelTests.Create(api); await vm.OpenAsync(company.Id);
        await vm.EditCommand.ExecuteAsync(); vm.Notes = "Черновик";
        api.Records[company.Id] = company with { LifecycleState = lifecycle };
        if (offPage) api.List = (_, _, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<IReadOnlyList<DesktopCompany>>>(
            new DesktopWorkResult<IReadOnlyList<DesktopCompany>>.Succeeded([]));
        await vm.RefreshCommand.ExecuteAsync();
        Assert.Null(vm.SelectedCompany); Assert.Null(vm.Details); Assert.Empty(vm.Items);
        Assert.Empty(vm.Name); Assert.Empty(vm.Notes); Assert.False(vm.IsEditing); Assert.False(vm.CanWrite);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("save-response")]
    [InlineData("save-confirmation")]
    [InlineData("link-response")]
    public async System.Threading.Tasks.Task ConfirmedLifecycleChangeScrubsCardOnEveryReadback(string operation)
    {
        var company = new DesktopCompany(Guid.NewGuid(), 1, "Компания");
        var contact = CompanyTestClient.Contact("Контакт"); var api = new CompanyTestClient(company);
        api.Contacts.Add(contact.Id, contact);
        using var vm = CompaniesViewModelTests.Create(api); await vm.OpenAsync(company.Id);
        var unavailable = company with { Version = 2, LifecycleState = "trashed" };
        switch (operation)
        {
            case "reload": api.Records[company.Id] = unavailable; await vm.ReloadCommand.ExecuteAsync(); break;
            case "save-response":
                await vm.EditCommand.ExecuteAsync(); api.Save = (_, _, _) => System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(unavailable));
                await vm.SaveCommand.ExecuteAsync(); break;
            case "save-confirmation":
                await vm.EditCommand.ExecuteAsync(); api.Read = (_, _) => System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(unavailable));
                await vm.SaveCommand.ExecuteAsync(); break;
            default:
                vm.ContactQuery = "Контакт"; await vm.SearchContactsCommand.ExecuteAsync(); vm.SelectedCandidate = contact;
                api.Link = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult(CompanyTestClient.Ok(unavailable));
                await vm.LinkContactCommand.ExecuteAsync(); break;
        }
        Assert.Null(vm.SelectedCompany); Assert.Null(vm.Details); Assert.Empty(vm.Items);
        Assert.Empty(vm.Name); Assert.Empty(vm.RelatedContacts); Assert.Empty(vm.ContactCandidates);
        Assert.False(vm.IsEditing); Assert.False(vm.IsBusy); Assert.False(vm.CanWrite);
    }
}
