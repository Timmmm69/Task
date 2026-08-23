using Task.Desktop.Security;

namespace Task.Desktop.ViewModels;

/// <summary>Passwords supplied by PasswordBox controls for one password-change request.</summary>
public sealed record PasswordChangeInput(
    string? CurrentPassword,
    string? NewPassword,
    string? Confirmation);

internal sealed record WorkflowOperationResult(bool Succeeded, string? Message = null);

/// <summary>View model for the first connection and server-change screen.</summary>
public sealed class ServerSetupViewModel : ViewModelBase, IDisposable
{
    private readonly DesktopServerProbeClient _probeClient;
    private readonly Func<Uri, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> _continue;
    private Uri? _verifiedEndpoint;
    private string _address;
    private string? _statusMessage;
    private string? _errorMessage;

    internal ServerSetupViewModel(
        DesktopServerProbeClient probeClient,
        Func<Uri, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> continueAction,
        Uri? initialEndpoint = null)
    {
        _probeClient = probeClient ?? throw new ArgumentNullException(nameof(probeClient));
        _continue = continueAction ?? throw new ArgumentNullException(nameof(continueAction));
        _address = initialEndpoint?.AbsoluteUri ?? string.Empty;

        CheckConnectionCommand = new AsyncCommand(CheckConnectionAsync);
        ContinueCommand = new AsyncCommand(ContinueAsync, _ => _verifiedEndpoint is not null);
        CheckConnectionCommand.ExecutionFailed += HandleUnexpectedFailure;
        ContinueCommand.ExecutionFailed += HandleUnexpectedFailure;
        CheckConnectionCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
        ContinueCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
    }

    public string Address
    {
        get => _address;
        set
        {
            if (!SetProperty(ref _address, value ?? string.Empty))
            {
                return;
            }

            _verifiedEndpoint = null;
            StatusMessage = null;
            ErrorMessage = null;
            OnPropertyChanged(nameof(IsConnectionVerified));
            ContinueCommand.RaiseCanExecuteChanged();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsConnectionVerified => _verifiedEndpoint is not null;

    public bool IsBusy => CheckConnectionCommand.IsExecuting || ContinueCommand.IsExecuting;

    public AsyncCommand CheckConnectionCommand { get; }

    public AsyncCommand ContinueCommand { get; }

    public void Dispose()
    {
        CheckConnectionCommand.Dispose();
        ContinueCommand.Dispose();
    }

    private async global::System.Threading.Tasks.Task CheckConnectionAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        BeginOperation("Проверяем подключение к серверу…");
        try
        {
            var result = await _probeClient.ProbeAsync(Address, cancellationToken).ConfigureAwait(true);
            switch (result)
            {
                case ServerProbeResult.Succeeded { Endpoint: var endpoint }:
                    _verifiedEndpoint = endpoint;
                    Address = endpoint.AbsoluteUri;
                    // Address normalisation invalidates the proof through the setter.
                    _verifiedEndpoint = endpoint;
                    StatusMessage = "Подключение установлено. Сервер готов.";
                    ErrorMessage = null;
                    break;

                case ServerProbeResult.InvalidAddress { Error: var error }:
                    ErrorMessage = MapAddressError(error);
                    break;

                case ServerProbeResult.TlsFailure:
                    ErrorMessage =
                        "Сертификат сервера не прошёл проверку. Task не обходит проверку TLS. " +
                        "Проверьте адрес и обратитесь к ИТ-администратору.";
                    break;

                case ServerProbeResult.Unreachable:
                    ErrorMessage = "Сервер недоступен. Проверьте адрес и подключение к локальной сети.";
                    break;

                case ServerProbeResult.NotReady:
                    ErrorMessage = "Сервер отвечает, но пока не готов к работе. Повторите проверку позже.";
                    break;

                default:
                    ErrorMessage = "Сервер вернул неожиданный ответ. Проверьте адрес или обратитесь к ИТ-администратору.";
                    break;
            }
        }
        finally
        {
            StatusMessage = _verifiedEndpoint is null ? null : StatusMessage;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsConnectionVerified));
            ContinueCommand.RaiseCanExecuteChanged();
        }
    }

    private async global::System.Threading.Tasks.Task ContinueAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        var endpoint = _verifiedEndpoint;
        if (endpoint is null)
        {
            ErrorMessage = "Сначала проверьте подключение к серверу.";
            return;
        }

        BeginOperation("Сохраняем проверенный адрес сервера…");
        try
        {
            var result = await _continue(endpoint, cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message ?? "Не удалось сохранить адрес сервера.";
                StatusMessage = null;
            }
        }
        finally
        {
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private void BeginOperation(string status)
    {
        ErrorMessage = null;
        StatusMessage = status;
        OnPropertyChanged(nameof(IsBusy));
    }

    private void HandleUnexpectedFailure(Exception exception)
    {
        ErrorMessage = "Не удалось выполнить операцию. Повторите попытку.";
        StatusMessage = null;
        OnPropertyChanged(nameof(IsBusy));
    }

    private static string MapAddressError(ServerAddressError error) => error switch
    {
        ServerAddressError.Missing => "Введите адрес сервера.",
        ServerAddressError.Invalid => "Укажите корректный абсолютный адрес сервера.",
        ServerAddressError.NotHttps => "Используйте защищённый адрес HTTPS.",
        ServerAddressError.UserInfoNotAllowed =>
            "Адрес сервера не должен содержать имя пользователя или пароль.",
        ServerAddressError.QueryNotAllowed =>
            "Адрес сервера не должен содержать параметры запроса.",
        ServerAddressError.FragmentNotAllowed =>
            "Адрес сервера не должен содержать фрагмент.",
        _ => "Укажите корректный адрес сервера.",
    };
}

