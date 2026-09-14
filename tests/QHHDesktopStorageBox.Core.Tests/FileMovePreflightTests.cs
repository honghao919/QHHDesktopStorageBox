using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class FileMovePreflightTests
{
    [Fact]
    public async Task Preflight_ReportsDirectorySizeAndConflict()
    {
        await using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var source = Directory.CreateDirectory(Path.Combine(workspace.Root, "source"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "same.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "nested.txt"), "nested");

        var storageRoot = box.StoragePath!;
        Directory.CreateDirectory(storageRoot);
        await File.WriteAllTextAsync(Path.Combine(storageRoot, "source"), "conflict");

        var result = await workspace.Service.PreflightImportAsync(
            box.Id,
            [source.FullName]);

        Assert.Equal(1, result.ItemCount);
        Assert.Equal(2, result.FileCount);
        Assert.True(result.TotalBytes > 0);
        Assert.Equal(1, result.ConflictCount);
        Assert.True(result.ContainsDirectory);
        Assert.True(result.RequiresConfirmation);
        Assert.Contains("映射盒", result.BuildSummary());
    }

    [Fact]
    public async Task SkipPolicy_LeavesConflictingSourceUntouched()
    {
        await using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var source = Path.Combine(workspace.Root, "source.txt");
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(Path.Combine(box.StoragePath!, "source.txt"), "old");

        var imported = await workspace.Service.TryImportPathAsync(
            box.Id,
            source,
            new ImportOptions(ImportConflictPolicy.Skip));

        Assert.Null(imported);
        Assert.True(File.Exists(source));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(box.StoragePath!, "source.txt")));
    }

    [Fact]
    public void SharingViolationMessage_ExplainsRecoveryOptions()
    {
        var message = DrawerService.BuildSharingViolationMessage(
            "project",
            isDirectory: true);

        Assert.Contains("正被其他程序占用", message);
        Assert.Contains("映射收纳盒", message);
    }

    private sealed class Workspace : IAsyncDisposable
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
                "QHHDesktopStorageBoxPreflightTests",
                Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            return new Workspace(root, service);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
