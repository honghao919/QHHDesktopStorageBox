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

public sealed class QuickPanelSearchTests
{
    [Fact]
    public async Task SearchText_DebouncesAndMatchesPinyinInitials()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox.QuickSearchTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(Path.Combine(root, "data"));
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var box = await drawerService.CreateBoxAsync("Search", BoxType.Mapping);
            var chinese = Path.Combine(root, "工作文档.pdf");
            var other = Path.Combine(root, "notes.txt");
            await File.WriteAllTextAsync(chinese, "a");
            await File.WriteAllTextAsync(other, "b");
            await drawerService.ImportPathAsync(box.Id, chinese);
            await drawerService.ImportPathAsync(box.Id, other);
            var logger = NullAppLogger.Instance;
            var viewModel = new QuickPanelViewModel(
                drawerService,
                new NoOpFileLauncher(),
                logger,
                new BoxVisualStyleStore(drawerService, logger));
            await viewModel.LoadAsync();

            viewModel.SearchText = "gzwd";
            await WaitUntilAsync(
                () => viewModel.Items.Count == 1
                    && viewModel.Items[0].DisplayName == "工作文档.pdf",
                TimeSpan.FromSeconds(3));

            Assert.Single(viewModel.Items);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), "Condition was not met before the timeout.");
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
