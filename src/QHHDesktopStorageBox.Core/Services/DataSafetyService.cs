using System.IO.Compression;
using System.Text;
using System.Text.Json;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Services;

public sealed record DataBackupResult(
    string ArchivePath,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record BrokenReference(
    Guid BoxId,
    string BoxName,
    Guid ItemId,
    string DisplayName,
    string SourcePath,
    ItemKind ItemKind);

public sealed record BrokenReferenceScanResult(
    int ScannedReferenceCount,
    IReadOnlyList<BrokenReference> MissingReferences);

public sealed record DiagnosticReportResult(
    string ReportPath,
    int BrokenReferenceCount,
    int BoxCount,
    int ItemCount);

/// <summary>
/// 数据安全操作：完整 ZIP 备份、恢复到新目录、失效引用扫描和诊断报告。
/// </summary>
public sealed class DataSafetyService
{
    private const int BackupFormatVersion = 1;
    private const string BackupManifestFileName = "backup-manifest.json";
    private const string BackupProductName = "QHH Desktop Storage Box";
    private const string BackupTempFolderName = "QHHDesktopStorageBoxBackup";
    private const int DiagnosticLogTailLineCount = 120;

    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly StorageLocationStore _locationStore;

    public DataSafetyService(
        AppPaths paths,
        DrawerRepository repository,
        StorageLocationStore locationStore)
    {
        _paths = paths;
        _repository = repository;
        _locationStore = locationStore;
    }

    public async Task<DataBackupResult> CreateBackupAsync(
        string destinationArchivePath,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchivePath);
        var sourceRoot = Path.GetFullPath(_paths.RootDirectory);
        var destinationPath = Path.GetFullPath(destinationArchivePath.Trim());
        if (!string.Equals(
                Path.GetExtension(destinationPath),
                ".zip",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份文件必须使用 .zip 扩展名。");
        }

        if (IsSameOrDescendantOf(destinationPath, sourceRoot))
        {
            throw new InvalidOperationException("备份文件不能保存在当前数据目录内部。");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("备份文件目录无效。");
        Directory.CreateDirectory(destinationDirectory);

        await _repository.CheckpointAsync(cancellationToken);
        var createdAt = DateTimeOffset.UtcNow;
        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            BackupTempFolderName,
            Guid.NewGuid().ToString("N"));
        var temporaryArchivePath = destinationPath + ".tmp";

        try
        {
            var manifest = new BackupManifest(
                BackupFormatVersion,
                BackupProductName,
                applicationVersion,
                createdAt,
                sourceRoot);
            await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(stagingRoot);
                    if (!File.Exists(_paths.DatabasePath))
                    {
                        throw new InvalidOperationException("数据库文件不存在，无法创建完整备份。");
                    }

                    File.Copy(
                        _paths.DatabasePath,
                        Path.Combine(stagingRoot, AppPaths.DatabaseFileName),
                        overwrite: false);

                    if (Directory.Exists(_paths.BoxesDirectory))
                    {
                        CopyDirectory(
                            _paths.BoxesDirectory,
                            Path.Combine(stagingRoot, AppPaths.BoxesDirectoryName),
                            cancellationToken);
                    }

                    File.WriteAllText(
                        Path.Combine(stagingRoot, BackupManifestFileName),
                        JsonSerializer.Serialize(
                            manifest,
                            new JsonSerializerOptions { WriteIndented = true }),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    if (File.Exists(temporaryArchivePath))
                    {
                        File.Delete(temporaryArchivePath);
                    }

                    ZipFile.CreateFromDirectory(
                        stagingRoot,
                        temporaryArchivePath,
                        CompressionLevel.Optimal,
                        includeBaseDirectory: false);
                },
                cancellationToken);

            File.Move(temporaryArchivePath, destinationPath, overwrite: true);
            return new DataBackupResult(
                destinationPath,
                new FileInfo(destinationPath).Length,
                createdAt);
        }
        catch
        {
            TryDeleteFile(temporaryArchivePath);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public async Task<AppPaths> RestoreBackupAsync(
        string archivePath,
        string targetRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRootDirectory);

        var sourceArchive = Path.GetFullPath(archivePath.Trim());
        if (!File.Exists(sourceArchive))
        {
            throw new FileNotFoundException("备份文件不存在。", sourceArchive);
        }

        var targetRoot = Path.GetFullPath(targetRootDirectory.Trim());
        var currentRoot = Path.GetFullPath(_paths.RootDirectory);
        if (string.Equals(targetRoot, currentRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("恢复目标不能是当前正在使用的数据目录。");
        }

        if (IsSameOrDescendantOf(targetRoot, currentRoot))
        {
            throw new InvalidOperationException("恢复目标不能位于当前数据目录内部。");
        }

        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new InvalidOperationException("恢复目标必须为空，以避免覆盖现有数据。");
        }

        var targetParent = Path.GetDirectoryName(targetRoot)
            ?? throw new InvalidOperationException("恢复目标父目录无效。");
        Directory.CreateDirectory(targetParent);
        var temporaryRoot = targetRoot + $".tmp-restoring-{Guid.NewGuid():N}";

        try
        {
            await Task.Run(
                () => ExtractArchiveSafely(sourceArchive, temporaryRoot, cancellationToken),
                cancellationToken);

            var manifestPath = Path.Combine(temporaryRoot, BackupManifestFileName);
            var databasePath = Path.Combine(temporaryRoot, AppPaths.DatabaseFileName);
            if (!File.Exists(manifestPath) || !File.Exists(databasePath))
            {
                throw new InvalidOperationException(
                    "备份包缺少备份清单或数据库文件，已拒绝恢复。");
            }

            await using (var stream = File.OpenRead(manifestPath))
            {
                var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                    stream,
                    cancellationToken: cancellationToken);
                if (manifest is null
                    || manifest.FormatVersion != BackupFormatVersion
                    || !string.Equals(
                        manifest.Product,
                        BackupProductName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("备份格式不受支持或文件已损坏。");
                }
            }

            DataStorageMigrationService.PromoteStagedDirectory(
                temporaryRoot,
                targetRoot);
            _locationStore.SaveConfiguredDirectory(targetRoot);
            return new AppPaths(targetRoot);
        }
        catch
        {
            TryDeleteDirectory(temporaryRoot);
            throw;
        }
    }

    public async Task<BrokenReferenceScanResult> ScanBrokenReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        var items = await _repository.GetItemsAsync(
            boxId: null,
            cancellationToken);
        var referenceBoxes = boxes
            .Where(box => box.Type is BoxType.Mapping or BoxType.Smart)
            .ToDictionary(box => box.Id);

        return await Task.Run(
            () =>
            {
                var missing = new List<BrokenReference>();
                var scannedCount = 0;
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!referenceBoxes.TryGetValue(item.BoxId, out var box)
                        || string.IsNullOrWhiteSpace(item.SourcePath)
                        || InstalledApplicationReference.IsReference(item.SourcePath))
                    {
                        continue;
                    }

                    scannedCount++;
                    var path = item.SourcePath;
                    var exists = item.ItemKind == ItemKind.Directory
                        ? Directory.Exists(path)
                        : File.Exists(path);
                    if (exists)
                    {
                        continue;
                    }

                    missing.Add(new BrokenReference(
                        box.Id,
                        box.Name,
                        item.Id,
                        item.DisplayName,
                        path,
                        item.ItemKind));
                }

                return new BrokenReferenceScanResult(scannedCount, missing);
            },
            cancellationToken);
    }

    public async Task<DiagnosticReportResult> CreateDiagnosticReportAsync(
        string destinationPath,
        string applicationVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var reportPath = Path.GetFullPath(destinationPath.Trim());
        var reportDirectory = Path.GetDirectoryName(reportPath)
            ?? throw new InvalidOperationException("诊断报告目录无效。");
        Directory.CreateDirectory(reportDirectory);

        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        var items = await _repository.GetItemsAsync(
            boxId: null,
            cancellationToken);
        var brokenReferences = await ScanBrokenReferencesAsync(cancellationToken);
        var databaseSize = File.Exists(_paths.DatabasePath)
            ? new FileInfo(_paths.DatabasePath).Length
            : 0;
        var driveRoot = Path.GetPathRoot(_paths.RootDirectory);
        var driveInfo = string.IsNullOrWhiteSpace(driveRoot)
            ? null
            : new DriveInfo(driveRoot);

        var reportText = await Task.Run(
            () =>
            {
                var report = new StringBuilder();
                report.AppendLine("QHH Desktop Storage Box diagnostic report");
                report.AppendLine("Generated: " + DateTimeOffset.Now.ToString("O"));
                report.AppendLine("Application version: " + applicationVersion);
                report.AppendLine("Windows: " + Environment.OSVersion);
                report.AppendLine("Architecture: " + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
                report.AppendLine("Data root: " + RedactUserPath(_paths.RootDirectory));
                report.AppendLine("Database exists: " + File.Exists(_paths.DatabasePath));
                report.AppendLine("Database bytes: " + databaseSize);
                report.AppendLine("Boxes: " + boxes.Count);
                report.AppendLine("Items: " + items.Count);
                report.AppendLine("Reference items scanned: " + brokenReferences.ScannedReferenceCount);
                report.AppendLine("Broken references: " + brokenReferences.MissingReferences.Count);
                if (driveInfo is not null)
                {
                    report.AppendLine("Data drive free bytes: " + driveInfo.AvailableFreeSpace);
                    report.AppendLine("Data drive total bytes: " + driveInfo.TotalSize);
                }

                report.AppendLine();
                report.AppendLine("Broken mapping/smart references");
                if (brokenReferences.MissingReferences.Count == 0)
                {
                    report.AppendLine("None");
                }
                else
                {
                    foreach (var reference in brokenReferences.MissingReferences)
                    {
                        report.AppendLine(
                            $"- box={reference.BoxName}; item={reference.DisplayName}; "
                            + $"kind={reference.ItemKind}; path={RedactUserPath(reference.SourcePath)}");
                    }
                }

                report.AppendLine();
                report.AppendLine("Recent log tail");
                AppendLogTail(report, cancellationToken);
                return report.ToString();
            },
            cancellationToken);

        await File.WriteAllTextAsync(
            reportPath,
            reportText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return new DiagnosticReportResult(
            reportPath,
            brokenReferences.MissingReferences.Count,
            boxes.Count,
            items.Count);
    }

    private void AppendLogTail(StringBuilder report, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            report.AppendLine("No log directory.");
            return;
        }

        var logFiles = Directory
            .EnumerateFiles(_paths.LogsDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(5)
            .ToArray();
        if (logFiles.Length == 0)
        {
            report.AppendLine("No log files.");
            return;
        }

        foreach (var logFile in logFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            report.AppendLine();
            report.AppendLine("File: " + RedactUserPath(logFile.Name));
            try
            {
                var lines = File.ReadLines(logFile.FullName).TakeLast(DiagnosticLogTailLineCount);
                foreach (var line in lines)
                {
                    report.AppendLine(RedactUserPath(line));
                }
            }
            catch (Exception exception)
            {
                report.AppendLine("Unable to read log: " + exception.GetType().Name);
            }
        }
    }

    private static void ExtractArchiveSafely(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("备份包包含不安全路径，已拒绝恢复。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(targetPath, overwrite: false);
        }
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        EnsureNoReparsePoint(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(directory);
            CopyDirectory(
                directory,
                Path.Combine(targetDirectory, Path.GetFileName(directory)),
                cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoint(file);
            File.Copy(
                file,
                Path.Combine(targetDirectory, Path.GetFileName(file)),
                overwrite: false);
        }
    }

    private static void EnsureNoReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("备份不支持符号链接或其他 reparse point: " + path);
        }
    }

    private static bool IsSameOrDescendantOf(string candidate, string ancestor)
    {
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedAncestor = Path.GetFullPath(ancestor)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(
                normalizedCandidate,
                normalizedAncestor,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedAncestor + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactUserPath(string value)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? value
            : value.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record BackupManifest(
        int FormatVersion,
        string Product,
        string ApplicationVersion,
        DateTimeOffset CreatedAt,
        string SourceRootDirectory);
}
