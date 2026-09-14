using QHHDesktopStorageBox.App.ViewModels;

namespace QHHDesktopStorageBox.App.Messages;

public sealed record DrawerSortModeChangedMessage(
    Guid BoxId,
    DrawerItemSortMode SortMode);
