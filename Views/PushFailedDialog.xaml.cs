using System.Windows;
using GitAutoBackup.Services;

namespace GitAutoBackup.Views;

/// <summary>
/// 备份失败说明对话框：用通俗语言说明「发生了什么 / 为什么 / 怎么办」，
/// 原始技术错误折叠在下方，需要时可以复制给开发者。
/// </summary>
public partial class PushFailedDialog : Window
{
    /// <summary>用户是否选择了重试（仅当错误可重试时才有意义）。</summary>
    public bool RetryRequested { get; private set; }

    public PushFailedDialog(string projectName, FriendlyError error)
    {
        InitializeComponent();

        Title = error.Title;
        TitleText.Text = string.IsNullOrWhiteSpace(projectName)
            ? error.Title
            : $"{error.Title}（{projectName}）";
        ExplainText.Text = error.Explanation;
        SuggestionText.Text = error.Suggestion;

        RawText.Text = string.IsNullOrWhiteSpace(error.Raw)
            ? "（GitHub 未返回详细信息）"
            : error.Raw;

        // 重试无意义的错误（文件过大 / 认证失败 / 仓库不存在等）不提供「重试」按钮，
        // 避免用户反复点重试却总是失败。
        RetryButton.Visibility = error.Retryable ? Visibility.Visible : Visibility.Collapsed;
        if (!error.Retryable) RetryButton.IsDefault = false;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        RetryRequested = true;
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
