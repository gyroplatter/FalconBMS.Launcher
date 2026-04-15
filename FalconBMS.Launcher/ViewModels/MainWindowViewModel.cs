using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using System.ComponentModel;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Top-level shell view model that manages tab switching and device refresh coordination across pages.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly DeviceHotplugSnapshotService _hotplugSnapshot = new();
    private readonly AxisBindingsSnapshotService _axisSnapshot = new();
    private DeviceHotplugSnapshotService.Snapshot? _lastDeviceSnapshot;

    public MainViewModel Main { get; } = new();
    public ControlsViewModel Controls { get; }
    public AudioViewModel Audio { get; }
    public ViewsViewModel Views { get; }
    public KeymappingViewModel Keymapping { get; }
#if DEBUG
    public StylesViewModel Styles { get; } = new();
#endif

    private LauncherTab _currentTab = LauncherTab.Main;
    public LauncherTab CurrentTab
    {
        get => _currentTab;
        set
        {
            if (!Set(ref _currentTab, value)) return;
            OnPropertyChanged(nameof(CurrentViewModel));
        }
    }

    public object CurrentViewModel =>
        CurrentTab switch
        {
            LauncherTab.Controls => Controls,
            LauncherTab.Audio => Audio,
            LauncherTab.Views => Views,
            LauncherTab.Keymapping => Keymapping,
#if DEBUG
            LauncherTab.Styles => Styles,
#endif
            _ => Main
        };

    public RelayCommand SetTabCommand { get; }

    public MainWindowViewModel()
    {
        ControlsViewModel controls = null!;
        AudioViewModel audio = null!;
        ViewsViewModel views = null!;
        KeymappingViewModel keymapping = null!;

        controls = new ControlsViewModel(() => Main.SelectedInstall);
        audio = new AudioViewModel(() => Main.SelectedInstall);
        views = new ViewsViewModel();
        keymapping = new KeymappingViewModel(() => Main.SelectedInstall);

        Controls = controls;
        Audio = audio;
        Views = views;
        Keymapping = keymapping;

        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedInstall))
            {
                string actionId = DebugDiagnosticsService.CreateActionId("SEL");
                DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=MainWindowViewModel.SelectedInstallChanged | Scope=FullDeviceState");

                RefreshDeviceState();
                ResetDeviceSnapshot();
            }
        };

        SetTabCommand = new RelayCommand(() => { }, () => true);

        if (Main.SelectedInstall is null)
        {
            DebugDiagnosticsService.Info("MainWindowViewModel starting with no selected install.");
            Controls.RefreshFromDisk();
            Audio.RefreshFromDisk();
            Keymapping.RefreshFromDisk();
            ResetDeviceSnapshot();
        }
        else
        {
            DebugDiagnosticsService.Info($"MainWindowViewModel starting with selected install: {Main.SelectedInstall.RegistryKeyName}");
            RefreshDeviceState();
            ResetDeviceSnapshot();
        }
    }

    public void SetTab(LauncherTab tab) => CurrentTab = tab;

    public void RefreshDeviceState()
    {
        string actionId = DebugDiagnosticsService.CreateActionId("RDEV");

        var install = Main.SelectedInstall;
        if (install is null)
        {
            DebugDiagnosticsService.Info($"REFRESH BEGIN | ActionId={actionId} | Method=RefreshDeviceState | Install=<null> | Mode=RefreshFromDisk");
            Controls.RefreshFromDisk();
            Audio.RefreshFromDisk();
            Keymapping.RefreshFromDisk();
            DebugDiagnosticsService.Info($"REFRESH END | ActionId={actionId} | Method=RefreshDeviceState | Install=<null>");
            return;
        }

        DebugDiagnosticsService.Info($"REFRESH BEGIN | ActionId={actionId} | Method=RefreshDeviceState | Install={install.RegistryKeyName}");

        Controls.PrepareAxisConfigForRefresh(install.BaseDir);

        DebugDiagnosticsService.Info($"SNAPSHOT BUILD | ActionId={actionId} | Source=MainWindowViewModel.RefreshDeviceState");
        var snapshot = _axisSnapshot.Build(
            install.BaseDir,
            AxisCatalog.All.Select(d => d.Function));

        DebugDiagnosticsService.Info($"REFRESH APPLY | ActionId={actionId} | Target=Controls.RefreshFromSnapshot");
        Controls.RefreshFromSnapshot(snapshot);

        DebugDiagnosticsService.Info($"REFRESH APPLY | ActionId={actionId} | Target=Audio.RefreshFromSnapshot");
        Audio.RefreshFromSnapshot(snapshot);

        DebugDiagnosticsService.Info($"REFRESH APPLY | ActionId={actionId} | Target=Keymapping.RefreshFromDisk");
        Keymapping.RefreshFromDisk();

        DebugDiagnosticsService.Info($"REFRESH END | ActionId={actionId} | Method=RefreshDeviceState | Install={install.RegistryKeyName}");
    }

    public bool RefreshDeviceStateIfNeeded()
    {
        string actionId = DebugDiagnosticsService.CreateActionId("HOT");
        DebugDiagnosticsService.Info($"HOTPLUG CHECK | ActionId={actionId} | Source=MainWindowViewModel.RefreshDeviceStateIfNeeded | PreviousSnapshotPresent={_lastDeviceSnapshot is not null}");

        var current = _hotplugSnapshot.Capture();
        bool changed = _hotplugSnapshot.HasChanged(_lastDeviceSnapshot, current);

        DebugDiagnosticsService.Info($"HOTPLUG RESULT | ActionId={actionId} | Changed={changed}");

        if (!changed)
            return false;

        RefreshDeviceState();
        _lastDeviceSnapshot = current;
        return true;
    }

    public void ResetDeviceSnapshot()
    {
        _lastDeviceSnapshot = _hotplugSnapshot.Capture();
        DebugDiagnosticsService.Info($"HOTPLUG SNAPSHOT RESET | SnapshotPresent={_lastDeviceSnapshot is not null}");
    }
}
