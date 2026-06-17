using FalconBMS.Launcher.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FalconBMS.Launcher.Services;

public sealed class BindingJsonImportExportService
{
    private const string BindingJsonFilter = "Launcher binding JSON (*.json)|*.json";

    private readonly JsonKeyboardBindingWriterService _keyboardJsonWriter = new();
    private readonly DeviceJsonWriterService _deviceJsonWriter = new();

    public bool Import(
        string baseDir,
        BindingModel bindingModel,
        Window? owner)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Import Controls",
            Filter = BindingJsonFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (openDialog.ShowDialog(owner) != true)
            return false;

        try
        {
            ImportCandidate candidate = ReadImportCandidate(openDialog.FileName);

            return candidate.BindingType switch
            {
                "hotas" => ImportDeviceJson(baseDir, bindingModel, openDialog.FileName, candidate, owner),
                "keyboard" => ImportKeyboardJson(baseDir, bindingModel, openDialog.FileName, candidate, owner),
                _ => ShowUnsupportedImport(candidate.BindingType, owner)
            };
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Binding JSON import failed: {openDialog.FileName}");

            MessageBox.Show(
                owner,
                "The selected file could not be imported.\n\n" + ex.Message,
                "Import Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return false;
        }
    }

    public void Export(
        string baseDir,
        BindingModel bindingModel,
        Window? owner)
    {
        try
        {
            List<ExportCandidate> candidates =
                BuildExportCandidates(bindingModel);

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "No bindings were found to export.",
                    "Export Controls",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ExportCandidate? selected =
                SelectExportCandidate(
                    candidates,
                    owner);

            if (selected is null)
                return;

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Controls",
                Filter = BindingJsonFilter,
                FileName = selected.FileName,
                OverwritePrompt = true
            };

            if (saveDialog.ShowDialog(owner) != true)
                return;

            string actionId =
                DebugDiagnosticsService.CreateActionId("JSONEXPORT");

            WriteExportCandidate(
                selected,
                saveDialog.FileName,
                actionId);

            MessageBox.Show(
                owner,
                "Exported controls for:\n\n" + selected.DisplayName,
                "Export Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "Control JSON export failed");

            MessageBox.Show(
                owner,
                "The selected control file could not be exported.\n\n" + ex.Message,
                "Export Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void WriteExportCandidate(
    ExportCandidate candidate,
    string destinationPath,
    string actionId)
    {
        switch (candidate.Kind)
        {
            case ExportCandidateKind.Keyboard:
                if (candidate.KeyboardProfile is null)
                    throw new InvalidOperationException("Keyboard export candidate is missing its profile.");

                _keyboardJsonWriter.WriteExportFile(
                    candidate.KeyboardProfile,
                    destinationPath,
                    actionId);

                DebugDiagnosticsService.Info(
                    $"Binding JSON exported from memory | Type=Keyboard | Display=\"{candidate.DisplayName}\" | Destination=\"{destinationPath}\" | ActionId={actionId}");

                break;

            case ExportCandidateKind.Device:
                if (candidate.DeviceProfile is null ||
                    candidate.DeviceAircraftProfile is null)
                {
                    throw new InvalidOperationException("Device export is missing its profile.");
                }

                _deviceJsonWriter.WriteExportFile(
                    candidate.DeviceProfile,
                    candidate.DeviceAircraftProfile,
                    destinationPath,
                    actionId);

                DebugDiagnosticsService.Info(
                    $"Control JSON exported from memory | Type=Device | Display=\"{candidate.DisplayName}\" | Destination=\"{destinationPath}\" | ActionId={actionId}");

                break;

            default:
                throw new InvalidOperationException("Unknown export candidate type.");
        }
    }

    private bool ImportDeviceJson(
        string baseDir,
        BindingModel bindingModel,
        string sourcePath,
        ImportCandidate candidate,
        Window? owner)
    {
        if (string.IsNullOrWhiteSpace(candidate.PidVid) ||
            string.IsNullOrWhiteSpace(candidate.AircraftProfile))
        {
            MessageBox.Show(
                owner,
                "The selected device control file is missing required identity fields.\n\n" +
                "Required fields: pidvid, aircraft_profile.",
                "Import Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        List<DeviceBindingProfile> pidVidMatches =
            bindingModel.DeviceProfiles
                .Where(device =>
                    device.IsConnected &&
                    SameText(device.PidVid, candidate.PidVid))
                .ToList();

        if (pidVidMatches.Count == 0)
        {
            MessageBox.Show(
                owner,
                "This control file is for:\n\n" +
                DisplayImportDeviceName(candidate) + "\n" +
                candidate.AircraftProfile + "\n\n" +
                "A matching device is not currently detected by the Launcher.\n\n" +
                "The control file was not imported. Connect the matching device and try again.",
                "Import Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        if (pidVidMatches.Count > 1)
        {
            MessageBox.Show(
                owner,
                "This control file matches more than one connected device.\n\n" +
                "The Launcher cannot safely choose which connected device should receive this update.\n\n" +
                "The control file was not imported.",
                "Import Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        DeviceBindingProfile matchingDevice = pidVidMatches[0];

        bool productNameMatches =
            SameText(
                NormalizeDeviceName(matchingDevice.ProductName),
                NormalizeDeviceName(candidate.ProductName));

        string jsonDir = GetJsonDir(baseDir);
        Directory.CreateDirectory(jsonDir);

        string destinationFileName =
            BuildDeviceFileName(
                candidate.AircraftProfile,
                matchingDevice.DurableDeviceKey,
                matchingDevice.ProductName);

        string destinationPath =
            Path.Combine(
                jsonDir,
                destinationFileName);

        if (!ConfirmReplaceIfNeeded(destinationPath, owner))
            return false;

        BackupIfExists(
            destinationPath,
            candidate.AircraftProfile,
            matchingDevice.ProductName);
        CopyJson(sourcePath, destinationPath);

        DebugDiagnosticsService.Info(
            $"Device JSON imported | Device=\"{matchingDevice.ProductName}\" | Aircraft={candidate.AircraftProfile} | Source=\"{sourcePath}\" | Destination=\"{destinationPath}\"");

        if (!productNameMatches)
        {
            DebugDiagnosticsService.Warn(
                $"Device JSON imported with product name mismatch | JsonProductName=\"{candidate.ProductName}\" | ConnectedProductName=\"{matchingDevice.ProductName}\" | PIDVID={candidate.PidVid} | Aircraft={candidate.AircraftProfile}");
        }

        MessageBox.Show(
            owner,
            "Imported control for:\n\n" +
            matchingDevice.ProductName + "\n" +
            candidate.AircraftProfile,
            "Import Controls",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return true;
    }

    private bool ImportKeyboardJson(
        string baseDir,
        BindingModel bindingModel,
        string sourcePath,
        ImportCandidate candidate,
        Window? owner)
    {
        if (string.IsNullOrWhiteSpace(candidate.AircraftProfile))
        {
            MessageBox.Show(
                owner,
                "The selected keyboard file is missing aircraft_profile.",
                "Import Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        BindingAircraftProfile? matchingProfile = bindingModel.AircraftProfiles.FirstOrDefault(profile =>
            SameText(profile.AircraftProfile, candidate.AircraftProfile));

        if (matchingProfile is null)
        {
            MessageBox.Show(
                owner,
                "This keyboard file is for an aircraft profile that is not currently loaded:\n\n" +
                candidate.AircraftProfile,
                "Import Controls",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        MessageBoxResult replaceResult =
            MessageBox.Show(
                owner,
                "This will replace your current " + matchingProfile.AircraftProfile + " keyboard file.\n\n" +
                "A backup will be created before import.",
                "Import Keyboard Control",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

        if (replaceResult != MessageBoxResult.OK)
            return false;

        string jsonDir = GetJsonDir(baseDir);
        Directory.CreateDirectory(jsonDir);

        string destinationFileName =
            "KeyboardBindings_" +
            SanitizeFileNameSegment(matchingProfile.AircraftProfile).TrimEnd('.') +
            ".json";

        string destinationPath =
            Path.Combine(
                jsonDir,
                destinationFileName);

        BackupIfExists(
            destinationPath,
            matchingProfile.AircraftProfile,
            "Keyboard");
        CopyJson(sourcePath, destinationPath);

        DebugDiagnosticsService.Info(
            $"Keyboard JSON imported | Aircraft={matchingProfile.AircraftProfile} | Source=\"{sourcePath}\" | Destination=\"{destinationPath}\"");

        MessageBox.Show(
            owner,
            "Imported keyboard file for:\n\n" + matchingProfile.AircraftProfile,
            "Import Controls",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return true;
    }

    private static string DisplayImportDeviceName(
        ImportCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ProductName))
            return candidate.ProductName;

        if (!string.IsNullOrWhiteSpace(candidate.DurableDeviceKey))
            return candidate.DurableDeviceKey;

        return "Unknown Device";
    }

    private static bool ShowUnsupportedImport(
        string bindingType,
        Window? owner)
    {
        MessageBox.Show(
            owner,
            "The selected JSON file is not a supported Launcher control file.\n\n" +
            "binding_type: " + (string.IsNullOrWhiteSpace(bindingType) ? "(missing)" : bindingType),
            "Import Controls",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        return false;
    }

    private static bool ConfirmReplaceIfNeeded(
        string destinationPath,
        Window? owner)
    {
        if (!File.Exists(destinationPath))
            return true;

        MessageBoxResult result =
            MessageBox.Show(
                owner,
                "A matching control file already exists.\n\n" +
                Path.GetFileName(destinationPath) + "\n\n" +
                "A backup will be created before it is replaced.",
                "Import Controls",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }

    private static void BackupIfExists(
        string destinationPath,
        string aircraftProfile,
        string backupDisplayName)
    {
        if (!File.Exists(destinationPath))
            return;

        File.SetAttributes(
            destinationPath,
            File.GetAttributes(destinationPath) & ~FileAttributes.ReadOnly);

        string jsonDir =
            Path.GetDirectoryName(destinationPath) ?? "";

        string configDir =
            Directory.GetParent(jsonDir)?.FullName ?? jsonDir;

        string backupDir =
            Path.Combine(
                configDir,
                "Launcher-Backups");

        Directory.CreateDirectory(
            backupDir);

        string timestamp =
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        string backupFileName =
            BuildImportBackupFileName(
                timestamp,
                aircraftProfile,
                backupDisplayName);

        string backupPath =
            Path.Combine(
                backupDir,
                backupFileName);

        backupPath =
            GetUniqueBackupPath(backupPath);

        File.Copy(
            destinationPath,
            backupPath,
            overwrite: false);

        DebugDiagnosticsService.Info(
            $"Binding JSON backup created | Source=\"{destinationPath}\" | Backup=\"{backupPath}\"");
    }

    private static string BuildImportBackupFileName(
    string timestamp,
    string aircraftProfile,
    string backupDisplayName)
    {
        string safeAircraftProfile =
            SanitizeFileNameSegment(aircraftProfile).TrimEnd('.');

        if (string.IsNullOrWhiteSpace(safeAircraftProfile))
            safeAircraftProfile = "Unknown Aircraft";

        string safeDisplayName =
            SanitizeFileNameSegment(backupDisplayName).TrimEnd('.');

        if (string.IsNullOrWhiteSpace(safeDisplayName))
            safeDisplayName = "Unknown";

        safeDisplayName =
            LimitFileNameSegment(
                safeDisplayName,
                80);

        return
            "Backup-" +
            timestamp +
            "_" +
            safeAircraftProfile +
            "_" +
            safeDisplayName +
            ".json";
    }

    private static string LimitFileNameSegment(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength).TrimEnd('.', ' ');
    }

    private static string GetUniqueBackupPath(
    string backupPath)
    {
        if (!File.Exists(backupPath))
            return backupPath;

        string directory =
            Path.GetDirectoryName(backupPath) ?? "";

        string fileNameWithoutExtension =
            Path.GetFileNameWithoutExtension(backupPath);

        string extension =
            Path.GetExtension(backupPath);

        for (int index = 2; index < 100; index++)
        {
            string numberedPath =
                Path.Combine(
                    directory,
                    fileNameWithoutExtension + "-" + index.ToString("00") + extension);

            if (!File.Exists(numberedPath))
                return numberedPath;
        }

        throw new IOException("Unable to create a unique import backup file.");
    }


    private static void CopyJson(
        string sourcePath,
        string destinationPath)
    {
        if (File.Exists(destinationPath))
            File.SetAttributes(destinationPath, File.GetAttributes(destinationPath) & ~FileAttributes.ReadOnly);

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static ImportCandidate ReadImportCandidate(
        string path)
    {
        using FileStream stream = File.OpenRead(path);

        using JsonDocument document =
            JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The selected JSON file does not contain a JSON object.");

        return new ImportCandidate(
            BindingType: ReadString(root, "binding_type"),
            AircraftProfile: ReadString(root, "aircraft_profile"),
            PidVid: ReadString(root, "pidvid"),
            DurableDeviceKey: ReadString(root, "durable_device_key"),
            ProductName: ReadString(root, "product_name"));
    }

    private static string ReadString(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
            return "";

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : property.ToString();
    }

    private static List<ExportCandidate> BuildExportCandidates(
        BindingModel bindingModel)
    {
        var candidates = new List<ExportCandidate>();

        foreach (BindingAircraftProfile profile in bindingModel.AircraftProfiles)
        {
            string fileName =
                "KeyboardBindings_" +
                SanitizeFileNameSegment(profile.AircraftProfile).TrimEnd('.') +
                ".json";

            candidates.Add(
                new ExportCandidate(
                    DisplayName: "Keyboard / " + profile.AircraftProfile,
                    FileName: fileName,
                    Kind: ExportCandidateKind.Keyboard,
                    KeyboardProfile: profile,
                    DeviceProfile: null,
                    DeviceAircraftProfile: null));
        }

        foreach (DeviceBindingProfile device in bindingModel.DeviceProfiles)
        {
            foreach (DeviceAircraftBindingProfile aircraft in device.AircraftProfiles)
            {
                string fileName =
                    BuildDeviceFileName(
                        aircraft.AircraftProfile,
                        device.DurableDeviceKey,
                        device.ProductName);

                candidates.Add(
                    new ExportCandidate(
                        DisplayName: "Device / " + aircraft.AircraftProfile + " / " + device.ProductName,
                        FileName: fileName,
                        Kind: ExportCandidateKind.Device,
                        KeyboardProfile: null,
                        DeviceProfile: device,
                        DeviceAircraftProfile: aircraft));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExportCandidate? SelectExportCandidate(
        IReadOnlyList<ExportCandidate> candidates,
        Window? owner)
    {
        var listBox =
            new ListBox
            {
                ItemsSource = candidates,
                DisplayMemberPath = nameof(ExportCandidate.DisplayName),
                SelectionMode = SelectionMode.Single,
                MinWidth = 520,
                MinHeight = 280,
                Margin = new Thickness(0, 8, 0, 12)
            };

        listBox.SetResourceReference(
            Control.BackgroundProperty,
            "AppSurfaceBrush");

        listBox.SetResourceReference(
            Control.ForegroundProperty,
            "AppForegroundBrush");

        listBox.SetResourceReference(
            Control.BorderBrushProperty,
            "AppBorderBrush");

        if (candidates.Count > 0)
            listBox.SelectedIndex = 0;

        var exportButton =
            new Button
            {
                Content = "Export Selected",
                Width = 120,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };

        var cancelButton =
            new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };

        var buttons =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(exportButton);

        var panel =
            new StackPanel
            {
                Margin = new Thickness(16)
            };

        panel.Children.Add(
            new TextBlock
            {
                Text = "Choose one control file to export:",
                FontWeight = FontWeights.SemiBold
            });

        panel.Children.Add(listBox);
        panel.Children.Add(buttons);

        var window =
            new Window
            {
                Title = "Export Controls",
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };

        if (owner is not null)
            window.Owner = owner;

        exportButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not null)
                window.DialogResult = true;
        };

        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is not null)
                window.DialogResult = true;
        };

        return window.ShowDialog() == true
            ? listBox.SelectedItem as ExportCandidate
            : null;
    }

    private static string BuildDeviceFileName(
        string aircraftProfile,
        string durableDeviceKey,
        string productName)
    {
        string safeAircraftProfile = SanitizeFileNameSegment(aircraftProfile);
        if (string.IsNullOrWhiteSpace(safeAircraftProfile))
            safeAircraftProfile = "Unknown Aircraft";

        string safeProductName = SanitizeFileNameSegment(productName);
        if (string.IsNullOrWhiteSpace(safeProductName))
            safeProductName = "Unknown Device";

        return
            "DeviceBindings_" +
            safeAircraftProfile.TrimEnd('.') +
            "_" +
            durableDeviceKey +
            "_" +
            safeProductName.TrimEnd('.') +
            ".json";
    }

    private static string GetJsonDir(
        string baseDir)
    {
        return Path.Combine(baseDir, "User", "Config", "JSON");
    }

    private static string SanitizeFileNameSegment(
        string value)
    {
        string safeValue = value ?? "";

        foreach (char invalid in Path.GetInvalidFileNameChars())
            safeValue = safeValue.Replace(invalid, '_');

        return safeValue.Trim();
    }

    private static string NormalizeDeviceName(
        string value)
    {
        return string.Join(
            " ",
            (value ?? "").Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool SameText(
        string left,
        string right)
    {
        return string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ImportCandidate(
        string BindingType,
        string AircraftProfile,
        string PidVid,
        string DurableDeviceKey,
        string ProductName);

    private enum ExportCandidateKind
    {
        Keyboard,
        Device
    }

    private sealed record ExportCandidate(
        string DisplayName,
        string FileName,
        ExportCandidateKind Kind,
        BindingAircraftProfile? KeyboardProfile,
        DeviceBindingProfile? DeviceProfile,
        DeviceAircraftBindingProfile? DeviceAircraftProfile);
}