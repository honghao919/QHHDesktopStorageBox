using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class FileOperationHistoryTests
{
    [Fact]
    public async Task UndoImport_RestoresSourceAndRemovesItem()
    {
        await using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("box", BoxType.Normal);
        var source = workspace.CreateFile("import.txt", "content");

        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var undone = await workspace.Service.UndoLastFileOperationAsync();

        Assert.NotNull(undone);
        Assert.Equal(FileOperationKind.Import, undone.Kind);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(item.StoredPath));
        Assert.Empty(await workspace.Service.GetItemsAsync(box.Id));
    }

    [Fact]
    public async Task UndoMove_ReturnsItemToSourceBox()
    {
        await using var workspace = await Workspace.CreateAsync();
        var sourceBox = await workspace.Service.CreateBoxAsync("source", BoxType.Normal);
        var targetBox = await workspace.Service.CreateBoxAsync("target", BoxType.Normal);
        var source = workspace.CreateFile("move.txt", "content");
        var item = await workspace.Service.ImportPathAsync(sourceBox.Id, source);

        await workspace.Service.MoveItemToBoxAsync(item.Id, targetBox.Id);
        await workspace.Service.UndoLastFileOperationAsync();

        var restored = await workspace.Service.GetItemsAsync(sourceBox.Id);
        Assert.Single(restored);
        Assert.Equal(sourceBox.Id, restored[0].BoxId);
        Assert.True(File.Exists(restored[0].StoredPath));
    }

    [Fact]
    public async Task UndoDelete_ReturnsRestoredFileToBox()
    {
        await using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("box", BoxType.Normal);
        var source = workspace.CreateFile("delete.txt", "content");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);

        await workspace.Service.DeleteItemAsync(item.Id);
        Assert.True(File.Exists(source));

        await workspace.Service.UndoLastFileOperationAsync();

        Assert.False(File.Exists(source));
        var restored = Assert.Single(await workspace.Service.GetItemsAsync(box.Id));
        Assert.NotNull(restored.StoredPath);
        Assert.True(File.Exists(restored.StoredPath));
    }

    [Fact]
    public async Task UndoExport_ReturnsFileToBox()
    {
        await using var workspace = await Workspace.CreateAsync();
        var box = await workspace.Service.CreateBoxAsync("box", BoxType.Normal);
        var source = workspace.CreateFile("export.txt", "content");
        var item = await workspace.Service.ImportPathAsync(box.Id, source);
        var exportDirectory = Directory.CreateDirectory(
            Path.Combine(workspace.Root, "export"));

        var exported = await workspace.Service.ExportItemToDirectoryAsync(
            item.Id,
            exportDirectory.FullName);
        await workspace.Service.UndoLastFileOperationAsync();

        Assert.False(File.Exists(exported));
        var restored = Assert.Single(await workspace.Service.GetItemsAsync(box.Id));
        Assert.True(File.Exists(restored.StoredPath));
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

        public string CreateFile(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public static async Task<Workspace> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "QHHDesktopStorageBoxHistoryTests",
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
