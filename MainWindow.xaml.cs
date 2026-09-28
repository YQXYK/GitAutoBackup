using System.Windows;
using System.Windows.Media;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;

namespace GitAutoBackup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyLightTerminalTheme();
        Closing += (_, _) => (DataContext as ViewModels.MainViewModel)?.Dispose();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.TerminalCommandRequested += RunInTerminal;
        };
    }

    /// <summary>给终端设置浅色主题，匹配软件整体的黑白 Apple 风（白底深色文字）。</summary>
    private void ApplyLightTerminalTheme()
    {
        static uint C(byte r, byte g, byte b) => EasyTerminalControl.ColorToVal(Color.FromRgb(r, g, b));

        TerminalControl.Theme = new TerminalTheme
        {
            DefaultBackground = C(0xFF, 0xFF, 0xFF),
            DefaultForeground = C(0x1D, 0x1D, 0x1F),
            DefaultSelectionBackground = C(0xE5, 0xE5, 0xEA),
            CursorStyle = CursorStyle.BlinkingBar,
            ColorTable = new uint[]
            {
                C(0x0C,0x0C,0x0C), C(0xC5,0x0F,0x1F), C(0x13,0xA1,0x0E), C(0xC1,0x9C,0x00),
                C(0x00,0x37,0xDA), C(0x88,0x17,0x98), C(0x3A,0x96,0xDD), C(0xCC,0xCC,0xCC),
                C(0x76,0x76,0x76), C(0xE7,0x48,0x56), C(0x16,0xC6,0x0C), C(0xF9,0xF1,0xA5),
                C(0x3B,0x78,0xFF), C(0xB4,0x00,0x9E), C(0x61,0xD6,0xD6), C(0xF2,0xF2,0xF2)
            }
        };
    }

    /// <summary>
    /// 在内置终端里执行命令。对 gh 设备流这类需要交互的命令：
    /// 先写入命令并回车执行，gh 会打印一次性代码并停在 "Press Enter to open ... in your browser..."，
    /// 稍后自动补一个回车触发它打开浏览器（用户无需手动按键）。
    /// </summary>
    private void RunInTerminal(string cmd)
    {
        var term = TerminalControl.ConPTYTerm;
        if (term == null) return;

        term.WriteToTerm(cmd + "\r");

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            term.WriteToTerm("\r");
        };
        timer.Start();
    }
}
