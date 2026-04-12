using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace FalconBMS.Launcher.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        // IMPORTANT:
        // Do NOT set DataContext here.
        // The MainWindow ContentControl + DataTemplate provides the shared MainViewModel instance.
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}