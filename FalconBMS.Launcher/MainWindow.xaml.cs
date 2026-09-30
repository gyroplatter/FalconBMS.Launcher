using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    private readonly DispatcherTimer _deviceChangeDebounceTimer;

    private HwndSource? _windowSource;

    private bool _deviceChangePending;
    private volatile bool _isClosing;

    private int _modalOverlayDepth;

    // Each refresh waits for its predecessor.
    // Only the newest discovery result is applied to persistent hardware.
    private readonly object _refreshSync = new();

    private Task _refreshTask = Task.CompletedTask;

    private int _refreshVersion;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        ((MainWindowViewModel)DataContext)
            .Main.PropertyChanged +=
                MainViewModel_PropertyChanged;

        _deviceChangeDebounceTimer =
            new DispatcherTimer(
                DispatcherPriority.Background)
            {
                Interval =
                    TimeSpan.FromMilliseconds(500)
            };

        _deviceChangeDebounceTimer.Tick +=
            DeviceChangeDebounceTimer_Tick;

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        ThemeService.EffectiveDarkThemeChanged += ThemeService_EffectiveDarkThemeChanged;

        #if DEBUG
                PreviewKeyDown += MainWindow_DebugPreviewKeyDown;
        #endif

        DebugDiagnosticsService.Info("MainWindow constructed.");
    }

    public static IDisposable BeginModalOverlay(Window? ownerWindow)
    {
        if (ownerWindow is MainWindow mainWindow)
        {
            mainWindow.ShowModalOverlay();
            return new ModalOverlayScope(mainWindow);
        }

        return EmptyDisposable.Instance;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ApplyNativeTitleBarTheme(
            ThemeService.IsCurrentEffectiveThemeDark());

        IntPtr hwnd =
            new WindowInteropHelper(this).Handle;

        _windowSource =
            HwndSource.FromHwnd(hwnd);

        _windowSource?.AddHook(
            MainWindow_WndProc);

        // Only the main window initializes persistent hardware
        PersistentDirectInputManager.Initialize(hwnd);

        QueuePersistentDeviceRefresh();
    }

    private void MainViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
            nameof(MainViewModel.CurrentBindingModel))
        {
            QueuePersistentDeviceRefresh();
        }
    }

    private void QueuePersistentDeviceRefresh()
    {
        if (_isClosing ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // Model notifications can occur before WPF creates
        // the main window's native handle
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
            return;

        Guid[] connectedDevices =
            viewModel.Main.CurrentBindingModel.DeviceProfiles
                .Where(device =>
                    device.IsConnected &&
                    device.InstanceGuid != Guid.Empty)
                .Select(device => device.InstanceGuid)
                .Distinct()
                .ToArray();

        lock (_refreshSync)
        {
            if (_isClosing)
                return;

            int version =
                ++_refreshVersion;

            Task previous =
                _refreshTask;

            _refreshTask =
                SynchronizePersistentDevicesAsync(
                    previous,
                    version,
                    connectedDevices);
        }
    }

    private async Task SynchronizePersistentDevicesAsync(
        Task previous,
        int version,
        Guid[] connectedDevices)
    {
        // Serialize refreshes without blocking the UI thread
        await previous.ConfigureAwait(false);

        if (_isClosing ||
            version != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                if (!_isClosing &&
                    version == Volatile.Read(ref _refreshVersion))
                {
                    PersistentDirectInputManager.Current
                        .Synchronize(connectedDevices);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_isClosing)
            {
                DebugDiagnosticsService.Exception(
                    ex,
                    "Persistent DirectInput synchronization failed.");
            }
        }
    }

    private IntPtr MainWindow_WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (_isClosing ||
            msg != WM_DEVICECHANGE ||
            wParam.ToInt32() != DBT_DEVNODES_CHANGED)
        {
            return IntPtr.Zero;
        }

        _deviceChangePending = true;

        // Assignment windows intentionally keep their original device/capture
        // snapshot. Do not replace the BindingModel underneath one.
        // The pending hardware refresh runs immediately after the modal closes.
        if (_modalOverlayDepth > 0)
        {
            _deviceChangeDebounceTimer.Stop();

            DebugDiagnosticsService.Info(
                "WM_DEVICECHANGE detected while a modal Launcher window is open. Device refresh deferred until the window closes.");

            return IntPtr.Zero;
        }

        ScheduleDeviceChangeRefresh();

        return IntPtr.Zero;
    }

    private void ScheduleDeviceChangeRefresh()
    {
        if (_isClosing)
            return;

        _deviceChangeDebounceTimer.Stop();
        _deviceChangeDebounceTimer.Start();
    }

    private void DeviceChangeDebounceTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _deviceChangeDebounceTimer.Stop();

        if (_isClosing ||
            !_deviceChangePending)
        {
            return;
        }

        // A modal may have opened during the debounce interval
        if (_modalOverlayDepth > 0)
            return;

        _deviceChangePending = false;

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Main.RefreshBindingModelForDeviceChange();
        }
    }

    private void ThemeService_EffectiveDarkThemeChanged(bool isDarkTheme)
    {
        ApplyNativeTitleBarTheme(isDarkTheme);
    }

    private void ShowModalOverlay()
    {
        _modalOverlayDepth++;

        ModalOverlay.Visibility =
            Visibility.Visible;
    }

    private void HideModalOverlay()
    {
        if (_modalOverlayDepth > 0)
            _modalOverlayDepth--;

        if (_modalOverlayDepth == 0)
        {
            ModalOverlay.Visibility =
                Visibility.Collapsed;

            if (_deviceChangePending &&
                !_isClosing)
            {
                DebugDiagnosticsService.Info(
                    "Processing deferred WM_DEVICECHANGE after modal Launcher window closed.");

                ScheduleDeviceChangeRefresh();
            }
        }
    }

    private void ApplyNativeTitleBarTheme(bool useDarkTitleBar)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var value = useDarkTitleBar ? 1 : 0;

        var result = DwmSetWindowAttribute(
            hwnd,
            DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref value,
            sizeof(int));

        if (result != 0)
        {
            DebugDiagnosticsService.Warn($"Unable to apply native title bar theme. DwmSetWindowAttribute result={result}");
        }
    }

