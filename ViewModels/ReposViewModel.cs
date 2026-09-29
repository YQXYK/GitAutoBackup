using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GitAutoBackup.Models;
using GitAutoBackup.Services;

namespace GitAutoBackup.ViewModels;

/// <summary>「GitHub 仓库」页：列出当前账号在 GitHub 上的仓库。</summary>
public class ReposViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _main;

    public ObservableCollection<GitHubRepo> Repos { get; } = new();

    // ---- 账号相关（复用主页 VM，保证全局一致） ----
    /// <summary>GitHub 账号。</summary>
    public string AccountText
    {
        get => _main.AccountText;
        set => _main.AccountText = value;
    }
    public ICommand DetectAccountCommand => _main.DetectAccountCommand;

    /// <summary>解锁删除权限（转发全局命令：内置登录时重新授权，否则走内置终端 gh）。</summary>
    public ICommand UnlockDeleteCommand => _main.UnlockDeleteCommand;

    private bool _hasDeleteScope;
    /// <summary>当前 token 是否已开通 delete_repo 权限。</summary>
    public bool HasDeleteScope
    {
        get => _hasDeleteScope;
        private set
        {
            if (_hasDeleteScope == value) return;
            _hasDeleteScope = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeleteScopeStatusText));
        }
    }

    private bool _deleteScopeChecked;
    /// <summary>是否成功完成过一次权限检测（用于区分"未开通"与"检测失败"）。</summary>
    public bool DeleteScopeChecked
    {
        get => _deleteScopeChecked;
        private set
        {
            if (_deleteScopeChecked == value) return;
            _deleteScopeChecked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeleteScopeStatusText));
        }
    }

    /// <summary>删除权限状态文本：区分「已开通 / 未开通 / 检测失败」，避免检测异常时误报为"未开通"。</summary>
    public string DeleteScopeStatusText => !_deleteScopeChecked
        ? "权限状态未知（检测失败）"
        : _hasDeleteScope ? "删除权限已开通" : "删除权限未开通";

    public ICommand RefreshDeleteScopeCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>编辑选中仓库的可见性与描述（CommandParameter 为 GitHubRepo）。</summary>
    public ICommand EditCommand { get; }

    /// <summary>删除选中仓库（CommandParameter 为 GitHubRepo）。</summary>
    public ICommand DeleteCommand { get; }

    private bool _isLoading;
    /// <summary>本次加载的开始时间，用于超时兜底（防止异常导致永久卡在"加载中"）。</summary>
    private DateTime _loadStartedAt;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        private set { _message = value; OnPropertyChanged(); }
    }

    public ReposViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(_ => Load());
        RefreshDeleteScopeCommand = new RelayCommand(_ => RefreshDeleteScope());
        EditCommand = new RelayCommand(p => Edit(p as GitHubRepo));
        DeleteCommand = new RelayCommand(p => Delete(p as GitHubRepo));
        Load();
        RefreshDeleteScope();

        // 转发主页 VM 的账号变更，保证本页输入框实时刷新
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.AccountText)) OnPropertyChanged(nameof(AccountText));
        };

        // 删除权限可能在别处开通（内置终端完成 gh 授权 / 重新登录追加范围），
        // 开通后主页会广播该事件，这里自动重新检测，无需用户手动点「刷新」。
        main.DeleteScopeChanged += RefreshDeleteScope;
    }

    private void Load()
    {
        // 防重入：上次仍在加载则忽略本次请求。
        // 但加超时兜底 —— 若上次加载超过 30 秒仍未结束（异常导致状态未复位），允许重新加载，
        // 否则界面会永久卡在"加载中"且刷新失效（只能重启程序）。
        if (_isLoading && (DateTime.UtcNow - _loadStartedAt).TotalSeconds < 30) return;

        _isLoading = true;
        _loadStartedAt = DateTime.UtcNow;
        IsLoading = true;
        Message = "正在获取仓库列表...";
        var task = _main.AddTask("获取仓库列表");
        _main.SetBusyState(true, "正在获取 GitHub 仓库列表 ...");

        System.Threading.Tasks.Task.Run(() =>
        {
            // 1. 取数据：底层即便抛异常也要兜住，否则下面的状态复位永远不会执行
            var list = new List<GitHubRepo>();
            string? error = null;
            try
            {
                list = GitHubService.ListRepos(out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _main.Log("获取仓库列表异常：" + ex.Message);
            }

            // 2. 更新界面
            try
            {
                RunOnUi(() =>
                {
                    Repos.Clear();
                    foreach (var r in list) Repos.Add(r);
                    Message = !string.IsNullOrEmpty(error)
                        ? "获取失败：" + error
                        : list.Count == 0
                            ? "没有找到仓库（可在左下角账号入口登录，或执行 gh auth login）。"
                            : $"共 {list.Count} 个仓库（按最近更新排序）。";
                });
            }
            catch (Exception ex)
            {
                error ??= ex.Message;
                _main.Log("刷新仓库列表界面失败：" + ex.Message);
            }
            finally
            {
                // 3. 无论如何都要解除"加载中"，并复位底部状态栏进度条
                _isLoading = false;
                RunOnUi(() => IsLoading = false);
                _main.SetBusyState(false, string.IsNullOrEmpty(error) ? $"已加载 {list.Count} 个仓库" : "✗ 获取仓库列表失败");
                _main.CompleteTask(task, string.IsNullOrEmpty(error),
                    string.IsNullOrEmpty(error) ? $"已加载 {list.Count} 个仓库" : "获取失败");
            }
        });
    }

    /// <summary>编辑仓库的可见性与描述。</summary>
    private void Edit(GitHubRepo? repo)
    {
        if (repo == null) return;

        var dlg = new Views.EditRepoDialog(repo.FullName, repo.IsPrivate, repo.Description)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true) return;

        var isPrivate = dlg.IsPrivate;
        var description = dlg.Description;

        Message = $"正在更新 {repo.FullName} ...";
        var task = _main.AddTask($"更新仓库 {repo.FullName}");
        _main.SetBusyState(true, $"正在更新仓库 {repo.FullName} ...");
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;
            try { GitHubService.UpdateRepo(repo.FullName, isPrivate, description, out error); }
            catch (Exception ex) { error = ex.Message; }

            try
            {
                RunOnUi(() =>
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        // 这两个属性带变更通知，改完表格行会实时刷新
                        repo.IsPrivate = isPrivate;
                        repo.Description = description;
                        Message = $"已更新仓库：{repo.FullName}";
                    }
                    else
                    {
                        Message = $"更新失败：{error}";
                    }
                });
            }
            catch (Exception ex)
            {
                error ??= ex.Message;
                _main.Log("更新仓库后刷新界面失败：" + ex.Message);
            }
            finally
            {
                // 无论成功失败都要复位忙碌状态，否则底部进度条会一直转
                var ok = string.IsNullOrEmpty(error);
                _main.SetBusyState(false, ok ? $"√ 已更新仓库 {repo.FullName}" : "✗ 更新仓库失败");
                _main.CompleteTask(task, ok, ok ? "已更新" : "更新失败");
            }
        });
    }

    /// <summary>删除一个 GitHub 仓库（含确认弹窗）。</summary>
    private void Delete(GitHubRepo? repo)
    {
        if (repo == null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"确认删除 GitHub 仓库「{repo.FullName}」？\n\n此操作不可撤销，仓库及其全部内容都会被删除。",
            "删除仓库",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        Message = $"正在删除 {repo.FullName} ...";
        var task = _main.AddTask($"删除仓库 {repo.FullName}");
        _main.SetBusyState(true, $"正在删除仓库 {repo.FullName} ...");
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;
            try { GitHubService.DeleteRepo(repo.FullName, out error); }
            catch (Exception ex) { error = ex.Message; }

            try
            {
                RunOnUi(() =>
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        Repos.Remove(repo);
                        Message = $"已删除仓库：{repo.FullName}";
                    }
                    else
                    {
                        Message = $"删除失败：{error}";
                    }
                });
            }
            catch (Exception ex)
            {
                error ??= ex.Message;
                _main.Log("删除仓库后刷新界面失败：" + ex.Message);
            }
            finally
            {
                var ok = string.IsNullOrEmpty(error);
                _main.SetBusyState(false, ok ? $"√ 已删除仓库 {repo.FullName}" : "✗ 删除仓库失败");
                _main.CompleteTask(task, ok, ok ? "已删除" : "删除失败");
            }
        });
    }

    /// <summary>检测当前 token 是否已开通 delete_repo 权限。异常时标记为"检测失败"而非"未开通"。</summary>
    private void RefreshDeleteScope()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            bool has = false;
            bool ok = false;
            try
            {
                has = GitHubService.HasDeleteScope();
                ok = true;
            }
            catch (Exception ex)
            {
                _main.Log("检测删除权限失败：" + ex.Message);
            }

            RunOnUi(() =>
            {
                DeleteScopeChecked = ok;
                HasDeleteScope = has;

                // 已确认开通：若主页还在后台轮询等待授权，让它停下，免得白跑 gh 进程
                if (ok && has) _main.StopDeleteScopeWatch();

                // 给状态栏一个明确反馈：否则用户点完「刷新」后，下面还停在旧的提示文字上
                _main.SetStatus(ok
                    ? (has ? "√ 删除权限已开通" : "删除权限未开通")
                    : "✗ 权限检测失败");
            });
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) { action(); return; }
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
