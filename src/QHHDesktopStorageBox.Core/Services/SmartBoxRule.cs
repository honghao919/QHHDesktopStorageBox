namespace QHHDesktopStorageBox.Core.Services;

public sealed record SmartBoxRule(
    string RootDirectory,
    string NameContains = "",
    string Extensions = "",
    int ModifiedWithinDays = 0,
    int MaxItems = 300);
