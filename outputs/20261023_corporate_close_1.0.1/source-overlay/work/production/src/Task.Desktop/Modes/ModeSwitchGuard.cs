using Task.Desktop.ViewModels;

namespace Task.Desktop.Modes;

internal enum ModeSwitchDecision { Save, Discard, Cancel }

internal sealed record ModeSwitchEditor(
    string Name, Func<bool> HasState, Func<bool> IsBusy, AsyncCommand? Save, Func<bool>? IsSaved = null,
    object? SaveParameter = null, Func<bool>? HasUnconfirmedOperation = null);

internal static class ModeSwitchGuard
{
    internal static bool HasPendingOperations(IReadOnlyList<ModeSwitchEditor> editors) =>
        editors.Any(editor => editor.IsBusy() || editor.HasUnconfirmedOperation?.Invoke() == true);

    public static async global::System.Threading.Tasks.Task<bool> PrepareAsync(
        IReadOnlyList<ModeSwitchEditor> editors, Func<bool> canWrite,
        Func<IReadOnlyList<string>, bool, ModeSwitchDecision> choose)
    {
        // Never abandon a write whose server outcome is still unknown.
        if (HasPendingOperations(editors)) return false;
        var pending = editors.Where(editor => editor.HasState()).ToArray();
        if (pending.Length == 0) return true;
        var canSave = canWrite() && pending.All(editor => editor.Save?.CanExecute(editor.SaveParameter) == true);
        var decision = choose(pending.Select(editor => editor.Name).ToArray(), canSave);
        if (decision == ModeSwitchDecision.Cancel) return false;
        // Discard is committed by disposing the old context, after preference persistence.
        if (decision == ModeSwitchDecision.Discard) return !HasPendingOperations(editors);
        if (!canSave) return false;
        foreach (var editor in pending)
        {
            if (!canWrite() || editor.IsBusy() || !editor.Save!.CanExecute(editor.SaveParameter)) return false;
            await editor.Save.ExecuteAsync(editor.SaveParameter);
            // ICommand completion is not proof of a successful server write.
            if (!(editor.IsSaved?.Invoke() ?? !editor.HasState())) return false;
        }
        return !HasPendingOperations(editors) && !editors.Any(editor => editor.HasState());
    }

