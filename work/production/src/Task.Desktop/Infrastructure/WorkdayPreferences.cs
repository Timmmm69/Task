using System.Text.Json;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Infrastructure;

internal sealed record PhraseChoice(DateOnly Date, int Index);

public sealed partial class ViewStateStore
{
    private PhraseChoice? _phrase;
    private bool _notifyAtDayEnd = true;
    internal bool NotifyAtDayEnd { get { lock (_sync) return _notifyAtDayEnd; } }

    internal void SetDayEndNotifications(bool enabled) => Change(() => _notifyAtDayEnd = enabled);

    internal string ChoosePhrase(DateOnly date, bool another)
    {
        lock (_sync)
        {
            if (_phrase is null || _phrase.Date != date || another)
            {
                var index = ((_phrase?.Index ?? -1) + 1) % WorkdayPhrases.All.Length;
                Change(() => _phrase = new(date, index));
            }
            return WorkdayPhrases.All[_phrase?.Index ?? 0];
        }
    }

    private void ReadWorkdayPreferences(JsonElement root)
    {
        if (root.TryGetProperty("notifyAtDayEnd", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            _notifyAtDayEnd = false;
        if (root.TryGetProperty("phrase", out var phrase) && phrase.ValueKind == JsonValueKind.Object &&
            phrase.TryGetProperty("Date", out var date) && date.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", out var day) &&
            phrase.TryGetProperty("Index", out var index) && index.ValueKind == JsonValueKind.Number &&
            index.TryGetInt32(out var value) && value >= 0 && value < WorkdayPhrases.All.Length)
            _phrase = new(day, value);
    }
}
