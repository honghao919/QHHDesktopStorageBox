using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core;

/// <summary>
/// 应用本地数据路径约定。
/// 默认使用 %LocalAppData%\QHHDesktopStorageBox，可通过环境变量覆盖。
/// </summary>
public sealed record AppPaths(string RootDirectory)
{
    /// <summary>
    /// 用于覆盖默认数据根目录的环境变量名。
    /// 例如：QHH_DESKTOP_STORAGE_BOX_DATA_DIR=D:\data\QHHDesktopStorageBox
    /// </summary>
    public const string DataDirectoryEnvironmentVariableName = "QHH_DESKTOP_STORAGE_BOX_DATA_DIR";

    /// <summary>
    /// 默认数据目录名（位于 LocalApplicationData 下）。
    /// </summary>
    public const string DefaultRootDirectoryName = "QHHDesktopStorageBox";

    /// <summary>
    /// 重命名前使用的数据目录名，仅用于首次启动兼容迁移。
    /// </summary>
    public const string LegacyDefaultRootDirectoryName = "WitchDrawer";

    /// <summary>
    /// 数据库文件名。
    /// </summary>
    public const string DatabaseFileName = "qhhdesktopstoragebox.db";

    /// <summary>
    /// 重命名前使用的数据库文件名，仅用于首次启动兼容迁移。
    /// </summary>
    public const string LegacyDatabaseFileName = "witchdrawer.db";

    /// <summary>
    /// 收纳盒实体文件根目录名。
    /// </summary>
    public const string BoxesDirectoryName = "Boxes";

    /// <summary>
    /// 日志目录名。
    /// </summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>
    /// 可写性探测文件名（创建后立即删除）。
    /// </summary>
    private const string WritabilityProbeFileName = ".qhhdesktopstoragebox_write_probe";

    public string BoxesDirectory => Path.Combine(RootDirectory, BoxesDirectoryName);

    public string DatabasePath => Path.Combine(RootDirectory, DatabaseFileName);

    public string LogsDirectory => Path.Combine(RootDirectory, LogsDirectoryName);

    /// <summary>
    /// 解析当前用户应使用的数据路径：
    /// 1. 环境变量 QHH_DESKTOP_STORAGE_BOX_DATA_DIR（若设置且有效）
    /// 2. 设置页保存的自定义数据目录（storage-location.json）
    /// 3. %LocalAppData%\QHHDesktopStorageBox
    /// 解析后会校验目录可写；不可写时抛出带路径上下文的异常。
    /// </summary>
    public static AppPaths ForCurrentUser()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredPaths = new AppPaths(Path.GetFullPath(configuredRoot.Trim()));
            configuredPaths.EnsureCreatedAndWritable();
            return configuredPaths;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "无法解析 LocalApplicationData。请设置环境变量 "
                + DataDirectoryEnvironmentVariableName
                + " 指向可写目录。");
        }

        var defaultRoot = ResolveAndMigrateLegacyDefaultRoot(localAppData);

        string? settingsConfiguredRoot = null;
        try
        {
            settingsConfiguredRoot = new StorageLocationStore(
                    Path.Combine(defaultRoot, StorageLocationStore.ConfigFileName))
                .LoadConfiguredDirectory();
        }
        catch
        {
            // 引导配置不可读时回退默认目录，避免应用无法启动。
        }

        if (!string.IsNullOrWhiteSpace(settingsConfiguredRoot))
        {
            var settingsPaths = new AppPaths(settingsConfiguredRoot);
            settingsPaths.EnsureCreatedAndWritable();
            return settingsPaths;
        }

        var defaultPaths = new AppPaths(defaultRoot);
        defaultPaths.EnsureCreatedAndWritable();
        return defaultPaths;
    }

    internal static string ResolveAndMigrateLegacyDefaultRoot(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        var legacyRoot = Path.Combine(localAppData, LegacyDefaultRootDirectoryName);
        var currentRoot = Path.Combine(localAppData, DefaultRootDirectoryName);

        if (Directory.Exists(currentRoot))
        {
            TryRenameLegacyFiles(currentRoot);
            return currentRoot;
        }

        if (!Directory.Exists(legacyRoot))
        {
            return currentRoot;
        }

        try
        {
            Directory.Move(legacyRoot, currentRoot);
            TryRenameLegacyFiles(currentRoot);
            return currentRoot;
        }
        catch
        {
            // Never create a fresh database while the old data is still present.
            if (Directory.Exists(currentRoot) && !Directory.Exists(legacyRoot))
            {
                try
                {
                    Directory.Move(currentRoot, legacyRoot);
                }
                catch
                {
                }
            }

            return legacyRoot;
        }
    }

    private static void TryRenameLegacyFiles(string root)
    {
        var legacyDatabase = Path.Combine(root, LegacyDatabaseFileName);
        var currentDatabase = Path.Combine(root, DatabaseFileName);
        if (File.Exists(currentDatabase) || !File.Exists(legacyDatabase))
        {
            return;
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var source = legacyDatabase + suffix;
            var destination = currentDatabase + suffix;
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Move(source, destination);
            }
        }
    }

    /// <summary>
    /// 创建必要目录结构（不做可写校验）。
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BoxesDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// 创建必要目录，并验证根目录可创建/删除临时文件。
    /// SQLite 在 WAL 模式下需要在同目录创建 -wal/-shm，目录只读会导致 Error 14。
    /// </summary>
    public void EnsureCreatedAndWritable()
    {
        EnsureCreated();
        EnsureRootDirectoryWritable();
    }

    /// <summary>
    /// 探测根目录是否允许创建新文件。
    /// </summary>
    private void EnsureRootDirectoryWritable()
    {
        var probePath = Path.Combine(RootDirectory, WritabilityProbeFileName);
        try
        {
            using (var stream = new FileStream(
                       probePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
                stream.WriteByte(1);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "QHH Desktop Storage Box 数据目录不可写，SQLite 无法创建数据库旁路文件（-wal/-shm）。"
                + Environment.NewLine
                + "数据目录: "
                + RootDirectory
                + Environment.NewLine
                + "请检查目录权限，或设置环境变量 "
                + DataDirectoryEnvironmentVariableName
                + " 指向可写目录。",
                exception);
        }
        finally
        {
            // DeleteOnClose 通常已清理；再兜底一次，避免探测文件残留。
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // 清理失败不影响启动判定；可写性已在创建阶段确认。
            }
        }
    }
}
