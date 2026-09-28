namespace GitAutoBackup.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using GitAutoBackup.Models;
using GitAutoBackup.Services;
using Microsoft.Win32;

/// <summary>主窗口的简单 MVVM。</summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Settings _settings;
    private readonly BackupService _backup;
    private readonly DispatcherTimer _timer = new();

    private bool _gitInstalled;
    private bool _ghInstalled;
    private bool _envChecked;

    // ---- 导航 ----
    public ObservableCollection<NavItem> NavItems { get; } = new();

    private NavItem? _selectedNavItem;
    /// <summary>当前选中的导航项，切换时更新右侧页面。</summary>
    public NavItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            if (SetField(ref _selectedNavItem, value))
            {
                foreach (var n in NavItems) n.IsSelected = ReferenceEquals(n, value);
                OnPropertyChanged(nameof(CurrentPage));
            }
        }
    }

    /// <summary>当前展示的页面 ViewModel（由选中导航项决定）。</summary>
    public object? CurrentPage => SelectedNavItem?.PageViewModel;

    public ObservableCollection<BackupJob> Jobs { get; } = new();
    /// <summary>集中式仓库名列表（多仓库，供「集中仓库」页展示与选择）</summary>
    public ObservableCollection<string> ConsolidatedRepos { get; } = new();
    public string LogText { get; private set; } = string.Empty;

    private string _statusText = "就绪";
    /// <summary>状态栏文字：备份进行中的当前阶段提示。</summary>
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    /// <summary>备份方式选项（供界面下拉，显示中文）</summary>
    public List<KeyValuePair<BackupMode, string>> ModeOptions { get; } = new()
    {
        new(BackupMode.Consolidated, "集中式"),
        new(BackupMode.Standalone, "独立式"),
    };

    /// <summary>定时间隔单位选项</summary>
    public string[] IntervalUnits { get; } = { "分钟", "小时", "天" };

    private BackupJob? _selectedJob;
    public BackupJob? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetField(ref _selectedJob, value))
            {
                RemoveJobCommand?.RaiseCanExecuteChanged();
                RunSelectedCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    // 仓库/账号区
    /// <summary>账号框：可手动输入（直接绑定保存），点“自动检测”按钮则自动获取。</summary>
    public string AccountText
    {
        get => _settings.GitHubAccount;
        set { _settings.GitHubAccount = value?.Trim() ?? string.Empty; OnPropertyChanged(); Save(); }
    }
    public string ConsolidatedRepoName
    {
        get => _settings.ConsolidatedRepoName;
        set { _settings.ConsolidatedRepoName = value; OnPropertyChanged(); Save(); }
    }
    public string BackupRoot
    {
        get => _settings.BackupRoot;
        set { _settings.BackupRoot = value; OnPropertyChanged(); Save(); }
    }

    // 定时区
    public double IntervalValue
    {
        get => _settings.IntervalValue;
        set { _settings.IntervalValue = value; OnPropertyChanged(); UpdateTimer(); Save(); }
    }
    public string IntervalUnit
    {
        get => _settings.IntervalUnit;
        set { _settings.IntervalUnit = value; OnPropertyChanged(); UpdateTimer(); Save(); }
    }
    public bool SchedulerEnabled
    {
        get => _settings.SchedulerEnabled;
        set { _settings.SchedulerEnabled = value; OnPropertyChanged(); UpdateTimer(); Save(); }
    }

    // 命令
    public RelayCommand RemoveJobCommand { get; }
    public RelayCommand PickBackupRootCommand { get; }
    public RelayCommand RunAllCommand { get; }
    public RelayCommand RunSelectedCommand { get; }
    public ICommand DetectAccountCommand { get; }
    public RelayCommand ToggleLogCommand { get; }
    public RelayCommand UnlockDeleteCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand ShowLogPanelCommand { get; }
    public RelayCommand ShowTerminalPanelCommand { get; }
    public RelayCommand EnvActionCommand { get; }

    /// <summary>运行环境是否就绪（git 与 gh CLI 均可用）。</summary>
    public bool EnvOk => _gitInstalled && _ghInstalled;

    /// <summary>环境状态文本（状态栏显示）。</summary>
    public string EnvStatusText => !_envChecked
        ? "环境检查中 ..."
        : EnvOk
            ? "环境正常（git / gh）"
            : !_gitInstalled && !_ghInstalled ? "缺少 git 与 gh CLI，点此下载"
            : !_gitInstalled ? "缺少 git CLI，点此下载"
            : "缺少 gh CLI，点此下载";

    /// <summary>请求在内置终端里执行命令（由 MainWindow 订阅并写入终端）。</summary>
    public event Action<string>? TerminalCommandRequested;

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            _busy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIdle));
            RunAllCommand?.RaiseCanExecuteChanged();
            RunSelectedCommand?.RaiseCanExecuteChanged();
        }
    }
    public bool IsIdle => !_busy;

    private bool _isLogVisible = true;
    /// <summary>底部日志面板是否展开（可最小化）。</summary>
    public bool IsLogVisible
    {
        get => _isLogVisible;
        set => SetField(ref _isLogVisible, value);
    }

    private bool _isTerminalPanel;
    /// <summary>底部面板当前显示「终端」（false=日志，true=终端）。</summary>
    public bool IsTerminalPanel
    {
        get => _isTerminalPanel;
        set => SetField(ref _isTerminalPanel, value);
    }

    public MainViewModel()
    {
        _settings = SettingsService.Load();
        _backup = new BackupService();
        _backup.Log += (msg) => AppendLog(msg);
        _backup.StageChanged += (msg) => RunOnUi(() => StatusText = msg);
        _backup.RetryPrompt += (err) => AskRetry(err);

        foreach (var j in _settings.Jobs) Jobs.Add(j);

        // 迁移旧数据：旧版只有一个全局仓库名 → 纳入多仓库列表；旧集中式任务归入默认仓库
        if (_settings.ConsolidatedRepos.Count == 0 && !string.IsNullOrWhiteSpace(_settings.ConsolidatedRepoName))
            _settings.ConsolidatedRepos.Add(_settings.ConsolidatedRepoName);
        foreach (var r in _settings.ConsolidatedRepos) ConsolidatedRepos.Add(r);
        foreach (var j in Jobs)
            if (j.Mode == BackupMode.Consolidated && string.IsNullOrWhiteSpace(j.ConsolidatedRepoName))
                j.ConsolidatedRepoName = _settings.ConsolidatedRepoName;

        RemoveJobCommand = new RelayCommand(_ => RemoveSelected(), _ => SelectedJob != null);
        PickBackupRootCommand = new RelayCommand(_ => PickBackupRoot());
        RunAllCommand = new RelayCommand(_ => RunAll(), _ => IsIdle);
        RunSelectedCommand = new RelayCommand(_ => RunSelected(), _ => IsIdle && Jobs.Any(j => j.IsSelected));
        DetectAccountCommand = new RelayCommand(_ => DetectAccount());
        ToggleLogCommand = new RelayCommand(_ => IsLogVisible = !IsLogVisible);
        UnlockDeleteCommand = new RelayCommand(_ => UnlockDeleteScope());
        EnvActionCommand = new RelayCommand(_ => EnvAction());
        ShowLogCommand = new RelayCommand(_ => IsLogVisible = true);
        ShowLogPanelCommand = new RelayCommand(_ => IsTerminalPanel = false);
        ShowTerminalPanelCommand = new RelayCommand(_ => IsTerminalPanel = true);

        UpdateTimer();
        LoadAccount();

        // 导航页：本 VM 即「备份任务」页（展示 + 再备份）；新增项目到对应视图进行
        var tasksNav = new NavItem("备份任务", this);
        var reposNav = new NavItem("GitHub 仓库", new ReposViewModel(this));
        NavItems.Add(reposNav);
        NavItems.Add(tasksNav);
        NavItems.Add(new NavItem("集中仓库", new ConsolidatedViewModel(this)));
        NavItems.Add(new NavItem("独立仓库", new StandaloneViewModel(this)));
        var schedule = new ScheduleViewModel(_settings);
        schedule.Changed += () => RunOnUi(UpdateTimer);
        NavItems.Add(new NavItem("定时设置", schedule));
        NavItems.Add(new NavItem("排除规则", new ExcludesViewModel(_settings)));
        NavItems.Add(new NavItem("关于", new AboutViewModel()));
        // 启动默认定位到「GitHub 仓库」页
        SelectedNavItem = reposNav;

        // 启动后检查运行环境（git / gh CLI）
        CheckEnvironment();
    }

    private void LoadAccount()
    {
        // 启动时：仅当账号框为空时才自动检测，避免覆盖用户已手动填的值。
        if (string.IsNullOrWhiteSpace(_settings.GitHubAccount))
            DetectAccount();
    }

    private void DetectAccount()
    {
        RunOnUi(() => IsBusy = true);
        AppendLog("正在自动检测 GitHub 账号...");
        System.Threading.Tasks.Task.Run(() =>
        {
            var acc = GitHubService.GetAccount();
            RunOnUi(() =>
            {
                if (!string.IsNullOrWhiteSpace(acc))
                {
                    _settings.GitHubAccount = acc;
                    OnPropertyChanged(nameof(AccountText));
                    AppendLog($"自动检测成功：{acc}");
                }
                else
                {
                    AppendLog("未检测到 GitHub 账号，请手动输入或先运行：gh auth login");
                }
                IsBusy = false;
                Save();
            });
        });
    }

    /// <summary>请求 delete_repo 删除权限：在内置终端里执行 gh auth refresh。</summary>
    private void UnlockDeleteScope()
    {
        IsTerminalPanel = true;
        IsLogVisible = true;
        AppendLog("正在内置终端里请求删除仓库权限（delete_repo），请在终端里按提示完成授权...");
        StatusText = "等待浏览器授权...";
        TerminalCommandRequested?.Invoke("gh auth refresh -s delete_repo -h github.com");
    }

    private void PickBackupRoot()
    {
        var dlg = new OpenFolderDialog { Title = "选择集中式的备份根目录" };
        if (dlg.ShowDialog() == true) BackupRoot = dlg.FolderName;
    }

    private void RemoveSelected()
    {
        if (SelectedJob == null) return;
        var job = SelectedJob;

        // 按对象引用移除（同一项目可能同时存在集中式与独立式两条任务，不能按路径删）
        Jobs.Remove(job);
        _settings.Jobs.Remove(job);

        Save();
    }

    /// <summary>
    /// 由「集中仓库」「独立仓库」视图新增备份任务。
    /// 去重规则：集中式按「路径 + 集中仓库名」、独立式按「路径 + 独立仓库名」判断，
    /// 因此同一个项目可以分别拥有一条集中式任务和一条独立式任务。
    /// </summary>
    public bool TryAddJob(string sourcePath, BackupMode mode, string repoName = "", string consolidatedRepoName = "", bool isPrivate = true)
    {
        bool duplicated = mode == BackupMode.Consolidated
            ? Jobs.Any(j => j.Mode == BackupMode.Consolidated
                            && string.Equals(j.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(j.ConsolidatedRepoName, consolidatedRepoName, StringComparison.OrdinalIgnoreCase))
            : Jobs.Any(j => j.Mode == BackupMode.Standalone
                            && string.Equals(j.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(j.RepoName, repoName, StringComparison.OrdinalIgnoreCase));
        if (duplicated) return false;

        var job = new BackupJob
        {
            SourcePath = sourcePath,
            Mode = mode,
            RepoName = repoName,
            ConsolidatedRepoName = consolidatedRepoName,
            IsPrivate = isPrivate
        };
        Jobs.Add(job);
        _settings.Jobs.Add(job);
        Save();
        return true;
    }

    /// <summary>新增一个集中仓库名（含可见性）。返回清洗后的仓库名；重复或非法返回 null。</summary>
    public string? AddConsolidatedRepo(string name, bool isPrivate = true)
    {
        name = SanitizeRepoName(name);
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (ConsolidatedRepos.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;

        ConsolidatedRepos.Add(name);
        _settings.ConsolidatedRepos.Add(name);
        _settings.ConsolidatedRepoPrivates[name] = isPrivate;
        Save();
        return name;
    }

    /// <summary>后台刷新某集中仓库在 GitHub 上的 README（仓库不存在则跳过）。</summary>
    public void RefreshConsolidatedReadme(string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName) || string.IsNullOrWhiteSpace(_settings.GitHubAccount)) return;

        var repoFull = $"{_settings.GitHubAccount}/{repoName}";
        var projects = Jobs
            .Where(j => j.Mode == BackupMode.Consolidated
                        && string.Equals(j.ConsolidatedRepoName, repoName, StringComparison.OrdinalIgnoreCase))
            .Select(j => j.ProjectName)
            .ToList();

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                if (!GitHubService.RepoExists(repoFull)) return;
                GitHubService.WriteBackupReadme(repoFull, BackupService.BuildConsolidatedReadme(repoName, projects), out _);
            }
            catch { /* README 刷新失败忽略 */ }
        });
    }

    /// <summary>检查运行环境（git / gh CLI 是否可用）。</summary>
    public void CheckEnvironment()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var git = CheckCommand("git");
            var gh = CheckCommand("gh");
            RunOnUi(() =>
            {
                _gitInstalled = git;
                _ghInstalled = gh;
                _envChecked = true;
                OnPropertyChanged(nameof(EnvOk));
                OnPropertyChanged(nameof(EnvStatusText));
                AppendLog(EnvOk
                    ? "环境检查：git 与 gh CLI 均可用。"
                    : $"环境检查：{EnvStatusText}");
            });
        });
    }

    /// <summary>状态栏环境按钮：正常则重新检测，缺失则打开官方下载页。</summary>
    private void EnvAction()
    {
        if (EnvOk || !_envChecked) { CheckEnvironment(); return; }

        var url = !_gitInstalled ? "https://git-scm.com/download/win" : "https://cli.github.com/";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            AppendLog($"已打开下载页：{url}");
        }
        catch (Exception ex) { AppendLog("打开下载页失败：" + ex.Message); }
    }

    private static bool CheckCommand(string exe)
    {
        try
        {
            var r = ProcessRunner.Run(exe, "--version", timeoutMs: 10000);
            return r.Succeeded;
        }
        catch { return false; }
    }

    /// <summary>供各页面设置全局忙碌状态（驱动底部状态栏的进度条 + 状态文字）。</summary>
    public void SetBusyState(bool busy, string status)
    {
        RunOnUi(() =>
        {
            IsBusy = busy;
            if (!string.IsNullOrEmpty(status)) StatusText = status;
        });
    }

    private static string SanitizeRepoName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_');
    }

    /// <summary>删除一个集中仓库：GitHub 私有仓库 + 本地备份目录 + 其下所有任务 + 仓库列表。</summary>
    public void DeleteConsolidatedRepo(string repoName)
    {
        if (string.IsNullOrWhiteSpace(repoName)) return;

        var confirm = System.Windows.MessageBox.Show(
            $"确认删除集中仓库「{repoName}」？\n\n将删除：\n· GitHub 私有仓库 {_settings.GitHubAccount}/{repoName}\n· 本地备份目录\n· 该仓库下的所有备份项目",
            "删除集中仓库",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        RunOnUi(() => { IsBusy = true; StatusText = "正在删除仓库..."; });
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;

            // GitHub 仓库删除：失败不阻断，只记录
            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.GitHubAccount))
                    GitHubService.DeleteRepo($"{_settings.GitHubAccount}/{repoName}", out error);
            }
            catch (Exception ex) { error = "GitHub 删除异常：" + ex.Message; }

            // 本地备份目录删除：失败不阻断，只记录
            try { BackupService.DeleteConsolidatedRepoDirectory(repoName, _settings); }
            catch (Exception ex) { error = (string.IsNullOrEmpty(error) ? "" : error + "；") + "本地目录删除失败：" + ex.Message; }

            RunOnUi(() =>
            {
                var jobs = Jobs.Where(j => j.Mode == BackupMode.Consolidated
                        && string.Equals(j.ConsolidatedRepoName, repoName, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var j in jobs) { Jobs.Remove(j); _settings.Jobs.Remove(j); }

                ConsolidatedRepos.Remove(repoName);
                _settings.ConsolidatedRepos.Remove(repoName);

                Save();
                IsBusy = false;
                StatusText = "就绪";
                AppendLog($"√ 已删除集中仓库：{repoName}" + (string.IsNullOrEmpty(error) ? "" : $"（{error}）"));
            });
        });
    }

    /// <summary>删除一个独立式任务及其 GitHub 私有仓库。</summary>
    public void DeleteStandaloneJob(BackupJob job)
    {
        var repoFull = BackupService.ResolveStandaloneRepoFullName(job, _settings);
        var confirm = System.Windows.MessageBox.Show(
            $"确认删除独立仓库「{repoFull}」？\n\n将删除 GitHub 私有仓库，并移除该备份项目。",
            "删除独立仓库",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        RunOnUi(() => { IsBusy = true; StatusText = "正在删除仓库..."; });
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(repoFull))
                    GitHubService.DeleteRepo(repoFull, out error);
            }
            catch (Exception ex) { error = "GitHub 删除异常：" + ex.Message; }

            RunOnUi(() =>
            {
                Jobs.Remove(job);
                _settings.Jobs.Remove(job);
                Save();
                IsBusy = false;
                StatusText = "就绪";
                AppendLog($"√ 已删除独立仓库：{repoFull}" + (string.IsNullOrEmpty(error) ? "" : $"（{error}）"));
            });
        });
    }

    /// <summary>立即备份全部：不管是否勾选,备份列表中的所有任务。</summary>
    private void RunAll()
    {
        var jobs = Jobs.ToArray();
        if (jobs.Length == 0)
        {
            RunOnUi(() => System.Windows.MessageBox.Show(
                "当前没有可备份的任务，请先到「集中仓库」或「独立仓库」页新增项目。",
                "GitAutoBackup", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information));
            return;
        }
        System.Threading.Tasks.Task.Run(() => ExecuteJobs(jobs));
    }

    /// <summary>备份选中项目：只备份「备份」列已勾选（= 选中）的任务。</summary>
    private void RunSelected()
    {
        var jobs = Jobs.Where(j => j.IsSelected).ToArray();
        if (jobs.Length == 0)
        {
            RunOnUi(() => System.Windows.MessageBox.Show(
                "当前没有勾选任何任务。请至少勾选「备份」列中的一项后重试。",
                "GitAutoBackup", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information));
            return;
        }
        System.Threading.Tasks.Task.Run(() => ExecuteJobs(jobs));
    }

    private void ExecuteJobs(IEnumerable<BackupJob> jobs)
    {
        // 需在 UI 线程切换 IsBusy，因为绑定要求
        RunOnUi(() => { IsBusy = true; StatusText = "正在备份..."; });
        try
        {
            foreach (var job in jobs)
            {
                // 「状态 / 最近备份」由 BackupJob 自身通知（已切回 UI 线程），无需整表刷新
                _backup.RunJob(job, _settings);
            }
            Save();
        }
        finally
        {
            _settings.LastRunAt = DateTime.Now;
            Save();
            RunOnUi(() => { IsBusy = false; StatusText = "就绪"; });
        }
    }

    private void UpdateTimer()
    {
        _timer.Stop();
        _timer.Tick -= _timer_Tick;
        if (!SchedulerEnabled || IntervalValue <= 0) return;

        TimeSpan span;
        if (IntervalUnit == "小时") span = TimeSpan.FromHours(IntervalValue);
        else if (IntervalUnit == "天") span = TimeSpan.FromDays(IntervalValue);
        else span = TimeSpan.FromMinutes(IntervalValue);

        _timer.Interval = span;
        _timer.Tick += _timer_Tick;
        _timer.Start();
    }

    private void _timer_Tick(object? sender, EventArgs e) => MaybeRunScheduled();

    private void MaybeRunScheduled()
    {
        if (_busy) return;
        _settings.LastRunAt = DateTime.Now;
        RunAll();
    }

    private void AppendLog(string msg)
    {
        RunOnUi(() =>
        {
            LogText += msg + Environment.NewLine;
            // 截断防止无限增长
            if (LogText.Length > 200000) LogText = LogText[^100000..];
            OnPropertyChanged(nameof(LogText));
        });
    }

    /// <summary>在 UI 线程弹出推送失败对话框，询问是否重试（阻塞等待用户选择）。返回 true=重试。</summary>
    private bool AskRetry(string error)
    {
        bool result = false;
        RunOnUi(() =>
        {
            StatusText = "推送失败，等待你的选择...";
            var r = System.Windows.MessageBox.Show(
                "推送备份到 GitHub 失败。\n\n原始报错：\n" + error + "\n\n是否重试？",
                "GitAutoBackup - 推送失败",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            result = r == System.Windows.MessageBoxResult.Yes;
            StatusText = "已处理推送失败";
        });
        return result;
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) { action(); return; }
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    private void Save() => SettingsService.Save(_settings);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; OnPropertyChanged(name); return true; }
        return false;
    }

    public void Dispose() => _timer.Stop();
}