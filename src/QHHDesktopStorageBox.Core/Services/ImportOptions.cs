namespace QHHDesktopStorageBox.Core.Services;

public sealed record ImportOptions(ImportConflictPolicy ConflictPolicy)
{
    public static ImportOptions Default { get; } =
        new(ImportConflictPolicy.AutoRename);
}
