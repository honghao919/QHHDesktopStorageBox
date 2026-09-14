using System;

namespace QHHDesktopStorageBox.App.Messages;

public sealed record BoxLayoutPresetChangedMessage(Guid BoxId, string Preset);
