using System.Windows.Controls;
using System.Windows.Navigation;

namespace GitAutoBackup.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    /// <summary>点击超链接时用系统默认浏览器打开。</summary>
    private void OnHyperlinkRequest(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}