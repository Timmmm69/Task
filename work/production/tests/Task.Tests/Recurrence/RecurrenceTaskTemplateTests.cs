using Task.Domain;
using Task.Domain.Recurrence;

namespace Task.Tests.Recurrence;

public sealed class RecurrenceTaskTemplateTests
{
    private static readonly Guid AuthorId = Guid.Parse("2f1c8d4a-3b2e-4f6a-9c1d-7e5a8b0f3c2d");
    private static readonly Guid AssigneeId = Guid.Parse("9a4b6c2d-1e3f-4a5b-8c7d-2e4f6a8b0c1d");
    private static readonly Guid WatcherId = Guid.Parse("7c2d9f4b-6a8e-4b3c-9d1f-5e7a2c8f0b4d");
    private static readonly Guid RecipientId = Guid.Parse("5e8b3f7a-2c4d-4f9e-8a1b-6d3c5e7f0a2c");
    private static readonly Guid ProjectId = Guid.Parse("3f7a2c9e-5b4d-4e8a-9c6f-1d2b3e4f5a6c");
    private static readonly Guid ChecklistId = Guid.Parse("6d1f4a8b-2c3e-4f7a-9b5d-8e0c1f2a3b4c");
    private static readonly Guid ChecklistItemId = Guid.Parse("8e4b2f6a-1c5d-4e9b-8a3f-7d0c2e5b9a1d");
    private static readonly Guid ReminderRuleId = Guid.Parse("1b7c3e9f-5a2d-4b8e-9f4c-6e1a3d7b0c5f");

    [Fact]
    public void Create_AcceptsACompleteTemplate()
    {
        var template = Template();

        Assert.Equal("Weekly sync report", template.Title);
        Assert.Equal(AuthorId, template.AuthorUserId);
        Assert.Equal(TaskPriority.High, template.Priority);
        Assert.Equal(30, template.PlannedDurationMinutes);
        Assert.Equal(1, template.TemplateVersion);
        Assert.Single(template.AssigneeIds);
        Assert.Single(template.WatcherIds);
        Assert.Single(template.Checklists);
        Assert.Single(template.ReminderRules);
        Assert.Equal(ProjectId, template.ProjectId);
    }

    [Fact]
    public void Create_AcceptsEmptyOptionalCollections()
    {
        var template = Template(assigneeIds: [], watcherIds: [], checklists: [], reminderRules: []);

        Assert.Empty(template.AssigneeIds);
        Assert.Empty(template.WatcherIds);
        Assert.Empty(template.Checklists);
        Assert.Empty(template.ReminderRules);
        Assert.Null(template.Description);
        Assert.Null(template.RequesterUserId);
        Assert.Null(template.PrimaryCounterpartyObjectId);
        Assert.Null(template.DeadlineOffsetMinutes);
    }

    [Fact]
    public void Create_RejectsEmptyOrTooLongTitle()
    {
        Assert.Throws<ArgumentException>(() => Template(title: ""));
        Assert.Throws<ArgumentException>(() => Template(title: "   "));
        Assert.Throws<ArgumentException>(() => Template(title: new string('x', 501)));
        Assert.Equal("Trimmed", Template(title: "  Trimmed  ").Title);
    }

    [Fact]
    public void Create_RejectsTooLongDescription()
    {
        Assert.Throws<ArgumentException>(() => Template(description: new string('x', 50001)));
    }

    [Fact]
    public void Create_RejectsEmptyIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => Template(authorUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Template(requesterUserId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Template(primaryCounterpartyObjectId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Template(assigneeIds: [Guid.Empty]));
        Assert.Throws<ArgumentException>(() => Template(watcherIds: [Guid.Empty]));
    }

    [Fact]
    public void Create_RejectsUndefinedPriorityAndBadDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(priority: (TaskPriority)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(plannedDurationMinutes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(plannedDurationMinutes: 10081));
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(plannedDurationMinutes: -1));
    }

    [Fact]
    public void Create_RejectsExcessiveOrDuplicateUsers()
    {
        Assert.Throws<ArgumentException>(() => Template(assigneeIds: Enumerable.Repeat(AssigneeId, 101).ToArray()));
        Assert.Throws<ArgumentException>(() => Template(assigneeIds: [AssigneeId, AssigneeId]));
        Assert.Throws<ArgumentException>(() => Template(watcherIds: Enumerable.Repeat(WatcherId, 101).ToArray()));
        Assert.Throws<ArgumentException>(() => Template(watcherIds: [WatcherId, WatcherId]));
    }

