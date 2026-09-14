using QHHDesktopStorageBox.Core;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class AppPathsMigrationTests
{
    [Fact]
    public void ResolveAndMigrateLegacyDefaultRoot_MovesLegacyDirectoryAndDatabase()
    {
        var localAppData = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBoxPathTests",
            Guid.NewGuid().ToString("N"));
        var legacyRoot = Path.Combine(
            localAppData,
            AppPaths.LegacyDefaultRootDirectoryName);
        Directory.CreateDirectory(legacyRoot);
        var legacyDatabase = Path.Combine(
            legacyRoot,
            AppPaths.LegacyDatabaseFileName);
        File.WriteAllText(legacyDatabase, "database");

        try
        {
            var resolved = AppPaths.ResolveAndMigrateLegacyDefaultRoot(localAppData);

            Assert.Equal(
                Path.Combine(localAppData, AppPaths.DefaultRootDirectoryName),
                resolved);
            Assert.False(Directory.Exists(legacyRoot));
            Assert.True(File.Exists(Path.Combine(
                resolved,
                AppPaths.DatabaseFileName)));
        }
        finally
        {
            if (Directory.Exists(localAppData))
            {
                Directory.Delete(localAppData, recursive: true);
            }
        }
    }
}
