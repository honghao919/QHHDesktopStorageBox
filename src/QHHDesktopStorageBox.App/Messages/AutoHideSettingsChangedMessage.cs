using QHHDesktopStorageBox.App.Infrastructure;

namespace QHHDesktopStorageBox.App.Messages;

public sealed record AutoHideSettingsChangedMessage(
    bool IsEnabled,
    int HiddenPercent,
    AutoHideRevealScope RevealScope,
    bool FadeWholeBox,
    bool FadeTitle,
    bool FadeBorder);