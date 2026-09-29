using System.IO;

namespace GitAutoBackup.Services;

/// <summary>一个超限文件（RelativePath 相对源目录）。</summary>
public sealed record LargeFile(string RelativePath, long Size)
{
    /// <summary>便于列表展示的大小文本。</summary>
    public string SizeText => FormatSize(Size);

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB",
        >= 1024L * 1024 => (bytes / 1024.0 / 1024).ToString("0.0") + " MB",
        >= 1024 => (bytes / 1024.0).ToString("0.0") + " KB",
        _ => bytes + " B"
    };
}

/// <summary>一次目录扫描的结果。</summary>
public sealed class SizeScanResult
{
    /// <summary>计入备份的文件总数（已应用排除规则）。</summary>
    public int TotalFiles { get; init; }

    /// <summary>计入备份的总字节数（已应用排除规则）。</summary>
    public long TotalBytes { get; init; }

    /// <summary>会被 GitHub 直接拒绝的文件（≥ 100 MiB），必须处理。</summary>
    public List<LargeFile> BlockingFiles { get; init; } = new();

    /// <summary>会触发 GitHub 警告、且显著拖大仓库的文件（≥ 50 MiB）。</summary>
    public List<LargeFile> WarningFiles { get; init; } = new();

    /// <summary>最大的若干个文件，用于向用户展示"是谁占的空间"。</summary>
    public List<LargeFile> Largest { get; init; } = new();

    /// <summary>扫描中因权限等原因跳过的条目数。</summary>
    public int SkippedEntries { get; init; }

    public string TotalSizeText => LargeFile.FormatSize(TotalBytes);
    public bool HasBlocking => BlockingFiles.Count > 0;
    public bool HasWarning => WarningFiles.Count > 0;
}

/// <summary>
/// 备份前的体积检查：GitHub 对单个文件与仓库总量有硬性限制，
/// 超限会让 push 直接失败（或让仓库被官方要求整改），所以必须提前发现。
///
/// 官方限制（2026-09 核对）：
/// - 单个文件 &gt; 50 MiB：push 有警告，仍可推上去，但会让仓库显著膨胀；
/// - 单个文件 &gt; 100 MiB：**push 被服务端直接拒绝**（file is too large）；
/// - 仓库总量：建议 &lt; 1 GB，强烈建议 &lt; 5 GB；
/// - 单次 push 总量：2 GB 硬限制。
/// </summary>
public static class BackupSizeChecker
{
    /// <summary>GitHub 单文件警告线：50 MiB。</summary>
    public const long WarnFileBytes = 50L * 1024 * 1024;

    /// <summary>GitHub 单文件硬上限：100 MiB，超过必然推送失败。</summary>
    public const long BlockFileBytes = 100L * 1024 * 1024;

    /// <summary>仓库总量建议上限：1 GB。</summary>
    public const long WarnRepoBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>仓库总量硬上限（近似）：5 GB。</summary>
    public const long BlockRepoBytes = 5L * 1024 * 1024 * 1024;

    /// <summary>"最大的文件"列表保留条数。</summary>
    private const int LargestCount = 8;

    /// <summary>
    /// 扫描目录，统计备份体积并找出超限文件。
    /// </summary>
    /// <param name="sourceDir">要扫描的源目录。</param>
    /// <param name="nameExcluded">按名称判断是否排除（自定义规则）；硬编码排除目录始终生效。</param>
    /// <param name="progress">可选：回报进度（已扫描文件数）。</param>
    public static SizeScanResult Scan(string sourceDir, Func<string, bool>? nameExcluded = null,
                                      Action<int>? progress = null)
    {
        var blocking = new List<LargeFile>();
        var warning = new List<LargeFile>();
        var largest = new List<LargeFile>();
        long total = 0;
        int files = 0;
        int skipped = 0;

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            return new SizeScanResult();
        }

        var root = Path.GetFullPath(sourceDir);

        void Walk(string dir)
        {
            // 目录枚举本身可能因权限失败
            string[] subDirs, dirFiles;
            try
            {
                subDirs = Directory.GetDirectories(dir);
                dirFiles = Directory.GetFiles(dir);
            }
            catch
            {
                skipped++;
                return;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                // 硬编码排除（.git / node_modules / bin ...）与自定义规则
                if (BackupService.ExcludedDirNames.Contains(name)) continue;
                if (nameExcluded?.Invoke(name) == true) continue;

                // 跳过符号链接/junction，避免无限递归
                try
                {
                    if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { skipped++; continue; }

                Walk(sub);
            }

            foreach (var file in dirFiles)
            {
                var name = Path.GetFileName(file);
                if (nameExcluded?.Invoke(name) == true) continue;

                long size;
                try { size = new FileInfo(file).Length; }
                catch { skipped++; continue; }

                total += size;
                files++;
                if (progress != null && files % 500 == 0) progress(files);

                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var item = new LargeFile(rel, size);

                if (size >= BlockFileBytes) blocking.Add(item);
                else if (size >= WarnFileBytes) warning.Add(item);

                // 维护"最大的 N 个"
                if (largest.Count < LargestCount)
                {
                    largest.Add(item);
                    largest.Sort((a, b) => b.Size.CompareTo(a.Size));
                }
                else if (size > largest[^1].Size)
                {
                    largest[^1] = item;
                    largest.Sort((a, b) => b.Size.CompareTo(a.Size));
                }
            }
        }

        Walk(root);
        progress?.Invoke(files);

        blocking.Sort((a, b) => b.Size.CompareTo(a.Size));
        warning.Sort((a, b) => b.Size.CompareTo(a.Size));

        return new SizeScanResult
        {
            TotalFiles = files,
            TotalBytes = total,
            BlockingFiles = blocking,
            WarningFiles = warning,
            Largest = largest,
            SkippedEntries = skipped
        };
    }

    /// <summary>统计一个目录的总字节数（含 .git），用于估算已有仓库体积。失败返回 0。</summary>
    public static long MeasureDirectory(string dir)    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return 0;
        long total = 0;

        void Walk(string d)
        {
            try
            {
                foreach (var f in Directory.GetFiles(d))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (var sub in Directory.GetDirectories(d))
                {
                    try
                    {
                        if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    Walk(sub);
                }
            }
            catch { }
        }

        Walk(dir);
        return total;
    }
}

/// <summary>超大文件确认请求（供界面层弹窗展示）。</summary>
public sealed record LargeFileRequest(string ProjectName, SizeScanResult Scan);
