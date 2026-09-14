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

public sealed class QuickPanelIncrementalRefreshTests
{
    [Fact]
    public async Task RefreshBoxAsync_ReplacesOnlyTheAffectedBoxItems()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox.QuickPanelRefreshTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var firstBox = await drawerService.CreateBoxAsync("First", BoxType.Mapping);
            var secondBox = await drawerService.CreateBoxAsync("Second", BoxType.Mapping);
            var firstPath = Path.Combine(root, "first.txt");
            var secondPath = Path.Combine(root, "second.txt");
            var addedPath = Path.Combine(root, "added.txt");
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(secondPath, "second");
            await File.WriteAllTextAsync(addedPath, "added");
            await drawerService.ImportPathAsync(firstBox.Id, firstPath);
            await drawerService.ImportPathAsync(secondBox.Id, secondPath);
            var logger = new RecordingLogger();
            var viewModel = new QuickPanelViewModel(
                drawerService,
                new NoOpFileLauncher(),
                logger,
                new BoxVisualStyleStore(drawerService, logger));
            await viewModel.LoadAsync();
            var unaffectedItem = Assert.Single(
                viewModel.Items,
                item => item.Model.BoxId == secondBox.Id);

            await drawerService.ImportPathAsync(firstBox.Id, addedPath);
            await viewModel.RefreshBoxAsync(firstBox.Id);

            Assert.Equal(
                2,
                viewModel.Items.Count(item => item.Model.BoxId == firstBox.Id));
            Assert.Same(
                unaffectedItem,
                Assert.Single(
                    viewModel.Items,
                    item => item.Model.BoxId == secondBox.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Error(Exception exception, string message)
        {
        }
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
