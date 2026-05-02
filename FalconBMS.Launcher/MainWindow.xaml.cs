using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace FalconBMS.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();

        Closing += MainWindow_Closing;

        DebugDiagnosticsService.Info("MainWindow constructed.");
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.SaveOutputsForClose();
    }
}