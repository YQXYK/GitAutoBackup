using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace GitAutoBackup.Models;

/// <summary>备份方式</summary>
public enum BackupMode
{
    /// <summary>集中式：复制到统一备份目录，推送到单个私有仓库</summary>
    Consolidated = 0,

    /// <summary>独立式：每个项目各自推送到独立私有仓库</summary>
    Standalone = 1
}

/// <summary>
/// 一条备份任务记录。实现 INotifyPropertyChanged，使「状态 / 最近备份」等列在备份过程中实时刷新。
/// 属性名与序列化结构保持不变，settings.json 向下兼容。
/// </summary>
public class BackupJob : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _sourcePath = string.Empty;
    private BackupMode _mode = BackupMode.Consolidated;
    private string _repoName = string.Empty;
    private string _consolidatedRepoName = string.Empty;
    private bool _isPrivate = true;
    private string _lastStatus = "未备份";
    private DateTime _lastBackupAt;

    /// <summary>是否被勾选参与备份（界面勾选框）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>源项目文件夹完整路径</summary>
    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetField(ref _sourcePath, value)) OnPropertyChanged(nameof(ProjectName));
        }
    }

    /// <summary>备份方式</summary>
    public BackupMode Mode
    {
        get => _mode;
        set
        {
            if (SetField(ref _mode, value)) OnPropertyChanged(nameof(ModeText));
        }
    }

    /// <summary>备份方式的中文文本（只读显示用，不允许下拉修改）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ModeText => Mode == BackupMode.Standalone ? "独立式" : "集中式";

    /// <summary>独立式时的私有仓库名（不含 owner）</summary>
    public string RepoName
    {
        get => _repoName;
        set => SetField(ref _repoName, value);
    }

    /// <summary>集中式时所属的仓库名（多仓库支持）</summary>
    public string ConsolidatedRepoName
    {
        get => _consolidatedRepoName;
        set => SetField(ref _consolidatedRepoName, value);
    }

    /// <summary>创建/使用该备份仓库时是否私有（独立式用）</summary>
    public bool IsPrivate
    {
        get => _isPrivate;
        set => SetField(ref _isPrivate, value);
    }

    /// <summary>最近一次备份状态文本</summary>
    public string LastStatus
    {
        get => _lastStatus;
        set => SetField(ref _lastStatus, value);
    }

    /// <summary>最近一次备份时间</summary>
    public DateTime LastBackupAt
    {
        get => _lastBackupAt;
        set => SetField(ref _lastBackupAt, value);
    }

    /// <summary>该项目特有的额外排除规则（可空；为空时仅使用全局排除规则）</summary>
    public List<ExcludeRule> JobExcludes { get; set; } = new();

    /// <summary>项目名（从源路径取最后一段）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ProjectName =>
        string.IsNullOrEmpty(SourcePath)
            ? "(无路径)"
            : Path.GetFileName(SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler == null) return;

        // 备份流程运行在后台线程，属性通知必须切回 UI 线程，否则界面绑定不会刷新
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => handler(this, new PropertyChangedEventArgs(name)));
        else
            handler(this, new PropertyChangedEventArgs(name));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
