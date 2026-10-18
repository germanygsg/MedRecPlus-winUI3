using System.Text.Json;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MedRecPlus.App;

/// <summary>Shared column visibility and widths for a table's header and recycled rows.</summary>
internal sealed class TableColumnPicker
{
    internal sealed record Column(int Index, string Key, string Label, bool CanHide = true, double MinimumWidth = 80);

    private readonly SettingsService _settings;
    private readonly string _settingsKey;
    private readonly string _widthsSettingsKey;
    private readonly Grid _header;
    private readonly Grid _table;
    private readonly ScrollViewer _viewport;
    private readonly double _fixedWidth;
    private readonly (Column Column, GridLength Width)[] _columns;
    private readonly List<WeakReference<Grid>> _grids = [];
    private HashSet<string> _hidden;
    private Dictionary<string, double> _widths;
    private Dictionary<string, double>? _beforeDrag;
    private double _dragWidth;

    public TableColumnPicker(Button button, Grid header, Grid table, ScrollViewer viewport,
        string pageKey, params Column[] columns)
    {
        _settings = AppSession.Settings;
        _settingsKey = "table_columns." + pageKey;
        _widthsSettingsKey = "table_column_widths." + pageKey;
        _header = header;
        _table = table;
        _viewport = viewport;
        _columns = columns.Select(column => (column, header.ColumnDefinitions[column.Index].Width)).ToArray();
        _fixedWidth = header.Padding.Left + header.Padding.Right + header.ColumnDefinitions
            .Where((_, index) => columns.All(column => column.Index != index)).Sum(definition => definition.Width.Value);
        try
        {
            _hidden = new HashSet<string>(_settings.Get<string[]>(_settingsKey) ?? [], StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A damaged or older preference must not prevent the record list from opening.
            _hidden = new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            var savedWidths = _settings.Get<Dictionary<string, double>>(_widthsSettingsKey) ?? [];
            _widths = columns.Where(column => savedWidths.TryGetValue(column.Key, out var width)
                    && double.IsFinite(width) && width >= column.MinimumWidth && width <= TableColumnResizeHandle.MaximumWidth)
                .ToDictionary(column => column.Key, column => savedWidths[column.Key], StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            _widths = new Dictionary<string, double>(StringComparer.Ordinal);
        }

        var options = new StackPanel { Spacing = 0 };
        foreach (var column in columns.Where(column => column.CanHide))
        {
            var checkBox = new CheckBox
            {
                Content = column.Label,
                Tag = column.Key,
                IsChecked = !_hidden.Contains(column.Key),
                MinHeight = 36,
                MinWidth = 180,
            };
            checkBox.Click += Column_Click;
            options.Children.Add(checkBox);
        }
        button.Flyout = new Flyout
        {
            Content = options,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
        };

        foreach (var column in columns)
        {
            var handle = new TableColumnResizeHandle(column.Label, column.MinimumWidth,
                () => header.ColumnDefinitions[column.Index].ActualWidth,
                value => SetWidth(column, value), () => ResetWidth(column))
            {
                Tag = column.Key,
            };
            Grid.SetColumn(handle, column.Index);
            handle.DragStarted += (_, _) =>
            {
                _beforeDrag = new Dictionary<string, double>(_widths, StringComparer.Ordinal);
                _dragWidth = header.ColumnDefinitions[column.Index].ActualWidth;
            };
            handle.DragDelta += (_, args) =>
            {
                if (_beforeDrag == null) return;
                _dragWidth = Math.Clamp(_dragWidth + args.HorizontalChange, column.MinimumWidth, TableColumnResizeHandle.MaximumWidth);
                _widths[column.Key] = _dragWidth;
                ApplyAll();
            };
            handle.DragCompleted += (_, args) =>
            {
                if (_beforeDrag == null) return;
                var previous = _beforeDrag;
                _beforeDrag = null;
                if (args.Canceled) { _widths = previous; ApplyAll(); }
                else SaveWidths(previous);
            };
            header.Children.Add(handle);
            foreach (var label in header.Children.OfType<TextBlock>().Where(label => Grid.GetColumn(label) == column.Index))
            {
                label.TextWrapping = TextWrapping.NoWrap;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.Margin = new Thickness(0, 0, 8, 0);
            }
        }
        viewport.SizeChanged += (_, _) => UpdateTableWidth();
        ApplyTo(header);
        UpdateTableWidth();
    }

    public void ApplyTo(Grid grid)
    {
        _grids.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (!_grids.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, grid)))
            _grids.Add(new WeakReference<Grid>(grid));
        ApplyLayout(grid);
    }

    private void ApplyLayout(Grid grid)
    {
        foreach (var (column, width) in _columns)
        {
            var visible = !column.CanHide || !_hidden.Contains(column.Key);
            grid.ColumnDefinitions[column.Index].MinWidth = visible ? column.MinimumWidth : 0;
            grid.ColumnDefinitions[column.Index].Width = !visible ? new GridLength(0)
                : _widths.TryGetValue(column.Key, out var resized) ? new GridLength(resized) : width;
            foreach (var cell in grid.Children.OfType<FrameworkElement>().Where(cell => Grid.GetColumn(cell) == column.Index))
                cell.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Column_Click(object sender, RoutedEventArgs args)
    {
        var checkBox = (CheckBox)sender;
        var key = (string)checkBox.Tag;
        var hidden = new HashSet<string>(_hidden, StringComparer.Ordinal);
        if (checkBox.IsChecked == true) hidden.Remove(key);
        else hidden.Add(key);
        try
        {
            // Save immediately so leaving the page or closing the app cannot lose a toggle.
            _settings.Set(_settingsKey, hidden.Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception)
        {
            checkBox.IsChecked = !_hidden.Contains(key);
            Dialogs.Alert(checkBox.XamlRoot, "Couldn't save columns", "Your column selection could not be saved. Please try again.");
            return;
        }
        _hidden = hidden;
        ApplyAll();
    }

    private void ApplyAll()
    {
        UpdateTableWidth();
        _grids.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _grids)
            if (reference.TryGetTarget(out var grid)) ApplyLayout(grid);
    }

    private void UpdateTableWidth()
    {
        var minimum = _fixedWidth + _columns.Where(item => !item.Column.CanHide || !_hidden.Contains(item.Column.Key))
            .Sum(item => _widths.TryGetValue(item.Column.Key, out var resized) ? resized
                : item.Width.IsAbsolute ? Math.Max(item.Width.Value, item.Column.MinimumWidth) : item.Column.MinimumWidth);
        _table.MinWidth = minimum;
        _table.Width = Math.Max(minimum, _viewport.ActualWidth);
    }

    private void SetWidth(Column column, double value)
    {
        var previous = new Dictionary<string, double>(_widths, StringComparer.Ordinal);
        _widths[column.Key] = value;
        ApplyAll();
        SaveWidths(previous);
    }

    private void ResetWidth(Column column)
    {
        var previous = new Dictionary<string, double>(_widths, StringComparer.Ordinal);
        _widths.Remove(column.Key);
        ApplyAll();
        SaveWidths(previous);
    }

    private void SaveWidths(Dictionary<string, double> previous)
    {
        try { _settings.Set(_widthsSettingsKey, _widths); }
        catch (Exception)
        {
            _widths = previous;
            ApplyAll();
            Dialogs.Alert(_header.XamlRoot, "Couldn't save column widths", "Your column widths could not be saved. Please try again.");
        }
    }
}
