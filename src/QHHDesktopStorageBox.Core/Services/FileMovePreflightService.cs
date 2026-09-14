using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Services;

public sealed class FileMovePreflightService(
    AppPaths paths,
    DrawerRepository repository)
{
    public Task<FileMovePreflightResult> AnalyzeImportAsync(
        Guid boxId,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        return Task.Run(
            () => AnalyzeImport(boxId, sourcePaths, cancellationToken),
            cancellationToken);
    }

    private FileMovePreflightResult AnalyzeImport(
        Guid boxId,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        var box = repository.GetBoxAsync(boxId).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Todo boxes do not accept files.");
        }

        if (box.Type == BoxType.Smart)
        {
            throw new InvalidOperationException(
                "Smart boxes are maintained by rules and do not accept manual imports.");
        }

        var isReferenceBox = box.Type is BoxType.Mapping or BoxType.Smart;
        var storageRoot = isReferenceBox
            ? null
            : box.StoragePath ?? Path.Combine(paths.BoxesDirectory, box.Id.ToString("N"));
        var occupiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (storageRoot is not null && Directory.Exists(storageRoot))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(storageRoot))
            {
                occupiedNames.Add(Path.GetFileName(entry));
            }
        }

        var fileCount = 0;
        var totalBytes = 0L;
        var conflictCount = 0;
        var inaccessibleCount = 0;
        var containsDirectory = false;
        var crossVolume = false;

        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = PathSafety.GetFullExistingPath(sourcePath);
            var isDirectory = Directory.Exists(fullPath);
            var displayName = Path.GetFileName(
                fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

            if (isDirectory)
            {
                containsDirectory = true;
                try
                {
                    var snapshot = CaptureDirectory(fullPath, cancellationToken);
                    fileCount += snapshot.FileCount;
                    totalBytes += snapshot.TotalBytes;
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
                {
                    inaccessibleCount++;
                }
            }
            else
            {
                fileCount++;
                try
                {
                    totalBytes += new FileInfo(fullPath).Length;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    inaccessibleCount++;
                }
            }

            if (storageRoot is not null)
            {
                if (occupiedNames.Contains(displayName))
                {
                    conflictCount++;
                }

                occupiedNames.Add(displayName);
                crossVolume |= !SafeFileOps.AreSameVolume(fullPath, storageRoot);
            }
        }

        return new FileMovePreflightResult(
            sourcePaths.Count,
            fileCount,
            totalBytes,
            conflictCount,
            inaccessibleCount,
            containsDirectory,
            crossVolume);
    }

    private static (int FileCount, long TotalBytes) CaptureDirectory(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var fileCount = 0;
        var totalBytes = 0L;
        foreach (var file in Directory.EnumerateFiles(
                     rootDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileCount++;
            totalBytes += new FileInfo(file).Length;
        }

        return (fileCount, totalBytes);
    }
}
