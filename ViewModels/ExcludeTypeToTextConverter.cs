namespace GitAutoBackup.ViewModels;

using System.Windows.Data;
using GitAutoBackup.Models;

/// <summary>ExcludeType -> 中文显示（文件夹/文件）。</summary>
public class ExcludeTypeToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is ExcludeType.File ? "文件" : "文件夹";

    public object ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}