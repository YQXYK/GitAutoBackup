using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitAutoBackup.Models;
using Microsoft.Win32;

namespace GitAutoBackup.ViewModels;

/// <summary>「独立仓库」页：新增独立式任务、展示现有独立式任务。</summary>
public class StandaloneViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private readonly ObservableCollection<BackupJob> _jobs = new();

    private string _repoName = string.Empty;
    /// <summary>独立式仓库名（留空则使用项目名作为仓库名）。</summary>
    public string RepoName
    {
        get => _repoName;
        set { _repoName = value ?? string.Empty; OnPropertyChanged(); }
    }

    private bool _isPrivate = true;
    /// <summary>新增独立式项目时的仓库可见性（true=私有）。</summary>
    public bool IsPrivate
    {
        get => _isPrivate;
        set { _isPrivate = value; OnPropertyChanged(); }
    }

    /// <summary>可见性选项（供下拉）。</summary>
    public List<KeyValuePair<bool, string>> VisibilityOptions { get; } = new()
    {
        new(true, "私有"),
        new(false, "公开"),
    };

    public ICommand AddCommand { get; }
    public ICommand DeleteJobCommand { get; }

    /// <summary>仅独立式的任务（过滤副本，保证增删/改方式时实时刷新）。</summary>
    public ObservableCollection<BackupJob> Jobs => _jobs;

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }

    public StandaloneViewModel(MainViewModel main)
    {
        _main = main;
        Rebuild();

        main.Jobs.CollectionChanged += OnJobsChanged;
        foreach (var j in main.Jobs) j.PropertyChanged += OnJobPropertyChanged;

        AddCommand = new RelayCommand(_ => Add());
        DeleteJobCommand = new RelayCommand(p => DeleteJob(p as BackupJob));
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
        if (e.PropertyName == nameof(BackupJob.Mode)) Rebuild();
    }

    private void Rebuild()
    {
        _jobs.Clear();
        foreach (var j in _main.Jobs)
            if (j.Mode == BackupMode.Standalone)
                _jobs.Add(j);
    }

    private void Add()
    {
        var dlg = new OpenFolderDialog { Title = "选择要纳入独立式备份的项目文件夹", Multiselect = true };
        if (dlg.ShowDialog() != true) return;

        int added = 0;
        foreach (var f in dlg.FolderNames)
            if (_main.TryAddJob(f, BackupMode.Standalone, RepoName?.Trim() ?? string.Empty, isPrivate: IsPrivate)) added++;

        Message = added > 0
            ? $"已添加 {added} 个独立式备份任务。"
            : "所选文件夹均已存在，未重复添加。";
    }

    private void DeleteJob(BackupJob? job)
    {
        if (job == null) return;
        _main.DeleteStandaloneJob(job);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
