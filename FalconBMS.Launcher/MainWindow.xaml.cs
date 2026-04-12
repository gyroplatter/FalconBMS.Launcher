using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using FalconBMS.Launcher.Views;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    private const int WM_DEVICECHANGED = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    private readonly DispatcherTimer _restoreRefreshTimer;
    private readonly DispatcherTimer _deviceRefreshTimer;
    private bool _wasMinimized;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        DebugDiagnosticsService.Info("MainWindow constructed.");

        _restoreRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        _restoreRefreshTimer.Tick += RestoreRefreshTimer_Tick;

        _deviceRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _deviceRefreshTimer.Tick += DeviceRefreshTimer_Tick;

        StateChanged += MainWindow_StateChanged;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);

        DebugDiagnosticsService.Info("Post_OnInitialized.");
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            _wasMinimized = true;
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (!_wasMinimized)
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("RESTORE");
        DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=MainWindow.ActivatedAfterMinimize | Scope=DeferredHotplugCheck");

        _restoreRefreshTimer.Stop();
        _restoreRefreshTimer.Start();
    }

    private void RestoreRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _restoreRefreshTimer.Stop();
        _wasMinimized = false;

        if (DataContext is not MainWindowViewModel vm)
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("RESTORE");
        DebugDiagnosticsService.Info($"REFRESH BEGIN | ActionId={actionId} | Method=RestoreRefreshTimer_Tick");

        if (vm.RefreshDeviceStateIfNeeded())
        {
            DebugDiagnosticsService.Info($"REFRESH ESCALATED | ActionId={actionId} | Reason=HotplugChanged | Action=RefreshActiveTabAfterDeviceHotplug");
            RefreshActiveTabAfterDeviceHotplug();
        }
        else
        {
            DebugDiagnosticsService.Info($"REFRESH NO-OP | ActionId={actionId} | Reason=HotplugUnchanged");
        }
    }

    private void DeviceRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _deviceRefreshTimer.Stop();

        if (DataContext is not MainWindowViewModel vm)
            return;

        string actionId = DebugDiagnosticsService.CreateActionId("WMDEV");
        DebugDiagnosticsService.Info($"REFRESH BEGIN | ActionId={actionId} | Method=DeviceRefreshTimer_Tick | Reason=WM_DEVICECHANGE delayed check");

        if (vm.RefreshDeviceStateIfNeeded())
        {
            DebugDiagnosticsService.Info($"REFRESH ESCALATED | ActionId={actionId} | Reason=HotplugChanged | Action=RefreshActiveTabAfterDeviceHotplug");
            RefreshActiveTabAfterDeviceHotplug();
        }
        else
        {
            DebugDiagnosticsService.Info($"REFRESH NO-OP | ActionId={actionId} | Reason=HotplugUnchanged");
        }
    }

    private void RefreshActiveTabAfterDeviceHotplug()
    {
        if (FindDescendant<KeymappingView>(this) is KeymappingView keymappingView)
        {
            DebugDiagnosticsService.Info("HOTPLUG TAB REFRESH | Target=KeymappingView");
            keymappingView.RefreshAfterDeviceHotplug();
            return;
        }

        if (FindDescendant<ControlsView>(this) is ControlsView controlsView)
        {
            DebugDiagnosticsService.Info("HOTPLUG TAB REFRESH | Target=ControlsView");
            controlsView.RefreshAfterDeviceHotplug();
            return;
        }

        if (FindDescendant<AudioView>(this) is AudioView audioView)
        {
            DebugDiagnosticsService.Info("HOTPLUG TAB REFRESH | Target=AudioView");
            audioView.RefreshAfterDeviceHotplug();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGED && wParam.ToInt32() == DBT_DEVNODES_CHANGED)
        {
            string actionId = DebugDiagnosticsService.CreateActionId("WMDEV");
            DebugDiagnosticsService.Info($"WM_DEVICECHANGE | ActionId={actionId} | wParam={wParam} | lParam={lParam} | SchedulingDelayedRefresh=true");
            _deviceRefreshTimer.Stop();
            _deviceRefreshTimer.Start();
        }

        return IntPtr.Zero;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _restoreRefreshTimer.Stop();
        _deviceRefreshTimer.Stop();

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        DebugDiagnosticsService.Info("MainWindow closed.");
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            T? nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
