using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RadiationShieldingDesign;

public static class DragReorderBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable", typeof(bool), typeof(DragReorderBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

    private static Point _dragStart;
    private static object? _dragItem;

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;

        if ((bool)e.NewValue)
        {
            ic.PreviewMouseLeftButtonDown += OnMouseDown;
            ic.MouseMove += OnMouseMove;
            ic.Drop += OnDrop;
            ic.AllowDrop = true;
        }
        else
        {
            ic.PreviewMouseLeftButtonDown -= OnMouseDown;
            ic.MouseMove -= OnMouseMove;
            ic.Drop -= OnDrop;
            ic.AllowDrop = false;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = GetItemUnderMouse(sender as ItemsControl, e.OriginalSource as DependencyObject);
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ItemsControl ic) return;
        if (_dragItem is null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        DragDrop.DoDragDrop(ic, _dragItem, DragDropEffects.Move);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl ic) return;

        var source = _dragItem;
        var target = GetItemUnderMouse(ic, e.OriginalSource as DependencyObject);
        _dragItem = null;

        if (source is null || target is null || ReferenceEquals(source, target)) return;

        // Use non-generic IList so ObservableCollection<BarrierItemViewModel> works
        var list = ic.ItemsSource as IList ?? ic.Items;
        if (list is null) return;

        int oldIndex = list.IndexOf(source);
        int newIndex = list.IndexOf(target);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return;

        // Prefer Move if it exists (keeps CollectionChanged action == Move)
        var moveMethod = list.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
        if (moveMethod != null)
        {
            moveMethod.Invoke(list, new object[] { oldIndex, newIndex });
        }
        else
        {
            // Fallback: remove/insert
            var item = list[oldIndex];
            list.RemoveAt(oldIndex);
            list.Insert(newIndex, item);
        }
    }

    private static object? GetItemUnderMouse(ItemsControl? ic, DependencyObject? origin)
    {
        if (ic is null || origin is null) return null;

        // Works for ListBox, DataGrid, etc.
        var container = ic.ContainerFromElement(origin) as FrameworkElement;
        return container is null ? null : ic.ItemContainerGenerator.ItemFromContainer(container);
    }
}
