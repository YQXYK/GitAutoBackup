using System.Windows;
using System.Windows.Data;

namespace GitAutoBackup.ViewModels;

/// <summary>bool -> Visibility 反向转换器（false 显示，true 折叠）。</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
