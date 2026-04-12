using FalconBMS.Launcher.ViewModels;
using System;
using System.Windows;
using System.Windows.Interop;

namespace FalconBMS.Launcher.Views;

public partial class KeyMappingWindow : Window
{
    public KeyMappingWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is KeyMappingWindowViewModel vm)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                vm.StartCapture(hwnd);
            }
        };

        Closed += (_, _) =>
        {
            if (DataContext is KeyMappingWindowViewModel vm)
            {
                vm.StopCapture();
                if (vm is IDisposable d)
                    d.Dispose();
            }
        };
    }
}