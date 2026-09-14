using QHHDesktopStorageBox.App.ViewModels;
using QHHDesktopStorageBox.Core.Services;

namespace QHHDesktopStorageBox.App.Tests;

public sealed class AutomaticUpdateCheckTests
{
    [Fact]
    public void ShouldCheckForUpdates_AllowsFirstCheck()
    {
        Assert.True(MainViewModel.ShouldCheckForUpdates(
            lastCheck: null,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ShouldCheckForUpdates_WaitsForConfiguredInterval()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(MainViewModel.ShouldCheckForUpdates(
            now - MainViewModel.AutomaticUpdateCheckInterval + TimeSpan.FromMinutes(1),
            now));
        Assert.True(MainViewModel.ShouldCheckForUpdates(
            now - MainViewModel.AutomaticUpdateCheckInterval,
            now));
        Assert.True(MainViewModel.ShouldCheckForUpdates(
            now + TimeSpan.FromMinutes(5),
            now));
    }

    [Fact]
    public void UpdateCheckResult_DefaultsToFailed()
    {
        Assert.False(new UpdateCheckResult().IsSuccessful);
        Assert.True(new UpdateCheckResult { IsSuccessful = true }.IsSuccessful);
    }
}
