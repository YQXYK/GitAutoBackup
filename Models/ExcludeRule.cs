namespace GitAutoBackup.Models;

/// <summary>排除目标类型</summary>
public enum ExcludeType
{
    /// <summary>排除文件夹（按名称精确匹配）</summary>
    Folder = 0,

    /// <summary>排除文件（按文件名精确匹配）</summary>
    File = 1
}

/// <summary>一条自定义排除规则：按名称精确匹配，跳过该文件夹/文件不备份。</summary>
public class ExcludeRule
{
    /// <summary>要排除的名称（如 server / test.vue），按名称精确匹配</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>排除目标类型（文件夹 / 文件）</summary>
    public ExcludeType Type { get; set; } = ExcludeType.Folder;
}