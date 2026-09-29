using System.IO;

namespace GitAutoBackup.Services;

/// <summary>
/// 统一的路径服务：所有用户数据都放在 <c>%AppData%\GitAutoBackup\</c> 下。
///
/// 目录结构：
/// <code>
/// %AppData%\GitAutoBackup\
///   ├── settings.json   配置（仓库名、排除规则、定时、任务列表）
///   ├── token.dat       OAuth Token（DPAPI 加密，仅当前 Windows 用户可解）
///   ├── avatar.png      头像缓存
///   ├── avatar.login    头像归属账号
///   ├── errors.log      崩溃异常日志
///   └── logs\           程序日志（按天滚动）
/// </code>
///
/// 用 Roaming 而非 Local：配置与凭据跟随账号漫游更合理；日志目录同样受益于统一管理。
/// </summary>
public static class PathService
{
    /// <summary>数据根目录：%AppData%\GitAutoBackup</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitAutoBackup");

    /// <summary>确保数据根目录存在并返回它。</summary>
    public static string EnsureDataDir()
    {
        Directory.CreateDirectory(DataDir);
        return DataDir;
    }
}
