namespace QHHDesktopStorageBox.Core.Models;

public enum FileOperationKind
{
    Import = 0,
    Move = 1,
    Export = 2,
    Delete = 3
}

public sealed record FileOperationRecord(
    Guid Id,
    FileOperationKind Kind,
    string Description,
    string PayloadJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UndoneAt = null);
