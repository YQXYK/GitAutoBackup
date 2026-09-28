using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitAutoBackup.Models;
using GitAutoBackup.Services;

namespace GitAutoBackup.ViewModels;

/// <summary>「排除规则」页：管理全局规则与每个项目的规则。</summary>
public class ExcludesViewModel : INotifyPropertyChanged
{
    private readonly Settings _settings;

    /// <summary>类型选项（中文显示，供新增时选择）</summary>
    public List<KeyValuePair<ExcludeType, string>> TypeOptions { get; } = new()
    {
        new(ExcludeType.Folder, "文件夹"),
        new(ExcludeType.File, "文件"),
    };

    /// <summary>内置排除规则（只读展示，不能删除）</summary>
    public IEnumerable<string> BuiltinItems => GitAutoBackup.Services.BackupService.ExcludedDirNames;

    /// <summary>内置规则的展示文本</summary>
    public string BuiltinItemsText => ".git / node_modules / bin / obj / Debug / Release / .vs / .idea / $RECYCLE.BIN";

    // 全局规则集合（直接绑定 settings.GlobalExcludes 的副本，增删后同步保存）
    public ObservableCollection<ExcludeRuleItem> GlobalItems { get; } = new();

    // 项目特有规则集合
    public ObservableCollection<ExcludeRuleItem> ProjectItems { get; } = new();

    /// <summary>项目下拉选项（用于选择"按项目"编辑哪个项目）</summary>
    public ObservableCollection<BackupJob> JobOptions { get; } = new();

    private BackupJob? _selectedJob;
    public BackupJob? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetField(ref _selectedJob, value))
                ReloadProjectItems();
        }
    }

    private bool _isGlobal = true;
    /// <summary>true=编辑全局规则；false=编辑选中项目的规则。</summary>
    public bool IsGlobal
    {
        get => _isGlobal;
        set
        {
            if (SetField(ref _isGlobal, value))
            {
                OnPropertyChanged(nameof(IsProject));
                OnPropertyChanged(nameof(ShowGlobal));
                OnPropertyChanged(nameof(ShowProject));
            }
        }
    }
    public bool IsProject
    {
        get => !_isGlobal;
        set
        {
            // 反向设置：IsProject=true 即 IsGlobal=false（RadioButton 双向写回只读属性会抛异常，故给 setter）
            var newGlobal = !value;
            if (SetField(ref _isGlobal, newGlobal))
            {
                OnPropertyChanged(nameof(IsGlobal));
                OnPropertyChanged(nameof(ShowGlobal));
                OnPropertyChanged(nameof(ShowProject));
            }
        }
    }

    /// <summary>全局规则面板可见性</summary>
    public bool ShowGlobal => _isGlobal;

    /// <summary>按项目规则面板可见性</summary>
    public bool ShowProject => !_isGlobal;

    public string NewItemName { get; set; } = string.Empty;
    public ExcludeType NewItemType { get; set; } = ExcludeType.Folder;

    public ICommand AddGlobalCommand { get; }
    public ICommand AddProjectCommand { get; }
    public ICommand RemoveGlobalCommand { get; }
    public ICommand RemoveProjectCommand { get; }

    public ExcludesViewModel(Settings settings)
    {
        _settings = settings;
        AddGlobalCommand = new RelayCommand(_ => AddGlobal());
        AddProjectCommand = new RelayCommand(_ => AddProject());
        RemoveGlobalCommand = new RelayCommand(p => RemoveGlobal((p as ExcludeRule) ?? (p as ExcludeRuleItem)?.Rule));
        RemoveProjectCommand = new RelayCommand(p => RemoveProject((p as ExcludeRule) ?? (p as ExcludeRuleItem)?.Rule));

        foreach (var r in (_settings.GlobalExcludes ?? new List<ExcludeRule>()))
            GlobalItems.Add(new ExcludeRuleItem(r));
        foreach (var j in _settings.Jobs) JobOptions.Add(j);
        _isGlobal = true;
    }

    // 新增全局规则（读输入框 NewItemName / NewItemType）
    private void AddGlobal()
    {
        var name = NewItemName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        _settings.GlobalExcludes ??= new List<ExcludeRule>();
        var rule = new ExcludeRule { Name = name, Type = NewItemType };
        _settings.GlobalExcludes.Add(rule);
        GlobalItems.Add(new ExcludeRuleItem(rule));
        NewItemName = string.Empty;
        OnPropertyChanged(nameof(NewItemName));
        Save();
    }

    private void AddProject()
    {
        if (SelectedJob == null) return;
        var name = NewItemName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        SelectedJob.JobExcludes ??= new List<ExcludeRule>();
        var rule = new ExcludeRule { Name = name, Type = NewItemType };
        SelectedJob.JobExcludes.Add(rule);
        ReloadProjectItems();
        Save();
    }

    private void RemoveGlobal(ExcludeRule? rule)
    {
        if (rule == null) return;
        _settings.GlobalExcludes?.Remove(rule);
        var item = GlobalItems.FirstOrDefault(x => ReferenceEquals(x.Rule, rule));
        if (item != null) GlobalItems.Remove(item);
        Save();
    }

    private void RemoveProject(ExcludeRule? rule)
    {
        if (rule == null || SelectedJob == null) return;
        SelectedJob.JobExcludes?.Remove(rule);
        ReloadProjectItems();
        Save();
    }

    private void ReloadProjectItems()
    {
        ProjectItems.Clear();
        if (SelectedJob == null) { OnPropertyChanged(nameof(HasProject)); return; }
        foreach (var r in (SelectedJob.JobExcludes ?? new List<ExcludeRule>()))
            ProjectItems.Add(new ExcludeRuleItem(r));
        OnPropertyChanged(nameof(ProjectHint));
    }

    public bool HasProject => SelectedJob != null;
    public string ProjectHint =>
        SelectedJob == null ? "请先在「备份任务」页添加项目，再为项目配置额外排除规则。" :
        $"正在编辑：{SelectedJob.ProjectName} 的排除规则（叠加全局规则）";

    private void Save() => SettingsService.Save(_settings);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; OnPropertyChanged(name); return true; }
        return false;
    }
}

/// <summary>排除规则在界面的展示项（包裹 ExcludeRule）。</summary>
public class ExcludeRuleItem
{
    public ExcludeRule Rule { get; }

    public string Name { get => Rule.Name; set { Rule.Name = value; } }
    public ExcludeType Type { get => Rule.Type; set { Rule.Type = value; } }

    public ExcludeRuleItem(ExcludeRule rule) { Rule = rule; }
}