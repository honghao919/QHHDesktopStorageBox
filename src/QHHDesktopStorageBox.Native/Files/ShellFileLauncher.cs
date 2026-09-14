using System.Diagnostics;
using QHHDesktopStorageBox.Core.Abstractions;
using QHHDesktopStorageBox.Core.Models;

namespace QHHDesktopStorageBox.Native.Files;

public sealed class ShellFileLauncher : IFileLauncher
{
    public Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (InstalledApplicationReference.TryGetAppUserModelId(path, out var appId))
        {
            Process.Start(CreateInstalledApplicationStartInfo(appId));
            return Task.CompletedTask;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("Cannot open a missing file or directory.", path);
        }

        Process.Start(CreateFileStartInfo(path));

        return Task.CompletedTask;
    }

    internal static ProcessStartInfo CreateInstalledApplicationStartInfo(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{InstalledApplicationReference.Create(appUserModelId)}\"",
            UseShellExecute = true
        };
    }

    internal static ProcessStartInfo CreateFileStartInfo(string path)
    {
        return new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };
    }
}

