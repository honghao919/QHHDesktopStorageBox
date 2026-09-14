using Microsoft.Data.Sqlite;
using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class DrawerRepositorySchemaBackupTests
{
    [Fact]
    public async Task InitializeAsync_BacksUpDatabaseBeforeSchemaUpgrade()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            await repository.InitializeAsync();
            await ExecuteNonQueryAsync(paths.DatabasePath, "PRAGMA user_version = 0;");

            await repository.InitializeAsync();

            var backupDirectory = Path.Combine(root, "Backups");
            var backupPath = Assert.Single(
                Directory.EnumerateFiles(
                    backupDirectory,
                    "qhhdesktopstoragebox-before-schema-*.db"));
            Assert.True(new FileInfo(backupPath).Length > 0);
            Assert.Equal(0L, await ExecuteScalarAsync(backupPath, "PRAGMA user_version;"));
            Assert.Equal(1L, await ExecuteScalarAsync(paths.DatabasePath, "PRAGMA user_version;"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task ExecuteNonQueryAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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
