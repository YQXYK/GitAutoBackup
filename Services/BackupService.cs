using GitAutoBackup.Models;
using System.IO;

namespace GitAutoBackup.Services;

/// <summary>备份核心编排：方式A（集中式）与方式B（独立式）。</summary>
public class BackupService
{
    /// <summary>内置排除的文件夹名（永久生效，如 .git 不备份版本历史）</summary>
    public static readonly string[] ExcludedDirNames =
        { ".git", "node_modules", "bin", "obj", "Debug", "Release", ".vs", ".idea", "$RECYCLE.BIN" };

    public event Action<string>? Log;
    /// <summary>实时阶段状态（如"复制中…"、"正在推送…"），供 UI 状态栏显示。</summary>
    public event Action<string>? StageChanged;

    /// <summary>
    /// 推送失败时向界面请求决定：传入原始错误信息，返回 true=重试，false=取消。
    /// 由界面层弹窗让用户选择；为 null 时自动重试有限次数。
    /// </summary>
    public Func<string, bool>? RetryPrompt;

    /// <summary>推送失败时的自动重试次数（如无法弹窗或用户选择重试时的兜底）。</summary>
    public const int MaxPushRetries = 3;

    private void Emit(string msg) => Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {MaskToken(msg)}");
    private void Stage(string msg) => StageChanged?.Invoke(msg);

    /// <summary>日志脱敏：内置登录的 token 可能出现在 git 输出（如推送地址）里，统一替换掉。</summary>
    private static string MaskToken(string text)
    {
        var token = GitHubAuthService.LoadToken();
        return string.IsNullOrEmpty(token) ? text : text.Replace(token, "***");
    }

