using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Task.Desktop.ViewModels;

namespace Task.Desktop;

/// <summary>PasswordBox and focus bridge for the authentication workflow.</summary>
public partial class AuthWindow : Window
{
    private readonly Dictionary<PasswordBox, (ViewModelBase Owner, Action Clear)> _passwordClearBindings = [];

    public AuthWindow(AuthWorkflowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitialFocus_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            control.Focus();
        }
    }

    private void LoginSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LoginViewModel viewModel } element
            && FindNamedElement<PasswordBox>(element, "LoginPasswordBox") is { } passwordBox)
        {
            viewModel.SignInCommand.Execute(passwordBox.Password);
        }
    }

    private void PasswordChangeSubmit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PasswordChangeViewModel viewModel } element)
        {
            return;
        }

        var current = FindNamedElement<PasswordBox>(element, "CurrentPasswordBox");
        var next = FindNamedElement<PasswordBox>(element, "NewPasswordBox");
        var confirmation = FindNamedElement<PasswordBox>(element, "ConfirmPasswordBox");
        if (current is null || next is null || confirmation is null)
        {
            return;
        }

        viewModel.ChangePasswordCommand.Execute(new PasswordChangeInput(
            current.Password,
            next.Password,
            confirmation.Password));
    }

    private void LoginPasswordBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: LoginViewModel viewModel } passwordBox)
        {
            UnbindPasswordClear(passwordBox);
            Action clear = passwordBox.Clear;
            viewModel.PasswordClearRequested += clear;
            _passwordClearBindings[passwordBox] = (viewModel, clear);
        }
    }

    private void LoginPasswordBox_Unloaded(object sender, RoutedEventArgs e) => UnbindPasswordClear(sender);

    private void PasswordChangeBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: PasswordChangeViewModel viewModel } passwordBox)
        {
            UnbindPasswordClear(passwordBox);
            Action clear = passwordBox.Clear;
            viewModel.PasswordsClearRequested += clear;
            _passwordClearBindings[passwordBox] = (viewModel, clear);
            if (passwordBox.Name == "CurrentPasswordBox")
            {
                passwordBox.Focus();
            }
        }
    }

    private void PasswordChangeBox_Unloaded(object sender, RoutedEventArgs e) => UnbindPasswordClear(sender);

    private void UnbindPasswordClear(object sender)
    {
        if (sender is not PasswordBox passwordBox
            || !_passwordClearBindings.Remove(passwordBox, out var binding))
        {
            return;
        }

        switch (binding.Owner)
        {
            case LoginViewModel login:
                login.PasswordClearRequested -= binding.Clear;
                break;
            case PasswordChangeViewModel passwordChange:
                passwordChange.PasswordsClearRequested -= binding.Clear;
                break;
        }
    }

    private static T? FindNamedElement<T>(FrameworkElement source, string name)
        where T : FrameworkElement
    {
        for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ContentPresenter presenter
                && presenter.ContentTemplate?.FindName(name, presenter) is T templatedResult)
            {
                return templatedResult;
            }

            if (current is FrameworkElement element && element.FindName(name) is T result)
            {
                return result;
            }
        }

        return null;
    }
}
