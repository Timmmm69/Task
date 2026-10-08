using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Data;
using Task.Desktop.TaskApi;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;

namespace Task.Desktop.Tests;

[CollectionDefinition("CompletionWpfUi", DisableParallelization = true)]
public sealed class CompletionWpfUiCollection;

[Collection("CompletionWpfUi")]
public sealed class CompletionFeedbackUiTests
{
    [Fact]
    public async global::System.Threading.Tasks.Task GlyphPulse_ResetsAfterPlaybackAndRecycling_WithoutChangingLayout()
    {
        await RunStaAsync(async () =>
        {
            var dto = new DesktopTaskDto(Guid.NewGuid(), Guid.NewGuid(), 2,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Завершена", Guid.NewGuid(),
                DesktopTaskStatus.Completed, DesktopTaskPriority.Normal, null, null, [], [], null);
            var pulse = new TaskCompletionFeedback(dto.Id, dto.Version, true);
            var glyph = new Path
            {
                Width = 16, Height = 16, Stretch = Stretch.Uniform,
                Data = Geometry.Parse("M 0,5 L 4,9 L 12,0"), Stroke = SystemColors.WindowTextBrush,
                DataContext = new TaskItemViewModel(dto, completionFeedback: pulse)
            };
            CompletionFeedbackBehavior.SetFeedback(glyph, pulse);
            var window = new Window { Content = glyph, Width = 160, Height = 100,
                Left = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show();
                window.UpdateLayout();
                var size = glyph.RenderSize;
                Assert.False(glyph.Focusable);
                await global::System.Threading.Tasks.Task.Delay(260);
                Assert.Equal(1, glyph.Opacity);
                Assert.Equal(size, glyph.RenderSize);
                Assert.False(pulse.TryPlay(dto.Id, dto.Version));

                var next = dto with { Id = Guid.NewGuid(), Version = 3 };
                var nextPulse = new TaskCompletionFeedback(next.Id, next.Version, true);
                glyph.DataContext = new TaskItemViewModel(next, completionFeedback: nextPulse);
                CompletionFeedbackBehavior.SetFeedback(glyph, nextPulse);
                // Simulate recycling while an animation is running.
                glyph.DataContext = new TaskItemViewModel(dto);
                CompletionFeedbackBehavior.SetFeedback(glyph, null);
                Assert.Equal(1, glyph.Opacity);
                Assert.Equal(size, glyph.RenderSize);
                window.Content = null;
                Assert.Equal(1, glyph.Opacity);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async global::System.Threading.Tasks.Task RecycledListContainers_DoNotCarryFeedbackToOtherTasksOrStealKeyboardFocus()
    {
        await RunStaAsync(async () =>
        {
            var first = new DesktopTaskDto(Guid.NewGuid(), Guid.NewGuid(), 2,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Завершена", Guid.NewGuid(),
                DesktopTaskStatus.Completed, DesktopTaskPriority.Normal, null, null, [], [], null);
            var feedback = new TaskCompletionFeedback(first.Id, 2, true);
            var rows = Enumerable.Range(0, 500).Select(index => index == 0
                ? new TaskItemViewModel(first, completionFeedback: feedback)
                : new TaskItemViewModel(first with { Id = Guid.NewGuid(), Status = DesktopTaskStatus.InProgress })).ToArray();
            var factory = new FrameworkElementFactory(typeof(Path));
            factory.SetValue(FrameworkElement.WidthProperty, 16d);
            factory.SetValue(FrameworkElement.HeightProperty, 16d);
            factory.SetValue(Path.DataProperty, Geometry.Parse("M 0,5 L 4,9 L 12,0"));
            factory.SetValue(Shape.StrokeProperty, SystemColors.WindowTextBrush);
            factory.SetBinding(CompletionFeedbackBehavior.FeedbackProperty, new Binding(nameof(TaskItemViewModel.CompletionFeedback)));
            var list = new ListBox { ItemsSource = rows, ItemTemplate = new DataTemplate { VisualTree = factory } };
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true);
            var window = new Window { Content = list, Width = 160, Height = 120,
                Left = -20000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show();
                window.UpdateLayout();
                list.SelectedIndex = 0;
                list.Focus();
                var focused = System.Windows.Input.Keyboard.FocusedElement;
                list.ScrollIntoView(rows[^1]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                window.UpdateLayout();
                var realized = rows.Select(row => list.ItemContainerGenerator.ContainerFromItem(row))
                    .OfType<ListBoxItem>().ToArray();
                Assert.InRange(realized.Length, 1, 20);
                Assert.Contains(realized, container => ReferenceEquals(container.DataContext, rows[^1]));
                foreach (var container in realized)
                {
                    var presenter = FindChild<Path>(container);
                    Assert.NotNull(presenter);
                    Assert.Null(CompletionFeedbackBehavior.GetFeedback(presenter));
                    Assert.Equal(1, presenter.Opacity);
                }
                Assert.Same(focused, System.Windows.Input.Keyboard.FocusedElement);
                var origin = new Button();
                var other = new TextBox();
                Assert.True(MainWindow.ShouldRestoreCompletionFocus(null, origin, window));
                Assert.True(MainWindow.ShouldRestoreCompletionFocus(origin, origin, window));
                Assert.False(MainWindow.ShouldRestoreCompletionFocus(other, origin, window));
            }
            finally { window.Close(); }
        });
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BoundList_ExpiryKeepsAnotherTasksPendingConfirmation()
    {
        await RunStaAsync(async () =>
        {
            var first = new DesktopTaskDto(Guid.NewGuid(), Guid.NewGuid(), 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Первая", Guid.NewGuid(),
                DesktopTaskStatus.InProgress, DesktopTaskPriority.Normal, null, null, [], [], null);
            var second = first with { Id = Guid.NewGuid(), Title = "Вторая" };
            var client = new TaskScreen.TasksViewModelTests.FakeTasksApiClient
            {
                TransitionResult = new DesktopTaskWriteResult<DesktopTaskDto>.Succeeded(
                    first with { Status = DesktopTaskStatus.Completed, Version = 2 }, 2, false)
            };
            client.EnqueuePage(new DesktopTasksApiResult<DesktopTaskPage>.Succeeded(new DesktopTaskPage([first, second], null, null)));
            var finish = new TaskCompletionSource();
            using var vm = new TasksViewModel(client, ["Task.Read", "Task.ChangeStatus"], () => true, _ => finish.Task);
            var list = new ListBox { DataContext = vm };
            list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(TasksViewModel.Items)));
            list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding(nameof(TasksViewModel.SelectedItem)) { Mode = BindingMode.TwoWay });
            await vm.ActivateAsync();
            vm.SelectedStatusFilter = "В работе";
            await vm.TransitionCommand.ExecuteAsync("Completed");
            await vm.ConfirmTransitionCommand.ExecuteAsync();
            vm.SelectedItem = vm.Items.Single(row => row.Id == second.Id);
            await vm.TransitionCommand.ExecuteAsync("Completed");
            finish.SetResult();
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.Single(vm.Items);
            Assert.Equal(second.Id, vm.SelectedItem!.Id);
            Assert.Same(vm.SelectedItem, list.SelectedItem);
            Assert.Equal(DesktopTaskStatus.Completed, vm.PendingTransition);
        });
    }

    [Fact]
    public void ProductionWiring_PreservesAccessibleStatusContrastAndRecycling()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Task.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var main = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(directory.FullName, "src", "Task.Desktop", "MainWindow.xaml"));
        string? Attribute(System.Xml.Linq.XElement element, string name) => element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == name || attribute.Name.LocalName.EndsWith("." + name))?.Value;
        var list = main.Descendants().Single(element => Attribute(element, "Name") == "TasksList");
        Assert.Equal("Recycling", Attribute(list, "VirtualizationMode"));
        Assert.Equal("True", Attribute(list, "IsVirtualizing"));
        var glyph = list.Descendants().Single(element => Attribute(element, "Feedback") is not null);
        Assert.Equal("{Binding CompletionFeedback}", Attribute(glyph, "Feedback"));
        Assert.Contains(list.Descendants(), element => Attribute(element, "Text") == "{Binding StatusText}");
        var announcement = main.Descendants().Single(element => Attribute(element, "AutomationId") == "TasksAnnouncement");
        Assert.Equal("Assertive", Attribute(announcement, "LiveSetting"));
        var styles = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(directory.FullName, "src", "Task.Desktop", "Resources", "Controls.Data.xaml"));
        var icon = styles.Descendants().Single(element => Attribute(element, "Key") == "Task.Semantic.Icon");
        Assert.Contains(icon.Descendants(), element => Attribute(element, "Binding")?.Contains("SystemParameters.HighContrast") == true);
        Assert.Contains(icon.Descendants(), element => Attribute(element, "Value")?.Contains("SystemColors.WindowTextBrushKey") == true);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ReducedMotion_GlyphStaysStatic()
    {
        await RunStaAsync(() =>
        {
            var pulse = new TaskCompletionFeedback(Guid.NewGuid(), 2, false);
            var glyph = new Path { Width = 16, Height = 16 };
            CompletionFeedbackBehavior.SetFeedback(glyph, pulse);
            glyph.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Equal(1, glyph.Opacity);
            Assert.False(pulse.TryPlay(pulse.TaskId, pulse.Version));
            return global::System.Threading.Tasks.Task.CompletedTask;
        });
    }

    private static global::System.Threading.Tasks.Task RunStaAsync(Func<global::System.Threading.Tasks.Task> action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completed.SetResult(); }
                catch (Exception error) { completed.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
