using FalconBMS.Launcher.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class ControlsView : UserControl
{
    private Window? _hostWindow;

    public ControlsView()
    {
        InitializeComponent();
        IsVisibleChanged += ControlsView_IsVisibleChanged;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ControlsViewModel vm)
            return;

        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is null)
            return;

        _hostWindow.Activated += HostWindow_Activated;
        _hostWindow.Deactivated += HostWindow_Deactivated;

        IntPtr hwnd = new WindowInteropHelper(_hostWindow).Handle;
        vm.StartAxisBarLive(hwnd);
        UpdatePollingState();
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow.Deactivated -= HostWindow_Deactivated;
            _hostWindow = null;
        }

        if (DataContext is ControlsViewModel vm)
            vm.StopAxisBarLive();
    }

    private void ControlsView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdatePollingState();
    }

    private void HostWindow_Activated(object? sender, EventArgs e)
    {
        UpdatePollingState();
    }

    private void HostWindow_Deactivated(object? sender, EventArgs e)
    {
        UpdatePollingState();
    }

    private void UpdatePollingState()
    {
        if (DataContext is not ControlsViewModel vm)
            return;

        bool isActive = IsVisible && _hostWindow is not null && _hostWindow.IsActive;
        vm.SetPollingActive(isActive);
    }

    public void RefreshAfterDeviceHotplug()
    {
        if (DataContext is not ControlsViewModel vm)
            return;

        var win = Window.GetWindow(this);
        if (win is null)
            return;

        IntPtr hwnd = new WindowInteropHelper(win).Handle;
        vm.StartAxisBarLive(hwnd);
        UpdatePollingState();
    }
}