using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task.Api.Security;
using Task.Application.Security;

namespace Task.ServiceHosts.Tests;

public sealed class TaskPermissionAuthorizationTests
{
    private static readonly Guid OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_WithGrant_Allows()
    {
        var store = new FakePolicyStore
        {
            UserOrg = OrganizationId,
            Grants = [new PolicyGrantRow(HasDirectRoleMembership: true)],
        };

        var context = await EvaluateAsync(store, TaskPermissionAuthorization.AuditEntryReadPermissionCode);

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_WithDefaultDeny_Fails()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId };

        var context = await EvaluateAsync(store, TaskPermissionAuthorization.AuditEntryReadPermissionCode);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_WithNoOrg_Fails()
    {
        var store = new FakePolicyStore { UserOrg = null };

        var context = await EvaluateAsync(store, TaskPermissionAuthorization.AuditEntryReadPermissionCode);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_WithoutAuthenticatedRequestContext_Fails()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId };

        var context = await EvaluateAsync(
            store,
            TaskPermissionAuthorization.AuditEntryReadPermissionCode,
            includeRequestContext: false);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_WhenStoreThrows_FailsClosed()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId, ThrowOnOrgLookup = true };

        var context = await EvaluateAsync(store, TaskPermissionAuthorization.AuditEntryReadPermissionCode);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Handler_PassesOrganizationUserAndRequirementCodeToEngine()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId };

        await EvaluateAsync(store, TaskPermissionAuthorization.AuditEntryReadPermissionCode);

        Assert.Equal((OrganizationId, UserId, "audit.entry.read"), store.LastDenyQuery);
        Assert.Equal((OrganizationId, UserId, "audit.entry.read"), store.LastGrantQuery);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Registration_AddsNamedPoliciesWithPermissionRequirements()
    {
        using var server = CreateServer();

        var policyProvider = server.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var auditPolicy = await policyProvider.GetPolicyAsync(TaskPermissionAuthorization.AuditReadPolicyName);
        Assert.NotNull(auditPolicy);
        var auditRequirement = Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(
            Assert.Single(auditPolicy.Requirements));
        Assert.Equal(TaskPermissionAuthorization.AuditEntryReadPermissionCode, auditRequirement.Code);

        var loginAttemptsPolicy = await policyProvider.GetPolicyAsync(TaskPermissionAuthorization.LoginAttemptsReadPolicyName);
        Assert.NotNull(loginAttemptsPolicy);
        var loginAttemptsRequirement = Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(
            Assert.Single(loginAttemptsPolicy.Requirements));
        Assert.Equal(TaskPermissionAuthorization.AuditEntryReadPermissionCode, loginAttemptsRequirement.Code);

        var taskReadPolicy = await policyProvider.GetPolicyAsync(TaskPermissionAuthorization.TaskReadPolicyName);
        Assert.NotNull(taskReadPolicy);
        var taskReadRequirement = Assert.IsType<TaskPermissionAuthorization.PermissionRequirement>(
            Assert.Single(taskReadPolicy.Requirements));
        Assert.Equal(TaskPermissionAuthorization.TaskReadBackingPermissionCode, taskReadRequirement.Code);

        Assert.Null(await policyProvider.GetPolicyAsync("permission.unknown"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Registration_PreservesFoundationFallbackPolicy()
    {
        using var server = CreateServer();

        var fallback = await server.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(TaskApiSecurityFoundation.FoundationAuthenticationScheme, fallback.AuthenticationSchemes);
    }

    private static async global::System.Threading.Tasks.Task<AuthorizationHandlerContext> EvaluateAsync(
        FakePolicyStore store,
        string code,
        bool includeRequestContext = true)
    {
        var httpContext = new DefaultHttpContext();
        if (includeRequestContext)
        {
            httpContext.Items[TaskJwtAuthenticationHandler.AuthenticatedRequestContextItemName] =
                new AuthenticatedRequestContext(
                    UserId,
                    SessionId,
                    OrganizationId,
                    credentialVersion: 1,
                    authorizationScopeVersion: 1,
                    correlationId: Guid.NewGuid().ToString("D"),
                    traceId: "trace-test");
        }

        var requirement = new TaskPermissionAuthorization.PermissionRequirement(code);
        var handler = new TaskPermissionAuthorization.PermissionAuthorizationHandler(
            new PermissionDecisionService(store));
        var context = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity("test")),
            httpContext);

        await handler.HandleAsync(context);
        return context;
    }

    private static TestServer CreateServer()
    {
        var keysDirectory = CreateEphemeralVerificationKeys();
        return new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(Options.Create(new TaskIdentityFoundationOptions
                {
                    Issuer = "https://task.example.internal",
                    Audience = "task-desktop",
                    SigningKeyReference = $"file:{Path.Combine(keysDirectory, "signing.pem")}",
                    PepperReference = "file:/run/secrets/task-pepper",
                    VerificationKeysDirectory = $"file:{keysDirectory}",
                }));
                services.AddTaskApiSecurityFoundation();
                services.AddTaskPermissionAuthorization();
                services.AddSingleton<IAuthorizationPolicyStore>(new FakePolicyStore { UserOrg = OrganizationId });
                services.AddSingleton<PermissionDecisionService>();
            })
            .Configure(_ => { }));
    }

    private static string CreateEphemeralVerificationKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"task-permission-auth-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(
            Path.Combine(directory, "test-key.pem"),
            ecdsa.ExportSubjectPublicKeyInfoPem());

        return directory;
    }

    private sealed class FakePolicyStore : IAuthorizationPolicyStore
    {
        public Guid? UserOrg { get; set; }

        public IReadOnlyList<PolicyGrantRow> Grants { get; set; } = [];

        public IReadOnlyList<PolicyDenyRow> Denies { get; set; } = [];

        public bool ThrowOnOrgLookup { get; set; }

        public (Guid OrgId, Guid UserId, string Code)? LastDenyQuery { get; private set; }

        public (Guid OrgId, Guid UserId, string Code)? LastGrantQuery { get; private set; }

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnOrgLookup)
            {
                throw new InvalidOperationException("Store failure.");
            }

            return global::System.Threading.Tasks.Task.FromResult(UserOrg);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            LastGrantQuery = (orgId, userId, permissionCode);
            return global::System.Threading.Tasks.Task.FromResult(Grants);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            LastDenyQuery = (orgId, userId, permissionCode);
            return global::System.Threading.Tasks.Task.FromResult(Denies);
        }
    }
}
