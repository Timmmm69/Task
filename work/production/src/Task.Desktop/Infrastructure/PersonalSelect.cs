using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Task.Desktop.Infrastructure;

public enum PersonalSelectVariant { Compact, Detailed, Search }

/// <summary>Shared window-scoped selectors for both modes. ItemsSource and shared collection views are never filtered or replaced.</summary>
public static class PersonalSelect
{
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.RegisterAttached(
        "Scope", typeof(bool), typeof(PersonalSelect), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, ScopeChanged));
    public static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "Variant", typeof(PersonalSelectVariant), typeof(PersonalSelect), new PropertyMetadata(PersonalSelectVariant.Compact));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(PersonalSelect), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(System.Windows.Media.Geometry), typeof(PersonalSelect), new PropertyMetadata(null));
    public static System.Windows.Media.Geometry? GetGlyph(DependencyObject target) => (System.Windows.Media.Geometry?)target.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject target, System.Windows.Media.Geometry? value) => target.SetValue(GlyphProperty, value);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(SelectState), typeof(PersonalSelect));
    public static bool GetScope(DependencyObject target) => (bool)target.GetValue(ScopeProperty);
    public static void SetScope(DependencyObject target, bool value) => target.SetValue(ScopeProperty, value);
    public static PersonalSelectVariant GetVariant(DependencyObject target) => (PersonalSelectVariant)target.GetValue(VariantProperty);
    public static void SetVariant(DependencyObject target, PersonalSelectVariant value) => target.SetValue(VariantProperty, value);
    public static string GetDescription(DependencyObject target) => (string)target.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject target, string value) => target.SetValue(DescriptionProperty, value);

    // A window's tunneled Escape handler runs before the popup's own key handler.
    internal static bool CloseOpenDropdown(DependencyObject root)
    {
        if (root is ComboBox { IsDropDownOpen: true } combo && GetScope(combo))
        { combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false); combo.Focus(); return true; }
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (CloseOpenDropdown(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }

    private static void ScopeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ComboBox combo) return;
        if ((bool)args.NewValue)
        {
            if (combo.GetValue(StateProperty) is not null) return;
            var state = new SelectState(combo);
            combo.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (combo.GetValue(StateProperty) is SelectState state)
        {
            state.Detach();
            combo.ClearValue(StateProperty);
        }
    }

    internal static string DisplayText(ComboBox combo, object item)
    {
        if (item is ComboBoxItem container) item = container.Content;
        object? value = item;
        var path = combo.DisplayMemberPath;
        if (string.IsNullOrEmpty(path)) path = TextSearch.GetTextPath(combo);
        if (!string.IsNullOrEmpty(path))
            foreach (var part in path.Split('.')) value = value is null ? null : TypeDescriptor.GetProperties(value)[part]?.GetValue(value);
        else if (combo.ItemTemplate is not null && item is not null)
        {
            // Custom recurrence rows show a local date/title, not the record's debug representation (IDs, versions, etc.).
            var properties = TypeDescriptor.GetProperties(item);
            var label = new[] { "Name", "Title", "Label", "Text" }.Select(name => properties[name]?.GetValue(item)).OfType<string>().FirstOrDefault();
            if (label is not null)
                value = properties["LocalDate"]?.GetValue(item) is DateOnly date ? $"{date:dd.MM.yyyy} — {label}" : label;
        }
        return Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
    }

    private static PersonalSelectVariant ChooseVariant(ComboBox combo)
    {
        var path = BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty)?.Path?.Path ?? "";
        var member = path.Split('.').Last();
        if (member is "Priorities" or "Triggers" or "ReminderOptions" or "ReminderTriggers") return PersonalSelectVariant.Detailed;
        if (member is "Projects" or "ProjectChoices" or "Tasks" or "Targets" or "Objects" or "ActiveObjects" or "Folders"
            or "CatalogMoveDestinations" or "NetworkResources" or "Occurrences" or "ProjectFilters" or "People"
            or "Counterparties" or "FileOptions" or "TaskOptions" or "ReminderTargets") return PersonalSelectVariant.Search;
        return combo.Items.Count > 10 ? PersonalSelectVariant.Search : PersonalSelectVariant.Compact;
    }

    private static string Describe(string text) => text switch
    {
        "Низкий" => "Можно выполнить позже",
        "Обычный" => "Стандартный приоритет задачи",
        "Высокий" => "Требует повышенного внимания",
        "Критический" => "Требует первоочередного внимания",
        "До начала" => "За указанное число минут до начала",
        "В момент начала" => "Когда наступит время начала",
        "До срока" => "За указанное число минут до срока",
        "В момент срока" => "Когда наступит срок задачи",
        "В конкретное время" => "В выбранные дату и время",
        "По настройке" => "Использовать настройку напоминаний",
        "Не напоминать" => "Без автоматического напоминания о сроке",
        "За 5 минут" or "За 15 минут" or "За 30 минут" or "За 1 час" or "За 1 день" => "До наступления срока задачи",
        "Другой интервал" => "Указать число минут до срока",
        _ => ""
    };

    private sealed class SelectState(ComboBox combo)
    {
        private TextBox? _search;
        private FrameworkElement? _popupRoot;
        private ControlTemplate? _originalTemplate;
        private Style? _originalItemStyle;
        private ItemsPanelTemplate? _originalPanel;
        private bool _originalTextSearch;
        private bool _configured;
        private bool _automaticVariant;
        private bool _updating;
        private string _query = "";
        private readonly List<int> _matches = [];
        private readonly Dictionary<ComboBoxItem, Style?> _directStyles = [];

        public void Attach()
        {
            combo.Loaded += Loaded;
            combo.Unloaded += Unloaded;
            combo.DropDownOpened += Opened;
            combo.DropDownClosed += Closed;
            combo.ItemContainerGenerator.StatusChanged += Generated;
            if (combo.IsLoaded) Configure();
        }
        private void Loaded(object sender, RoutedEventArgs args) => Configure();
        private void Configure()
        {
            if (_configured) return;
            combo.ApplyTemplate();
            _originalTemplate = combo.Template;
            _originalItemStyle = combo.ItemContainerStyle;
            _originalPanel = combo.ItemsPanel;
            _originalTextSearch = combo.IsTextSearchEnabled;
            combo.SetCurrentValue(Control.TemplateProperty, combo.FindResource("Personal.Select.Template"));
            combo.SetCurrentValue(ItemsControl.ItemContainerStyleProperty, combo.FindResource("Personal.Select.Item"));
            combo.SetCurrentValue(ItemsControl.ItemsPanelProperty, combo.FindResource("Personal.Select.Panel"));
            _automaticVariant = combo.ReadLocalValue(VariantProperty) == DependencyProperty.UnsetValue;
            if (_automaticVariant) combo.SetCurrentValue(VariantProperty, ChooseVariant(combo));
            combo.SetCurrentValue(ComboBox.IsTextSearchEnabledProperty, GetVariant(combo) != PersonalSelectVariant.Search);
            _configured = true;
        }
        private void Unloaded(object sender, RoutedEventArgs args)
        {
            combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            UnwirePopup();
        }
        private void Opened(object? sender, EventArgs args)
        {
            Configure();
            if (_automaticVariant) combo.SetCurrentValue(VariantProperty, ChooseVariant(combo));
            combo.SetCurrentValue(ComboBox.IsTextSearchEnabledProperty, GetVariant(combo) != PersonalSelectVariant.Search);
            combo.ApplyTemplate();
            UnwirePopup();
            _search = combo.Template.FindName("PART_Search", combo) as TextBox;
            _popupRoot = (combo.Template.FindName("PART_Popup", combo) as Popup)?.Child as FrameworkElement;
            if (_popupRoot is not null) _popupRoot.PreviewKeyDown += KeyDown;
            _query = "";
            if (_search is not null)
            {
                _search.Text = "";
                _search.TextChanged += SearchChanged;
            }
            combo.Dispatcher.BeginInvoke(() =>
            {
                if (!combo.IsDropDownOpen || !combo.IsLoaded) return;
                _popupRoot?.UpdateLayout();
                UpdateItems();
                if (GetVariant(combo) == PersonalSelectVariant.Search) _search?.Focus();
            }, DispatcherPriority.Input);
        }
        private void Closed(object? sender, EventArgs args)
        {
            _query = "";
            if (_search is not null) _search.Text = "";
            UpdateItems();
            UnwirePopup();
        }
        private void UnwirePopup()
        {
            if (_search is not null) _search.TextChanged -= SearchChanged;
            if (_popupRoot is not null) _popupRoot.PreviewKeyDown -= KeyDown;
            _search = null;
            _popupRoot = null;
        }
        private void Generated(object? sender, EventArgs args)
        {
            if (combo.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated && combo.IsDropDownOpen) UpdateItems();
        }
        private void SearchChanged(object sender, TextChangedEventArgs args)
        {
            _query = _search?.Text.Trim() ?? "";
            UpdateItems();
            // Text input inside a popup must never mark the enclosing task draft as changed.
            args.Handled = true;
        }
        private void UpdateItems()
        {
            if (_updating) return;
            _updating = true;
            try
            {
                _matches.Clear();
                var visibleMatches = 0;
                var words = _query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < combo.Items.Count; i++)
                {
                    var text = DisplayText(combo, combo.Items[i]);
                    var matches = words.All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase));
                    if (matches) visibleMatches++;
                    var item = combo.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem;
                    if (matches && (item?.IsEnabled ?? (combo.Items[i] as UIElement)?.IsEnabled ?? true)) _matches.Add(i);
                    if (item is null) continue;
                    // Already-authored ComboBoxItems bypass ItemsControl.ItemContainerStyle.
                    if (ReferenceEquals(combo.Items[i], item) && !_directStyles.ContainsKey(item))
                    {
                        _directStyles.Add(item, item.Style);
                        item.SetCurrentValue(FrameworkElement.StyleProperty, combo.FindResource("Personal.Select.Item"));
                    }
                    item.SetCurrentValue(UIElement.VisibilityProperty, matches ? Visibility.Visible : Visibility.Collapsed);
                    var sourcePath = BindingOperations.GetBinding(combo, ItemsControl.ItemsSourceProperty)?.Path?.Path ?? "";
                    var icon = sourcePath.Contains("People", StringComparison.Ordinal) ? "Task.Icon.Contacts"
                        : sourcePath.Contains("Task", StringComparison.Ordinal) ? "Task.Icon.Tasks" : "Task.Icon.Projects";
                    SetGlyph(item, GetVariant(combo) == PersonalSelectVariant.Search
                        ? combo.TryFindResource(icon) as System.Windows.Media.Geometry : null);
                    SetDescription(item, GetVariant(combo) == PersonalSelectVariant.Detailed ? Describe(text) : "");
                }
                if (combo.Template?.FindName("PART_Empty", combo) is TextBlock empty)
                    empty.Visibility = visibleMatches == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _updating = false; }
        }
        private void KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
                combo.Focus();
                return;
            }
            if (GetVariant(combo) != PersonalSelectVariant.Search) return;
            if (args.Key == Key.Oem2 && Keyboard.Modifiers == ModifierKeys.None && _search?.IsKeyboardFocusWithin != true)
            {
                _search?.Focus(); args.Handled = true; return;
            }
            if (args.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.None) return;
            if (args.Key == Key.Tab)
            {
                args.Handled = true;
                combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
                combo.Focus();
                combo.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
                return;
            }
            if (args.Key is not (Key.Down or Key.Up or Key.Enter or Key.Home or Key.End)) return;
            if (_search?.IsKeyboardFocusWithin == true && args.Key is Key.Home or Key.End) return;
            args.Handled = true;
            UpdateItems(); // An option may have become disabled since the last text edit.
            var focused = _matches.FindIndex(i => (combo.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem)?.IsKeyboardFocusWithin == true);
            if (args.Key == Key.Enter)
            {
                if (_matches.Count == 0) return;
                var index = _matches[focused >= 0 ? focused : 0];
                combo.SetCurrentValue(Selector.SelectedItemProperty, combo.Items[index]);
                combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
                combo.Focus();
            }
            else if (_matches.Count > 0)
            {
                var next = args.Key == Key.Home ? 0 : args.Key == Key.End ? _matches.Count - 1
                    : focused < 0 ? (args.Key == Key.Down ? 0 : _matches.Count - 1)
                    : (focused + (args.Key == Key.Down ? 1 : -1) + _matches.Count) % _matches.Count;
                if (combo.ItemContainerGenerator.ContainerFromIndex(_matches[next]) is ComboBoxItem item)
                { item.BringIntoView(); item.Focus(); }
            }
        }
        public void Detach()
        {
            combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            Closed(combo, EventArgs.Empty);
            combo.Loaded -= Loaded; combo.Unloaded -= Unloaded;
            combo.DropDownOpened -= Opened; combo.DropDownClosed -= Closed;
            combo.ItemContainerGenerator.StatusChanged -= Generated;
            foreach (var (item, style) in _directStyles) item.SetCurrentValue(FrameworkElement.StyleProperty, style);
            _directStyles.Clear();
            if (!_configured) return;
            combo.SetCurrentValue(Control.TemplateProperty, _originalTemplate);
            combo.SetCurrentValue(ItemsControl.ItemContainerStyleProperty, _originalItemStyle);
            combo.SetCurrentValue(ItemsControl.ItemsPanelProperty, _originalPanel);
            combo.SetCurrentValue(ComboBox.IsTextSearchEnabledProperty, _originalTextSearch);
        }
    }
}
