using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Task.Desktop.ViewModels;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed partial class DesktopWorkApiClientTests
{
    private const string SettingsJson = """{"version":1,"language":"ru-RU","timeFormat":"24h","firstDayOfWeek":1,"workdayStart":"09:00:00","workdayEnd":"18:00:00","weekendDays":[6,7],"defaultTaskDurationMinutes":60,"defaultReminderOffsetMinutes":15,"dateOnlyReminderTime":"09:00:00","autostartEnabled":true,"allowLocalPaths":true,"confirmCatalogDelete":true,"missingFileBehavior":"show_actions"}""";
    private const string PreferencesJson = """{"version":1,"enabled":true,"desktopEnabled":true,"soundEnabled":true,"defaultSnoozeMinutes":15,"quietHoursStart":null,"quietHoursEnd":null,"quietHoursTimeZone":null}""";

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async System.Threading.Tasks.Task SettingsChoicesRoundTripThroughExistingApiAndReload(int day)
    {
        var stored = JsonNode.Parse(SettingsJson)!;
        await using var fixture = await Fixture.CreateAsync(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("preferences")) return Json(HttpStatusCode.OK, PreferencesJson);
            if (request.Method == HttpMethod.Patch)
            {
                var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                foreach (var pair in patch) stored[pair.Key] = pair.Value?.DeepClone();
                stored["version"] = 2;
            }
            return Json(HttpStatusCode.OK, stored.ToJsonString());
        });
        using (var hub = await SettingsHub(fixture.Client))
        {
            Assert.False(hub.HasProfileDraft);
            Assert.Equal("Русский", Assert.Single(hub.LanguageChoices).Value);
            Assert.Equal("Понедельник", hub.WeekdayChoices.Single(item => item.Key == 1).Value);
            Assert.Equal("09:00", hub.WorkdayStartDisplay);
            hub.FirstDayOfWeek = day;
            hub.WorkdayStartDisplay = "08:30"; hub.WorkdayEndDisplay = "17:15";
            hub.ReminderTimeDisplay = "07:45";
            hub.UserTimeFormat = "12h";
            await hub.SaveUserSettingsCommand.ExecuteAsync();
            Assert.Equal(WorkHubFeedbackKind.Success, hub.FeedbackKind);
            Assert.False(hub.HasProfileDraft);
        }
        using var reopened = await SettingsHub(fixture.Client);
        Assert.Equal(day, reopened.FirstDayOfWeek);
        Assert.Equal("08:30", reopened.WorkdayStartDisplay);
        Assert.Equal("17:15", reopened.WorkdayEndDisplay);
        Assert.Equal("07:45", reopened.ReminderTimeDisplay);
        Assert.Equal("12h", reopened.UserTimeFormat);
        var write = Assert.Single(fixture.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Equal("\"v1\"", write.IfMatch);
        var body = JsonNode.Parse(write.Body)!;
        Assert.Equal(day, body["firstDayOfWeek"]!.GetValue<int>());
        Assert.Equal("08:30:00", body["workdayStart"]!.GetValue<string>());
        Assert.Equal("07:45:00", body["dateOnlyReminderTime"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("start", "25:00")]
    [InlineData("start", "09:60")]
    [InlineData("end", "")]
    [InlineData("end", "12 PM")]
    [InlineData("reminder", "9")]
    [InlineData("reminder", "tomorrow")]
    [InlineData("quiet", "24:00")]
    [InlineData("quiet", "22:00")]
    [InlineData("language", "en-US")]
    [InlineData("day", "0")]
    public async System.Threading.Tasks.Task InvalidSettingsAreExplainedBeforeAnyWrite(string field, string value)
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("preferences") ? PreferencesJson : SettingsJson)));
        using var hub = await SettingsHub(fixture.Client);
        switch (field)
        {
            case "start": hub.WorkdayStartDisplay = value; break;
            case "end": hub.WorkdayEndDisplay = value; break;
            case "reminder": hub.ReminderTimeDisplay = value; break;
            case "quiet": hub.QuietStartDisplay = value; break;
            case "language": hub.UserLanguage = value; break;
            case "day": hub.FirstDayOfWeek = int.Parse(value); break;
        }
        Assert.NotNull(field == "quiet" ? hub.NotificationSettingsError : hub.ProfileSettingsError);
        await (field == "quiet" ? hub.SaveNotificationPreferencesCommand : hub.SaveUserSettingsCommand).ExecuteAsync();
        Assert.Equal(WorkHubFeedbackKind.Warning, hub.FeedbackKind);
        Assert.DoesNotContain(fixture.Requests, r => r.Method != HttpMethod.Get);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    public async System.Threading.Tasks.Task SettingsWriteErrorRetainsDraftAndDoesNotReportSuccess(HttpStatusCode status)
    {
        await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(
            request.Method == HttpMethod.Patch ? Json(status, "{}")
                : Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("preferences") ? PreferencesJson : SettingsJson)));
        using var hub = await SettingsHub(fixture.Client);
        hub.WorkdayStartDisplay = "10:30";
        await hub.SaveUserSettingsCommand.ExecuteAsync();
        Assert.NotEqual(WorkHubFeedbackKind.Success, hub.FeedbackKind);
        Assert.Equal("10:30", hub.WorkdayStartDisplay);
        Assert.True(hub.HasProfileDraft);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    public async System.Threading.Tasks.Task TimeEditingIsCultureIndependentAndPreservesSavedSeconds(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            await using var fixture = await Fixture.CreateAsync((request, _) => System.Threading.Tasks.Task.FromResult(Json(HttpStatusCode.OK,
                request.RequestUri!.AbsolutePath.EndsWith("preferences") ? PreferencesJson : SettingsJson.Replace("09:00:00", "09:00:35"))));
            using var hub = await SettingsHub(fixture.Client);
            Assert.Equal("09:00:35", hub.WorkdayStartDisplay);
            Assert.False(hub.HasProfileDraft);
            hub.WorkdayStartDisplay = "21:30";
            Assert.Equal("21:30:00", hub.UserWorkdayStart);
            Assert.Null(hub.ProfileSettingsError);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static async System.Threading.Tasks.Task<WorkHubViewModel> SettingsHub(IDesktopWorkApiClient client)
    {
        var hub = new WorkHubViewModel(client, ["Settings.ReadOwn", "Settings.UpdateOwn"]);
        hub.Activate(WorkHubArea.Settings);
        for (var i = 0; i < 100 && hub.RefreshCommand.IsExecuting; i++) await System.Threading.Tasks.Task.Delay(10);
        Assert.True(hub.SaveUserSettingsCommand.CanExecute(null));
        return hub;
    }
}
