using Task.Application.Security;

namespace Task.Tests.Security;

public sealed class PermissionDecisionServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string PermissionCode = "tasks.view";

    private static PermissionDecisionService CreateService(FakePolicyStore store) => new(store);

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_UnknownUser_ReturnsNoOrgAndStopsBeforeRuleQueries()
    {
        var store = new FakePolicyStore { UserOrg = null };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.NoOrg, decision.Reason);
        Assert.Equal(PermissionCode, decision.PermissionCode);
        Assert.Equal(1, store.OrgLookupCount);
        Assert.Equal(0, store.DenyLookupCount);
        Assert.Equal(0, store.GrantLookupCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_UserOfAnotherOrganization_ReturnsNoOrg()
    {
        var store = new FakePolicyStore { UserOrg = Guid.NewGuid() };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.NoOrg, decision.Reason);
        Assert.Equal(0, store.DenyLookupCount);
        Assert.Equal(0, store.GrantLookupCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_ExplicitDenyOutranksGrants_ReturnsExplicitDeny()
    {
        var store = new FakePolicyStore
        {
            UserOrg = OrganizationId,
            Denies = [new PolicyDenyRow(HasDirectRoleMembership: true)],
            Grants = [new PolicyGrantRow(HasDirectRoleMembership: true)],
        };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.ExplicitDeny, decision.Reason);
        Assert.Equal(1, store.DenyLookupCount);
        Assert.Equal(0, store.GrantLookupCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_ExplicitDenyWithoutGrants_ReturnsExplicitDeny()
    {
        var store = new FakePolicyStore
        {
            UserOrg = OrganizationId,
            Denies = [new PolicyDenyRow(HasDirectRoleMembership: true)],
            Grants = [],
        };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.ExplicitDeny, decision.Reason);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_GrantThroughDirectUserRole_IsAllowed()
    {
        var store = new FakePolicyStore
        {
            UserOrg = OrganizationId,
            Grants = [new PolicyGrantRow(HasDirectRoleMembership: true)],
        };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.True(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.None, decision.Reason);
        Assert.Equal(PermissionCode, decision.PermissionCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_GrantThroughUserRole_IsAllowed()
    {
        var store = new FakePolicyStore
        {
            UserOrg = OrganizationId,
            Grants = [new PolicyGrantRow(HasDirectRoleMembership: false)],
        };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.True(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.None, decision.Reason);
        Assert.Equal(PermissionCode, decision.PermissionCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_NoMatchingRules_ReturnsDefaultDeny()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId };

        var decision = await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.False(decision.Allowed);
        Assert.Equal(AuthorizationDenyReason.DefaultDeny, decision.Reason);
        Assert.Equal(PermissionCode, decision.PermissionCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_PassesOrganizationUserAndCodeToRuleQueries()
    {
        var store = new FakePolicyStore { UserOrg = OrganizationId };

        await CreateService(store).EvaluateAsync(OrganizationId, UserId, PermissionCode);

        Assert.Equal((OrganizationId, UserId, PermissionCode), store.LastDenyQuery);
        Assert.Equal((OrganizationId, UserId, PermissionCode), store.LastGrantQuery);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EvaluateAsync_EmptyIdentifiersAndBlankCode_Throw()
    {
        var service = CreateService(new FakePolicyStore());

        await Assert.ThrowsAsync<ArgumentException>(() => service.EvaluateAsync(Guid.Empty, UserId, PermissionCode));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EvaluateAsync(OrganizationId, Guid.Empty, PermissionCode));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EvaluateAsync(OrganizationId, UserId, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.EvaluateAsync(OrganizationId, UserId, string.Empty));
    }

    [Fact]
    public void Ctor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PermissionDecisionService(null!));
    }

    private sealed class FakePolicyStore : IAuthorizationPolicyStore
    {
        public Guid? UserOrg { get; set; }

        public IReadOnlyList<PolicyGrantRow> Grants { get; set; } = [];

        public IReadOnlyList<PolicyDenyRow> Denies { get; set; } = [];

        public int OrgLookupCount { get; private set; }

        public int DenyLookupCount { get; private set; }

        public int GrantLookupCount { get; private set; }

        public (Guid OrgId, Guid UserId, string Code)? LastDenyQuery { get; private set; }

        public (Guid OrgId, Guid UserId, string Code)? LastGrantQuery { get; private set; }

        public global::System.Threading.Tasks.Task<Guid?> GetUserOrgAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            OrgLookupCount++;
            return global::System.Threading.Tasks.Task.FromResult(UserOrg);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyGrantRow>> GetUserGrantsAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            GrantLookupCount++;
            LastGrantQuery = (orgId, userId, permissionCode);
            return global::System.Threading.Tasks.Task.FromResult(Grants);
        }

        public global::System.Threading.Tasks.Task<IReadOnlyList<PolicyDenyRow>> GetUserDeniesAsync(
            Guid orgId,
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            DenyLookupCount++;
            LastDenyQuery = (orgId, userId, permissionCode);
            return global::System.Threading.Tasks.Task.FromResult(Denies);
        }
    }
}