using FalconBMS.Launcher.Models;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class LegacyImportChoiceWindow : Window
{
    public LegacyImportChoice? Choice { get; private set; }

    public LegacyImportChoiceWindow()
    {
        InitializeComponent();
    }

    private void ImportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Choice = LegacyImportChoice.Import;
        DialogResult = true;
    }

    private void StartFreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Choice = LegacyImportChoice.StartFresh;
        DialogResult = true;
    }
}