using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Task.Desktop.Infrastructure;

/// <summary>Opt-in WPF adapters. Only expansion/column sizing/sort change; visibility and data stay authoritative.</summary>
public static class ViewState
{
    internal static string DeviceNamespace
    {
        get
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return Environment.MachineName + "/" + identity.User?.Value;
        }
    }
    public static readonly DependencyProperty StoreProperty = DependencyProperty.RegisterAttached("Store", typeof(IViewStateStore), typeof(ViewState), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetStore(DependencyObject target, IViewStateStore? value) => target.SetValue(StoreProperty, value);
    public static IViewStateStore? GetStore(DependencyObject target) => (IViewStateStore?)target.GetValue(StoreProperty);
    public static readonly DependencyProperty SectionProperty = DependencyProperty.RegisterAttached("Section", typeof(string), typeof(ViewState), new PropertyMetadata(null, OnKeyChanged));
    public static void SetSection(DependencyObject target, string value) => target.SetValue(SectionProperty, value);
    public static string? GetSection(DependencyObject target) => (string?)target.GetValue(SectionProperty);
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached("Surface", typeof(string), typeof(ViewState), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnKeyChanged));
    public static void SetSurface(DependencyObject target, string value) => target.SetValue(SurfaceProperty, value);
    public static string? GetSurface(DependencyObject target) => (string?)target.GetValue(SurfaceProperty);
    public static readonly DependencyProperty ColumnIdProperty = DependencyProperty.RegisterAttached("ColumnId", typeof(string), typeof(ViewState));
    public static void SetColumnId(DependencyObject target, string value) => target.SetValue(ColumnIdProperty, value);
    public static string? GetColumnId(DependencyObject target) => (string?)target.GetValue(ColumnIdProperty);
    private static readonly DependencyProperty AdapterProperty = DependencyProperty.RegisterAttached("Adapter", typeof(Adapter), typeof(ViewState));
    private static readonly DependencyProperty ClosingProperty = DependencyProperty.RegisterAttached("Closing", typeof(bool), typeof(ViewState), new PropertyMetadata(false));
    internal static bool IsCompletingClose(Window window) => (bool)window.GetValue(ClosingProperty);

    public static void Attach(Window window, ViewStateStore store)
    {
        SetStore(window, store);
        bool saving = false;
        async void Closing(object? sender, CancelEventArgs args)
        {
            if (args.Cancel || IsCompletingClose(window)) return;
            args.Cancel = true;
            if (saving) return;
            saving = true;
            window.IsEnabled = false;
            await store.DisposeAsync(); // Worker I/O finishes before process shutdown.
            window.SetValue(ClosingProperty, true);
            window.Close();
        }
        void Closed(object? sender, EventArgs args)
        {
            window.Closing -= Closing; window.Closed -= Closed;
            SetStore(window, null);
        }
        window.Closing += Closing; window.Closed += Closed;
    }

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Expander && target is not DataGrid) return;
        var element = (FrameworkElement)target;
        if (element.GetValue(AdapterProperty) is Adapter old) old.Dispose();
        element.SetValue(AdapterProperty, new Adapter(element));
    }

    private sealed class Adapter : IDisposable
    {
        private readonly FrameworkElement _element;
        private readonly List<(DependencyPropertyDescriptor Descriptor, object Target, EventHandler Handler)> _subscriptions = [];
        private IViewStateStore? _store;
        private int _generation;
        private bool _restoring;
        private DataGridColumn? _resizedColumn;
        private bool _resizeChanged;
        private ViewSort? _sort;
        internal bool Connected => _store is not null;

        public Adapter(FrameworkElement element)
        {
            _element = element;
            element.Loaded += Loaded; element.Unloaded += Unloaded;
            if (element.IsLoaded) Load();
        }
        private void Loaded(object sender, RoutedEventArgs args) => Load();
        private async void Load()
        {
            Disconnect();
            var generation = _generation;
            var store = GetStore(_element);
            if (store is null) return;
            await store.Ready;
            if (generation != _generation || !_element.IsLoaded || !ReferenceEquals(store, GetStore(_element))) return;
            _store = store;
            _restoring = true;
            try
            {
                if (_element is Expander expander && GetSection(expander) is { } key && ViewStateKeys.Sections.Contains(key))
                {
                    RestoreExpansion(expander);
                    expander.Expanded += ExpansionChanged; expander.Collapsed += ExpansionChanged;
                    expander.IsVisibleChanged += VisibilityChanged;
                }
                if (_element is DataGrid grid && GetSurface(grid) is { } surface && ViewStateKeys.LinkSurfaces.Contains(surface))
                {
                    foreach (var column in grid.Columns.Where(column => grid.CanUserResizeColumns && column.CanUserResize))
                    {
                        if (GetColumnId(column) is not { } id || !ViewStateKeys.Columns.Contains(id)) continue;
                        if (store.Width(surface + "/" + id, column.MinWidth, column.MaxWidth) is { } width)
                            column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel); // WPF pixels are DIP.
                        Subscribe(column, DataGridColumn.WidthProperty, (_, _) =>
                        { if (!_restoring && ReferenceEquals(column, _resizedColumn)) _resizeChanged = true; });
                    }
                    _sort = store.Sort(surface);
                    ApplySort(grid);
                    grid.Sorting += Sorting;
                    grid.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted), true);
                    grid.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(DragDelta), true);
                    grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(DragCompleted), true);
                    Subscribe(grid, ItemsControl.ItemsSourceProperty, (_, _) => ApplySort(grid));
                }
            }
            finally { _restoring = false; }
        }
        private void Subscribe(object target, DependencyProperty property, EventHandler handler)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, target.GetType())!;
            descriptor.AddValueChanged(target, handler);
            _subscriptions.Add((descriptor, target, handler));
        }
        private void RestoreExpansion(Expander expander)
        {
            if (!expander.IsVisible) return;
            _restoring = true;
            try { if (_store?.Expanded(GetSection(expander)!) is { } expanded) expander.SetCurrentValue(Expander.IsExpandedProperty, expanded); }
            finally { _restoring = false; }
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        { if (args.NewValue is true) RestoreExpansion((Expander)sender); }
        private void ExpansionChanged(object sender, RoutedEventArgs args)
        {
            if (!ReferenceEquals(args.OriginalSource, sender) || _restoring || sender is not Expander { IsVisible: true } expander) return;
            _store?.SetExpanded(GetSection(expander)!, expander.IsExpanded);
        }
        private static DataGridColumnHeader? Header(object original)
        {
            var node = original as DependencyObject;
            while (node is not null)
            {
                if (node is DataGridColumnHeader header) return header;
                node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : null;
            }
            return null;
        }
        private void DragStarted(object sender, DragStartedEventArgs args)
        {
            var grid = (DataGrid)sender;
            var header = Header(args.OriginalSource);
            _resizedColumn = header?.Column;
            // WPF's left gripper resizes the previous visible column, including after reorder.
            if (header is not null && ReferenceEquals(args.OriginalSource, header.Template.FindName("PART_LeftHeaderGripper", header)))
                _resizedColumn = grid.Columns.Where(column => column.Visibility == Visibility.Visible && column.DisplayIndex < header.DisplayIndex)
                    .OrderByDescending(column => column.DisplayIndex).FirstOrDefault();
            _resizeChanged = false;
        }
        private void DragCompleted(object sender, DragCompletedEventArgs args)
        {
            if (!args.Canceled) SaveWidths((DataGrid)sender);
            _resizedColumn = null; _resizeChanged = false;
        }
        private void DragDelta(object sender, DragDeltaEventArgs args)
        { if (_resizedColumn is not null && args.HorizontalChange != 0) _resizeChanged = true; }
        private void SaveWidths(DataGrid grid)
        {
            if (!_resizeChanged || _restoring || !grid.CanUserResizeColumns || _resizedColumn is not { CanUserResize: true } column) return;
            if (GetColumnId(column) is { } id)
                _store?.SetWidth(GetSurface(grid) + "/" + id, column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth, column.MinWidth, column.MaxWidth);
        }
        private void Sorting(object sender, DataGridSortingEventArgs args)
        {
            var grid = (DataGrid)sender;
            args.Handled = true; // This complete local list supports one primary sort.
            if (!ViewStateKeys.SortFields.Contains(args.Column.SortMemberPath)) return;
            _sort = new(args.Column.SortMemberPath, args.Column.SortDirection == ListSortDirection.Ascending);
            _store?.SetSort(GetSurface(grid)!, _sort);
            ApplySort(grid);
        }
        private void ApplySort(DataGrid grid)
        {
            if (_sort is null || grid.ItemsSource is null || !grid.CanUserSortColumns) return;
            var column = grid.Columns.FirstOrDefault(column => column.SortMemberPath == _sort.Field && column.CanUserSort);
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (column is null || !view.CanSort) return;
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new(_sort.Field, _sort.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
            }
            foreach (var item in grid.Columns) item.SortDirection = ReferenceEquals(item, column) ? view.SortDescriptions[0].Direction : null;
        }
        private void Unloaded(object sender, RoutedEventArgs args) => Disconnect();
        private void Disconnect()
        {
            _generation++;
            foreach (var subscription in _subscriptions) subscription.Descriptor.RemoveValueChanged(subscription.Target, subscription.Handler);
            _subscriptions.Clear();
            if (_element is Expander expander)
            { expander.Expanded -= ExpansionChanged; expander.Collapsed -= ExpansionChanged; expander.IsVisibleChanged -= VisibilityChanged; }
            if (_element is DataGrid grid)
            {
                grid.Sorting -= Sorting;
                grid.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted));
                grid.RemoveHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(DragDelta));
                grid.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(DragCompleted));
            }
            _store = null; _resizedColumn = null; _resizeChanged = false;
        }
        public void Dispose() { Disconnect(); _element.Loaded -= Loaded; _element.Unloaded -= Unloaded; }
    }
}
