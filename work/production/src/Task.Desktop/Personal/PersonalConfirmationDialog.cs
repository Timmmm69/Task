using System.Windows;
using System.Windows.Controls;

namespace Task.Desktop.Personal;

internal sealed class PersonalConfirmationDialog : Window
{
    private PersonalConfirmationDialog(Window owner, string title, string message, string? action, string? details = null)
    {
        Owner = owner; Title = title; Icon = owner.Icon;
        Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)owner.FindResource("Task.Window.Style");
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = title, Style = (Style)owner.FindResource("Task.State.Title") });
        body.Children.Add(new TextBlock { Text = message, Style = (Style)owner.FindResource("Task.State.Body"), Margin = new Thickness(0, 12, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (details is not null) body.Children.Add(new Expander { Header = "Подробности", Margin = new Thickness(0, 0, 0, 16), Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap } });
        var cancel = new Button { Content = action is null ? "Закрыть" : "Отмена", IsCancel = true, IsDefault = true, Style = (Style)owner.FindResource("Task.Button.Secondary"), Margin = new Thickness(0, 0, 10, 0) };
        actions.Children.Add(cancel);
        if (action is not null)
        {
            var confirm = new Button { Content = action, Style = (Style)owner.FindResource("Task.Button.Primary") };
            confirm.Click += (_, _) => DialogResult = true;
            actions.Children.Add(confirm);
        }
        body.Children.Add(actions); Content = body;
        Loaded += (_, _) => cancel.Focus();
    }
    internal static bool Confirm(Window owner, string title, string message, string action) => new PersonalConfirmationDialog(owner, title, message, action).ShowDialog() == true;
    internal static void Notice(Window owner, string title, string message, string? details = null) => new PersonalConfirmationDialog(owner, title, message, null, details).ShowDialog();
}
