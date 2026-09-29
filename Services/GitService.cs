using System.IO;

namespace GitAutoBackup.Services;

/// <summary>封装系统 git 命令的常用操作。</summary>
public static class GitService
{
    private static readonly string GitExe = "git";

    private static string Arg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static ProcessResult Run(string args, string? workDir, int timeoutMs = 300000)
        => ProcessRunner.Run(GitExe, args, workDir, timeoutMs);

    /// <summary>初始化仓库（若目录已含 .git 则忽略）</summary>
    public static ProcessResult Init(string dir) => Run($"init", dir);

    /// <summary>是否已是 git 仓库</summary>
    public static bool IsRepo(string dir)
        => !string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, ".git"));

    /// <summary>设置远程 origin 地址</summary>
    public static ProcessResult SetRemote(string dir, string url)
        => Run($"remote set-url origin {Arg(url)}", dir);

    public static ProcessResult AddRemote(string dir, string url)
        => Run($"remote add origin {Arg(url)}", dir);

    /// <summary>添 加所有变更（默认忽略列表由拷贝阶段处理）</summary>
    public static ProcessResult AddAll(string dir) => Run("add --all", dir);

    /// <summary>从索引移除文件但保留工作区文件（用于跳过超大文件）。文件不存在于索引时不报错。</summary>
    public static ProcessResult RemoveFromIndex(string dir, string relativePath)
        => Run($"rm --cached --ignore-unmatch {Arg(relativePath)}", dir);

    /// <summary>提交</summary>
    public static ProcessResult Commit(string dir, string message)
        => Run($"commit -m {Arg(message)}", dir);

    /// <summary>推送（首次用 -u 建立跟踪）。
    /// 注意：超时会抛 <see cref="TimeoutException"/>（由 ProcessRunner 抛出），调用方需自行处理，
    /// 因为"推送超时"与"推送被拒绝"是完全不同的场景，需要给用户不同的选择。</summary>
    public static ProcessResult Push(string dir, string branch, bool setUpstream = false, int timeoutMs = 300000)
        => Run($"push -u origin {branch}", dir, timeoutMs);

    /// <summary>推送到显式 URL（不修改本地 origin 配置，用于携带 OAuth token 的临时地址）</summary>
    public static ProcessResult PushTo(string dir, string url, string branch, int timeoutMs = 300000)
        => Run($"push {Arg(url)} {branch}", dir, timeoutMs);

    /// <summary>从显式 URL 拉取并变基（不修改本地 origin 配置）</summary>
    public static ProcessResult PullRebaseFrom(string dir, string url, string branch, int timeoutMs = 300000)
        => Run($"pull --rebase {Arg(url)} {branch}", dir, timeoutMs);

    public static ProcessResult CurrentBranch(string dir)
        => Run($"rev-parse --abbrev-ref HEAD", dir);

    public static ProcessResult CheckRemote(string dir)
        => Run($"remote get-url origin", dir);

    public static ProcessResult ConfigUser(string dir, string name, string email)
        => Run($"config user.email {Arg(email)} && git config user.name {Arg(name)}", dir);
}