    public static IReadOnlyList<ModeSwitchEditor> Inspect(MainWindowViewModel shell)
    {
        var editors = new List<ModeSwitchEditor>();
        void Add(string name, Func<bool> state, Func<bool> busy, AsyncCommand? save,
            Func<bool>? saved = null, object? parameter = null, Func<bool>? unconfirmed = null) =>
            editors.Add(new(name, state, busy, save, saved, parameter, unconfirmed));
        if (shell.Tasks is { } tasks)
        {
            Add("Задача", () => tasks.Editor is not null, () => tasks.IsMutationBusy, tasks.SaveEditorCommand, unconfirmed: () => tasks.HasUnconfirmedMutation);
            Add("Изменение статуса задачи", () => tasks.PendingTransition.HasValue,
                () => tasks.ConfirmTransitionCommand.IsExecuting, tasks.ConfirmTransitionCommand);
            if (tasks.Workspace is { } workspace)
            {
                Add("Комментарий задачи", () => !string.IsNullOrEmpty(workspace.Comment), () => workspace.IsBusy, workspace.AddCommentCommand, unconfirmed: () => workspace.HasUnconfirmedMutation);
                Add("Чек-лист задачи", () => !string.IsNullOrEmpty(workspace.CheckText), () => workspace.IsBusy, workspace.AddCheckCommand);
                // Selections for explicit link actions are drafts, not an instruction to create a link.
                Add("Связи задачи", () => workspace.File is not null || workspace.Dependency is not null, () => workspace.IsBusy, null);
            }
        }
        if (shell.Today is { } today)
        {
            Add("Планирование задачи на сегодня", () => today.Planning?.HasChanges == true,
                () => today.IsActionBusy, null, unconfirmed: () => today.HasUnconfirmedMutation);
            Add("Изменение статуса в разделе «Сегодня»", () => today.IsTransitionConfirmationVisible,
                () => today.IsActionBusy, null);
        }
        if (shell.Administration is { } admin)
        {
            Add("Карточка сотрудника", () => admin.IsEditorOpen,
                () => admin.IsSavingUser, admin.SaveUserCommand, unconfirmed: () => admin.HasUnconfirmedMutation);
            Add("Действие с учётной записью", () => admin.IsTransitionOpen,
                () => admin.ConfirmUserTransitionCommand.IsExecuting, null);
        }
        if (shell.Calendar is { } calendar)
        {
            Add("Событие календаря", () => calendar.Editor is not null, () => calendar.IsSaving, calendar.SaveEventCommand, unconfirmed: () => calendar.HasUnconfirmedMutation);
            if (calendar.Recurrence is { } recurrence)
            {
                var revision = recurrence.SuccessfulSaveRevision;
                Add("Повторения", () => recurrence.IsOpen && recurrence.SuccessfulSaveRevision == revision,
                    () => recurrence.IsBusy, recurrence.SaveCommand,
                    () => recurrence.SuccessfulSaveRevision > revision, unconfirmed: () => recurrence.HasUnconfirmedMutation);
            }
        }
        if (shell.Projects is { } projects)
        {
            Add("Проект", () => projects.Editor is not null, () => projects.SaveProjectCommand.IsExecuting, projects.SaveProjectCommand, unconfirmed: () => projects.HasUnconfirmedMutation);
            Add("Новый участник проекта", () => !string.IsNullOrEmpty(projects.NewMemberUserId),
                () => projects.AddMemberCommand.IsExecuting, projects.AddMemberCommand);
            Add("Роли участников проекта", () => projects.Members.Any(member => member.SelectedRole is { } role && role.Id != member.Source.ProjectRoleId),
                () => projects.ChangeMemberRoleCommand.IsExecuting || projects.RemoveMemberCommand.IsExecuting || projects.ArchiveProjectCommand.IsExecuting, null);
        }
        if (shell.Projects?.Files is { } files)
            Add("Связь проекта с файлом", () => files.Choosing, () => files.AddCommand.IsExecuting || files.RemoveCommand.IsExecuting, null, unconfirmed: () => files.HasUnconfirmedMutation);
        if (shell.Projects?.Contacts is { } contacts)
            Add("Связь проекта с контактом", () => contacts.Choosing, () => contacts.AddCommand.IsExecuting || contacts.RemoveCommand.IsExecuting, null, unconfirmed: () => contacts.HasUnconfirmedMutation);
        if (shell.Inbox is { } inbox)
        {
            Add("Преобразование входящей задачи", () => inbox.Conversion is not null, () => inbox.IsBusy, inbox.SaveConversionCommand, unconfirmed: () => inbox.HasUnconfirmedMutation);
            Add("Новая входящая задача", () => !string.IsNullOrEmpty(inbox.CaptureText), () => inbox.CaptureCommand.IsExecuting, inbox.CaptureCommand);
        }
        if (shell.WorkHub is { } work)
        {
            Add("Запись каталога", () => !string.IsNullOrEmpty(work.NewItemName), () => work.HasRunningMutation, work.CreateCatalogItemCommand, unconfirmed: () => work.HasUnconfirmedMutation);
            Add("Путь файла", () => !string.IsNullOrEmpty(work.NewItemPath), () => work.HasRunningMutation, work.AddLocationCommand);
            Add("Контакт", () => !string.IsNullOrEmpty(work.NewContactFirstName + work.NewContactLastName + work.NewContactDisplayName),
                () => work.HasRunningMutation, work.CreateContactCommand);
            if (work.ContactCard is { } contactCard)
            {
                Add("Карточка контакта", () => contactCard.IsEditing, () => work.HasRunningMutation, contactCard.SaveCommand, unconfirmed: () => contactCard.HasUnconfirmedMutation);
                Add("Канал или адрес контакта", () => contactCard.HasEditableRelationDraft, () => work.HasRunningMutation, contactCard.SaveRelationCommand);
                Add("Удаление канала контакта", () => contactCard.IsConfirmingChannelDelete, () => work.HasRunningMutation, null);
            }
            if (work.Companies is { } companies)
                Add("Карточка компании", () => companies.IsEditing, () => work.HasRunningMutation, companies.SaveCommand, unconfirmed: () => companies.HasUnconfirmedMutation);
            foreach (var interactions in new[] { work.ContactInteractions, work.CompanyInteractions })
                if (interactions is not null)
                    Add("Взаимодействие", () => interactions.IsEditing, () => interactions.IsMutating, interactions.SaveCommand, unconfirmed: () => interactions.HasUnconfirmedMutation);
            if (work.Locations is { } locations)
                Add("Расположение файла", () => locations.EditorVisible || locations.ShowDeleteConfirmation, () => locations.IsMutating, null, unconfirmed: () => locations.HasUnconfirmedMutation);
            if (work.Links is { } links)
                Add("Связи объекта", () => !string.IsNullOrEmpty(links.TargetId) || links.TargetPicker?.SelectedTarget is not null,
                    () => links.IsMutating, null, unconfirmed: () => links.HasUnconfirmedMutation);
            Add("Настройки профиля", () => work.HasProfileDraft, () => work.HasRunningMutation, work.SaveUserSettingsCommand);
            Add("Настройки уведомлений", () => work.HasNotificationDraft, () => work.HasRunningMutation, work.SaveNotificationPreferencesCommand);
            Add("Корпоративное напоминание", () => work.HasReminderDraft, () => work.HasRunningMutation, work.SaveReminderCommand);
            Add("Настройки организации", () => work.HasOrganizationDraft, () => work.HasRunningMutation, work.SaveOrganizationSettingsCommand);
        }
        return editors;
    }
}
