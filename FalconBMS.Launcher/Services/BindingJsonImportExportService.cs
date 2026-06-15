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

    public bool Import(
        string baseDir,
        BindingModel bindingModel,
        Window? owner)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Import Bindings",
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
            List<ExportCandidate> candidates = BuildExportCandidates(baseDir, bindingModel);

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "No binding JSON files were found to export.",
                    "Export Bindings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ExportCandidate? selected = SelectExportCandidate(candidates, owner);
            if (selected is null)
                return;

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Bindings",
                Filter = BindingJsonFilter,
                FileName = selected.FileName,
                OverwritePrompt = true
            };

            if (saveDialog.ShowDialog(owner) != true)
                return;

            File.Copy(selected.Path, saveDialog.FileName, overwrite: true);

            DebugDiagnosticsService.Info(
                $"Binding JSON exported | Source=\"{selected.Path}\" | Destination=\"{saveDialog.FileName}\"");

            MessageBox.Show(
                owner,
                "Exported bindings for:\n\n" + selected.DisplayName,
                "Export Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, "Binding JSON export failed");

            MessageBox.Show(
                owner,
                "The selected bindings could not be exported.\n\n" + ex.Message,
                "Export Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
            string.IsNullOrWhiteSpace(candidate.ProductName) ||
            string.IsNullOrWhiteSpace(candidate.AircraftProfile))
        {
            MessageBox.Show(
                owner,
                "The selected device binding file is missing required identity fields.\n\n" +
                "Required fields: pidvid, product_name, aircraft_profile.",
                "Import Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        DeviceBindingProfile? matchingDevice = bindingModel.DeviceProfiles.FirstOrDefault(device =>
            device.IsConnected &&
            SameText(device.PidVid, candidate.PidVid) &&
            SameText(NormalizeDeviceName(device.ProductName), NormalizeDeviceName(candidate.ProductName)));

        if (matchingDevice is null)
        {
            MessageBox.Show(
                owner,
                "This binding file is for:\n\n" +
                candidate.ProductName + "\n" +
                candidate.AircraftProfile + "\n\n" +
                "That matching device is not currently detected by the Launcher.\n\n" +
                "The bindings were not imported. Connect the matching device and try again.",
                "Import Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

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

        BackupIfExists(destinationPath);
        CopyJson(sourcePath, destinationPath);

        DebugDiagnosticsService.Info(
            $"Device binding JSON imported | Device=\"{matchingDevice.ProductName}\" | Aircraft={candidate.AircraftProfile} | Source=\"{sourcePath}\" | Destination=\"{destinationPath}\"");

        MessageBox.Show(
            owner,
            "Imported bindings for:\n\n" + matchingDevice.ProductName + "\n" + candidate.AircraftProfile,
            "Import Bindings",
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
                "The selected keyboard binding file is missing aircraft_profile.",
                "Import Bindings",
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
                "This keyboard binding file is for an aircraft profile that is not currently loaded:\n\n" +
                candidate.AircraftProfile,
                "Import Bindings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        MessageBoxResult replaceResult =
            MessageBox.Show(
                owner,
                "This will replace your current " + matchingProfile.AircraftProfile + " keyboard bindings.\n\n" +
                "A backup will be created before import.",
                "Import Keyboard Bindings",
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

        BackupIfExists(destinationPath);
        CopyJson(sourcePath, destinationPath);

        DebugDiagnosticsService.Info(
            $"Keyboard binding JSON imported | Aircraft={matchingProfile.AircraftProfile} | Source=\"{sourcePath}\" | Destination=\"{destinationPath}\"");

        MessageBox.Show(
            owner,
            "Imported keyboard bindings for:\n\n" + matchingProfile.AircraftProfile,
            "Import Bindings",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        return true;
    }

    private static bool ShowUnsupportedImport(
        string bindingType,
        Window? owner)
    {
        MessageBox.Show(
            owner,
            "The selected JSON file is not a supported Launcher binding file.\n\n" +
            "binding_type: " + (string.IsNullOrWhiteSpace(bindingType) ? "(missing)" : bindingType),
            "Import Bindings",
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
                "A matching binding file already exists.\n\n" +
                Path.GetFileName(destinationPath) + "\n\n" +
                "A backup will be created before it is replaced.",
                "Import Bindings",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }

    private static void BackupIfExists(
        string destinationPath)
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

        string timestamp =
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        string backupDir =
            Path.Combine(
                configDir,
                "Launcher-Import-Backup-" + timestamp);

        Directory.CreateDirectory(backupDir);

        string backupPath =
            Path.Combine(
                backupDir,
                Path.GetFileName(destinationPath));

        File.Copy(
            destinationPath,
            backupPath,
            overwrite: false);

        DebugDiagnosticsService.Info(
            $"Binding JSON backup created | Source=\"{destinationPath}\" | Backup=\"{backupPath}\"");
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
        string baseDir,
        BindingModel bindingModel)
    {
        string jsonDir = GetJsonDir(baseDir);
        var candidates = new List<ExportCandidate>();

        foreach (BindingAircraftProfile profile in bindingModel.AircraftProfiles.OrderBy(profile => profile.AircraftProfile, StringComparer.OrdinalIgnoreCase))
        {
            string fileName =
                "KeyboardBindings_" +
                SanitizeFileNameSegment(profile.AircraftProfile).TrimEnd('.') +
                ".json";

            string path =
                Path.Combine(
                    jsonDir,
                    fileName);

            if (File.Exists(path))
            {
                candidates.Add(
                    new ExportCandidate(
                        DisplayName: "Keyboard / " + profile.AircraftProfile,
                        FileName: fileName,
                        Path: path));
            }
        }

        foreach (DeviceBindingProfile device in bindingModel.DeviceProfiles.OrderBy(device => device.ProductName, StringComparer.OrdinalIgnoreCase))
        {
            foreach (DeviceAircraftBindingProfile aircraft in device.AircraftProfiles.OrderBy(aircraft => aircraft.AircraftProfile, StringComparer.OrdinalIgnoreCase))
            {
                string fileName =
                    BuildDeviceFileName(
                        aircraft.AircraftProfile,
                        device.DurableDeviceKey,
                        device.ProductName);

                string path =
                    Path.Combine(
                        jsonDir,
                        fileName);

                if (File.Exists(path))
                {
                    candidates.Add(
                        new ExportCandidate(
                            DisplayName: "Device / " + aircraft.AircraftProfile + " / " + device.ProductName,
                            FileName: fileName,
                            Path: path));
                }
            }
        }

        return candidates;
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
                MinWidth = 520,
                MinHeight = 280,
                Margin = new Thickness(0, 8, 0, 12)
            };

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

        buttons.Children.Add(exportButton);
        buttons.Children.Add(cancelButton);

        var panel =
            new StackPanel
            {
                Margin = new Thickness(16)
            };

        panel.Children.Add(
            new TextBlock
            {
                Text = "Choose a control file to export:",
                FontWeight = FontWeights.SemiBold
            });

        panel.Children.Add(listBox);
        panel.Children.Add(buttons);

        var window =
            new Window
            {
                Title = "Export Bindings",
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

    private sealed record ExportCandidate(
        string DisplayName,
        string FileName,
        string Path);
}