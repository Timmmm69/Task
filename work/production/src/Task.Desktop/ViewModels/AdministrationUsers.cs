using Task.Desktop.Administration;

namespace Task.Desktop.ViewModels;

public sealed partial class AdministrationViewModel
{
    private AdminUserEditor? _userEditor;
    private CancellationTokenSource? _userDetails;
    private int _userGeneration, _accessGeneration;
    private bool _editorLoading, _savingUser, _versionConflict, _uncertainSave;
    private string? _userEditorMessage, _saveKey;
    private DesktopUserCreate? _pendingCreate;
    private DesktopUserPatch? _pendingPatch;
    private string? _nextUserCursor;
    private string? _userSearchText, _appliedUserFilter;

    public AsyncCommand CreateUserCommand { get; private set; } = null!;
    public AsyncCommand EditUserCommand { get; private set; } = null!;
    public AsyncCommand SaveUserCommand { get; private set; } = null!;
    public AsyncCommand CancelUserCommand { get; private set; } = null!;
    public AsyncCommand MoreUsersCommand { get; private set; } = null!;
    public AsyncCommand SearchUsersCommand { get; private set; } = null!;
    public string? UserSearchText { get => _userSearchText; set => SetProperty(ref _userSearchText, value); }
    public bool CanSearchUsers => CanAccessUsers && !IsLoading && !IsEditorOpen && !_editorLoading && !_savingUser && !IsTransitionOpen && !_transitionBusy;
    public AdminUserEditor? UserEditor { get => _userEditor; private set { SetProperty(ref _userEditor, value); NotifyUserCommands(); } }
    public string? UserEditorMessage { get => _userEditorMessage; private set => SetProperty(ref _userEditorMessage, value); }
    public bool IsEditorOpen => UserEditor is not null;
    public bool IsUserInspectorVisible => IsUsers && !IsEditorOpen;
    public bool IsUserEditorLoading => _editorLoading;
    public bool CanSelectUser => !IsEditorOpen && !_savingUser && !IsTransitionOpen && !_transitionBusy;
    public bool CanEditUserFields => CanWriteUsers && !_savingUser && !_uncertainSave;
    internal bool HasUnconfirmedMutation => _uncertainSave || _transitionUncertain;

    public bool IsSavingUser => _savingUser;
    public bool IsUserListEmpty => IsUsers && !IsLoading && Users.Count == 0 && CanReadCurrentArea && Message is null;
    public bool HasMoreUsers => _nextUserCursor is not null;
    private bool CanAccessUsers => _active && _sessionAvailable && _networkAvailable && IsUsers
        && _capabilities.Contains("User.Read");
    private bool CanWriteUsers => CanAccessUsers && _capabilities.Contains(UserEditor?.Original is null ? "User.Create" : "User.Update");
    public string UserWriteAccessText => !_sessionAvailable ? "Сессия завершена. Выполните вход снова."
        : !_networkAvailable ? "Офлайн · сохранение недоступно. Локальная очередь изменений не создаётся."
        : !_capabilities.Contains("User.Create") && !_capabilities.Contains("User.Update")
            ? "Только чтение · создание и редактирование недоступны."
        : "Создание и редактирование доступны в пределах ваших прав.";

    private void InitializeUserCommands()
    {
        CreateUserCommand = new((_, _) =>
        {
            InvalidateUserDetails(); ResetUserOperation(); UserEditorMessage = null;
            UserEditor = new(); return System.Threading.Tasks.Task.CompletedTask;
        }, _ => CanAccessUsers && _capabilities.Contains("User.Create") && !IsEditorOpen && !_editorLoading && !IsLoading && !IsTransitionOpen && !_transitionBusy);
        EditUserCommand = new(OpenUserEditorAsync, _ => CanAccessUsers && _capabilities.Contains("User.Update")
            && SelectedUser is not null && !IsEditorOpen && !_editorLoading && !IsLoading && !IsTransitionOpen && !_transitionBusy);
        SaveUserCommand = new(SaveUserAsync, _ => UserEditor is not null && CanWriteUsers && !_versionConflict && !_savingUser);
        CancelUserCommand = new((_, _) =>
        {
            if (_uncertainSave) Message = "Результат предыдущего сохранения неизвестен. Обновите список и проверьте учётную запись перед созданием новой.";
            InvalidateUserDetails(); UserEditor = null; UserEditorMessage = null; ResetUserOperation();
            return System.Threading.Tasks.Task.CompletedTask;
        }, _ => !_savingUser && (IsEditorOpen || _editorLoading));
        MoreUsersCommand = new(async (_, ct) => await LoadUserPageAsync(true, ct),
            _ => CanAccessUsers && HasMoreUsers && !IsLoading && !IsEditorOpen && !IsTransitionOpen && !_transitionBusy);
        SearchUsersCommand = new(async (_, ct) =>
        {
            if (UserSearchText?.Length > 2000) { Message = "Поисковый запрос должен содержать не более 2000 символов."; return; }
            _appliedUserFilter = string.IsNullOrWhiteSpace(UserSearchText) ? null : UserSearchText.Trim();
            _nextUserCursor = null; Message = null;
            await LoadUserPageAsync(false, ct);
        }, _ => CanSearchUsers);
        foreach (var command in new[] { CreateUserCommand, EditUserCommand, SaveUserCommand, MoreUsersCommand, SearchUsersCommand })
            command.ExecutionFailed += _ =>
            {
                UserEditorMessage = "Не удалось выполнить операцию. Ввод сохранён; подтверждённые данные не изменены.";
                Message = UserEditorMessage;
            };
    }