/// <summary>View model for login. It retains the login but never retains a password.</summary>
public sealed class LoginViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string, string, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> _signIn;
    private string _login = string.Empty;
    private string? _statusMessage;
    private string? _errorMessage;

    internal LoginViewModel(
        Func<string, string, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> signIn,
        Func<CancellationToken, global::System.Threading.Tasks.Task> changeServer,
        string? initialLogin,
        string? initialMessage)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        Login = initialLogin ?? string.Empty;
        ErrorMessage = initialMessage;

        SignInCommand = new AsyncCommand(SignInAsync);
        ChangeServerCommand = new AsyncCommand(async (_, cancellationToken) =>
            await changeServer(cancellationToken).ConfigureAwait(true));
        SignInCommand.ExecutionFailed += HandleUnexpectedFailure;
        ChangeServerCommand.ExecutionFailed += HandleUnexpectedFailure;
        SignInCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
        ChangeServerCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
    }

    /// <summary>Requests the PasswordBox bridge to erase its value after an attempt.</summary>
    public event Action? PasswordClearRequested;

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value ?? string.Empty);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy => SignInCommand.IsExecuting || ChangeServerCommand.IsExecuting;

    public AsyncCommand SignInCommand { get; }

    public AsyncCommand ChangeServerCommand { get; }

    public void Dispose()
    {
        SignInCommand.Dispose();
        ChangeServerCommand.Dispose();
    }

    private async global::System.Threading.Tasks.Task SignInAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        var password = parameter as string;
        ErrorMessage = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(password))
            {
                ErrorMessage = "Введите логин и пароль.";
                return;
            }

            StatusMessage = "Выполняется вход…";
            var result = await _signIn(Login.Trim(), password, cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message ?? "Не удалось выполнить вход.";
                StatusMessage = null;
            }
        }
        finally
        {
            password = null;
            RequestPasswordClear();
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private void HandleUnexpectedFailure(Exception exception)
    {
        ErrorMessage = "Не удалось выполнить вход. Повторите попытку.";
        StatusMessage = null;
        RequestPasswordClear();
        OnPropertyChanged(nameof(IsBusy));
    }

    private void RequestPasswordClear()
    {
        try
        {
            PasswordClearRequested?.Invoke();
        }
        catch
        {
            // A closing PasswordBox bridge must not fault the authentication command.
        }
    }
}

/// <summary>View model for the mandatory password-change step.</summary>
public sealed class PasswordChangeViewModel : ViewModelBase, IDisposable
{
    public const string PolicyText =
        "Не менее 10 символов, одна заглавная буква, одна цифра и один специальный символ.";

    private readonly Func<string, string, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> _changePassword;
    private string? _statusMessage;
    private string? _errorMessage;

    internal PasswordChangeViewModel(
        Func<string, string, CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> changePassword,
        Func<CancellationToken, global::System.Threading.Tasks.Task> logout)
    {
        _changePassword = changePassword ?? throw new ArgumentNullException(nameof(changePassword));
        ChangePasswordCommand = new AsyncCommand(ChangePasswordAsync);
        LogoutCommand = new AsyncCommand(async (_, cancellationToken) =>
            await logout(cancellationToken).ConfigureAwait(true));
        ChangePasswordCommand.ExecutionFailed += HandleUnexpectedFailure;
        LogoutCommand.ExecutionFailed += HandleUnexpectedFailure;
        ChangePasswordCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
        LogoutCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
    }

