using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Infrastructure;

/// <summary>Local shortcuts share the button command and never intercept IME or key repeats.</summary>
public static class FormKeyboard
{
    public static readonly DependencyProperty SaveProperty = DependencyProperty.RegisterAttached(
        "Save", typeof(ICommand), typeof(FormKeyboard), new PropertyMetadata(null, Attach));
    public static readonly DependencyProperty CaptureProperty = DependencyProperty.RegisterAttached(
        "Capture", typeof(AsyncCommand), typeof(FormKeyboard), new PropertyMetadata(null, Attach));
    public static void SetSave(DependencyObject target, ICommand? value) => target.SetValue(SaveProperty, value);
    public static ICommand? GetSave(DependencyObject target) => (ICommand?)target.GetValue(SaveProperty);
    public static void SetCapture(DependencyObject target, AsyncCommand? value) => target.SetValue(CaptureProperty, value);
    public static AsyncCommand? GetCapture(DependencyObject target) => (AsyncCommand?)target.GetValue(CaptureProperty);
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not UIElement element) return;
        element.PreviewKeyDown -= OnKey;
        element.PreviewKeyDown += OnKey;
        if (target is TextBox input && e.Property == CaptureProperty)
        {
            input.TextChanged -= OnCaptureTextChanged;
            input.TextChanged += OnCaptureTextChanged;
        }
    }
    private static void OnCaptureTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { Text.Length: 0, IsVisible: true } input || GetCapture(input)?.IsExecuting != true) return;
        var context = input.DataContext;
        var window = Window.GetWindow(input);
        var owner = FindOwner(input);
        var focused = Keyboard.FocusedElement;
        input.Dispatcher.BeginInvoke(() =>
        {
            if (input.Text.Length == 0 && input.IsLoaded && input.IsVisible && ReferenceEquals(context, input.DataContext)
                && ReferenceEquals(focused, Keyboard.FocusedElement)
                && owner?.IsKeyboardFocusWithin == true && window?.IsActive == true) input.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }
    private static UserControl? FindOwner(DependencyObject element)
    {
        for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(element); parent is not null;
             parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            if (parent is UserControl view) return view;
        return null;
    }
    internal static bool IsShortcut(Key key, ModifierKeys modifiers, bool repeat, bool capture) =>
        !repeat && key == Key.Enter && modifiers == (capture ? ModifierKeys.None : ModifierKeys.Control);
    private static async void OnKey(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { IsVisible: true, IsKeyboardFocusWithin: true } element) return;
        // ImeProcessed is deliberately not treated as Enter: composition keeps ownership of the key.
        if (GetSave(element) is { } save && IsShortcut(e.Key, Keyboard.Modifiers, e.IsRepeat, false))
        {
            e.Handled = true;
            if (save.CanExecute(null)) save.Execute(null);
        }
        else if (element is TextBox { AcceptsReturn: false } input && GetCapture(input) is { } capture
            && IsShortcut(e.Key, Keyboard.Modifiers, e.IsRepeat, true))
        {
            e.Handled = true;
            if (!capture.CanExecute(null)) return;
            var context = input.DataContext;
            var window = Window.GetWindow(input);
            await capture.ExecuteAsync();
            // Only a confirmed successful write clears the model's text. No delayed focus into another view/account.
            if (input.Text.Length == 0 && input.IsLoaded && input.IsVisible && input.IsKeyboardFocusWithin
                && ReferenceEquals(context, input.DataContext) && window?.IsActive == true) input.Focus();
        }
    }
}
