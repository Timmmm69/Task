using Task.Desktop.ViewModels;
using Task.Desktop.Work;
using AsyncTask = System.Threading.Tasks.Task;
using ContactPage = Task.Desktop.Work.DesktopWorkResult<System.Collections.Generic.IReadOnlyList<Task.Desktop.Work.DesktopContact>>;

namespace Task.Desktop.Tests.Work;

public sealed partial class WorkHubViewModelTests
{
    [Fact]
    public async AsyncTask ContactSearchUsesServerPagesAndPreservesConfirmedResultsOnFailure()
    {
        var anna = CompanyTestClient.Contact("Анна Иванова");
        var irina = CompanyTestClient.Contact("Ирина Иванова");
        var other = CompanyTestClient.Contact("Михаил Белов");
        var requests = new List<(string Query, string? Cursor)>();
        var api = new CompanyTestClient
        {
            Find = (query, cursor, _) =>
            {
                requests.Add((query, cursor));
                return System.Threading.Tasks.Task.FromResult<ContactPage>((query, cursor) switch
                {
                    ("Иванова", null) => new ContactPage.Succeeded([anna]) { NextCursor = "next" },
                    ("Иванова", "next") => new ContactPage.Succeeded([irina]),
                    ("Ошибка", _) => new ContactPage.ServerUnavailable(),
                    _ => new ContactPage.Succeeded([]),
                });
            },
        };
        api.Contacts.Add(other.Id, other);
        using var hub = new WorkHubViewModel(new FakeClient
        {
            CompanyApi = api,
            Contacts = [other, anna],
            ContactDetailHandler = api.GetContactAsync,
        }, ["Contact.Read", "Contact.Update"]);
        hub.Activate(WorkHubArea.Contacts);
        hub.SelectedContact = other;
        await hub.ContactSelectionTask;
        var card = Assert.IsType<ContactCardViewModel>(hub.ContactCard);
        await card.EditCommand.ExecuteAsync();
        card.FirstName = "Черновик";
        await hub.RefreshCommand.ExecuteAsync("Иванова");
        Assert.Empty(requests);
        Assert.Equal("Черновик", card.FirstName);
        await card.CancelCommand.ExecuteAsync();

        Assert.True(await hub.RefreshCommand.ExecuteAsync(" Иванова "));
        Assert.Equal("Иванова", hub.ContactResultsQuery);
        Assert.Equal(anna, Assert.Single(hub.Contacts));
        Assert.Null(hub.SelectedContact);

        await hub.RefreshCommand.ExecuteAsync("Ошибка");
        Assert.Equal("Иванова", hub.ContactResultsQuery);
        Assert.Equal(anna, Assert.Single(hub.Contacts));
        Assert.Equal(WorkHubFeedbackKind.Error, hub.FeedbackKind);
        hub.UpdateConnectivity(true);
        Assert.True(await hub.LoadMoreCommand.ExecuteAsync());
        Assert.Equal(new[] { ("Иванова", (string?)null), ("Ошибка", null), ("Иванова", "next") }, requests);
        Assert.Equal(new[] { anna, irina }, hub.Contacts);

        await hub.RefreshCommand.ExecuteAsync("Несуществующий");
        Assert.Empty(hub.Contacts);
        Assert.Equal("Несуществующий", hub.ContactResultsQuery);

        await hub.OpenSearchObjectAsync("contact", other.Id);
        Assert.Equal("", hub.ContactResultsQuery);
        Assert.Contains(anna, hub.Contacts);
        Assert.Equal(other, hub.SelectedContact);

        await hub.RefreshCommand.ExecuteAsync("Иванова");
        await hub.RefreshCommand.ExecuteAsync("");
        Assert.Equal("", hub.ContactQuery);
        Assert.Equal("", hub.ContactResultsQuery);
        Assert.Equal(new[] { other, anna }, hub.Contacts);

        hub.UpdateCapabilities([]);
        Assert.False(await hub.RefreshCommand.ExecuteAsync("Иванова"));
        Assert.Empty(hub.Contacts);
    }
}
