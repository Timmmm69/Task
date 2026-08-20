using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Task.Application.Security;
using Task.Worker;

namespace Task.ServiceHosts.Tests;

public sealed class ExpiredSessionMaintenanceWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShortPeriod = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void PeriodRetentionAndBatchSize_Constants()
    {
        Assert.Equal(60, ExpiredSessionMaintenanceWorker.PeriodMinutes);
        Assert.Equal(30, ExpiredSessionMaintenanceWorker.RetentionDays);
        Assert.Equal(1000, ExpiredSessionMaintenanceWorker.BatchSize);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NotConfigured_LogsWarning_AndKeepsRunningEmptyPasses()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        using var worker = new ExpiredSessionMaintenanceWorker(logger, runPeriod: ShortPeriod);

        await worker.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("ISessionRepository is not registered")), Timeout);
        Assert.Contains(messages, m => m.Contains("ISessionRepository is not registered"));

        var skipped = await logger.WaitUntilAsync(
            msgs => msgs.Count(m => m.Contains("pass skipped (repository not registered)")) >= 2, Timeout);
        Assert.True(skipped.Count(m => m.Contains("pass skipped (repository not registered)")) >= 2);

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.Contains(logger.Messages, m => m.Contains("hosting loop stopped"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task EmptyPass_LogsDebug_AndDoesNotPurgeAnything()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        var repository = new FakeSessionRepository();
        using var worker = new ExpiredSessionMaintenanceWorker(logger, repository, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("completed with nothing to purge")), Timeout);
        Assert.Contains(messages, m => m.Contains("completed with nothing to purge"));

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        var tokenCall = Assert.Single(repository.TokenPurgeCalls);
        Assert.Equal(1000, tokenCall.MaxCount);
        var sessionCall = Assert.Single(repository.SessionPurgeCalls);
        Assert.Equal(1000, sessionCall.MaxCount);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PurgeRunsInBatches_UntilBatchComesBackNonFull_WithRetentionCutoff()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        var tokenCalls = 0;
        var sessionCalls = 0;
        var repository = new FakeSessionRepository(
            purgeTokens: (_, _) => global::System.Threading.Tasks.Task.FromResult(++tokenCalls <= 2 ? 1000 : 0),
            purgeSessions: (_, _) => global::System.Threading.Tasks.Task.FromResult(++sessionCalls <= 2 ? 1000 : 0));
        using var worker = new ExpiredSessionMaintenanceWorker(logger, repository, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("purged 2000 expired refresh tokens and 2000 expired sessions")), Timeout);
        Assert.Contains(messages, m => m.Contains("purged 2000 expired refresh tokens and 2000 expired sessions"));

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(3, repository.TokenPurgeCalls.Count);
        Assert.Equal(3, repository.SessionPurgeCalls.Count);
        Assert.All(repository.TokenPurgeCalls, call => Assert.Equal(1000, call.MaxCount));
        Assert.All(repository.SessionPurgeCalls, call => Assert.Equal(1000, call.MaxCount));

        var expectedCutoff = DateTimeOffset.UtcNow.AddDays(-ExpiredSessionMaintenanceWorker.RetentionDays);
        foreach (var call in repository.TokenPurgeCalls.Concat(repository.SessionPurgeCalls))
        {
            Assert.InRange(call.OlderThan, expectedCutoff.AddMinutes(-1), expectedCutoff.AddMinutes(1));
        }

        Assert.NotEmpty(repository.TokenPurgeCalls);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RepositoryFailure_LogsError_AndNextPassSucceeds()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        var tokenCalls = 0;
        var repository = new FakeSessionRepository(
            purgeTokens: (_, _) =>
            {
                tokenCalls++;
                return tokenCalls == 1
                    ? global::System.Threading.Tasks.Task.FromException<int>(new InvalidOperationException("purge exploded"))
                    : global::System.Threading.Tasks.Task.FromResult(0);
            });
        using var worker = new ExpiredSessionMaintenanceWorker(logger, repository, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("pass failed")), Timeout);
        Assert.Contains(messages, m => m.Contains("pass failed"));

        var recovered = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("completed with nothing to purge")), Timeout);
        Assert.Contains(recovered, m => m.Contains("completed with nothing to purge"));

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(2, repository.TokenPurgeCalls.Count);
        Assert.Contains(logger.Messages, m => m.Contains("hosting loop stopped"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DatabaseUnavailable_LogsWarning_AndNextPassRetries()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        var tokenCalls = 0;
        var repository = new FakeSessionRepository(
            purgeTokens: (_, _) =>
            {
                tokenCalls++;
                return tokenCalls == 1
                    ? global::System.Threading.Tasks.Task.FromException<int>(new NpgsqlException("database is down"))
                    : global::System.Threading.Tasks.Task.FromResult(0);
            });
        using var worker = new ExpiredSessionMaintenanceWorker(logger, repository, ShortPeriod);

        await worker.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("database is unavailable")), Timeout);
        Assert.Contains(messages, m => m.Contains("database is unavailable"));

        var recovered = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("completed with nothing to purge")), Timeout);

        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(2, repository.TokenPurgeCalls.Count);
        Assert.Contains(recovered, m => m.Contains("completed with nothing to purge"));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task HostedServiceRegistration_ResolvesWithoutRepository_AndRunsEmptyPasses()
    {
        var logger = new RecordingLogger<ExpiredSessionMaintenanceWorker>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<ExpiredSessionMaintenanceWorker>>(logger);
        services.AddHostedService<ExpiredSessionMaintenanceWorker>();

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetRequiredService<IHostedService>();

        await hostedService.StartAsync(CancellationToken.None);

        var messages = await logger.WaitUntilAsync(
            msgs => msgs.Any(m => m.Contains("ISessionRepository is not registered")), Timeout);
        Assert.Contains(messages, m => m.Contains("ISessionRepository is not registered"));

        await hostedService.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.Contains(logger.Messages, m => m.Contains("hosting loop stopped"));
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        private readonly Func<DateTimeOffset, int, global::System.Threading.Tasks.Task<int>> _purgeTokens;
        private readonly Func<DateTimeOffset, int, global::System.Threading.Tasks.Task<int>> _purgeSessions;

        public FakeSessionRepository(
            Func<DateTimeOffset, int, global::System.Threading.Tasks.Task<int>>? purgeTokens = null,
            Func<DateTimeOffset, int, global::System.Threading.Tasks.Task<int>>? purgeSessions = null)
        {
            _purgeTokens = purgeTokens ?? ((_, _) => global::System.Threading.Tasks.Task.FromResult(0));
            _purgeSessions = purgeSessions ?? ((_, _) => global::System.Threading.Tasks.Task.FromResult(0));
        }

        public List<(DateTimeOffset OlderThan, int MaxCount)> TokenPurgeCalls { get; } = [];

        public List<(DateTimeOffset OlderThan, int MaxCount)> SessionPurgeCalls { get; } = [];

        public global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            TokenPurgeCalls.Add((olderThanUtc, maxCount));
            return _purgeTokens(olderThanUtc, maxCount);
        }

        public global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
            DateTimeOffset olderThanUtc,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            SessionPurgeCalls.Add((olderThanUtc, maxCount));
            return _purgeSessions(olderThanUtc, maxCount);
        }

        public SessionSnapshot? GetActiveSession(Guid organizationId, Guid sessionId) =>
            throw new NotSupportedException();

        public SessionSnapshot? GetSession(Guid organizationId, Guid sessionId) =>
            throw new NotSupportedException();

        public IReadOnlyList<UserSessionListItem> GetUserSessions(Guid organizationId, Guid userId) =>
            throw new NotSupportedException();

        public SessionRefreshLookup? FindSessionByRefreshTokenHash(string tokenHash) =>
            throw new NotSupportedException();

        public SessionRequestState GetSessionRequestState(
            Guid organizationId,
            Guid sessionId,
            long expectedCredentialVersion,
            long expectedAuthorizationScopeVersion) =>
            throw new NotSupportedException();

        public void CreateSession(SessionSnapshot session, RefreshTokenRecord refreshToken) =>
            throw new NotSupportedException();

        public bool RotateRefreshToken(
            Guid organizationId,
            Guid sessionId,
            string consumedTokenHash,
            RefreshTokenRecord newRefreshToken) =>
            throw new NotSupportedException();

        public void TouchSession(Guid organizationId, Guid sessionId) => throw new NotSupportedException();

        public void RevokeSession(Guid organizationId, Guid sessionId, string? reason) =>
            throw new NotSupportedException();

        public int RevokeAllUserSessions(Guid organizationId, Guid userId, Guid? exceptSessionId, string? reason) =>
            throw new NotSupportedException();

        public global::System.Threading.Tasks.Task<int> RevokeAllUserSessionsExceptAsync(
            Guid organizationId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}