using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application.Security;
using Task.Desktop.Security;

namespace Task.Desktop.Administration;

public sealed partial class DesktopAdministrationApiClient
{
    private static readonly JsonSerializerOptions UserJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    public async System.Threading.Tasks.Task<DesktopUserResult<DesktopUserPage>> GetUserPageAsync(
        DesktopUserQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);
        if (query.Filter?.Length > 2000 || query.Cursor is { Length: > 2000 }
            || query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)
            || query.Page is < 1 or > 100000)
            return InvalidUserInput<DesktopUserPage>();
        var path = "users?sort=id";
        if (query.Filter is not null) path += "&filter=" + Uri.EscapeDataString(query.Filter);
        path += query.Cursor is null ? "&page=" + query.Page.ToString(CultureInfo.InvariantCulture)
            : "&cursor=" + Uri.EscapeDataString(query.Cursor);
        var result = await _executor.GetAsync(new Uri(_root + path), Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is not AuthenticatedGetResult.Response { StatusCode: HttpStatusCode.OK } response)
            return UserFailure<DesktopUserPage>(result);
        try
        {
            var page = JsonSerializer.Deserialize<UserPagePayload>(response.Body, UserJson);
            if (page?.Items is null || page.Items.Length > 100 || page.Total < page.Items.Length
                || page.Items.Any(user => user is null || !ValidUser(user))
                || page.Items.Select(user => user.Id).Distinct().Count() != page.Items.Length
                || page.NextCursor is not null && (string.IsNullOrWhiteSpace(page.NextCursor)
                    || page.NextCursor.Length > 2000 || page.NextCursor == query.Cursor || page.Items.Length == 0))
                return new DesktopUserResult<DesktopUserPage>.MalformedResponse();
            return new DesktopUserResult<DesktopUserPage>.Succeeded(new(
                Array.AsReadOnly(page.Items), page.NextCursor, page.Total))
            { CorrelationId = response.CorrelationId };
        }
        catch (JsonException) { return new DesktopUserResult<DesktopUserPage>.MalformedResponse(); }
    }

    public async System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> GetUserDetailsAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id == Guid.Empty) return InvalidUserInput<DesktopManagedUser>();
        var result = await _executor.GetAsync(new Uri(_root + $"users/{id:D}"),
            Guid.NewGuid().ToString("D"), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadManagedUser(result, HttpStatusCode.OK, id);
    }

    public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> CreateUserAsync(
        DesktopUserCreate draft, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(draft);
        if (!ValidText(draft.FirstName, 1, 100) || !ValidText(draft.LastName, 1, 100)
            || !ValidText(draft.Login, 3, 100) || draft.DisplayName is not null && !ValidText(draft.DisplayName, 1, 300)
            || !ValidEmail(draft.WorkEmail) || !ValidNullableText(draft.JobTitle, 200)
            || draft.DepartmentId == Guid.Empty || !ValidUserKey(idempotencyKey))
            return System.Threading.Tasks.Task.FromResult(InvalidUserInput<DesktopManagedUser>());
        var body = new JsonObject { ["firstName"] = draft.FirstName, ["lastName"] = draft.LastName, ["login"] = draft.Login };
        if (draft.DisplayName is not null) body["displayName"] = draft.DisplayName;
        if (draft.WorkEmail is not null) body["workEmail"] = draft.WorkEmail;
        if (draft.DepartmentId is not null) body["departmentId"] = draft.DepartmentId;
        if (draft.JobTitle is not null) body["jobTitle"] = draft.JobTitle;
        return SendManagedUserAsync("users", HttpMethod.Post, body, null, idempotencyKey,
            HttpStatusCode.Created, null, cancellationToken);
    }

    public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> UpdateUserAsync(
        Guid id, long version, DesktopUserPatch patch, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(patch);
        if (id == Guid.Empty || version < 1 || !ValidUserKey(idempotencyKey))
            return System.Threading.Tasks.Task.FromResult(InvalidUserInput<DesktopManagedUser>());
        var body = new JsonObject();
        if (!AddText(body, "displayName", patch.DisplayName, 1, 300)
            || !AddText(body, "firstName", patch.FirstName, 1, 100)
            || !AddText(body, "lastName", patch.LastName, 1, 100)
            || !AddText(body, "login", patch.Login, 3, 100)
            || !AddNullableText(body, "workEmail", patch.WorkEmail, 320)
            || !AddNullableText(body, "jobTitle", patch.JobTitle, 200)
            || patch.WorkEmail.IsSpecified && !ValidEmail(patch.WorkEmail.Value)
            || patch.DepartmentId is null || patch.DepartmentId.IsSpecified && patch.DepartmentId.Value == Guid.Empty)
            return System.Threading.Tasks.Task.FromResult(InvalidUserInput<DesktopManagedUser>());
        if (patch.DepartmentId.IsSpecified) body["departmentId"] = patch.DepartmentId.Value;
        if (body.Count == 0) return System.Threading.Tasks.Task.FromResult(InvalidUserInput<DesktopManagedUser>());
        return SendManagedUserAsync($"users/{id:D}", HttpMethod.Patch, body, version, idempotencyKey,
            HttpStatusCode.OK, id, cancellationToken);
    }

    public System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> TransitionUserAsync(
        Guid id, long version, DesktopUserTransition transition, string? idempotencyKey = null,
        string? reason = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requiresBody = transition is DesktopUserTransition.Activate or DesktopUserTransition.Block or DesktopUserTransition.Deactivate;
        var requiresReason = transition is DesktopUserTransition.Block or DesktopUserTransition.Deactivate;
        if (id == Guid.Empty || version < 1 || !Enum.IsDefined(transition)
            || requiresBody && !ValidUserKey(idempotencyKey)
            || requiresReason && !ValidText(reason, 1, 2000) || !requiresReason && reason is not null)
            return System.Threading.Tasks.Task.FromResult(InvalidUserInput<DesktopManagedUser>());
        JsonObject? body = requiresBody ? new() { ["expectedVersion"] = version } : null;
        if (requiresReason) body!["reason"] = reason;
        return SendManagedUserAsync($"users/{id:D}/{transition.ToString().ToLowerInvariant()}", HttpMethod.Post,
            body, version, requiresBody ? idempotencyKey : null, HttpStatusCode.OK, id, cancellationToken);
    }

    private async System.Threading.Tasks.Task<DesktopUserResult<DesktopManagedUser>> SendManagedUserAsync(
        string path, HttpMethod method, JsonObject? body, long? version, string? key,
        HttpStatusCode successStatus, Guid? expectedId, CancellationToken cancellationToken)
    {
        // Never queue or automatically retry an uncertain mutation. Only the shared executor's
        // single 401 refresh replay is allowed; it keeps the same bytes, key and correlation ID.
        if (_connectivity is { Status: not DesktopConnectivityStatus.Online })
            return new DesktopUserResult<DesktopManagedUser>.ServerUnavailable();
        var result = await _executor.SendAsync(method, new Uri(_root + path),
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, UserJson), Guid.NewGuid().ToString("D"),
            version is null ? null : $"\"v{version.Value.ToString(CultureInfo.InvariantCulture)}\"", key, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadManagedUser(result, successStatus, expectedId);
    }

    private static DesktopUserResult<DesktopManagedUser> ReadManagedUser(
        AuthenticatedGetResult result, HttpStatusCode successStatus, Guid? expectedId)
    {
        if (result is not AuthenticatedGetResult.Response response || response.StatusCode != successStatus)
            return UserFailure<DesktopManagedUser>(result);
        try
        {
            var user = JsonSerializer.Deserialize<DesktopManagedUser>(response.Body, UserJson);
            if (user is null || !ValidUser(user) || expectedId is not null && user.Id != expectedId
                || response.EntityTag != $"\"v{user.Version.ToString(CultureInfo.InvariantCulture)}\""
                || response.IdempotencyReplayed is not (null or "true" or "false"))
                return new DesktopUserResult<DesktopManagedUser>.MalformedResponse();
            return new DesktopUserResult<DesktopManagedUser>.Succeeded(user, user.Version,
                response.IdempotencyReplayed == "true")
            { CorrelationId = response.CorrelationId };
        }
        catch (JsonException) { return new DesktopUserResult<DesktopManagedUser>.MalformedResponse(); }
    }

    private static bool ValidUser(DesktopManagedUser user) => user.Id != Guid.Empty && user.OrganizationId != Guid.Empty
        && user.Version > 0 && ValidText(user.DisplayName, 1, 300) && ValidText(user.FirstName, 1, 100)
        && ValidText(user.LastName, 1, 100) && ValidText(user.Login, 3, 100)
        && ValidEmail(user.WorkEmail) && ValidNullableText(user.JobTitle, 200) && user.DepartmentId != Guid.Empty
        && user.AccountStatus is "pending_activation" or "active" or "blocked" or "deactivated";

    private static bool ValidText(string? value, int min, int max) => !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length >= min && value.Length <= max;
    private static bool ValidNullableText(string? value, int max) => value is null || ValidText(value, 1, max);
    private static bool ValidEmail(string? value) => value is null || ValidText(value, 1, 320) && MailAddress.TryCreate(value, out _);
    private static bool ValidUserKey(string? key) => key is { Length: >= 8 and <= 200 } && key.All(c => c is >= '!' and <= '~');
    private static DesktopUserResult<T> InvalidUserInput<T>() => new DesktopUserResult<T>.ValidationFailure("Проверьте поля запроса, версию и ключ операции.")
    { ErrorCode = "VALIDATION_FAILED" };

    private static bool AddText(JsonObject body, string name, OptionalUserField<string>? field, int min, int max)
    {
        if (field is null) return false;
        if (!field.IsSpecified) return true;
        if (!ValidText(field.Value, min, max)) return false;
        body[name] = field.Value;
        return true;
    }

    private static bool AddNullableText(JsonObject body, string name, OptionalUserField<string?>? field, int max)
    {
        if (field is null) return false;
        if (!field.IsSpecified) return true;
        if (!ValidNullableText(field.Value, max)) return false;
        body[name] = field.Value;
        return true;
    }

    private static DesktopUserResult<T> UserFailure<T>(AuthenticatedGetResult result)
    {
        if (result is AuthenticatedGetResult.AuthenticationFailure) return new DesktopUserResult<T>.AuthenticationFailure();
        if (result is AuthenticatedGetResult.ServerUnavailable) return new DesktopUserResult<T>.ServerUnavailable();
        if (result is not AuthenticatedGetResult.Response response) return new DesktopUserResult<T>.MalformedResponse();
        DesktopUserResult<T> failure = (int)response.StatusCode switch
        {
            400 or 422 => new DesktopUserResult<T>.ValidationFailure("Сервер отклонил данные запроса."),
            401 => new DesktopUserResult<T>.AuthenticationFailure(),
            403 => new DesktopUserResult<T>.Forbidden(),
            404 => new DesktopUserResult<T>.NotFound(),
            409 => new DesktopUserResult<T>.Conflict(),
            412 => new DesktopUserResult<T>.VersionConflict(),
            428 => new DesktopUserResult<T>.PreconditionRequired(),
            >= 500 => new DesktopUserResult<T>.ServerUnavailable(),
            _ => new DesktopUserResult<T>.MalformedResponse(),
        };
        // Keep machine-readable metadata, never forward arbitrary problem titles or bodies.
        try
        {
            var problem = JsonNode.Parse(response.Body) as JsonObject;
            var code = problem?["code"] is JsonValue codeValue && codeValue.TryGetValue<string>(out var parsedCode) ? parsedCode : null;
            if (code is not null && (code.Length is 0 or > 100 || !code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))) code = null;
            int? retry = problem?["retryAfterSeconds"] is JsonValue retryValue && retryValue.TryGetValue<int>(out var parsedRetry) ? parsedRetry : null;
            bool? retryable = problem?["retryable"] is JsonValue retryableValue && retryableValue.TryGetValue<bool>(out var parsedRetryable) ? parsedRetryable : null;
            return failure with
            {
                ErrorCode = code,
                CorrelationId = response.CorrelationId,
                Retryable = retryable,
                RetryAfterSeconds = retry >= 0 ? retry : null
            };
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        { return failure with { CorrelationId = response.CorrelationId }; }
    }

    private sealed record UserPagePayload(
        [property: System.Text.Json.Serialization.JsonRequired] DesktopManagedUser[] Items,
        [property: System.Text.Json.Serialization.JsonRequired] string? NextCursor,
        [property: System.Text.Json.Serialization.JsonRequired] long Total);
}
