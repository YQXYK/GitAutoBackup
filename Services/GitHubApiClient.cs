using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GitAutoBackup.Models;

namespace GitAutoBackup.Services;

/// <summary>
/// GitHub REST API 客户端（用 OAuth token 直连，用于替代 gh CLI）。
/// 所有方法同步、失败时通过 error 带出信息；调用方应在后台线程调用。
/// </summary>
public static class GitHubApiClient
{
    private const string ApiBase = "https://api.github.com";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static HttpRequestMessage Req(HttpMethod method, string url, string token, string? jsonBody = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.UserAgent.ParseAdd("GitAutoBackup");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        return req;
    }

    /// <summary>当前 token 对应的登录名。失败返回空字符串。</summary>
    public static string GetLogin(string token, out string error)
    {
        error = string.Empty;
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/user", token)).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) { error = $"HTTP {(int)resp.StatusCode}：{body}"; return string.Empty; }
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty;
        }
        catch (Exception ex) { error = ex.Message; return string.Empty; }
    }

    /// <summary>获取当前用户信息（login + 头像地址）。失败返回 null。</summary>
    public static (string Login, string AvatarUrl)? GetUserProfile(string token, out string error)
    {
        error = string.Empty;
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/user", token)).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) { error = $"HTTP {(int)resp.StatusCode}：{body}"; return null; }
            using var doc = JsonDocument.Parse(body);
            return (Str(doc.RootElement, "login"), Str(doc.RootElement, "avatar_url"));
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>仓库是否存在。</summary>
    public static bool RepoExists(string token, string fullName)
    {
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/repos/{fullName}", token)).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>列出当前账号的仓库（最多 200 个，按最近更新排序）。</summary>
    public static List<GitHubRepo> ListRepos(string token, out string error)
    {
        error = string.Empty;
        var list = new List<GitHubRepo>();
        try
        {
            for (var page = 1; page <= 2; page++)
            {
                var url = $"{ApiBase}/user/repos?per_page=100&sort=updated&affiliation=owner&page={page}";
                using var resp = Http.SendAsync(Req(HttpMethod.Get, url, token)).GetAwaiter().GetResult();
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) { error = $"HTTP {(int)resp.StatusCode}：{body}"; return list; }

                using var doc = JsonDocument.Parse(body);
                var arr = doc.RootElement;
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) break;

                foreach (var el in arr.EnumerateArray())
                {
                    list.Add(new GitHubRepo
                    {
                        FullName = Str(el, "full_name"),
                        Name = Str(el, "name"),
                        IsPrivate = el.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True,
                        Description = Str(el, "description"),
                        Language = Str(el, "language"),
                        DiskUsageKb = el.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0,
                        UpdatedAt = DateTime.TryParse(Str(el, "updated_at"), out var dt) ? dt.ToLocalTime() : DateTime.MinValue,
                    });
                }
            }
        }
        catch (Exception ex) { error = ex.Message; }
        return list;
    }

    /// <summary>确保仓库存在（不存在则按 isPrivate 创建）。auto_init=true 便于随后 push。</summary>
    public static bool EnsureRepo(string token, string name, bool isPrivate, out string error)
    {
        error = string.Empty;
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                name,
                @private = isPrivate,
                auto_init = false,
                description = "由 GitAutoBackup 创建的备份仓库",
            });
            using var resp = Http.SendAsync(Req(HttpMethod.Post, $"{ApiBase}/user/repos", token, json)).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode) return true;

            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return true; // 已存在视为成功
            error = $"HTTP {(int)resp.StatusCode}：{body}";
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>删除仓库。</summary>
    public static bool DeleteRepo(string token, string fullName, out string error)
    {
        error = string.Empty;
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Delete, $"{ApiBase}/repos/{fullName}", token)).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode) return true;

            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var code = (int)resp.StatusCode;
            error = code == 403
                ? "缺少删除仓库权限（delete_repo）。请点「解锁删除权限」授权后重试。\n" + body
                : $"HTTP {code}：{body}";
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>修改仓库可见性与描述。</summary>
    public static bool UpdateRepo(string token, string fullName, bool isPrivate, string description, out string error)
    {
        error = string.Empty;
        try
        {
            var json = JsonSerializer.Serialize(new { @private = isPrivate, description });
            using var resp = Http.SendAsync(Req(HttpMethod.Patch, $"{ApiBase}/repos/{fullName}", token, json)).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode) return true;
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            error = $"HTTP {(int)resp.StatusCode}：{body}";
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>生成/覆盖 README.md（存在则带 SHA 更新，不存在则创建）。</summary>
    public static bool WriteReadme(string token, string fullName, string markdown, out string error)
    {
        error = string.Empty;
        try
        {
            var sha = GetReadmeSha(token, fullName);
            var payload = new Dictionary<string, object>
            {
                ["message"] = "GitAutoBackup: 更新备份仓库说明",
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown)),
            };
            if (!string.IsNullOrWhiteSpace(sha)) payload["sha"] = sha;

            var json = JsonSerializer.Serialize(payload);
            using var resp = Http.SendAsync(Req(HttpMethod.Put, $"{ApiBase}/repos/{fullName}/contents/README.md", token, json)).GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode) return true;
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            error = $"HTTP {(int)resp.StatusCode}：{body}";
            return false;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static string GetReadmeSha(string token, string fullName)
    {
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/repos/{fullName}/contents/README.md", token)).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return string.Empty;
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("sha", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>当前 token 是否具备 delete_repo 权限（读取响应头 X-OAuth-Scopes）。</summary>
    public static bool HasDeleteScope(string token)
    {
        try
        {
            using var resp = Http.SendAsync(Req(HttpMethod.Get, $"{ApiBase}/user", token)).GetAwaiter().GetResult();
            if (resp.Headers.TryGetValues("X-OAuth-Scopes", out var vals))
                return string.Join(",", vals).Contains("delete_repo", StringComparison.OrdinalIgnoreCase);
            return false;
        }
        catch { return false; }
    }

    private static string Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
}
