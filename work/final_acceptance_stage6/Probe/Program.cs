using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Task.Application.Calendar;
using Task.Desktop.Calendar;
using Task.Desktop.Personal;
using Task.Desktop.Projects;
using Task.Desktop.TaskApi;
using Task.Domain;
using Task.Domain.Reminders;

// Acceptance fixture only: all data lives in the explicit synthetic root.
var action = args[0];
var root = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
using var store = new PersonalTaskStore(new(root));
long Version(Guid id) => store.WorkspaceObjects().Single(o => o.Id == id).Version;
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
if (action == "seed")
{
    Require(store.List().Count == 0, "Fixture must start empty.");
    var project = store.SaveProject(new(Guid.Empty, 0, "Acceptance project", "Exact project description",
        DesktopProjectStatus.Active, new(2026, 10, 6), new(2026, 12, 1), DefaultTimeZone: "UTC", ColorCode: "#336699"));
    var task = store.Create(new("PERSONAL_SECRET_MARKER", DesktopTaskPriority.High, card: new()
    {
        Description = "Exact task description", ProjectId = project.Id,
        ScheduledDate = new(2026, 10, 6)
    }));
    store.Create(new("Acceptance subtask", DesktopTaskPriority.Normal,
        card: new() { ParentTaskId = task.Id, ProjectId = project.Id, Description = "Exact subtask description" }));
    store.WriteChecklist(task.Id, Version(task.Id), null, "Exact checklist item", false, false);
    store.Create(new("Acceptance Inbox", DesktopTaskPriority.Normal));
    store.SaveEvent(null, null, new(project.Id, "Acceptance event", "Exact event description",
        new(2026, 10, 6), true, null, null, "UTC"));
    store.CreateContact("Acceptance", "Contact", "Exact contact display");
    var file = Path.Combine(root, "harmless.txt"); File.WriteAllText(file, "Harmless external file, never deleted by Task.");
    var catalog = store.CreateCatalog("Acceptance reference", "file_reference", "Exact catalog description");
    store.AddPersonalLocation(catalog.Id, catalog.Version, file);
    store.AddPersonalLink(task.Id, Version(task.Id), catalog.Id, "task_file");
    store.Transition(new(task.Id, Version(task.Id), DesktopTaskStatus.InProgress));
    var definition = new RecurrenceDefinition
    {
        Frequency = "daily", Interval = 1, OccurrenceStartDate = new(2026, 10, 6), TimeZone = "UTC", MaxOccurrences = 80,
        Template = new() { Title = "Acceptance recurring", Description = "Exact recurrence template", ProjectId = project.Id, AuthorUserId = store.LocalActorId }
    };
    store.ExecuteRecurrence(new DesktopRecurrenceCommand.Save(null, null, definition, "acceptance-series"));
    store.AddReminder(task.Id, DesktopScheduleItemType.Task, ReminderTriggerType.Absolute, absolute: DateTimeOffset.UtcNow.AddMinutes(-1));
    Require(store.ReconcileReminders() == 1 && store.Notifications().Count == 1, "Exactly one notification expected.");
    File.WriteAllText(Path.Combine(root, "application-preferences.json"), "{\"version\":1,\"mode\":\"Personal\"}");
}
if (action == "next")
{
    var series = store.RecurrenceSeries().Single();
    var horizon = series.Definition.NextGenerationDate!.Value;
    var result = store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, series.Version, horizon, "acceptance-next"));
    Require(result.GeneratedCount == 1, "Expected exactly one next occurrence.");
    var repeated = store.ExecuteRecurrence(new DesktopRecurrenceCommand.Generate(series.Id, result.Series!.Version, horizon, "acceptance-next-repeat"));
    Require(repeated.GeneratedCount == 0, "Duplicate recurrence occurrence.");
}
if (action == "lifecycle")
{
    var marker = store.List().Single(t => t.Title == "PERSONAL_SECRET_MARKER");
    foreach (var transition in new[] { "archive", "unarchive", "trash", "restore" })
        store.ChangeWorkspaceLifecycle(marker.Id, Version(marker.Id), transition);
    Require(store.SearchPersonal("PERSONAL_SECRET_MARKER").Count == 1, "Marker lost after lifecycle round trip.");
    var catalog = store.Catalog().Single();
    var file = store.PersonalLocation(catalog.Id)!.RawPath;
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    store.ChangeWorkspaceLifecycle(catalog.Id, Version(catalog.Id), "trash");
    Require(File.Exists(file), "Trash deleted external file.");
    store.ChangeWorkspaceLifecycle(catalog.Id, Version(catalog.Id), "restore");
    var link = store.PersonalLinks(marker.Id).Items.Single();
    store.RemovePersonalLink(marker.Id, Version(marker.Id), link.Id);
    store.ChangeWorkspaceLifecycle(catalog.Id, Version(catalog.Id), "trash");
    store.PurgeWorkspaceObject(catalog.Id, Version(catalog.Id));
    Require(File.Exists(file) && hash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), "Metadata removal changed external file.");
}
if (action == "backup")
{
    // Reuse the production backup service, without adding a production test switch.
    var owner = typeof(PersonalTaskStore).GetField("_ownership", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
    var serviceType = typeof(PersonalTaskStore).Assembly.GetType("Task.Desktop.Personal.PersonalBackupService", true)!;
    var service = Activator.CreateInstance(serviceType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
        null, [new PersonalDataPaths(root), owner, null], null)!;
    serviceType.GetMethod("Backup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [store, Path.Combine(root, "acceptance.taskbackup")]);
}
Require(store.SearchPersonal("CORPORATE_SECRET_MARKER").Count == 0, "Corporate marker leaked into Personal.");
Require(store.ReconcileReminders() == 0 && store.Notifications().Count == 1, "Notification repeated after restart.");
var snapshot = new
{
    Projects = store.Projects(), Tasks = store.List().OrderBy(t => t.Id),
    Checklists = store.List().OrderBy(t => t.Id).Select(t => new { t.Id, Items = store.Checklist(t.Id) }),
    Events = store.Events(), Series = store.RecurrenceSeries(),
    Occurrences = store.RecurrenceSeries().Select(s => store.ExecuteRecurrence(new DesktopRecurrenceCommand.Occurrences(s.Id)).Occurrences),
    Reminders = store.Reminders(),
    Notifications = store.Notifications().Select(n => new { n.Id, n.ReminderId, n.DedupeKey, n.TargetId, n.Title, n.DueAt, n.DeliveredAt, n.IsRead }),
    Contacts = store.Contacts(), Catalog = store.Catalog(),
    Links = store.WorkspaceObjects().OrderBy(o => o.Id).Select(o => new { o.Id, Links = store.PersonalLinks(o.Id) })
};
File.WriteAllText(output, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
// Native presentation bookkeeping changes from pending to submitted at startup.
// Track it separately from the exact business-object snapshot.
File.WriteAllText(output + ".presentations.json", JsonSerializer.Serialize(store.Notifications().Select(n => n.PresentationState)));
Console.WriteLine($"PASS {action}: {store.List().Count} tasks, {store.RecurrenceSeries().Count} series, {store.Notifications().Count} notification.");
