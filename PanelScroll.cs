using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RadiationShieldingDesign;

/// <summary>
/// Makes the mouse wheel scroll a side panel no matter what is under the pointer.
///
/// WPF ScrollViewers mark every wheel event as handled, even when they have nothing to scroll.
/// The panels are full of controls that contain their own ScrollViewer (text boxes, the barrier list,
/// trace grid), so the wheel was swallowed whenever the pointer was over one of them.
/// This handler runs first (tunneling): an inner list/grid that can still scroll in the wheel's
/// direction keeps the wheel; otherwise the panel scrolls. Closed combo boxes no longer change their
/// value when the wheel passes over them, which prevents accidental material/occupancy changes.
///
/// Usage: &lt;ScrollViewer local:PanelScroll.Enable="True"&gt;
/// </summary>
public static class PanelScroll
{
    /// <summary>Pixels scrolled per wheel notch (Windows default is ~48; panels are long, so a bit faster).</summary>
    private const double PixelsPerNotch = 72;

    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(PanelScroll),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        sv.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue) sv.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer panel) return;

        // Ctrl+wheel and Shift+wheel are left alone for other uses (zoom, horizontal scroll).
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        bool up = e.Delta > 0;
        for (var d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, panel); d = Parent(d))
        {
            if (d is ScrollViewer inner && inner.ScrollableHeight > 0.5)
            {
                bool canScroll = up ? inner.VerticalOffset > 0.5
                                    : inner.VerticalOffset < inner.ScrollableHeight - 0.5;
                if (canScroll) return;   // let the inner list/grid scroll itself
            }
        }

        double notches = e.Delta / 120.0;
        panel.ScrollToVerticalOffset(panel.VerticalOffset - notches * PixelsPerNotch);
        e.Handled = true;
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual || d is System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    /// <summary>Scrolls <paramref name="panel"/> so <paramref name="target"/> sits near the top.</summary>
    public static void ScrollToTop(ScrollViewer panel, FrameworkElement target, double margin = 8)
    {
        if (!target.IsVisible || !panel.IsAncestorOf(target)) return;
        double y = target.TransformToAncestor(panel).Transform(new Point(0, 0)).Y;
        panel.ScrollToVerticalOffset(Math.Max(0, panel.VerticalOffset + y - margin));
    }
}
