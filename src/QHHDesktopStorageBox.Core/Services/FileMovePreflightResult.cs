namespace QHHDesktopStorageBox.Core.Services;

public sealed record FileMovePreflightResult(
    int ItemCount,
    int FileCount,
    long TotalBytes,
    int ConflictCount,
    int InaccessibleCount,
    bool ContainsDirectory,
    bool CrossVolume)
{
    public bool RequiresConfirmation =>
        ConflictCount > 0
        || InaccessibleCount > 0
        || ContainsDirectory
        || CrossVolume
        || FileCount >= 100
        || TotalBytes >= 100L * 1024 * 1024;

    public string BuildSummary()
    {
        var size = FormatBytes(TotalBytes);
        var parts = new List<string>
        {
            $"{ItemCount} 个项目，共 {FileCount} 个文件，{size}"
        };

        if (ContainsDirectory)
        {
            parts.Add("包含文件夹，普通盒会移动整个目录；如目录正在使用，请改用映射盒");
        }

        if (CrossVolume)
        {
            parts.Add("需要跨磁盘复制");
        }

        if (ConflictCount > 0)
        {
            parts.Add($"{ConflictCount} 个同名项目");
        }

        if (InaccessibleCount > 0)
        {
            parts.Add($"{InaccessibleCount} 个项目无法完整读取");
        }

        return string.Join("；", parts) + "。是否继续导入？";
    }

    private static string FormatBytes(long bytes)
    {
        const double kilobyte = 1024;
        const double megabyte = kilobyte * 1024;
        const double gigabyte = megabyte * 1024;

        return bytes switch
        {
            >= (long)gigabyte => $"{bytes / gigabyte:0.##} GB",
            >= (long)megabyte => $"{bytes / megabyte:0.##} MB",
            >= (long)kilobyte => $"{bytes / kilobyte:0.##} KB",
            _ => $"{bytes} B"
        };
    }
}
