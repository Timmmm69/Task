using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Task.Desktop.Tests.Calendar;

public sealed class CalendarAppearanceTests
{
    [Fact]
    public void CalendarTemplate_PopulatesDaysAndNavigatesMonthsYearsAndDecades()
    {
        OnSta(() =>
        {
            var theme = LoadTheme();
            var calendar = new global::System.Windows.Controls.Calendar
            {
                Resources = theme,
                Style = (Style)theme["Task.Calendar"],
                DisplayDate = new DateTime(2026, 10, 15),
                SelectedDate = new DateTime(2026, 10, 15),
            };
            Layout(calendar);
            var item = Assert.IsType<CalendarItem>(calendar.Template.FindName("PART_CalendarItem", calendar));
            var days = Assert.IsType<Grid>(item.Template.FindName("PART_MonthView", item));
            var periods = Assert.IsType<Grid>(item.Template.FindName("PART_YearView", item));
            Assert.Equal(42, days.Children.OfType<CalendarDayButton>().Count());
            Assert.All(days.Children.OfType<CalendarDayButton>(), day => Assert.IsType<Style>(day.FocusVisualStyle));
            Assert.Single(days.Children.OfType<CalendarDayButton>(), day => day.IsSelected);
            Assert.Equal(Visibility.Visible, days.Visibility);
            Assert.Equal(Visibility.Hidden, periods.Visibility);

            var next = Assert.IsType<Button>(item.Template.FindName("PART_NextButton", item));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(11, calendar.DisplayDate.Month);
            Assert.Equal(new DateTime(2026, 10, 15), calendar.SelectedDate);

            var header = Assert.IsType<Button>(item.Template.FindName("PART_HeaderButton", item));
            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(calendar);
            Assert.Equal(CalendarMode.Year, calendar.DisplayMode);
            Assert.Equal(Visibility.Hidden, days.Visibility);
            Assert.Equal(Visibility.Visible, periods.Visibility);
            Assert.Equal(12, periods.Children.OfType<CalendarButton>().Count());
            Assert.All(periods.Children.OfType<CalendarButton>(), period => Assert.IsType<Style>(period.FocusVisualStyle));

            header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(calendar);
            Assert.Equal(CalendarMode.Decade, calendar.DisplayMode);
            Assert.Equal(Visibility.Visible, periods.Visibility);

            calendar.DisplayMode = CalendarMode.Month;
            Layout(calendar);
            Assert.Equal(Visibility.Visible, days.Visibility);
            Assert.Equal(Visibility.Hidden, periods.Visibility);
        });
    }

    [Fact]
    public void DatePickerTemplate_KeepsNativePartsAndUsesSharedCalendarStyle()
    {
        OnSta(() =>
        {
            var theme = LoadTheme();
            var picker = new DatePicker
            {
                Resources = theme,
                Style = (Style)theme["Task.Input.Date"],
                SelectedDate = new DateTime(2026, 10, 24),
            };
            Layout(picker);
            Assert.IsType<DatePickerTextBox>(picker.Template.FindName("PART_TextBox", picker));
            Assert.IsType<Button>(picker.Template.FindName("PART_Button", picker));
            var popup = Assert.IsType<Popup>(picker.Template.FindName("PART_Popup", picker));
            var calendar = Assert.IsType<global::System.Windows.Controls.Calendar>(popup.Child);
            Assert.Same(theme["Task.Calendar"], picker.CalendarStyle);
            Assert.Same(picker.CalendarStyle, calendar.Style);
            Assert.Equal(new DateTime(2026, 10, 24), picker.SelectedDate);
            picker.SelectedDate = null;
            Assert.Null(picker.SelectedDate);
        });
    }

    private static ResourceDictionary LoadTheme() =>
        (ResourceDictionary)global::System.Windows.Application.LoadComponent(
            new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative));

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(400, 400));
        element.Arrange(new Rect(0, 0, 400, 400));
        element.UpdateLayout();
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF appearance check timed out.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
