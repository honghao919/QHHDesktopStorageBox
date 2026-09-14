using System.IO;
using QHHDesktopStorageBox.App.Infrastructure;
using QHHDesktopStorageBox.App.ViewModels;
using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Abstractions;
using QHHDesktopStorageBox.Core.Logging;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.App.Tests;

public sealed class BatchOperationTests
{
    [Fact]
    public async Task BatchDelete_RestoresEverySelectedStoredItem()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBoxBatchTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var logger = NullAppLogger.Instance;
            var launcher = new NoOpFileLauncher();
            var visualStyles = new BoxVisualStyleStore(drawerService, logger);
            var quickPanel = new QuickPanelViewModel(
                drawerService,
                launcher,
                logger,
                visualStyles);
            var viewModel = new MainViewModel(
                drawerService,
                new TodoService(repository),
                launcher,
                logger,
                quickPanel,
                new UpdateService(logger),
                visualStyles,
                new BoxPositionLockStateStore(drawerService, logger),
                paths,
                new DataStorageMigrationService(
                    paths,
                    repository,
                    new StorageLocationStore(Path.Combine(root, "storage-location.json"))),
                new AutoHideSettingsStore(drawerService));
            var box = (await drawerService.GetBoxesAsync())
                .First(candidate => candidate.Type == BoxType.Normal);
            var first = CreateSource(root, "first.txt");
            var second = CreateSource(root, "second.txt");
            var firstItem = await drawerService.ImportPathAsync(box.Id, first);
            var secondItem = await drawerService.ImportPathAsync(box.Id, second);

            await viewModel.BatchDeleteItemsAsync(
            [
                new DrawerItemViewModel(firstItem, box.Name, logger: logger),
                new DrawerItemViewModel(secondItem, box.Name, logger: logger)
            ]);

            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Empty(await drawerService.GetItemsAsync(box.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateSource(string root, string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private sealed class NoOpFileLauncher : IFileLauncher
    {
        public Task OpenAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
