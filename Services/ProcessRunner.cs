using System.Diagnostics;

namespace GitAutoBackup.Services;

/// <summary>子进程执行结果</summary>
public class ProcessResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(Stderr) ? Stdout.Trim() : $"{Stdout.Trim()}\n{Stderr.Trim()}".Trim();
}

/// <summary>通用的子进程执行封装：git 与 gh CLI 复用</summary>
public static class ProcessRunner
{
    /// <summary>执行一个命令并等待完成。workingDirectory 可为空。</summary>
    public static ProcessResult Run(string fileName, string arguments, string? workingDirectory = null,
        int timeoutMs = 300000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.SystemDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException($"无法启动进程: {fileName}");

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            p.Kill(true);
            throw new TimeoutException($"命令超时: {fileName} {arguments}");
        }

        return new ProcessResult { ExitCode = p.ExitCode, Stdout = outTask.Result, Stderr = errTask.Result };
    }
}
