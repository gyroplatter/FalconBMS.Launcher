using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System.Windows;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        DebugDiagnosticsService.Info("MainWindow constructed.");
    }
}