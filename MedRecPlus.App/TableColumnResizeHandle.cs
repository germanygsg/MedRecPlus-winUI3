using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MedRecPlus.App;

/// <summary>A native drag handle that also exposes column sizing to keyboard and automation users.</summary>
internal sealed class TableColumnResizeHandle : Control
{
    internal const double MaximumWidth = 4096;
    private readonly Func<double> _readWidth;
    private readonly Action<double> _setWidth;
    private Thumb? _thumb;

    internal event EventHandler<DragStartedEventArgs>? DragStarted;
    internal event EventHandler<DragDeltaEventArgs>? DragDelta;
    internal event EventHandler<DragCompletedEventArgs>? DragCompleted;

    internal double MinimumWidth { get; }
    internal double ColumnWidth => _readWidth();

    internal TableColumnResizeHandle(string label, double minimumWidth, Func<double> readWidth,
        Action<double> setWidth, Action resetWidth)
    {
        MinimumWidth = minimumWidth;
        _readWidth = readWidth;
        _setWidth = setWidth;
        Style = (Style)Application.Current.Resources["TableColumnResizeHandleStyle"];
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        IsTabStop = true;
        AutomationProperties.SetName(this, $"Resize {label} column");
        ToolTipService.SetToolTip(this, $"Drag to resize {label}. Use Left/Right arrows to adjust. Double-click to reset.");
        DoubleTapped += (_, args) => { resetWidth(); args.Handled = true; };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_thumb != null)
        {
            _thumb.DragStarted -= NativeDragStarted;
            _thumb.DragDelta -= NativeDragDelta;
            _thumb.DragCompleted -= NativeDragCompleted;
        }
        _thumb = (Thumb)GetTemplateChild("DragThumb");
        _thumb.DragStarted += NativeDragStarted;
        _thumb.DragDelta += NativeDragDelta;
        _thumb.DragCompleted += NativeDragCompleted;
    }

    private void NativeDragStarted(object sender, DragStartedEventArgs args) => DragStarted?.Invoke(this, args);
    private void NativeDragDelta(object sender, DragDeltaEventArgs args) => DragDelta?.Invoke(this, args);
    private void NativeDragCompleted(object sender, DragCompletedEventArgs args) => DragCompleted?.Invoke(this, args);

    internal void SetColumnWidth(double value)
    {
        if (!double.IsFinite(value) || value < MinimumWidth || value > MaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(value));
        _setWidth(value);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Left or VirtualKey.Right)
        {
            SetColumnWidth(Math.Clamp(ColumnWidth + (args.Key == VirtualKey.Right ? 8 : -8), MinimumWidth, MaximumWidth));
            args.Handled = true;
        }
        else base.OnKeyDown(args);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ResizeHandleAutomationPeer(this);

    private sealed class ResizeHandleAutomationPeer(TableColumnResizeHandle owner)
        : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override string GetClassNameCore() => nameof(TableColumnResizeHandle);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);

        public bool IsReadOnly => false;
        public double LargeChange => 32;
        public double SmallChange => 8;
        public double Maximum => MaximumWidth;
        public double Minimum => owner.MinimumWidth;
        public double Value => owner.ColumnWidth;
        public void SetValue(double value) => owner.SetColumnWidth(value);
    }
}
