using FalconBMS.Launcher.ViewModels;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class AxisAssignWindow : Window
{
    public AxisAssignWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is AxisAssignViewModel vm)
                vm.Start();
        };

        Closed += (_, _) =>
        {
            if (DataContext is AxisAssignViewModel vm)
                vm.Dispose();
        };
    }
}