    /// <summary>执行一个备份任务，更新其 LastStatus/LastBackupAt。返回是否成功。</summary>
    public bool RunJob(BackupJob job, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(job.SourcePath) || !Directory.Exists(job.SourcePath))
        {
            job.LastStatus = "失败：源路径不存在";
            Emit($"✗ [{job.ProjectName}] {job.LastStatus}: {job.SourcePath}");
            return false;
        }
        return job.Mode == BackupMode.Standalone
            ? RunStandalone(job, settings)
            : RunConsolidated(job, settings);
    }

    // ---------- 方式A：集中式 ----------
    private bool RunConsolidated(BackupJob job, Settings settings)
    {
        Emit($"=== 开始集中式备份：{job.ProjectName} ===");
        try
        {
            // 归属的集中仓库名：优先用任务自带的，否则回退默认仓库名（兼容旧数据）
            var repoName = SanitizeName(
                string.IsNullOrWhiteSpace(job.ConsolidatedRepoName)
                    ? settings.ConsolidatedRepoName
                    : job.ConsolidatedRepoName);
            if (string.IsNullOrWhiteSpace(repoName))
            {
                job.LastStatus = "未设置集中仓库名";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            // 备份根目录按仓库名分目录：<备份根>\<仓库名> 为一个独立 git 仓库
            var baseRoot = ResolveBackupRoot(settings);
            if (string.IsNullOrWhiteSpace(baseRoot))
            {
                job.LastStatus = "未设置备份根目录";
                Emit($"✗ {job.LastStatus}");
                return false;
            }
            var root = Path.Combine(baseRoot, repoName);
            Directory.CreateDirectory(root);
            if (!GitService.IsRepo(root)) { GitService.Init(root); }
            EnsureGitIgnore(root);

            // 复制源项目到备份根目录（排除 .git / node_modules 等）
            var dest = Path.Combine(root, job.ProjectName);
            var samePath = string.Equals(
                Path.GetFullPath(dest).TrimEnd('\\'),
                Path.GetFullPath(job.SourcePath).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            if (samePath)
            {
                // 源项目位于备份根目录内：一个 git 仓库内不能嵌另一个带 .git 的项目，会造成嵌套仓库而无法提交内容
                job.LastStatus = "配置错误：备份根目录包含了要备份的项目，请将备份根目录设置为独立的空文件夹";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Stage($"正在复制 {job.ProjectName}...");
            Emit($"→ 复制 {job.ProjectName} 中...");
            CopyDirectory(job.SourcePath, dest, BuildNameExcluded(job, settings));
            Emit($"√ 已复制（已排除 .git / node_modules / 自定义规则等）");

            Stage("正在提交到 Git...");
            GitService.AddAll(root);
            var commit = GitService.Commit(root, $"[GitAutoBackup] {job.ProjectName} {DateTime.Now:yyyy-MM-dd HH:mm}");
            if (!commit.Succeeded)
                Emit($"ℹ 提交未产生新内容（{commit.CombinedOutput}）");

            // 3. 统一私有仓库：推送到 owner/{仓库名}
            var repoFull = $"{settings.GitHubAccount}/{repoName}";
            if (string.IsNullOrWhiteSpace(settings.GitHubAccount))
            {
                job.LastStatus = "无法获取 GitHub 账号";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            // 可见性：查该集中仓库的设置（缺省私有）
            var isPrivate = !settings.ConsolidatedRepoPrivates.TryGetValue(repoName, out var priv) || priv;
            if (!GitHubService.EnsureRepo(repoFull, isPrivate, null, out var repoErr))
            {
                job.LastStatus = $"仓库创建/校验失败：{repoErr}";
                Emit($"✗ {job.LastStatus}");
                return false;
            }
            EnsureRemote(root, repoFull);

            var branch = CurrentBranchOrMain(root);
            Stage($"正在推送到 GitHub（{repoFull}），网络可能需要几十秒，请稍候...");
            Emit($"→ 正在推送到 {repoFull}，网络可能需要几十秒，请稍候...");
            if (!PushWithRetry(root, branch, repoFull))
            {
                job.LastStatus = $"推送失败（已重试/取消）：{repoFull}";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            job.LastStatus = "成功";
            job.LastBackupAt = DateTime.Now;

            // 更新仓库 README（含类型与项目列表）
            TryWriteConsolidatedReadme(repoFull, repoName, settings);

            Stage("备份完成");
            Emit($"√ 集中式备份完成：{job.ProjectName} → {repoFull}");
            return true;
        }
        catch (Exception ex)
        {
            job.LastStatus = $"失败：{ex.Message}";
            Emit($"✗ {job.LastStatus}");
            return false;
        }
    }

    // ---------- 方式B：独立式 ----------
    private bool RunStandalone(BackupJob job, Settings settings)
    {
        Emit($"=== 开始独立式备份：{job.ProjectName} ===");
        try
        {
            var src = job.SourcePath;
            if (!GitService.IsRepo(src)) GitService.Init(src);
            EnsureGitIdentity(src);
            AppendExcludesToGitIgnore(src, job, settings);

            // 清理本地 .git 不必要不删除；但对大目录可走配置，此处直接 add（依赖 .gitignore 过滤 build 产物）
            var add = GitService.AddAll(src);
            if (!add.Succeeded)
            {
                job.LastStatus = $"add 失败：{add.CombinedOutput}";
                Emit($"✗ {job.LastStatus}");
                return false;
            }
            var commit = GitService.Commit(src, $"[GitAutoBackup] {DateTime.Now:yyyy-MM-dd HH:mm}");
            if (!commit.Succeeded)
                Emit($"ℹ 提交未产生新内容（{commit.CombinedOutput}）");

            // 仓库名：若用户填了 owner/name 则用；否则用当前账号/项目名
            var repoFull = ResolveStandaloneRepoFullName(job, settings);
            if (string.IsNullOrWhiteSpace(repoFull))
            {
                job.LastStatus = "无法获取 GitHub 账号，请登录 gh";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            // 若本地已有远程，则由 gh 仅做校验；否则让 gh 由本地仓库创建并推送
            var sourceForPush = GitService.CheckRemote(src).Succeeded ? null : src;
            if (!GitHubService.EnsureRepo(repoFull, job.IsPrivate, sourceForPush, out var repoErr))
            {
                job.LastStatus = $"仓库创建/校验失败：{repoErr}";
                Emit($"✗ {job.LastStatus}");
                return false;
            }
            EnsureRemote(src, repoFull);

            var branch = CurrentBranchOrMain(src);
            if (!PushWithRetry(src, branch, repoFull))
            {
                job.LastStatus = $"推送失败（已重试/取消）：{repoFull}";
                Emit($"✗ {job.LastStatus}");
                return false;
            }

            job.LastStatus = "成功";
            job.LastBackupAt = DateTime.Now;

            // 更新仓库 README（含类型与项目信息）
            try { GitHubService.WriteBackupReadme(repoFull, BuildStandaloneReadme(job), out _); } catch { }

            Emit($"√ 独立式备份完成：{job.ProjectName} → {repoFull}");
            return true;
        }
        catch (Exception ex)
        {
            job.LastStatus = $"失败：{ex.Message}";
            Emit($"✗ {job.LastStatus}");
            return false;
        }
    }

    /// <summary>计算独立式任务对应的 GitHub 仓库全名(owner/repo)。</summary>
    public static string ResolveStandaloneRepoFullName(BackupJob job, Settings settings)
    {
        var owner = settings.GitHubAccount;
        if (string.IsNullOrWhiteSpace(owner)) return string.Empty;
        var name = job.RepoName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = job.ProjectName;
        name = name.Trim('/', '\\');
        if (name.Contains('/'))
        {
            // 用户提供了 owner/name，用用户给的
            return name.Replace('\\', '/').Trim('/');
        }
        return $"{owner}/{name}";
    }

    /// <summary>
    /// 构造远程地址：内置登录（OAuth）时携带 token，确保 git push 使用与建仓相同的账号；
    /// 未登录时返回匿名 HTTPS 地址（由 git 自身凭据配置决定）。
    /// </summary>
    private static string BuildRemoteUrl(string repoFull)
    {
        var token = GitHubAuthService.LoadToken();
        return string.IsNullOrEmpty(token)
            ? $"https://github.com/{repoFull}.git"
            : $"https://x-access-token:{token}@github.com/{repoFull}.git";
    }

    private void EnsureRemote(string dir, string repoFull)
    {
        // origin 保持匿名地址，避免把 token 写进本地 .git/config
        // （独立式是用户自己的项目仓库，写 token 有泄露风险；push 时用临时地址即可）
        var url = $"https://github.com/{repoFull}.git";
        if (GitService.CheckRemote(dir).Succeeded)
            GitService.SetRemote(dir, url);
        else
            GitService.AddRemote(dir, url);
    }

    private static string CurrentBranchOrMain(string dir)
    {
        var b = GitService.CurrentBranch(dir).Stdout.Trim();
        return string.IsNullOrWhiteSpace(b) ? "main" : b;
    }

    /// <summary>合并全局 + 项目自定义排除规则，返回按名称判断"是否应排除"的委托。</summary>
    private static Func<string, bool> BuildNameExcluded(BackupJob job, Settings settings)
    {
        var dirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(IEnumerable<ExcludeRule>? rules)
        {
            if (rules == null) return;
            foreach (var r in rules)
            {
                if (string.IsNullOrWhiteSpace(r.Name)) continue;
                var n = r.Name.Trim();
                if (r.Type == ExcludeType.Folder) dirNames.Add(n);
                else fileNames.Add(n);
            }
        }

        Collect(settings?.GlobalExcludes);
        Collect(job.JobExcludes);

        return name =>
        {
            if (fileNames.Contains(name)) return true;
            if (dirNames.Contains(name)) return true;
            return false;
        };
    }

    private void EnsureGitIdentity(string dir)
    {
        // 若本地 git 未配置 user，则设置占位身份（提交需要）
        var email = ProcessRunner.Run("git", "config user.email", dir);
        if (email.Succeeded && !string.IsNullOrWhiteSpace(email.Stdout)) return;

        var who = Environment.UserName ?? "git-bot";
        ProcessRunner.Run("git", $"config user.email \"{who}@local\"", dir);
        ProcessRunner.Run("git", $"config user.name \"{who}\"", dir);
    }

    /// <summary>
    /// 推送并支持失败处理：
    /// - 若为历史分叉（rejected / fetch first）：自动 git pull --rebase 合并后重试，不弹窗。
    /// - 若为网络类错误：通过 RetryPrompt 弹窗询问用户「重试/取消」。
    /// 返回 true 表示最终推送成功；false 表示用户取消或多次失败。
    /// </summary>
    private bool PushWithRetry(string dir, string branch, string repoFull)
    {
        // 内置登录（OAuth）时用带 token 的临时地址推送，确保 push 与建仓使用同一账号；
        // 否则 git 会走本地 credential helper，可能是另一个 gh 账号，导致 403。
        // 该地址只在命令行使用，**不写入本地 git 配置**。
        var pushUrl = BuildRemoteUrl(repoFull);
        var authed = pushUrl.Contains("x-access-token:", StringComparison.Ordinal);

        for (int attempt = 1; attempt <= MaxPushRetries; attempt++)
        {
            var push = authed
                ? GitService.PushTo(dir, pushUrl, branch)
                : GitService.Push(dir, branch, setUpstream: true);
            if (push.Succeeded) return true;

            var err = push.CombinedOutput;
            Emit($"✗ 第 {attempt} 次推送失败：{err}");

            if (IsDivergenceError(err))
            {
                // 远程有本地没有的提交（如自动创建 README 产生的提交）：先合并再重试，无需用户确认
                Emit("ℹ 远程分支有本地未包含的提交，自动进行 pull --rebase 合并后重试...");
                Stage($"检测到远程变更，正在合并...（{attempt}/{MaxPushRetries}）");
                var pull = authed
                    ? GitService.PullRebaseFrom(dir, pushUrl, branch)
                    : ProcessRunner.Run("git", $"pull --rebase origin {branch}", dir, timeoutMs: 120000);
                if (!pull.Succeeded)
                {
                    Emit($"✗ pull --rebase 失败：{pull.CombinedOutput}");
                }
                continue; // 合并后直接进入下一轮尝试（不 sleep 太长）
            }

            bool retry;
            if (RetryPrompt != null)
            {
                // 弹窗询问用户
                retry = RetryPrompt(err);
            }
            else
            {
                // 没有界面回调时，前几次自动重试，最后一次放弃
                retry = attempt < MaxPushRetries;
            }

            if (!retry) return false;

            Stage($"推送失败，正在重试...（{attempt}/{MaxPushRetries}）");
            System.Threading.Thread.Sleep(3000);
        }
        return false;
    }

    /// <summary>判断是否为"历史分叉"错误（远程有本地缺失提交导致 push 被拒），而非网络错误。</summary>
    private static bool IsDivergenceError(string error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        var e = error;
        return e.Contains("rejected", StringComparison.OrdinalIgnoreCase)
               || e.Contains("fetch first", StringComparison.OrdinalIgnoreCase)
               || e.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
               || e.Contains("failed to push some refs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 决定集中式的备份根目录：优先用用户设置；为空则自动存到 文档\GitAutoBackups\集中式仓库名。
    /// </summary>
    private static string ResolveBackupRoot(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.BackupRoot))
            return settings.BackupRoot;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs)) docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(docs, "GitAutoBackups");
    }

    /// <summary>把仓库名清洗成可安全用作目录名 / GitHub 仓库名的形式（替换非法字符与路径分隔符）。</summary>
    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace('/', '_').Replace('\\', '_');
    }

    /// <summary>删除某个集中仓库的本地备份目录（不删 GitHub 仓库）。</summary>
    public static void DeleteConsolidatedRepoDirectory(string repoName, Settings settings)
    {
        var baseRoot = ResolveBackupRoot(settings);
        if (string.IsNullOrWhiteSpace(baseRoot)) return;
        var dir = Path.Combine(baseRoot, SanitizeName(repoName));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>生成集中式备份仓库的 README 内容（含类型与项目列表）。</summary>
    public static string BuildConsolidatedReadme(string repoName, IEnumerable<string> projects)
    {
        var list = projects
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 🛟 备份仓库");
        sb.AppendLine();
        sb.AppendLine("> 本仓库由 **GitAutoBackup** 自动创建与维护，用于存放项目的自动备份。");
        sb.AppendLine();
        sb.AppendLine("⚠️ **请勿误删此仓库**，否则可能导致你的项目备份丢失。");
        sb.AppendLine();
        sb.AppendLine("## 仓库信息");
        sb.AppendLine();
        sb.AppendLine("| 项 | 内容 |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine("| 类型 | 集中式备份仓库 |");
        sb.AppendLine($"| 仓库名 | {repoName} |");
        sb.AppendLine($"| 包含项目 | {list.Count} 个 |");
        sb.AppendLine($"| 最近更新 | {DateTime.Now:yyyy-MM-dd HH:mm} |");
        sb.AppendLine();
        sb.AppendLine("## 项目列表");
        sb.AppendLine();
        if (list.Count == 0) sb.AppendLine("_（暂无项目）_");
        else foreach (var p in list) sb.AppendLine($"- `{p}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("_本文件由 GitAutoBackup 自动生成，请勿手动修改（下次自动更新时会被覆盖）。_");
        return sb.ToString();
    }

    /// <summary>生成独立式备份仓库的 README 内容（含类型与项目信息）。</summary>
    public static string BuildStandaloneReadme(BackupJob job)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 🛟 备份仓库");
        sb.AppendLine();
        sb.AppendLine("> 本仓库由 **GitAutoBackup** 自动创建与维护，用于存放项目的自动备份。");
        sb.AppendLine();
        sb.AppendLine("⚠️ **请勿误删此仓库**，否则可能导致你的项目备份丢失。");
        sb.AppendLine();
        sb.AppendLine("## 仓库信息");
        sb.AppendLine();
        sb.AppendLine("| 项 | 内容 |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine("| 类型 | 独立式备份仓库 |");
        sb.AppendLine($"| 项目 | `{job.ProjectName}` |");
        sb.AppendLine($"| 最近更新 | {DateTime.Now:yyyy-MM-dd HH:mm} |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("_本文件由 GitAutoBackup 自动生成，请勿手动修改（下次自动更新时会被覆盖）。_");
        return sb.ToString();
    }

    /// <summary>写入集中式仓库 README（失败不阻断备份）。</summary>
    private static void TryWriteConsolidatedReadme(string repoFull, string repoName, Settings settings)
    {
        try
        {
            var projects = (settings.Jobs ?? new List<BackupJob>())
                .Where(j => j.Mode == BackupMode.Consolidated
                            && string.Equals(j.ConsolidatedRepoName, repoName, StringComparison.OrdinalIgnoreCase))
                .Select(j => j.ProjectName);
            GitHubService.WriteBackupReadme(repoFull, BuildConsolidatedReadme(repoName, projects), out _);
        }
        catch { /* README 写入失败不阻断备份 */ }
    }

    private static void EnsureGitIgnore(string dir)
    {
        var file = Path.Combine(dir, ".gitignore");
        if (File.Exists(file)) return;
        File.WriteAllText(file,
            "# 由 GitAutoBackup 自动生成\nnode_modules/\nbin/\nobj/\nDebug/\nRelease/\n.idea/\n.vs/\n.DS_Store\n");
    }

    /// <summary>把全局+项目自定义排除规则追加到指定目录的 .gitignore（独立式 git add 时跳过）。</summary>
    private static void AppendExcludesToGitIgnore(string dir, BackupJob job, Settings settings)
    {
        var rules = new List<ExcludeRule>();
        if (settings?.GlobalExcludes != null) rules.AddRange(settings.GlobalExcludes);
        if (job.JobExcludes != null) rules.AddRange(job.JobExcludes);

        var file = Path.Combine(dir, ".gitignore");
        if (!File.Exists(file))
        {
            File.WriteAllText(file, "# 由 GitAutoBackup 自动生成\n");
        }

        var lines = File.ReadAllLines(file).ToList();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addition = new List<string>();
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.Name)) continue;
            var n = r.Name.Trim();
            var pattern = r.Type == ExcludeType.Folder ? n + "/" : n;
            if (added.Add(pattern)) addition.Add(pattern);
        }
        if (addition.Count > 0)
        {
            lines.Add("# GitAutoBackup 自定义排除");
            lines.AddRange(addition);
            File.WriteAllLines(file, lines);
        }
    }

    /// <summary>
    /// 递归复制目录。nameExcluded(dirName/fileName) 返回 true 时跳过该项（自定义排除规则）。
    /// 硬编码的 ExcludedDirNames（.git 等）始终排除。
    /// </summary>
    public static void CopyDirectory(string sourceDir, string destDir, Func<string, bool> nameExcluded)
    {
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            if (ExcludedDirNames.Contains(name)) continue;
            if (nameExcluded(name)) continue;
            CopyDirectory(dir, Path.Combine(destDir, name), nameExcluded);
        }
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (nameExcluded(name)) continue;
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
        }
    }
}