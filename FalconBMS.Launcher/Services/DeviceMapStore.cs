using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Resolves and stores device-map images.
///
/// Launcher-provided images live under Stock\Maps.
/// User-provided images live under User\Config\Launcher-Backups.
///
/// Images are matched to hardware by PID/VID, which is embedded in the
/// filename as {PIDVID}. The human-readable device name remains in the
/// filename only for readability.
/// </summary>
public sealed class DeviceMapStore
{
    private static readonly string[] SupportedImageExtensions =
    {
        ".png",
        ".jpg",
        ".jpeg"
    };

    /// <summary>
    /// Finds the image associated with a device.
    ///
    /// User images are checked first so a user replacement overrides
    /// a launcher-provided stock image for the same PID/VID.
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
                GetUserImageDirectory(baseDir),
                device.PidVid);

        if (!string.IsNullOrWhiteSpace(userImage))
            return userImage;

        return FindMatchingImage(
            GetStockImageDirectory(),
            device.PidVid);
    }

    /// <summary>
    /// Copies a user-selected image into User\Config\Launcher-Backups
    /// using the device name plus PID/VID.
    ///
    /// Only one user image is retained for each PID/VID.
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

        string userImageDirectory =
            GetUserImageDirectory(baseDir);

        Directory.CreateDirectory(
            userImageDirectory);

        string displayName =
            GetDeviceDisplayName(device);

        string destinationFileName =
            $"{SanitizeFileName(displayName)} {{{device.PidVid}}}{extension.ToLowerInvariant()}";

        string destinationPath =
            Path.Combine(
                userImageDirectory,
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

        // Keep lookup deterministic by allowing only one user image
        // for a particular PID/VID.
        foreach (string existingPath in
                 EnumerateMatchingImages(
                     userImageDirectory,
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
    /// Loads the image fully into memory so WPF does not keep the source
    /// file locked. This will matter when users replace an image later.
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

    private static string GetStockImageDirectory()
    {
        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Stock",
            "Maps");
    }

    private static string GetUserImageDirectory(
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