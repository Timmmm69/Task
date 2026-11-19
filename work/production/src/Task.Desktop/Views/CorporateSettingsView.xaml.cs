using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class CorporateSettingsView : UserControl
{
    public static readonly DependencyProperty HasInvalidInputProperty =
        DependencyProperty.Register(nameof(HasInvalidInput), typeof(bool), typeof(CorporateSettingsView));
    public static readonly DependencyProperty HasInvalidFieldsProperty =
        DependencyProperty.Register(nameof(HasInvalidFields), typeof(bool), typeof(CorporateSettingsView));

    public bool HasInvalidInput => (bool)GetValue(HasInvalidInputProperty);
    public bool HasInvalidFields => (bool)GetValue(HasInvalidFieldsProperty);

    public CorporateSettingsView()
    {
        InitializeComponent();
        Validation.AddErrorHandler(this, (_, _) => ScheduleValidationCheck());
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != SectionNavigation) return;
        SettingsScroll?.ScrollToTop();
        ScheduleValidationCheck();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkHubViewModel model || !model.DiscardCurrentSettingsDraft()) return;

        // A failed conversion leaves the source value intact, so it may not raise a change notification.
        foreach (var field in ActiveFields())
            field.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();

        UpdateInputValidation();
        SectionNavigation.Focus();
    }

    private void ScheduleValidationCheck() =>
        Dispatcher.BeginInvoke(UpdateInputValidation, DispatcherPriority.DataBind);

    private void UpdateInputValidation()
    {
        var invalid = ActiveFields().Any(Validation.GetHasError);
        SetValue(HasInvalidInputProperty, invalid);
        SetValue(HasInvalidFieldsProperty, TextFields(ProfileFields).Concat(TextFields(NotificationFields))
            .Concat(TextFields(OrganizationFields)).Any(Validation.GetHasError));
    }

    private IEnumerable<TextBox> ActiveFields() => DataContext is WorkHubViewModel model
        ? TextFields(model.IsServerSettings ? OrganizationFields : model.IsNotificationSettings ? NotificationFields : ProfileFields)
        : [];

    private static IEnumerable<TextBox> TextFields(DependencyObject? parent)
    {
        if (parent is null) yield break;
        if (parent is TextBox textBox)
        {
            yield return textBox;
            yield break;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in TextFields(VisualTreeHelper.GetChild(parent, index)))
                yield return child;
    }
}
