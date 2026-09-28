using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitAutoBackup.Models;
using Microsoft.Win32;

namespace GitAutoBackup.ViewModels;

/// <summary>「集中仓库」页：管理集中仓库、新增集中式任务、展示现有集中式任务。</summary>
public class ConsolidatedViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private readonly ObservableCollection<BackupJob> _jobs = new();

    public ICommand AddRepoCommand { get; }
    public ICommand AddProjectCommand { get; }
    public ICommand DeleteRepoCommand { get; }

    /// <summary>备份根目录（复用主页 VM 属性，保证同步）。</summary>
    public string BackupRoot
    {
        get => _main.BackupRoot;
        set => _main.BackupRoot = value;
    }
    public ICommand PickBackupRootCommand => _main.PickBackupRootCommand;

    /// <summary>集中仓库名列表（供下拉选择与展示）。</summary>
    public ObservableCollection<string> Repos => _main.ConsolidatedRepos;

    private string _selectedRepo = string.Empty;
    public string SelectedRepo
    {
        get => _selectedRepo;
        set { _selectedRepo = value ?? string.Empty; OnPropertyChanged(); }
    }

    private string _newRepoName = string.Empty;
    public string NewRepoName
    {
        get => _newRepoName;
        set { _newRepoName = value ?? string.Empty; OnPropertyChanged(); }
    }

    private bool _newRepoIsPrivate = true;
    /// <summary>新增集中仓库时的可见性（true=私有）。</summary>
    public bool NewRepoIsPrivate
    {
        get => _newRepoIsPrivate;
        set { _newRepoIsPrivate = value; OnPropertyChanged(); }
    }

    /// <summary>可见性选项（供下拉）。</summary>
    public List<KeyValuePair<bool, string>> VisibilityOptions { get; } = new()
    {
        new(true, "私有"),
        new(false, "公开"),
    };

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }

    /// <summary>仅集中式的任务（过滤副本，保证增删/改方式时实时刷新）。</summary>
    public ObservableCollection<BackupJob> Jobs => _jobs;

    public ConsolidatedViewModel(MainViewModel main)
    {
        _main = main;
        Rebuild();

        main.Jobs.CollectionChanged += OnJobsChanged;
        foreach (var j in main.Jobs) j.PropertyChanged += OnJobPropertyChanged;

        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.BackupRoot)) OnPropertyChanged(nameof(BackupRoot));
        };

        AddRepoCommand = new RelayCommand(_ => AddRepo());
        AddProjectCommand = new RelayCommand(_ => AddProject());
        DeleteRepoCommand = new RelayCommand(p => DeleteRepo(p as string));
    }

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (BackupJob j in e.NewItems) j.PropertyChanged += OnJobPropertyChanged;
        if (e.OldItems != null)
            foreach (BackupJob j in e.OldItems) j.PropertyChanged -= OnJobPropertyChanged;
        Rebuild();
    }

    private void OnJobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupJob.Mode) || e.PropertyName == nameof(BackupJob.ConsolidatedRepoName))
            Rebuild();
    }

    private void Rebuild()
    {
        _jobs.Clear();
        foreach (var j in _main.Jobs)
            if (j.Mode == BackupMode.Consolidated)
                _jobs.Add(j);
    }

    private void AddRepo()
    {
        var added = _main.AddConsolidatedRepo(NewRepoName, NewRepoIsPrivate);
        if (added != null)
        {
            Message = $"已新增集中仓库：{added}";
            NewRepoName = string.Empty;
            SelectedRepo = added;
        }
        else
        {
            var raw = NewRepoName?.Trim() ?? string.Empty;
            Message = string.IsNullOrWhiteSpace(raw) ? "请输入仓库名。" : $"仓库「{raw}」已存在或名称无效。";
        }
    }

    private void AddProject()
    {
        var repo = SelectedRepo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repo))
        {
            Message = "请先选择（或新增）一个集中仓库。";
            return;
        }
        var dlg = new OpenFolderDialog { Title = "选择要加入该集中仓库的项目文件夹", Multiselect = true };
        if (dlg.ShowDialog() != true) return;

        int added = 0;
        foreach (var f in dlg.FolderNames)
            if (_main.TryAddJob(f, BackupMode.Consolidated, consolidatedRepoName: repo)) added++;

        if (added > 0) _main.RefreshConsolidatedReadme(repo);

        Message = added > 0
            ? $"已添加 {added} 个项目到「{repo}」。"
            : "所选文件夹均已存在，未重复添加。";
    }

    private void DeleteRepo(string? repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName)) return;
        _main.DeleteConsolidatedRepo(repoName);
        if (string.Equals(SelectedRepo, repoName, StringComparison.OrdinalIgnoreCase))
            SelectedRepo = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
