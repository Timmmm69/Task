using Task.Desktop.Administration;

namespace Task.Desktop.ViewModels;

public sealed partial class AdministrationViewModel
{
    private DesktopManagedUser? _transitionUser;
    private DesktopUserTransition _transition;
    private bool _transitionBusy, _transitionUncertain, _transitionConflict;
    private string? _transitionReason, _transitionKey, _sentReason;

    public AsyncCommand BeginUserTransitionCommand { get; private set; } = null!;
    public AsyncCommand ConfirmUserTransitionCommand { get; private set; } = null!;
    public AsyncCommand CancelUserTransitionCommand { get; private set; } = null!;
    public bool IsTransitionOpen => _transitionUser is not null;
    public bool CanEditTransitionReason => !_transitionBusy && !_transitionUncertain;
    public string TransitionConfirmLabel => _transitionUncertain ? "Проверить исходный запрос" : "Подтвердить";
    public bool TransitionRequiresReason => RequiresReason(_transition);
    public string? TransitionReason { get => _transitionReason; set => SetProperty(ref _transitionReason, value); }
    public string TransitionTitle => $"{ActionLabel(_transition)}: {_transitionUser?.DisplayName} ({_transitionUser?.Login})";
    public string TransitionConsequences => _transition == DesktopUserTransition.Activate
        ? "Учётная запись станет активной. Учётные данные этим действием не выдаются."
        : _transition is DesktopUserTransition.Block or DesktopUserTransition.Deactivate
            ? "Вход будет недоступен. Сервер отзовёт существующие сессии и refresh-токены сотрудника."
            : "Учётная запись станет активной. Сервер отзовёт существующие сессии и refresh-токены сотрудника; потребуется новый вход.";

    public bool ShowActivate => ShowTransition(DesktopUserTransition.Activate);
    public bool ShowBlock => ShowTransition(DesktopUserTransition.Block);
    public bool ShowUnblock => ShowTransition(DesktopUserTransition.Unblock);
    public bool ShowDeactivate => ShowTransition(DesktopUserTransition.Deactivate);
    public bool ShowReactivate => ShowTransition(DesktopUserTransition.Reactivate);

    // This is a presentation filter for effective actions, not an alternative to server validation.
    // The server also accepts block/unblock/reactivate no-ops; those need no separate UI action.
    private static bool Applicable(string status, DesktopUserTransition action) => action switch
    {
        DesktopUserTransition.Activate => status == "pending_activation",
        DesktopUserTransition.Block => status == "active",
        DesktopUserTransition.Unblock => status == "blocked",
        DesktopUserTransition.Deactivate => status is "pending_activation" or "active" or "blocked",
        DesktopUserTransition.Reactivate => status == "deactivated",
        _ => false,
    };
    private static string Permission(DesktopUserTransition action) => action == DesktopUserTransition.Activate ? "User.Create" : "User.Block";
    private static bool RequiresReason(DesktopUserTransition action) => action is DesktopUserTransition.Block or DesktopUserTransition.Deactivate;
    private static string ActionLabel(DesktopUserTransition action) => action switch
    {
        DesktopUserTransition.Activate => "Активировать", DesktopUserTransition.Block => "Заблокировать",
        DesktopUserTransition.Unblock => "Разблокировать", DesktopUserTransition.Deactivate => "Деактивировать",
        DesktopUserTransition.Reactivate => "Восстановить учётную запись", _ => "Неизвестное действие",
    };
    private bool ShowTransition(DesktopUserTransition action) => IsUsers && !IsEditorOpen && !IsTransitionOpen
        && SelectedUser is { } user && _capabilities.Contains("User.Read") && _capabilities.Contains(Permission(action))
        && Applicable(user.AccountStatus, action);
    private bool CanBeginTransition(object? value) => value is DesktopUserTransition action && ShowTransition(action)
        && CanAccessUsers && !_transitionBusy && !_savingUser && !_editorLoading && !IsLoading;
    private bool CanConfirmTransition => IsTransitionOpen && CanAccessUsers && _capabilities.Contains(Permission(_transition))
        && !_transitionBusy && !_transitionConflict && SelectedUser?.Id == _transitionUser?.Id;

    private void InitializeTransitionCommands()
    {
        BeginUserTransitionCommand = new(BeginTransitionAsync, CanBeginTransition);
        ConfirmUserTransitionCommand = new(ConfirmTransitionAsync, _ => CanConfirmTransition);
        CancelUserTransitionCommand = new((_, _) =>
        {
            Message = _transitionUncertain ? "Результат операции неизвестен. Обновите запись перед новым действием." : "Действие отменено.";
            ClearTransition(); NotifyUserCommands(); return System.Threading.Tasks.Task.CompletedTask;
        }, _ => IsTransitionOpen && !_transitionBusy);
    }

