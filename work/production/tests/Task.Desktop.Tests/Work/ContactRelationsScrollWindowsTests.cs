using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class ContactRelationsScrollWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task OpeningRelationEditorRevealsUsableFieldsAfterLongLists()
    {
        if (Environment.GetEnvironmentVariable("TASK_RELATION_SCROLL_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(OpeningRelationEditorRevealsUsableFieldsAfterLongLists));
            start.Environment["TASK_RELATION_SCROLL_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55));
                Assert.True(child.ExitCode == 0, await output + await error);
            }
            finally { if (!child.HasExited) child.Kill(true); }
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources.Add("Task.BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
            try
            {
                foreach (var size in new[] { (800d, 600d), (1100d, 1000d) })
                    foreach (var action in new[] { "add", "edit", "address", "delete" })
                    {
                        var contact = new DesktopContact(Guid.NewGuid(), 1, "Тестовый контакт", "Тест", null, null, "active", "active");
                        var details = new DesktopContactDetails(contact, null)
                        {
                            Channels = Enumerable.Range(1, 8).Select(i => new DesktopContactChannel(Guid.NewGuid(), "phone", $"+375 00 00000{i:00}")).ToArray()
                        };
                        var client = new WorkHubViewModelTests.FakeClient
                        {
                            RelationState = details,
                            ContactDetailHandler = (_, _) => System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(details))
                        };
                        using var model = new ContactCardViewModel(client, () => true, () => true, () => true);
                        model.SelectAsync(contact.Id).GetAwaiter().GetResult();
                        var view = new ContactRelationsView { DataContext = model };
                        var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                        var window = new Window { Content = scroll, Width = size.Item1, Height = size.Item2, ShowInTaskbar = false };
                        try
                        {
                            window.Show(); Pump();
                            var button = Descendants<Button>(view).First(b => action switch
                            {
                                "add" => AutomationProperties.GetAutomationId(b) == "AddContactChannel",
                                "address" => AutomationProperties.GetAutomationId(b) == "AddContactAddress",
                                "edit" => b.Content as string == "Изменить канал",
                                _ => b.Content as string == "Удалить канал"
                            });
                            button.BringIntoView(); Pump(); button.Focus();
                            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
                            Pump();
                            FrameworkElement field = action == "delete"
                                ? Descendants<Button>(view).Single(b => b.Content as string == "Подтвердить удаление")
                                : Descendants<ComboBox>(view).Single(c => c.IsVisible);
                            var bounds = field.TransformToAncestor(scroll).TransformBounds(new Rect(field.RenderSize));
                            Assert.True(bounds.Top >= -1 && bounds.Bottom <= scroll.ViewportHeight + 1,
                                $"{action} at {size}: editor field outside viewport: {bounds}, viewport {scroll.ViewportHeight}");
                            Assert.True(model.HasRelationDraft);
                        }
                        finally { window.Close(); Pump(); }
                    }
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(50));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
