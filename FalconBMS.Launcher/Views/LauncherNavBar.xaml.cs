using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FalconBMS.Launcher.Views;

public partial class LauncherNavBar : UserControl
{
    public LauncherNavBar()
    {
        InitializeComponent();

#if !DEBUG
    // Keep LauncherTab.Styles available so Release builds compile,
    // but hide the developer-only Styles tab from tester/public builds.
    StylesButton.Visibility = Visibility.Collapsed;
#endif
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void Main_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Main);
    private void Views_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Views);
    private void Controls_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Controls);
    private void Styles_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Styles);
}