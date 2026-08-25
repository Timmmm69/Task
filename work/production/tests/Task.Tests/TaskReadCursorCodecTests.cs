using Task.Application;

namespace Task.Tests;

public sealed class TaskReadCursorCodecTests
{
    private static readonly Guid OrganizationId =
        Guid.Parse("019fb732-ad08-7de1-b27d-c86bae8a2937");
    private static readonly Guid UserAccountId =
        Guid.Parse("019fa078-3f10-7ec1-99e2-7c1cba4ee3d4");
    private static readonly Guid LastId =
        Guid.Parse("019fc4e2-0dd8-7bf2-a43b-f2ee2b73cb3a");
    private static readonly DateTimeOffset SnapshotBoundary =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastUpdatedAt = SnapshotBoundary.AddMinutes(-5);

    [Fact]
    public void CreateAndParse_RoundTripsBoundContinuation()
    {
        var cursor = CreateCursor();

        var continuation = TaskReadCursorCodec.Parse(
            cursor,
            OrganizationId,
            UserAccountId,
            authorizationScopeVersion: 7);

        Assert.InRange(cursor.Length, 1, TaskReadCursorCodec.MaximumEncodedLength);
        Assert.Equal(SnapshotBoundary, continuation.SnapshotBoundaryUtc);
        Assert.Equal(LastUpdatedAt, continuation.LastUpdatedAtUtc);
        Assert.Equal(LastId, continuation.LastId);
        Assert.DoesNotContain("=", cursor, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsMalformedCursor()
    {
        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Parse(
            "not-a-valid-cursor!",
            OrganizationId,
            UserAccountId,
            authorizationScopeVersion: 7));
    }

    [Fact]
    public void Parse_RejectsDifferentOrganizationUserAndScope()
    {
        var cursor = CreateCursor();

        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Parse(
            cursor,
            Guid.NewGuid(),
            UserAccountId,
            authorizationScopeVersion: 7));
        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Parse(
            cursor,
            OrganizationId,
            Guid.NewGuid(),
            authorizationScopeVersion: 7));
        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Parse(
            cursor,
            OrganizationId,
            UserAccountId,
            authorizationScopeVersion: 8));
    }

    [Fact]
    public void Create_RejectsNonUtcOrInvertedContinuation()
    {
        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Create(
            OrganizationId,
            UserAccountId,
            authorizationScopeVersion: 7,
            SnapshotBoundary.ToOffset(TimeSpan.FromHours(3)),
            LastUpdatedAt,
            LastId));
        Assert.Throws<TaskReadCursorException>(() => TaskReadCursorCodec.Create(
            OrganizationId,
            UserAccountId,
            authorizationScopeVersion: 7,
            SnapshotBoundary,
            SnapshotBoundary.AddTicks(1),
            LastId));
    }

    private static string CreateCursor() => TaskReadCursorCodec.Create(
        OrganizationId,
        UserAccountId,
        authorizationScopeVersion: 7,
        SnapshotBoundary,
        LastUpdatedAt,
        LastId);
}
