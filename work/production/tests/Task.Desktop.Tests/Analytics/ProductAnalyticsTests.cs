using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Task.Desktop.Analytics;
using Task.Desktop.Modes;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;
using Xunit;
using AsyncTask = System.Threading.Tasks.Task;

namespace Task.Desktop.Tests.Analytics;

public sealed class ProductAnalyticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"Task-analytics-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new();
    private static AnalyticsConfiguration Configuration => AnalyticsConfiguration.Parse("https://eu.i.posthog.com", "phc_unit_test", isTest: true)!;
    private string StatePath => Path.Combine(_root, "analytics", "usage-v1.json");

    [Theory]
    [InlineData("https://eu.i.posthog.com.evil.example", "phc_unit_test")]
    [InlineData("https://eu.i.posthog.com:444", "phc_unit_test")]
    [InlineData("http://eu.i.posthog.com", "phc_unit_test")]
    [InlineData("https://eu.i.posthog.com/?route=1", "phc_unit_test")]
    [InlineData("https://eu.i.posthog.com", "phx_private_key")]
    [InlineData("https://eu.i.posthog.com", null)]
    public void OnlyOfficialHttpsOriginsAndProjectTokensAreAccepted(string host, string? token) =>
        Assert.Null(AnalyticsConfiguration.Parse(host, token, false));

    [Fact]
    public async AsyncTask NoConsentOrTokenMeansNoIdentifiersOrRequests()
    {
        foreach (var config in new[] { Configuration, null })
        {
            using var transport = new FakeTransport();
            using var analytics = new ProductAnalytics(_root, config, _clock, transport);
            analytics.ModeOpened(ApplicationMode.Personal);
            await analytics.Ready;
            analytics.Record(UsageEvent.TaskCreated, ApplicationMode.Personal);
            Assert.False(analytics.Enabled);
            if (config is null) Assert.False(await analytics.SetEnabledAsync(true));
            Assert.False(File.Exists(StatePath));
            Assert.Empty(transport.Requests);
        }
    }

    [Fact]
    public async AsyncTask FirstNoticeStartsCaptureAutomaticallyAndDoesNotRepeatOnRestart()
    {
        Guid installation;
        using (var transport = new FakeTransport())
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, transport))
        {
            analytics.ModeOpened(ApplicationMode.Personal);
            await analytics.Ready;
            Assert.Empty(transport.Requests);
            Assert.False(await analytics.EnableAfterNoticeAsync());
            Assert.False(File.Exists(StatePath));

            Assert.True(analytics.TryClaimNotice());
            Assert.False(analytics.TryClaimNotice());
            Assert.True(await analytics.EnableAfterNoticeAsync());
            await UntilAsync(() => !transport.Requests.IsEmpty);
            Assert.Equal("app_opened", Assert.Single(transport.Requests.First().Events).GetProperty("event").GetString());
            installation = ReadState()!.Installation;
        }

        using var reopenedTransport = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, reopenedTransport);
        await reopened.Ready;
        Assert.True(reopened.Enabled);
        Assert.False(reopened.TryClaimNotice());
        Assert.Equal(installation, ReadState()!.Installation);
    }

    [Fact]
    public async AsyncTask OptOutSurvivesRestartAndPreventsAutomaticReenable()
    {
        using (var transport = new FakeTransport())
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, transport))
        {
            await analytics.Ready;
            Assert.True(analytics.TryClaimNotice());
            var optedOut = analytics.SetEnabledAsync(false);
            Assert.False(await analytics.EnableAfterNoticeAsync());
            Assert.True(await optedOut);
        }

        using var reopenedTransport = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, reopenedTransport);
        reopened.ModeOpened(ApplicationMode.Corporate);
        await reopened.Ready;
        Assert.False(reopened.Enabled);
        Assert.False(reopened.TryClaimNotice());
        Assert.Empty(reopenedTransport.Requests);
        Assert.False(File.Exists(StatePath));
        Assert.True(File.Exists(Path.Combine(_root, "analytics", "usage-disabled")));

        Assert.True(await reopened.SetEnabledAsync(true));
        Assert.True(reopened.Enabled);
        Assert.False(File.Exists(Path.Combine(_root, "analytics", "usage-disabled")));
        await UntilAsync(() => !reopenedTransport.Requests.IsEmpty);
    }

    [Fact]
    public async AsyncTask ClosingAsOptOutIsRequestedStillPersistsThePreference()
    {
        using (var transport = new FakeTransport())
        {
            var analytics = new ProductAnalytics(_root, Configuration, _clock, transport);
            await analytics.Ready;
            Assert.True(analytics.TryClaimNotice());
            Assert.True(await analytics.EnableAfterNoticeAsync());
            var optOut = analytics.SetEnabledAsync(false);
            analytics.Dispose();
            await optOut;
        }

        using var reopenedTransport = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, reopenedTransport);
        await reopened.Ready;
        Assert.False(reopened.Enabled);
        Assert.False(reopened.TryClaimNotice());
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async AsyncTask ConsentOnlyConfigurationDoesNotReuseAutomaticPermission()
    {
        using (var transport = new FakeTransport())
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, transport))
        {
            await analytics.Ready;
            Assert.True(analytics.TryClaimNotice());
            Assert.True(await analytics.EnableAfterNoticeAsync());
            Assert.True(ReadState()!.EnabledByNotice);
        }


        using var explicitTransport = new FakeTransport();
        using var explicitConsent = new ProductAnalytics(_root, Configuration with { RequiresConsent = true }, _clock, explicitTransport);
        explicitConsent.ModeOpened(ApplicationMode.Personal);
        await explicitConsent.Ready;
        Assert.False(explicitConsent.Enabled);
        Assert.False(explicitConsent.TryClaimNotice());
        Assert.False(File.Exists(StatePath));
        Assert.Empty(explicitTransport.Requests);

        Assert.True(await explicitConsent.SetEnabledAsync(true));
        Assert.False(ReadState()!.EnabledByNotice);

        await UntilAsync(() => !explicitTransport.Requests.IsEmpty);
    }

    [Fact]
    public async AsyncTask ConsentIsSharedBetweenModesAndRevocationRotatesInstallation()
    {
        Guid installation;
        Guid session;
        using (var transport = new FakeTransport())
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, transport))
        {
            analytics.ModeOpened(ApplicationMode.Personal);
            Assert.True(await analytics.SetEnabledAsync(true));
            analytics.ModeOpened(ApplicationMode.Corporate);
            analytics.ModeOpened(ApplicationMode.Personal);
            await UntilAsync(() => transport.Requests.SelectMany(request => request.Events).Count() == 2);
            var events = transport.Requests.SelectMany(request => request.Events).ToArray();
            installation = events[0].GetProperty("distinct_id").GetGuid();
            session = events[0].GetProperty("session_id").GetGuid();
            Assert.All(events, item =>
            {
                Assert.Equal("app_opened", item.GetProperty("event").GetString());
                Assert.Equal(installation, item.GetProperty("distinct_id").GetGuid());
                Assert.Equal(session, item.GetProperty("session_id").GetGuid());
            });
            Assert.Equal(new[] { "corporate", "personal" }, events.Select(Mode).Order().ToArray());
        }

        using var reopenedTransport = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, reopenedTransport);
        reopened.ModeOpened(ApplicationMode.Corporate);
        await reopened.Ready;
        Assert.True(reopened.Enabled);
        await UntilAsync(() => reopenedTransport.Requests.SelectMany(request => request.Events).Any(item => item.GetProperty("session_id").GetGuid() != session));
        var nextSession = reopenedTransport.Requests.SelectMany(request => request.Events).First(item => item.GetProperty("session_id").GetGuid() != session);
        Assert.Equal(installation, nextSession.GetProperty("distinct_id").GetGuid());
        Assert.True(await reopened.SetEnabledAsync(false));
        Assert.False(File.Exists(StatePath));
        reopened.Record(UsageEvent.TaskCreated, ApplicationMode.Corporate);
        Assert.True(await reopened.SetEnabledAsync(true));
        Assert.NotEqual(installation, ReadState()!.Installation);
        Assert.DoesNotContain(ReadState()!.Events, item => item.Event == UsageEvent.TaskCreated);
    }

    [Fact]
    public async AsyncTask PartialCaptureRetriesOnlyUnconfirmedEventsUsingTheOriginalIdentifiers()
    {
        using var transport = new FakeTransport((requests, request, _) => AsyncTask.FromResult(requests == 1
            ? Response(HttpStatusCode.OK, request.Events.Select((item, index) =>
                (item.GetProperty("uuid").GetGuid(), index switch { 0 => "ok", 1 => "warning", 2 => "drop", _ => "retry" })))
            : RespondOk(request)));
        using var analytics = new ProductAnalytics(_root, Configuration, _clock, transport);
        analytics.ModeOpened(ApplicationMode.Personal);
        Assert.True(await analytics.SetEnabledAsync(true));
        analytics.Record(UsageEvent.TaskCreated, ApplicationMode.Personal);
        analytics.Record(UsageEvent.TaskPlanned, ApplicationMode.Personal);
        analytics.Record(UsageEvent.TaskCompleted, ApplicationMode.Personal);

        await UntilAsync(() => transport.Requests.Count >= 2 && ReadState()?.Events.Count == 0);
        var first = transport.Requests.First();
        var second = transport.Requests.Last();
        Assert.Equal(4, first.Events.Length);
        var retried = Assert.Single(second.Events);
        Assert.Equal(first.Events[3].GetRawText(), retried.GetRawText());
        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Equal(2, second.Attempt);
        Assert.Equal(Configuration.Endpoint, first.Uri);
        var properties = retried.GetProperty("properties");
        Assert.Equal(5, properties.EnumerateObject().Count());
        Assert.True(properties.GetProperty("$geoip_disable").GetBoolean());
        Assert.True(properties.GetProperty("task_analytics_test").GetBoolean());
        Assert.False(retried.GetProperty("options").GetProperty("process_person_profile").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(503)]
    [InlineData(429)]
    public async AsyncTask OfflineAndTransientFailuresSurviveClosingAndReopening(int status)
    {
        Guid id;
        Guid requestId;
        using (var failure = new FakeTransport((_, _, _) => status == 0
            ? AsyncTask.FromException<HttpResponseMessage>(new HttpRequestException("offline"))
            : AsyncTask.FromResult(new HttpResponseMessage((HttpStatusCode)status))))
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, failure))
        {
            Assert.True(await analytics.SetEnabledAsync(true));
            analytics.Record(UsageEvent.TaskCreated, ApplicationMode.Personal);
            await UntilAsync(() => !failure.Requests.IsEmpty);
            var attempted = failure.Requests.First();
            id = Assert.Single(attempted.Events).GetProperty("uuid").GetGuid();
            requestId = attempted.RequestId;
        }
        Assert.Contains(ReadState()!.Events, item => item.Id == id);
        using var available = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, available);
        await UntilAsync(() => !available.Requests.IsEmpty && ReadState()?.Events.Count == 0);
        var retry = available.Requests.First();
        Assert.Equal(id, Assert.Single(retry.Events).GetProperty("uuid").GetGuid());
        Assert.Equal(requestId, retry.RequestId);
        Assert.True(retry.Attempt >= 2);
    }

    [Fact]
    public async AsyncTask PermanentClientErrorDoesNotRetry()
    {
        using var transport = new FakeTransport((_, _, _) => AsyncTask.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        using var analytics = new ProductAnalytics(_root, Configuration, _clock, transport);
        Assert.True(await analytics.SetEnabledAsync(true));
        analytics.Record(UsageEvent.TaskCreated, ApplicationMode.Corporate);
        await UntilAsync(() => transport.Requests.Count == 1 && ReadState()?.Events.Count == 0);
        Assert.Null(ReadState()!.Batch);
    }

    [Fact]
    public async AsyncTask RevocationCancelsAnActiveRequestAndPurgesTheQueue()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new FakeTransport(async (_, _, token) =>
        {
            try { await AsyncTask.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.TrySetResult(); }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var analytics = new ProductAnalytics(_root, Configuration, _clock, transport);
        Assert.True(await analytics.SetEnabledAsync(true));
        analytics.ModeOpened(ApplicationMode.Corporate);
        await UntilAsync(() => transport.Requests.Count == 1);
        Assert.True(await analytics.SetEnabledAsync(false));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(File.Exists(StatePath));
        Assert.False(analytics.Enabled);
    }

    [Fact]
    public async AsyncTask BufferedParallelEventsAreBoundedAndExpiredEventsAreNeverSent()
    {
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var transport = new FakeTransport(async (_, _, token) =>
        {
            await stop.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }))
        using (var analytics = new ProductAnalytics(_root, Configuration, _clock, transport))
        {
            Assert.True(await analytics.SetEnabledAsync(true));
            Parallel.For(0, 1200, _ => analytics.Record(UsageEvent.TaskCreated, ApplicationMode.Personal));
            await UntilAsync(() => !transport.Requests.IsEmpty);
        }
        var buffered = ReadState()!.Events;
        Assert.InRange(buffered.Count, 20, ProductAnalytics.QueueLimit);
        Assert.Equal(buffered.Count, buffered.Select(item => item.Id).Distinct().Count());

        _clock.Advance(TimeSpan.FromDays(8));
        using var expired = new FakeTransport();
        using var reopened = new ProductAnalytics(_root, Configuration, _clock, expired);
        await UntilAsync(() => ReadState()?.Events.Count == 0);
        Assert.Empty(expired.Requests);
    }

    [Fact]
    public async AsyncTask PersonalWritesEmitOnlyAfterCommitAndIgnoreReplaysNoOpsAndFailures()
    {
        var seen = new List<UsageEvent>();
        using var store = new PersonalTaskStore(new PersonalDataPaths(_root), _clock) { Usage = seen.Add };
        var client = new PersonalTasksClient(store);
        var create = new DesktopCreateTaskCommand("Confidential task title", DesktopTaskPriority.Normal);
        var task = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(await client.CreateTaskAsync(create)).Value;
        await client.CreateTaskAsync(create);
        await client.PatchTaskAsync(new(task.Id, task.Version + 10, DesktopTaskField<string>.From("Rejected")));
        var changed = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await client.PatchTaskAsync(new(task.Id, task.Version, DesktopTaskField<string>.From("Changed")))).Value;
        var unchanged = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await client.PatchTaskAsync(new(task.Id, changed.Version, DesktopTaskField<string>.From("Changed")))).Value;
        var planned = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await client.PatchTaskAsync(new(task.Id, unchanged.Version, startAtUtc: DesktopTaskField<DateTimeOffset?>.From(_clock.GetUtcNow())))).Value;
        var started = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await client.TransitionTaskAsync(new(task.Id, planned.Version, DesktopTaskStatus.InProgress))).Value;
        Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(
            await client.TransitionTaskAsync(new(task.Id, started.Version, DesktopTaskStatus.Completed)));
        Assert.Equal(new[] { UsageEvent.TaskCreated, UsageEvent.TaskUpdated, UsageEvent.TaskPlanned, UsageEvent.TaskCompleted }, seen);
    }

    [Fact]
    public async AsyncTask CancellingAnEditorDoesNotProduceAnEventOrPersistADraft()
    {
        var seen = new List<UsageEvent>();
        using var model = new PersonalApplicationModel(_root, _clock, null, usage: seen.Add);
        var tasks = model.Tasks!;
        await tasks.NewCommand.ExecuteAsync();
        tasks.Editor!.Title = "Unsaved draft";
        await tasks.CancelCommand.ExecuteAsync();
        Assert.Null(tasks.Editor);
        Assert.Empty(model.Store.List());
        Assert.Empty(seen);
    }

    [Fact]
    public async AsyncTask AnalyticsCallbackFailureDoesNotChangeACommittedBusinessResult()
    {
        using var store = new PersonalTaskStore(new PersonalDataPaths(_root), _clock)
        {
            Usage = _ => throw new InvalidOperationException("Analytics unavailable"),
        };
        var client = new PersonalTasksClient(store);
        var result = await client.CreateTaskAsync(new("Committed", DesktopTaskPriority.Normal));
        var saved = Assert.IsType<DesktopTaskWriteResult<DesktopTaskDto>.Succeeded>(result);
        Assert.Equal("Committed", store.Get(saved.Value.Id)?.Title);
    }

    private AnalyticsState? ReadState()
    {
        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<AnalyticsState>(stream);
        }
        catch (IOException) { return null; }
    }

    private static string? Mode(JsonElement item) => item.GetProperty("properties").GetProperty("mode").GetString();

    private static async AsyncTask UntilAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8), "Expected analytics transition did not occur.");
            await AsyncTask.Delay(15);
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode status, IEnumerable<(Guid Id, string Result)> results) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                results = results.ToDictionary(item => item.Id, item => new { result = item.Result }),
            }), Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage RespondOk(Request request) =>
        Response(HttpStatusCode.OK, request.Events.Select(item => (item.GetProperty("uuid").GetGuid(), "ok")));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestClock : TimeProvider
    {
        private const int Speed = 30;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private long _offset;
        public void Advance(TimeSpan interval) => Interlocked.Add(ref _offset, interval.Ticks);
        public override DateTimeOffset GetUtcNow() =>
            new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(_elapsed.Elapsed.Ticks * Speed + Interlocked.Read(ref _offset));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            System.CreateTimer(callback, state, dueTime / Speed, period == Timeout.InfiniteTimeSpan ? period : period / Speed);
    }

    private sealed record Request(Uri Uri, Guid RequestId, int Attempt, JsonElement[] Events);

    private sealed class FakeTransport(Func<int, Request, CancellationToken, Task<HttpResponseMessage>>? reply = null) : HttpMessageHandler
    {
        private int _count;
        public ConcurrentQueue<Request> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            Assert.Equal("Bearer", message.Headers.Authorization?.Scheme);
            Assert.Equal(Configuration.Token, message.Headers.Authorization?.Parameter);
            Assert.False(message.Headers.Contains("Cookie"));
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            var request = new Request(message.RequestUri!, Guid.Parse(message.Headers.GetValues("PostHog-Request-Id").Single()),
                int.Parse(message.Headers.GetValues("PostHog-Attempt").Single()), body.RootElement.GetProperty("batch").EnumerateArray().Select(item => item.Clone()).ToArray());
            Requests.Enqueue(request);
            return reply is null ? RespondOk(request) : await reply(Interlocked.Increment(ref _count), request, token);
        }
    }
}
