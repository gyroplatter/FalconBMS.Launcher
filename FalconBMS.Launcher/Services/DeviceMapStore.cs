using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Resolves and stores device-map images and persisted map JSON.
///
/// Launcher-provided map assets live under Stock\Maps.
/// User-provided map assets live under User\Config\Launcher-Backups.
///
/// Assets are matched to hardware by PID/VID, embedded in the filename as
/// {PIDVID}. The human-readable device name remains in the filename only for
/// readability.
/// </summary>
public sealed class DeviceMapStore
{
    private static readonly string[] SupportedImageExtensions =
    {
        ".png",
        ".jpg",
        ".jpeg"
    };

    private static readonly JsonSerializerOptions MapJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

    /// <summary>
    /// Finds the image associated with a device.
    /// User images override launcher-provided stock images.
    /// </summary>
    public string? FindImagePath(
        string baseDir,
        DeviceBindingProfile device)
    {
        if (string.IsNullOrWhiteSpace(baseDir) ||
            string.IsNullOrWhiteSpace(device.PidVid))
        {
            return null;
        }

        string? userImage =
            FindMatchingImage(
                GetUserMapDirectory(baseDir),
                device.PidVid);

        if (!string.IsNullOrWhiteSpace(userImage))
            return userImage;

        return FindMatchingImage(
            GetStockMapDirectory(),
            device.PidVid);
    }

    /// <summary>
    /// Finds persisted map JSON for a device.
    /// User map JSON overrides launcher-provided stock map JSON.
    /// </summary>
    public string? FindMapPath(
        string baseDir,
        DeviceBindingProfile device)
    {
        if (string.IsNullOrWhiteSpace(baseDir) ||
            string.IsNullOrWhiteSpace(device.PidVid))
        {
            return null;
        }

        string? userMap =
            FindMatchingMap(
                GetUserMapDirectory(baseDir),
                device.PidVid);

        if (!string.IsNullOrWhiteSpace(userMap))
            return userMap;

        return FindMatchingMap(
            GetStockMapDirectory(),
            device.PidVid);
    }

    public DeviceMapDefinition? LoadMap(
        string baseDir,
        DeviceBindingProfile device)
    {
        string? mapPath =
            FindMapPath(
                baseDir,
                device);

        if (string.IsNullOrWhiteSpace(mapPath) ||
            !File.Exists(mapPath))
        {
            return null;
        }

        try
        {
            string json =
                File.ReadAllText(mapPath);

            return JsonSerializer.Deserialize<DeviceMapDefinition>(
                json,
                MapJsonOptions);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map JSON load failed: {mapPath}");

            return null;
        }
    }

