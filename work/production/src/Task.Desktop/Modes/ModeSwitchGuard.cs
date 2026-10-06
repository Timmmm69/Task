using Task.Desktop.ViewModels;

namespace Task.Desktop.Modes;

internal enum ModeSwitchDecision { Save, Discard, Cancel }

internal sealed record ModeSwitchEditor(
    string Name, Func<bool> HasState, Func<bool> IsBusy, AsyncCommand? Save, Func<bool>? IsSaved = null,
    object? SaveParameter = null);

internal static class ModeSwitchGuard
{
    public static async global::System.Threading.Tasks.Task<bool> PrepareAsync(
        IReadOnlyList<ModeSwitchEditor> editors, Func<bool> canWrite,
        Func<IReadOnlyList<string>, bool, ModeSwitchDecision> choose)
    {
        // Never abandon a write whose server outcome is still unknown.
        if (editors.Any(editor => editor.IsBusy())) return false;
        var pending = editors.Where(editor => editor.HasState()).ToArray();
        if (pending.Length == 0) return true;
        var canSave = canWrite() && pending.All(editor => editor.Save?.CanExecute(editor.SaveParameter) == true);
        var decision = choose(pending.Select(editor => editor.Name).ToArray(), canSave);
        if (decision == ModeSwitchDecision.Cancel) return false;
        // Discard is committed by disposing the old context, after preference persistence.
        if (decision == ModeSwitchDecision.Discard) return !editors.Any(editor => editor.IsBusy());
        if (!canSave) return false;
        foreach (var editor in pending)
        {
            if (!canWrite() || editor.IsBusy() || !editor.Save!.CanExecute(editor.SaveParameter)) return false;
            await editor.Save.ExecuteAsync(editor.SaveParameter);
            // ICommand completion is not proof of a successful server write.
            if (!(editor.IsSaved?.Invoke() ?? !editor.HasState())) return false;
        }
        return !editors.Any(editor => editor.IsBusy() || editor.HasState());
    }

    public static IReadOnlyList<ModeSwitchEditor> Inspect(MainWindowViewModel shell)
    {
        var editors = new List<ModeSwitchEditor>();
        void Add(string name, Func<bool> state, Func<bool> busy, AsyncCommand? save,
            Func<bool>? saved = null, object? parameter = null) =>
            editors.Add(new(name, state, busy, save, saved, parameter));
        if (shell.Tasks is { } tasks)
        {
            Add("Задача", () => tasks.Editor is not null, () => tasks.IsMutationBusy, tasks.SaveEditorCommand);
            Add("Изменение статуса задачи", () => tasks.PendingTransition.HasValue,
                () => tasks.ConfirmTransitionCommand.IsExecuting, tasks.ConfirmTransitionCommand);
            if (tasks.Workspace is { } workspace)
            {
                Add("Комментарий задачи", () => !string.IsNullOrEmpty(workspace.Comment), () => workspace.IsBusy, workspace.AddCommentCommand);
                Add("Чек-лист задачи", () => !string.IsNullOrEmpty(workspace.CheckText), () => workspace.IsBusy, workspace.AddCheckCommand);
                // Selections for explicit link actions are drafts, not an instruction to create a link.
                Add("Связи задачи", () => workspace.File is not null || workspace.Dependency is not null, () => workspace.IsBusy, null);
            }
        }
        if (shell.Calendar is { } calendar)
        {
            Add("Событие календаря", () => calendar.Editor is not null, () => calendar.IsSaving, calendar.SaveEventCommand);
            if (calendar.Recurrence is { } recurrence)
            {
                var revision = recurrence.SuccessfulSaveRevision;
                Add("Повторения", () => recurrence.IsOpen && recurrence.SuccessfulSaveRevision == revision,
                    () => recurrence.IsBusy, recurrence.SaveCommand,
                    () => recurrence.SuccessfulSaveRevision > revision);
            }
        }
        if (shell.Projects is { } projects)
        {
            Add("Проект", () => projects.Editor is not null, () => projects.SaveProjectCommand.IsExecuting, projects.SaveProjectCommand);
            Add("Новый участник проекта", () => !string.IsNullOrEmpty(projects.NewMemberUserId),
                () => projects.AddMemberCommand.IsExecuting, projects.AddMemberCommand);
            Add("Роли участников проекта", () => projects.Members.Any(member => member.SelectedRole is { } role && role.Id != member.Source.ProjectRoleId),
                () => projects.ChangeMemberRoleCommand.IsExecuting || projects.RemoveMemberCommand.IsExecuting || projects.ArchiveProjectCommand.IsExecuting, null);
        }
        if (shell.Inbox is { } inbox)
        {
            Add("Преобразование входящей задачи", () => inbox.Conversion is not null, () => inbox.IsBusy, inbox.SaveConversionCommand);
            Add("Новая входящая задача", () => !string.IsNullOrEmpty(inbox.CaptureText), () => inbox.CaptureCommand.IsExecuting, inbox.CaptureCommand);
        }
        if (shell.WorkHub is { } work)
        {
            Add("Запись каталога", () => !string.IsNullOrEmpty(work.NewItemName), () => work.HasRunningMutation, work.CreateCatalogItemCommand);
            Add("Путь файла", () => !string.IsNullOrEmpty(work.NewItemPath), () => work.HasRunningMutation, work.AddLocationCommand);
            Add("Контакт", () => !string.IsNullOrEmpty(work.NewContactFirstName + work.NewContactLastName + work.NewContactDisplayName),
                () => work.HasRunningMutation, work.CreateContactCommand);
            Add("Настройки профиля", () => work.HasProfileDraft, () => work.HasRunningMutation, work.SaveUserSettingsCommand);
            Add("Настройки уведомлений", () => work.HasNotificationDraft, () => work.HasRunningMutation, work.SaveNotificationPreferencesCommand);
            Add("Настройки организации", () => work.HasOrganizationDraft, () => work.HasRunningMutation, work.SaveOrganizationSettingsCommand);
        }
        return editors;
    }
}
