using System.IO;
using System.Windows;
using GitAutoBackup.Services;

namespace GitAutoBackup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ea) =>
        {
            LogException(ea.Exception);
            var msg = string.IsNullOrWhiteSpace(ea.Exception?.Message) ? "未知错误" : ea.Exception.Message;
            MessageBox.Show($"程序遇到错误：\n{msg}\n\n错误已记录到日志目录。", "GitAutoBackup 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ea.Handled = true;
        };

        var vm = new ViewModels.MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
    }

    /// <summary>记录未处理异常：写入统一日志目录，并额外保留 errors.log 便于快速定位。</summary>
    private static void LogException(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            PathService.EnsureDataDir();
            var text = ex.ToString();
            LogService.Write("未处理异常：" + text, LogLevel.Error);
            File.AppendAllText(Path.Combine(PathService.DataDir, "errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\r\n\r\n");
        }
        catch { }
    }
}