    public string SaveMap(
        string baseDir,
        DeviceBindingProfile device,
        DeviceMapDefinition map)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            throw new ArgumentException(
                "The BMS install folder is required.",
                nameof(baseDir));
        }

        if (string.IsNullOrWhiteSpace(device.PidVid))
        {
            throw new InvalidOperationException(
                "The selected device does not have a PID/VID identity.");
        }

        string userMapDirectory =
            GetUserMapDirectory(baseDir);

        Directory.CreateDirectory(
            userMapDirectory);

        string destinationPath =
            Path.Combine(
                userMapDirectory,
                GetMapFileName(device));

        string json =
            JsonSerializer.Serialize(
                map,
                MapJsonOptions);

        File.WriteAllText(
            destinationPath,
            json);

        return destinationPath;
    }

    /// <summary>
    /// Copies a user-selected image into User\Config\Launcher-Backups using
    /// the device name plus PID/VID. Only one user image is retained for each
    /// PID/VID.
    /// </summary>
    public string ImportUserImage(
        string baseDir,
        DeviceBindingProfile device,
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            throw new ArgumentException(
                "The BMS install folder is required.",
                nameof(baseDir));
        }

        if (string.IsNullOrWhiteSpace(device.PidVid))
        {
            throw new InvalidOperationException(
                "The selected device does not have a PID/VID identity.");
        }

        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "The selected image could not be found.",
                sourcePath);
        }

        string extension =
            Path.GetExtension(sourcePath);

        if (!IsSupportedImageExtension(extension))
        {
            throw new InvalidOperationException(
                "Device map images must be PNG, JPG, or JPEG files.");
        }

        string userMapDirectory =
            GetUserMapDirectory(baseDir);

        Directory.CreateDirectory(
            userMapDirectory);

        string displayName =
            GetDeviceDisplayName(device);

        string destinationFileName =
            $"{SanitizeFileName(displayName)} {{{device.PidVid}}}{extension.ToLowerInvariant()}";

        string destinationPath =
            Path.Combine(
                userMapDirectory,
                destinationFileName);

        string sourceFullPath =
            Path.GetFullPath(sourcePath);

        string destinationFullPath =
            Path.GetFullPath(destinationPath);

        if (!string.Equals(
                sourceFullPath,
                destinationFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(
                sourceFullPath,
                destinationFullPath,
                overwrite: true);
        }

        // Keep lookup deterministic by allowing only one user image for a
        // particular PID/VID, regardless of image extension.
        foreach (string existingPath in
                 EnumerateMatchingImages(
                     userMapDirectory,
                     device.PidVid))
        {
            if (string.Equals(
                    Path.GetFullPath(existingPath),
                    destinationFullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(existingPath);
        }

        return destinationFullPath;
    }

    /// <summary>
    /// Loads the image fully into memory so WPF does not keep the source file
    /// locked. This allows the image to be replaced while the launcher runs.
    /// </summary>
    public BitmapImage? LoadImage(
        string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) ||
            !File.Exists(imagePath))
        {
            return null;
        }

        var image =
            new BitmapImage();

        image.BeginInit();

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.UriSource =
            new Uri(
                imagePath,
                UriKind.Absolute);

        image.EndInit();
        image.Freeze();

        return image;
    }

    private static string GetStockMapDirectory()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Stock",
            "Maps");
    }

    private static string GetUserMapDirectory(
        string baseDir)
    {
        return Path.Combine(
            baseDir,
            "User",
            "Config",
            "Launcher-Backups");
    }

    private static string? FindMatchingImage(
        string directory,
        string pidVid)
    {
        return EnumerateMatchingImages(
                directory,
                pidVid)
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindMatchingMap(
        string directory,
        string pidVid)
    {
        if (!Directory.Exists(directory))
            return null;

        string pidVidToken =
            "{" + pidVid + "}";

        return Directory
            .EnumerateFiles(
                directory,
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                Path.GetFileNameWithoutExtension(path)
                    .IndexOf(
                        pidVidToken,
                        StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(
                path => path,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateMatchingImages(
        string directory,
        string pidVid)
    {
        if (!Directory.Exists(directory))
            return Enumerable.Empty<string>();

        string pidVidToken =
            "{" + pidVid + "}";

        return Directory
            .EnumerateFiles(
                directory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path =>
                IsSupportedImageExtension(
                    Path.GetExtension(path)))
            .Where(path =>
                Path.GetFileNameWithoutExtension(path)
                    .IndexOf(
                        pidVidToken,
                        StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsSupportedImageExtension(
        string extension)
    {
        return SupportedImageExtensions.Any(candidate =>
            string.Equals(
                candidate,
                extension,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string GetMapFileName(
        DeviceBindingProfile device)
    {
        return
            $"{SanitizeFileName(GetDeviceDisplayName(device))} {{{device.PidVid}}}.json";
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

    private static string SanitizeFileName(
        string value)
    {
        char[] invalidCharacters =
            Path.GetInvalidFileNameChars();

        char[] sanitized =
            value
                .Select(character =>
                    invalidCharacters.Contains(character)
                        ? '_'
                        : character)
                .ToArray();

        string result =
            new string(sanitized)
                .Trim()
                .TrimEnd('.');

        return string.IsNullOrWhiteSpace(result)
            ? "Device"
            : result;
    }
}