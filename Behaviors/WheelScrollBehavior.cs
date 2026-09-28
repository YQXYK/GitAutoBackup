using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GitAutoBackup.Behaviors;

/// <summary>
/// 附加行为：让内部滚动控件（DataGrid / ListBox 等）在**自身无法继续滚动**时，
/// 把鼠标滚轮事件交给外层（页面级）ScrollViewer。
/// 解决"鼠标悬停在表格上就滚不动页面"的问题。
/// 用法：<c>behaviors:WheelScrollBehavior.ForwardToParent="True"</c>
/// </summary>
public static class WheelScrollBehavior
{
    public static readonly DependencyProperty ForwardToParentProperty =
        DependencyProperty.RegisterAttached(
            "ForwardToParent",
            typeof(bool),
            typeof(WheelScrollBehavior),
            new PropertyMetadata(false, OnForwardToParentChanged));

    public static void SetForwardToParent(DependencyObject element, bool value)
        => element.SetValue(ForwardToParentProperty, value);

    public static bool GetForwardToParent(DependencyObject element)
        => (bool)element.GetValue(ForwardToParentProperty);

    private static void OnForwardToParentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if (e.NewValue is true) element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not DependencyObject d) return;

        // 内部还有可滚动距离 → 交给它自己处理
        var inner = FindDescendant<ScrollViewer>(d);
        if (inner != null)
        {
            var canScrollUp = e.Delta > 0 && inner.VerticalOffset > 0.5;
            var canScrollDown = e.Delta < 0 && inner.VerticalOffset < inner.ScrollableHeight - 0.5;
            if (canScrollUp || canScrollDown) return;
        }

        // 内部滚不动了 → 转发给外层页面 ScrollViewer
        var outer = FindAncestor<ScrollViewer>(d);
        if (outer == null) return;

        e.Handled = true;
        outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
