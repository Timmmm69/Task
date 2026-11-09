using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Personal;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalContactsBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-contact-browser-tests", Guid.NewGuid().ToString("N"));
    private PersonalDataPaths Paths => new(_root);

    [Fact]
    public async System.Threading.Tasks.Task FilteringAndRefreshing_PreserveSelectionAndCreationDraft()
    {
        using var store = new PersonalTaskStore(Paths);
        var anna = store.CreateContact("Анна", "Павлова", "Анна Павлова");
        store.CreateContact("Иван", null, "Иван");
        using var model = new PersonalWorkspaceViewModel(store);
        model.Area = model.Areas.Single(a => a.Title == "Контакты");
        await model.Hub.RefreshCommand.ExecuteAsync();
        model.ContactSelection = model.VisibleContacts.Single(c => c.Id == anna.Id);
        model.Hub.NewContactFirstName = "Черновик";
        model.Hub.NewContactDisplayName = "Не сохранён";
        model.IsContactCreating = true;
        model.ContactQuery = "не существует";
        Assert.Empty(model.VisibleContacts);
        Assert.True(model.ShowContactsEmpty);
        Assert.True(model.IsContactSelectionHidden);
        model.ContactSelection = null; // WPF clears SelectedItem while replacing ItemsSource.
        await model.Hub.RefreshCommand.ExecuteAsync();
        Assert.Equal(anna.Id, model.ContactSelection?.Id);
        Assert.Equal(anna.Id, model.Selected?.Id);
        Assert.Equal("Черновик", model.Hub.NewContactFirstName);
        Assert.Equal("Не сохранён", model.Hub.NewContactDisplayName);
        Assert.True(model.IsContactCreating);
        model.ResetContactFilters();
        Assert.Equal(2, model.VisibleContacts.Count);
        Assert.False(model.IsContactSelectionHidden);
    }

    [Fact]
    public async System.Threading.Tasks.Task ArchivedContact_IsNotReinsertedAsIfItWereOnAnotherPage()
    {
        using var store = new PersonalTaskStore(Paths);
        var anna = store.CreateContact("Анна", "Павлова", "Анна Павлова");
        store.CreateContact("Иван", null, "Иван");
        using var model = new PersonalWorkspaceViewModel(store);
        model.Area = model.Areas.Single(a => a.Title == "Контакты");
        await model.Hub.RefreshCommand.ExecuteAsync();
        model.ContactSelection = model.VisibleContacts.Single(c => c.Id == anna.Id);
        await model.ArchiveCommand.ExecuteAsync();
        await model.Hub.RefreshCommand.ExecuteAsync();
        Assert.Single(model.VisibleContacts);
        Assert.DoesNotContain(model.Hub.Contacts, c => c.Id == anna.Id);
        Assert.Null(model.ContactSelection);
        Assert.Null(model.Selected);
        Assert.True(model.ShowContactSelectionHint);
    }

    [Fact]
    public async System.Threading.Tasks.Task SearchUsesEveryExistingTextField_AndFiltersOnlySupportedData()
    {
        using var store = new PersonalTaskStore(Paths);
        var anna = store.CreateContact("Анна", "Павлова", "Аня");
        store.CreateContact("Елена", null, "Елена");
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Paths.DatabasePath, Pooling = false }.ToString()))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "UPDATE personal_contacts SET notes='Студия Север demo@example.com' WHERE id=$id";
            command.Parameters.AddWithValue("$id", anna.Id.ToString("D")); command.ExecuteNonQuery();
        }
        using var model = new PersonalWorkspaceViewModel(store);
        model.Area = model.Areas.Single(a => a.Title == "Контакты");
        await model.Hub.RefreshCommand.ExecuteAsync();
        model.ContactQuery = "  АННА павл СЕВЕР ";
        Assert.Equal(anna.Id, Assert.Single(model.VisibleContacts).Id);
        model.ContactQuery = "EXAMPLE.COM";
        Assert.Equal(anna.Id, Assert.Single(model.VisibleContacts).Id);
        model.ResetContactFilters(); model.ContactFilter = "Без фамилии";
        Assert.Equal("Елена", Assert.Single(model.VisibleContacts).Name);
        model.ContactFilter = "С заметками";
        Assert.Equal(anna.Id, Assert.Single(model.VisibleContacts).Id);
        model.ContactFilter = "Без заметок";
        Assert.Equal("Елена", Assert.Single(model.VisibleContacts).Name);
        model.ResetContactFilters(); model.ContactSort = "Имя: Я–А";
        Assert.Equal(new[] { "Елена", "Аня" }, model.VisibleContacts.Select(c => c.Name));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
