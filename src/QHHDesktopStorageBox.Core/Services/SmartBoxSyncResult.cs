namespace QHHDesktopStorageBox.Core.Services;

public sealed record SmartBoxSyncResult(
    int ScannedCount,
    int MatchedCount,
    int AddedCount,
    int RemovedCount);
