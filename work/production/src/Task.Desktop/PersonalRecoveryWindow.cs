using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Task.Desktop;

internal sealed class PersonalRecoveryWindow : Window
{
    internal event Action? RestoreRequested;
    internal event Action? SafetyRequested;
    internal event Action? CorporateRequested;
    internal event Action? ExitRequested;
    internal PersonalRecoveryWindow(string reason, string databasePath, bool canRestore, bool pending)
    {
        Title = "Task — Personal recovery"; Width = 680; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetAutomationId(this, "PersonalRecoveryWindow");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Personal недоступен", FontSize = 24, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = reason + "\n\nБаза не сброшена. Файл для диагностики:\n" + databasePath, TextWrapping = TextWrapping.Wrap });
        Add(panel, "Восстановить backup", "RestorePersonalBackup", canRestore, () => RestoreRequested?.Invoke());
        if (pending) Add(panel, "Вернуть safety copy", "RecoverPersonalSafety", canRestore, () => SafetyRequested?.Invoke());
        Add(panel, "Перейти в Corporate", "RecoveryCorporate", true, () => CorporateRequested?.Invoke());
        Add(panel, "Выход", "RecoveryExit", true, () => ExitRequested?.Invoke());
        Content = panel;
    }
    private static void Add(Panel panel, string label, string id, bool enabled, Action action)
    {
        var button = new Button { Content = label, IsEnabled = enabled, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(button, id); button.Click += (_, _) => action(); panel.Children.Add(button);
    }
}