    [Fact]
    public void Create_RejectsExcessiveChecklistsAndReminderRules()
    {
        var checklist = RecurrenceTemplateChecklist.Create(ChecklistId, "Setup", 0, []);
        var reminder = RecurrenceTemplateReminderRule.Create(ReminderRuleId, RecipientId, RecurrenceReminderTriggerType.AtStart, null);

        Assert.Throws<ArgumentException>(() => Template(checklists: Enumerable.Repeat(checklist, 51).ToList()));
        Assert.Throws<ArgumentException>(() => Template(reminderRules: Enumerable.Repeat(reminder, 51).ToList()));
    }

    [Fact]
    public void Create_RejectsInvalidChecklistContent()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklist.Create(Guid.Empty, "Setup", 0, []));
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklist.Create(ChecklistId, "", 0, []));
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklist.Create(ChecklistId, new string('x', 301), 0, []));
        Assert.Throws<ArgumentException>(() =>
            RecurrenceTemplateChecklist.Create(ChecklistId, "Setup", 0, Enumerable.Range(0, 501).Select(i => Item()).ToArray()));
    }

    [Fact]
    public void Create_RejectsInvalidChecklistItemContent()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklistItem.Create(Guid.Empty, "Text", 0));
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklistItem.Create(ChecklistItemId, "", 0));
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateChecklistItem.Create(ChecklistItemId, new string('x', 1001), 0));
    }

    [Fact]
    public void Create_RejectsInvalidReminderRuleContent()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateReminderRule.Create(Guid.Empty, RecipientId, RecurrenceReminderTriggerType.AtStart, null));
        Assert.Throws<ArgumentException>(() => RecurrenceTemplateReminderRule.Create(ReminderRuleId, Guid.Empty, RecurrenceReminderTriggerType.AtStart, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceTemplateReminderRule.Create(ReminderRuleId, RecipientId, (RecurrenceReminderTriggerType)99, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceTemplateReminderRule.Create(ReminderRuleId, RecipientId, RecurrenceReminderTriggerType.AtStart, -1));
    }

    [Fact]
    public void Create_RejectsNonPositiveTemplateVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(templateVersion: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Template(templateVersion: -1));
    }

    [Fact]
    public void Create_ValueEqualityDetectsFieldAndCollectionChanges()
    {
        var baseTemplate = Template();
        Assert.Equal(Template(), baseTemplate);
        Assert.NotEqual(baseTemplate, Template(title: "Other"));
        Assert.NotEqual(baseTemplate, Template(assigneeIds: [WatcherId]));
        Assert.NotEqual(baseTemplate, Template(templateVersion: 2));

        var differentChecklist = RecurrenceTemplateChecklist.Create(
            ChecklistId,
            "Other",
            0,
            [RecurrenceTemplateChecklistItem.Create(ChecklistItemId, "Different", 0)]);
        Assert.NotEqual(baseTemplate, Template(checklists: [differentChecklist]));
    }

    private static RecurrenceTemplateChecklistItem Item() =>
        RecurrenceTemplateChecklistItem.Create(ChecklistItemId, "Prepare slides", 0);

    private static RecurrenceTemplateChecklist Checklist() =>
        RecurrenceTemplateChecklist.Create(ChecklistId, "Setup", 0, [Item()]);

    private static RecurrenceTemplateReminderRule Reminder() =>
        RecurrenceTemplateReminderRule.Create(ReminderRuleId, RecipientId, RecurrenceReminderTriggerType.BeforeStart, 15);

    private static RecurrenceTaskTemplate Template(
        string? title = null,
        string? description = null,
        Guid? authorUserId = null,
        Guid? requesterUserId = null,
        Guid? primaryCounterpartyObjectId = null,
        TaskPriority priority = TaskPriority.High,
        int? plannedDurationMinutes = 30,
        IReadOnlyList<Guid>? assigneeIds = null,
        IReadOnlyList<Guid>? watcherIds = null,
        IReadOnlyList<RecurrenceTemplateChecklist>? checklists = null,
        IReadOnlyList<RecurrenceTemplateReminderRule>? reminderRules = null,
        long templateVersion = 1) =>
        RecurrenceTaskTemplate.Create(
            ProjectId,
            title ?? "Weekly sync report",
            description,
            authorUserId ?? AuthorId,
            requesterUserId,
            primaryCounterpartyObjectId,
            priority,
            plannedDurationMinutes,
            null,
            assigneeIds ?? [AssigneeId],
            watcherIds ?? [WatcherId],
            checklists ?? [Checklist()],
            reminderRules ?? [Reminder()],
            templateVersion);
}