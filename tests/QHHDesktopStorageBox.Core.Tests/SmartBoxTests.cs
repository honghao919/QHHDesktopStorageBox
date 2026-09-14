using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class SmartBoxTests
{
    [Fact]
    public async Task SyncSmartBox_AddsMatchingReferencesWithoutTouchingSources()
    {
        await using var workspace = await Workspace.CreateAsync();
        var sourceRoot = Directory.CreateDirectory(Path.Combine(workspace.Root, "watch"));
        var pdf = Path.Combine(sourceRoot.FullName, "report.pdf");
        var text = Path.Combine(sourceRoot.FullName, "notes.txt");
        await File.WriteAllTextAsync(pdf, "pdf");
        await File.WriteAllTextAsync(text, "txt");
        var smartBox = await workspace.Service.CreateBoxAsync("smart", BoxType.Smart);
        await workspace.Service.SaveSmartBoxRuleAsync(
            smartBox.Id,
            new SmartBoxRule(sourceRoot.FullName, Extensions: ".pdf"));

        var result = await workspace.Service.SyncSmartBoxAsync(smartBox.Id);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.AddedCount);
        var item = Assert.Single(await workspace.Service.GetItemsAsync(smartBox.Id));
        Assert.Equal(Path.GetFullPath(pdf), item.SourcePath);
        Assert.Null(item.StoredPath);
        Assert.True(File.Exists(pdf));
        Assert.True(File.Exists(text));
    }

    [Fact]
    public async Task ReSyncSmartBox_RemovesReferencesThatNoLongerMatch()
    {
        await using var workspace = await Workspace.CreateAsync();
        var sourceRoot = Directory.CreateDirectory(Path.Combine(workspace.Root, "watch"));
        var text = Path.Combine(sourceRoot.FullName, "notes.txt");
        await File.WriteAllTextAsync(text, "txt");
        var smartBox = await workspace.Service.CreateBoxAsync("smart", BoxType.Smart);
        await workspace.Service.SaveSmartBoxRuleAsync(
            smartBox.Id,
            new SmartBoxRule(sourceRoot.FullName));
        await workspace.Service.SyncSmartBoxAsync(smartBox.Id);
        await workspace.Service.SaveSmartBoxRuleAsync(
            smartBox.Id,
            new SmartBoxRule(sourceRoot.FullName, Extensions: ".pdf"));

        var result = await workspace.Service.SyncSmartBoxAsync(smartBox.Id);

        Assert.Equal(1, result.RemovedCount);
        Assert.Empty(await workspace.Service.GetItemsAsync(smartBox.Id));
        Assert.True(File.Exists(text));
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
                "QHHDesktopStorageBoxSmartBoxTests",
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
