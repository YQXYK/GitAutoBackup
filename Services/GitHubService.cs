using System.Text;
using System.Text.Json;

namespace GitAutoBackup.Services;

/// <summary>封装 gh CLI：查询账号、创建私有仓库。</summary>
public static class GitHubService
{
    private static readonly string GhExe = "gh";

    /// <summary>获取当前登录的 GitHub 账号名（gh api user -> login）。失败返回空。</summary>
    public static string GetAccount()
    {
        try
        {
            var r = ProcessRunner.Run(GhExe, "api user", timeoutMs: 30000);
            if (!r.Succeeded) return string.Empty;
            using var doc = JsonDocument.Parse(r.Stdout);
            if (doc.RootElement.TryGetProperty("login", out var login))
                return login.GetString() ?? string.Empty;
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 确保仓库存在（不存在则按 isPrivate 创建）。集中式先建空仓；独立式用 --source 由本地 git 仓库创建并推送。
    /// repoFullName 为 owner/name 形式。
    /// </summary>
    public static bool EnsureRepo(string repoFullName, bool isPrivate, string? sourcePathForPush, out string error)
    {
        error = string.Empty;
        var (owner, name) = SplitRepo(repoFullName);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
        {
            error = "仓库名格式无效，应为 owner/repo";
            return false;
        }

        // 检查是否已存在
        var view = ProcessRunner.Run(GhExe, $"repo view {Arg(repoFullName)} --json name", timeoutMs: 30000);
        if (view.Succeeded)
        {
            return true; // 已存在
        }

        // 创建（可见性由调用方决定）
        var visibility = isPrivate ? "--private" : "--public";
        string createArgs;
        if (!string.IsNullOrEmpty(sourcePathForPush) && GitService.IsRepo(sourcePathForPush))
        {
            // 由本地 git 仓库创建并推送
            createArgs = $"repo create {Arg(repoFullName)} {visibility} --source {Arg(sourcePathForPush)} --push";
        }
        else
        {
            createArgs = $"repo create {Arg(repoFullName)} {visibility}";
        }

        var r = ProcessRunner.Run(GhExe, createArgs, timeoutMs: 120000);
        if (!r.Succeeded)
        {
            error = r.CombinedOutput;
            return false;
        }

        // 新建成功后不再写 README，改由备份流程写入带类型/项目列表的说明
        return true;
    }

    /// <summary>删除 GitHub 上的仓库（gh repo delete --yes）。返回是否成功。</summary>
    public static bool DeleteRepo(string repoFullName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(repoFullName)) { error = "仓库名为空"; return false; }
        var r = ProcessRunner.Run(GhExe, $"repo delete {Arg(repoFullName)} --yes", timeoutMs: 60000);
        if (r.Succeeded) return true;
        error = r.CombinedOutput;
        if (error.Contains("delete_repo", StringComparison.OrdinalIgnoreCase))
            error = "缺少删除仓库权限（delete_repo scope）。请在命令行运行：gh auth refresh -s delete_repo 后重试。\n原始信息：" + error;
        return false;
    }

    /// <summary>修改仓库的可见性与描述（gh repo edit）。返回是否成功。</summary>
    public static bool UpdateRepo(string repoFullName, bool isPrivate, string description, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(repoFullName)) { error = "仓库名为空"; return false; }

        var visibility = isPrivate ? "private" : "public";
        var args = $"repo edit {Arg(repoFullName)} --visibility {visibility} --accept-visibility-change-consequences";
        if (description != null) args += $" --description {Arg(description)}";

        var r = ProcessRunner.Run(GhExe, args, timeoutMs: 60000);
        if (r.Succeeded) return true;
        error = r.CombinedOutput;
        return false;
    }

