using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Models.Legacy;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Legacy;
using FalconBMS.Launcher.Utils;
using FalconBMS.Launcher.Views;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Main page view model that manages installs, theaters, RSS, and Launcher strip items.
/// </summary>

public sealed class MainViewModel : ViewModelBase
{
    private readonly InstallDiscoveryService _discovery = new();
    private readonly ProcessService _proc = new();
    private readonly LaunchPrepService _launchPrep = new();
    private readonly FolderService _folders = new();
    private readonly RssService _rss = new();
    private readonly RegistryService _registry = new();
    private readonly CallsignService _callsign = new();
    private readonly TheaterDiscoveryService _theaterDiscovery = new();
    private readonly FirstPartyLauncherStripService _firstPartyStrip = new();
    private readonly ThirdPartyLauncherStripService _thirdPartyStrip = new();
    private readonly KeyCatalogService _keyCatalogService = new();
    private readonly BindingModelBuilderService _bindingModelBuilder = new();
    private readonly JsonKeyboardBindingReaderService _jsonKeyboardBindingReader = new();
    private readonly DeviceDiscoveryService _deviceDiscovery = new();
    private readonly DeviceBindingProfileBuilderService _deviceBindingProfileBuilder = new();
    private readonly DeviceJsonReaderService _deviceJsonReader = new();
    private readonly LegacyImportService _legacyImport = new();

    // Tracks catalog-vs-JSON differences discovered during startup.
    // This is separate from ControlsViewModel.IsDirty because BMS adding or removing
    // callbacks should sync JSON without being treated as a user binding edit.
    private bool _needsKeyboardJsonCatalogSync;

    // Set when missing device JSON files/profiles were rebuilt from stock/defaults
    // during startup and need to be written back on close or launch.
    private bool _needsDeviceJsonSync;

    // Set when a keyboard/device JSON file failed to read during startup.
    // When true, output writes are blocked so fallback/partial in-memory data
    // cannot overwrite the user's broken JSON file or generated outputs.
    private bool _jsonReadFailureBlocksOutputSave;

    // Prevents the startup warning from being shown more than once for the same load.
    private bool _hasShownJsonReadFailureStartupWarning;

    public ObservableCollection<BmsInstall> Installs { get; } = new();
    public ObservableCollection<RssItemViewModel> NewsItems { get; } = new();
    public ObservableCollection<string> Theaters { get; } = new();
    public ObservableCollection<LauncherStripItem> FirstPartyItems { get; } = new();
    public ObservableCollection<ThirdPartyToolItem> ThirdPartyItems { get; } = new();

    private bool _isEditingCommunityTools;
    public bool IsEditingCommunityTools
    {
        get => _isEditingCommunityTools;
        set => Set(ref _isEditingCommunityTools, value);
    }

    public IReadOnlyList<KeyCatalog> KeyCatalogs { get; private set; } = Array.Empty<KeyCatalog>();
    public BindingModel CurrentBindingModel { get; private set; } = new();

    private BmsInstall? _selectedInstall;
    public BmsInstall? SelectedInstall
    {
        get => _selectedInstall;
        set
        {
            if (!Set(ref _selectedInstall, value)) return;

            if (value is not null)
            {
                DebugDiagnosticsService.InitializeForInstall(value.BaseDir);
                DebugDiagnosticsService.Info($"Selected install changed to: {value.RegistryKeyName} ({value.BaseDir})");

                Properties.Settings.Default.LastInstall = value.RegistryKeyName;
                Properties.Settings.Default.Save();

                if (!HandleStartupLegacyImport(value))
                    return;

                // Build the complete model (catalogs + keyboard JSON + device JSON) before
                // firing any notification. Subscribers see a fully populated model on the
                // first and only notification rather than an incomplete one.
                LoadFullBindingModelForInstall(value);

                OnPropertyChanged(nameof(CurrentBindingModel));

                ShowJsonReadFailureStartupWarningIfNeeded();

                LoadTheaterForSelectedInstall();

                RefreshLauncherStrips();
            }
            else
            {
                DebugDiagnosticsService.Warn("Selected install cleared.");

                Theaters.Clear();
                SelectedTheater = null;

                KeyCatalogs = Array.Empty<KeyCatalog>();
                CurrentBindingModel = new();
                _needsKeyboardJsonCatalogSync = false;
                _needsDeviceJsonSync = false;
                _jsonReadFailureBlocksOutputSave = false;
                _hasShownJsonReadFailureStartupWarning = false;

                OnPropertyChanged(nameof(KeyCatalogs));
                OnPropertyChanged(nameof(CurrentBindingModel));

                RefreshLauncherStrips();
            }

            OnPropertyChanged(nameof(SelectedInstallHeaderVersion));
            OnPropertyChanged(nameof(SelectedInstallIsInternal));

            RaiseCommandStates();
        }
    }

    private string? _selectedTheater;
    private bool _isLoadingTheater;

