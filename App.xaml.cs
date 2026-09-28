using System.IO;
using System.Windows;

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
            MessageBox.Show($"程序遇到错误：\n{msg}\n\n错误已记录到 error.log", "GitAutoBackup 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ea.Handled = true;
        };

        var vm = new ViewModels.MainViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
    }

    private static void LogException(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitAutoBackup");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}