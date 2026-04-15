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
        StylesButton.Visibility = Visibility.Collapsed;
#endif
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void Main_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Main);
    private void Controls_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Controls);
    private void Audio_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Audio);
    private void Views_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Views);
    private void Keymapping_Click(object sender, RoutedEventArgs e) => Vm?.SetTab(LauncherTab.Keymapping);

    private void Styles_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        Vm?.SetTab(LauncherTab.Styles);
#endif
    }
}