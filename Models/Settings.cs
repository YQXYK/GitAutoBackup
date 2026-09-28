namespace GitAutoBackup.Models;

/// <summary>全局配置</summary>
public class Settings
{
    /// <summary>备份任务列表</summary>
    public List<BackupJob> Jobs { get; set; } = new();

    /// <summary>集中式的统一私有仓库名（旧版单仓库；现作默认仓库名，兼容旧数据）</summary>
    public string ConsolidatedRepoName { get; set; } = "MyProjectBackup";

    /// <summary>集中式仓库名列表（多仓库：每个名字对应一个集中仓库）</summary>
    public List<string> ConsolidatedRepos { get; set; } = new();

    /// <summary>集中仓库可见性：仓库名 → 是否私有（缺省视为私有）</summary>
    public Dictionary<string, bool> ConsolidatedRepoPrivates { get; set; } = new();

    /// <summary>集中式的备份根目录（源码会复制到这里）</summary>
    public string BackupRoot { get; set; } = string.Empty;

    /// <summary>定时间隔数值</summary>
    public double IntervalValue { get; set; } = 60;

    /// <summary>定时间隔单位：分钟 / 小时 / 天</summary>
    public string IntervalUnit { get; set; } = "分钟";

    /// <summary>是否启用定时</summary>
    public bool SchedulerEnabled { get; set; }

    /// <summary>最近一次自动执行时间（避免重复）</summary>
    public DateTime LastRunAt { get; set; }

    /// <summary>GitHub 账号名</summary>
    public string GitHubAccount { get; set; } = string.Empty;

    /// <summary>全局排除规则（按名称精确匹配，对所有项目生效）</summary>
    public List<ExcludeRule> GlobalExcludes { get; set; } = new();
}