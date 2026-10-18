using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Task.Desktop.ViewModels;
using Task.Desktop.Views;
using Task.Desktop.Work;

namespace Task.Desktop.Tests.Work;

public sealed class ContactCardWindowsTests
{
    [Fact]
    public async System.Threading.Tasks.Task NativeCardSelectionEditSaveReadonlyAndCompactLayout()
    {
        if (Environment.GetEnvironmentVariable("TASK_CONTACT_CARD_PROBE") != "1")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("vstest"); start.ArgumentList.Add(GetType().Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + GetType().FullName + "." + nameof(NativeCardSelectionEditSaveReadonlyAndCompactLayout));
            start.Environment["TASK_CONTACT_CARD_PROBE"] = "1";
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55)); Assert.True(child.ExitCode == 0, await output + await error); }
            finally { if (!child.HasExited) child.Kill(true); }
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Task.Desktop;component/Resources/Theme.xaml", UriKind.Relative) });
            app.Resources.Add("Task.BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            app.Dispatcher.BeginInvoke(async () =>
            {
                Window? window = null;
                var errors = new StringWriter(); var listener = new TextWriterTraceListener(errors);
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                try
                {
                    var contact = new DesktopContact(Guid.NewGuid(), 2, "Анна Иванова", "Анна", "Иванова", "Корпоративный контакт", "active", "active");
                    var client = new WorkHubViewModelTests.FakeClient { Contacts = [contact], ContactDetailHandler = (_, _) =>
                        System.Threading.Tasks.Task.FromResult<DesktopWorkResult<DesktopContactDetails>>(new DesktopWorkResult<DesktopContactDetails>.Succeeded(new(contact, "Петровна"))) };
                    using var hub = new WorkHubViewModel(client, ["Contact.Read", "Contact.Update", "Contact.Create"]);
                    hub.Activate(WorkHubArea.Contacts);
                    var view = new WorkHubView { DataContext = hub, Background = Brushes.White };
                    window = new Window { Content = view, Width = 1100, Height = 1000, ShowInTaskbar = false, Title = "Task — проверка карточки контакта" };
                    window.Show(); Pump();
                    var list = Find<ListBox>(view, "ContactsList"); list.SelectedItem = contact; await hub.ContactSelectionTask; Pump();
                    Assert.Equal("Петровна", hub.ContactCard!.Details!.MiddleName); Save(view, "contact-view.png");
                    Invoke(Find<Button>(view, "EditContactButton")); Pump(); Assert.True(hub.ContactCard.IsEditing);
                    var field = Descendants<TextBox>(view).Single(x => x.IsVisible && AutomationProperties.GetName(x) == "Имя контакта");
                    field.Text = "Мария"; field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    Save(view, "contact-edit.png");
                    window.Width = 800; window.Height = 600; Pump();
                    var save = Find<Button>(view, "SaveContactButton"); save.BringIntoView(); Pump();
                    var bounds = save.TransformToAncestor(view).TransformBounds(new Rect(save.RenderSize));
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= view.ActualHeight + 1); Save(view, "contact-edit-compact.png");
                    Invoke(save); Pump(); Assert.Equal("Мария", hub.ContactCard.Details!.Contact.FirstName);
                    Assert.False(hub.ContactCard.IsEditing); Assert.Equal(3, hub.SelectedContact!.Version);
                    hub.UpdateCapabilities(["Contact.Read"]); await hub.RefreshCommand.ExecuteAsync();
                    list.SelectedItem = hub.Contacts.Single(); await hub.ContactSelectionTask;
                    Pump(); Assert.False(Find<Button>(view, "EditContactButton").IsEnabled);
                    window.Width = 1100; window.Height = 1000; Pump();
                    foreach (var scroll in Descendants<ScrollViewer>(view)) scroll.ScrollToTop(); Pump(); Save(view, "contact-readonly.png");
                    Assert.DoesNotContain("System.Windows.Data Error", errors.ToString());
                    done.SetResult();
                }
                catch (Exception e) { done.SetException(e); }
                finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); window?.Close(); app.Shutdown(); }
            });
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); await done.Task.WaitAsync(TimeSpan.FromSeconds(50));
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants<T>(root).Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void Save(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("TASK_CONTACT_CARD_EVIDENCE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); view.UpdateLayout();
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(view)
            { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(view), view.RenderSize), Stretch = Stretch.Fill }, null, new Rect(view.RenderSize));
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
