using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class InstalledApplicationReferenceTests
{
    [Theory]
    [InlineData(BoxType.Normal)]
    [InlineData(BoxType.Mapping)]
    public async Task AddInstalledApplicationAsync_AddsReferenceWithoutStoredFile(BoxType boxType)
    {
        using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("应用盒", boxType);

        var item = await workspace.Service.AddInstalledApplicationAsync(
            box.Id,
            "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            "计算器",
            gridColumn: 2,
            gridRow: 1);

        Assert.Equal(
            @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
            item.SourcePath);
        Assert.Null(item.StoredPath);
        Assert.Equal(2, item.GridColumn);
        Assert.Equal(1, item.GridRow);
        Assert.True(InstalledApplicationReference.IsReference(item.SourcePath));
    }

    [Fact]
    public async Task AddInstalledApplicationAsync_DeduplicatesWithinBox()
    {
        using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("应用盒", BoxType.Normal);

        var first = await workspace.Service.AddInstalledApplicationAsync(
            box.Id,
            "Example.App!App",
            "示例应用");
        var second = await workspace.Service.AddInstalledApplicationAsync(
            box.Id,
            "Example.App!App",
            "示例应用");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await workspace.Service.GetItemsAsync(box.Id));
    }

    [Fact]
    public async Task AddInstalledApplicationAsync_Win32PathUsesAbsoluteReference()
    {
        using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("应用盒", BoxType.Normal);
        var executablePath = Path.Combine(workspace.Root, "Example.exe");
        await File.WriteAllTextAsync(executablePath, "placeholder");

        var item = await workspace.Service.AddInstalledApplicationAsync(
            box.Id,
            executablePath,
            "示例程序");

        Assert.Equal(Path.GetFullPath(executablePath), item.SourcePath);
        Assert.Null(item.StoredPath);
    }

    [Fact]
    public async Task MoveItemToBoxAsync_InstalledApplicationMovesReferenceOnly()
    {
        using var workspace = await Workspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("普通盒", BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("映射盒", BoxType.Mapping);
        var item = await workspace.Service.AddInstalledApplicationAsync(
            sourceBox.Id,
            "Example.App!App",
            "示例应用");

        await workspace.Service.MoveItemToBoxAsync(
            item.Id,
            targetBox.Id,
            gridColumn: 4,
            gridRow: 3);

        var moved = Assert.Single(await workspace.Service.GetItemsAsync(targetBox.Id));
        Assert.Equal(item.SourcePath, moved.SourcePath);
        Assert.Null(moved.StoredPath);
        Assert.Equal((4, 3), (moved.GridColumn, moved.GridRow));
        Assert.Empty(await workspace.Service.GetItemsAsync(sourceBox.Id));
    }

    [Fact]
    public async Task DeleteItemAsync_InstalledApplicationRemovesReferenceOnly()
    {
        using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("应用盒", BoxType.Mapping);
        var item = await workspace.Service.AddInstalledApplicationAsync(
            box.Id,
            "Example.App!App",
            "示例应用");

        var result = await workspace.Service.DeleteItemAsync(item.Id);

        Assert.Equal(item.Id, result.ItemId);
        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));
        Assert.True(InstalledApplicationReference.IsReference(item.SourcePath));
    }

    private sealed class Workspace : IDisposable
    {
        private Workspace(string root, DrawerService service)
        {
            Root = root;
            Service = service;
        }

        public string Root { get; }

        public DrawerService Service { get; }

        public static async Task<Workspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "QHHDesktopStorageBox.Tests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var service = new DrawerService(
                paths,
                new DrawerRepository(paths.DatabasePath));
            await service.InitializeAsync();
            return new Workspace(root, service);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
