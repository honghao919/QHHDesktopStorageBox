using QHHDesktopStorageBox.Core.Services;

namespace QHHDesktopStorageBox.App.ViewModels;

public sealed class ImportPreflightRequestedEventArgs(
    FileMovePreflightResult result,
    TaskCompletionSource<ImportConflictPolicy?> completion) : EventArgs
{
    public FileMovePreflightResult Result { get; } = result;

    public TaskCompletionSource<ImportConflictPolicy?> Completion { get; } = completion;
}
