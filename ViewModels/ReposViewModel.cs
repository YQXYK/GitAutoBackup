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

    /// <summary>删除权限状态文本。</summary>
    public string DeleteScopeStatusText => _hasDeleteScope ? "删除权限已开通" : "删除权限未开通";

    public ICommand RefreshDeleteScopeCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>编辑选中仓库的可见性与描述（CommandParameter 为 GitHubRepo）。</summary>
    public ICommand EditCommand { get; }

    /// <summary>删除选中仓库（CommandParameter 为 GitHubRepo）。</summary>
    public ICommand DeleteCommand { get; }

    private bool _isLoading;
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
    }

    private void Load()
    {
        if (_isLoading) return;
        IsLoading = true;
        Message = "正在获取仓库列表...";
        _main.SetBusyState(true, "正在获取 GitHub 仓库列表 ...");

        System.Threading.Tasks.Task.Run(() =>
        {
            var list = GitHubService.ListRepos(out var error);
            RunOnUi(() =>
            {
                Repos.Clear();
                foreach (var r in list) Repos.Add(r);
                IsLoading = false;
                Message = !string.IsNullOrEmpty(error)
                    ? "获取失败：" + error
                    : list.Count == 0
                        ? "没有找到仓库（请确认已登录 gh：gh auth login）。"
                        : $"共 {list.Count} 个仓库（按最近更新排序）。";
                _main.SetBusyState(false, string.IsNullOrEmpty(error) ? $"已加载 {list.Count} 个仓库" : "✗ 获取仓库列表失败");
            });
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
        _main.SetBusyState(true, $"正在更新仓库 {repo.FullName} ...");
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;
            try { GitHubService.UpdateRepo(repo.FullName, isPrivate, description, out error); }
            catch (Exception ex) { error = ex.Message; }

            RunOnUi(() =>
            {
                if (string.IsNullOrEmpty(error))
                {
                    // 这两个属性带变更通知，改完表格行会实时刷新
                    repo.IsPrivate = isPrivate;
                    repo.Description = description;
                    Message = $"已更新仓库：{repo.FullName}";
                    _main.SetBusyState(false, $"√ 已更新仓库 {repo.FullName}");
                }
                else
                {
                    Message = $"更新失败：{error}";
                    _main.SetBusyState(false, "✗ 更新仓库失败");
                }
            });
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
        _main.SetBusyState(true, $"正在删除仓库 {repo.FullName} ...");
        System.Threading.Tasks.Task.Run(() =>
        {
            string? error = null;
            try { GitHubService.DeleteRepo(repo.FullName, out error); }
            catch (Exception ex) { error = ex.Message; }

            RunOnUi(() =>
            {
                if (string.IsNullOrEmpty(error))
                {
                    Repos.Remove(repo);
                    Message = $"已删除仓库：{repo.FullName}";
                    _main.SetBusyState(false, $"√ 已删除仓库 {repo.FullName}");
                }
                else
                {
                    Message = $"删除失败：{error}";
                    _main.SetBusyState(false, "✗ 删除仓库失败");
                }
            });
        });
    }

    /// <summary>检测当前 token 是否已开通 delete_repo 权限。</summary>
    private void RefreshDeleteScope()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var has = GitHubService.HasDeleteScope();
            RunOnUi(() => HasDeleteScope = has);
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
