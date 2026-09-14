namespace QHHDesktopStorageBox.Core.Models;

/// <summary>
/// Stores a Windows Start-menu application as an AppsFolder reference instead of
/// moving or copying any system files.
/// </summary>
public static class InstalledApplicationReference
{
    public const string Prefix = @"shell:AppsFolder\";

    public static string Create(string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        var appId = appUserModelId.Trim();
        if (appId.Length > 512
            || appId.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("The application identifier is invalid.", nameof(appUserModelId));
        }

        return appId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? appId
            : Prefix + appId;
    }

    public static bool IsReference(string? sourcePath)
        => TryGetAppUserModelId(sourcePath, out _);

    public static bool TryGetAppUserModelId(string? sourcePath, out string appUserModelId)
    {
        appUserModelId = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath)
            || !sourcePath.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var appId = sourcePath[Prefix.Length..].Trim();
        if (appId.Length == 0
            || appId.Length > 512
            || appId.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            return false;
        }

        appUserModelId = appId;
        return true;
    }
}
