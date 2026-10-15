using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Domain;
using static Task.Desktop.Tests.TaskScreen.InboxViewModelTests;

namespace Task.Desktop.Tests.TaskScreen;

public sealed class InboxRetargetTests
{
    [Fact]
    public void RetargetPreservesUneditedServerFieldsAndOnlyPatchesTheUserDraft()
    {
        var source = CreateTask("Source");
        var task = source with { Id = Guid.NewGuid(), Title = "Server title", Priority = DesktopTaskPriority.High,
            DeadlineAtUtc = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), Card = new TaskCardContent { Description = "Server description" } };
        var editor = new TaskEditorViewModel(TaskEditorMode.Edit, source);
        editor.Card.Duration = "45";
        editor.RetargetConversion(task);
        Assert.Equal("Server title", editor.Title); Assert.Equal(DesktopTaskPriority.High, editor.Priority.Value);
        Assert.Equal("Server description", editor.Card.Description);
        var patch = editor.BuildPatchCommand()!;
        Assert.Equal(task.Id, patch.Id); Assert.False(patch.Title.IsSpecified); Assert.False(patch.Priority.IsSpecified);
        Assert.False(patch.DeadlineAtUtc.IsSpecified);
        var card = JsonNode.Parse(patch.CardPatch!)!.AsObject();
        Assert.Equal(45, card["plannedDurationMinutes"]!.GetValue<int>());
        Assert.False(card.ContainsKey("description"));
    }

    [Fact]
    public void RetargetKeepsExplicitEditsAndEmptyDeadlineDoesNotCreateFakeChanges()
    {
        var source = CreateTask("Source");
        var editor = new TaskEditorViewModel(TaskEditorMode.Edit, source) { Title = "User title" };
        editor.Card.Description = "User description";
        var task = source with { Id = Guid.NewGuid(), Card = new TaskCardContent { Description = "Other description" } };
        editor.RetargetConversion(task);
        Assert.Equal("User title", editor.Title); Assert.Equal("User description", editor.Card.Description);
        Assert.Equal("", editor.DeadlineText);
        var patch = editor.BuildPatchCommand()!;
        Assert.Equal("User title", patch.Title.Value); Assert.False(patch.DeadlineAtUtc.IsSpecified);
        Assert.Equal("User description", JsonNode.Parse(patch.CardPatch!)!["description"]!.GetValue<string>());
    }
}