    /// <summary>列出当前登录账号的仓库（最多 200 个，按最近更新排序）。失败时 error 非空、返回空列表。</summary>
    public static List<GitAutoBackup.Models.GitHubRepo> ListRepos(out string error)
    {
        error = string.Empty;
        var repos = new List<GitAutoBackup.Models.GitHubRepo>();

        var r = ProcessRunner.Run(GhExe,
            "repo list --limit 200 --json name,nameWithOwner,isPrivate,description,updatedAt,diskUsage,primaryLanguage",
            timeoutMs: 60000);
        if (!r.Succeeded) { error = r.CombinedOutput; return repos; }

        try
        {
            using var doc = JsonDocument.Parse(r.Stdout);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var repo = new GitAutoBackup.Models.GitHubRepo
                {
                    FullName = GetString(el, "nameWithOwner"),
                    Name = GetString(el, "name"),
                    IsPrivate = el.TryGetProperty("isPrivate", out var p) && p.ValueKind == JsonValueKind.True,
                    Description = GetString(el, "description"),
                    DiskUsageKb = el.TryGetProperty("diskUsage", out var du) && du.ValueKind == JsonValueKind.Number ? du.GetInt64() : 0,
                    UpdatedAt = DateTime.TryParse(GetString(el, "updatedAt"), out var dt) ? dt.ToLocalTime() : DateTime.MinValue
                };
                if (el.TryGetProperty("primaryLanguage", out var lang) && lang.ValueKind == JsonValueKind.Object
                    && lang.TryGetProperty("name", out var langName))
                    repo.Language = langName.GetString() ?? string.Empty;

                repos.Add(repo);
            }
        }
        catch (Exception ex) { error = ex.Message; }

        return repos;
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;

    /// <summary>当前 gh token 是否已具备 delete_repo 权限（gh auth status 的 scopes 里是否含 delete_repo）。</summary>
    public static bool HasDeleteScope()
    {
        try
        {
            var r = ProcessRunner.Run(GhExe, "auth status", timeoutMs: 30000);
            // gh auth status 的输出在 stderr，CombinedOutput 已合并两者
            return r.CombinedOutput.Contains("delete_repo", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>仓库是否存在（owner/name）。</summary>
    public static bool RepoExists(string repoFullName)
    {
        if (string.IsNullOrWhiteSpace(repoFullName)) return false;
        var r = ProcessRunner.Run(GhExe, $"repo view {Arg(repoFullName)} --json name", timeoutMs: 30000);
        return r.Succeeded;
    }

    /// <summary>
    /// 生成/覆盖仓库的 README.md（不存在则创建，存在则带 SHA 更新）。
    /// 使用 GitHub Contents API（PUT /repos/{owner}/{repo}/contents/README.md），内容 base64 编码。
    /// </summary>
    public static bool WriteBackupReadme(string repoFullName, string markdown, out string error)
    {
        error = string.Empty;
        try
        {
            var sha = GetReadmeSha(repoFullName);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown));
            var args = $"api repos/{Arg(repoFullName)}/contents/README.md -X PUT " +
                       $"-f \"message=GitAutoBackup: 更新备份仓库说明\" -f \"content={base64}\"";
            if (!string.IsNullOrWhiteSpace(sha)) args += $" -f \"sha={sha}\"";

            var r = ProcessRunner.Run(GhExe, args, timeoutMs: 60000);
            if (r.Succeeded) return true;
            error = r.CombinedOutput;
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>读取仓库 README 的 blob SHA（README 不存在时返回空字符串）。</summary>
    private static string GetReadmeSha(string repoFullName)
    {
        var r = ProcessRunner.Run(GhExe, $"api repos/{Arg(repoFullName)}/contents/README.md --jq .sha", timeoutMs: 30000);
        return r.Succeeded ? r.Stdout.Trim().Trim('"') : string.Empty;
    }

    private static (string owner, string name) SplitRepo(string repoFullName)
    {
        var parts = repoFullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return (string.Empty, parts[0]);
        if (parts.Length >= 2) return (parts[^2], parts[^1]);
        return (string.Empty, string.Empty);
    }

    private static string Arg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}