    private void InvalidateUserDetails()
    {
        ClearTransition(); ++_userGeneration; _userDetails?.Cancel(); _userDetails?.Dispose(); _userDetails = null;
        _editorLoading = false; OnPropertyChanged(nameof(IsUserEditorLoading));
    }

    private async System.Threading.Tasks.Task OpenUserEditorAsync(object? _, CancellationToken ct)
    {
        var id = SelectedUser!.Id;
        InvalidateUserDetails();
        var generation = _userGeneration;
        var access = _accessGeneration;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _userDetails = linked;
        _editorLoading = true; UserEditorMessage = null; Message = null; NotifyUserCommands();
        try
        {
            var result = await _client.GetUserDetailsAsync(id, linked.Token);
            if (linked.IsCancellationRequested || generation != _userGeneration || access != _accessGeneration
                || SelectedUser?.Id != id || !CanAccessUsers || !_capabilities.Contains("User.Update")) return;
            if (result is DesktopUserResult<DesktopManagedUser>.Succeeded ok)
            {
                ResetUserOperation(); UserEditor = new(ok.Value);
            }
            else { InvalidateDeniedUserRead(result); SetUserFailure(result); Message = UserEditorMessage; }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally
        {
            if (generation == _userGeneration) { _editorLoading = false; _userDetails = null; NotifyUserCommands(); }
        }
    }

    private async System.Threading.Tasks.Task SaveUserAsync(object? _, CancellationToken ct)
    {
        var editor = UserEditor!;
        var errors = editor.Validate();
        if (errors.Count > 0) { UserEditorMessage = string.Join("\n", errors); return; }
        if (!editor.HasChanges) { UserEditorMessage = "Нет изменений для сохранения."; return; }
        // An uncertain mutation can only be retried explicitly with its original bytes/key/version.
        _saveKey ??= Guid.NewGuid().ToString("D");
        _pendingCreate ??= editor.Original is null ? editor.Create() : null;
        _pendingPatch ??= editor.Original is null ? null : editor.Patch();
        _uncertainSave = true;
        _savingUser = true; UserEditorMessage = "Сохранение…"; NotifyUserCommands();
        var access = _accessGeneration;
        try
        {
            var result = editor.Original is null
                ? await _client.CreateUserAsync(_pendingCreate!, _saveKey, ct)
                : await _client.UpdateUserAsync(editor.Original.Id, editor.Original.Version, _pendingPatch!, _saveKey, ct);
            if (result is DesktopUserResult<DesktopManagedUser>.Succeeded ok)
            {
                if (access != _accessGeneration || !_sessionAvailable || !_capabilities.Contains("User.Read")) return;
                UserEditor = null; ResetUserOperation();
                // The mutation receipt is confirmed server data even if the following refresh fails.
                UpsertUser(ok.Value);
                Message = null;
                try { await LoadUserPageAsync(false, ct, ok.Value); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Message = "Список не обновлён. Сохранённая запись подтверждена ответом сервера."; }
                var refreshMessage = Message;
                Message = (editor.Original is null
                    ? "Пользователь создан · ожидает активации. Для входа потребуется отдельный процесс получения учётных данных."
                    : "Изменения сотрудника сохранены.") + (refreshMessage is null ? "" : " Обновление списка: " + refreshMessage);
            }
            else
            {
                _uncertainSave = result is DesktopUserResult<DesktopManagedUser>.ServerUnavailable
                    or DesktopUserResult<DesktopManagedUser>.MalformedResponse
                    || result.ErrorCode == "REQUEST_IN_PROGRESS";
                if (!_uncertainSave) { _saveKey = null; _pendingCreate = null; _pendingPatch = null; }
                _versionConflict = result is DesktopUserResult<DesktopManagedUser>.VersionConflict
                    or DesktopUserResult<DesktopManagedUser>.PreconditionRequired;
                SetUserFailure(result);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _uncertainSave = true;
            UserEditorMessage = "Результат сохранения не подтверждён. Ввод сохранён. Повторное сохранение отправит тот же запрос с тем же ключом.";
        }
        finally { _savingUser = false; NotifyUserCommands(); }
    }

    private static DesktopAdminUser Summary(DesktopManagedUser u) => new(u.Id, u.Version, u.DisplayName, u.Login, u.JobTitle, u.AccountStatus);
    private void UpsertUser(DesktopManagedUser user)
    {
        var item = Summary(user);
        Users = Users.Where(u => u.Id != user.Id).Append(item).OrderBy(u => u.Id).ToArray();
        // A lifecycle receipt updates the locked selection without cancelling its confirmation.
        if (_transitionBusy) { _selectedUser = item; OnPropertyChanged(nameof(SelectedUser)); NotifyUserCommands(); }
        else SelectedUser = item;
    }

    private async System.Threading.Tasks.Task LoadUserPageAsync(bool append, CancellationToken ct, DesktopManagedUser? saved = null)
    {
        if (!CanAccessUsers) return;
        var access = _accessGeneration;
        var area = SelectedTab;
        var selected = saved?.Id ?? SelectedUser?.Id;
        IsLoading = true; NotifyUserCommands();
        try
        {
            var result = await _client.GetUserPageAsync(new(Filter: _appliedUserFilter, Cursor: append ? _nextUserCursor : null), ct);
            if (ct.IsCancellationRequested || access != _accessGeneration || SelectedTab != area || !CanAccessUsers) return;
            if (result is DesktopUserResult<DesktopUserPage>.Succeeded ok)
            {
                Users = (append ? Users.Concat(ok.Value.Items.Select(Summary)) : ok.Value.Items.Select(Summary)).DistinctBy(u => u.Id).ToArray();
                _nextUserCursor = ok.Value.NextCursor;
                if (saved is not null) UpsertUser(saved);
                else SelectedUser = Users.FirstOrDefault(u => u.Id == selected) ?? Users.FirstOrDefault();
                _lastRefresh = DateTimeOffset.UtcNow; OnPropertyChanged(nameof(LastSuccessfulRefreshText));
            }
            else
            {
                InvalidateDeniedUserRead(result);
                SetUserFailure(result); Message = UserEditorMessage;
            }
        }
        finally { IsLoading = false; NotifyUserCommands(); }
    }

    private void InvalidateDeniedUserRead<T>(DesktopUserResult<T> result)
    {
        if (result is DesktopUserResult<T>.Forbidden)
            UpdateCapabilities(_capabilities.Where(code => !string.Equals(code, "User.Read", StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    private void SetUserFailure<T>(DesktopUserResult<T> result)
    {
        UserEditorMessage = result switch
        {
            DesktopUserResult<T>.Conflict when result.ErrorCode == "DUPLICATE_RESOURCE" => "Логин или рабочий email уже используется. Проверьте оба поля: сервер не уточняет, какое из них занято.",
            DesktopUserResult<T>.VersionConflict or DesktopUserResult<T>.PreconditionRequired => "Запись изменена другим администратором. Ваш ввод сохранён. Скопируйте нужные изменения, отмените редактирование и откройте актуальную запись снова.",
            DesktopUserResult<T>.Forbidden => "Недостаточно прав для операции. Ввод сохранён.",
            DesktopUserResult<T>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopUserResult<T>.NotFound => "Запись удалена или недоступна. Изменения не сохранены.",
            DesktopUserResult<T>.ValidationFailure => "Сервер отклонил данные. Проверьте имя, фамилию, отображаемое имя, логин, рабочий email и должность.",
            DesktopUserResult<T>.ServerUnavailable => "Сервер недоступен. Результат не подтверждён. Ввод сохранён; после восстановления связи повторите сохранение с тем же ключом.",
            DesktopUserResult<T>.Conflict when result.ErrorCode == "REQUEST_IN_PROGRESS" => "Запрос ещё обрабатывается. Повторное сохранение проверит тот же запрос; поля временно заблокированы.",
            DesktopUserResult<T>.Conflict => "Операция отклонена сервером. Ввод сохранён. Обновите запись перед новой попыткой.",
            _ => "Ответ сервера не подтверждён. Ввод сохранён; повторное сохранение отправит тот же запрос с тем же ключом.",
        };
        if (result is DesktopUserResult<T>.AuthenticationFailure) UpdateSessionState(false);
        if (result is DesktopUserResult<T>.ServerUnavailable) UpdateConnectivity(false);
    }
    private void ResetUserOperation()
    {
        _saveKey = null; _pendingCreate = null; _pendingPatch = null;
        _versionConflict = false; _uncertainSave = false;
    }
    private void NotifyUserCommands()
    {
        foreach (var name in new[] { nameof(IsEditorOpen), nameof(IsUserInspectorVisible), nameof(CanSelectUser),
            nameof(CanEditUserFields), nameof(IsUserEditorLoading), nameof(IsSavingUser), nameof(IsUserListEmpty), nameof(HasMoreUsers), nameof(UserWriteAccessText), nameof(CanSearchUsers) })
            OnPropertyChanged(name);
        CreateUserCommand?.RaiseCanExecuteChanged(); EditUserCommand?.RaiseCanExecuteChanged();
        SaveUserCommand?.RaiseCanExecuteChanged(); CancelUserCommand?.RaiseCanExecuteChanged(); MoreUsersCommand?.RaiseCanExecuteChanged();
        SearchUsersCommand?.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged(); NotifyTransitionCommands();
    }
}
