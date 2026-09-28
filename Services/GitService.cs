using System.IO;

namespace GitAutoBackup.Services;

/// <summary>封装系统 git 命令的常用操作。</summary>
public static class GitService
{
    private static readonly string GitExe = "git";

    private static string Arg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static ProcessResult Run(string args, string? workDir) => ProcessRunner.Run(GitExe, args, workDir);

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

    /// <summary>提交</summary>
    public static ProcessResult Commit(string dir, string message)
        => Run($"commit -m {Arg(message)}", dir);

    /// <summary>推送（首次用 -u 建立跟踪）</summary>
    public static ProcessResult Push(string dir, string branch, bool setUpstream = false)
        => Run($"push -u origin {branch}", dir);

    public static ProcessResult CurrentBranch(string dir)
        => Run($"rev-parse --abbrev-ref HEAD", dir);

    public static ProcessResult CheckRemote(string dir)
        => Run($"remote get-url origin", dir);

    public static ProcessResult ConfigUser(string dir, string name, string email)
        => Run($"config user.email {Arg(email)} && git config user.name {Arg(name)}", dir);
}