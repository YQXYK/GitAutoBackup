using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GitAutoBackup.Models;

namespace GitAutoBackup.Views;

public partial class TasksView : UserControl
{
    public TasksView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 单击「备份」勾选框：勾选状态由 CheckBox 自己切换，这里只把该行设为选中行（高亮）。
    /// </summary>
    private void BackupCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BackupJob job })
            TaskGrid.SelectedItem = job;
    }

    /// <summary>
    /// 点击行（勾选框 / 下拉框等交互控件除外）→ 切换该行的「参与备份」勾选状态，并选中该行。
    /// 这样「点行」与「点勾选框」效果一致：都是切换是否参与备份。
    /// </summary>
    private void TaskGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;

        var source = e.OriginalSource as DependencyObject;
        if (IsInteractiveControl(source)) return;

        if (FindAncestor<DataGridRow>(source) is { DataContext: BackupJob job })
        {
            job.IsSelected = !job.IsSelected;
            TaskGrid.SelectedItem = job;
            e.Handled = true;
        }
    }

    /// <summary>点击源是否落在会自行处理鼠标的交互控件内。</summary>
    private static bool IsInteractiveControl(DependencyObject? source)
        => FindAncestor<CheckBox>(source) != null
           || FindAncestor<ComboBox>(source) != null
           || FindAncestor<TextBox>(source) != null
           || FindAncestor<ButtonBase>(source) != null;

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
