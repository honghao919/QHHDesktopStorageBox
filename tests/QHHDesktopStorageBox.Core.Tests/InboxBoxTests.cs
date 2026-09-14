using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class InboxBoxTests
{
    [Fact]
    public async Task Inbox_UsesStoredFileBehaviorAndRestoresSource()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBoxInboxTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();
            var inbox = (await service.GetBoxesAsync())
                .Single(box => box.Type == BoxType.Inbox);
            var source = Path.Combine(root, "inbox-source.txt");
            await File.WriteAllTextAsync(source, "content");

            var item = await service.ImportPathAsync(inbox.Id, source);

            Assert.False(File.Exists(source));
            Assert.NotNull(item.StoredPath);
            Assert.True(File.Exists(item.StoredPath));

            await service.DeleteItemAsync(item.Id);

            Assert.True(File.Exists(source));
            Assert.Empty(await service.GetItemsAsync(inbox.Id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
