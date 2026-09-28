using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitAutoBackup.Models;

/// <summary>GitHub 上的一个仓库（用于「GitHub 仓库」页展示）。</summary>
public class GitHubRepo : INotifyPropertyChanged
{
    /// <summary>owner/name</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>仓库名（不含 owner）</summary>
    public string Name { get; set; } = string.Empty;

    private bool _isPrivate;
    /// <summary>是否私有</summary>
    public bool IsPrivate
    {
        get => _isPrivate;
        set
        {
            if (_isPrivate == value) return;
            _isPrivate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibilityText));
        }
    }

    private string _description = string.Empty;
    /// <summary>描述</summary>
    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    /// <summary>主要语言</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>占用大小（KB）</summary>
    public long DiskUsageKb { get; set; }

    /// <summary>最近更新时间</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>可见性文本</summary>
    public string VisibilityText => IsPrivate ? "私有" : "公开";

    /// <summary>大小文本（KB / MB）</summary>
    public string SizeText => DiskUsageKb < 1024
        ? $"{DiskUsageKb} KB"
        : $"{DiskUsageKb / 1024.0:0.#} MB";

    /// <summary>语言文本（无语言时显示 -）</summary>
    public string LanguageText => string.IsNullOrWhiteSpace(Language) ? "-" : Language;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
