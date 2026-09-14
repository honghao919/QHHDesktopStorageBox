using System.Collections.Concurrent;
using System.Text.Json;
using QHHDesktopStorageBox.Core.Abstractions;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Storage;

namespace QHHDesktopStorageBox.Core.Services;

public sealed class DrawerService
{
    private const string SmartBoxRuleSettingPrefix = "SmartBoxRule:";
    private static readonly TimeSpan BoxPruneInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan GlobalPruneInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SmartBoxAutoSyncInterval = TimeSpan.FromSeconds(5);
    private readonly AppPaths _paths;
    private readonly DrawerRepository _repository;
    private readonly AsyncLocal<bool> _suppressHistory = new();
    private readonly ConcurrentDictionary<Guid, long> _lastPruneTicks = new();
    private readonly ConcurrentDictionary<Guid, long> _lastSmartSyncTicks = new();
    private long _lastGlobalPruneTick;

    public DrawerService(AppPaths paths, DrawerRepository repository)
    {
        _paths = paths;
        _repository = repository;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await _repository.InitializeAsync(cancellationToken);
        await RepairStoredPathsAsync(cancellationToken);
        await EnsureDefaultBoxesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetBoxesAsync(cancellationToken);
    }

    public async Task ReorderBoxesAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedBoxIds);

        var requestedIds = orderedBoxIds.ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new ArgumentException("Box order cannot contain duplicate ids.", nameof(orderedBoxIds));
        }

        var existingBoxes = await _repository.GetBoxesAsync(cancellationToken);
        var existingIds = existingBoxes.Select(box => box.Id).ToHashSet();
        if (requestedIds.Length != existingIds.Count || requestedIds.Any(id => !existingIds.Contains(id)))
        {
            throw new ArgumentException(
                "Box order must contain every existing box exactly once.",
                nameof(orderedBoxIds));
        }

        await _repository.UpdateBoxSortOrdersAsync(requestedIds, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetItemsAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        return await PruneMissingStoredItemsAsync(items, boxId, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetItemsAsync(null, cancellationToken);
        return await PruneMissingStoredItemsAsync(items, null, cancellationToken);
    }

    public async Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(string query, int limit = 200, CancellationToken cancellationToken = default)
    {
        await PruneMissingStoredItemsAsync(null, cancellationToken);
        return await _repository.SearchItemsAsync(query.Trim(), limit, cancellationToken);
    }

    public async Task<Box> CreateBoxAsync(string name, BoxType type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(name));
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var storagePath = type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer or BoxType.Inbox
            ? Path.Combine(_paths.BoxesDirectory, id.ToString("N"))
            : null;
        if (storagePath is not null)
        {
            Directory.CreateDirectory(storagePath);
        }

        var box = new Box(
            id,
            name.Trim(),
            type,
            storagePath,
            await _repository.GetNextBoxSortOrderAsync(cancellationToken),
            now,
            now);

        await _repository.AddBoxAsync(box, cancellationToken);
        return box;
    }

    public async Task<DrawerItem> ImportPathAsync(
        Guid boxId,
        string sourcePath,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        var item = await ImportPathCoreAsync(
            boxId,
            sourcePath,
            ImportOptions.Default,
            gridColumn,
            gridRow,
            cancellationToken);
        return item ?? throw new InvalidOperationException("Import was skipped unexpectedly.");
    }

    public Task<DrawerItem?> TryImportPathAsync(
        Guid boxId,
        string sourcePath,
        ImportOptions options,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ImportPathCoreAsync(
            boxId,
            sourcePath,
            options,
            gridColumn,
            gridRow,
            cancellationToken);
    }

    public Task<FileMovePreflightResult> PreflightImportAsync(
        Guid boxId,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        return new FileMovePreflightService(_paths, _repository)
            .AnalyzeImportAsync(boxId, sourcePaths, cancellationToken);
    }

    public Task<SmartBoxRule?> GetSmartBoxRuleAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        return GetSmartBoxRuleCoreAsync(boxId, cancellationToken);
    }

    public async Task SaveSmartBoxRuleAsync(
        Guid boxId,
        SmartBoxRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type != BoxType.Smart)
        {
            throw new InvalidOperationException("Rules can only be saved for smart boxes.");
        }

        var normalized = NormalizeSmartBoxRule(rule);
        await _repository.SetSettingAsync(
            SmartBoxRuleSettingPrefix + boxId.ToString("N"),
            JsonSerializer.Serialize(normalized),
            cancellationToken);
    }

    public Task<SmartBoxSyncResult> SyncSmartBoxAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        return SyncSmartBoxCoreAsync(boxId, force: true, cancellationToken);
    }

    public Task<SmartBoxSyncResult> SyncSmartBoxIfStaleAsync(
        Guid boxId,
        CancellationToken cancellationToken = default)
    {
        return SyncSmartBoxCoreAsync(boxId, force: false, cancellationToken);
    }

    private async Task<SmartBoxSyncResult> SyncSmartBoxCoreAsync(
        Guid boxId,
        bool force,
        CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type != BoxType.Smart)
        {
            throw new InvalidOperationException("Only smart boxes can be synchronized.");
        }

        var rule = await GetSmartBoxRuleCoreAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Smart box rule has not been configured.");
        if (!Directory.Exists(rule.RootDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Smart box root directory does not exist: {rule.RootDirectory}");
        }

        var tickNow = Environment.TickCount64;
        if (!force
            && !ShouldRun(
                _lastSmartSyncTicks,
                boxId,
                tickNow,
                SmartBoxAutoSyncInterval))
        {
            return new SmartBoxSyncResult(0, 0, 0, 0);
        }

        var matches = await Task.Run(
            () => FindSmartBoxMatches(rule, cancellationToken),
            cancellationToken);
        var existingItems = await _repository.GetItemsAsync(boxId, cancellationToken);
        var existingByPath = existingItems
            .Where(item => !string.IsNullOrWhiteSpace(item.SourcePath))
            .GroupBy(
                item => Path.GetFullPath(item.SourcePath!),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var stale in existingItems.Where(item =>
                     string.IsNullOrWhiteSpace(item.SourcePath)
                     || !matches.Paths.Contains(
                         Path.GetFullPath(item.SourcePath!),
                         StringComparer.OrdinalIgnoreCase)))
        {
            await _repository.RemoveItemAsync(stale.Id, cancellationToken);
            removed++;
        }

        var added = 0;
        var nextSortOrder = await _repository.GetNextItemSortOrderAsync(
            boxId,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var path in matches.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingByPath.ContainsKey(path))
            {
                continue;
            }

            var item = new DrawerItem(
                Guid.NewGuid(),
                boxId,
                Path.GetFileName(path),
                ItemKind.File,
                path,
                null,
                nextSortOrder++,
                now,
                now);
            await _repository.AddItemAsync(item, cancellationToken);
            added++;
        }

        return new SmartBoxSyncResult(
            ScannedCount: matches.ScannedCount,
            MatchedCount: matches.Paths.Count,
            AddedCount: added,
            RemovedCount: removed);
    }

    private async Task<DrawerItem?> ImportPathCoreAsync(
        Guid boxId,
        string sourcePath,
        ImportOptions options,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        if (box.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Todo boxes do not accept files.");
        }

        if (box.Type == BoxType.Smart)
        {
            throw new InvalidOperationException(
                "Smart boxes are maintained by rules and do not accept manual imports.");
        }

        var fullSourcePath = PathSafety.GetFullExistingPath(sourcePath);
        var isDirectory = Directory.Exists(fullSourcePath);
        var itemKind = isDirectory ? ItemKind.Directory : ItemKind.File;
        var displayName = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sortOrder = await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        DrawerItem item;
        if (box.Type == BoxType.Mapping || box.Type == BoxType.Smart)
        {
            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                displayName,
                itemKind,
                fullSourcePath,
                null,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);
        }
        else
        {
            var storageRoot = box.StoragePath ?? Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            Directory.CreateDirectory(storageRoot);
            if (options.ConflictPolicy == ImportConflictPolicy.Skip
                && FileNameService.DestinationExists(storageRoot, displayName))
            {
                return null;
            }

            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SafeFileOps.MoveAsync(
                    fullSourcePath,
                    targetPath,
                    isDirectory,
                    cancellationToken);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                throw new InvalidOperationException(
                    BuildSharingViolationMessage(displayName, isDirectory),
                    exception);
            }

            item = new DrawerItem(
                Guid.NewGuid(),
                box.Id,
                Path.GetFileName(targetPath),
                itemKind,
                fullSourcePath,
                targetPath,
                sortOrder,
                now,
                now,
                gridColumn,
                gridRow);

            try
            {
                await _repository.AddItemAsync(item, CancellationToken.None);
            }
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory);
                throw;
            }

            await RecordFileOperationAsync(
                FileOperationKind.Import,
                $"导入 {item.DisplayName}",
                new FileOperationUndoData(
                    BeforeItem: null,
                    AfterItem: item,
                    BeforeStoredPath: null,
                    AfterPath: item.StoredPath),
                cancellationToken);
            return item;
        }

        await _repository.AddItemAsync(item, cancellationToken);
        await RecordFileOperationAsync(
            FileOperationKind.Import,
            $"导入 {item.DisplayName}",
            new FileOperationUndoData(
                BeforeItem: null,
                AfterItem: item,
                BeforeStoredPath: null,
                AfterPath: null),
            cancellationToken);
        return item;
    }

    public Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default)
    {
        return _repository.UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
    }

    public Task UpdateItemGridPositionsAsync(
        IReadOnlyDictionary<Guid, (int GridColumn, int GridRow)> positions,
        CancellationToken cancellationToken = default)
    {
        return _repository.UpdateItemGridPositionsAsync(positions, cancellationToken);
    }

    public async Task<DrawerItem> AddInstalledApplicationAsync(
        Guid boxId,
        string appUserModelId,
        string displayName,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        if (box.Type is not (BoxType.Normal or BoxType.Pixel or BoxType.Mapping or BoxType.Inbox))
        {
            throw new InvalidOperationException("This box type does not accept installed applications.");
        }

        var applicationIdentifier = appUserModelId.Trim();
        string sourcePath;
        if (Path.IsPathRooted(applicationIdentifier))
        {
            sourcePath = PathSafety.GetFullExistingPath(applicationIdentifier);
        }
        else
        {
            sourcePath = InstalledApplicationReference.Create(applicationIdentifier);
        }
        var existing = (await _repository.GetItemsAsync(boxId, cancellationToken))
            .FirstOrDefault(item => string.Equals(
                item.SourcePath,
                sourcePath,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (gridColumn is not null && gridRow is not null)
            {
                await _repository.UpdateItemGridPositionAsync(
                    existing.Id,
                    gridColumn,
                    gridRow,
                    cancellationToken);
            }

            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var item = new DrawerItem(
            Guid.NewGuid(),
            box.Id,
            displayName.Trim(),
            ItemKind.File,
            sourcePath,
            null,
            await _repository.GetNextItemSortOrderAsync(boxId, cancellationToken),
            now,
            now,
            gridColumn,
            gridRow);
        await _repository.AddItemAsync(item, cancellationToken);
        return item;
    }

    public async Task MoveItemToBoxAsync(
        Guid itemId,
        Guid targetBoxId,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");
        var sourceBox = await _repository.GetBoxAsync(item.BoxId, cancellationToken)
            ?? throw new InvalidOperationException("Source box does not exist.");
        var targetBox = await _repository.GetBoxAsync(targetBoxId, cancellationToken)
            ?? throw new InvalidOperationException("Target box does not exist.");

        if (sourceBox.Type == BoxType.Todo || targetBox.Type == BoxType.Todo)
        {
            throw new InvalidOperationException("Files cannot be moved into or out of a todo box.");
        }

        if (sourceBox.Type == BoxType.Smart || targetBox.Type == BoxType.Smart)
        {
            throw new InvalidOperationException(
                "Smart box references are updated by their rules and cannot be moved manually.");
        }

        if (item.BoxId == targetBoxId)
        {
            await UpdateItemGridPositionAsync(itemId, gridColumn, gridRow, cancellationToken);
            return;
        }

        var targetSortOrder = await _repository.GetNextItemSortOrderAsync(targetBoxId, cancellationToken);
        var sourcePath = item.SourcePath;
        var storedPath = item.StoredPath;
        var displayName = item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;

        if (InstalledApplicationReference.IsReference(item.SourcePath))
        {
            await _repository.MoveItemToBoxAsync(
                item,
                targetBox.Id,
                displayName,
                item.SourcePath,
                storedPath: null,
                targetSortOrder,
                gridColumn,
                gridRow,
                cancellationToken);
            return;
        }

        if (targetBox.Type == BoxType.Mapping)
        {
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                throw new InvalidOperationException("Stored items cannot be moved into a mapping box.");
            }

            storedPath = null;
        }
        else
        {
            if (sourceBox.Type == BoxType.Mapping)
            {
                throw new InvalidOperationException("Mapping references cannot be moved into a storage box.");
            }

            var sourceFilePath = item.EffectivePath;
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                throw new InvalidOperationException("Item has no file path.");
            }

            var fullSourcePath = PathSafety.GetFullExistingPath(sourceFilePath);
            if (!string.IsNullOrWhiteSpace(item.StoredPath))
            {
                PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullSourcePath);
            }

            var storageRoot = targetBox.StoragePath ?? Path.Combine(_paths.BoxesDirectory, targetBox.Id.ToString("N"));
            Directory.CreateDirectory(storageRoot);
            var targetPath = FileNameService.GetUniqueDestinationPath(storageRoot, displayName, isDirectory);
            PathSafety.EnsureChildPath(storageRoot, targetPath);

            await SafeFileOps.MoveAsync(fullSourcePath, targetPath, isDirectory, cancellationToken);

            displayName = Path.GetFileName(targetPath);
            storedPath = targetPath;

            try
            {
                await _repository.MoveItemToBoxAsync(
                    item,
                    targetBox.Id,
                    displayName,
                    sourcePath,
                    storedPath,
                    targetSortOrder,
                    gridColumn,
                    gridRow,
                    CancellationToken.None);
            }
            catch
            {
                await TryCompensateMoveAsync(targetPath, fullSourcePath, isDirectory);
                throw;
            }

            await RecordFileOperationAsync(
                FileOperationKind.Move,
                $"移动 {item.DisplayName} 到 {targetBox.Name}",
                new FileOperationUndoData(
                    BeforeItem: item,
                    AfterItem: item with
                    {
                        BoxId = targetBox.Id,
                        DisplayName = displayName,
                        SourcePath = sourcePath,
                        StoredPath = storedPath,
                        SortOrder = targetSortOrder,
                        GridColumn = gridColumn,
                        GridRow = gridRow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    },
                    BeforeStoredPath: item.StoredPath,
                    AfterPath: storedPath),
                cancellationToken);
            return;
        }

        await _repository.MoveItemToBoxAsync(
            item,
            targetBox.Id,
            displayName,
            sourcePath,
            storedPath,
            targetSortOrder,
            gridColumn,
            gridRow,
            cancellationToken);
        await RecordFileOperationAsync(
            FileOperationKind.Move,
            $"移动 {item.DisplayName} 到 {targetBox.Name}",
            new FileOperationUndoData(
                BeforeItem: item,
                AfterItem: item with
                {
                    BoxId = targetBox.Id,
                    DisplayName = displayName,
                    SourcePath = sourcePath,
                    StoredPath = storedPath,
                    SortOrder = targetSortOrder,
                    GridColumn = gridColumn,
                    GridRow = gridRow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                BeforeStoredPath: item.StoredPath,
                AfterPath: storedPath),
            cancellationToken);
    }

    public async Task<string> ExportItemToDirectoryAsync(
        Guid itemId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Only stored items can be exported.");
        }

        var sourcePath = PathSafety.GetFullExistingPath(item.StoredPath);
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, sourcePath);

        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(fullTargetDirectory);

        var displayName = string.IsNullOrWhiteSpace(item.DisplayName)
            ? Path.GetFileName(sourcePath)
            : item.DisplayName;
        var isDirectory = item.ItemKind == ItemKind.Directory;
        var targetPath = FileNameService.GetUniqueDestinationPath(fullTargetDirectory, displayName, isDirectory);
        PathSafety.EnsureChildPath(fullTargetDirectory, targetPath);

        cancellationToken.ThrowIfCancellationRequested();
        await SafeFileOps.MoveAsync(sourcePath, targetPath, isDirectory, cancellationToken);

        try
        {
            await _repository.RemoveItemAsync(itemId, CancellationToken.None);
        }
        catch
        {
            await TryCompensateMoveAsync(targetPath, sourcePath, isDirectory);
            throw;
        }

        await RecordFileOperationAsync(
            FileOperationKind.Export,
            $"导出 {item.DisplayName}",
            new FileOperationUndoData(
                BeforeItem: item,
                AfterItem: null,
                BeforeStoredPath: item.StoredPath,
                AfterPath: targetPath),
            cancellationToken);
        return targetPath;
    }

    public async Task<ItemDeleteResult> DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
            await RecordFileOperationAsync(
                FileOperationKind.Delete,
                $"删除引用 {item.DisplayName}",
                new FileOperationUndoData(
                    BeforeItem: item,
                    AfterItem: null,
                    BeforeStoredPath: null,
                    AfterPath: null),
                cancellationToken);
            return ItemDeleteResult.ReferenceRemoved(item.Id, item.DisplayName);
        }

        var restore = await RestoreStoredItemAsync(item, reservedTargets: null, cancellationToken);
        try
        {
            await _repository.RemoveItemAsync(itemId, CancellationToken.None);
        }
        catch
        {
            // Best effort: try to put the file back into box storage if the DB write failed.
            if (!string.IsNullOrWhiteSpace(item.StoredPath) && !string.IsNullOrWhiteSpace(restore.RestoredPath))
            {
                var isDirectory = item.ItemKind == ItemKind.Directory;
                await TryCompensateMoveAsync(restore.RestoredPath, item.StoredPath, isDirectory);
            }

            throw;
        }

        await RecordFileOperationAsync(
            FileOperationKind.Delete,
            $"删除并还原 {item.DisplayName}",
            new FileOperationUndoData(
                BeforeItem: item,
                AfterItem: null,
                BeforeStoredPath: item.StoredPath,
                AfterPath: restore.RestoredPath),
            cancellationToken);
        return restore;
    }

    public async Task<BoxDeleteResult> DeleteBoxAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");
        _lastPruneTicks.TryRemove(boxId, out _);
        _lastSmartSyncTicks.TryRemove(boxId, out _);

        if (box.Type is BoxType.Mapping or BoxType.Todo or BoxType.Smart)
        {
            await _repository.RemoveBoxAsync(boxId, cancellationToken);
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: true,
                RestoredCount: 0,
                FailedCount: 0,
                Failures: Array.Empty<string>());
        }

        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var restoredCount = 0;
        var failures = new List<string>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.StoredPath))
            {
                await _repository.RemoveItemAsync(item.Id, cancellationToken);
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RestoreStoredItemAsync(item, reservedTargets, cancellationToken);
                await _repository.RemoveItemAsync(item.Id, CancellationToken.None);
                restoredCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add($"{item.DisplayName}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return new BoxDeleteResult(
                box.Id,
                box.Name,
                box.Type,
                BoxRemoved: false,
                RestoredCount: restoredCount,
                FailedCount: failures.Count,
                Failures: failures);
        }

        await _repository.RemoveBoxAsync(boxId, cancellationToken);
        TryDeleteBoxStorageDirectory(box);

        return new BoxDeleteResult(
            box.Id,
            box.Name,
            box.Type,
            BoxRemoved: true,
            RestoredCount: restoredCount,
            FailedCount: 0,
            Failures: Array.Empty<string>());
    }

    public Task<IReadOnlyList<FileOperationRecord>> GetRecentFileOperationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetFileOperationsAsync(limit, cancellationToken);
    }

    public async Task<FileOperationRecord?> UndoLastFileOperationAsync(
        CancellationToken cancellationToken = default)
    {
        var operations = await _repository.GetFileOperationsAsync(
            limit: 50,
            cancellationToken);
        var operation = operations.FirstOrDefault(item => item.UndoneAt is null);
        if (operation is null)
        {
            return null;
        }

        var undoData = JsonSerializer.Deserialize<FileOperationUndoData>(
            operation.PayloadJson)
            ?? throw new InvalidOperationException("Operation history data is invalid.");

        await ExecuteWithoutHistoryAsync(async () =>
        {
            await UndoFileOperationCoreAsync(undoData, cancellationToken);
            return true;
        });

        var undoneAt = DateTimeOffset.UtcNow;
        await _repository.MarkFileOperationUndoneAsync(
            operation.Id,
            undoneAt,
            cancellationToken);
        return operation with { UndoneAt = undoneAt };
    }

    private async Task UndoFileOperationCoreAsync(
        FileOperationUndoData undoData,
        CancellationToken cancellationToken)
    {
        switch (undoData.BeforeItem is null, undoData.AfterItem is null)
        {
            case (true, false):
                var imported = await _repository.GetItemAsync(
                    undoData.AfterItem!.Id,
                    cancellationToken);
                if (imported is not null)
                {
                    await DeleteItemAsync(imported.Id, cancellationToken);
                }
                return;

            case (false, false):
                var beforeItem = undoData.BeforeItem!;
                var moved = await _repository.GetItemAsync(
                    undoData.AfterItem!.Id,
                    cancellationToken);
                if (moved is not null)
                {
                    await MoveItemToBoxAsync(
                        moved.Id,
                        beforeItem.BoxId,
                        beforeItem.GridColumn,
                        beforeItem.GridRow,
                        cancellationToken);
                }
                return;

            case (false, true):
                await ReinsertStoredItemAsync(
                    undoData.BeforeItem!,
                    undoData.AfterPath,
                    cancellationToken);
                return;

            default:
                throw new InvalidOperationException("Operation history data is incomplete.");
        }
    }

    private async Task ReinsertStoredItemAsync(
        DrawerItem beforeItem,
        string? currentExternalPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(beforeItem.StoredPath))
        {
            await _repository.AddItemAsync(beforeItem, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(currentExternalPath)
            || (!File.Exists(currentExternalPath) && !Directory.Exists(currentExternalPath)))
        {
            throw new FileNotFoundException(
                "The item can no longer be found at the location recorded in history.",
                currentExternalPath);
        }

        var desiredStoredPath = Path.GetFullPath(beforeItem.StoredPath);
        var storageRoot = Path.GetDirectoryName(desiredStoredPath)
            ?? throw new InvalidOperationException("Stored path is invalid.");
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, desiredStoredPath);
        Directory.CreateDirectory(storageRoot);

        var isDirectory = beforeItem.ItemKind == ItemKind.Directory;
        var targetPath = FileNameService.GetUniqueDestinationPath(
            storageRoot,
            Path.GetFileName(desiredStoredPath),
            isDirectory);
        PathSafety.EnsureChildPath(storageRoot, targetPath);

        await SafeFileOps.MoveAsync(
            currentExternalPath,
            targetPath,
            isDirectory,
            cancellationToken);

        var restoredItem = beforeItem with
        {
            DisplayName = Path.GetFileName(targetPath),
            StoredPath = targetPath,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await _repository.AddItemAsync(restoredItem, CancellationToken.None);
        }
        catch
        {
            await TryCompensateMoveAsync(
                targetPath,
                currentExternalPath,
                isDirectory);
            throw;
        }
    }

    private async Task RecordFileOperationAsync(
        FileOperationKind kind,
        string description,
        FileOperationUndoData undoData,
        CancellationToken cancellationToken)
    {
        if (_suppressHistory.Value)
        {
            return;
        }

        try
        {
            await _repository.AddFileOperationAsync(
                new FileOperationRecord(
                    Guid.NewGuid(),
                    kind,
                    description,
                    JsonSerializer.Serialize(undoData),
                    DateTimeOffset.UtcNow),
                cancellationToken: CancellationToken.None);
        }
        catch
        {
            // History is a recovery aid. A journal write failure must not turn a
            // successfully completed file operation into a reported failure.
        }
    }

    private async Task<T> ExecuteWithoutHistoryAsync<T>(Func<Task<T>> operation)
    {
        var previous = _suppressHistory.Value;
        _suppressHistory.Value = true;
        try
        {
            return await operation();
        }
        finally
        {
            _suppressHistory.Value = previous;
        }
    }

    private async Task<SmartBoxRule?> GetSmartBoxRuleCoreAsync(
        Guid boxId,
        CancellationToken cancellationToken)
    {
        var value = await _repository.GetSettingAsync(
            SmartBoxRuleSettingPrefix + boxId.ToString("N"),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SmartBoxRule>(value) is { } rule
                ? NormalizeSmartBoxRule(rule)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SmartBoxRule NormalizeSmartBoxRule(SmartBoxRule rule)
    {
        return rule with
        {
            RootDirectory = string.IsNullOrWhiteSpace(rule.RootDirectory)
                ? string.Empty
                : Path.GetFullPath(rule.RootDirectory.Trim()),
            NameContains = rule.NameContains?.Trim() ?? string.Empty,
            Extensions = string.Join(
                ',',
                ParseExtensions(rule.Extensions)),
            ModifiedWithinDays = Math.Clamp(rule.ModifiedWithinDays, 0, 3650),
            MaxItems = Math.Clamp(rule.MaxItems, 1, 2000)
        };
    }

    private static SmartBoxMatchResult FindSmartBoxMatches(
        SmartBoxRule rule,
        CancellationToken cancellationToken)
    {
        const int maximumScannedFiles = 20_000;
        var extensions = ParseExtensions(rule.Extensions);
        var modifiedAfter = rule.ModifiedWithinDays > 0
            ? DateTime.UtcNow.AddDays(-rule.ModifiedWithinDays)
            : (DateTime?)null;
        var matches = new List<(string Path, DateTime LastWriteTime)>();
        var scanned = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var path in Directory.EnumerateFiles(
                     rule.RootDirectory,
                     "*",
                     options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            if (scanned > maximumScannedFiles)
            {
                break;
            }

            var fileName = Path.GetFileName(path);
            if (rule.NameContains.Length > 0
                && !fileName.Contains(
                    rule.NameContains,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            if (extensions.Count > 0
                && !extensions.Contains(extension))
            {
                continue;
            }

            DateTime lastWriteTime;
            try
            {
                lastWriteTime = File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                continue;
            }

            if (modifiedAfter is not null && lastWriteTime < modifiedAfter)
            {
                continue;
            }

            matches.Add((path, lastWriteTime));
        }

        var paths = matches
            .OrderByDescending(match => match.LastWriteTime)
            .Take(rule.MaxItems)
            .Select(match => match.Path)
            .ToArray();
        return new SmartBoxMatchResult(scanned, paths);
    }

    private static HashSet<string> ParseExtensions(string? value)
    {
        return (value ?? string.Empty)
            .Split(
                [',', ';', '，', '；', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.')
                ? extension
                : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record SmartBoxMatchResult(
        int ScannedCount,
        IReadOnlyList<string> Paths);

    private async Task<ItemDeleteResult> RestoreStoredItemAsync(
        DrawerItem item,
        HashSet<string>? reservedTargets,
        CancellationToken cancellationToken)
    {
        var plan = CreateRestorePlan(item, reservedTargets);
        await SafeFileOps.MoveAsync(plan.SourcePath, plan.TargetPath, plan.IsDirectory, cancellationToken);

        return new ItemDeleteResult(
            item.Id,
            item.DisplayName,
            WasStoredItem: true,
            RestoredPath: plan.TargetPath,
            RestoredToOriginal: plan.RestoredToOriginal,
            RestoredToDesktop: plan.RestoredToDesktop);
    }

    private RestorePlan CreateRestorePlan(DrawerItem item, HashSet<string>? reservedTargets)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            throw new InvalidOperationException("Mapping items do not have stored files to restore.");
        }

        var storedPath = PathSafety.GetFullExistingPath(item.StoredPath);
        PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);

        var isDirectory = Directory.Exists(storedPath);
        var originalName = ResolveRestoreFileName(item, storedPath);

        if (TryGetExistingOriginalDirectory(item.SourcePath, out var originalDirectory))
        {
            var targetPath = GetReservedUniqueDestinationPath(originalDirectory, originalName, isDirectory, reservedTargets);
            PathSafety.EnsureChildPath(originalDirectory, targetPath);
            return new RestorePlan(storedPath, targetPath, isDirectory, RestoredToOriginal: true, RestoredToDesktop: false);
        }

        var desktopDirectory = GetDesktopDirectory();
        Directory.CreateDirectory(desktopDirectory);
        var desktopTarget = GetReservedUniqueDestinationPath(desktopDirectory, originalName, isDirectory, reservedTargets);
        PathSafety.EnsureChildPath(desktopDirectory, desktopTarget);
        return new RestorePlan(storedPath, desktopTarget, isDirectory, RestoredToOriginal: false, RestoredToDesktop: true);
    }

    private static string ResolveRestoreFileName(DrawerItem item, string storedPath)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            try
            {
                var originalPath = Path.GetFullPath(item.SourcePath);
                var fromSource = Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fromSource))
                {
                    return fromSource;
                }
            }
            catch
            {
                // Fall through to display name / stored path.
            }
        }

        if (!string.IsNullOrWhiteSpace(item.DisplayName))
        {
            return item.DisplayName;
        }

        var fromStored = Path.GetFileName(storedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fromStored))
        {
            throw new InvalidOperationException("Item does not contain a file name to restore.");
        }

        return fromStored;
    }

    private static bool TryGetExistingOriginalDirectory(string? sourcePath, out string directory)
    {
        directory = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        try
        {
            var originalPath = Path.GetFullPath(sourcePath);
            var originalDirectory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(originalDirectory) || !Directory.Exists(originalDirectory))
            {
                return false;
            }

            directory = originalDirectory;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDesktopDirectory()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            throw new InvalidOperationException("Desktop directory is not available for restore fallback.");
        }

        return Path.GetFullPath(desktopPath);
    }

    private static string GetReservedUniqueDestinationPath(
        string directory,
        string fileName,
        bool isDirectory,
        HashSet<string>? reservedTargets)
    {
        var targetPath = FileNameService.GetUniqueDestinationPath(directory, fileName, isDirectory);
        if (reservedTargets is null)
        {
            return targetPath;
        }

        var normalizedTargetPath = Path.GetFullPath(targetPath);
        if (reservedTargets.Add(normalizedTargetPath))
        {
            return targetPath;
        }

        var nameWithoutExtension = isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExtension} ({index}){extension}");
            var normalizedCandidate = Path.GetFullPath(candidate);
            if ((File.Exists(candidate) || Directory.Exists(candidate))
                || !reservedTargets.Add(normalizedCandidate))
            {
                continue;
            }

            return candidate;
        }

        throw new IOException($"Could not find a unique destination for {fileName}.");
    }

    private void TryDeleteBoxStorageDirectory(Box box)
    {
        try
        {
            var storagePath = box.StoragePath;
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                storagePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            }

            var fullStoragePath = Path.GetFullPath(storagePath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, fullStoragePath);

            if (Directory.Exists(fullStoragePath)
                && Directory.GetFileSystemEntries(fullStoragePath).Length == 0)
            {
                Directory.Delete(fullStoragePath, recursive: false);
            }
        }
        catch
        {
            // Storage cleanup is best-effort.
        }
    }

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return _repository.GetSettingAsync(key, cancellationToken);
    }

    public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        return _repository.SetSettingAsync(key, value, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetSettingsAsync(keys, cancellationToken);
    }

    public Task SetSettingsAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        return _repository.SetSettingsAsync(values, cancellationToken);
    }

    public Task<bool> DeleteSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        return _repository.DeleteSettingAsync(key, cancellationToken);
    }

    public async Task RenameBoxAsync(Guid boxId, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("Box name cannot be empty.", nameof(newName));
        }

        var box = await _repository.GetBoxAsync(boxId, cancellationToken)
            ?? throw new InvalidOperationException("Box does not exist.");

        await _repository.UpdateBoxNameAsync(boxId, newName.Trim(), cancellationToken);
    }

    public async Task OpenItemAsync(Guid itemId, IFileLauncher launcher, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetItemAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException("Item does not exist.");

        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.OpenAsync(path, cancellationToken);
    }

    private async Task PruneMissingStoredItemsAsync(Guid? boxId, CancellationToken cancellationToken)
    {
        var items = await _repository.GetItemsAsync(boxId, cancellationToken);
        await PruneMissingStoredItemsAsync(items, boxId, cancellationToken);
    }

    private async Task<IReadOnlyList<DrawerItem>> PruneMissingStoredItemsAsync(
        IReadOnlyList<DrawerItem> items,
        Guid? boxId,
        CancellationToken cancellationToken)
    {
        // 存储根不可达（可移动盘/网络盘暂时掉线）时绝不能清理：
        // 文件仍然存在只是暂时不可见，把"看不到"当成"已删除"会永久销毁记录与恢复信息，
        // 驱动器重新挂载后文件就变成无人知晓的孤儿。
        if (!Directory.Exists(_paths.BoxesDirectory))
        {
            return items;
        }

        var now = Environment.TickCount64;
        if (boxId is Guid id)
        {
            if (!ShouldRun(_lastPruneTicks, id, now, BoxPruneInterval))
            {
                return items;
            }
        }
        else if (!ShouldRun(ref _lastGlobalPruneTick, now, GlobalPruneInterval))
        {
            return items;
        }

        var missingItemIds = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return items
                .Where(IsMissingStoredItem)
                .Select(item => item.Id)
                .ToArray();
        }, cancellationToken);

        foreach (var itemId in missingItemIds)
        {
            await _repository.RemoveItemAsync(itemId, cancellationToken);
        }

        return missingItemIds.Length == 0
            ? items
            : items.Where(item => !missingItemIds.Contains(item.Id)).ToArray();
    }

    private static bool ShouldRun(
        ConcurrentDictionary<Guid, long> lastRuns,
        Guid key,
        long now,
        TimeSpan interval)
    {
        var previous = lastRuns.GetOrAdd(key, 0);
        if (previous != 0 && now - previous < interval.TotalMilliseconds)
        {
            return false;
        }

        lastRuns[key] = now;
        return true;
    }

    private static bool ShouldRun(ref long lastRun, long now, TimeSpan interval)
    {
        var previous = Volatile.Read(ref lastRun);
        if (previous != 0 && now - previous < interval.TotalMilliseconds)
        {
            return false;
        }

        Volatile.Write(ref lastRun, now);
        return true;
    }

    private bool IsMissingStoredItem(DrawerItem item)
    {
        if (string.IsNullOrWhiteSpace(item.StoredPath))
        {
            return false;
        }

        try
        {
            var storedPath = Path.GetFullPath(item.StoredPath);
            PathSafety.EnsureChildPath(_paths.BoxesDirectory, storedPath);

            // A missing or inaccessible parent can mean an offline volume, a temporarily
            // unavailable box directory, or a stale pre-migration path. Preserve the database
            // record unless the containing directory is definitely reachable.
            var parentDirectory = Path.GetDirectoryName(storedPath);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                return false;
            }

            return !File.Exists(storedPath) && !Directory.Exists(storedPath);
        }
        catch
        {
            return false;
        }
    }

    private async Task RepairStoredPathsAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        foreach (var box in boxes.Where(box => box.Type is BoxType.Normal or BoxType.Pixel or BoxType.Drawer or BoxType.Inbox))
        {
            var expectedStoragePath = Path.Combine(_paths.BoxesDirectory, box.Id.ToString("N"));
            if (!Directory.Exists(expectedStoragePath))
            {
                continue;
            }

            if (!string.Equals(
                    Path.GetFullPath(box.StoragePath ?? expectedStoragePath),
                    Path.GetFullPath(expectedStoragePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                await _repository.UpdateBoxStoragePathAsync(
                    box.Id,
                    expectedStoragePath,
                    cancellationToken);
            }

            var items = await _repository.GetItemsAsync(box.Id, cancellationToken);
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.StoredPath)))
            {
                var name = Path.GetFileName(item.StoredPath);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var expectedStoredPath = Path.Combine(expectedStoragePath, name);
                if ((!File.Exists(expectedStoredPath) && !Directory.Exists(expectedStoredPath))
                    || string.Equals(
                        Path.GetFullPath(item.StoredPath!),
                        Path.GetFullPath(expectedStoredPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await _repository.UpdateItemStoredPathAsync(
                    item.Id,
                    expectedStoredPath,
                    cancellationToken);
            }
        }
    }

    private async Task EnsureDefaultBoxesAsync(CancellationToken cancellationToken)
    {
        var boxes = await _repository.GetBoxesAsync(cancellationToken);
        if (boxes.Count == 0)
        {
            await CreateBoxAsync("普通收纳盒", BoxType.Normal, cancellationToken);
            await CreateBoxAsync("映射收纳盒", BoxType.Mapping, cancellationToken);
            boxes = await _repository.GetBoxesAsync(cancellationToken);
        }

        const string inboxCreatedKey = "DefaultInboxCreated";
        var inboxCreated = string.Equals(
            await _repository.GetSettingAsync(inboxCreatedKey, cancellationToken),
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);
        if (!inboxCreated && boxes.All(box => box.Type != BoxType.Inbox))
        {
            await CreateBoxAsync("临时收件箱", BoxType.Inbox, cancellationToken);
            await _repository.SetSettingAsync(
                inboxCreatedKey,
                bool.TrueString,
                cancellationToken);
        }
    }

    private static async Task TryCompensateMoveAsync(
        string movedPath,
        string originalPath,
        bool isDirectory)
    {
        try
        {
            if ((isDirectory && Directory.Exists(movedPath)) || (!isDirectory && File.Exists(movedPath)))
            {
                await SafeFileOps.MoveAsync(movedPath, originalPath, isDirectory, CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort compensation only; the original failure is rethrown by the caller.
        }
    }

    internal static string BuildSharingViolationMessage(
        string displayName,
        bool isDirectory)
    {
        var kind = isDirectory ? "文件夹" : "文件";
        return $"{kind}“{displayName}”正被其他程序占用，无法移动。"
            + "请关闭正在使用它的程序后重试，"
            + (isDirectory
                ? "或为该目录使用映射收纳盒，只保存路径引用。"
                : "或将所在目录放进映射收纳盒。");
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private sealed record RestorePlan(
        string SourcePath,
        string TargetPath,
        bool IsDirectory,
        bool RestoredToOriginal,
        bool RestoredToDesktop);
}
