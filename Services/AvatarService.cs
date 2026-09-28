using System.IO;
using System.Net.Http;

namespace GitAutoBackup.Services;

/// <summary>
/// 头像下载与本地缓存：下载到 <c>%AppData%\GitAutoBackup\avatar.png</c>，
/// 并用 <c>avatar.login</c> 记录该头像属于哪个账号，换账号时自动重新下载。
/// </summary>
public static class AvatarService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitAutoBackup");

    private static readonly string ImagePath = Path.Combine(Dir, "avatar.png");
    private static readonly string LoginPath = Path.Combine(Dir, "avatar.login");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>已缓存的头像文件路径；没有则返回 null。</summary>
    public static string? GetCachedPath() => File.Exists(ImagePath) ? ImagePath : null;

    /// <summary>
    /// 确保头像已缓存（账号变化时重新下载）。返回本地路径；失败返回 null（调用方可回退到默认头像）。
    /// </summary>
    public static string? Ensure(string login, string avatarUrl)
    {
        try
        {
            Directory.CreateDirectory(Dir);

            var cachedLogin = File.Exists(LoginPath) ? File.ReadAllText(LoginPath).Trim() : string.Empty;
            var sameAccount = string.Equals(cachedLogin, login, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(ImagePath) && sameAccount) return ImagePath;
            if (string.IsNullOrWhiteSpace(avatarUrl)) return File.Exists(ImagePath) ? ImagePath : null;

            using var req = new HttpRequestMessage(HttpMethod.Get, avatarUrl);
            req.Headers.UserAgent.ParseAdd("GitAutoBackup");
            using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return File.Exists(ImagePath) ? ImagePath : null;

            var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (bytes.Length == 0) return File.Exists(ImagePath) ? ImagePath : null;

            File.WriteAllBytes(ImagePath, bytes);
            File.WriteAllText(LoginPath, login);
            return ImagePath;
        }
        catch { return File.Exists(ImagePath) ? ImagePath : null; }
    }

    /// <summary>清除缓存的头像（退出登录时调用）。</summary>
    public static void Clear()
    {
        try { if (File.Exists(ImagePath)) File.Delete(ImagePath); } catch { /* 忽略 */ }
        try { if (File.Exists(LoginPath)) File.Delete(LoginPath); } catch { /* 忽略 */ }
    }
}
