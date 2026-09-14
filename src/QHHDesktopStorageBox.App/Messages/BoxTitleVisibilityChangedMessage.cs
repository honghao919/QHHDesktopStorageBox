namespace QHHDesktopStorageBox.App.Messages;

public sealed record BoxTitleVisibilityChangedMessage(
    Guid BoxId,
    bool IsVisible);
