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
    private DevicesViewModel? _subscribedViewModel;

    public DevicesView()
    {
        InitializeComponent();

        _deviceButtonPolling.ButtonStateChanged += DeviceButtonPolling_ButtonStateChanged;

        Loaded += DevicesView_Loaded;
        Unloaded += DevicesView_Unloaded;
        DataContextChanged += DevicesView_DataContextChanged;
    }

    private void DevicesView_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeToViewModel(DataContext as DevicesViewModel);
        StartLiveDevicePolling();
    }

    private void DevicesView_Unloaded(object sender, RoutedEventArgs e)
    {
        StopLiveDevicePolling();
        SubscribeToViewModel(null);
    }

    private void DevicesView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeToViewModel(e.NewValue as DevicesViewModel);
    }

    private void SubscribeToViewModel(DevicesViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
            return;

        if (_subscribedViewModel is not null)
            _subscribedViewModel.KeyMappingRequested -= DevicesViewModel_KeyMappingRequested;

        _subscribedViewModel = viewModel;

        if (_subscribedViewModel is not null)
            _subscribedViewModel.KeyMappingRequested += DevicesViewModel_KeyMappingRequested;
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

    private void DevicesViewModel_KeyMappingRequested(object? sender, DevicesKeyMappingRequestedEventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel)
            return;

        ControlsViewModel? controlsViewModel = viewModel.ControlsViewModel;
        if (controlsViewModel?.SelectedProfile is null)
            return;

        StopLiveDevicePolling();

        var window = new KeyMappingWindow
        {
            Owner = Window.GetWindow(this)
        };

        window.DataContext = new KeyMappingWindowViewModel(
            e.Row,
            controlsViewModel.SelectedProfileRows,
            controlsViewModel.DeviceColumns,
            controlsViewModel.SelectedProfile.AircraftProfile,
            controlsViewModel.ApplyKeyboardMappingFromPopup,
            controlsViewModel.ApplyDeviceButtonMappingFromPopup,
            () => window.Close());

        window.ShowDialog();

        // KeyMappingWindow saves through ControlsViewModel.
        // Refresh this panel from the same in-memory model after the popup closes.
        viewModel.RefreshActiveMappedControlDetails();

        StartLiveDevicePolling();
    }
}