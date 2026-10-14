using Task.Desktop.Work;

namespace Task.Desktop.TaskApi;

public interface IDesktopDeadlineSettingsClient
{
    System.Threading.Tasks.Task<DesktopUserSettings?> GetDeadlineSettingsAsync(CancellationToken token = default);
}

public sealed partial class DesktopTasksApiClient : IDesktopDeadlineSettingsClient
{
    private DesktopWorkApiClient _deadlineSettings = null!;
    public async System.Threading.Tasks.Task<DesktopUserSettings?> GetDeadlineSettingsAsync(CancellationToken token = default)
        => await _deadlineSettings.GetUserSettingsAsync(token) is DesktopWorkResult<DesktopUserSettings>.Succeeded result ? result.Value : null;
}
