using System.Text;
using System.Text.Json;

namespace GitAutoBackup.Services;

/// <summary>
/// GitHub 操作统一入口：**优先使用 OAuth token 直连 REST API**（用户内置登录，无需装 gh）；
/// 未登录（无 token）时自动回退到本机 gh CLI，两种方式都能用。
/// </summary>
public static class GitHubService
{
    private static readonly string GhExe = "gh";

    /// <summary>是否走 OAuth/API 路径（有本地 token）。</summary>
    private static bool TryGetToken(out string token)
    {
        token = GitHubAuthService.LoadToken() ?? string.Empty;
        return token.Length > 0;
    }

    /// <summary>当前使用的后端名称（供界面提示）。</summary>
    public static string BackendName => GitHubAuthService.HasToken ? "内置登录（OAuth）" : "gh CLI";

    /// <summary>获取当前登录的 GitHub 账号名。失败返回空。</summary>
    public static string GetAccount()
    {
        if (TryGetToken(out var token))
        {
            var login = GitHubApiClient.GetLogin(token, out _);
            if (!string.IsNullOrEmpty(login)) return login;
        }

        try
        {
            var r = ProcessRunner.Run(GhExe, "api user", timeoutMs: 30000);
            if (!r.Succeeded) return string.Empty;
            using var doc = JsonDocument.Parse(r.Stdout);
            return doc.RootElement.TryGetProperty("login", out var login2) ? login2.GetString() ?? string.Empty : string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>获取当前用户的登录名与头像地址。失败返回 null。</summary>
    public static (string Login, string AvatarUrl)? GetUserProfile()
    {
        if (TryGetToken(out var token))
            return GitHubApiClient.GetUserProfile(token, out _);

        try
        {
            var r = ProcessRunner.Run(GhExe, "api user", timeoutMs: 30000);
            if (!r.Succeeded) return null;
            using var doc = JsonDocument.Parse(r.Stdout);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("avatar_url", out var a) ? a.GetString() ?? string.Empty : string.Empty);
        }
        catch { return null; }
    }

    /// <summary>
    /// 确保仓库存在（不存在则按 isPrivate 创建）。repoFullName 为 owner/name。
    /// API 路径只创建空仓库，推送由备份流程的 EnsureRemote + push 完成。
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

        // ---------- OAuth / REST API ----------
        if (TryGetToken(out var token))
        {
            if (GitHubApiClient.RepoExists(token, repoFullName)) return true;
            return GitHubApiClient.EnsureRepo(token, name, isPrivate, out error);
        }

        // ---------- gh CLI 回退 ----------
        var view = ProcessRunner.Run(GhExe, $"repo view {Arg(repoFullName)} --json name", timeoutMs: 30000);
        if (view.Succeeded) return true;

        var visibility = isPrivate ? "--private" : "--public";
        string createArgs;
        if (!string.IsNullOrEmpty(sourcePathForPush) && GitService.IsRepo(sourcePathForPush))
            createArgs = $"repo create {Arg(repoFullName)} {visibility} --source {Arg(sourcePathForPush)} --push";
        else
            createArgs = $"repo create {Arg(repoFullName)} {visibility}";

        var r = ProcessRunner.Run(GhExe, createArgs, timeoutMs: 120000);
        if (r.Succeeded) return true;
        error = r.CombinedOutput;
        return false;
    }

    /// <summary>删除 GitHub 上的仓库。</summary>
    public static bool DeleteRepo(string repoFullName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(repoFullName)) { error = "仓库名为空"; return false; }

        if (TryGetToken(out var token))
            return GitHubApiClient.DeleteRepo(token, repoFullName, out error);

        var r = ProcessRunner.Run(GhExe, $"repo delete {Arg(repoFullName)} --yes", timeoutMs: 60000);
        if (r.Succeeded) return true;
        error = r.CombinedOutput;
        if (error.Contains("delete_repo", StringComparison.OrdinalIgnoreCase))
            error = "缺少删除仓库权限（delete_repo）。请在「GitHub 仓库」页点「解锁删除权限」后重试。\n原始信息：" + error;
        return false;
    }

    /// <summary>修改仓库的可见性与描述。</summary>
    public static bool UpdateRepo(string repoFullName, bool isPrivate, string description, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(repoFullName)) { error = "仓库名为空"; return false; }

        if (TryGetToken(out var token))
            return GitHubApiClient.UpdateRepo(token, repoFullName, isPrivate, description, out error);

        var visibility = isPrivate ? "private" : "public";
        var args = $"repo edit {Arg(repoFullName)} --visibility {visibility} --accept-visibility-change-consequences";
        if (description != null) args += $" --description {Arg(description)}";

        var r = ProcessRunner.Run(GhExe, args, timeoutMs: 60000);
        if (r.Succeeded) return true;
        error = r.CombinedOutput;
        return false;
    }

    /// <summary>列出当前账号的仓库（最多 200 个，按最近更新排序）。</summary>
    public static List<GitAutoBackup.Models.GitHubRepo> ListRepos(out string error)
    {
        if (TryGetToken(out var token))
            return GitHubApiClient.ListRepos(token, out error);

        return ListReposViaGh(out error);
    }

    private static List<GitAutoBackup.Models.GitHubRepo> ListReposViaGh(out string error)
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

    /// <summary>当前凭据是否已具备 delete_repo 权限。</summary>
    public static bool HasDeleteScope()
    {
        if (TryGetToken(out var token))
            return GitHubApiClient.HasDeleteScope(token);

        try
        {
            var r = ProcessRunner.Run(GhExe, "auth status", timeoutMs: 30000);
            // gh auth status 输出在 stderr，CombinedOutput 已合并
            return r.CombinedOutput.Contains("delete_repo", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>仓库是否存在（owner/name）。</summary>
    public static bool RepoExists(string repoFullName)
    {
        if (string.IsNullOrWhiteSpace(repoFullName)) return false;

        if (TryGetToken(out var token))
            return GitHubApiClient.RepoExists(token, repoFullName);

        var r = ProcessRunner.Run(GhExe, $"repo view {Arg(repoFullName)} --json name", timeoutMs: 30000);
        return r.Succeeded;
    }

    /// <summary>生成/覆盖仓库的 README.md（不存在则创建，存在则带 SHA 更新）。</summary>
    public static bool WriteBackupReadme(string repoFullName, string markdown, out string error)
    {
        error = string.Empty;

        if (TryGetToken(out var token))
            return GitHubApiClient.WriteReadme(token, repoFullName, markdown, out error);

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
