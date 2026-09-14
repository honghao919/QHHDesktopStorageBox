using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using QHHDesktopStorageBox.App.Infrastructure;
using QHHDesktopStorageBox.App.Messages;
using QHHDesktopStorageBox.Core;
using QHHDesktopStorageBox.Core.Abstractions;
using QHHDesktopStorageBox.Core.Logging;
using QHHDesktopStorageBox.Core.Models;
using QHHDesktopStorageBox.Core.Services;
using QHHDesktopStorageBox.Native.Windows;

namespace QHHDesktopStorageBox.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const double ItemIconSizeDip = 19;
    private const string ThemeSettingKey = "Theme";
    internal const string ThemeBoxOpacitySettingKeyPrefix = "ThemeBoxOpacity.";
    internal const string BoxBorderOpacitySettingKeyPrefix = "DesktopBoxBorderOpacity.";
    internal const string IconFrameOpacitySettingKeyPrefix = "DesktopIconFrameOpacity.";
    internal const string ThemeBoxOpacityMigrationVersionSettingKey = "ThemeBoxOpacityVersion";
    private const string ThemeBoxOpacityMigrationVersion = "2";
    internal const string EditorFollowsBoxOpacitySettingKey = "EditorFollowsBoxOpacity";
    internal const string DesktopDoubleClickSettingKey = "DesktopDoubleClickToggle";
    internal const string IconToolTipCompactSettingKey = "IconToolTipCompact";
    internal const string AboutPageShownSettingKey = "AboutPageShown";
    internal const string AutomaticUpdateCheckSettingKey = "AutomaticUpdateCheckUtc";
    internal static readonly TimeSpan AutomaticUpdateCheckInterval = TimeSpan.FromHours(12);
    private const string StartupRegistryKeyName = "QHHDesktopStorageBox";
    private const string LegacyStartupRegistryKeyName = "WitchDrawer";

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly QuickPanelViewModel _quickPanelViewModel;
    private readonly UpdateService _updateService;
    private readonly BoxVisualStyleStore _boxVisualStyleStore;
    private readonly BoxPositionLockStateStore _boxPositionLockStateStore;
    private readonly AppPaths _appPaths;
    private readonly DataStorageMigrationService _dataStorageMigrationService;
    private readonly DataSafetyService? _dataSafetyService;
    private BoxViewModel? _selectedBox;
    private CancellationTokenSource? _itemsLoadCts;
    private int _itemsLoadVersion;
    private bool _isBusy;
    private bool _pendingDesktopReload;
    private Guid? _pendingDesktopReloadBoxId;
    private bool _isSettingsPage;
    private bool _isAboutPage;
    private bool _isArchivePage;
    private string _statusText = "准备就绪";
    private string _themeLabel = "清透雅致";
    private AppTheme _currentTheme;
    private double _themeTransparencyPercent = (1 - AppThemeManager.DefaultBoxOpacity) * 100;
    private double _boxBorderTransparencyPercent;
    private double _iconFrameTransparencyPercent;
    private readonly object _themeOpacitySaveLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _themeOpacitySaveDelays = [];
    private readonly SemaphoreSlim _themeOpacityWriteGate = new(1, 1);
    private bool _isSynchronizingThemeTransparency;
    private bool _editorFollowsBoxOpacity;
    private bool _iconToolTipCompact;
    private readonly AutoHideSettingsStore _autoHideSettingsStore;
    private bool _autoHideEnabled;
    private int _autoHideHiddenTransparencyPercent = AutoHideSettings.DefaultHiddenTransparencyPercent;
    private AutoHideRevealScope _autoHideRevealScope = AutoHideRevealScope.HoveredBoxOnly;
    private bool _autoHideFadeWholeBox = true;
    private bool _autoHideFadeTitle = true;
    private bool _autoHideFadeBorder = true;
    private CancellationTokenSource? _autoHideSaveCts;
    private bool _launchOnStartup;
    private bool _areDesktopIconsHidden;
    private bool _isDesktopDoubleClickEnabled;
    private string _updateStatusText = string.Empty;
    private string _dataSafetyStatusText = string.Empty;
    private bool _isCheckingUpdate;
    private bool _isDataSafetyBusy;
    private string? _pendingUpdateSha256;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;

    public MainViewModel(
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IAppLogger logger,
        QuickPanelViewModel quickPanelViewModel,
        UpdateService updateService,
        BoxVisualStyleStore boxVisualStyleStore,
        BoxPositionLockStateStore boxPositionLockStateStore,
        AppPaths appPaths,
        DataStorageMigrationService dataStorageMigrationService,
        AutoHideSettingsStore autoHideSettingsStore,
        DataSafetyService? dataSafetyService = null)
    {
        _drawerService = drawerService;
        _todoService = todoService;
        _launcher = launcher;
        _logger = logger;
        _quickPanelViewModel = quickPanelViewModel;
        _updateService = updateService;
        _boxVisualStyleStore = boxVisualStyleStore;
        _boxPositionLockStateStore = boxPositionLockStateStore;
        _appPaths = appPaths;
        _dataStorageMigrationService = dataStorageMigrationService;
        _dataSafetyService = dataSafetyService;
        _autoHideSettingsStore = autoHideSettingsStore;
        TodoBoxDetail = new TodoBoxDetailViewModel(todoService, logger);
        TodoBoxDetail.ItemsChanged += OnTodoBoxDetailItemsChanged;
        BoxSizeSettings = new BoxSizeSettingsViewModel(drawerService, logger);

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        CreateNormalBoxCommand = new AsyncRelayCommand(
            () => CreateBoxAsync(BoxType.Normal, BoxVisualStyle.Modern));
        CreateMappingBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Mapping));
        CreatePixelBoxCommand = new AsyncRelayCommand(
            () => CreateBoxAsync(BoxType.Normal, BoxVisualStyle.Pixel));
        CreateStyledNormalBoxCommand =
            new AsyncRelayCommand<BoxVisualStyleOption?>(CreateStyledNormalBoxAsync);
        SetSelectedBoxVisualStyleCommand =
            new AsyncRelayCommand<BoxVisualStyleOption?>(
                SetSelectedBoxVisualStyleAsync,
                option => option is not null && SelectedBox?.CanSelectVisualStyle == true);
        ToggleSelectedBoxPositionLockCommand =
            new AsyncRelayCommand(
                ToggleSelectedBoxPositionLockAsync,
                () => SelectedBox is not null);
        CreateTodoBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Todo));
        CreateDrawerBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Drawer));
        CreateInboxBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Inbox));
        CreateSmartBoxCommand = new AsyncRelayCommand(() => CreateBoxAsync(BoxType.Smart));
        DeleteSelectedBoxCommand = new AsyncRelayCommand(DeleteSelectedBoxAsync, () => SelectedBox is not null);
        RenameSelectedBoxCommand = new AsyncRelayCommand<string?>(RenameSelectedBoxAsync, _ => SelectedBox is not null);
        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RestoreArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(RestoreArchivedTodoAsync);
        DeleteArchivedTodoCommand = new AsyncRelayCommand<ArchivedTodoItemViewModel?>(DeleteArchivedTodoAsync);
        UndoArchivedDeleteCommand = new AsyncRelayCommand(UndoArchivedDeleteAsync, () => !IsBusy && ArchiveUndo.IsAvailable);
        ArchiveUndo.AvailabilityChanged += (_, _) => UndoArchivedDeleteCommand.NotifyCanExecuteChanged();
        SetCurrentTheme(AppThemeManager.CurrentTheme);

        ApplyMoeThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Moe));
        ApplyGlassThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Glass));
        ApplyCrystalThemeCommand = new AsyncRelayCommand(() => ApplyThemeAsync(AppTheme.Crystal));
        ResetThemeTransparencyCommand = new RelayCommand(ResetThemeTransparency);
        ToggleLaunchOnStartupCommand = new AsyncRelayCommand(ToggleLaunchOnStartupAsync);
        ToggleDesktopIconsCommand = new AsyncRelayCommand(ToggleDesktopIconsAsync);
        ToggleDesktopDoubleClickCommand = new AsyncRelayCommand(ToggleDesktopDoubleClickAsync);
        ToggleEditorOpacityFollowCommand = new AsyncRelayCommand(ToggleEditorOpacityFollowAsync);
        ToggleIconToolTipCompactCommand = new AsyncRelayCommand(ToggleIconToolTipCompactAsync);
        ToggleAutoHideEnabledCommand = new AsyncRelayCommand(ToggleAutoHideEnabledAsync);
        ApplyAutoHideScopeHoveredOnlyCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.HoveredBoxOnly));
        ApplyAutoHideScopeAllCommand =
            new AsyncRelayCommand(() => ApplyAutoHideRevealScopeAsync(AutoHideRevealScope.AllBoxes));
        CheckForUpdateCommand = new AsyncRelayCommand(
            () => CheckForUpdateAsync(showFailureStatus: true));
        UndoLastFileOperationCommand = new AsyncRelayCommand(
            UndoLastFileOperationAsync,
            () => RecentFileOperations.Any(operation => operation.UndoneAt is null) && !IsBusy);
        ShowDashboardCommand = new RelayCommand(() =>
        {
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = false;
        });
        ShowArchiveCommand = new AsyncRelayCommand(ShowArchiveAsync);
        ShowSettingsCommand = new RelayCommand(() =>
        {
            SelectedBox = null;
            AreDesktopIconsHidden = DesktopIconVisibility.IsHidden();
            IsArchivePage = false;
            IsSettingsPage = true;
            IsAboutPage = false;
            FireAndForget.Run(
                LoadFileOperationHistoryAsync(),
                _logger,
                "Failed to refresh file operation history.");
        });
        ShowAboutCommand = new RelayCommand(() =>
        {
            SelectedBox = null;
            IsArchivePage = false;
            IsSettingsPage = false;
            IsAboutPage = true;
        });
    }

    public event EventHandler? BoxesChanged;

    public event EventHandler<BoxItemsChangedEventArgs>? ItemsChanged;

    public event EventHandler<ImportPreflightRequestedEventArgs>? ImportPreflightRequested;

    public ObservableCollection<BoxViewModel> Boxes { get; } = [];

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<ArchivedTodoItemViewModel> ArchivedTodos { get; } = [];

    public ObservableCollection<FileOperationRecord> RecentFileOperations { get; } = [];

    public TodoBoxDetailViewModel TodoBoxDetail { get; }

    public BoxSizeSettingsViewModel BoxSizeSettings { get; }

    public IReadOnlyList<BoxVisualStyleOption> BoxVisualStyleOptions =>
        BoxVisualStyleCatalog.Options;

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);

        foreach (var item in Items)
        {
            item.RequestIconSize(GetIconPixelSize(item.IsPixelated));
        }
    }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand CreateNormalBoxCommand { get; }

    public IAsyncRelayCommand CreateMappingBoxCommand { get; }

    public IAsyncRelayCommand CreatePixelBoxCommand { get; }

    public IAsyncRelayCommand<BoxVisualStyleOption?> CreateStyledNormalBoxCommand { get; }

    public IAsyncRelayCommand<BoxVisualStyleOption?> SetSelectedBoxVisualStyleCommand { get; }

    public IAsyncRelayCommand ToggleSelectedBoxPositionLockCommand { get; }

    public IAsyncRelayCommand CreateTodoBoxCommand { get; }

    public IAsyncRelayCommand CreateDrawerBoxCommand { get; }

    public IAsyncRelayCommand CreateInboxBoxCommand { get; }

    public IAsyncRelayCommand CreateSmartBoxCommand { get; }

    public IAsyncRelayCommand DeleteSelectedBoxCommand { get; }

    public IAsyncRelayCommand<string?> RenameSelectedBoxCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand<ArchivedTodoItemViewModel?> RestoreArchivedTodoCommand { get; }

    public IAsyncRelayCommand<ArchivedTodoItemViewModel?> DeleteArchivedTodoCommand { get; }

    public IAsyncRelayCommand UndoArchivedDeleteCommand { get; }

    public TodoUndoViewModel ArchiveUndo { get; } = new();

    public IAsyncRelayCommand ApplyMoeThemeCommand { get; }

    public IAsyncRelayCommand ApplyGlassThemeCommand { get; }

    public IAsyncRelayCommand ApplyCrystalThemeCommand { get; }

    public IRelayCommand ResetThemeTransparencyCommand { get; }

    public IAsyncRelayCommand ToggleLaunchOnStartupCommand { get; }

    public IAsyncRelayCommand ToggleDesktopIconsCommand { get; }

    public IAsyncRelayCommand ToggleDesktopDoubleClickCommand { get; }

    public IAsyncRelayCommand ToggleEditorOpacityFollowCommand { get; }

    public IAsyncRelayCommand ToggleIconToolTipCompactCommand { get; }

    public IAsyncRelayCommand ToggleAutoHideEnabledCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeHoveredOnlyCommand { get; }

    public IAsyncRelayCommand ApplyAutoHideScopeAllCommand { get; }

    public IAsyncRelayCommand CheckForUpdateCommand { get; }

    public IAsyncRelayCommand UndoLastFileOperationCommand { get; }

    public IRelayCommand ShowDashboardCommand { get; }

    public IAsyncRelayCommand ShowArchiveCommand { get; }

    public IRelayCommand ShowSettingsCommand { get; }

    public IRelayCommand ShowAboutCommand { get; }

    public BoxViewModel? SelectedBox
    {
        get => _selectedBox;
        set
        {
            if (UpdateSelectedBoxCore(value))
            {
                QueueSelectedBoxItemsLoad();
            }
        }
    }

    public bool IsSelectedTodoBox => SelectedBox?.IsTodoBox == true;

    public bool CanImportFiles => SelectedBox is { IsTodoBox: false, IsSmartBox: false };

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UndoLastFileOperationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSettingsPage
    {
        get => _isSettingsPage;
        set => SetProperty(ref _isSettingsPage, value);
    }

    public bool IsAboutPage
    {
        get => _isAboutPage;
        set => SetProperty(ref _isAboutPage, value);
    }

    public bool IsArchivePage
    {
        get => _isArchivePage;
        set => SetProperty(ref _isArchivePage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    internal void ReportStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    public string ThemeLabel
    {
        get => _themeLabel;
        private set => SetProperty(ref _themeLabel, value);
    }

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                OnPropertyChanged(nameof(IsMoeTheme));
                OnPropertyChanged(nameof(IsGlassTheme));
                OnPropertyChanged(nameof(IsCrystalTheme));
            }
        }
    }

    public bool IsMoeTheme => CurrentTheme == AppTheme.Moe;

    public bool IsGlassTheme => CurrentTheme == AppTheme.Glass;

    public bool IsCrystalTheme => CurrentTheme == AppTheme.Crystal;

    public double ThemeTransparencyPercent
    {
        get => _themeTransparencyPercent;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var normalized = Math.Clamp(
                Math.Round(value),
                0,
                (1 - AppThemeManager.MinimumBoxOpacity) * 100);
            if (SetProperty(ref _themeTransparencyPercent, normalized))
            {
                OnPropertyChanged(nameof(ThemeTransparencyLabel));
                var opacity = 1 - (normalized / 100);
                if (!_isSynchronizingThemeTransparency)
                {
                    AppThemeManager.SetBoxOpacity(CurrentTheme, opacity);
                    SynchronizeThemeTransparency();
                    QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(CurrentTheme), FormatOpacity(opacity));
                }
            }
        }
    }

    public string ThemeTransparencyLabel => $"{ThemeTransparencyPercent:0}%";

    public double BoxBorderTransparencyPercent
    {
        get => _boxBorderTransparencyPercent;
        set => SetAppearanceTransparency(ref _boxBorderTransparencyPercent, value,
            nameof(BoxBorderTransparencyPercent), BoxBorderOpacitySettingKeyPrefix,
            AppThemeManager.SetBoxBorderOpacity);
    }

    public double IconFrameTransparencyPercent
    {
        get => _iconFrameTransparencyPercent;
        set => SetAppearanceTransparency(ref _iconFrameTransparencyPercent, value,
            nameof(IconFrameTransparencyPercent), IconFrameOpacitySettingKeyPrefix,
            AppThemeManager.SetIconFrameOpacity);
    }

    private void SetAppearanceTransparency(ref double field, double value, string propertyName,
        string settingPrefix, Action<AppTheme, double?> apply)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        var normalized = Math.Clamp(Math.Round(value), 0, 100);
        if (SetProperty(ref field, normalized, propertyName) && !_isSynchronizingThemeTransparency)
        {
            var opacity = 1 - normalized / 100;
            apply(CurrentTheme, opacity);
            QueueThemeOpacitySave(settingPrefix + CurrentTheme, FormatOpacity(opacity));
        }
    }

    private void ResetThemeTransparency()
    {
        var theme = CurrentTheme;
        var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
        AppThemeManager.SetBoxOpacity(theme, opacity);
        AppThemeManager.SetBoxBorderOpacity(theme, null);
        AppThemeManager.SetIconFrameOpacity(theme, null);
        SynchronizeThemeTransparency();
        QueueThemeOpacitySave(GetThemeBoxOpacitySettingKey(theme), FormatOpacity(opacity));
        QueueThemeOpacitySave(BoxBorderOpacitySettingKeyPrefix + theme, null);
        QueueThemeOpacitySave(IconFrameOpacitySettingKeyPrefix + theme, null);
    }

    public bool EditorFollowsBoxOpacity
    {
        get => _editorFollowsBoxOpacity;
        private set => SetProperty(ref _editorFollowsBoxOpacity, value);
    }

    /// <summary>
    /// 图标名称（悬停提示）显示模式。<see langword="false"/> = 完整显示（文件路径），
    /// <see langword="true"/> = 精简显示（文件名，快捷方式自动去掉 .lnk）。
    /// </summary>
    public bool IconToolTipCompact
    {
        get => _iconToolTipCompact;
        private set => SetProperty(ref _iconToolTipCompact, value);
    }

    public bool AutoHideEnabled
    {
        get => _autoHideEnabled;
        private set => SetProperty(ref _autoHideEnabled, value);
    }

    public int AutoHideHiddenTransparencyPercent
    {
        get => _autoHideHiddenTransparencyPercent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _autoHideHiddenTransparencyPercent, clamped))
            {
                return;
            }

            // 立即应用（拖动滑块时实时预览），仅持久化走防抖。
            PublishAutoHideSettings();
            QueueAutoHideSave();
        }
    }

    public AutoHideRevealScope AutoHideRevealScope
    {
        get => _autoHideRevealScope;
        private set
        {
            if (!SetProperty(ref _autoHideRevealScope, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsAutoHideScopeHoveredOnly));
            OnPropertyChanged(nameof(IsAutoHideScopeAll));
        }
    }

    public bool IsAutoHideScopeHoveredOnly => AutoHideRevealScope == AutoHideRevealScope.HoveredBoxOnly;

    public bool IsAutoHideScopeAll => AutoHideRevealScope == AutoHideRevealScope.AllBoxes;

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒外壳（背景/边框/阴影）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeWholeBox
    {
        get => _autoHideFadeWholeBox;
        set
        {
            if (SetProperty(ref _autoHideFadeWholeBox, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒标题一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeTitle
    {
        get => _autoHideFadeTitle;
        set
        {
            if (SetProperty(ref _autoHideFadeTitle, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    /// <summary>
    /// 是否在自动隐藏时连同收纳盒边框（描边）一起透明。与内容透明相互独立。
    /// </summary>
    public bool AutoHideFadeBorder
    {
        get => _autoHideFadeBorder;
        set
        {
            if (SetProperty(ref _autoHideFadeBorder, value))
            {
                PublishAutoHideSettings();
                QueueAutoHideSave();
            }
        }
    }

    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        private set => SetProperty(ref _launchOnStartup, value);
    }

    public bool AreDesktopIconsHidden
    {
        get => _areDesktopIconsHidden;
        private set => SetProperty(ref _areDesktopIconsHidden, value);
    }

    public bool IsDesktopDoubleClickEnabled
    {
        get => _isDesktopDoubleClickEnabled;
        private set => SetProperty(ref _isDesktopDoubleClickEnabled, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set => SetProperty(ref _isCheckingUpdate, value);
    }

    public string CurrentVersionText
    {
        get
        {
            var version = GetCurrentVersion();
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// 当前生效的数据根目录（数据库与收纳盒文件所在位置）。
    /// </summary>
    public string CurrentDataDirectory => _appPaths.RootDirectory;

    public string DataSafetyStatusText
    {
        get => _dataSafetyStatusText;
        private set => SetProperty(ref _dataSafetyStatusText, value);
    }

    public bool IsDataSafetyBusy
    {
        get => _isDataSafetyBusy;
        private set => SetProperty(ref _isDataSafetyBusy, value);
    }

    /// <summary>
    /// 将数据目录整体迁移到新文件夹。成功后需重启应用才会切换到新目录。
    /// </summary>
    public async Task MigrateDataDirectoryAsync(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        IsBusy = true;
        StatusText = "正在迁移数据目录…";
        try
        {
            var newPaths = await _dataStorageMigrationService.MigrateAsync(targetDirectory);
            StatusText = "数据已迁移，重启后生效";
            _logger.Info($"Data directory migrated to {newPaths.RootDirectory}. Restart required.");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Data directory migration failed.");
            StatusText = "数据目录迁移失败";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<DataBackupResult> CreateDataBackupAsync(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        IsDataSafetyBusy = true;
        DataSafetyStatusText = "正在创建完整备份…";
        try
        {
            var result = await RequireDataSafetyService().CreateBackupAsync(
                destinationPath,
                GetCurrentVersion().ToString(3));
            DataSafetyStatusText = $"备份完成：{FormatFileSize(result.SizeBytes)}";
            StatusText = DataSafetyStatusText;
            _logger.Info($"Data backup created at {result.ArchivePath}.");
            return result;
        }
        catch (Exception exception)
        {
            DataSafetyStatusText = "备份失败";
            _logger.Error(exception, "Failed to create data backup.");
            throw;
        }
        finally
        {
            IsDataSafetyBusy = false;
        }
    }

    public async Task<AppPaths> RestoreDataBackupAsync(
        string archivePath,
        string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        IsDataSafetyBusy = true;
        DataSafetyStatusText = "正在验证并恢复备份…";
        try
        {
            var restoredPaths = await RequireDataSafetyService().RestoreBackupAsync(
                archivePath,
                targetDirectory);
            DataSafetyStatusText = "备份已恢复，重启后生效";
            StatusText = DataSafetyStatusText;
            _logger.Info($"Data backup restored to {restoredPaths.RootDirectory}. Restart required.");
            return restoredPaths;
        }
        catch (Exception exception)
        {
            DataSafetyStatusText = "恢复失败";
            _logger.Error(exception, "Failed to restore data backup.");
            throw;
        }
        finally
        {
            IsDataSafetyBusy = false;
        }
    }

    public async Task<BrokenReferenceScanResult> ScanBrokenReferencesAsync()
    {
        IsDataSafetyBusy = true;
        DataSafetyStatusText = "正在检查映射与智能盒引用…";
        try
        {
            var result = await RequireDataSafetyService().ScanBrokenReferencesAsync();
            DataSafetyStatusText = result.MissingReferences.Count == 0
                ? $"引用检查完成：{result.ScannedReferenceCount} 项均可用"
                : $"发现 {result.MissingReferences.Count} 个失效引用";
            StatusText = DataSafetyStatusText;
            return result;
        }
        catch (Exception exception)
        {
            DataSafetyStatusText = "引用检查失败";
            _logger.Error(exception, "Failed to scan broken references.");
            throw;
        }
        finally
        {
            IsDataSafetyBusy = false;
        }
    }

    public async Task<DiagnosticReportResult> CreateDiagnosticReportAsync(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        IsDataSafetyBusy = true;
        DataSafetyStatusText = "正在生成诊断报告…";
        try
        {
            var result = await RequireDataSafetyService().CreateDiagnosticReportAsync(
                destinationPath,
                GetCurrentVersion().ToString(3));
            DataSafetyStatusText = result.BrokenReferenceCount == 0
                ? "诊断报告已生成，未发现失效引用"
                : $"诊断报告已生成，包含 {result.BrokenReferenceCount} 个失效引用";
            StatusText = DataSafetyStatusText;
            return result;
        }
        catch (Exception exception)
        {
            DataSafetyStatusText = "诊断报告生成失败";
            _logger.Error(exception, "Failed to create diagnostic report.");
            throw;
        }
        finally
        {
            IsDataSafetyBusy = false;
        }
    }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var existingSelection = SelectedBox?.Id;
            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);

            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(box => box.Id == existingSelection) ?? Boxes.FirstOrDefault());

            // 必须在首次启动标记写入前判断是否为旧安装，才能让新用户使用二段透明度，
            // 同时让升级用户保留旧主题原本的视觉效果。
            await RestoreThemeBoxOpacitiesAsync();
            await RestoreAppearanceOpacitiesAsync();
            var editorOpacityFollowSetting =
                await _drawerService.GetSettingAsync(EditorFollowsBoxOpacitySettingKey);
            EditorFollowsBoxOpacity = bool.TryParse(
                editorOpacityFollowSetting,
                out var editorFollowsBoxOpacity)
                && editorFollowsBoxOpacity;

            var iconToolTipCompactSetting =
                await _drawerService.GetSettingAsync(IconToolTipCompactSettingKey);
            IconToolTipCompact = bool.TryParse(
                iconToolTipCompactSetting,
                out var iconToolTipCompact)
                && iconToolTipCompact;
            PublishIconToolTipMode();

            var autoHideSettings = await _autoHideSettingsStore.LoadAsync();
            AutoHideEnabled = autoHideSettings.IsEnabled;
            AutoHideHiddenTransparencyPercent = autoHideSettings.HiddenTransparencyPercent;
            AutoHideRevealScope = autoHideSettings.RevealScope;
            AutoHideFadeWholeBox = autoHideSettings.FadeWholeBox;
            AutoHideFadeTitle = autoHideSettings.FadeTitle;
            AutoHideFadeBorder = autoHideSettings.FadeBorder;
            PublishAutoHideSettings();

            var aboutPageShown = await _drawerService.GetSettingAsync(AboutPageShownSettingKey);
            if (!bool.TryParse(aboutPageShown, out var hasShownAboutPage) || !hasShownAboutPage)
            {
                ShowAboutCommand.Execute(null);
                try
                {
                    await _drawerService.SetSettingAsync(AboutPageShownSettingKey, bool.TrueString);
                }
                catch (Exception exception)
                {
                    // The guide is still useful if the preference cannot be persisted;
                    // try again on the next launch instead of failing startup.
                    _logger.Error(exception, "Failed to persist first-launch about-page preference.");
                }
            }

            MigrateLegacyStartupRegistry();
            LaunchOnStartup = ReadStartupRegistry();
            AreDesktopIconsHidden = DesktopIconVisibility.IsHidden();
            var desktopDoubleClickSetting =
                await _drawerService.GetSettingAsync(DesktopDoubleClickSettingKey);
            IsDesktopDoubleClickEnabled =
                bool.TryParse(desktopDoubleClickSetting, out var desktopDoubleClickEnabled)
                && desktopDoubleClickEnabled;
            StatusText = $"{Boxes.Count} 个收纳盒已同步到桌面";
            await LoadFileOperationHistoryAsync();
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task LoadFileOperationHistoryAsync()
    {
        var operations = await _drawerService.GetRecentFileOperationsAsync(30);
        RecentFileOperations.Clear();
        foreach (var operation in operations)
        {
            RecentFileOperations.Add(operation);
        }

        OnPropertyChanged(nameof(HasRecentFileOperations));
        OnPropertyChanged(nameof(LastFileOperationText));
        UndoLastFileOperationCommand.NotifyCanExecuteChanged();
    }

    public bool HasRecentFileOperations => RecentFileOperations.Count > 0;

    public string LastFileOperationText =>
        RecentFileOperations.FirstOrDefault(operation => operation.UndoneAt is null)?.Description
        ?? "暂无可撤销操作";

    private async Task UndoLastFileOperationAsync()
    {
        if (IsBusy)
        {
            return;
        }

        FileOperationRecord? undone;
        try
        {
            IsBusy = true;
            undone = await _drawerService.UndoLastFileOperationAsync();
            StatusText = undone is null
                ? "暂无可撤销操作"
                : $"已撤销：{undone.Description}";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to undo the last file operation.");
            StatusText = "撤销失败：" + exception.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (undone is not null)
        {
            await LoadAsync();
            await _quickPanelViewModel.LoadAsync();
        }
    }

    /// <summary>
    /// Reloads items/quick-panel state without raising BoxesChanged (avoids desktop refresh loops).
    /// </summary>
    public async Task ReloadItemsFromDesktopAsync(Guid? affectedBoxId = null)
    {
        if (IsBusy)
        {
            // 忙时合流而非丢弃：记录一次待刷，忙完补刷；null 表示全量，
            // 一旦出现第二个不同盒子的变更就保持全量。
            _pendingDesktopReloadBoxId = _pendingDesktopReload
                ? (_pendingDesktopReloadBoxId == affectedBoxId ? affectedBoxId : null)
                : affectedBoxId;
            _pendingDesktopReload = true;
            return;
        }

        try
        {
            IsBusy = true;
            if (affectedBoxId is null || SelectedBox?.Id == affectedBoxId.Value)
            {
                await LoadItemsForSelectedBoxAsync(SelectedBox);
            }
            if (IsArchivePage)
            {
                await LoadArchivedTodosAsync();
            }
            if (affectedBoxId is Guid boxId)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
            }
            else
            {
                await _quickPanelViewModel.LoadAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to reload items from desktop boxes.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            FlushPendingDesktopReload();
        }
    }

    private void FlushPendingDesktopReload()
    {
        if (!_pendingDesktopReload || IsBusy)
        {
            return;
        }

        _pendingDesktopReload = false;
        var boxId = _pendingDesktopReloadBoxId;
        _pendingDesktopReloadBoxId = null;
        _ = ReloadItemsFromDesktopAsync(boxId);
    }

    public async Task ReorderBoxAsync(Guid draggedBoxId, Guid targetBoxId, bool insertAfter)
    {
        var draggedBox = Boxes.FirstOrDefault(box => box.Id == draggedBoxId);
        var targetBox = Boxes.FirstOrDefault(box => box.Id == targetBoxId);
        if (draggedBox is null || targetBox is null || ReferenceEquals(draggedBox, targetBox))
        {
            return;
        }

        var originalIndex = Boxes.IndexOf(draggedBox);
        var originalOrder = Boxes.Select(box => box.Id).ToArray();
        Boxes.RemoveAt(originalIndex);

        var targetIndex = Boxes.IndexOf(targetBox);
        var insertionIndex = insertAfter ? targetIndex + 1 : targetIndex;
        Boxes.Insert(insertionIndex, draggedBox);

        var reorderedIds = Boxes.Select(box => box.Id).ToArray();
        if (reorderedIds.SequenceEqual(originalOrder))
        {
            return;
        }

        try
        {
            await _drawerService.ReorderBoxesAsync(reorderedIds);
            SelectedBox = draggedBox;
            StatusText = $"已调整“{draggedBox.Name}”的排列位置";
        }
        catch (Exception exception)
        {
            var currentIndex = Boxes.IndexOf(draggedBox);
            if (currentIndex >= 0 && currentIndex != originalIndex)
            {
                Boxes.Move(currentIndex, originalIndex);
            }

            _logger.Error(exception, "Failed to reorder boxes.");
            StatusText = "收纳盒排序保存失败，已恢复原顺序";
        }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            StatusText = "请先选择一个收纳盒";
            return;
        }

        if (selectedBox.IsTodoBox)
        {
            StatusText = "待办收纳盒请使用任务输入框添加事项";
            return;
        }

        var pathList = paths.ToArray();
        if (pathList.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            // 固定格数盒的硬约束：主窗口导入同样不得超容量（桌面盒拖入路径已有同等约束）。
            // 超容量的项目若入库，会因无格位被分配到固定边界外，在盒内不可见也不可选。
            var pathsToImport = pathList;
            var skippedForCapacity = 0;
            if (selectedBox.SupportsFixedSize && BoxSizeSettings.IsFixedMode)
            {
                var capacity = BoxSizeSettings.FixedColumns * BoxSizeSettings.FixedRows;
                var remaining = Math.Max(0, capacity - Items.Count);
                if (remaining < pathList.Length)
                {
                    pathsToImport = pathList.Take(remaining).ToArray();
                    skippedForCapacity = pathList.Length - pathsToImport.Length;
                }
            }

            var preflight = await _drawerService.PreflightImportAsync(
                selectedBox.Id,
                pathsToImport);
            var conflictPolicy = ImportConflictPolicy.AutoRename;
            if (preflight.RequiresConfirmation)
            {
                var selectedPolicy = await RequestImportConflictPolicyAsync(preflight);
                if (selectedPolicy is null)
                {
                    StatusText = "已取消导入";
                    return;
                }

                conflictPolicy = selectedPolicy.Value;
            }

            var imported = 0;
            var skippedForConflict = 0;
            var failed = new List<string>();
            foreach (var path in pathsToImport)
            {
                try
                {
                    var item = await _drawerService.TryImportPathAsync(
                        selectedBox.Id,
                        path,
                        new ImportOptions(conflictPolicy));
                    if (item is null)
                    {
                        skippedForConflict++;
                    }
                    else
                    {
                        imported++;
                    }
                }
                catch (Exception exception)
                {
                    failed.Add(exception.Message);
                    _logger.Error(exception, $"Failed to import {path}.");
                }
            }

            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            var notes = new List<string>();
            if (skippedForCapacity > 0)
            {
                notes.Add($"{skippedForCapacity} 项超出容量");
            }

            if (skippedForConflict > 0)
            {
                notes.Add($"{skippedForConflict} 项因重名跳过");
            }

            if (failed.Count > 0)
            {
                notes.Add($"{failed.Count} 项失败：{failed[0]}");
            }

            StatusText = skippedForCapacity > 0
                && skippedForConflict == 0
                && failed.Count == 0
                ? imported > 0
                    ? $"已导入 {imported} 项到 {selectedBox.Name}，盒子已满（{skippedForCapacity} 项未导入）"
                    : $"{selectedBox.Name} 已满，无法导入"
                : notes.Count == 0
                    ? $"已导入 {imported} 项到 {selectedBox.Name}"
                    : $"已导入 {imported} 项到 {selectedBox.Name}；{string.Join("；", notes)}";
            await LoadFileOperationHistoryAsync();
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
        });
    }

    public Task<SmartBoxRule?> GetSelectedSmartBoxRuleAsync()
    {
        var selectedBox = SelectedBox;
        return selectedBox?.IsSmartBox == true
            ? _drawerService.GetSmartBoxRuleAsync(selectedBox.Id)
            : Task.FromResult<SmartBoxRule?>(null);
    }

    public async Task SaveSmartBoxRuleAsync(SmartBoxRule rule)
    {
        var selectedBox = SelectedBox;
        if (selectedBox?.IsSmartBox != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.SaveSmartBoxRuleAsync(selectedBox.Id, rule);
            var result = await _drawerService.SyncSmartBoxAsync(selectedBox.Id);
            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText =
                $"智能盒已同步：匹配 {result.MatchedCount} 项，新增 {result.AddedCount} 项，移除 {result.RemovedCount} 项";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
        });
    }

    public async Task SyncSelectedSmartBoxAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox?.IsSmartBox != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _drawerService.SyncSmartBoxAsync(selectedBox.Id);
            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText =
                $"智能盒已同步：匹配 {result.MatchedCount} 项，新增 {result.AddedCount} 项，移除 {result.RemovedCount} 项";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
        });
    }

    private async Task<ImportConflictPolicy?> RequestImportConflictPolicyAsync(
        FileMovePreflightResult preflight)
    {
        var handler = ImportPreflightRequested;
        if (handler is null)
        {
            return ImportConflictPolicy.AutoRename;
        }

        var completion = new TaskCompletionSource<ImportConflictPolicy?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            handler(this, new ImportPreflightRequestedEventArgs(preflight, completion));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to request import preflight confirmation.");
            return ImportConflictPolicy.AutoRename;
        }

        return await completion.Task;
    }

    private Task CreateStyledNormalBoxAsync(BoxVisualStyleOption? option)
    {
        return option is null
            ? Task.CompletedTask
            : CreateBoxAsync(BoxType.Normal, option.Style);
    }

    private async Task CreateBoxAsync(
        BoxType type,
        BoxVisualStyle? visualStyle = null)
    {
        await RunBusyAsync(async () =>
        {
            var prefix = type switch
            {
                BoxType.Normal => "普通收纳盒",
                BoxType.Mapping => "映射收纳盒",
                BoxType.Pixel => "像素收纳盒",
                BoxType.Todo => "待办收纳盒",
                BoxType.Drawer => "抽屉盒",
                BoxType.Inbox => "临时收件箱",
                BoxType.Smart => "智能收纳盒",
                _ => "收纳盒"
            };
            var matchingBoxCount = type == BoxType.Normal
                ? Boxes.Count(box => box.Type is BoxType.Normal or BoxType.Pixel)
                : Boxes.Count(box => box.Type == type);
            var name = $"{prefix} {matchingBoxCount + 1}";
            var box = await _drawerService.CreateBoxAsync(name, type);
            if (type == BoxType.Drawer)
            {
                await _drawerService.SetSettingAsync(
                    BoxViewModel.GetLayoutPresetSettingKey(box.Id),
                    DesktopBoxLayoutSettings.DefaultDrawerPreset);
            }
            var effectiveStyle = visualStyle ?? BoxVisualStyle.Modern;
            if (type == BoxType.Normal)
            {
                try
                {
                    await _boxVisualStyleStore.SaveAsync(box.Id, effectiveStyle);
                }
                catch
                {
                    await CompensateFailedStyledBoxCreationAsync(box.Id);
                    throw;
                }
            }

            var viewModel = new BoxViewModel(
                box,
                _drawerService,
                effectiveStyle,
                isPositionLocked: false,
                logger: _logger);
            Boxes.Add(viewModel);
            await SelectBoxAsync(viewModel);
            StatusText = $"已创建 {name}，桌面收纳栏已生成";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task SetSelectedBoxVisualStyleAsync(BoxVisualStyleOption? option)
    {
        var selectedBox = SelectedBox;
        if (option is null || selectedBox?.CanSelectVisualStyle != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _boxVisualStyleStore.SaveAsync(selectedBox.Id, option.Style);
            selectedBox.ApplyVisualStyle(option.Style);
            await LoadItemsForSelectedBoxAsync(selectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = $"已将“{selectedBox.Name}”切换为{option.Name}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task ToggleSelectedBoxPositionLockAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var isPositionLocked = !selectedBox.IsPositionLocked;
            await _boxPositionLockStateStore.SaveAsync(
                selectedBox.Id,
                isPositionLocked);
            selectedBox.ApplyPositionLockState(isPositionLocked);
            WeakReferenceMessenger.Default.Send(
                new BoxPositionLockStateChangedMessage(
                    selectedBox.Id,
                    isPositionLocked));
            StatusText = isPositionLocked
                ? $"已锁定“{selectedBox.Name}”的桌面位置"
                : $"已解锁“{selectedBox.Name}”的桌面位置";
        });
    }

    private async Task CompensateFailedStyledBoxCreationAsync(Guid boxId)
    {
        try
        {
            await _drawerService.DeleteBoxAsync(boxId);
            _logger.Info(
                $"Removed empty box {boxId:N} after visual style persistence failed.");
        }
        catch (Exception compensationException)
        {
            _logger.Error(
                compensationException,
                $"Failed to remove empty box {boxId:N} after visual style persistence failed.");
        }
    }

    private async Task<(Box Box, BoxVisualStyle VisualStyle, bool IsPositionLocked)[]> LoadBoxPresentationAsync(
        IReadOnlyList<Box> boxes)
    {
        return await Task.WhenAll(
            boxes.Select(async box =>
            {
                var visualStyleTask = _boxVisualStyleStore.LoadAsync(box);
                var positionLockStateTask =
                    _boxPositionLockStateStore.LoadAsync(box.Id);
                await Task.WhenAll(visualStyleTask, positionLockStateTask);
                return (
                    Box: box,
                    VisualStyle: await visualStyleTask,
                    IsPositionLocked: await positionLockStateTask);
            }));
    }

    private async Task DeleteSelectedBoxAsync()
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _drawerService.DeleteBoxAsync(selectedBox.Id);

            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(
                result.BoxRemoved
                    ? Boxes.FirstOrDefault()
                    : Boxes.FirstOrDefault(box => box.Id == result.BoxId) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = result.StatusMessage;
            BoxesChanged?.Invoke(this, EventArgs.Empty);
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(selectedBox.Id));
        });
    }

    private async Task RenameSelectedBoxAsync(string? newName)
    {
        var selectedBox = SelectedBox;
        if (selectedBox is null || newName is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.RenameBoxAsync(selectedBox.Id, newName);

            var boxes = await _drawerService.GetBoxesAsync();
            var presentedBoxes = await LoadBoxPresentationAsync(boxes);
            Boxes.Clear();
            foreach (var (box, visualStyle, isPositionLocked) in presentedBoxes)
            {
                Boxes.Add(new BoxViewModel(
                    box,
                    _drawerService,
                    visualStyle,
                    isPositionLocked,
                    _logger));
            }

            await SelectBoxAsync(Boxes.FirstOrDefault(b => b.Id == selectedBox.Id) ?? Boxes.FirstOrDefault());

            await _quickPanelViewModel.RefreshBoxAsync(selectedBox.Id);
            StatusText = $"已重命名收纳盒为 {newName.Trim()}";
            BoxesChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool UpdateSelectedBoxCore(BoxViewModel? value)
    {
        if (EqualityComparer<BoxViewModel?>.Default.Equals(_selectedBox, value))
        {
            return false;
        }

        _selectedBox = value;
        BoxSizeSettings.SetTargetBox(value);
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(IsSelectedTodoBox));
        OnPropertyChanged(nameof(CanImportFiles));
        DeleteSelectedBoxCommand.NotifyCanExecuteChanged();
        RenameSelectedBoxCommand.NotifyCanExecuteChanged();
        SetSelectedBoxVisualStyleCommand.NotifyCanExecuteChanged();
        ToggleSelectedBoxPositionLockCommand.NotifyCanExecuteChanged();
        return true;
    }

    private async Task SelectBoxAsync(BoxViewModel? box)
    {
        UpdateSelectedBoxCore(box);
        await LoadItemsForSelectedBoxAsync(box);
    }

    private void QueueSelectedBoxItemsLoad()
    {
        var selectedBox = SelectedBox;
        var (version, cancellationToken) = BeginItemsLoad();
        _ = LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private async Task LoadItemsForSelectedBoxAsync(BoxViewModel? selectedBox)
    {
        var (version, cancellationToken) = BeginItemsLoad();
        await LoadItemsForSelectedBoxAsync(selectedBox, version, cancellationToken);
    }

    private (int Version, CancellationToken CancellationToken) BeginItemsLoad()
    {
        _itemsLoadCts?.Cancel();
        _itemsLoadCts = new CancellationTokenSource();

        var version = Interlocked.Increment(ref _itemsLoadVersion);
        return (version, _itemsLoadCts.Token);
    }

    private bool IsCurrentItemsLoad(BoxViewModel? selectedBox, int version)
    {
        return version == Volatile.Read(ref _itemsLoadVersion)
            && SelectedBox?.Id == selectedBox?.Id;
    }

    private async Task LoadItemsForSelectedBoxAsync(
        BoxViewModel? selectedBox,
        int version,
        CancellationToken cancellationToken)
    {
        if (selectedBox?.IsTodoBox == true)
        {
            if (IsCurrentItemsLoad(selectedBox, version))
            {
                Items.ReplaceAll([]);
            }

            await TodoBoxDetail.LoadAsync(selectedBox.Id);
            return;
        }

        await TodoBoxDetail.LoadAsync(null);
        if (selectedBox is null)
        {
            if (IsCurrentItemsLoad(null, version))
            {
                Items.ReplaceAll([]);
            }

            return;
        }

        try
        {
            await selectedBox.EnsurePresentationSettingsLoadedAsync();

            if (selectedBox.IsSmartBox)
            {
                try
                {
                    await _drawerService.SyncSmartBoxIfStaleAsync(
                        selectedBox.Id,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.Error(
                        exception,
                        $"Failed to synchronize smart box {selectedBox.Id:N}.");
                    StatusText = "智能盒规则未配置或源目录不可用";
                }
            }

            var items = await _drawerService.GetItemsAsync(selectedBox.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            var isPixelated = selectedBox.IsPixelStyle;
            var iconPixelSize = GetIconPixelSize(isPixelated);
            var existingById = Items.ToDictionary(item => item.Id);
            var nextItems = new List<DrawerItemViewModel>(items.Count);
            foreach (var item in items)
            {
                if (!existingById.TryGetValue(item.Id, out var itemViewModel)
                    || itemViewModel.IsPixelated != isPixelated
                    || !string.Equals(
                        itemViewModel.BoxName,
                        selectedBox.Name,
                        StringComparison.Ordinal))
                {
                    itemViewModel = new DrawerItemViewModel(
                        item,
                        selectedBox.Name,
                        isPixelated,
                        iconPixelSize,
                        _logger);
                }

                itemViewModel.RequestIconSize(iconPixelSize);
                nextItems.Add(itemViewModel);
            }

            Items.ReplaceAll(nextItems);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentItemsLoad(selectedBox, version))
            {
                return;
            }

            _logger.Error(exception, "Failed to load drawer items.");
            StatusText = exception.Message;
        }
    }

    private int GetIconPixelSize(bool isPixelated)
    {
        return DpiAwareIconSize.Calculate(
            ItemIconSizeDip,
            ItemIconSizeDip,
            _iconDpiScaleX,
            _iconDpiScaleY,
            isPixelated);
    }

    private static double NormalizeDpiScale(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = $"已打开 {item.DisplayName}";
        });
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _drawerService.DeleteItemAsync(item.Id);
            await LoadItemsForSelectedBoxAsync(SelectedBox);
            await _quickPanelViewModel.RefreshBoxAsync(item.Model.BoxId);
            StatusText = result.StatusMessage;
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(item.Model.BoxId));
        });
    }

    public async Task BatchOpenItemsAsync(IEnumerable<DrawerItemViewModel> items)
    {
        var targets = NormalizeBatchItems(items);
        if (targets.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var succeeded = 0;
            var failures = new List<string>();
            foreach (var item in targets)
            {
                try
                {
                    await _drawerService.OpenItemAsync(item.Id, _launcher);
                    succeeded++;
                }
                catch (Exception exception)
                {
                    failures.Add($"{item.DisplayName}: {exception.Message}");
                    _logger.Error(exception, $"Failed to open {item.DisplayName}.");
                }
            }

            StatusText = BatchStatus("打开", succeeded, failures);
        });
    }

    public async Task BatchDeleteItemsAsync(IEnumerable<DrawerItemViewModel> items)
    {
        var targets = NormalizeBatchItems(items);
        if (targets.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var succeeded = 0;
            var failures = new List<string>();
            var affectedBoxIds = new HashSet<Guid>();
            foreach (var item in targets)
            {
                try
                {
                    await _drawerService.DeleteItemAsync(item.Id);
                    succeeded++;
                    affectedBoxIds.Add(item.Model.BoxId);
                }
                catch (Exception exception)
                {
                    failures.Add($"{item.DisplayName}: {exception.Message}");
                    _logger.Error(exception, $"Failed to delete {item.DisplayName}.");
                }
            }

            await LoadItemsForSelectedBoxAsync(SelectedBox);
            foreach (var boxId in affectedBoxIds)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
                ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
            }

            StatusText = BatchStatus("删除", succeeded, failures);
            await LoadFileOperationHistoryAsync();
        });
    }

    public async Task BatchExportItemsAsync(IEnumerable<DrawerItemViewModel> items)
    {
        var targets = NormalizeBatchItems(items);
        if (targets.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                StatusText = "无法定位桌面目录";
                return;
            }

            var succeeded = 0;
            var failures = new List<string>();
            var affectedBoxIds = new HashSet<Guid>();
            foreach (var item in targets)
            {
                try
                {
                    await _drawerService.ExportItemToDirectoryAsync(item.Id, desktop);
                    succeeded++;
                    affectedBoxIds.Add(item.Model.BoxId);
                }
                catch (Exception exception)
                {
                    failures.Add($"{item.DisplayName}: {exception.Message}");
                    _logger.Error(exception, $"Failed to export {item.DisplayName}.");
                }
            }

            await LoadItemsForSelectedBoxAsync(SelectedBox);
            foreach (var boxId in affectedBoxIds)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
                ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
            }

            StatusText = BatchStatus("导出", succeeded, failures);
            await LoadFileOperationHistoryAsync();
        });
    }

    public async Task BatchMoveItemsAsync(
        IEnumerable<DrawerItemViewModel> items,
        Guid targetBoxId)
    {
        var targets = NormalizeBatchItems(items);
        if (targets.Length == 0)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var succeeded = 0;
            var failures = new List<string>();
            var affectedBoxIds = new HashSet<Guid> { targetBoxId };
            foreach (var item in targets)
            {
                try
                {
                    await _drawerService.MoveItemToBoxAsync(item.Id, targetBoxId);
                    succeeded++;
                    affectedBoxIds.Add(item.Model.BoxId);
                }
                catch (Exception exception)
                {
                    failures.Add($"{item.DisplayName}: {exception.Message}");
                    _logger.Error(exception, $"Failed to move {item.DisplayName}.");
                }
            }

            await LoadItemsForSelectedBoxAsync(SelectedBox);
            foreach (var boxId in affectedBoxIds)
            {
                await _quickPanelViewModel.RefreshBoxAsync(boxId);
                ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
            }

            StatusText = BatchStatus("移动", succeeded, failures);
            await LoadFileOperationHistoryAsync();
        });
    }

    private static DrawerItemViewModel[] NormalizeBatchItems(
        IEnumerable<DrawerItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .Where(item => item is not null)
            .DistinctBy(item => item.Id)
            .ToArray();
    }

    private static string BatchStatus(
        string operation,
        int succeeded,
        IReadOnlyCollection<string> failures)
    {
        if (failures.Count == 0)
        {
            return $"已{operation} {succeeded} 项";
        }

        return succeeded > 0
            ? $"已{operation} {succeeded} 项，{failures.Count} 项失败：{failures.First()}"
            : $"{operation}失败 {failures.Count} 项：{failures.First()}";
    }

    private async Task ShowArchiveAsync()
    {
        SelectedBox = null;
        IsArchivePage = true;
        IsSettingsPage = false;
        IsAboutPage = false;
        await LoadArchivedTodosAsync();
    }

    private async Task RestoreArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _todoService.RestoreArchivedAsync(todo.Id);
            await LoadArchivedTodosAsync();
            if (SelectedBox?.Id == todo.Model.BoxId)
            {
                await TodoBoxDetail.LoadAsync(todo.Model.BoxId);
            }

            StatusText = $"已将“{todo.Title}”恢复到 {todo.BoxName}";
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(todo.Model.BoxId));
        });
    }

    private async Task DeleteArchivedTodoAsync(ArchivedTodoItemViewModel? todo)
    {
        if (todo is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var undo = await _todoService.DeleteWithUndoAsync(todo.Id);
            ArchivedTodos.Remove(todo);
            ArchiveUndo.Offer(undo);
            StatusText = $"已删除归档事项“{todo.Title}”，10 秒内可撤销";
        });
    }

    private Task UndoArchivedDeleteAsync()
    {
        var pending = ArchiveUndo.Pending;
        if (pending is null) return Task.CompletedTask;
        return RunBusyAsync(async () =>
        {
            await _todoService.UndoDeleteAsync(pending.Token);
            ArchiveUndo.Clear();
            await LoadArchivedTodosAsync();
            StatusText = "已撤销删除归档事项";
        });
    }

    private async Task LoadArchivedTodosAsync()
    {
        try
        {
            var archivedTodos = await _todoService.GetArchivedTodosAsync();
            var boxNames = Boxes.ToDictionary(box => box.Id, box => box.Name);

            ArchivedTodos.Clear();
            foreach (var todo in archivedTodos)
            {
                var boxName = boxNames.GetValueOrDefault(todo.BoxId, "待办收纳盒");
                ArchivedTodos.Add(new ArchivedTodoItemViewModel(todo, boxName));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load archived todos.");
            StatusText = exception.Message;
        }
    }

    private void OnTodoBoxDetailItemsChanged(object? sender, EventArgs e)
    {
        StatusText = TodoBoxDetail.StatusText;
        if (TodoBoxDetail.BoxId is Guid boxId)
        {
            ItemsChanged?.Invoke(this, new BoxItemsChangedEventArgs(boxId));
        }
    }

    private async Task ApplyThemeAsync(AppTheme theme)
    {
        try
        {
            AppThemeManager.Apply(theme);
            SetCurrentTheme(theme);
            await _drawerService.SetSettingAsync(ThemeSettingKey, theme.ToString());
            StatusText = $"已切换到 {ThemeLabel} 风格";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to apply theme.");
            StatusText = exception.Message;
        }
    }

    private void SetCurrentTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        SynchronizeThemeTransparency();
        UpdateThemeLabel();
    }

    private void UpdateThemeLabel()
    {
        ThemeLabel = CurrentTheme switch
        {
            AppTheme.Glass => "暗黑曜石",
            AppTheme.Crystal => "全透水晶",
            _ => "清透雅致"
        };
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Operation failed.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            FlushPendingDesktopReload();
        }
    }

    private async Task ToggleLaunchOnStartupAsync()
    {
        try
        {
            var newState = !LaunchOnStartup;
            WriteStartupRegistry(newState);
            LaunchOnStartup = newState;
            StatusText = newState ? "已开启开机自启动" : "已关闭开机自启动";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle startup registry key.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopIconsAsync()
    {
        try
        {
            var hidden = !AreDesktopIconsHidden;
            await DesktopIconVisibility.SetHiddenAsync(hidden);
            AreDesktopIconsHidden = hidden;
            StatusText = hidden ? "已隐藏 Windows 桌面图标" : "已显示 Windows 桌面图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle Windows desktop icons.");
            StatusText = exception.Message;
        }
    }

    private async Task ToggleDesktopDoubleClickAsync()
    {
        try
        {
            var enabled = !IsDesktopDoubleClickEnabled;
            await _drawerService.SetSettingAsync(
                DesktopDoubleClickSettingKey,
                enabled.ToString());
            IsDesktopDoubleClickEnabled = enabled;
            StatusText = enabled ? "已开启桌面双击切换图标" : "已关闭桌面双击切换图标";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save desktop double-click setting.");
            OnPropertyChanged(nameof(IsDesktopDoubleClickEnabled));
            StatusText = exception.Message;
        }
    }

    private static bool ReadStartupRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            var value = key?.GetValue(StartupRegistryKeyName) as string;
            value ??= key?.GetValue(LegacyStartupRegistryKeyName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteStartupRegistry(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

        if (key is null)
        {
            return;
        }

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(StartupRegistryKeyName, $"\"{exePath}\" --silent");
                key.DeleteValue(LegacyStartupRegistryKeyName, throwOnMissingValue: false);
            }
        }
        else
        {
            key.DeleteValue(StartupRegistryKeyName, throwOnMissingValue: false);
            key.DeleteValue(LegacyStartupRegistryKeyName, throwOnMissingValue: false);
        }
    }

    private static void MigrateLegacyStartupRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            if (key?.GetValue(LegacyStartupRegistryKeyName) is not string legacyValue
                || string.IsNullOrWhiteSpace(legacyValue))
            {
                return;
            }

            if (key.GetValue(StartupRegistryKeyName) is not string currentValue
                || string.IsNullOrWhiteSpace(currentValue))
            {
                key.SetValue(StartupRegistryKeyName, legacyValue);
            }

            key.DeleteValue(LegacyStartupRegistryKeyName, throwOnMissingValue: false);
        }
        catch
        {
            // Startup registration migration is best-effort.
        }
    }

    private async Task RestoreThemeBoxOpacitiesAsync()
    {
        var themes = Enum.GetValues<AppTheme>();
        var settingKeys = themes
            .Select(GetThemeBoxOpacitySettingKey)
            .Append(ThemeBoxOpacityMigrationVersionSettingKey)
            .ToArray();
        var savedSettings = await _drawerService.GetSettingsAsync(settingKeys);
        savedSettings.TryGetValue(
            ThemeBoxOpacityMigrationVersionSettingKey,
            out var migrationVersion);
        if (string.Equals(
                migrationVersion,
                ThemeBoxOpacityMigrationVersion,
                StringComparison.Ordinal))
        {
            foreach (var theme in themes)
            {
                savedSettings.TryGetValue(GetThemeBoxOpacitySettingKey(theme), out var savedOpacity);
                AppThemeManager.SetBoxOpacity(theme, ParseSavedOpacity(savedOpacity));
            }

            SynchronizeThemeTransparency();
            return;
        }

        var valuesToSave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var theme in themes)
        {
            var opacity = AppThemeManager.GetDefaultBoxOpacity(theme);
            if (string.Equals(migrationVersion, "1", StringComparison.Ordinal))
            {
                savedSettings.TryGetValue(
                    GetThemeBoxOpacitySettingKey(theme),
                    out var savedOpacityValue);
                var savedOpacity = ParseSavedOpacity(
                    savedOpacityValue);
                if (!IsVersionOneGeneratedDefault(savedOpacity))
                {
                    opacity = savedOpacity;
                }
            }

            AppThemeManager.SetBoxOpacity(theme, opacity);
            valuesToSave[GetThemeBoxOpacitySettingKey(theme)] = FormatOpacity(opacity);
        }

        valuesToSave[ThemeBoxOpacityMigrationVersionSettingKey] =
            ThemeBoxOpacityMigrationVersion;
        await _drawerService.SetSettingsAsync(valuesToSave);
        SynchronizeThemeTransparency();
    }

    private async Task ToggleEditorOpacityFollowAsync()
    {
        try
        {
            var enabled = !EditorFollowsBoxOpacity;
            await _drawerService.SetSettingAsync(
                EditorFollowsBoxOpacitySettingKey,
                enabled.ToString());
            EditorFollowsBoxOpacity = enabled;
            StatusText = enabled
                ? "编辑页已跟随桌面盒子透明度"
                : "编辑页已保持标准透明度";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save editor opacity follow setting.");
            OnPropertyChanged(nameof(EditorFollowsBoxOpacity));
            StatusText = exception.Message;
        }
    }

    private async Task ToggleIconToolTipCompactAsync()
    {
        try
        {
            var compact = !IconToolTipCompact;
            await _drawerService.SetSettingAsync(
                IconToolTipCompactSettingKey,
                compact.ToString());
            IconToolTipCompact = compact;
            PublishIconToolTipMode();
            StatusText = compact
                ? "图标名称已设为精简显示"
                : "图标名称已设为完整显示";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save icon tooltip compact setting.");
            OnPropertyChanged(nameof(IconToolTipCompact));
            StatusText = exception.Message;
        }
    }

    private void PublishIconToolTipMode()
    {
        WeakReferenceMessenger.Default.Send(
            new IconToolTipModeChangedMessage(IconToolTipCompact));
    }

    private async Task ToggleAutoHideEnabledAsync()
    {
        try
        {
            var enabled = !AutoHideEnabled;
            AutoHideEnabled = enabled;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = enabled ? "已开启自动隐藏" : "已关闭自动隐藏";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide enable setting.");
            OnPropertyChanged(nameof(AutoHideEnabled));
            StatusText = exception.Message;
        }
    }

    private async Task ApplyAutoHideRevealScopeAsync(AutoHideRevealScope scope)
    {
        try
        {
            AutoHideRevealScope = scope;
            PublishAutoHideSettings();
            await SaveAutoHideSettingsAsync();
            StatusText = scope == AutoHideRevealScope.AllBoxes
                ? "悬停任一收纳盒将全部显示"
                : "悬停某个收纳盒仅其内容显示";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide reveal scope setting.");
            OnPropertyChanged(nameof(AutoHideRevealScope));
            StatusText = exception.Message;
        }
    }

    private Task SaveAutoHideSettingsAsync()
    {
        return _autoHideSettingsStore.SaveAsync(new AutoHideSettings(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void PublishAutoHideSettings()
    {
        WeakReferenceMessenger.Default.Send(new AutoHideSettingsChangedMessage(
            AutoHideEnabled,
            AutoHideHiddenTransparencyPercent,
            AutoHideRevealScope,
            AutoHideFadeWholeBox,
            AutoHideFadeTitle,
            AutoHideFadeBorder));
    }

    private void QueueAutoHideSave()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _autoHideSaveCts, next);
        // 只取消不立即 Dispose：旧任务可能仍挂在该 token 的 Task.Delay 上，
        // 此时 Dispose 会让其回调注册抛出 ObjectDisposedException（被误记为保存失败）。
        // 已取消且无注册的 CancellationTokenSource 由 GC 回收即可。
        previous?.Cancel();

        _ = PersistAutoHideAfterDelayAsync(next.Token);
    }

    private async Task PersistAutoHideAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 应用已在属性 setter 中即时完成，这里只负责持久化。
            await SaveAutoHideSettingsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save auto hide settings.");
        }
    }

    private async Task RestoreAppearanceOpacitiesAsync()
    {
        var themes = Enum.GetValues<AppTheme>();
        var settingKeys = themes
            .SelectMany(theme => new[]
            {
                BoxBorderOpacitySettingKeyPrefix + theme,
                IconFrameOpacitySettingKeyPrefix + theme
            })
            .ToArray();
        var savedSettings = await _drawerService.GetSettingsAsync(settingKeys);
        foreach (var theme in themes)
        {
            savedSettings.TryGetValue(
                BoxBorderOpacitySettingKeyPrefix + theme,
                out var boxBorderOpacity);
            savedSettings.TryGetValue(
                IconFrameOpacitySettingKeyPrefix + theme,
                out var iconFrameOpacity);
            AppThemeManager.SetBoxBorderOpacity(theme, ParseAppearanceOpacity(
                boxBorderOpacity));
            AppThemeManager.SetIconFrameOpacity(theme, ParseAppearanceOpacity(
                iconFrameOpacity));
        }

        SynchronizeThemeTransparency();
    }

    private static double? ParseAppearanceOpacity(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)
        && double.IsFinite(opacity) && opacity >= 0 && opacity <= 1 ? opacity : null;

    private static bool IsVersionOneGeneratedDefault(double opacity)
    {
        return Math.Abs(opacity - AppThemeManager.DefaultBoxOpacity) < 0.0001
            || Math.Abs(opacity - AppThemeManager.MaximumBoxOpacity) < 0.0001;
    }

    private void SynchronizeThemeTransparency()
    {
        _isSynchronizingThemeTransparency = true;
        try
        {
            ThemeTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetBoxOpacity(CurrentTheme)) * 100);
            BoxBorderTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetBoxBorderOpacity(CurrentTheme)) * 100);
            IconFrameTransparencyPercent =
                Math.Round((1 - AppThemeManager.GetIconFrameOpacity(CurrentTheme)) * 100);
        }
        finally
        {
            _isSynchronizingThemeTransparency = false;
        }
    }

    private void QueueThemeOpacitySave(string settingKey, string? value)
    {
        CancellationTokenSource delay;
        lock (_themeOpacitySaveLock)
        {
            if (_themeOpacitySaveDelays.TryGetValue(settingKey, out var previousDelay))
            {
                previousDelay.Cancel();
            }

            delay = new CancellationTokenSource();
            _themeOpacitySaveDelays[settingKey] = delay;
        }

        _ = PersistThemeOpacityAfterDelayAsync(settingKey, value, delay);
    }

    private async Task PersistThemeOpacityAfterDelayAsync(
        string settingKey,
        string? value,
        CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(250, delay.Token).ConfigureAwait(false);
            await _themeOpacityWriteGate.WaitAsync(delay.Token).ConfigureAwait(false);
            try
            {
                delay.Token.ThrowIfCancellationRequested();
                if (value is null)
                {
                    await _drawerService.DeleteSettingAsync(settingKey, delay.Token).ConfigureAwait(false);
                }
                else
                {
                    await _drawerService.SetSettingAsync(settingKey, value, delay.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _themeOpacityWriteGate.Release();
            }
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested)
        {
            // 连续拖动时只保存停止后的最终值。
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Failed to persist {settingKey}.");
        }
        finally
        {
            lock (_themeOpacitySaveLock)
            {
                if (_themeOpacitySaveDelays.TryGetValue(settingKey, out var currentDelay)
                    && ReferenceEquals(currentDelay, delay))
                {
                    _themeOpacitySaveDelays.Remove(settingKey);
                }
            }

            delay.Dispose();
        }
    }

    internal static string GetThemeBoxOpacitySettingKey(AppTheme theme)
    {
        return ThemeBoxOpacitySettingKeyPrefix + theme;
    }

    private static string FormatOpacity(double opacity)
    {
        return opacity.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static double ParseSavedOpacity(string? value)
    {
        return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var opacity)
            ? Math.Clamp(
                opacity,
                AppThemeManager.MinimumBoxOpacity,
                AppThemeManager.MaximumBoxOpacity)
            : AppThemeManager.DefaultBoxOpacity;
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var value = await _drawerService.GetSettingAsync(AutomaticUpdateCheckSettingKey);
            var lastCheck = DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
            if (!ShouldCheckForUpdates(lastCheck, DateTimeOffset.UtcNow))
            {
                return;
            }

            var succeeded = await CheckForUpdateAsync(showFailureStatus: false);
            if (succeeded)
            {
                await _drawerService.SetSettingAsync(
                    AutomaticUpdateCheckSettingKey,
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Automatic update check failed.");
        }
    }

    internal static bool ShouldCheckForUpdates(
        DateTimeOffset? lastCheck,
        DateTimeOffset now)
    {
        return lastCheck is null
            || lastCheck > now
            || now - lastCheck.Value >= AutomaticUpdateCheckInterval;
    }

    private async Task<bool> CheckForUpdateAsync(bool showFailureStatus)
    {
        if (IsCheckingUpdate)
        {
            return false;
        }

        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在检查更新...";

            var currentVersion = GetCurrentVersion();
            var result = await _updateService.CheckForUpdateAsync(currentVersion);
            if (!result.IsSuccessful)
            {
                if (showFailureStatus)
                {
                    UpdateStatusText = "检查更新失败";
                    StatusText = UpdateStatusText;
                }

                return false;
            }

            if (!result.HasUpdate)
            {
                UpdateStatusText = $"已是最新版本 v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}";
                StatusText = UpdateStatusText;
                return true;
            }

            var versionText = $"v{result.LatestVersion.Major}.{result.LatestVersion.Minor}.{result.LatestVersion.Build}";
            UpdateStatusText = $"发现新版本 {versionText}";
            StatusText = UpdateStatusText;
            _pendingUpdateSha256 = result.ExpectedSha256;

            UpdateRequested?.Invoke(this, result);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update check failed.");
            if (showFailureStatus)
            {
                UpdateStatusText = "检查更新失败";
                StatusText = UpdateStatusText;
            }

            return false;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public async Task ExecuteUpdateAsync(string downloadUrl)
    {
        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "正在下载更新...";

            var progress = new Progress<int>(percent =>
            {
                UpdateStatusText = $"正在下载更新... {percent}%";
            });

            var success = await _updateService.DownloadAndApplyUpdateAsync(
                downloadUrl,
                progress,
                _pendingUpdateSha256);

            if (success)
            {
                UpdateStatusText = "更新下载完成，正在重启...";
                StatusText = UpdateStatusText;
                UpdateConfirmed?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                UpdateStatusText = "下载更新失败";
                StatusText = UpdateStatusText;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Update download failed.");
            UpdateStatusText = "下载更新失败";
            StatusText = UpdateStatusText;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    public event EventHandler<UpdateCheckResult>? UpdateRequested;
    public event EventHandler? UpdateConfirmed;

    private static Version GetCurrentVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }

    private DataSafetyService RequireDataSafetyService()
    {
        return _dataSafetyService
            ?? throw new InvalidOperationException("数据安全服务尚未初始化。");
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.00} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024d):0.00} MB";
        }

        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return bytes + " B";
    }
}
