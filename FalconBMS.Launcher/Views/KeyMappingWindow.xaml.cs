using FalconBMS.Launcher.ViewModels;
using System.Windows;

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
                vm.StartCapture();
            }
        };

        Closed += (_, _) =>
        {
            if (DataContext is KeyMappingWindowViewModel vm)
            {
                vm.StopCapture();
                vm.Dispose();
            }
        };
    }
}