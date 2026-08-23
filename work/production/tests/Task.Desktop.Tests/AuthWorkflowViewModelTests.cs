using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using Task.Desktop.Security;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public sealed class AuthWorkflowViewModelTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task FirstLaunch_ShowsServerSetup_AndBlocksReady()
    {
        using var harness = new WorkflowHarness();
        using var workflow = harness.CreateWorkflow();

        await workflow.StartAsync();

        Assert.Equal(AuthWorkflowState.ServerSetup, workflow.CurrentState);
        Assert.NotNull(workflow.ServerSetup);
        Assert.False(workflow.IsReady);
        Assert.Empty(harness.AuthRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ServerSetup_RejectsHttpLocally_WithSafeRussianMessage()
    {
        using var harness = new WorkflowHarness();
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.ServerSetup!.Address = "http://task.local";

        await workflow.ServerSetup.CheckConnectionCommand.ExecuteAsync();

        Assert.Contains("HTTPS", workflow.ServerSetup.ErrorMessage, StringComparison.Ordinal);
        Assert.False(workflow.ServerSetup.IsConnectionVerified);
        Assert.Empty(harness.ProbeRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ServerSetup_TlsFailure_IsDistinct_AndCannotContinue()
    {
        using var harness = new WorkflowHarness
        {
            ProbeResponder = (_, _) => throw new HttpRequestException(
                "TLS failed",
                new AuthenticationException("certificate rejected")),
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.ServerSetup!.Address = "https://tls.task.local";

        await workflow.ServerSetup.CheckConnectionCommand.ExecuteAsync();

        Assert.Contains("сертификат", workflow.ServerSetup.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TLS", workflow.ServerSetup.ErrorMessage!, StringComparison.Ordinal);
        Assert.False(workflow.ServerSetup.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ServerSetup_ValidProbe_NormalizesAndSaves_ThenShowsLogin()
    {
        using var harness = new WorkflowHarness();
        harness.Vault.SaveRefreshToken("old", "", "old-login", "old-device-key-123", "old-refresh");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var setup = workflow.ServerSetup!;
        setup.Address = " https://task.local/ ";

        await setup.CheckConnectionCommand.ExecuteAsync();
        var continued = await setup.ContinueCommand.ExecuteAsync();

        Assert.True(continued);
        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Equal("https://task.local/", workflow.ServerEndpoint!.AbsoluteUri);
        Assert.Equal(workflow.ServerEndpoint, harness.Settings.Load());
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.Equal(new[] { "/health/live", "/health/ready" }, harness.ProbeRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_SuccessWithConfirmedSession_TransitionsToReady()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";
        var passwordCleared = false;
        login.PasswordClearRequested += () => passwordCleared = true;

        await login.SignInCommand.ExecuteAsync("Current!2345");

        Assert.Equal(AuthWorkflowState.Ready, workflow.CurrentState);
        Assert.True(workflow.IsReady);
        Assert.True(passwordCleared);
        Assert.NotNull(harness.Vault.GetRefreshToken());
        Assert.Equal(new[] { "/api/v1/auth/login", "/api/v1/auth/session" }, harness.AuthRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_InvalidCredentials_StaysOnLogin_KeepsLoginAndClearsPassword()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = _ => Problem(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";
        var passwordCleared = false;
        login.PasswordClearRequested += () => passwordCleared = true;

        await login.SignInCommand.ExecuteAsync("Wrong!2345");

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Equal("ivan", login.Login);
        Assert.Equal("Неверный логин или пароль.", login.ErrorMessage);
        Assert.True(passwordCleared);
        Assert.Null(harness.Vault.GetRefreshToken());
    }

    [Theory]
    [InlineData("ACCOUNT_LOCKED_TEMPORARILY", 37, "37 сек")]
    [InlineData("RATE_LIMITED", 19, "19 сек")]
    public async global::System.Threading.Tasks.Task Login_TemporaryRestriction_ShowsRetryHint(
        string code,
        int retryAfterSeconds,
        string expected)
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = _ => Problem(HttpStatusCode.TooManyRequests, code, retryAfterSeconds);
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";

        await login.SignInCommand.ExecuteAsync("Wrong!2345");

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Contains(expected, login.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_AccountBlocked_DoesNotSuggestImmediateRetry()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = _ => Problem(HttpStatusCode.Locked, "ACCOUNT_BLOCKED");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";

        await login.SignInCommand.ExecuteAsync("Current!2345");

        Assert.Contains("заблокирована", login.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("администратору", login.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("повторите", login.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_MustChangePassword_CannotReachReady()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";

        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");

        Assert.Equal(AuthWorkflowState.PasswordChangeRequired, workflow.CurrentState);
        Assert.NotNull(workflow.PasswordChange);
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_SessionMetadataUnavailable_ShowsRecoveryAndKeepsVault()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsLogin(request)
            ? Json(HttpStatusCode.OK, TokensJson())
            : throw new HttpRequestException("session endpoint offline");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";

        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");

        Assert.Equal(AuthWorkflowState.Recovery, workflow.CurrentState);
        Assert.NotNull(harness.Vault.GetRefreshToken());
        Assert.False(workflow.IsReady);
        Assert.Contains("подтвердить", workflow.Recovery!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Login_NetworkFailure_StaysRetryableWithoutPersistingSession()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = _ => throw new HttpRequestException("offline");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";

        await login.SignInCommand.ExecuteAsync("Current!2345");

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Contains("недоступен", login.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Vault.GetRefreshToken());
    }

    [Theory]
    [InlineData("Current!2345", "Weak1!", "Weak1!", "политики")]
    [InlineData("Current!2345", "Replacement!234", "Different!234", "не совпадают")]
    [InlineData("", "Replacement!234", "Replacement!234", "Заполните")]
    public async global::System.Threading.Tasks.Task PasswordChange_LocalValidation_DoesNotCallServer(
        string currentPassword,
        string newPassword,
        string confirmation,
        string expected)
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        var passwordChange = workflow.PasswordChange!;
        var requestCount = harness.AuthRequests.Length;
        var cleared = false;
        passwordChange.PasswordsClearRequested += () => cleared = true;

        await passwordChange.ChangePasswordCommand.ExecuteAsync(
            new PasswordChangeInput(currentPassword, newPassword, confirmation));

        Assert.Equal(requestCount, harness.AuthRequests.Length);
        Assert.Contains(expected, passwordChange.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.True(cleared);
        Assert.Equal(AuthWorkflowState.PasswordChangeRequired, workflow.CurrentState);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PasswordChange_SuccessRequiresSecondSessionConfirmation()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        var sessionReads = 0;
        harness.AuthResponder = request =>
        {
            if (IsLogin(request))
            {
                return Json(HttpStatusCode.OK, TokensJson());
            }

            if (IsSession(request))
            {
                sessionReads++;
                return Json(HttpStatusCode.OK, SessionJson(sessionReads == 1));
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        var passwordChange = workflow.PasswordChange!;

        await passwordChange.ChangePasswordCommand.ExecuteAsync(
            new PasswordChangeInput("Current!2345", "Replacement!234", "Replacement!234"));

        Assert.Equal(2, sessionReads);
        Assert.Equal(AuthWorkflowState.Ready, workflow.CurrentState);
        Assert.True(workflow.IsReady);
        Assert.Contains("/api/v1/auth/change-password", harness.AuthRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PasswordChange_InvalidCurrentPassword_StaysOnMandatoryScreen()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsLogin(request)
            ? Json(HttpStatusCode.OK, TokensJson())
            : IsSession(request)
                ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true))
                : Problem(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        var passwordChange = workflow.PasswordChange!;

        await passwordChange.ChangePasswordCommand.ExecuteAsync(
            new PasswordChangeInput("Wrong!2345", "Replacement!234", "Replacement!234"));

        Assert.Equal(AuthWorkflowState.PasswordChangeRequired, workflow.CurrentState);
        Assert.Equal("Неверно указан текущий пароль.", passwordChange.ErrorMessage);
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PasswordChange_PostChangeConfirmationOffline_BlocksReadyInRecovery()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        var sessionReads = 0;
        harness.AuthResponder = request =>
        {
            if (IsLogin(request))
            {
                return Json(HttpStatusCode.OK, TokensJson());
            }

            if (IsSession(request))
            {
                sessionReads++;
                if (sessionReads > 1)
                {
                    throw new HttpRequestException("offline after password change");
                }

                return Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true));
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        var passwordChange = workflow.PasswordChange!;

        await passwordChange.ChangePasswordCommand.ExecuteAsync(
            new PasswordChangeInput("Current!2345", "Replacement!234", "Replacement!234"));

        Assert.Equal(AuthWorkflowState.Recovery, workflow.CurrentState);
        Assert.NotNull(harness.Vault.GetRefreshToken());
        Assert.False(workflow.IsReady);
        Assert.Contains("Пароль изменён", workflow.Recovery!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PasswordChange_AccountBlocked_ClearsVaultAndReturnsToLogin()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsLogin(request)
            ? Json(HttpStatusCode.OK, TokensJson())
            : IsSession(request)
                ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true))
                : Problem(HttpStatusCode.Locked, "ACCOUNT_BLOCKED");
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        var stateChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workflow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AuthWorkflowViewModel.CurrentState)
                && workflow.CurrentState == AuthWorkflowState.Login)
            {
                stateChanged.TrySetResult();
            }
        };

        await workflow.PasswordChange!.ChangePasswordCommand.ExecuteAsync(
            new PasswordChangeInput("Current!2345", "Replacement!234", "Replacement!234"));
        await stateChanged.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Contains("заблокирована", workflow.Login!.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task StartupRestore_ConfirmedSession_TransitionsToReady()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();

        await workflow.StartAsync();

        Assert.Equal(AuthWorkflowState.Ready, workflow.CurrentState);
        Assert.Equal(new[] { "/api/v1/auth/refresh", "/api/v1/auth/session" }, harness.AuthRequests);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task StartupRestore_MustChangePassword_ShowsMandatoryScreen()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: true))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();

        await workflow.StartAsync();

        Assert.Equal(AuthWorkflowState.PasswordChangeRequired, workflow.CurrentState);
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task StartupRestore_Offline_ShowsRecoveryAndKeepsVault_ThenRetryCanSucceed()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        var offline = true;
        harness.AuthResponder = request =>
        {
            if (offline)
            {
                throw new HttpRequestException("offline");
            }

            return IsSession(request)
                ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
                : Json(HttpStatusCode.OK, TokensJson());
        };
        using var workflow = harness.CreateWorkflow();

        await workflow.StartAsync();

        Assert.Equal(AuthWorkflowState.Recovery, workflow.CurrentState);
        Assert.NotNull(harness.Vault.GetRefreshToken());
        Assert.Contains("не удалена", workflow.Recovery!.Message, StringComparison.OrdinalIgnoreCase);

        offline = false;
        await workflow.Recovery.RetryCommand.ExecuteAsync();

        Assert.Equal(AuthWorkflowState.Ready, workflow.CurrentState);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task StartupRestore_RevokedSession_ClearsVaultAndReturnsToLogin()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        harness.AuthResponder = _ => Problem(HttpStatusCode.Unauthorized, "SESSION_REVOKED");
        using var workflow = harness.CreateWorkflow();

        await workflow.StartAsync();

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Contains("администратором", workflow.Login!.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Logout_FromReady_AlwaysClearsLocalSessionAndShowsLogin()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
            : request.RequestUri!.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal)
                ? throw new HttpRequestException("server offline")
                : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();

        await workflow.LogoutAsync();

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Equal("Вы вышли из Task.", workflow.Login!.ErrorMessage);
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.False(workflow.IsReady);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BackgroundTerminalRefresh_ImmediatelyClosesReadyState()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer(withStoredSession: true);
        var revoke = false;
        harness.AuthResponder = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh", StringComparison.Ordinal) && revoke)
            {
                return Problem(HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
            }

            return IsSession(request)
                ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
                : Json(HttpStatusCode.OK, TokensJson());
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        Assert.True(workflow.IsReady);
        var signedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workflow.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AuthWorkflowViewModel.CurrentState)
                && workflow.CurrentState == AuthWorkflowState.Login)
            {
                signedOut.TrySetResult();
            }
        };

        revoke = true;
        await harness.LastSessionService!.RefreshAsync();
        await signedOut.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Contains("истёк", workflow.Login!.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(harness.Vault.GetRefreshToken());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ChangeServer_ClearsOldVaultBeforeShowingSetup()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponder = request => IsSession(request)
            ? Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false))
            : Json(HttpStatusCode.OK, TokensJson());
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        workflow.Login!.Login = "ivan";
        await workflow.Login.SignInCommand.ExecuteAsync("Current!2345");
        await workflow.LogoutAsync();
        harness.Vault.SaveRefreshToken("old", "", "ivan", "device-key-123456", "old-refresh");

        await workflow.Login!.ChangeServerCommand.ExecuteAsync();

        Assert.Equal(AuthWorkflowState.ServerSetup, workflow.CurrentState);
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.Equal(WorkflowHarness.Endpoint, workflow.ServerSetup!.Address);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginCommand_DoubleSubmit_MakesOnlyOneRequest()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        var loginStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLogin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AuthResponderAsync = async (request, cancellationToken) =>
        {
            if (IsLogin(request))
            {
                loginStarted.SetResult();
                await releaseLogin.Task.WaitAsync(cancellationToken);
                return Json(HttpStatusCode.OK, TokensJson());
            }

            return Json(HttpStatusCode.OK, SessionJson(mustChangePassword: false));
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";

        var first = login.SignInCommand.ExecuteAsync("Current!2345");
        await loginStarted.Task;
        var secondStarted = await login.SignInCommand.ExecuteAsync("Current!2345");
        releaseLogin.SetResult();
        await first;

        Assert.False(secondStarted);
        Assert.Equal(1, harness.AuthRequests.Count(path => path.EndsWith("/login", StringComparison.Ordinal)));
        Assert.Equal(AuthWorkflowState.Ready, workflow.CurrentState);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LoginCommand_Cancellation_IsContainedAndClearsPassword()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        harness.AuthResponderAsync = async (_, cancellationToken) =>
        {
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };
        using var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";
        var cleared = false;
        login.PasswordClearRequested += () => cleared = true;
        using var cancellation = new CancellationTokenSource();

        var execution = login.SignInCommand.ExecuteAsync("Current!2345", cancellation.Token);
        cancellation.Cancel();
        await execution;

        Assert.Equal(AuthWorkflowState.Login, workflow.CurrentState);
        Assert.Null(harness.Vault.GetRefreshToken());
        Assert.True(cleared);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task WorkflowDispose_CancelsInFlightLogin_WithoutFaultingCommandBoundary()
    {
        using var harness = new WorkflowHarness();
        harness.ConfigureServer();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.AuthResponderAsync = async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await global::System.Threading.Tasks.Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        };
        var workflow = harness.CreateWorkflow();
        await workflow.StartAsync();
        var login = workflow.Login!;
        login.Login = "ivan";

        var execution = login.SignInCommand.ExecuteAsync("Current!2345");
        await requestStarted.Task;
        workflow.Dispose();

        await execution;
        workflow.Dispose();

        Assert.Null(harness.Vault.GetRefreshToken());
    }

    private static bool IsLogin(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal);

    private static bool IsSession(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/session", StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, int? retryAfter = null)
    {
        var retry = retryAfter.HasValue ? $",\"retryAfterSeconds\":{retryAfter.Value}" : string.Empty;
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{"title":"Error","status":{{(int)status}},"code":"{{code}}"{{retry}}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
    }

    private static string TokensJson() =>
        $$"""{"accessToken":"AT_value","accessExpiresAt":"{{DateTimeOffset.UtcNow.AddHours(1):O}}","refreshToken":"RT_value","refreshExpiresAt":"{{DateTimeOffset.UtcNow.AddDays(7):O}}","sessionId":"{{WorkflowHarness.SessionId}}"}""";

    private static string SessionJson(bool mustChangePassword) =>
        $$"""{"userId":"{{WorkflowHarness.UserId}}","sessionId":"{{WorkflowHarness.SessionId}}","organizationId":"{{WorkflowHarness.OrganizationId}}","credentialVersion":1,"authorizationScopeVersion":1,"mustChangePassword":{{mustChangePassword.ToString().ToLowerInvariant()}}}""";

    private sealed class WorkflowHarness : IDisposable
    {
        public const string Endpoint = "https://task.local/";
        public const string SessionId = "019fa078-3f10-7ec1-99e2-7c1cba4ee3d4";
        public const string UserId = "019fb732-ad08-7de1-b27d-c86bae8a2937";
        public const string OrganizationId = "019fb732-ad08-7de1-b27d-c86bae8a2938";

        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "Task.Desktop.Tests",
            Guid.NewGuid().ToString("N"));

        public WorkflowHarness()
        {
            Vault = new DesktopCredentialVault(_directory);
            Settings = new DesktopServerSettingsStore(_directory);
            ProbeResponder = (request, _) => global::System.Threading.Tasks.Task.FromResult(
                Json(
                    HttpStatusCode.OK,
                    request.RequestUri!.AbsolutePath.EndsWith("/live", StringComparison.Ordinal)
                        ? "{\"status\":\"Alive\"}"
                        : "{\"status\":\"Ready\"}"));
        }

        public DesktopCredentialVault Vault { get; }

        public DesktopServerSettingsStore Settings { get; }

        public ConcurrentQueue<string> ProbeRequestQueue { get; } = new();

        public ConcurrentQueue<string> AuthRequestQueue { get; } = new();

        public string[] ProbeRequests => ProbeRequestQueue.ToArray();

        public string[] AuthRequests => AuthRequestQueue.ToArray();

        public Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> ProbeResponder { get; set; }

        public Func<HttpRequestMessage, HttpResponseMessage> AuthResponder { get; set; } =
            _ => throw new InvalidOperationException("No auth request expected.");

        public Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>>? AuthResponderAsync { get; set; }

        public SessionService? LastSessionService { get; private set; }

        public void ConfigureServer(bool withStoredSession = false)
        {
            Settings.SaveVerifiedEndpoint(new Uri(Endpoint), Vault);
            if (withStoredSession)
            {
                Vault.SaveRefreshToken(
                    "saved-device",
                    string.Empty,
                    "ivan",
                    "device-key-123456",
                    "saved-refresh");
            }
        }

        public AuthWorkflowViewModel CreateWorkflow()
        {
            var probe = new DesktopServerProbeClient(new HttpClient(new FakeHandler(async (request, token) =>
            {
                ProbeRequestQueue.Enqueue(request.RequestUri!.AbsolutePath);
                return await ProbeResponder(request, token);
            }), disposeHandler: true));

            return new AuthWorkflowViewModel(Settings, probe, Vault, endpoint =>
            {
                var handler = new FakeHandler(async (request, token) =>
                {
                    AuthRequestQueue.Enqueue(request.RequestUri!.AbsolutePath);
                    if (AuthResponderAsync is not null)
                    {
                        return await AuthResponderAsync(request, token);
                    }

                    return AuthResponder(request);
                });
                var client = new DesktopAuthApiClient(
                    new HttpClient(handler, disposeHandler: true),
                    endpoint.AbsoluteUri);
                LastSessionService = new SessionService(
                    client,
                    Vault,
                    "Test workstation",
                    ClientPlatform.Windows,
                    "0.1.0",
                    refreshMargin: TimeSpan.Zero,
                    retryDelay: TimeSpan.FromHours(1));
                return LastSessionService;
            });
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class FakeHandler(
            Func<HttpRequestMessage, CancellationToken, global::System.Threading.Tasks.Task<HttpResponseMessage>> responder)
            : HttpMessageHandler
        {
            protected override global::System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) => responder(request, cancellationToken);
        }
    }
}
