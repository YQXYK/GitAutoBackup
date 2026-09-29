using System.IO;
using System.Text;

namespace GitAutoBackup.Services;

/// <summary>日志级别</summary>
public enum LogLevel
{
    Info,
    Warn,
    Error
}

/// <summary>
/// 日志落盘服务：把程序日志追加写入 %AppData%\GitAutoBackup\logs\yyyy-MM-dd.log。
///
/// 设计要点：
/// - <b>追加写入</b>：一行一条，格式 <c>[HH:mm:ss] [级别] 内容</c>，方便直接翻文件。
/// - <b>按天滚动</b>：每天一个文件，启动时清理超过 <see cref="RetentionDays"/> 天的旧文件。
/// - <b>单文件限长</b>：超过 <see cref="MaxFileBytes"/> 就切分到 <c>yyyy-MM-dd.1.log</c>，避免写爆磁盘。
/// - <b>绝不抛异常</b>：日志本身失败不能影响主流程，所有 IO 异常都被吞掉。
/// </summary>
public static class LogService
{
    /// <summary>日志保留天数，超过则启动时删除。</summary>
    private const int RetentionDays = 30;

    /// <summary>单个日志文件的最大字节数（约 5 MB），超出即切分。</summary>
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>日志目录：%AppData%\GitAutoBackup\logs</summary>
    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitAutoBackup", "logs");

    /// <summary>今天的日志文件完整路径。</summary>
    public static string TodayFile => Path.Combine(LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

    /// <summary>级别标签宽度固定为 5，让各行内容左对齐。</summary>
    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "INFO "
    };

    /// <summary>写入一条日志。失败静默忽略。</summary>
    public static void Write(string message, LogLevel level = LogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                var path = ResolveWritableFile();
                // 多行消息（例如异常堆栈）逐行写，保持格式统一
                var lines = message.Replace("\r\n", "\n").Split('\n');
                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    if (line.Length == 0) continue;
                    sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] [")
                      .Append(Tag(level)).Append("] ").Append(line).Append("\r\n");
                }
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }

    /// <summary>
    /// 取当天可写的文件：若当天主文件超过上限，则往后找一个未超上限的分片
    /// （2016-09-29.1.log、2016-09-29.2.log ...）。
    /// </summary>
    private static string ResolveWritableFile()
    {
        var baseName = DateTime.Now.ToString("yyyy-MM-dd");
        var main = Path.Combine(LogDir, baseName + ".log");
        if (!File.Exists(main) || new FileInfo(main).Length < MaxFileBytes) return main;

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(LogDir, $"{baseName}.{i}.log");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length < MaxFileBytes)
                return candidate;
        }
        return main; // 极端情况兜底
    }

    /// <summary>启动时调用：清理过期日志。失败静默忽略。</summary>
    public static void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(LogDir, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
                catch { /* 单个文件失败不影响其他 */ }
            }
        }
        catch { }
    }

    /// <summary>清空全部日志文件（保留目录）。返回是否成功。</summary>
    public static bool ClearAll()
    {
        try
        {
            lock (Gate)
            {
                if (!Directory.Exists(LogDir)) return true;
                foreach (var file in Directory.GetFiles(LogDir, "*.log"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>日志目录占用空间（字节）。</summary>
    public static long GetTotalSize()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return 0;
            long total = 0;
            foreach (var f in Directory.GetFiles(LogDir, "*.log"))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }
}
