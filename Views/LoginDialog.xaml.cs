using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GitAutoBackup.Services;

namespace GitAutoBackup.Views;

/// <summary>OAuth 设备码登录对话框：显示设备码 → 浏览器授权 → 后台轮询换取 token。</summary>
public partial class LoginDialog : Window
{
    private readonly DeviceCodeInfo _info;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>授权成功后拿到的 access_token；取消或失败时为 null。</summary>
    public string? Token { get; private set; }

    public LoginDialog(DeviceCodeInfo info)
    {
        InitializeComponent();
        _info = info;
        CodeText.Text = info.UserCode;

        Loaded += (_, _) =>
        {
            OpenBrowser();
            StartPolling();
        };
        Closed += (_, _) => _cts.Cancel();
    }

    private void OpenBrowser()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_info.VerificationUri) { UseShellExecute = true });
        }
        catch { /* 打不开就让用户手动访问 */ }
    }

    private void StartPolling()
    {
        Task.Run(() =>
        {
            var token = GitHubAuthService.PollForToken(_info, _cts.Token, out var error);
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(token))
                {
                    Token = token;
                    DialogResult = true;
                }
                else
                {
                    StatusText.Text = string.IsNullOrEmpty(error) ? "登录未完成。" : error;
                }
            });
        });
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_info.UserCode); StatusText.Text = "设备码已复制到剪贴板。"; }
        catch { StatusText.Text = "复制失败，请手动记录设备码。"; }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenBrowser();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
    }
}