    private async System.Threading.Tasks.Task BeginTransitionAsync(object? value, CancellationToken ct)
    {
        var action = (DesktopUserTransition)value!;
        var id = SelectedUser!.Id;
        var generation = _userGeneration;
        var access = _accessGeneration;
        _transitionBusy = true; Message = "Загрузка актуальной записи…"; NotifyUserCommands();
        try
        {
            var result = await _client.GetUserDetailsAsync(id, ct);
            if (ct.IsCancellationRequested || generation != _userGeneration || access != _accessGeneration
                || !CanAccessUsers || !_capabilities.Contains(Permission(action)) || SelectedUser?.Id != id) return;
            if (result is DesktopUserResult<DesktopManagedUser>.Succeeded ok && ok.Value.Id == id)
            {
                UpsertUser(ok.Value);
                if (!Applicable(ok.Value.AccountStatus, action))
                { Message = "Состояние записи изменилось. Выберите доступное действие заново."; return; }
                _transition = action; _transitionUser = ok.Value;
                _transitionKey = Guid.NewGuid().ToString("D");
                Message = null;
            }
            else TransitionFailure(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { Message = "Действие отменено."; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Message = "Актуальная запись не получена. Изменение не отправлено."; }
        finally { _transitionBusy = false; NotifyUserCommands(); }
    }

    private async System.Threading.Tasks.Task ConfirmTransitionAsync(object? _, CancellationToken ct)
    {
        if (!_transitionUncertain && TransitionRequiresReason
            && (string.IsNullOrWhiteSpace(TransitionReason) || TransitionReason.Length > 2000))
        { Message = "Укажите причину: от 1 до 2000 символов."; return; }
        var user = _transitionUser!;
        var access = _accessGeneration;
        if (!_transitionUncertain) _sentReason = TransitionRequiresReason ? TransitionReason!.Trim() : null;
        _transitionBusy = true; _transitionUncertain = true;
        Message = "Ожидание подтверждения сервера…"; NotifyUserCommands();
        try
        {
            var result = await _client.TransitionUserAsync(user.Id, user.Version, _transition, _transitionKey, _sentReason, ct);
            if (access != _accessGeneration || !CanAccessUsers || !_capabilities.Contains(Permission(_transition))) return;
            if (result is DesktopUserResult<DesktopManagedUser>.Succeeded ok && ok.Value.Id == user.Id)
            {
                // Use the authoritative receipt, including unchanged versions on idempotent no-ops.
                UpsertUser(ok.Value); ClearTransition();
                Message = ok.Replayed ? "Сервер подтвердил результат ранее выполненного действия." : "Изменение состояния подтверждено сервером.";
            }
            else
            {
                _transitionUncertain = result is DesktopUserResult<DesktopManagedUser>.ServerUnavailable
                    or DesktopUserResult<DesktopManagedUser>.MalformedResponse || result.ErrorCode == "REQUEST_IN_PROGRESS";
                _transitionConflict = !_transitionUncertain;
                TransitionFailure(result);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Message = "Результат не подтверждён. Явное повторное подтверждение отправит тот же запрос с исходной версией и ключом."; }
        finally { _transitionBusy = false; NotifyUserCommands(); }
    }

    private void TransitionFailure<T>(DesktopUserResult<T> result)
    {
        Message = result switch
        {
            DesktopUserResult<T>.Forbidden => "Недостаточно прав для изменения состояния.",
            DesktopUserResult<T>.AuthenticationFailure => "Сессия завершена. Выполните вход снова.",
            DesktopUserResult<T>.NotFound => "Учётная запись недоступна. Обновите список.",
            DesktopUserResult<T>.VersionConflict or DesktopUserResult<T>.PreconditionRequired => "Версия записи изменилась. Отмените действие и откройте его заново для проверки и нового подтверждения.",
            _ when result.ErrorCode == "INVALID_STATE_TRANSITION" => "Сервер запретил переход: он недопустим для текущего состояния либо лишает организацию последнего активного администратора. Обновите запись.",
            _ when result.ErrorCode == "REQUEST_IN_PROGRESS" => "Запрос ещё обрабатывается. Повторное подтверждение проверит тот же запрос с исходной версией и ключом.",
            _ when result.ErrorCode == "IDEMPOTENCY_KEY_REUSED" => "Ключ запроса уже использован. Отмените действие и проверьте актуальную запись.",
            DesktopUserResult<T>.ValidationFailure => "Сервер отклонил запрос. Отмените действие и проверьте причину и запись.",
            DesktopUserResult<T>.ServerUnavailable => "Соединение потеряно. Результат не подтверждён; офлайн-очередь не создаётся. После восстановления связи явное подтверждение повторит исходный запрос.",
            _ => "Результат не подтверждён. Повторное подтверждение отправит исходный запрос; состояние локально не изменено.",
        };
        if (result is DesktopUserResult<T>.AuthenticationFailure) UpdateSessionState(false);
        if (result is DesktopUserResult<T>.ServerUnavailable) UpdateConnectivity(false);
    }

    private void ClearTransition()
    {
        _transitionUser = null; _transitionReason = null; _sentReason = null; _transitionKey = null;
        _transitionUncertain = false; _transitionConflict = false;
    }
    private void NotifyTransitionCommands()
    {
        foreach (var name in new[] { nameof(IsTransitionOpen), nameof(CanEditTransitionReason), nameof(TransitionRequiresReason), nameof(TransitionConfirmLabel),
            nameof(TransitionReason), nameof(TransitionTitle), nameof(TransitionConsequences), nameof(ShowActivate), nameof(ShowBlock),
            nameof(ShowUnblock), nameof(ShowDeactivate), nameof(ShowReactivate) }) OnPropertyChanged(name);
        BeginUserTransitionCommand?.RaiseCanExecuteChanged(); ConfirmUserTransitionCommand?.RaiseCanExecuteChanged(); CancelUserTransitionCommand?.RaiseCanExecuteChanged();
    }
}