    /// <summary>Requests all three PasswordBox controls to erase their values.</summary>
    public event Action? PasswordsClearRequested;

    public string PasswordPolicy => PolicyText;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy => ChangePasswordCommand.IsExecuting || LogoutCommand.IsExecuting;

    public AsyncCommand ChangePasswordCommand { get; }

    public AsyncCommand LogoutCommand { get; }

    public void Dispose()
    {
        ChangePasswordCommand.Dispose();
        LogoutCommand.Dispose();
    }

    private async global::System.Threading.Tasks.Task ChangePasswordAsync(
        object? parameter,
        CancellationToken cancellationToken)
    {
        var input = parameter as PasswordChangeInput;
        ErrorMessage = null;
        StatusMessage = null;
        OnPropertyChanged(nameof(IsBusy));

        try
        {
            var validationError = Validate(input);
            if (validationError is not null)
            {
                ErrorMessage = validationError;
                return;
            }

            StatusMessage = "Изменяем пароль…";
            var result = await _changePassword(
                input!.CurrentPassword!,
                input.NewPassword!,
                cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message ?? "Не удалось изменить пароль.";
                StatusMessage = null;
            }
        }
        finally
        {
            input = null;
            RequestPasswordsClear();
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private void HandleUnexpectedFailure(Exception exception)
    {
        ErrorMessage = "Не удалось изменить пароль. Повторите попытку.";
        StatusMessage = null;
        RequestPasswordsClear();
        OnPropertyChanged(nameof(IsBusy));
    }

    private void RequestPasswordsClear()
    {
        try
        {
            PasswordsClearRequested?.Invoke();
        }
        catch
        {
            // A closing PasswordBox bridge must not fault the authentication command.
        }
    }

    private static string? Validate(PasswordChangeInput? input)
    {
        if (input is null
            || string.IsNullOrEmpty(input.CurrentPassword)
            || string.IsNullOrEmpty(input.NewPassword)
            || string.IsNullOrEmpty(input.Confirmation))
        {
            return "Заполните все поля.";
        }

        if (!string.Equals(input.NewPassword, input.Confirmation, StringComparison.Ordinal))
        {
            return "Новый пароль и подтверждение не совпадают.";
        }

        var password = input.NewPassword;
        if (password.Length < 10
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character)))
        {
            return "Новый пароль не соответствует требованиям политики.";
        }

        return null;
    }
}

/// <summary>
/// Blocking recovery screen used when tokens still exist but the server could not confirm
/// the current session. The user may retry or sign out; the main shell remains unavailable.
/// </summary>
public sealed class SessionRecoveryViewModel : ViewModelBase, IDisposable
{
    private string _message;
    private string? _statusMessage;
    private string? _errorMessage;

    internal SessionRecoveryViewModel(
        string message,
        Func<CancellationToken, global::System.Threading.Tasks.Task<WorkflowOperationResult>> retry,
        Func<CancellationToken, global::System.Threading.Tasks.Task> logout)
    {
        _message = message;
        RetryCommand = new AsyncCommand(async (_, cancellationToken) =>
        {
            ErrorMessage = null;
            StatusMessage = "Повторно проверяем сессию…";
            OnPropertyChanged(nameof(IsBusy));
            var result = await retry(cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Message ?? "Не удалось подтвердить сессию.";
                StatusMessage = null;
            }
        });
        LogoutCommand = new AsyncCommand(async (_, cancellationToken) =>
            await logout(cancellationToken).ConfigureAwait(true));
        RetryCommand.ExecutionFailed += HandleUnexpectedFailure;
        LogoutCommand.ExecutionFailed += HandleUnexpectedFailure;
        RetryCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
        LogoutCommand.CanExecuteChanged += (_, _) => OnPropertyChanged(nameof(IsBusy));
    }

    public string Message
    {
        get => _message;
        internal set => SetProperty(ref _message, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy => RetryCommand.IsExecuting || LogoutCommand.IsExecuting;

    public AsyncCommand RetryCommand { get; }

    public AsyncCommand LogoutCommand { get; }

    public void Dispose()
    {
        RetryCommand.Dispose();
        LogoutCommand.Dispose();
    }

    private void HandleUnexpectedFailure(Exception exception)
    {
        ErrorMessage = "Не удалось проверить сессию. Повторите попытку или выйдите.";
        StatusMessage = null;
        OnPropertyChanged(nameof(IsBusy));
    }
}
