using Npgsql;
using Task.Application.Security;

namespace Task.Worker;

/// <summary>
/// Periodic maintenance hosted service that purges expired sessions and refresh tokens.
/// Every pass removes refresh tokens and sessions whose expiry is older than the retention
/// window in batches of BatchSize, repeating until a batch comes back non-full. Refresh tokens
/// are purged before sessions because iam.refresh_tokens references iam.sessions with
/// ON DELETE RESTRICT. Failures never stop the hosting loop: the next pass retries. When no
/// ISessionRepository is registered the worker keeps running with empty passes and logs a
/// warning.
/// </summary>
public sealed class ExpiredSessionMaintenanceWorker(
    ILogger<ExpiredSessionMaintenanceWorker> logger,
    ISessionRepository? sessionRepository = null,
    TimeSpan? runPeriod = null,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public const int PeriodMinutes = 60;
    public const int RetentionDays = 30;
    public const int BatchSize = 1000;

    private readonly ILogger<ExpiredSessionMaintenanceWorker> _logger = logger;
    private readonly ISessionRepository? _sessionRepository = sessionRepository;
    private readonly TimeSpan _runPeriod = NormalizeRunPeriod(runPeriod);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Expired session maintenance worker hosting loop started");

        if (_sessionRepository is null)
        {
            _logger.LogWarning(
                "ISessionRepository is not registered; expired session maintenance runs empty passes " +
                "(register the TaskDatabase connection string to enable purging)");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sessionRepository is not null)
                {
                    var olderThan = _timeProvider.GetUtcNow().AddDays(-RetentionDays);
                    var purgedTokens = await PurgeExpiredRefreshTokensAsync(olderThan, stoppingToken);
                    var purgedSessions = await PurgeExpiredSessionsAsync(olderThan, stoppingToken);

                    if (purgedTokens == 0 && purgedSessions == 0)
                    {
                        _logger.LogDebug(
                            "Expired session maintenance pass completed with nothing to purge");
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Expired session maintenance pass purged {PurgedTokens} expired refresh tokens and {PurgedSessions} expired sessions",
                            purgedTokens,
                            purgedSessions);
                    }
                }
                else
                {
                    _logger.LogDebug("Expired session maintenance pass skipped (repository not registered)");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (NpgsqlException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Expired session maintenance pass failed because the database is unavailable; the next pass will retry");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expired session maintenance pass failed; the next pass will retry");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await global::System.Threading.Tasks.Task.Delay(_runPeriod, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Expired session maintenance worker hosting loop stopped");
    }

    private async global::System.Threading.Tasks.Task<int> PurgeExpiredRefreshTokensAsync(
        DateTimeOffset olderThanUtc,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var deleted = await _sessionRepository!.PurgeExpiredRefreshTokensAsync(
                olderThanUtc, BatchSize, cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }

    private async global::System.Threading.Tasks.Task<int> PurgeExpiredSessionsAsync(
        DateTimeOffset olderThanUtc,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var deleted = await _sessionRepository!.PurgeExpiredSessionsAsync(
                olderThanUtc, BatchSize, cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
            {
                return total;
            }
        }
    }

    private static TimeSpan NormalizeRunPeriod(TimeSpan? runPeriod)
    {
        var period = runPeriod ?? TimeSpan.FromMinutes(PeriodMinutes);
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(runPeriod), "Run period must be positive.");
        }

        return period;
    }
}