using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Views;

public partial class InboxView : UserControl
{
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
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ClearFileDrag();
        if (e.OldValue is InboxViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is InboxViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ClearFileDrag();
        if (DataContext is InboxViewModel viewModel) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
