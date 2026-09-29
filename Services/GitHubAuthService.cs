using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitAutoBackup.Services;

/// <summary>设备码流程返回的信息。</summary>
public class DeviceCodeInfo
{
    public string DeviceCode { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = "https://github.com/login/device";
    public int Interval { get; set; } = 5;
    public int ExpiresIn { get; set; } = 900;
}

/// <summary>
/// GitHub OAuth 登录（Device Flow）+ token 本地安全存储。不依赖 gh CLI：
/// 用户点「登录」→ 显示设备码 → 浏览器授权 → 程序拿到 token（DPAPI 加密存本地）。
/// Client ID 属公开信息（Device Flow 不使用 client_secret）。
/// </summary>
public static class GitHubAuthService
{
    /// <summary>GitAutoBackup 的 OAuth App Client ID（公开信息）。</summary>
    public const string ClientId = "Ov23liUZIkNAo9yGXdTE";

    /// <summary>基础权限范围（删除仓库时再增量申请 delete_repo）。</summary>
    public const string DefaultScope = "repo read:org";

    /// <summary>获取删除仓库权限时追加的范围。</summary>
    public const string DeleteScope = "repo read:org delete_repo";

    private static readonly string TokenFile = Path.Combine(PathService.DataDir, "token.dat");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // ------------- token 存储（DPAPI，仅当前 Windows 用户可解密） -------------

    /// <summary>读取已保存的 token；不存在或解密失败返回 null。</summary>
    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenFile)) return null;
            var encrypted = File.ReadAllBytes(TokenFile);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch { return null; }
    }

    public static void SaveToken(string token)
    {
        PathService.EnsureDataDir();
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenFile, encrypted);
    }

    public static void ClearToken()
    {
        try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { /* 忽略 */ }
    }

    public static bool HasToken => LoadToken() != null;

    // ------------- 设备码流程 -------------

    /// <summary>
    /// 以表单方式 POST 并**强制要求 JSON 响应**。
    /// 注意：GitHub 的 OAuth 端点默认返回 form-urlencoded（如 device_code=xxx&amp;user_code=yyy），
    /// 不加 Accept: application/json 会导致 JSON 解析失败。
    /// </summary>
    private static (bool ok, string body, int status) PostForm(string url, Dictionary<string, string> fields)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.UserAgent.ParseAdd("GitAutoBackup");
        using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    /// <summary>第一步：申请设备码。失败返回 null 并带出错误。</summary>
    public static DeviceCodeInfo? RequestDeviceCode(string scope, out string error)
    {
        error = string.Empty;
        try
        {
            var (ok, body, status) = PostForm("https://github.com/login/device/code", new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = scope,
            });
            if (!ok) { error = $"HTTP {status}：{body}"; return null; }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new DeviceCodeInfo
            {
                DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
                UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
                VerificationUri = root.TryGetProperty("verification_uri", out var v) && v.GetString() is { Length: > 0 } uri
                    ? uri : "https://github.com/login/device",
                Interval = root.TryGetProperty("interval", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 5,
                ExpiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 900,
            };
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>第二步：按 interval 轮询换取 access_token。成功返回 token，否则 null。</summary>
    public static string? PollForToken(DeviceCodeInfo info, CancellationToken ct, out string error)
    {
        error = string.Empty;
        var interval = Math.Max(info.Interval, 5);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(info.ExpiresIn, 60));

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                Task.Delay(TimeSpan.FromSeconds(interval), ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { error = "已取消"; return null; }

            if (ct.IsCancellationRequested) { error = "已取消"; return null; }

            try
            {
                var (ok, body, _) = PostForm("https://github.com/login/oauth/access_token", new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = info.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                });
                if (!ok) continue;
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("access_token", out var at) && at.GetString() is { Length: > 0 } token)
                    return token;

                var err = root.TryGetProperty("error", out var er) ? er.GetString() : null;
                switch (err)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += 5;
                        continue;
                    case "expired_token":
                        error = "设备码已过期，请重新登录";
                        return null;
                    case "access_denied":
                        error = "已拒绝授权";
                        return null;
                    default:
                        if (!string.IsNullOrEmpty(err)) { error = err; return null; }
                        continue;
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        error = "授权超时，请重试";
        return null;
    }
}
