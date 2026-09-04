using System.Text.Json.Nodes;
using Task.Desktop.ViewModels;
using Task.Domain;
namespace Task.Desktop.Tests.TaskScreen;

public sealed class TaskCardEditorTests
{
    [Fact]
    public void SearchRetainsSelectionsAndExplicitClear()
    {
        var project = Guid.NewGuid(); var user = Guid.NewGuid();
        var editor = new TaskCardEditor(new() { ProjectId = project, AssigneeIds = [user] });
        editor.Project = editor.Projects.Single(p => p.Id is null);
        editor.SetOptions(new JsonObject());
        var content = editor.Build(null);
        Assert.Null(content.ProjectId); Assert.Equal([user], content.AssigneeIds);
        Assert.Contains("projectId", editor.Patch(content));
    }
    [Fact]
    public void DescriptionOnlyPatchPreservesOtherFields()
    {
        var source = new TaskCardContent { Description = "До", ScheduledDate = new(2026, 9, 4), PlannedDurationMinutes = 30 };
        var editor = new TaskCardEditor(source) { Description = "После" };
        var patch = JsonNode.Parse(editor.Patch(editor.Build(null))!)!.AsObject();
        Assert.Single(patch); Assert.Equal("После", patch["description"]!.ToString());
        editor.Duration = "10081"; Assert.Throws<ArgumentException>(() => editor.Build(null));
    }
}
