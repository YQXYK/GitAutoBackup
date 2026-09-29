using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GitAutoBackup.Services;

namespace GitAutoBackup.Views;

/// <summary>
/// 备份前体积检查的确认对话框：列出超过 GitHub 单文件 100MB 上限的文件，
/// 由用户决定「跳过并继续」还是「取消备份」。
/// </summary>
public partial class LargeFileDialog : Window
{
    /// <summary>true=跳过这些文件继续备份；false=取消本次备份。</summary>
    public bool SkipThem { get; private set; }

    /// <summary>用户是否勾选了「本批次其余项目也这样处理」。</summary>
    public bool ApplyToRest { get; private set; }

    public LargeFileDialog(string projectName, SizeScanResult scan)
    {
        InitializeComponent();

        var count = scan.BlockingFiles.Count;
        HeadlineText.Text = $"项目「{projectName}」中有 {count} 个文件超过 100 MB";

        ExplainText.Text =
            "GitHub 不接受超过 100 MB 的单个文件，直接推送一定失败。\n" +
            "选择「跳过并继续」后，这些文件不会被备份到仓库，但你电脑上的原文件不会有任何改动。";

        // 列表最多展示 30 条，避免对话框过高
        FilesList.ItemsSource = scan.BlockingFiles.Take(30).ToList();

        var total = scan.BlockingFiles.Sum(f => f.Size);
        var totalText = $"合计 {LargeFile.FormatSize(total)}";
        if (scan.BlockingFiles.Count > 30)
            totalText += $"（列表仅显示前 30 个，共 {scan.BlockingFiles.Count} 个）";
        TotalText.Text = totalText;

        if (scan.WarningFiles.Count > 0)
        {
            WarningText.Text = $"另有 {scan.WarningFiles.Count} 个文件在 50~100 MB 之间，" +
                               "它们可以正常推送，但会明显撑大仓库（GitHub 建议仓库保持在 1 GB 以内）。";
            WarningText.Visibility = Visibility.Visible;
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SkipThem = true;
        ApplyToRest = ApplyAllCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SkipThem = false;
        DialogResult = false;
    }
}
