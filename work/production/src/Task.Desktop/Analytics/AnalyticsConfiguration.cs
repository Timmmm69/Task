using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Task.Desktop.Analytics;

internal sealed record AnalyticsConfiguration(Uri Endpoint, string Token, bool IsTest, bool RequiresConsent)
{
    public string Destination => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Endpoint}|{Token}|{IsTest}")));

    public static AnalyticsConfiguration? Load()
    {
        var assembly = typeof(AnalyticsConfiguration).Assembly;
        string? BuildSetting(string name) =>
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == name)?.Value;
        string? Read(string name) => Environment.GetEnvironmentVariable(name) ?? BuildSetting(name);

        const string consentSetting = "TASK_POSTHOG_REQUIRE_CONSENT";
        var requiresConsent = BuildSetting(consentSetting) == "1" || Read(consentSetting) == "1";
        return Parse(Read("TASK_POSTHOG_HOST"), Read("TASK_POSTHOG_PROJECT_TOKEN"), Read("TASK_POSTHOG_TEST") == "1", requiresConsent);
    }

    internal static AnalyticsConfiguration? Parse(string? host, string? token, bool isTest, bool requiresConsent = false)
    {
        var origin = host?.Trim().TrimEnd('/');
        if (origin is not ("https://us.i.posthog.com" or "https://eu.i.posthog.com")
            || token is null || token.Length is < 8 or > 200 || !token.StartsWith("phc_", StringComparison.Ordinal)
            || token.AsSpan(4).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-"))
            return null;

        return new(new Uri(origin + "/i/v1/analytics/events"), token, isTest, requiresConsent);
    }
}
