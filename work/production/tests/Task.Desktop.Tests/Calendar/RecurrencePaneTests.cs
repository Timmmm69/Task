using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Task.Application.Calendar;
using Task.Desktop.Calendar;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests.Calendar;

public sealed class RecurrencePaneTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task RevokedAccess_ClosesPaneAndClearsProtectedData()
    {
        var pane = CreatePane(new FakeClient(_ => global::System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<JsonElement>>(Succeeded(ListJson()))));
        pane.SetAccess(["Recurrence.Read"], active: true);
        await pane.OpenCommand.ExecuteAsync();

        pane.SetAccess([], active: true);

        Assert.False(pane.IsOpen);
        Assert.Empty(pane.Series);
        Assert.Empty(pane.Occurrences);
        Assert.Null(pane.Selected);
        pane.Dispose();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DelayedLoadAfterClose_DoesNotRepopulatePane()
    {
        var delayed = new TaskCompletionSource<DesktopCalendarResult<JsonElement>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pane = CreatePane(new FakeClient(_ => delayed.Task));
        pane.SetAccess(["Recurrence.Read"], active: true);
        var opening = pane.OpenCommand.ExecuteAsync();
        await global::System.Threading.Tasks.Task.Yield();
        await pane.CloseCommand.ExecuteAsync();
        delayed.SetResult(Succeeded(ListJson(Definition(Guid.NewGuid()))));
        await opening;

        Assert.False(pane.IsOpen);
        Assert.Empty(pane.Series);
        pane.Dispose();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task TransientSaveRetry_ReusesIdempotencyKey()
    {
        var definition = Definition(Guid.NewGuid()); var writes = 0;
        var client = new FakeClient(call =>
        {
            if (call.Method == HttpMethod.Post && call.Path.Length == 0)
                return global::System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<JsonElement>>(++writes == 1
                    ? new DesktopCalendarResult<JsonElement>.ServerUnavailable() : Succeeded(SeriesJson(definition)));
            return global::System.Threading.Tasks.Task.FromResult<DesktopCalendarResult<JsonElement>>(Succeeded(ListJson()));
        });
        var pane = CreatePane(client);
        pane.SetAccess(["Recurrence.Read", "Recurrence.Manage"], active: true);
        await pane.OpenCommand.ExecuteAsync();
        await pane.NewCommand.ExecuteAsync();
        pane.Editor.Title = "Еженедельная встреча";

        await pane.SaveCommand.ExecuteAsync();
        await pane.SaveCommand.ExecuteAsync();

        var keys = client.Calls.Where(c => c.Method == HttpMethod.Post && c.Path.Length == 0).Select(c => c.Key).ToArray();
        Assert.Equal(2, keys.Length);
        Assert.NotNull(keys[0]);
        Assert.Equal(keys[0], keys[1]);
        pane.Dispose();
    }

    [Fact]
    public void SourceWorkdays_RoundTripRetainsWeekdays()
    {
        var actor = Guid.NewGuid();
        var source = Definition(actor) with { Frequency = "daily", Weekdays = [1, 2, 3, 4, 5] };
        var editor = new RecurrenceEditorViewModel(source);

        var roundTrip = editor.Build(actor);

        Assert.Equal("workdays", editor.Frequency);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, roundTrip.Weekdays);
        Assert.Equal("daily", roundTrip.Frequency);
    }

    private static RecurrencePaneViewModel CreatePane(FakeClient client) => new(client, Guid.NewGuid());
    private static RecurrenceDefinition Definition(Guid author) => new()
    {
        Status = "active",
        Frequency = "weekly",
        Interval = 1,
        Weekdays = [1],
        OccurrenceStartDate = new DateOnly(2026, 1, 5),
        TimeZone = "UTC",
        Template = new RecurrenceTemplateData { Title = "Серия", AuthorUserId = author, Priority = "normal" },
    };
    private static DesktopCalendarResult<JsonElement>.Succeeded Succeeded(JsonElement element) => new(element);
    private static JsonElement ListJson(RecurrenceDefinition? definition = null)
    {
        var items = definition is null ? new JsonArray() : new JsonArray(JsonNode.Parse(SeriesJson(definition).GetRawText()));
        return JsonSerializer.SerializeToElement(new JsonObject { ["items"] = items }, RecurrenceService.JsonOptions);
    }
    private static JsonElement SeriesJson(RecurrenceDefinition definition)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(definition, RecurrenceService.JsonOptions))!.AsObject();
        node["id"] = Guid.NewGuid(); node["organizationId"] = Guid.NewGuid(); node["version"] = 1;
        node["createdAt"] = DateTimeOffset.UtcNow; node["updatedAt"] = DateTimeOffset.UtcNow;
        return JsonSerializer.SerializeToElement(node, RecurrenceService.JsonOptions);
    }

    private sealed class FakeClient(Func<Call, global::System.Threading.Tasks.Task<DesktopCalendarResult<JsonElement>>> send) : IDesktopRecurrenceApiClient
    {
        public List<Call> Calls { get; } = [];
        public global::System.Threading.Tasks.Task<DesktopCalendarResult<JsonElement>> SendAsync(HttpMethod method, string path, string? json, long? version, string? key, CancellationToken cancellationToken)
        { var call = new Call(method, path, key); Calls.Add(call); return send(call); }
    }
    private sealed record Call(HttpMethod Method, string Path, string? Key);
}
