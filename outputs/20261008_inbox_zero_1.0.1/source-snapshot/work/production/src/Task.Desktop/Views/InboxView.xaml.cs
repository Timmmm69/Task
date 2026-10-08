using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class InboxView : UserControl
{
    // No desktop realtime transport exists yet. Refresh the authoritative API while this view is open.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _hadItems;
    private bool _restoreFocus;
    private bool _zeroVisible;
    internal Func<bool> MotionEnabled { get; set; } = () => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private ExplorerDropOverlay? _fileOverlay;
    private int _fileDragRevision;
    public const string FileDropBlocker = "Файлы во входящие пока не поддерживаются: используйте каталог. Серверный Inbox file_link/rawPath не реализован.";
    private void RejectFileDrag(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
        _fileDragRevision++; e.Effects = DragDropEffects.None; e.Handled = true;
        if (_fileOverlay is null && System.Windows.Documents.AdornerLayer.GetAdornerLayer(this) is { } layer)
        { _fileOverlay = new(this) { IsHitTestVisible = false, Accepted = false, Message = FileDropBlocker }; layer.Add(_fileOverlay); }
    }
    private void ClearFileDrag()
    {
        _fileDragRevision++;
        if (_fileOverlay is not null) System.Windows.Documents.AdornerLayer.GetAdornerLayer(this)?.Remove(_fileOverlay);
        _fileOverlay = null;
    }
    private void FileDragLeave(object sender, DragEventArgs e)
    {
        var revision = _fileDragRevision;
        Dispatcher.BeginInvoke(() => { if (revision == _fileDragRevision) ClearFileDrag(); }, DispatcherPriority.Background);
    }
    private void RejectFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
        e.Effects = DragDropEffects.None; e.Handled = true; ClearFileDrag();
        MessageBox.Show(Window.GetWindow(this), FileDropBlocker, "Входящие", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    public InboxView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _refreshTimer.Tick += OnRefreshTick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ClearFileDrag();
        if (e.OldValue is InboxViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _restoreFocus = false;
        _zeroVisible = false;
        _hadItems = e.NewValue is InboxViewModel { HasItems: true };
        if (e.NewValue is InboxViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is InboxViewModel model)
        {
            model.PropertyChanged -= OnViewModelPropertyChanged;
            model.PropertyChanged += OnViewModelPropertyChanged;
        }
        _refreshTimer.Start();
    }

    internal async global::System.Threading.Tasks.Task RefreshVisibleInboxAsync()
    {
        if (DataContext is InboxViewModel { IsActive: true, IsRefreshing: false, IsBusy: false } model
            && model.State is not InboxScreenState.Forbidden and not InboxScreenState.SessionEnded)
            await model.RefreshCommand.ExecuteAsync();
    }

    private async void OnRefreshTick(object? sender, EventArgs e) => await RefreshVisibleInboxAsync();
    private void OnStartCapture(object sender, RoutedEventArgs e) => InboxCaptureTextBox.Focus();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ClearFileDrag();
        _refreshTimer.Stop();
        if (DataContext is InboxViewModel viewModel) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not InboxViewModel model) return;
        if (e.PropertyName == nameof(InboxViewModel.IsRefreshing) && model.IsRefreshing
            && InboxZeroPanel.IsKeyboardFocusWithin) _restoreFocus = true;
        if (e.PropertyName == nameof(InboxViewModel.IsRefreshing) && !model.IsRefreshing && model.HasItems && _restoreFocus)
        {
            _restoreFocus = false;
            Dispatcher.BeginInvoke(() => { if (IsLoaded && model.HasItems) InboxList.Focus(); }, DispatcherPriority.Input);
        }
        if (e.PropertyName == nameof(InboxViewModel.Items))
        {
            if (_hadItems && !model.HasItems)
                _restoreFocus = IsKeyboardFocusWithin && !InboxCaptureTextBox.IsKeyboardFocusWithin;
            _hadItems = model.HasItems;
        }
        if (e.PropertyName == nameof(InboxViewModel.ShowInboxZero))
        {
            if (model.ShowInboxZero && !_zeroVisible)
            {
                InboxZeroPanel.BeginAnimation(OpacityProperty, null);
                if (MotionEnabled()) InboxZeroPanel.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
            }
            else if (!model.ShowInboxZero) InboxZeroPanel.BeginAnimation(OpacityProperty, null);
            _zeroVisible = model.ShowInboxZero;
        }
        if (e.PropertyName == nameof(InboxViewModel.ShowInboxZero) && model.ShowInboxZero && _restoreFocus)
        {
            _restoreFocus = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsLoaded || !model.ShowInboxZero) return;
                if (InboxZeroCreateButton.IsEnabled) InboxZeroCreateButton.Focus();
                else InboxZeroHeading.Focus();
            }, DispatcherPriority.Input);
        }
        if (e.PropertyName != nameof(InboxViewModel.Conversion) || sender is not InboxViewModel { Conversion: not null }) return;
        Dispatcher.BeginInvoke(() =>
        {
            InboxConversionTitleBox.Focus();
            InboxConversionTitleBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnInboxListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not InboxViewModel viewModel
            || !viewModel.ConvertCommand.CanExecute(viewModel.SelectedItem)) return;
        viewModel.ConvertCommand.Execute(viewModel.SelectedItem);
        e.Handled = true;
    }
}
