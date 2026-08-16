using Task.Application.Security;

namespace Task.Tests;

public sealed class SecurityContractsTests
{
    private static readonly Guid UserAccountId = Guid.Parse("3077f0f8-536f-4988-bd73-6f26265d0b92");
    private static readonly Guid SessionId = Guid.Parse("957c3a11-d6f2-4a2a-bb1b-0945a0f6a820");
    private static readonly Guid OrganizationId = Guid.Parse("751fa8ce-5cc3-4d98-8574-1108080b2ff4");

    [Fact]
    public void AuthenticatedRequestContext_WithValidValues_SetsAllProperties()
    {
        const long credentialVersion = 3;
        const long authorizationScopeVersion = 5;

        var context = new AuthenticatedRequestContext(
            UserAccountId,
            SessionId,
            OrganizationId,
            credentialVersion,
            authorizationScopeVersion,
            "correlation-1",
            "trace-1");

        Assert.Equal(UserAccountId, context.UserAccountId);
        Assert.Equal(SessionId, context.SessionId);
        Assert.Equal(OrganizationId, context.OrganizationId);
        Assert.Equal(credentialVersion, context.CredentialVersion);
        Assert.Equal(authorizationScopeVersion, context.AuthorizationScopeVersion);
        Assert.Equal("correlation-1", context.CorrelationId);
        Assert.Equal("trace-1", context.TraceId);
    }

    [Theory]
    [InlineData("UserAccountId")]
    [InlineData("SessionId")]
    [InlineData("OrganizationId")]
    public void AuthenticatedRequestContext_WithEmptyIdentifier_ThrowsArgumentException(string parameter)
    {
        var userAccountId = parameter == "UserAccountId" ? Guid.Empty : UserAccountId;
        var sessionId = parameter == "SessionId" ? Guid.Empty : SessionId;
        var organizationId = parameter == "OrganizationId" ? Guid.Empty : OrganizationId;

        Assert.Throws<ArgumentException>(() => CreateContext(
            userAccountId, sessionId, organizationId, 1, 1, "correlation-1", "trace-1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void AuthenticatedRequestContext_WithInvalidCredentialVersion_ThrowsArgumentOutOfRangeException(
        long credentialVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(
            UserAccountId, SessionId, OrganizationId, credentialVersion, 1, "correlation-1", "trace-1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void AuthenticatedRequestContext_WithInvalidAuthorizationScopeVersion_ThrowsArgumentOutOfRangeException(
        long authorizationScopeVersion)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(
            UserAccountId, SessionId, OrganizationId, 1, authorizationScopeVersion, "correlation-1", "trace-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AuthenticatedRequestContext_WithEmptyCorrelationId_ThrowsArgumentException(string? correlationId)
    {
        Assert.Throws<ArgumentException>(() => CreateContext(
            UserAccountId, SessionId, OrganizationId, 1, 1, correlationId, "trace-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AuthenticatedRequestContext_WithEmptyTraceId_ThrowsArgumentException(string? traceId)
    {
        Assert.Throws<ArgumentException>(() => CreateContext(
            UserAccountId, SessionId, OrganizationId, 1, 1, "correlation-1", traceId));
    }

    [Fact]
    public void PermissionCode_Parse_AcceptsResourceActionAndNormalizesOuterWhitespace()
    {
        var code = PermissionCode.Parse("  task.read  ");

        Assert.Equal("task.read", code.Value);
        Assert.Equal("task.read", code.ToString());
    }

    [Fact]
    public void PermissionCode_Parse_AcceptsUnderscoresInParts()
    {
        var code = PermissionCode.Parse("task_status.prepare_review");

        Assert.Equal("task_status.prepare_review", code.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void PermissionCode_Parse_WithEmptyValue_ThrowsArgumentException(string? value)
    {
        Assert.Throws<ArgumentException>(() => PermissionCode.Parse(value!));
    }

    [Theory]
    [InlineData("taskread")]
    [InlineData("task.read.extra")]
    [InlineData("task..read")]
    [InlineData(".read")]
    [InlineData("task.")]
    public void PermissionCode_Parse_WithMalformedResourceActionForm_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>(() => PermissionCode.Parse(value));
    }

    [Theory]
    [InlineData("task read")]
    [InlineData("task@read")]
    [InlineData("task-read")]
    [InlineData("task/read")]
    [InlineData("task.read!")]
    public void PermissionCode_Parse_WithUnsupportedCharacters_ThrowsArgumentException(string value)
    {
        Assert.Throws<ArgumentException>(() => PermissionCode.Parse(value));
    }

    private static AuthenticatedRequestContext CreateContext(
        Guid userAccountId,
        Guid sessionId,
        Guid organizationId,
        long credentialVersion,
        long authorizationScopeVersion,
        string? correlationId,
        string? traceId)
    {
        return new AuthenticatedRequestContext(
            userAccountId,
            sessionId,
            organizationId,
            credentialVersion,
            authorizationScopeVersion,
            correlationId!,
            traceId!);
    }
}
