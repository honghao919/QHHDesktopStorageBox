using System.Diagnostics;
using QHHDesktopStorageBox.Core.Logging;
using QHHDesktopStorageBox.Core.Services;

namespace QHHDesktopStorageBox.Core.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("https://github.com/honghao919/QHHDesktopStorageBox/releases/download/v1.0.2/app.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/123/abc", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/123/abc", true)]
    [InlineData("http://github.com/honghao919/QHHDesktopStorageBox/releases/download/v1.0.2/app.zip", false)]
    [InlineData("https://attacker.githubusercontent.com/update.zip", false)]
    [InlineData("https://evil.example/update.zip", false)]
    [InlineData("https://github.com/other/other/releases/download/v1.0.2/app.zip", false)]
    [InlineData("not-a-url", false)]
    public void IsAllowedDownloadUrl_FiltersUnexpectedHosts(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsAllowedDownloadUrl(url));
    }

    [Fact]
    public void BuildUpdaterScript_WaitsForGracefulExitAndUsesRecoverableOverlay()
    {
        var script = UpdateService.BuildUpdaterScript();

        Assert.Contains("QHH_DESKTOP_STORAGE_BOX_EXIT_WAIT_SECONDS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("taskkill", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/MIR", script, StringComparison.Ordinal);
        Assert.Contains(
            "robocopy \"%QHH_DESKTOP_STORAGE_BOX_APP_DIR%\" \"%QHH_DESKTOP_STORAGE_BOX_ROLLBACK%\" /E",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "robocopy \"%QHH_DESKTOP_STORAGE_BOX_PAYLOAD%\" \"%QHH_DESKTOP_STORAGE_BOX_APP_DIR%\" /E",
            script,
            StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto backup_failed", script, StringComparison.Ordinal);
        Assert.Contains("if errorlevel 8 goto apply_failed", script, StringComparison.Ordinal);
        Assert.Contains("QHH_DESKTOP_STORAGE_BOX_STARTUP_SUCCESS_MARKER", script, StringComparison.Ordinal);
        Assert.Contains("Start-Process", script, StringComparison.Ordinal);
        Assert.Contains("Test-Path", script, StringComparison.Ordinal);
        Assert.Contains(":rollback", script, StringComparison.Ordinal);
        Assert.Contains(
            "robocopy \"%QHH_DESKTOP_STORAGE_BOX_ROLLBACK%\" \"%QHH_DESKTOP_STORAGE_BOX_APP_DIR%\" /E",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.3.1", "1.3", true)]
    [InlineData("1.3", "1.3.0", false)]
    [InlineData("1.3.0", "1.3", false)]
    [InlineData("1.3.5.0", "1.3.5", false)]
    [InlineData("1.3.5.1", "1.3.5", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.2.9", "1.3", false)]
    public void IsNewerVersion_TreatsMissingComponentsAsZero(
        string remote,
        string current,
        bool expected)
    {
        Assert.Equal(
            expected,
            UpdateService.IsNewerVersion(Version.Parse(remote), Version.Parse(current)));
    }

    [Fact]
    public void CreateUpdaterStartInfo_UsesHiddenCmdWithoutShellExecution()
    {
        const string tempRoot = @"C:\Temp\QHHDesktopStorageBox Update\run";
        const string updaterPath = tempRoot + @"\updater.bat";
        const string payloadPath = tempRoot + @"\payload";
        const string appDirectory = @"D:\应用\QHHDesktopStorageBox";
        const string appExecutable = appDirectory + @"\QHHDesktopStorageBox.App.exe";
        const string executableName = "QHHDesktopStorageBox.App.exe";
        const string logPath = @"C:\Users\Test\AppData\Local\QHHDesktopStorageBox\Logs\updater.log";

        var startInfo = UpdateService.CreateUpdaterStartInfo(
            updaterPath,
            tempRoot,
            payloadPath,
            appDirectory,
            appExecutable,
            executableName,
            logPath,
            appProcessId: 0,
            appProcessStartTimeUtcTicks: 0);

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(updaterPath), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, startInfo.WindowStyle);
        Assert.Equal($"/d /s /c \"\"{updaterPath}\"\"", startInfo.Arguments);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal(tempRoot, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_UPDATE_ROOT"]);
        Assert.Equal(payloadPath, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_PAYLOAD"]);
        Assert.Equal(appDirectory, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_APP_DIR"]);
        Assert.Equal(appExecutable, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_APP_EXE"]);
        Assert.Equal(executableName, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_EXE_NAME"]);
        Assert.Equal(logPath, startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_UPDATE_LOG"]);
        Assert.Equal("0", startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_APP_PID"]);
        Assert.Equal("0", startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_APP_START_TIME_UTC_TICKS"]);
        Assert.Equal(
            Path.Combine(tempRoot, "startup-succeeded.marker"),
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_STARTUP_SUCCESS_MARKER"]);
    }

    [Fact]
    public async Task ConfirmUpdateStartupAsync_WritesMarkerInsideGeneratedUpdateRoot()
    {
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBoxUpdate",
            Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(updateRoot, "startup-succeeded.marker");
        Directory.CreateDirectory(updateRoot);

        try
        {
            var service = new UpdateService(new ThrowingLogger());

            var confirmed = await service.ConfirmUpdateStartupAsync(markerPath);

            Assert.True(confirmed);
            Assert.True(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(updateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ConfirmUpdateStartupAsync_RejectsMarkerOutsideGeneratedUpdateRoot()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox Invalid Update Marker Tests",
            Guid.NewGuid().ToString("N"));
        var markerPath = Path.Combine(testRoot, "startup-succeeded.marker");
        Directory.CreateDirectory(testRoot);

        try
        {
            var service = new UpdateService(NullAppLogger.Instance);

            var confirmed = await service.ConfirmUpdateStartupAsync(markerPath);

            Assert.False(confirmed);
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void CleanupLegacyUpdaterArtifacts_DeletesOnlyKnownResidue()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox Legacy Cleanup Tests",
            Guid.NewGuid().ToString("N"));
        var appDirectory = Path.Combine(testRoot, "应用目录");
        Directory.CreateDirectory(appDirectory);

        try
        {
            var zipPath = Path.Combine(appDirectory, "update.zip");
            var updaterPath = Path.Combine(appDirectory, "updater.bat");
            var appPath = Path.Combine(appDirectory, "QHHDesktopStorageBox.App.exe");
            File.WriteAllText(zipPath, "legacy");
            File.WriteAllText(updaterPath, "legacy");
            File.WriteAllText(appPath, "keep");

            var removedCount = UpdateService.CleanupLegacyUpdaterArtifacts(appDirectory);

            Assert.Equal(2, removedCount);
            Assert.False(File.Exists(zipPath));
            Assert.False(File.Exists(updaterPath));
            Assert.True(File.Exists(appPath));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupStaleUpdateArtifacts_DeletesOnlyExpiredContent()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox Cleanup Tests",
            Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "updates");
        var tempDirectory = Path.Combine(testRoot, "temp");
        var oldUpdate = Path.Combine(updateRoot, Guid.NewGuid().ToString("N"));
        var currentUpdate = Path.Combine(updateRoot, Guid.NewGuid().ToString("N"));
        var oldScript = Path.Combine(
            tempDirectory,
            $"QHHDesktopStorageBoxUpdater-{Guid.NewGuid():N}.bat");
        var currentScript = Path.Combine(
            tempDirectory,
            $"QHHDesktopStorageBoxUpdater-{Guid.NewGuid():N}.bat");
        Directory.CreateDirectory(oldUpdate);
        Directory.CreateDirectory(currentUpdate);
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(oldUpdate, "payload.bin"), "old");
        File.WriteAllText(Path.Combine(currentUpdate, "payload.bin"), "current");
        File.WriteAllText(oldScript, "old");
        File.WriteAllText(currentScript, "current");
        var now = DateTimeOffset.UtcNow;
        Directory.SetLastWriteTimeUtc(oldUpdate, now.UtcDateTime.AddDays(-8));
        File.SetLastWriteTimeUtc(oldScript, now.UtcDateTime.AddDays(-2));

        try
        {
            var removed = UpdateService.CleanupStaleUpdateArtifacts(
                updateRoot,
                tempDirectory,
                now);

            Assert.Equal(2, removed);
            Assert.False(Directory.Exists(oldUpdate));
            Assert.False(File.Exists(oldScript));
            Assert.True(Directory.Exists(currentUpdate));
            Assert.True(File.Exists(currentScript));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_OverlaysPayloadAndPreservesUnrelatedFiles()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "QHHDesktopStorageBox Update Script Tests",
            Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "应用目录");
        var updaterPath = Path.Combine(testRoot, "QHHDesktopStorageBox Updater.cmd");
        var executableName = "QHHDesktopStorageBox.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var payloadExecutablePath = Path.Combine(payloadDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                payloadExecutablePath,
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo started\r\n>\"%QHH_DESKTOP_STORAGE_BOX_STARTUP_SUCCESS_MARKER%\" echo ready\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "update.zip"), "legacy");
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "updater.bat"), "legacy");
            var stalePayloadPath = Path.Combine(appDirectory, "stale-old-version.dll");
            await File.WriteAllTextAsync(stalePayloadPath, "stale");
            var replacedPath = Path.Combine(appDirectory, "QHHDesktopStorageBox.Core.dll");
            await File.WriteAllTextAsync(replacedPath, "old");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, "QHHDesktopStorageBox.Core.dll"),
                "new");

            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            var updaterLog = File.Exists(logPath)
                ? await ReadAllTextWithRetryAsync(logPath)
                : "Updater log was not created.";
            Assert.True(
                updaterProcess.ExitCode == 0,
                $"Updater exited with code {updaterProcess.ExitCode}.{Environment.NewLine}{updaterLog}");
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(appExecutablePath));
            Assert.False(File.Exists(Path.Combine(appDirectory, "update.zip")));
            Assert.False(File.Exists(Path.Combine(appDirectory, "updater.bat")));
            Assert.Equal("new", await ReadAllTextWithRetryAsync(replacedPath));
            Assert.True(File.Exists(stalePayloadPath));
            Assert.Equal("stale", await ReadAllTextWithRetryAsync(stalePayloadPath));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_WaitsForOriginalProcessExitBeforeApplyingPayload()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "QHHDesktopStorageBox Update Wait Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "QHHDesktopStorageBox.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo started\r\n>\"%QHH_DESKTOP_STORAGE_BOX_STARTUP_SUCCESS_MARKER%\" echo ready\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());
            await File.WriteAllTextAsync(appExecutablePath, "old");

            using var originalProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 3\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(originalProcess);

            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                originalProcess.Id,
                originalProcess.StartTime.ToUniversalTime().Ticks);
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await Task.Delay(500);
            Assert.False(File.Exists(markerPath));
            Assert.Equal("old", await ReadAllTextWithRetryAsync(appExecutablePath));

            await originalProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, updaterProcess.ExitCode);
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestoresPreviousInstallationAfterPayloadCopyFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "QHHDesktopStorageBox Update Rollback Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "QHHDesktopStorageBox.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");
        var firstAppPath = Path.Combine(appDirectory, "a-first.dll");
        var introducedAppPath = Path.Combine(appDirectory, "b-introduced.dll");
        var lockedPayloadPath = Path.Combine(payloadDirectory, "z-locked.dll");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(firstAppPath, "old-first");
            await File.WriteAllTextAsync(Path.Combine(appDirectory, "z-locked.dll"), "old-locked");
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo restored\r\nexit\r\n");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "a-first.dll"), "new-first");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "b-introduced.dll"), "new-introduced");
            await File.WriteAllTextAsync(lockedPayloadPath, "new-locked");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo updated\r\nexit\r\n");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());

            await using var lockStream = new FileStream(
                lockedPayloadPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await WaitForConditionAsync(() => File.Exists(introducedAppPath), TimeSpan.FromSeconds(5));
            Assert.Equal("new-introduced", await ReadAllTextWithRetryAsync(introducedAppPath));
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Equal("old-first", await ReadAllTextWithRetryAsync(firstAppPath));
            await WaitForConditionAsync(() => !File.Exists(introducedAppPath), TimeSpan.FromSeconds(10));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("restored", (await ReadAllTextWithRetryAsync(markerPath)).Trim());
            Assert.True(Directory.Exists(Path.Combine(updateRoot, "rollback")));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestartsOriginalApplicationWhenBackupFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "QHHDesktopStorageBox Update Backup Failure Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "QHHDesktopStorageBox.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");
        var lockedAppPath = Path.Combine(appDirectory, "z-locked.dll");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo original\r\nexit\r\n");
            await File.WriteAllTextAsync(lockedAppPath, "old-locked");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo updated\r\nexit\r\n");
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "z-locked.dll"), "new-locked");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());

            await using var lockStream = new FileStream(
                lockedAppPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Contains("echo original", await ReadAllTextWithRetryAsync(appExecutablePath));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("original", (await ReadAllTextWithRetryAsync(markerPath)).Trim());
            Assert.True(Directory.Exists(updateRoot));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UpdaterScript_RestoresOriginalApplicationWhenUpdatedStartupIsNotConfirmed()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "QHHDesktopStorageBox Update Startup Failure Tests", Guid.NewGuid().ToString("N"));
        var updateRoot = Path.Combine(testRoot, "update");
        var payloadDirectory = Path.Combine(updateRoot, "payload");
        var appDirectory = Path.Combine(testRoot, "app");
        var updaterPath = Path.Combine(testRoot, "updater.cmd");
        var executableName = "QHHDesktopStorageBox.TestTarget.cmd";
        var appExecutablePath = Path.Combine(appDirectory, executableName);
        var markerPath = Path.Combine(testRoot, "target-started.txt");
        var logPath = Path.Combine(testRoot, "updater.log");

        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(appDirectory);

        try
        {
            await File.WriteAllTextAsync(
                appExecutablePath,
                "@echo off\r\n>\"%QHH_DESKTOP_STORAGE_BOX_TEST_MARKER%\" echo restored\r\nexit\r\n");
            await File.WriteAllTextAsync(
                Path.Combine(payloadDirectory, executableName),
                "@echo off\r\nexit /b 1\r\n");
            await File.WriteAllTextAsync(updaterPath, UpdateService.BuildUpdaterScript());

            var startInfo = UpdateService.CreateUpdaterStartInfo(
                updaterPath,
                updateRoot,
                payloadDirectory,
                appDirectory,
                appExecutablePath,
                executableName,
                logPath,
                appProcessId: 0,
                appProcessStartTimeUtcTicks: 0);
            startInfo.Environment["QHH_DESKTOP_STORAGE_BOX_TEST_MARKER"] = markerPath;

            using var updaterProcess = Process.Start(startInfo);
            Assert.NotNull(updaterProcess);
            await updaterProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            Assert.NotEqual(0, updaterProcess.ExitCode);
            Assert.Contains("echo restored", await ReadAllTextWithRetryAsync(appExecutablePath));
            await WaitForConditionAsync(() => File.Exists(markerPath), TimeSpan.FromSeconds(5));
            Assert.Equal("restored", (await ReadAllTextWithRetryAsync(markerPath)).Trim());
            Assert.True(Directory.Exists(Path.Combine(updateRoot, "rollback")));
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(testRoot, TimeSpan.FromSeconds(5));
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for updater condition.");
            await Task.Delay(50);
        }
    }

    private static async Task<string> ReadAllTextWithRetryAsync(
        string path,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            try
            {
                return await File.ReadAllTextAsync(path);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Directory.Exists(path) && DateTime.UtcNow < deadline)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(50);
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup must not fail an otherwise valid updater assertion.
        }
        catch (UnauthorizedAccessException)
        {
            // Antivirus and Windows shell services may release handles after process exit.
        }
    }

    private sealed class ThrowingLogger : IAppLogger
    {
        public void Info(string message)
        {
            throw new IOException("log write failed");
        }

        public void Error(Exception exception, string message)
        {
            throw new IOException("log write failed");
        }
    }
}
