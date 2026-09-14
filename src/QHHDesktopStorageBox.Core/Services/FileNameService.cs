namespace QHHDesktopStorageBox.Core.Services;

internal static class FileNameService
{
    public static string GetUniqueDestinationPath(string directory, string originalName, bool isDirectory)
    {
        Directory.CreateDirectory(directory);

        var candidate = Path.Combine(directory, originalName);
        if (!DestinationExists(candidate, isDirectory))
        {
            return candidate;
        }

        var name = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(originalName);

        for (var index = 1; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!DestinationExists(candidate, isDirectory))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free file name for {originalName}.");
    }

    internal static bool DestinationExists(string path, bool isDirectory)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    internal static bool DestinationExists(string directory, string originalName)
    {
        return DestinationExists(
            Path.Combine(directory, originalName),
            isDirectory: false);
    }
}