    public string? SelectedTheater
    {
        get => _selectedTheater;
        set
        {
            if (!Set(ref _selectedTheater, value)) return;
            if (_isLoadingTheater) return;
            if (SelectedInstall is null) return;
            if (string.IsNullOrWhiteSpace(value)) return;

            var current = _registry.ReadString(SelectedInstall.RegistryKeyName, "curTheater");
            if (string.Equals(current, value, StringComparison.Ordinal))
                return;

            DebugDiagnosticsService.Info($"Writing theater selection: {value}");
            _registry.WriteString(SelectedInstall.RegistryKeyName, "curTheater", value!);
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private string _newsStatusText = "Loading news…";
    public string NewsStatusText
    {
        get => _newsStatusText;
        set
        {
            if (Set(ref _newsStatusText, value))
                OnPropertyChanged(nameof(IsNewsStatusVisible));
        }
    }

    public bool IsNewsStatusVisible => !string.IsNullOrWhiteSpace(NewsStatusText) && NewsItems.Count == 0;


    private string _launcherThemeMode = ThemeService.NormalizeThemeMode(Properties.Settings.Default.LauncherThemeMode);

    public bool LauncherThemeAuto
    {
        get => string.Equals(_launcherThemeMode, LauncherThemeModes.Auto, StringComparison.Ordinal);
        set
        {
            if (!value) return;
            SetLauncherThemeMode(LauncherThemeModes.Auto);
        }
    }

    public bool LauncherThemeLight
    {
        get => string.Equals(_launcherThemeMode, LauncherThemeModes.Light, StringComparison.Ordinal);
        set
        {
            if (!value) return;
            SetLauncherThemeMode(LauncherThemeModes.Light);
        }
    }

    public bool LauncherThemeDark
    {
        get => string.Equals(_launcherThemeMode, LauncherThemeModes.Dark, StringComparison.Ordinal);
        set
        {
            if (!value) return;
            SetLauncherThemeMode(LauncherThemeModes.Dark);
        }
    }

    private bool _launchAcmi = Properties.Settings.Default.CMD_ACMI;
    public bool LaunchAcmi
    {
        get => _launchAcmi;
        set
        {
            if (!Set(ref _launchAcmi, value)) return;
            Properties.Settings.Default.CMD_ACMI = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _launchWindow = Properties.Settings.Default.CMD_WINDOW;
    public bool LaunchWindow
    {
        get => _launchWindow;
        set
        {
            if (!Set(ref _launchWindow, value)) return;
            Properties.Settings.Default.CMD_WINDOW = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _launchNoMovie = Properties.Settings.Default.CMD_NOMOVIE;
    public bool LaunchNoMovie
    {
        get => _launchNoMovie;
        set
        {
            if (!Set(ref _launchNoMovie, value)) return;
            Properties.Settings.Default.CMD_NOMOVIE = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _launchEyeFly = Properties.Settings.Default.CMD_EF;
    public bool LaunchEyeFly
    {
        get => _launchEyeFly;
        set
        {
            if (!Set(ref _launchEyeFly, value)) return;
            Properties.Settings.Default.CMD_EF = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _launchDebug = Properties.Settings.Default.CMD_MONO;
    public bool LaunchDebug
    {
        get => _launchDebug;
        set
        {
            if (!Set(ref _launchDebug, value)) return;
            Properties.Settings.Default.CMD_MONO = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _exportRttTextures = Properties.Settings.Default.Misc_bExportRTTTextures;
    public bool ExportRttTextures
    {
        get => _exportRttTextures;
        set
        {
            if (!Set(ref _exportRttTextures, value)) return;
            Properties.Settings.Default.Misc_bExportRTTTextures = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _vrNoVr = Properties.Settings.Default.VR_NoVR;
    public bool VrNoVr
    {
        get => _vrNoVr;
        set
        {
            if (_vrNoVr == value) return;

            if (value)
            {
                _vrNoVr = true;
                _vrSteamVr = false;
                _vrOpenXr = false;
            }
            else if (!_vrSteamVr && !_vrOpenXr)
            {
                _vrNoVr = true;
            }
            else
            {
                _vrNoVr = false;
            }

            SaveVrSettings();
            OnPropertyChanged(nameof(VrNoVr));
            OnPropertyChanged(nameof(VrSteamVr));
            OnPropertyChanged(nameof(VrOpenXr));
        }
    }
    private void SetLauncherThemeMode(string themeMode)
    {
        var normalizedMode = ThemeService.NormalizeThemeMode(themeMode);
        if (string.Equals(_launcherThemeMode, normalizedMode, StringComparison.Ordinal))
            return;

        _launcherThemeMode = normalizedMode;

        ThemeService.ApplyTheme(normalizedMode);

        OnPropertyChanged(nameof(LauncherThemeAuto));
        OnPropertyChanged(nameof(LauncherThemeLight));
        OnPropertyChanged(nameof(LauncherThemeDark));
    }

    private bool _vrSteamVr = Properties.Settings.Default.VR_SteamVR;
    public bool VrSteamVr
    {
        get => _vrSteamVr;
        set
        {
            if (_vrSteamVr == value) return;

            if (value)
            {
                _vrSteamVr = true;
                _vrNoVr = false;
                _vrOpenXr = false;
            }
            else if (!_vrNoVr && !_vrOpenXr)
            {
                _vrNoVr = true;
                _vrSteamVr = false;
            }
            else
            {
                _vrSteamVr = false;
            }

            SaveVrSettings();
            OnPropertyChanged(nameof(VrNoVr));
            OnPropertyChanged(nameof(VrSteamVr));
            OnPropertyChanged(nameof(VrOpenXr));
        }
    }

    private bool _vrOpenXr = Properties.Settings.Default.VR_OpenXR;
    public bool VrOpenXr
    {
        get => _vrOpenXr;
        set
        {
            if (_vrOpenXr == value) return;

            if (value)
            {
                _vrOpenXr = true;
                _vrNoVr = false;
                _vrSteamVr = false;
            }
            else if (!_vrNoVr && !_vrSteamVr)
            {
                _vrNoVr = true;
                _vrOpenXr = false;
            }
            else
            {
                _vrOpenXr = false;
            }

            SaveVrSettings();
            OnPropertyChanged(nameof(VrNoVr));
            OnPropertyChanged(nameof(VrSteamVr));
            OnPropertyChanged(nameof(VrOpenXr));
        }
    }

    public RelayCommand LaunchCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand OpenDocsCommand { get; }
    public RelayCommand OpenUserCommand { get; }
    public RelayCommand OpenForumCommand { get; }
    public RelayCommand OpenScreenshotsCommand { get; }
    public RelayCommandParam LaunchFirstPartyCommand { get; }
    public RelayCommandParam LaunchThirdPartyCommand { get; }
    public RelayCommand AddThirdPartyToolCommand { get; }
    public RelayCommand ToggleCommunityToolsEditCommand { get; }
    public RelayCommandParam RemoveThirdPartyToolCommand { get; }

    public MainViewModel()
    {
        NormalizeVrState();

        LaunchCommand = new RelayCommand(LaunchSelected, () => SelectedInstall is not null);
        UpdateCommand = new RelayCommand(RunUpdaterSelected, () => SelectedInstall is not null);
        OpenDocsCommand = new RelayCommand(OpenDocs, () => SelectedInstall is not null);
        OpenUserCommand = new RelayCommand(OpenUser, () => SelectedInstall is not null);
        OpenScreenshotsCommand = new RelayCommand(OpenScreenshots, () => SelectedInstall is not null);
        OpenForumCommand = new RelayCommand(OpenForum);
        LaunchFirstPartyCommand = new RelayCommandParam(LaunchFirstParty, CanLaunchFirstParty);
        LaunchThirdPartyCommand = new RelayCommandParam(LaunchThirdParty);
        AddThirdPartyToolCommand = new RelayCommand(AddThirdPartyTool);
        ToggleCommunityToolsEditCommand =
            new RelayCommand(
                () => IsEditingCommunityTools = !IsEditingCommunityTools);
        RemoveThirdPartyToolCommand =
            new RelayCommandParam(RemoveThirdPartyTool);

        Init();
        RefreshLauncherStrips();
        _ = LoadNewsAsync();
    }

    private void Init()
    {
        DebugDiagnosticsService.Info("MainViewModel.Init starting.");

        var installs = _discovery.Discover();

        if (_proc.IsBmsUpdaterRunning(installs))
        {
            DebugDiagnosticsService.Warn("Falcon BMS Updater is currently running. Launcher will shut down.");

            MessageBox.Show(
                "Falcon BMS Updater is currently running. Please close it before starting the Launcher.",
                "Updater Running",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Application.Current.Shutdown();
            return;
        }

        Installs.Clear();
        foreach (var i in installs) Installs.Add(i);

        DebugDiagnosticsService.Info($"Install discovery complete. Count: {Installs.Count}");

        if (Installs.Count == 0)
        {
            StatusText = "No Falcon BMS installs detected.";
            DebugDiagnosticsService.Warn("Failed to find BMS installation.");
            RaiseCommandStates();
            return;
        }

        var last = Properties.Settings.Default.LastInstall;
        SelectedInstall = Installs.FirstOrDefault(i => i.RegistryKeyName == last) ?? Installs[0];
    }

    public void ReloadBindingModelForSelectedInstall()
    {
        if (SelectedInstall is null)
            return;

        DebugDiagnosticsService.Info("Reloading binding model after binding JSON import.");

        LoadFullBindingModelForInstall(SelectedInstall);
        OnPropertyChanged(nameof(CurrentBindingModel));
        ShowJsonReadFailureStartupWarningIfNeeded();
    }

    private bool HandleStartupLegacyImport(
        BmsInstall install)
    {
        if (HasExistingLauncherBindingJson(install.BaseDir))
        {
            DebugDiagnosticsService.Info(
                "Legacy import skipped because existing Launcher JSON bindings were found.");

            return true;
        }

        if (!_legacyImport.HasLegacyControlFiles(install.BaseDir))
        {
            DebugDiagnosticsService.Info(
                "Legacy import skipped because no Launcher JSON bindings or legacy v2 control files were found.");

            return true;
        }

        DebugDiagnosticsService.Info(
            "No Launcher JSON bindings were found, but legacy v2 control files exist. Running legacy import.");

        LegacyImportScanResult scanResult =
            _legacyImport.Scan(
                install.BaseDir);

        LegacyImportExecutionResult importResult =
            _legacyImport.Import(
                install.BaseDir,
                scanResult);

        if (!importResult.Succeeded)
        {
            ShowLegacyImportCompleteMessage(
                importResult);

            Application.Current.Shutdown();

            return false;
        }

        ApplyImportedLauncherSettings(
            importResult);

        ShowLegacyImportCompleteMessage(
            importResult);

        return true;
    }

    private static bool HasExistingLauncherBindingJson(
    string baseDir)
    {
        string jsonDir =
            Path.Combine(
                baseDir,
                "User",
                "Config",
                "JSON");

        if (!Directory.Exists(jsonDir))
            return false;

        return Directory.EnumerateFiles(jsonDir, "KeyboardBindings_*.json").Any()
            || Directory.EnumerateFiles(jsonDir, "DeviceBindings_*.json").Any();
    }

    private void ApplyImportedLauncherSettings(
        LegacyImportExecutionResult importResult)
    {
        _exportRttTextures =
            importResult.ExportRttTextures;

        Properties.Settings.Default.Misc_bExportRTTTextures =
            importResult.ExportRttTextures;

        Properties.Settings.Default.Save();

        OnPropertyChanged(nameof(ExportRttTextures));
    }

    private static void ShowLegacyImportCompleteMessage(
        LegacyImportExecutionResult importResult)
    {
        Window? ownerWindow =
            Application.Current.MainWindow;

        var completeWindow =
            new LegacyImportCompleteWindow(
                importResult);

        // During first-run v2-to-v3 import, this message can appear before MainWindow
        // has been shown. WPF does not allow assigning Owner to a window that has not
        // been shown yet, so only use ownership/overlay when the owner is actually visible
        Window? modalOwner =
            ownerWindow is not null &&
            ownerWindow != completeWindow &&
            ownerWindow.IsVisible
                ? ownerWindow
                : null;

        if (modalOwner is not null)
            completeWindow.Owner = modalOwner;

        using (FalconBMS.Launcher.MainWindow.BeginModalOverlay(modalOwner))
        {
            completeWindow.ShowDialog();
        }
    }

    /// <summary>
    /// Builds the complete in-memory binding model for the given install in one pass:
    ///   1. Load BMS - Full*.key catalogs (read-only structure and defaults)
    ///   2. Overlay saved keyboard JSON onto the catalog rows
    ///   3. Discover DirectInput devices and match stock XMLs
    ///   4. Load or build device binding profiles from device JSON (falling back to stock XML)
    ///
    /// Deliberately does NOT fire OnPropertyChanged. The caller owns notification timing
    /// so subscribers always see a complete model on the first notification.
    /// </summary>
    private void LoadFullBindingModelForInstall(BmsInstall install)
    {
        _needsDeviceJsonSync = false;
        _jsonReadFailureBlocksOutputSave = false;
        _hasShownJsonReadFailureStartupWarning = false;

        // Step 1+2: keyboard bindings
        KeyCatalogs = _keyCatalogService.LoadForInstall(install.BaseDir);
        CurrentBindingModel = _bindingModelBuilder.Build(KeyCatalogs);

        // FULL key files define the current structure/defaults.
        // JSON overlays saved keyboard state onto that current structure.
        // If FULL and JSON no longer match, remember that JSON needs a catalog sync
        // even if the user does not edit any binding this session.
        _needsKeyboardJsonCatalogSync = _jsonKeyboardBindingReader.Apply(install.BaseDir, CurrentBindingModel);

        if (_needsKeyboardJsonCatalogSync)
        {
            DebugDiagnosticsService.Info(
                "Keyboard JSON catalog sync required. FULL key catalog differences will be synced on close or launch.");
        }

        // Step 3+4: device bindings
        IReadOnlyList<StockDeviceSetupMatch> stockDeviceMatches =
            _deviceDiscovery.DiscoverAndMatchStockXml(install.BaseDir);

        CurrentBindingModel.DeviceProfiles.Clear();

        foreach (DeviceBindingProfile deviceProfile in _deviceJsonReader.LoadOrBuild(install.BaseDir, stockDeviceMatches))
            CurrentBindingModel.DeviceProfiles.Add(deviceProfile);

        _needsDeviceJsonSync = _deviceJsonReader.NeedsJsonSync;

        if (_needsDeviceJsonSync)
        {
            DebugDiagnosticsService.Info(
                "Device JSON sync required. Missing device JSON files/profiles will be synced on close or launch.");
        }

        if (_deviceJsonReader.HasReadFailuresBlockingSave)
        {
            CurrentBindingModel.HasJsonReadFailureBlockingSave = true;

            foreach (string message in _deviceJsonReader.ReadFailureMessages)
                CurrentBindingModel.JsonReadFailureMessages.Add(message);
        }

        _jsonReadFailureBlocksOutputSave = CurrentBindingModel.HasJsonReadFailureBlockingSave;

        if (_jsonReadFailureBlocksOutputSave)
        {
            DebugDiagnosticsService.Warn(
                $"Output saving disabled for this launcher run because one or more JSON files failed to read. Failures={CurrentBindingModel.JsonReadFailureMessages.Count}");

            foreach (string message in CurrentBindingModel.JsonReadFailureMessages)
                DebugDiagnosticsService.Warn($"JSON read failure blocking output save: {message}");
        }
    }

    private void LoadTheaterForSelectedInstall()
    {
        _isLoadingTheater = true;

        try
        {
            Theaters.Clear();
            SelectedTheater = null;

            if (SelectedInstall is null) return;

            var theaters = _theaterDiscovery.PopulateAndSave(SelectedInstall.BaseDir);
            foreach (var t in theaters)
                Theaters.Add(t);

            var cur = _registry.ReadString(SelectedInstall.RegistryKeyName, "curTheater");

            if (!string.IsNullOrWhiteSpace(cur))
            {
                if (!Theaters.Any(x => string.Equals(x, cur, StringComparison.Ordinal)))
                    Theaters.Insert(0, cur!);

                SelectedTheater = cur;
            }
            else
            {
                SelectedTheater = Theaters.Count > 0 ? Theaters[0] : null;
            }
        }
        finally
        {
            _isLoadingTheater = false;
        }
    }

    private void RefreshLauncherStrips()
    {
        FirstPartyItems.Clear();

        if (SelectedInstall is not null)
        {
            foreach (var item in _firstPartyStrip.GetItems(SelectedInstall))
                FirstPartyItems.Add(item);
        }

        ThirdPartyItems.Clear();

        foreach (var item in _thirdPartyStrip.LoadTools())
            ThirdPartyItems.Add(item);

        LaunchFirstPartyCommand.RaiseCanExecuteChanged();
    }

    private bool CanLaunchFirstParty(object? parameter) =>
        SelectedInstall is not null && parameter is string id && !string.IsNullOrWhiteSpace(id);

    // Set by MainWindowViewModel after construction, so SaveOutputsForClose
    // can check IsDirty and skip the write pipeline when nothing has changed.
    public ViewModels.ControlsViewModel? ControlsViewModel { get; set; }

    private bool IsOutputSaveBlockedByJsonReadFailure()
    {
        return _jsonReadFailureBlocksOutputSave ||
               CurrentBindingModel.HasJsonReadFailureBlockingSave;
    }

    private string GetJsonReadFailureSummary()
    {
        if (CurrentBindingModel.JsonReadFailureMessages.Count == 0)
            return "One or more JSON binding files failed to read.";

        return string.Join("\n", CurrentBindingModel.JsonReadFailureMessages);
    }

    private void ShowJsonReadFailureStartupWarningIfNeeded()
    {
        if (!IsOutputSaveBlockedByJsonReadFailure())
            return;

        if (_hasShownJsonReadFailureStartupWarning)
            return;

        _hasShownJsonReadFailureStartupWarning = true;

        string summary = GetJsonReadFailureSummary();

        DebugDiagnosticsService.Warn(
            $"Showing startup JSON binding file warning. {summary}");

        // Use the dispatcher so the warning appears after the main binding model
        // has finished loading and the selected install change has completed.
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                "One or more Launcher JSON binding files could not be read.\n\n" +
                "Saving and launching have been disabled for this session to avoid overwriting your bindings with incomplete fallback data.\n\n" +
                "Fix or restore the broken JSON file, then reopen the Launcher.\n\n" +
                summary,
                "JSON Binding File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            StatusText = "Fix the broken JSON binding file and reopen the Launcher.";
        }));
    }

    public void SaveOutputsForClose()
    {
        if (SelectedInstall is null)
            return;

        bool hasUserBindingChanges = ControlsViewModel?.IsDirty == true;
        bool needsKeyboardJsonCatalogSync = _needsKeyboardJsonCatalogSync;
        bool needsDeviceJsonSync = _needsDeviceJsonSync;

        if (IsOutputSaveBlockedByJsonReadFailure())
        {
            DebugDiagnosticsService.Warn(
                $"SaveOutputsForClose skipped because JSON read failed earlier in this launcher run. {GetJsonReadFailureSummary()}");

            return;
        }

        // Skip the full write pipeline only when there are no user binding edits
        // and no startup-discovered FULL-key catalog differences that need to be synced to JSON.
        // The individual writers also SHA1-diff before touching disk, but skipping the
        // entire pass avoids opening every file unnecessarily on close.
        if (!hasUserBindingChanges && !needsKeyboardJsonCatalogSync && !needsDeviceJsonSync)
        {
            DebugDiagnosticsService.Info("SaveOutputsForClose skipped: no binding changes and no JSON sync required.");
            return;
        }

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info($"Launcher close requested output save for install: {SelectedInstall.RegistryKeyName}");
            DebugDiagnosticsService.Info(
                $"PrepareForLaunch on close start. UserBindingChanges={hasUserBindingChanges} | KeyboardJsonCatalogSync={needsKeyboardJsonCatalogSync} | DeviceJsonSync={needsDeviceJsonSync}");

            bool vrEnabled = VrSteamVr || VrOpenXr;

            _launchPrep.PrepareForLaunch(
                SelectedInstall.BaseDir,
                SelectedInstall.RegistryKeyName,
                ExportRttTextures,
                vrEnabled,
                CurrentBindingModel);

            _needsKeyboardJsonCatalogSync = false;
            _needsDeviceJsonSync = false;
            ControlsViewModel?.ResetDirty();

            DebugDiagnosticsService.Info("PrepareForLaunch on close complete.");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "SaveOutputsForClose failed");
        }
    }

    private void LaunchSelected()
    {
        if (SelectedInstall is null) return;

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info($"Launch requested for install: {SelectedInstall.RegistryKeyName}");

            if (!_callsign.IsUniqueNameDefined(SelectedInstall.RegistryKeyName, SelectedInstall.BaseDir))
            {
                DebugDiagnosticsService.Warn("Unique pilot not defined. Opening CallsignWindow.");

                var window = new CallsignWindow(SelectedInstall.RegistryKeyName, SelectedInstall.BaseDir);

                if (Application.Current.MainWindow is Window owner && owner != window)
                    window.Owner = owner;

                using (FalconBMS.Launcher.MainWindow.BeginModalOverlay(window.Owner))
                {
                    window.ShowDialog();
                }

                if (!_callsign.IsUniqueNameDefined(SelectedInstall.RegistryKeyName, SelectedInstall.BaseDir))
                {
                    DebugDiagnosticsService.Warn("Launch canceled because valid pilot identity is still not defined.");
                    return;
                }
            }

            if (IsOutputSaveBlockedByJsonReadFailure())
            {
                string summary = GetJsonReadFailureSummary();

                DebugDiagnosticsService.Warn(
                    $"Launch canceled because JSON read failed earlier in this launcher run. {summary}");

                MessageBox.Show(
                    "One or more Launcher JSON binding files failed to read.\n\n" +
                    "The Launcher will not write JSON or generated BMS output files because that could overwrite valid bindings with partial fallback data.\n\n" +
                    "Fix or restore the broken JSON file, then reopen the Launcher.\n\n" +
                    summary,
                    "JSON Binding File Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                StatusText = "Launch canceled. Fix the broken JSON binding file and reopen the Launcher.";
                return;
            }

            StatusText = "Launching…";

            DebugDiagnosticsService.Info("PrepareForLaunch start.");

            bool vrEnabled = VrSteamVr || VrOpenXr;

            _launchPrep.PrepareForLaunch(
                SelectedInstall.BaseDir,
                SelectedInstall.RegistryKeyName,
                ExportRttTextures,
                vrEnabled,
                CurrentBindingModel);

            DebugDiagnosticsService.Info("PrepareForLaunch complete.");

            // All outputs are now current. Reset dirty/catalog-sync state so
            // SaveOutputsForClose can skip the redundant write if nothing changes
            // while BMS is running.
            _needsKeyboardJsonCatalogSync = false;
            _needsDeviceJsonSync = false;
            ControlsViewModel?.ResetDirty();

            var arguments = BuildFalconArguments();
            DebugDiagnosticsService.Info($"Falcon launch arguments: {arguments}");

            var p = _proc.StartFalcon(SelectedInstall.FalconExePath, arguments);
            DebugDiagnosticsService.Info($"Falcon process started. Id: {p.Id}");

            var mainWindow = Application.Current.MainWindow;
            if (mainWindow is not null)
            {
                mainWindow.WindowState = WindowState.Minimized;
                DebugDiagnosticsService.Info("Launcher minimized after Falcon process start.");
            }

            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                DebugDiagnosticsService.Info("Falcon process exited.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var w = Application.Current.MainWindow;
                    if (w is not null)
                    {
                        w.WindowState = WindowState.Normal;
                        w.Activate();
                    }

                    StatusText = "";
                });
            };
        }
        catch (Exception ex)
        {
            StatusText = "";
            DebugDiagnosticsService.Exception(ex, "LaunchSelected failed");
            MessageBox.Show(ex.Message, "Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildFalconArguments()
    {
        var args = new List<string>();

        if (LaunchAcmi)
            args.Add("-acmi");

        if (LaunchWindow)
            args.Add("-window");

        if (LaunchNoMovie)
            args.Add("-nomovie");

        if (LaunchEyeFly)
            args.Add("-ef");

        if (LaunchDebug)
            args.Add("-mono");

        if (VrSteamVr)
            args.Add("-vr");
        else if (VrOpenXr)
            args.Add("-xr");
        else
            args.Add("-novr");

        return string.Join(" ", args);
    }

    private void NormalizeVrState()
    {
        var selectedCount = 0;
        if (_vrNoVr) selectedCount++;
        if (_vrSteamVr) selectedCount++;
        if (_vrOpenXr) selectedCount++;

        if (selectedCount != 1)
        {
            _vrNoVr = true;
            _vrSteamVr = false;
            _vrOpenXr = false;
            SaveVrSettings();
        }
    }

    private void SaveVrSettings()
    {
        Properties.Settings.Default.VR_NoVR = _vrNoVr;
        Properties.Settings.Default.VR_SteamVR = _vrSteamVr;
        Properties.Settings.Default.VR_OpenXR = _vrOpenXr;
        Properties.Settings.Default.Save();
    }

    private void RunUpdaterSelected()
    {
        if (SelectedInstall is null) return;

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info("Updater launch requested.");

            // Save any pending Launcher output before the updater starts changing BMS files.
            SaveOutputsForClose();

            _proc.StartUpdater(SelectedInstall.BaseDir);

            DebugDiagnosticsService.Info(
                "Updater started successfully. Launcher will close so updated BMS files are loaded on next run.");

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "RunUpdaterSelected failed");
            MessageBox.Show(ex.Message, "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDocs()
    {
        if (SelectedInstall is null) return;

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info("Open Docs requested.");

            _folders.OpenFolder(Path.Combine(SelectedInstall.BaseDir, "Docs"));
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "OpenDocs failed");
            MessageBox.Show(ex.Message, "Open Docs Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenUser()
    {
        if (SelectedInstall is null) return;

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info("Open User requested.");

            _folders.OpenFolder(Path.Combine(SelectedInstall.BaseDir, "User"));
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "OpenUser failed");
            MessageBox.Show(ex.Message, "Open User Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenScreenshots()
    {
        if (SelectedInstall is null) return;

        try
        {
            DebugDiagnosticsService.InitializeForInstall(SelectedInstall.BaseDir);
            DebugDiagnosticsService.Info("Open Screenshots requested.");

            _folders.OpenFolder(
                Path.Combine(
                    SelectedInstall.BaseDir,
                    "User",
                    "Pictures"));
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "OpenScreenshots failed");
            MessageBox.Show(
                ex.Message,
                "Open Screenshots Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenForum()
    {
        try
        {
            DebugDiagnosticsService.Info("Open Falcon BMS forum requested.");
            _proc.OpenUrl("https://forum.falcon-bms.com/recent");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "OpenForum failed");
            MessageBox.Show(ex.Message, "Open Forum Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchFirstParty(object? parameter)
    {
        if (SelectedInstall is null) return;
        if (parameter is not string id || string.IsNullOrWhiteSpace(id)) return;

        try
        {
            if (string.Equals(id, "updater", StringComparison.OrdinalIgnoreCase))
            {
                RunUpdaterSelected();
                return;
            }

            var item = _firstPartyStrip.GetItem(SelectedInstall, id);
            if (item is null)
                return;

            var process = _proc.StartExecutable(item.ExePath, item.WorkingDirectory);

            if (item.MinimizeLauncherUntilExit)
                MinimizeWindowUntilProcessEnds(process);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tool Launch Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LaunchThirdParty(object? parameter)
    {
        if (IsEditingCommunityTools)
            return;

        if (parameter is not ThirdPartyToolItem item)
            return;

        if (item.IsBuiltInF4Wx)
        {
            LaunchBuiltInF4Wx(item);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ExecutablePath) ||
            !File.Exists(item.ExecutablePath))
        {
            MessageBox.Show(
                $"{item.DisplayName} could not be found.\n\n" +
                "Remove this application and add it again using the correct executable.",
                "Application Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            _proc.StartExecutable(
                item.ExecutablePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Tool Launch Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LaunchBuiltInF4Wx(
        ThirdPartyToolItem item)
    {
        try
        {
            string executablePath =
                Properties.Settings.Default.ThirdPartyF4WxExePath ?? "";

            if (!string.IsNullOrWhiteSpace(executablePath) &&
                !File.Exists(executablePath))
            {
                _thirdPartyStrip.ClearF4WxExecutablePath();

                executablePath = "";
                item.ExecutablePath = "";

                _thirdPartyStrip.SaveTools(
                    ThirdPartyItems,
                    out _);
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                var dialog =
                    new OpenFileDialog
                    {
                        Title = "Locate F4Wx",
                        Filter = "Executable files (*.exe)|*.exe",
                        CheckFileExists = true,
                        Multiselect = false,
                        FileName = "F4Wx.exe"
                    };

                if (dialog.ShowDialog() != true)
                {
                    _proc.OpenUrl(
                        ThirdPartyLauncherStripService.F4WxDownloadUrl);

                    return;
                }

                executablePath =
                    dialog.FileName;

                _thirdPartyStrip.SaveF4WxExecutablePath(
                    executablePath);

                item.ExecutablePath =
                    executablePath;

                _thirdPartyStrip.SaveTools(
                    ThirdPartyItems,
                    out _);
            }

            _proc.StartExecutable(
                executablePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Tool Launch Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddThirdPartyTool()
    {
        var dialog =
            new OpenFileDialog
            {
                Title = "Add Community Tool",
                Filter = "Executable files (*.exe)|*.exe",
                CheckFileExists = true,
                Multiselect = false
            };

        if (dialog.ShowDialog() != true)
            return;

        var newTool =
            _thirdPartyStrip.TryCreateTool(
                dialog.FileName,
                ThirdPartyItems,
                out string? createError);

        if (newTool is null)
        {
            MessageBox.Show(
                createError ??
                "The selected application could not be added.",
                "Add Community Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        ThirdPartyItems.Add(
            newTool);

        if (_thirdPartyStrip.SaveTools(
                ThirdPartyItems,
                out string? saveError))
        {
            DebugDiagnosticsService.Info(
                $"Community tool added | Name={newTool.DisplayName} | Path={newTool.ExecutablePath}");

            return;
        }

        ThirdPartyItems.Remove(
            newTool);

        _thirdPartyStrip.DeleteCachedIcon(
            newTool);

        MessageBox.Show(
            saveError ??
            "The Community Tools list could not be saved.",
            "Community Tools",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void RemoveThirdPartyTool(
        object? parameter)
    {
        if (parameter is not ThirdPartyToolItem item)
            return;

        int removedIndex =
            ThirdPartyItems.IndexOf(item);

        if (removedIndex < 0)
            return;

        ThirdPartyItems.RemoveAt(
            removedIndex);

        if (_thirdPartyStrip.SaveTools(
                ThirdPartyItems,
                out string? saveError))
        {
            DebugDiagnosticsService.Info(
                $"Community tool removed | Name={item.DisplayName} | Path={item.ExecutablePath} | PreviousIndex={removedIndex}");

            _thirdPartyStrip.DeleteCachedIcon(
                item);

            return;
        }

        ThirdPartyItems.Insert(
            removedIndex,
            item);

        MessageBox.Show(
            saveError ??
            "The Community Tools list could not be saved.",
            "Community Tools",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Reorders the visible collection in memory while dragging.
    /// </summary>
    public void MoveThirdPartyTool(
        int oldIndex,
        int newIndex)
    {
        if (oldIndex < 0 ||
            oldIndex >= ThirdPartyItems.Count ||
            newIndex < 0 ||
            newIndex >= ThirdPartyItems.Count ||
            oldIndex == newIndex)
        {
            return;
        }

        ThirdPartyItems.Move(
            oldIndex,
            newIndex);
    }

    /// <summary>
    /// Saves once when the completed drag releases the mouse.
    /// </summary>
    public void CompleteThirdPartyToolReorder(
        IReadOnlyList<ThirdPartyToolItem> originalOrder)
    {
        if (_thirdPartyStrip.SaveTools(
                ThirdPartyItems,
                out string? saveError))
        {
            string previousOrder =
                string.Join(
                    " > ",
                    originalOrder.Select(
                        item => item.DisplayName));

            string newOrder =
                string.Join(
                    " > ",
                    ThirdPartyItems.Select(
                        item => item.DisplayName));

            DebugDiagnosticsService.Info(
                $"Community tools reordered | Previous={previousOrder} | New={newOrder}");

            return;
        }

        ThirdPartyItems.Clear();

        foreach (ThirdPartyToolItem item in originalOrder)
            ThirdPartyItems.Add(item);

        MessageBox.Show(
            saveError ??
            "The new Community Tools order could not be saved.",
            "Community Tools",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void MinimizeWindowUntilProcessEnds(Process process)
    {
        var window = Application.Current.MainWindow;
        if (window is not null)
            window.WindowState = WindowState.Minimized;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var w = Application.Current.MainWindow;
                if (w is not null)
                {
                    w.WindowState = WindowState.Normal;
                    w.Activate();
                }
            });
        };
    }

    private async Task LoadNewsAsync()
    {
        try
        {
            DebugDiagnosticsService.Info("RSS fetch starting.");
            NewsStatusText = "Loading news…";

            // Main dashboard only shows the latest three RSS posts.
            var items = await _rss.FetchAsync(maxItems: 3, CancellationToken.None);

            NewsItems.Clear();
            foreach (var i in items)
                NewsItems.Add(new RssItemViewModel(i));

            DebugDiagnosticsService.Info("Completed RSS fetch on background-thread.");

            NewsStatusText = NewsItems.Count == 0 ? "News unavailable." : "";
            DebugDiagnosticsService.Info("RSS update finished.");
            OnPropertyChanged(nameof(IsNewsStatusVisible));
        }
        catch (Exception ex)
        {
            NewsItems.Clear();
            NewsStatusText = "News unavailable.";
            DebugDiagnosticsService.Exception(ex, "RSS fetch failed");
            OnPropertyChanged(nameof(IsNewsStatusVisible));
        }
    }

    public string SelectedInstallHeaderVersion
    {
        get
        {
            var s = SelectedInstall?.DisplayName ?? "";
            if (string.IsNullOrWhiteSpace(s)) return "";
            return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
        }
    }

    public bool SelectedInstallIsInternal
    {
        get
        {
            var s = SelectedInstall?.DisplayName ?? "";
            return s.IndexOf("(Internal", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public string LauncherVersion =>
        $"Falcon BMS Launcher v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}";

    private void RaiseCommandStates()
    {
        LaunchCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
        OpenDocsCommand.RaiseCanExecuteChanged();
        OpenUserCommand.RaiseCanExecuteChanged();
        OpenScreenshotsCommand.RaiseCanExecuteChanged();
        LaunchFirstPartyCommand.RaiseCanExecuteChanged();
    }
}