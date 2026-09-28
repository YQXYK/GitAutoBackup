namespace GitAutoBackup.ViewModels;

using System.Windows;
using System.Windows.Data;

/// <summary>bool -> Visibility 转换器（true 显示，false 折叠）。</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is Visibility.Visible;
}