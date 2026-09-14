using QHHDesktopStorageBox.Core.Models;

namespace QHHDesktopStorageBox.Core.Services;

public sealed record FileOperationUndoData(
    DrawerItem? BeforeItem,
    DrawerItem? AfterItem,
    string? BeforeStoredPath,
    string? AfterPath);
