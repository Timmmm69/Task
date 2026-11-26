using System.IO;
using Microsoft.Data.Sqlite;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Personal;

public sealed class PersonalInboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Task-personal-inbox", Guid.NewGuid().ToString("N"));
    private PersonalApplicationModel Create()
    {
        var model = new PersonalApplicationModel(_root);
        model.Tasks!.SelectedSection = model.Sections.Single(s => s.Title == "Пометки");
        return model;
    }
    [Fact]
    public async System.Threading.Tasks.Task SearchAndCountUseCaptureRuleAndDoNotChangeTaskFilters()
    {
        using var model = Create(); var vm = model.Tasks!;
        for (var i = 0; i < 205; i++) model.Store.Create(new($"Звонок {i:000}", DesktopTaskPriority.Normal));
        model.Store.Create(new("Звонок с описанием", DesktopTaskPriority.Normal, card: new() { Description = "Уже разобрано" }));
        model.Store.Create(new("Звонок с датой", DesktopTaskPriority.Normal, card: new() { ScheduledDate = DateOnly.FromDateTime(DateTime.Today) }));
        await vm.RefreshAsync();
        Assert.Equal(205, vm.InboxCount); Assert.Equal(205, vm.Items.Count);
        Assert.All(vm.Items, t => Assert.True(InboxItemViewModel.IsCaptureOnly(t.Source)));
        Assert.Equal(vm.Items.OrderByDescending(t => t.Source.CreatedAtUtc).ThenBy(t => t.Source.Id), vm.Items);
        vm.SearchText = "Фильтр раздела Задачи"; vm.StatusFilter = 3; vm.PriorityFilter = 3;
        vm.InboxSearchText = "ЗВОНОК 00"; Assert.Equal(10, vm.Items.Count); Assert.Equal(205, vm.InboxCount);
        vm.InboxSearchText = "нет совпадений"; Assert.True(vm.ShowEmpty); Assert.Equal("Ничего не найдено", vm.EmptyTitle); Assert.Null(vm.Selected);
        vm.InboxSearchText = ""; Assert.Equal(205, vm.Items.Count); Assert.NotNull(vm.Selected);
        Assert.Equal("Фильтр раздела Задачи", vm.SearchText); Assert.Equal(3, vm.StatusFilter);
    }
    [Fact]
    public async System.Threading.Tasks.Task EditorAndCaptureDraftsSurviveSearchRefreshAndBlockedNavigation()
    {
        using var model = Create(); var vm = model.Tasks!;
        model.Store.Create(new("Первая запись", DesktopTaskPriority.Normal));
        model.Store.Create(new("Вторая запись", DesktopTaskPriority.Normal)); await vm.RefreshAsync();
        using var shell = new PersonalShellViewModel(model);
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "inbox");
        var selected = vm.Selected!; var other = vm.Items.Single(t => t.Source.Id != selected.Source.Id);
        await vm.EditCommand.ExecuteAsync(); vm.Editor!.Title = "Черновик названия"; var editor = vm.Editor;
        vm.Selected = other; vm.InboxSearchText = "ничего"; await vm.RefreshAsync();
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        Assert.Same(editor, vm.Editor); Assert.Equal("Черновик названия", editor.Title);
        Assert.Equal(selected.Source.Id, vm.Selected!.Source.Id); Assert.Equal("inbox", shell.SelectedSection.Route);
        vm.CancelCommand.Execute(null); vm.CaptureText = "Черновик быстрой записи";
        vm.InboxSearchText = "Первая"; await vm.RefreshAsync();
        shell.SelectedSection = shell.Sections.Single(s => s.Route == "tasks");
        Assert.Equal("Черновик быстрой записи", vm.CaptureText); Assert.Equal("inbox", shell.SelectedSection.Route);
    }
    [Fact]
    public async System.Threading.Tasks.Task TitleOnlyStaysAndClassificationAdvancesToAdjacentRecord()
    {
        using var model = Create(); var vm = model.Tasks!;
        for (var i = 0; i < 4; i++) model.Store.Create(new($"Запись {i}", DesktopTaskPriority.Normal));
        await vm.RefreshAsync(); var selected = vm.Items[1]; var nextId = vm.Items[2].Source.Id;
        vm.Selected = selected; await vm.EditCommand.ExecuteAsync(); vm.Editor!.Title = "Уточнённое название";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(4, vm.InboxCount); Assert.Equal(selected.Source.Id, vm.Selected?.Source.Id); Assert.Contains("остаётся", vm.Message);
        await vm.EditCommand.ExecuteAsync(); vm.Editor!.Card.Description = "Детали разобранной записи";
        await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(3, vm.InboxCount); Assert.Equal(nextId, vm.Selected?.Source.Id); Assert.DoesNotContain(vm.Items, t => t.Source.Id == selected.Source.Id);
        Assert.NotNull(model.Store.Get(selected.Source.Id)); Assert.Contains("разобрана", vm.Message);
        vm.Selected = vm.Items.Last(); await vm.EditCommand.ExecuteAsync(); vm.Editor!.Card.Date = DateTime.Today.ToString("dd.MM.yyyy");
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal(vm.Items.Last().Source.Id, vm.Selected?.Source.Id);
    }
    [Fact]
    public async System.Threading.Tasks.Task CalendarNote_SurvivesRestartThenConvertsToOrdinaryScheduledTask()
    {
        Guid noteId;
        var date = new DateOnly(2026, 12, 14);
        using (var model = Create())
        {
            var vm = model.Tasks!;
            vm.BeginCalendarCapture(date);
            Assert.Equal(date, vm.CaptureScheduledDate);
            Assert.Null(vm.Editor);
            vm.ClearCalendarDate();
            Assert.Null(vm.CaptureScheduledDate);
            vm.BeginCalendarCapture(date);
            vm.CaptureText = "Позвонить после встречи";
            await vm.CaptureCommand.ExecuteAsync();
            noteId = Assert.Single(vm.Items).Source.Id;
            var saved = model.Store.Get(noteId)!;
            Assert.Equal(date, saved.CalendarNoteDate);
            Assert.Null(saved.Card!.ScheduledDate);
            model.Store.Create(new("Обычная задача с датой", DesktopTaskPriority.Normal, card: new() { ScheduledDate = date }));
        }

        using var restarted = Create();
        var inbox = restarted.Tasks!;
        await inbox.RefreshAsync();
        var note = Assert.Single(inbox.Items);
        Assert.Equal(noteId, note.Source.Id);
        Assert.Equal("14.12.2026", note.ScheduleText);
        Assert.Equal(1, inbox.InboxCount);
        inbox.SelectedSection = restarted.Sections.Single(section => section.Title == "Задачи");
        Assert.Contains(inbox.Items, item => item.Title == "Обычная задача с датой");
        inbox.SelectedSection = restarted.Sections.Single(section => section.Title == "Пометки");
        await inbox.EditCommand.ExecuteAsync();
        Assert.Equal("14.12.2026", inbox.Editor!.Card.Date);
        inbox.Editor.Title = "Позвонить после встречи (уточнено)";
        await inbox.SaveCommand.ExecuteAsync();
        var renamed = restarted.Store.Get(noteId)!;
        Assert.Equal(date, renamed.CalendarNoteDate);
        Assert.Null(renamed.Card!.ScheduledDate);
        Assert.Equal(1, inbox.InboxCount);
        await inbox.EditCommand.ExecuteAsync();
        inbox.Editor.Card.Description = "Перезвонить по итогам";
        await inbox.SaveCommand.ExecuteAsync();

        var converted = restarted.Store.Get(noteId)!;
        Assert.Null(converted.CalendarNoteDate);
        Assert.Equal(date, converted.Card!.ScheduledDate);
        await inbox.RefreshAsync();
        Assert.Empty(inbox.Items);
        Assert.Equal(0, inbox.InboxCount);
    }
    [Fact]
    public async System.Threading.Tasks.Task ValidationAndFailedCapturePreserveTextAndAllowRetry()
    {
        using var model = Create(); var vm = model.Tasks!; await vm.RefreshAsync();
        foreach (var text in new[] { "", "   ", new string('я', 501) })
        {
            vm.CaptureText = text; Assert.False(vm.CaptureCommand.CanExecute(null));
        }
        Assert.NotEmpty(vm.CaptureValidation);
        vm.CaptureText = "Не потерять запись";
        using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new PersonalDataPaths(_root).DatabasePath, Pooling = false }.ToString()); sql.Open();
        using var command = sql.CreateCommand(); command.CommandText = "CREATE TRIGGER reject_inbox BEFORE INSERT ON tasks BEGIN SELECT RAISE(ABORT, 'test'); END;"; command.ExecuteNonQuery();
        await vm.CaptureCommand.ExecuteAsync(); Assert.Equal("Не потерять запись", vm.CaptureText); Assert.Equal(0, vm.InboxCount); Assert.Contains("не сохранены", vm.Message);
        command.CommandText = "DROP TRIGGER reject_inbox;"; command.ExecuteNonQuery();
        await vm.CaptureCommand.ExecuteAsync(); Assert.Empty(vm.CaptureText); Assert.Equal(1, vm.InboxCount);
    }
    [Fact]
    public async System.Threading.Tasks.Task ReadFailureDoesNotClaimInboxIsEmptyAndRecoveryClearsError()
    {
        using var model = Create(); var vm = model.Tasks!; await vm.RefreshAsync(); Assert.True(vm.ShowEmpty);
        using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new PersonalDataPaths(_root).DatabasePath, Pooling = false }.ToString()); sql.Open();
        using var command = sql.CreateCommand(); command.CommandText = "ALTER TABLE tasks RENAME TO unavailable_tasks"; command.ExecuteNonQuery();
        await vm.RefreshAsync(); Assert.True(vm.InboxReadFailed); Assert.False(vm.ShowEmpty);
        command.CommandText = "ALTER TABLE unavailable_tasks RENAME TO tasks"; command.ExecuteNonQuery();
        await vm.RefreshAsync(); Assert.False(vm.InboxReadFailed); Assert.True(vm.ShowEmpty); Assert.Empty(vm.Message);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
