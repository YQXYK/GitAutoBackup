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
    public RelayCommand ShowTaskPanelCommand { get; }
    public RelayCommand EnvActionCommand { get; }
    public RelayCommand LoginCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand RefreshAccountCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    /// <summary>是否已内置登录（OAuth token）。</summary>
    public bool IsLoggedIn => GitHubAuthService.HasToken;

    /// <summary>头像下方的状态说明。</summary>
    public string LoginStatusText => IsLoggedIn ? "内置登录" : "未登录 · 点此登录";

    private System.Windows.Media.ImageSource? _avatarImage;
    /// <summary>用户头像（未登录或加载失败时为 null，界面回退到默认占位）。</summary>
    public System.Windows.Media.ImageSource? AvatarImage
    {
        get => _avatarImage;
        private set { _avatarImage = value; OnPropertyChanged(); }
    }

    /// <summary>运行环境是否可用。**git 必需**；gh 可选（未装时用内置登录 OAuth）。</summary>
    public bool EnvOk => _gitInstalled;

    /// <summary>环境状态文本（状态栏显示）。</summary>
    public string EnvStatusText => !_envChecked
        ? "环境检查中 ..."
        : !_gitInstalled
            ? "缺少 git CLI，点此下载"
            : _ghInstalled
                ? "环境正常（git / gh）"
                : "环境正常（git）· 未装 gh（可选，点此安装）";

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

    private BottomPanelTab _bottomTab = BottomPanelTab.Log;

    /// <summary>底部面板当前标签（日志 / 终端 / 任务）。</summary>
    public BottomPanelTab BottomTab
    {
        get => _bottomTab;
        set
        {
            if (_bottomTab == value) return;
            _bottomTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLogPanel));
            OnPropertyChanged(nameof(IsTerminalPanel));
            OnPropertyChanged(nameof(IsTaskPanel));
        }
    }

    public bool IsLogPanel => _bottomTab == BottomPanelTab.Log;
    public bool IsTerminalPanel => _bottomTab == BottomPanelTab.Terminal;
    public bool IsTaskPanel => _bottomTab == BottomPanelTab.Tasks;

    /// <summary>任务记录（底部「任务」面板显示，最新在前）。</summary>
    public ObservableCollection<TaskRecord> Tasks { get; } = new();

    public MainViewModel()
    {
        _settings = SettingsService.Load();
        _backup = new BackupService();
        _backup.Log += (msg) => AppendLog(msg);
        _backup.StageChanged += (msg) => RunOnUi(() => StatusText = msg);
        _backup.PushFailedPrompt = (err, project) => AskPushFailed(err, project);
        _backup.LargeFilePrompt = (req) => AskSkipLargeFiles(req);

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
        LoginCommand = new RelayCommand(_ => StartLogin(GitHubAuthService.DefaultScope));
        LogoutCommand = new RelayCommand(_ => Logout());
        RefreshAccountCommand = new RelayCommand(_ => RefreshAccount(withAvatar: true));
        ShowLogCommand = new RelayCommand(_ => IsLogVisible = true);
        ShowLogPanelCommand = new RelayCommand(_ => BottomTab = BottomPanelTab.Log);
        ShowTerminalPanelCommand = new RelayCommand(_ => BottomTab = BottomPanelTab.Terminal);
        ShowTaskPanelCommand = new RelayCommand(_ => BottomTab = BottomPanelTab.Tasks);
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        ClearLogCommand = new RelayCommand(_ => ClearLog());

        // 启动时清理过期日志
        LogService.CleanupOldLogs();
        AppendLog("===== GitAutoBackup 启动 =====");

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
        LoadAvatar();   // 先显示本地缓存头像
        if (GitHubAuthService.HasToken) { RefreshAccount(withAvatar: true); return; }
        if (string.IsNullOrWhiteSpace(_settings.GitHubAccount))
            DetectAccount();
    }

    private void DetectAccount()
    {
        SetBusyState(true, "正在检测 GitHub 账号 ...");
        var task = AddTask("检测 GitHub 账号");
        System.Threading.Tasks.Task.Run(() =>
        {
            // gh 调用可能超时并抛异常（ProcessRunner 超时即抛），必须兜住，
            // 否则下面的 RunOnUi 整块会被跳过，SetBusyState(false) 永不执行 → 底部进度条永久转动。
            string acc = string.Empty;
            string? error = null;
            try
            {
                acc = GitHubService.GetAccount();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppendLog("检测账号异常：" + ex.Message);
            }

            RunOnUi(() =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(acc))
                    {
                        _settings.GitHubAccount = acc;
                        OnPropertyChanged(nameof(AccountText));
                        SetBusyState(false, $"√ 已检测到账号 {acc}");
                        CompleteTask(task, true, $"账号 {acc}");
                    }
                    else
                    {
                        SetBusyState(false, error != null ? "✗ 检测账号失败" : "✗ 未检测到账号");
                        CompleteTask(task, false, error != null ? "检测失败" : "未检测到（可点「登录 GitHub」）");
                        AppendLog(error != null
                            ? $"检测账号失败：{error}"
                            : "未检测到 GitHub 账号：可点「登录 GitHub」内置登录，或用 gh auth login。");
                    }
                    Save();
                }
                finally
                {
                    // 兜底：任何意外都不能让忙碌状态卡住（只复位忙碌标志，不改状态文字）
                    IsBusy = false;
                }
            });
        });
    }

    /// <summary>
    /// 解锁删除权限：已内置登录则重新授权追加 delete_repo；否则走 gh（内置终端）。
    /// </summary>
    private void UnlockDeleteScope()
    {
        if (GitHubAuthService.HasToken)
        {
            AddTask("解锁删除权限（内置登录）");
            StartLogin(GitHubAuthService.DeleteScope);
            return;
        }

        BottomTab = BottomPanelTab.Terminal;
        IsLogVisible = true;
        var task = AddTask("解锁删除权限（gh 方式）");
        AppendLog("正在内置终端里请求删除仓库权限（delete_repo），请在终端里按提示完成授权...");
        SetBusyState(true, "等待浏览器授权...");
        TerminalCommandRequested?.Invoke("gh auth refresh -s delete_repo -h github.com");
        CompleteTask(task, true, "已发起，请在终端完成授权");

        // gh 授权在终端/浏览器里进行，程序拿不到完成信号，只能轮询检测权限是否已开通。
        StartDeleteScopeWatch(task);
    }

    /// <summary>删除权限状态发生变化（检测到已开通）时通知各页面刷新。</summary>
    public event Action? DeleteScopeChanged;

    /// <summary>请求停止删除权限的后台轮询（例如用户已手动刷新确认开通）。</summary>
    private volatile bool _deleteScopeWatchStop;

    /// <summary>已通过其他途径确认权限状态，停止后台轮询，避免无谓地反复调用 gh。</summary>
    public void StopDeleteScopeWatch() => _deleteScopeWatchStop = true;

    /// <summary>
    /// 轮询检测删除权限是否已开通。
    ///
    /// 背景：gh 的授权流程跑在内置终端里，用户在浏览器完成后，软件侧没有任何回调可用，
    /// 若不轮询就会出现「已经授权成功，但界面一直显示等待浏览器授权」的错觉。
    /// 检测到开通后复位状态栏、更新任务记录，并通知「GitHub 仓库」页刷新权限文字。
    /// </summary>
    private void StartDeleteScopeWatch(TaskRecord task)
    {
        _deleteScopeWatchStop = false;

        System.Threading.Tasks.Task.Run(() =>
        {
            // 最多等 3 分钟，每 3 秒检测一次（授权通常在一分钟内完成）
            const int maxWaitMs = 3 * 60 * 1000;
            const int intervalMs = 3000;

            for (var waited = 0; waited < maxWaitMs; waited += intervalMs)
            {
                System.Threading.Thread.Sleep(intervalMs);
                if (_deleteScopeWatchStop) return;

                bool ok;
                try { ok = GitHubService.HasDeleteScope(); }
                catch { continue; }   // 单次检测失败不影响整体等待

                if (!ok) continue;

                RunOnUi(() =>
                {
                    SetBusyState(false, "√ 删除权限已开通");
                    CompleteTask(task, true, "已开通");
                    AppendLog("√ 已检测到删除权限开通，现在可以删除 GitHub 仓库了。");
                    DeleteScopeChanged?.Invoke();
                });
                return;
            }

            if (_deleteScopeWatchStop) return;

            RunOnUi(() =>
            {
                SetBusyState(false, "授权未完成");
                CompleteTask(task, false, "等待超时");
                AppendLog("ℹ 等待删除权限授权超时（3 分钟）。若你已完成授权，可点「GitHub 仓库」页的「刷新」重新检测。");
            });
        });
    }

    /// <summary>内置登录：申请设备码 → 弹框等待授权 → 保存 token → 拉取账号与头像。</summary>
    private void StartLogin(string scope)
    {
        var task = AddTask("登录 GitHub");
        SetBusyState(true, "正在获取设备码 ...");
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var info = GitHubAuthService.RequestDeviceCode(scope, out var error);
                RunOnUi(() =>
                {
                    SetBusyState(false, "就绪");
                    if (info == null)
                    {
                        CompleteTask(task, false, "获取设备码失败");
                        AppendLog("获取设备码失败：" + error);
                        return;
                    }

                    var dlg = new Views.LoginDialog(info)
                    {
                        Owner = System.Windows.Application.Current?.MainWindow
                    };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Token))
                    {
                        GitHubAuthService.SaveToken(dlg.Token);
                        CompleteTask(task, true, "已登录");
                        SetBusyState(false, "√ 已登录 GitHub");
                        // 重新授权可能追加了 delete_repo 范围，通知页面刷新权限状态
                        DeleteScopeChanged?.Invoke();
                        RefreshAccount(withAvatar: true);
                    }
                    else
                    {
                        CompleteTask(task, false, "已取消");
                    }
                });
            }
            catch (Exception ex)
            {
                // 取设备码阶段异常（网络/超时等）：同样要复位忙碌状态并告知用户
                RunOnUi(() =>
                {
                    SetBusyState(false, "✗ 获取设备码失败");
                    CompleteTask(task, false, "获取设备码失败");
                    AppendLog("获取设备码失败：" + ex.Message);
                });
            }
        });
    }

    /// <summary>退出内置登录（之后回退到 gh CLI）。</summary>
    private void Logout()
    {
        GitHubAuthService.ClearToken();
        AvatarService.Clear();
        AvatarImage = null;
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(LoginStatusText));
        SetBusyState(false, "已退出登录");
        AppendLog("已退出内置登录，后续将使用 gh CLI。");
    }

    /// <summary>刷新账号信息（可选同时更新头像）。</summary>
    public void RefreshAccount(bool withAvatar = false)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var profile = GitHubService.GetUserProfile();
                RunOnUi(() =>
                {
                    if (profile is { Login.Length: > 0 } p)
                    {
                        _settings.GitHubAccount = p.Login;
                        OnPropertyChanged(nameof(AccountText));
                        OnPropertyChanged(nameof(IsLoggedIn));
                        OnPropertyChanged(nameof(LoginStatusText));
                        if (withAvatar && !string.IsNullOrWhiteSpace(p.AvatarUrl))
                            AvatarService.Ensure(p.Login, p.AvatarUrl);
                        Save();
                    }
                    LoadAvatar();
                });
            }
            catch (Exception ex)
            {
                // 获取账号/头像失败不应影响其他功能，只记录并回退到本地缓存头像
                AppendLog("刷新账号信息失败：" + ex.Message);
                RunOnUi(LoadAvatar);
            }
        });
    }

    /// <summary>从本地缓存加载头像为 ImageSource。</summary>
    private void LoadAvatar()
    {
        RunOnUi(() =>
        {
            try
            {
                var path = AvatarService.GetCachedPath();
                if (path == null || !System.IO.File.Exists(path)) { AvatarImage = null; return; }

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                AvatarImage = bmp;
            }
            catch { AvatarImage = null; }
        });
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
                AppendLog($"环境检查：{EnvStatusText}");
            });
        });
    }

    /// <summary>状态栏环境按钮：全部就绪则重新检测；缺 git（必需）或想装 gh（可选）时打开对应下载页。</summary>
    private void EnvAction()
    {
        if (!_envChecked || (EnvOk && _ghInstalled)) { CheckEnvironment(); return; }

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

    /// <summary>新增一条「进行中」任务记录（供各页面报告操作进度）。</summary>
    public TaskRecord AddTask(string title)
    {
        var t = new TaskRecord { Title = title };
        RunOnUi(() =>
        {
            Tasks.Insert(0, t);
            while (Tasks.Count > 100) Tasks.RemoveAt(Tasks.Count - 1);
        });
        return t;
    }

    /// <summary>结束任务记录。</summary>
    public void CompleteTask(TaskRecord? task, bool ok, string? status = null)
    {
        if (task == null) return;
        RunOnUi(() => task.Status = status ?? (ok ? "√ 成功" : "✗ 失败"));
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
        var task = AddTask($"删除集中仓库 {repoName}");
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
                try
                {
                    var jobs = Jobs.Where(j => j.Mode == BackupMode.Consolidated
                            && string.Equals(j.ConsolidatedRepoName, repoName, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var j in jobs) { Jobs.Remove(j); _settings.Jobs.Remove(j); }

                    ConsolidatedRepos.Remove(repoName);
                    _settings.ConsolidatedRepos.Remove(repoName);

                    Save();
                    StatusText = "就绪";
                    CompleteTask(task, string.IsNullOrEmpty(error), string.IsNullOrEmpty(error) ? "已删除" : "已删除（部分失败）");
                    if (!string.IsNullOrEmpty(error)) AppendLog($"删除集中仓库 {repoName} 出现问题：{error}");
                }
                finally
                {
                    // 兜底：清理过程出错也不能让忙碌状态卡住
                    IsBusy = false;
                }
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
        var task = AddTask($"删除独立仓库 {repoFull}");
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
                try
                {
                    Jobs.Remove(job);
                    _settings.Jobs.Remove(job);
                    Save();
                    StatusText = "就绪";
                    CompleteTask(task, string.IsNullOrEmpty(error), string.IsNullOrEmpty(error) ? "已删除" : "已删除（部分失败）");
                    if (!string.IsNullOrEmpty(error)) AppendLog($"删除独立仓库 {repoFull} 出现问题：{error}");
                }
                finally
                {
                    IsBusy = false;
                }
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
        var list = jobs as BackupJob[] ?? jobs.ToArray();
        var task = AddTask($"备份 {list.Length} 个项目");

        // 每批次重置「其余项目也跳过超大文件」的选择，避免上次的选择影响本次
        _skipLargeFilesForRest = false;

        // 需在 UI 线程切换 IsBusy，因为绑定要求
        RunOnUi(() => { IsBusy = true; StatusText = "正在备份..."; });

        var okCount = 0;
        var failures = new List<(string name, string reason)>();
        try
        {
            foreach (var job in list)
            {
                // 「状态 / 最近备份」由 BackupJob 自身通知（已切回 UI 线程），无需整表刷新
                if (_backup.RunJob(job, _settings)) okCount++;
                else failures.Add((job.ProjectName, job.LastStatus));
            }
            Save();
            CompleteTask(task, okCount == list.Length, $"{okCount}/{list.Length} 成功");
        }
        catch (Exception ex)
        {
            CompleteTask(task, false, "备份异常");
            AppendLog("备份异常：" + ex.Message);
            failures.Add(("（整体流程）", "程序异常：" + ex.Message));
        }
        finally
        {
            _settings.LastRunAt = DateTime.Now;
            Save();

            if (failures.Count == 0)
            {
                RunOnUi(() =>
                {
                    IsBusy = false;
                    StatusText = $"√ 备份完成（{okCount}/{list.Length} 成功）";
                });
            }
            else
            {
                var failCount = failures.Count;
                RunOnUi(() =>
                {
                    IsBusy = false;
                    StatusText = $"备份完成：{okCount} 成功 / {failCount} 失败";
                });
                // 有失败时主动弹窗汇总 —— 避免失败结果只出现在小字日志里被忽略
                ShowFailureSummary(failures, list.Length, okCount);
            }
        }
    }

    /// <summary>批量备份结束后，若有项目失败，弹窗汇总说明（每个项目给一句通俗原因）。</summary>
    private void ShowFailureSummary(List<(string name, string reason)> failures, int total, int okCount)
    {
        RunOnUi(() =>
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"本次备份共 {total} 个项目：{okCount} 个成功，{failures.Count} 个失败。");
                sb.AppendLine();
                sb.AppendLine("失败的项目：");
                foreach (var (name, reason) in failures.Take(10))
                    sb.AppendLine($"    · {name} —— {reason}");
                if (failures.Count > 10)
                    sb.AppendLine($"    · …另有 {failures.Count - 10} 个项目失败");
                sb.AppendLine();
                sb.AppendLine("完整的原始报错可在下方「日志」面板查看。");

                System.Windows.MessageBox.Show(
                    sb.ToString(),
                    "备份未全部完成",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AppendLog("显示失败汇总时出错：" + ex.Message);
            }
        });
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
        // 先落盘（含级别判定），再更新界面内存缓冲
        LogService.Write(msg, GuessLevel(msg));

        RunOnUi(() =>
        {
            LogText += msg + Environment.NewLine;
            // 截断防止无限增长
            if (LogText.Length > 200000) LogText = LogText[^100000..];
            OnPropertyChanged(nameof(LogText));
        });
    }

    /// <summary>供其他 ViewModel 写程序日志（落盘 + 界面同步）。</summary>
    public void Log(string message) => AppendLog(message);

    /// <summary>
    /// 仅更新状态栏文字，不触碰忙碌标志。
    /// 供各页面报告轻量结果（如"权限检测完成"），避免误复位正在进行的备份的忙碌状态。
    /// </summary>
    public void SetStatus(string text) => RunOnUi(() =>
    {
        if (!string.IsNullOrEmpty(text)) StatusText = text;
    });

    /// <summary>按关键词粗略判定日志级别，仅用于文件里的标签，不影响界面显示。</summary>
    private static LogLevel GuessLevel(string msg)
    {
        if (msg.Contains("失败") || msg.Contains("错误") || msg.Contains("异常") || msg.Contains("✗"))
            return LogLevel.Error;
        if (msg.Contains("警告") || msg.Contains("未检测到") || msg.Contains("重试") || msg.Contains("无法"))
            return LogLevel.Warn;
        return LogLevel.Info;
    }

    /// <summary>在资源管理器里打开日志目录。</summary>
    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogService.LogDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LogService.LogDir,
                UseShellExecute = true
            });
            SetBusyState(false, "已打开日志目录");
        }
        catch (Exception ex)
        {
            AppendLog("打开日志目录失败：" + ex.Message);
            SetBusyState(false, "✗ 打开日志目录失败");
        }
    }

    /// <summary>清空当前窗口与磁盘上的日志。</summary>
    private void ClearLog()
    {
        LogService.ClearAll();
        RunOnUi(() =>
        {
            LogText = string.Empty;
            OnPropertyChanged(nameof(LogText));
        });
        AppendLog("日志已清空。");
        SetBusyState(false, "日志已清空");
    }

    /// <summary>本批次备份中，用户勾选「其余项目也这样处理」后置为 true，后续超大文件不再弹窗。</summary>
    private bool _skipLargeFilesForRest;

    /// <summary>
    /// 备份前发现超过 GitHub 单文件上限的文件时弹窗，请用户决定。
    /// 阻塞等待用户选择（备份本身在后台线程执行，UI 线程可正常响应）。
    /// 返回 true=跳过这些文件并继续备份，false=取消本次备份。
    /// </summary>
    private bool AskSkipLargeFiles(LargeFileRequest req)
    {
        var result = false;
        RunOnUi(() =>
        {
            // 用户已选择"其余项目也这样处理"
            if (_skipLargeFilesForRest)
            {
                AppendLog($"⚠ [{req.ProjectName}] 已按上次选择自动跳过 {req.Scan.BlockingFiles.Count} 个超大文件。");
                result = true;
                return;
            }

            StatusText = "发现超大文件，等待你的选择...";
            try
            {
                var dlg = new Views.LargeFileDialog(req.ProjectName, req.Scan);
                var main = System.Windows.Application.Current?.MainWindow;
                // 主窗口已显示才能设 Owner，否则设 CenterScreen 避免异常
                if (main != null && main.IsLoaded) dlg.Owner = main;
                else dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                result = dlg.ShowDialog() == true && dlg.SkipThem;
                if (result && dlg.ApplyToRest) _skipLargeFilesForRest = true;
            }
            catch (Exception ex)
            {
                AppendLog("超大文件确认对话框出错：" + ex.Message);
                result = false;
            }
            StatusText = result ? "已选择跳过超大文件，继续备份" : "已取消备份";
        });
        return result;
    }

    /// <summary>
    /// 推送失败时弹出通俗易懂的说明（而不是让用户读 git 原始报错）。
    /// 阻塞等待用户选择（备份在后台线程执行，UI 线程可正常响应）。
    /// 返回 true=重试；false=结束本次备份。对"重试必然失败"的错误，弹窗不提供重试按钮，故一定返回 false。
    /// </summary>
    private bool AskPushFailed(FriendlyError error, string projectName)
    {
        var result = false;
        RunOnUi(() =>
        {
            StatusText = error.Title;
            try
            {
                var dlg = new Views.PushFailedDialog(projectName, error);
                var main = System.Windows.Application.Current?.MainWindow;
                if (main != null && main.IsLoaded) dlg.Owner = main;
                else dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

                dlg.ShowDialog();
                result = dlg.RetryRequested;
            }
            catch (Exception ex)
            {
                AppendLog("推送失败提示框出错：" + ex.Message);
                result = false;
            }
            StatusText = result ? "将重试推送" : $"推送失败：{error.Title}";
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