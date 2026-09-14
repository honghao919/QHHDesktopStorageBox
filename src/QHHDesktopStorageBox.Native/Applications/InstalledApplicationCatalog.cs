using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using QHHDesktopStorageBox.Core.Models;

namespace QHHDesktopStorageBox.Native.Applications;

public sealed record InstalledApplicationInfo(string Name, string AppId);

/// <summary>
/// Reads Start-menu applications and resolves Start-menu drag data without
/// moving or copying application files.
/// </summary>
public static class InstalledApplicationCatalog
{
    private const string ShellIdListFormat = "Shell IDList Array";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static IReadOnlyList<InstalledApplicationInfo>? _cachedApplications;
    private static DateTimeOffset _cacheExpiresAt;

    public static string ShellIdListDataFormat => ShellIdListFormat;

    public static async Task<IReadOnlyList<InstalledApplicationInfo>> GetInstalledApplicationsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh
            && _cachedApplications is not null
            && DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return _cachedApplications;
        }

        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && _cachedApplications is not null
                && DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cachedApplications;
            }

            var json = await RunGetStartAppsAsync(cancellationToken);
            _cachedApplications = ParseStartAppsJson(json);
            _cacheExpiresAt = DateTimeOffset.UtcNow + CacheLifetime;
            return _cachedApplications;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    public static bool TryResolveShellIdList(
        byte[]? shellIdList,
        out InstalledApplicationInfo? application)
    {
        application = null;
        if (shellIdList is not { Length: >= 8 })
        {
            return false;
        }

        var handle = GCHandle.Alloc(shellIdList, GCHandleType.Pinned);
        try
        {
            var root = handle.AddrOfPinnedObject();
            var itemCount = Marshal.ReadInt32(root, 0);
            var firstOffset = Marshal.ReadInt32(root, 4);
            if (itemCount <= 0 || firstOffset <= 0 || firstOffset >= shellIdList.Length)
            {
                return false;
            }

            var pidl = IntPtr.Add(root, firstOffset);
            var displayName = GetShellName(pidl, ShellDisplayName.NormalDisplay);
            var parsingName = GetShellName(pidl, ShellDisplayName.ParentRelativeParsing)
                ?? GetShellName(pidl, ShellDisplayName.DesktopAbsoluteParsing);
            if (string.IsNullOrWhiteSpace(parsingName))
            {
                return false;
            }

            var appId = parsingName.Trim();
            if (appId.StartsWith(InstalledApplicationReference.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                appId = appId[InstalledApplicationReference.Prefix.Length..];
            }
            else if (File.Exists(appId) || Directory.Exists(appId) || Path.IsPathRooted(appId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(appId)
                || appId.StartsWith("::", StringComparison.Ordinal)
                || appId.Length > 512)
            {
                return false;
            }

            application = new InstalledApplicationInfo(
                string.IsNullOrWhiteSpace(displayName) ? appId : displayName.Trim(),
                appId);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            handle.Free();
        }
    }

    internal static IReadOnlyList<InstalledApplicationInfo> ParseStartAppsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<InstalledApplicationInfo>();
        }

        try
        {
            using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
            var root = document.RootElement;
            var elements = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : new[] { root }.AsEnumerable();
            return elements
                .Select(ParseApplication)
                .Where(application => application is not null)
                .Select(application => application!)
                .Where(IsLaunchableApplication)
                .DistinctBy(application => application.AppId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(application => application.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<InstalledApplicationInfo>();
        }
    }

    private static InstalledApplicationInfo? ParseApplication(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = GetStringProperty(element, "Name");
        var appId = GetStringProperty(element, "AppID")
            ?? GetStringProperty(element, "AppId");
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        return new InstalledApplicationInfo(
            string.IsNullOrWhiteSpace(name) ? appId.Trim() : name.Trim(),
            appId.Trim());
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static bool IsLaunchableApplication(InstalledApplicationInfo application)
    {
        var appId = application.AppId;
        if (!Path.IsPathRooted(appId))
        {
            return true;
        }

        if (Directory.Exists(appId))
        {
            return true;
        }

        return Path.GetExtension(appId).ToLowerInvariant() is
            ".exe" or ".com" or ".bat" or ".cmd" or ".lnk" or ".msc" or ".appref-ms";
    }

    private static async Task<string> RunGetStartAppsAsync(CancellationToken cancellationToken)
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(windowsPowerShell))
        {
            return string.Empty;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = windowsPowerShell,
            Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "
                + "\"[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
                + "Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });
        if (process is null)
        {
            return string.Empty;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return string.Empty;
        }

        var output = await outputTask;
        _ = await errorTask;
        return process.ExitCode == 0 ? output : string.Empty;
    }

    private static string? GetShellName(IntPtr pidl, ShellDisplayName displayName)
    {
        var namePointer = IntPtr.Zero;
        try
        {
            var result = SHGetNameFromIDList(pidl, displayName, out namePointer);
            return result >= 0 && namePointer != IntPtr.Zero
                ? Marshal.PtrToStringUni(namePointer)
                : null;
        }
        finally
        {
            if (namePointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(namePointer);
            }
        }
    }

    private enum ShellDisplayName
    {
        NormalDisplay = 0x00000000,
        ParentRelativeParsing = unchecked((int)0x80018001),
        DesktopAbsoluteParsing = unchecked((int)0x80028000)
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetNameFromIDList(
        IntPtr pidl,
        ShellDisplayName sigdnName,
        out IntPtr ppszName);
}
