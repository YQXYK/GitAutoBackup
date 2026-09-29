using System.IO;
using System.Text.Json;
using GitAutoBackup.Models;

namespace GitAutoBackup.Services;

/// <summary>配置持久化：读写 %AppData%\GitAutoBackup\settings.json</summary>
public static class SettingsService
{
    private static string DirPath => PathService.DataDir;

    private static string FilePath => Path.Combine(DirPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (s != null) return s;
            }
        }
        catch
        {
            // 损坏则回退默认
        }
        return new Settings();
    }

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(DirPath);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(FilePath, json);
    }
}