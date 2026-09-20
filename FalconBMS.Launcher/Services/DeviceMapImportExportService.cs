using FalconBMS.Launcher.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Imports and exports portable Device Map ZIP packages.
///
/// This service is separate from Controls import/export.
/// DeviceMapStore remains responsible for device-map identity matching
/// and the final user-map storage layout.
/// </summary>
public sealed class DeviceMapImportExportService
{
    private const string DeviceMapZipFilter =
        "Device Map ZIP (*.zip)|*.zip";

    private const long MaximumJsonBytes =
        1024 * 1024;

    private const long MaximumImageBytes =
        50L * 1024L * 1024L;

    private static readonly JsonSerializerOptions MapJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly DeviceMapStore _deviceMapStore =
        new();

    /// <summary>
    /// Opens a Device Map ZIP, verifies that its hardware is currently connected,
    /// and stores it in the user DeviceMaps folder.
    ///
    /// Returns the connected device that received the map, or null when the user
    /// cancels or the import is rejected.
    /// </summary>
    public DeviceBindingProfile? Import(
        string baseDir,
        BindingModel bindingModel,
        Window? owner)
    {
        var openDialog =
            new OpenFileDialog
            {
                Title = "Import Device Map",
                Filter = DeviceMapZipFilter,
                CheckFileExists = true,
                Multiselect = false
            };

        if (openDialog.ShowDialog(owner) != true)
            return null;

        try
        {
            using FileStream zipStream =
                new FileStream(
                    openDialog.FileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            using var archive =
                new ZipArchive(
                    zipStream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);

            ImportPackage package =
                ReadImportPackage(
                    archive);

            DeviceBindingProfile? matchingDevice =
                _deviceMapStore.FindMatchingConnectedDeviceForImport(
                    package.Map,
                    package.MapEntry.Name,
                    bindingModel.DeviceProfiles);

            if (matchingDevice is null)
            {
                MessageBox.Show(
                    owner,
                    "A matching device is not currently detected by the Launcher.\n\n" +
                    "The device map was not imported. Connect the matching device and try again.",
                    "Import Device Map",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return null;
            }

            if (_deviceMapStore.HasUserMap(
                    baseDir,
                    matchingDevice))
            {
                MessageBoxResult replaceResult =
                    MessageBox.Show(
                        owner,
                        "A user map already exists for:\n\n" +
                        GetDeviceDisplayName(matchingDevice) +
                        "\n\nReplace it with the imported map?",
                        "Import Device Map",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                if (replaceResult != MessageBoxResult.Yes)
                    return null;
            }

            using Stream imageStream =
                package.ImageEntry.Open();

            _deviceMapStore.SaveImportedMap(
                baseDir,
                matchingDevice,
                package.Map,
                imageStream,
                Path.GetExtension(
                    package.ImageEntry.Name));

            DebugDiagnosticsService.Info(
                $"Device map imported | Device=\"{GetDeviceDisplayName(matchingDevice)}\" | Source=\"{openDialog.FileName}\"");

            MessageBox.Show(
                owner,
                "Imported device map for:\n\n" +
                GetDeviceDisplayName(matchingDevice),
                "Import Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return matchingDevice;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map import failed: {openDialog.FileName}");

            MessageBox.Show(
                owner,
                "The selected device map could not be imported.\n\n" +
                ex.Message,
                "Import Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return null;
        }
    }

    /// <summary>
    /// Exports the exact saved JSON and image currently resolved for the selected
    /// device. The map is not serialized from memory.
    /// </summary>
    public void Export(
        string baseDir,
        DeviceBindingProfile device,
        Window? owner)
    {
        string? mapPath =
            _deviceMapStore.FindMapPath(
                baseDir,
                device);

        string? imagePath =
            _deviceMapStore.FindImagePath(
                baseDir,
                device);

        if (string.IsNullOrWhiteSpace(mapPath) ||
            string.IsNullOrWhiteSpace(imagePath) ||
            !File.Exists(mapPath) ||
            !File.Exists(imagePath))
        {
            MessageBox.Show(
                owner,
                "No saved device map is available to export.",
                "Export Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        string resolvedMapPath =
            mapPath!;

        string resolvedImagePath =
            imagePath!;

        var saveDialog =
            new SaveFileDialog
            {
                Title = "Export Device Map",
                Filter = DeviceMapZipFilter,
                FileName =
                    Path.GetFileNameWithoutExtension(resolvedMapPath) +
                    ".zip",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true
            };

        if (saveDialog.ShowDialog(owner) != true)
            return;

        try
        {
            using FileStream destinationStream =
                new FileStream(
                    saveDialog.FileName,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

            using var archive =
                new ZipArchive(
                    destinationStream,
                    ZipArchiveMode.Create,
                    leaveOpen: false);

            // Export the exact files currently being used on disk.
            AddFileToArchive(
                archive,
                resolvedMapPath);

            AddFileToArchive(
                archive,
                resolvedImagePath);

            DebugDiagnosticsService.Info(
                $"Device map exported from saved files | Device=\"{GetDeviceDisplayName(device)}\" | Map=\"{resolvedMapPath}\" | Image=\"{resolvedImagePath}\" | Destination=\"{saveDialog.FileName}\"");

            MessageBox.Show(
                owner,
                "Exported device map for:\n\n" +
                GetDeviceDisplayName(device),
                "Export Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map export failed: {saveDialog.FileName}");

            MessageBox.Show(
                owner,
                "The device map could not be exported.\n\n" +
                ex.Message,
                "Export Device Map",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static ImportPackage ReadImportPackage(
        ZipArchive archive)
    {
        List<ZipArchiveEntry> jsonEntries =
            archive.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Name) &&
                    string.Equals(
                        Path.GetExtension(entry.Name),
                        ".json",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (jsonEntries.Count != 1)
        {
            throw new InvalidDataException(
                "A Device Map ZIP must contain exactly one JSON map file.");
        }

        ZipArchiveEntry mapEntry =
            jsonEntries[0];

        if (mapEntry.Length <= 0)
        {
            throw new InvalidDataException(
                "The Device Map JSON is empty.");
        }

        if (mapEntry.Length > MaximumJsonBytes)
        {
            throw new InvalidDataException(
                "The Device Map JSON is too large.");
        }

        DeviceMapDefinition map;

        using (Stream jsonStream = mapEntry.Open())
        {
            map =
                JsonSerializer.Deserialize<DeviceMapDefinition>(
                    jsonStream,
                    MapJsonOptions)
                ?? throw new InvalidDataException(
                    "The Device Map JSON is invalid.");
        }

        if (map.Hotspots is null ||
            map.Callouts is null)
        {
            throw new InvalidDataException(
                "The Device Map JSON does not contain valid hotspot and callout data.");
        }

        if (string.IsNullOrWhiteSpace(map.DeviceName) &&
            string.IsNullOrWhiteSpace(map.PidVid) &&
            string.IsNullOrWhiteSpace(
                Path.GetFileNameWithoutExtension(mapEntry.Name)))
        {
            throw new InvalidDataException(
                "The Device Map does not contain enough device information to identify its hardware.");
        }

        string imageFileName =
            Path.GetFileName(
                map.ImageFileName ?? "");

        if (string.IsNullOrWhiteSpace(imageFileName) ||
            !IsSupportedImageExtension(
                Path.GetExtension(imageFileName)))
        {
            throw new InvalidDataException(
                "The Device Map JSON does not reference a valid PNG, JPG, or JPEG image.");
        }

        List<ZipArchiveEntry> matchingImages =
            archive.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.Name) &&
                    string.Equals(
                        entry.Name,
                        imageFileName,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (matchingImages.Count != 1)
        {
            throw new InvalidDataException(
                "The image referenced by the Device Map JSON was not found in the ZIP.");
        }

        ZipArchiveEntry imageEntry =
            matchingImages[0];

        if (imageEntry.Length <= 0)
        {
            throw new InvalidDataException(
                "The Device Map image is empty.");
        }

        if (imageEntry.Length > MaximumImageBytes)
        {
            throw new InvalidDataException(
                "The Device Map image is too large.");
        }

        return new ImportPackage(
            mapEntry,
            imageEntry,
            map);
    }

    private static void AddFileToArchive(
        ZipArchive archive,
        string sourcePath)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                Path.GetFileName(sourcePath),
                CompressionLevel.Optimal);

        using Stream sourceStream =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

        using Stream destinationStream =
            entry.Open();

        sourceStream.CopyTo(
            destinationStream);
    }

    private static bool IsSupportedImageExtension(
        string extension)
    {
        return string.Equals(
                   extension,
                   ".png",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   extension,
                   ".jpg",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   extension,
                   ".jpeg",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDeviceDisplayName(
        DeviceBindingProfile device)
    {
        if (!string.IsNullOrWhiteSpace(device.ProductName))
            return device.ProductName;

        if (!string.IsNullOrWhiteSpace(device.InstanceName))
            return device.InstanceName;

        return device.PidVid;
    }

    private sealed class ImportPackage
    {
        public ImportPackage(
            ZipArchiveEntry mapEntry,
            ZipArchiveEntry imageEntry,
            DeviceMapDefinition map)
        {
            MapEntry = mapEntry;
            ImageEntry = imageEntry;
            Map = map;
        }

        public ZipArchiveEntry MapEntry { get; }

        public ZipArchiveEntry ImageEntry { get; }

        public DeviceMapDefinition Map { get; }
    }
}