#if DEBUG
    private void MainWindow_DebugPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Debug shortcuts are intentionally active only while the Main tab is selected.
        if (viewModel.CurrentTab != Models.LauncherTab.Main)
            return;

        // A toggles directly between the effective Light and Dark themes.
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.None)
        {
            var newThemeMode = ThemeService.IsCurrentEffectiveThemeDark()
                ? Models.LauncherThemeModes.Light
                : Models.LauncherThemeModes.Dark;

            // Use the MainViewModel properties so its theme radio-button state stays synchronized.
            if (newThemeMode == Models.LauncherThemeModes.Light)
                viewModel.Main.LauncherThemeLight = true;
            else
                viewModel.Main.LauncherThemeDark = true;

            e.Handled = true;
            return;
        }

        // Ctrl+W closes the Launcher through the normal window Closing pipeline.
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Close();
        }
    }
#endif

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        _deviceChangePending = false;

        _deviceChangeDebounceTimer.Stop();

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.SaveOutputsForClose();
    }

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Main.PropertyChanged -=
                MainViewModel_PropertyChanged;
        }

        Task pendingRefresh;

        lock (_refreshSync)
        {
            _isClosing = true;

            ++_refreshVersion;

            pendingRefresh = _refreshTask;
        }

        // Refresh continuations explicitly avoid the WPF
        // synchronization context, so waiting here cannot
        // deadlock against the UI dispatcher.
        try
        {
            pendingRefresh.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "Persistent DirectInput shutdown synchronization failed.");
        }

        PersistentDirectInputManager.Shutdown();

        _deviceChangeDebounceTimer.Tick -=
            DeviceChangeDebounceTimer_Tick;

        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(
                MainWindow_WndProc);

            _windowSource = null;
        }

        ThemeService.EffectiveDarkThemeChanged -=
            ThemeService_EffectiveDarkThemeChanged;

        #if DEBUG
                PreviewKeyDown -= MainWindow_DebugPreviewKeyDown;
        #endif
    }

    private sealed class ModalOverlayScope : IDisposable
    {
        private MainWindow? _mainWindow;

        public ModalOverlayScope(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void Dispose()
        {
            if (_mainWindow is null)
                return;

            _mainWindow.HideModalOverlay();
            _mainWindow = null;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}