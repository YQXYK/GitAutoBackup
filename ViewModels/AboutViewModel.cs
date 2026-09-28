namespace GitAutoBackup.ViewModels;

/// <summary>「关于」页：静态信息。</summary>
public class AboutViewModel
{
    public string AppName => "GitAutoBackup";
    public string Version => "1.1.0";
    public string Description =>
        "一个图形化 Git 备份工具：选择项目文件夹，自动备份到 GitHub 私有仓库。\n\n" +
        "支持集中式（一个仓库存所有项目）与独立式（每项目一个仓库）；\n" +
        "支持自定义排除规则（全局 / 每项目）；\n" +
        "支持定时自动备份与失败重试。";
    public string Tech => "C# / WPF / .NET 10，内置调用 git 与 gh CLI。";

    /// <summary>制作者 GitHub 主页</summary>
    public string AuthorUrl => "https://github.com/YQXYK";

    /// <summary>声明</summary>
    public string Declaration =>
        "本软件为个人开发的免费工具，按「现状」提供，不附带任何明示或暗示的担保。\n\n" +
        "备份数据的安全性请自行确认；作者不对因使用本软件造成的任何数据丢失、损坏或其他损失承担责任。\n\n" +
        "软件通过本机已安装的 git 与 gh CLI 完成备份与仓库操作，请确保已登录正确的 GitHub 账号。";
}