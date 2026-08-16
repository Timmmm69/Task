namespace Task.Application.Security;

/// <summary>
/// Server-derived identity for one authenticated request. HTTP adapters construct this only after
/// validating the access token, server session and current account/security versions.
/// </summary>
public sealed record AuthenticatedRequestContext
{
    public AuthenticatedRequestContext(
        Guid userAccountId,
        Guid sessionId,
        Guid organizationId,
        long credentialVersion,
        long authorizationScopeVersion,
        string correlationId,
        string traceId)
    {
        RequireIdentifier(userAccountId, nameof(userAccountId));
        RequireIdentifier(sessionId, nameof(sessionId));
        RequireIdentifier(organizationId, nameof(organizationId));

        if (credentialVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialVersion), "Credential version must be positive.");
        }

        if (authorizationScopeVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorizationScopeVersion),
                "Authorization scope version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new ArgumentException("Trace ID is required.", nameof(traceId));
        }

        UserAccountId = userAccountId;
        SessionId = sessionId;
        OrganizationId = organizationId;
        CredentialVersion = credentialVersion;
        AuthorizationScopeVersion = authorizationScopeVersion;
        CorrelationId = correlationId;
        TraceId = traceId;
    }

    public Guid UserAccountId { get; }

    public Guid SessionId { get; }

    public Guid OrganizationId { get; }

    public long CredentialVersion { get; }

    public long AuthorizationScopeVersion { get; }

    public string CorrelationId { get; }

    public string TraceId { get; }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier must not be empty.", parameterName);
        }
    }
}
