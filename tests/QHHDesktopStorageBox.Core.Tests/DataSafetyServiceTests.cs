using System.IO.Compression;
using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class DataSafetyServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_IncludesDatabaseBoxesAndManifest()
    {
        var root = CreateTempDirectory();
        var destination = Path.Combine(CreateTempDirectory(), "backup.zip");
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var storedFile = Path.Combine(paths.BoxesDirectory, "box-file.txt");
            await File.WriteAllTextAsync(storedFile, "stored");
            var store = new StorageLocationStore(Path.Combine(root, "storage-location.json"));
            var service = new DataSafetyService(paths, repository, store);

            var result = await service.CreateBackupAsync(destination, "1.3.14");

            Assert.True(File.Exists(destination));
            Assert.True(result.SizeBytes > 0);
            using var archive = ZipFile.OpenRead(destination);
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == AppPaths.DatabaseFileName);
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == "Boxes/box-file.txt");
            Assert.Contains(
                archive.Entries,
                entry => entry.FullName == "backup-manifest.json");
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(Path.GetDirectoryName(destination)!);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_ExtractsToEmptyTargetAndUpdatesBootstrap()
    {
        var root = CreateTempDirectory();
        var archiveRoot = CreateTempDirectory();
        var bootstrapRoot = CreateTempDirectory();
        var targetRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var storedFile = Path.Combine(paths.BoxesDirectory, "restored.txt");
            await File.WriteAllTextAsync(storedFile, "restored");
            var archivePath = Path.Combine(archiveRoot, "backup.zip");
            var store = new StorageLocationStore(
                Path.Combine(bootstrapRoot, StorageLocationStore.ConfigFileName));
            var service = new DataSafetyService(paths, repository, store);
            await service.CreateBackupAsync(archivePath, "1.3.14");

            var restoredPaths = await service.RestoreBackupAsync(archivePath, targetRoot);

            Assert.Equal(Path.GetFullPath(targetRoot), restoredPaths.RootDirectory);
            Assert.True(File.Exists(restoredPaths.DatabasePath));
            Assert.Equal(
                "restored",
                await File.ReadAllTextAsync(
                    Path.Combine(restoredPaths.BoxesDirectory, "restored.txt")));
            Assert.Equal(Path.GetFullPath(targetRoot), store.LoadConfiguredDirectory());
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(archiveRoot);
            DeleteDirectory(bootstrapRoot);
            DeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public async Task ScanBrokenReferencesAsync_ReportsMissingMappingReference()
    {
        var root = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var drawerService = new DrawerService(paths, repository);
            await drawerService.InitializeAsync();
            var mappingBox = await drawerService.CreateBoxAsync(
                "映射测试",
                BoxType.Mapping);
            var sourceFile = Path.Combine(sourceRoot, "will-be-missing.txt");
            await File.WriteAllTextAsync(sourceFile, "reference");
            await drawerService.ImportPathAsync(mappingBox.Id, sourceFile);
            File.Delete(sourceFile);
            var store = new StorageLocationStore(Path.Combine(root, "storage-location.json"));
            var service = new DataSafetyService(paths, repository, store);

            var result = await service.ScanBrokenReferencesAsync();

            var missing = Assert.Single(result.MissingReferences);
            Assert.Equal(mappingBox.Id, missing.BoxId);
            Assert.Equal("will-be-missing.txt", missing.DisplayName);
            Assert.Equal(1, result.ScannedReferenceCount);
        }
        finally
        {
            DeleteDirectory(root);
            DeleteDirectory(sourceRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
