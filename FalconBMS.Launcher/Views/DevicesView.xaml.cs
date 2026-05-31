using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FalconBMS.Launcher.Views;

public partial class DevicesView : UserControl
{
    private readonly LiveDeviceButtonPollingService _deviceButtonPolling = new();
    private DispatcherTimer? _timer;

    public DevicesView()
    {
        InitializeComponent();

        _deviceButtonPolling.ButtonStateChanged += DeviceButtonPolling_ButtonStateChanged;

        Loaded += DevicesView_Loaded;
        Unloaded += DevicesView_Unloaded;
    }

    private void DevicesView_Loaded(object sender, RoutedEventArgs e)
    {
        StartLiveDevicePolling();
    }

    private void DevicesView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopLiveDevicePolling();
    }

    private void StartLiveDevicePolling()
    {
        StopLiveDevicePolling();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void StopLiveDevicePolling()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        _deviceButtonPolling.Reset();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel)
            return;

        if (viewModel.SelectedDevice?.DeviceProfile is not DeviceBindingProfile selectedDevice)
            return;

        Window? window = Window.GetWindow(this);
        if (window is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // The Devices visual should react only to the selected device.
        // Presses on other connected controllers should not change the visible callout.
        _deviceButtonPolling.Poll(new[] { selectedDevice }, hwnd);
    }

    private void DeviceButtonPolling_ButtonStateChanged(object? sender, LiveDeviceButtonStateChangedEventArgs e)
    {
        if (!e.IsPressed)
            return;

        if (DataContext is not DevicesViewModel viewModel)
            return;

        viewModel.TryShowVisualCalloutForButton(e.DurableDeviceKey, e.ButtonIndex);
    }
}