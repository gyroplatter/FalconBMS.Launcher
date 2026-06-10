using FalconBMS.Launcher.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FalconBMS.Launcher.Views;

public partial class LegacyImportReviewWindow : Window
{
    public bool BackRequested { get; private set; }

    public LegacyImportReviewWindow(
        LegacyImportScanResult scanResult)
    {
        InitializeComponent();

        KeyboardFilesList.ItemsSource =
            BuildKeyboardFiles(scanResult);

        DevicesList.ItemsSource =
            scanResult.Devices;

        List<string> warnings =
            BuildWarnings(scanResult);

        if (warnings.Count > 0)
        {
            WarningsPanel.Visibility =
                Visibility.Visible;

            WarningsList.ItemsSource =
                warnings;
        }
    }

    private static List<LegacyImportKeyboardFileDisplay>
        BuildKeyboardFiles(
            LegacyImportScanResult scanResult)
    {
        var keyboardFiles =
            new List<LegacyImportKeyboardFileDisplay>();

        if (scanResult.HasF16Controls)
        {
            keyboardFiles.Add(
                new LegacyImportKeyboardFileDisplay
                {
                    AircraftName = "F-16",
                    StatusText = "Existing key file found"
                });
        }

        if (scanResult.HasF15Controls)
        {
            keyboardFiles.Add(
                new LegacyImportKeyboardFileDisplay
                {
                    AircraftName = "F-15",
                    StatusText = "Existing key file found"
                });
        }

        return keyboardFiles;
    }

    private static List<string> BuildWarnings(
        LegacyImportScanResult scanResult)
    {
        var warnings =
            new List<string>();

        foreach (LegacyImportDeviceScanResult device in
                 scanResult.Devices.Where(device =>
                     device.WillUseStockFallback))
        {
            warnings.Add(
                $"{device.DeviceName}: the existing device file could not be read. " +
                "The stock profile will be used instead.");
        }

        foreach (LegacyImportDeviceScanResult device in
                 scanResult.Devices.Where(device =>
                     device.CannotImport))
        {
            warnings.Add(
                $"{device.DeviceName}: the existing device file could not be read " +
                "and no stock profile was found. This device will be skipped.");
        }

        warnings.AddRange(
            scanResult.Warnings);

        return warnings;
    }

    private void ContinueButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void BackButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        BackRequested = true;
        DialogResult = false;
    }
}

public sealed class LegacyImportKeyboardFileDisplay
{
    public string AircraftName { get; init; } = "";

    public string StatusText { get; init; } = "";
}