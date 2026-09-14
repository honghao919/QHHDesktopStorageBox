using QHHDesktopStorageBox.Native.Applications;
using QHHDesktopStorageBox.Native.Files;
using QHHDesktopStorageBox.App.Features.ItemContextMenu;
using QHHDesktopStorageBox.App.ViewModels;
using QHHDesktopStorageBox.Core.Models;

namespace QHHDesktopStorageBox.App.Tests;

public sealed class InstalledApplicationCatalogTests
{
    [Fact]
    public void ParseStartAppsJson_HandlesSingleObjectAndDeduplicates()
    {
        const string json =
            """
            [
              { "Name": "计算器", "AppID": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
              { "Name": "计算器重复项", "AppID": "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" },
              { "Name": "设置", "AppID": "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel" }
            ]
            """;

        var applications = InstalledApplicationCatalog.ParseStartAppsJson(json);

        Assert.Equal(2, applications.Count);
        Assert.Contains(applications, app => app.Name == "计算器");
        Assert.Contains(applications, app => app.Name == "设置");
    }

    [Fact]
    public void ParseStartAppsJson_IgnoresInvalidEntries()
    {
        const string json =
            """
            [
              { "Name": "Missing id" },
              { "Name": "Valid", "AppID": "Example.App!App" }
            ]
            """;

        var applications = InstalledApplicationCatalog.ParseStartAppsJson(json);

        var application = Assert.Single(applications);
        Assert.Equal("Example.App!App", application.AppId);
    }

    [Fact]
    public void ParseStartAppsJson_ExcludesDocumentationShortcuts()
    {
        const string json =
            """
            [
              { "Name": "Manual", "AppID": "C:\\Program Files\\Example\\manual.chm" },
              { "Name": "Application", "AppID": "C:\\Program Files\\Example\\app.exe" }
            ]
            """;

        var applications = InstalledApplicationCatalog.ParseStartAppsJson(json);

        var application = Assert.Single(applications);
        Assert.Equal("Application", application.Name);
    }

    [Fact]
    public void InstalledApplicationStartInfo_UsesAppsFolderReference()
    {
        var startInfo = ShellFileLauncher.CreateInstalledApplicationStartInfo("Example.App!App");

        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.Equal("\"shell:AppsFolder\\Example.App!App\"", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }

    [Fact]
    public void InstalledApplicationReference_IsRecognizedByItemAndContextMenu()
    {
        var model = new DrawerItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "计算器",
            ItemKind.File,
            @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            null,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var item = new DrawerItemViewModel(model);

        var pathState = DrawerItemContextMenuCoordinator.InspectPath(item.PathLabel);

        Assert.True(item.IsInstalledApplication);
        Assert.Equal("应用", item.KindLabel);
        Assert.Equal("APP", item.KindBadge);
        Assert.Equal("APP", item.FallbackIconText);
        Assert.True(pathState.Exists);
        Assert.True(pathState.IsInstalledApplication);
    }
}
