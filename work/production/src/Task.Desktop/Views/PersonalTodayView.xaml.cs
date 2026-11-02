using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Task.Desktop.Personal;
using Task.Desktop.TaskApi;

namespace Task.Desktop.Views;

public partial class PersonalTodayView : UserControl
{
    public PersonalTodayView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Subscribe(); UpdateSummary(); };
        Unloaded += (_, _) => Unsubscribe();
        DataContextChanged += (_, _) => { Unsubscribe(); if (IsLoaded) Subscribe(); UpdateSummary(); };
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 760;
            DetailsColumn.Width = narrow ? new GridLength(0) : new GridLength(320);
            Grid.SetColumn(DetailsCard, narrow ? 0 : 1);
            Grid.SetRow(DetailsCard, narrow ? 1 : 0);
            DetailsCard.Margin = narrow ? new Thickness(0, 24, 24, 0) : new Thickness(0, 0, 0, 8);
        };
    }
    internal void FocusSelection() { if (IsVisible && TodayTasks.IsEnabled) TodayTasks.Focus(); }
    private PersonalTasksViewModel? _subscribed;
    private void Subscribe()
    {
        if (_subscribed is not null || DataContext is not PersonalTasksViewModel model) return;
        _subscribed = model;
        model.PropertyChanged += ModelChanged;
    }
    private void Unsubscribe()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= ModelChanged;
        _subscribed = null;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PersonalTasksViewModel.Items)) UpdateSummary();
        if (e.PropertyName == nameof(PersonalTasksViewModel.HasEditor) && sender is PersonalTasksViewModel { HasEditor: false })
            Dispatcher.BeginInvoke(() => { if (IsVisible && TodayTasks.IsEnabled) TodayTasks.Focus(); });
    }
    private void UpdateSummary()
    {
        var items = (DataContext as PersonalTasksViewModel)?.Items ?? [];
        TaskCountLabel.Text = $"Задач: {items.Count}";
        ActiveCountLabel.Text = $"Задач: {items.Count(t => t.Source.Status == DesktopTaskStatus.InProgress)}";
        var minutes = items.Sum(t => (long)(t.Source.Card?.PlannedDurationMinutes ?? 0));
        DurationLabel.Text = minutes == 0 ? "Без оценки" : minutes >= 60 ? $"{minutes / 60} ч {minutes % 60:00} мин" : $"{minutes} мин";
    }
}
