using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Resolves and stores device-map JSON files and their images.
///
/// Lookup order for a device:
///   1. User map   : {BMS}\User\Config\Launcher-Backups\DeviceMaps
///   2. Stock map  : {BMS}\Launcher\Stock\DeviceMaps, else {exe folder}\Stock\DeviceMaps
///   3. Nothing found: null is returned and callers treat it as a blank map.
/// 
/// Nothing is written to \DeviceMaps until the user saves in the editor.
///
/// Matching deliberately mirrors StockDeviceSetupMatcherService (name first, then
/// PID/VID) but is separate so the Controls code path is untouched.
/// There is no PID/VID list to maintain. When a hardware variant reports a different
/// name, ship a second JSON (and image copy) under a different file name.
///
/// The image for a map is named inside the JSON (ImageFileName) and is loaded from the
/// same folder as the JSON.
/// </summary>
public sealed class DeviceMapStore
{
    private const string MapFolderName = "DeviceMaps";

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
    /// A map JSON that was found, parsed, and whose image exists on disk.
    /// </summary>
    private sealed class ResolvedMap
    {
        public string MapPath { get; init; } = "";

        public DeviceMapDefinition Map { get; init; } = new();

        public string ImagePath { get; init; } = "";
    }

    /// <summary>
    /// Path of the image belonging to the map that resolves for this device, or null.
    /// </summary>
    public string? FindImagePath(
        string baseDir,
        DeviceBindingProfile device)
    {
        return Resolve(baseDir, device)?.ImagePath;
    }

    /// <summary>
    /// Path of the map JSON that resolves for this device, or null.
    /// </summary>
    public string? FindMapPath(
        string baseDir,
        DeviceBindingProfile device)
    {
        return Resolve(baseDir, device)?.MapPath;
    }

    /// <summary>
    /// Loads the resolved map. Null means no map exists yet (blank device).
    /// </summary>
    public DeviceMapDefinition? LoadMap(
        string baseDir,
        DeviceBindingProfile device)
    {
        return Resolve(baseDir, device)?.Map;
    }

    /// <summary>
    /// True when this device currently resolves to a complete saved map and image.
    /// This is based on storage state rather than WPF image state.
    /// </summary>
    public bool HasResolvedMap(
        string baseDir,
        DeviceBindingProfile device)
    {
        return Resolve(
                   baseDir,
                   device) is not null;
    }

    /// <summary>
    /// Writes the map JSON to the user DeviceMaps folder. This is the only place a
    /// map file is created; stock and blank maps live in memory until the user saves.
    /// </summary>
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

        // Copy first: if this fails we never leave a JSON behind that points at a
        // missing image.
        CopyStockImageToUserFolder(
            baseDir,
            map.ImageFileName,
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
    /// True when a user map JSON exists for this device. Enables Delete Map.
    /// </summary>
    public bool HasUserMap(
        string baseDir,
        DeviceBindingProfile device)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return false;

        return FindMatchingMap(
                   GetUserMapDirectory(baseDir),
                   device) is not null;
    }

    /// <summary>
    /// Deletes the user map JSON and the image it points at. Stock files are never
    /// touched because everything here is resolved inside the user folder.
    /// </summary>
    public void DeleteUserMap(
        string baseDir,
        DeviceBindingProfile device)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return;

        string? mapPath =
            FindMatchingMap(
                GetUserMapDirectory(baseDir),
                device);

        if (mapPath is null)
            return;

        // Read the JSON first to learn which image belongs to it.
        string? imagePath = null;

        try
        {
            string json =
                File.ReadAllText(mapPath);

            DeviceMapDefinition? map =
                JsonSerializer.Deserialize<DeviceMapDefinition>(
                    json,
                    MapJsonOptions);

            if (map is not null)
                imagePath = ResolveImagePath(mapPath, map);
        }
        catch (Exception ex)
        {
            // A corrupt JSON must still be deletable. Its image is simply left behind.
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map JSON unreadable during delete: {mapPath}");
        }

        File.Delete(mapPath);

        if (imagePath is not null &&
            File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }
    }

    /// <summary>
    /// Copies a user-selected image into the user DeviceMaps folder using the device
    /// name plus PID/VID. Only one user image is retained for each PID/VID.
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
    /// Saves an already validated imported Device Map package into the user folder.
    ///
    /// DeviceName, PidVid, hotspots, and callouts remain exactly as authored.
    /// Only ImageFileName is changed so the imported image follows the launcher's
    /// normal per-device user-storage naming and cannot collide with another map.
    /// </summary>
    public string SaveImportedMap(
        string baseDir,
        DeviceBindingProfile device,
        DeviceMapDefinition map,
        Stream imageStream,
        string imageExtension)
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
                "The matching device does not have a PID/VID identity.");
        }

        if (map is null)
            throw new ArgumentNullException(nameof(map));

        if (imageStream is null)
            throw new ArgumentNullException(nameof(imageStream));

        if (!IsSupportedImageExtension(imageExtension))
        {
            throw new InvalidOperationException(
                "Device map images must be PNG, JPG, or JPEG files.");
        }

        string userMapDirectory =
            GetUserMapDirectory(baseDir);

        Directory.CreateDirectory(
            userMapDirectory);

        string normalizedExtension =
            imageExtension.ToLowerInvariant();

        string destinationImageFileName =
            $"{SanitizeFileName(GetDeviceDisplayName(device))} {{{device.PidVid}}}{normalizedExtension}";

        string destinationImagePath =
            Path.Combine(
                userMapDirectory,
                destinationImageFileName);

        string destinationMapPath =
            Path.Combine(
                userMapDirectory,
                GetMapFileName(device));

        /*
         * Prepare both replacement files before touching an existing user map.
         * This prevents a bad ZIP/image/write failure from deleting a working map.
         */
        string temporaryId =
            Guid.NewGuid()
                .ToString("N");

        string temporaryImagePath =
            Path.Combine(
                userMapDirectory,
                $".device-map-image-{temporaryId}.tmp");

        string temporaryMapPath =
            Path.Combine(
                userMapDirectory,
                $".device-map-json-{temporaryId}.tmp");

        try
        {
            using (FileStream destinationImageStream =
                   new FileStream(
                       temporaryImagePath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                imageStream.CopyTo(
                    destinationImageStream);
            }

            /*
             * Preserve the imported hardware identity and map geometry.
             * Only the image filename is launcher-controlled after import.
             */
            map.ImageFileName =
                destinationImageFileName;

            string json =
                JsonSerializer.Serialize(
                    map,
                    MapJsonOptions);

            File.WriteAllText(
                temporaryMapPath,
                json);

            /*
             * Everything required for the replacement now exists successfully,
             * so the previous user package can be removed.
             */
            DeleteUserMap(
                baseDir,
                device);

            if (File.Exists(destinationImagePath))
            {
                File.Delete(
                    destinationImagePath);
            }

            if (File.Exists(destinationMapPath))
            {
                File.Delete(
                    destinationMapPath);
            }

            File.Move(
                temporaryImagePath,
                destinationImagePath);

            File.Move(
                temporaryMapPath,
                destinationMapPath);

            // A previous user image for this PID/VID may have used another
            // extension. Retain only the image referenced by the new map.
            foreach (string existingPath in
                     EnumerateMatchingImages(
                         userMapDirectory,
                         device.PidVid))
            {
                if (string.Equals(
                        Path.GetFullPath(existingPath),
                        Path.GetFullPath(destinationImagePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(
                    existingPath);
            }

            return destinationMapPath;
        }
        finally
        {
            if (File.Exists(temporaryImagePath))
            {
                File.Delete(
                    temporaryImagePath);
            }

            if (File.Exists(temporaryMapPath))
            {
                File.Delete(
                    temporaryMapPath);
            }
        }
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

        using (FileStream stream =
               new FileStream(
                   imagePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            var image =
                new BitmapImage();

            image.BeginInit();

            image.CacheOption =
                BitmapCacheOption.OnLoad;

            image.StreamSource =
                stream;

            image.EndInit();
            image.Freeze();

            return image;
        }
    }

    // ------------------------------------------------------------------
    // Resolution
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the lookup order: user folder, then stock folder. Returns null when
    /// neither has a usable map, which callers treat as a blank map.
    /// </summary>
    private static ResolvedMap? Resolve(
        string baseDir,
        DeviceBindingProfile device)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return null;

        // A user-made (or user-edited) map always wins over the stock one.
        ResolvedMap? userMap =
            TryLoad(
                FindMatchingMap(
                    GetUserMapDirectory(baseDir),
                    device));

        if (userMap is not null)
            return userMap;

        // Launcher-provided stock map. Null here means no map anywhere.
        return TryLoad(
            FindMatchingMap(
                GetStockMapDirectory(baseDir),
                device));
    }

    /// <summary>
    /// Parses one map JSON and confirms its image exists. A map whose JSON is corrupt
    /// or whose image is missing is treated as not found, so the lookup can fall
    /// through to the next location instead of showing a broken map.
    /// </summary>
    private static ResolvedMap? TryLoad(
        string? mapPath)
    {
        // The explicit null check lets the compiler treat mapPath as non-null below.
        // On .NET Framework, string.IsNullOrWhiteSpace does not tell it that.
        if (mapPath is null ||
            string.IsNullOrWhiteSpace(mapPath) ||
            !File.Exists(mapPath))
        {
            return null;
        }

        try
        {
            string json =
                File.ReadAllText(mapPath);

            DeviceMapDefinition? map =
                JsonSerializer.Deserialize<DeviceMapDefinition>(
                    json,
                    MapJsonOptions);

            if (map is null)
                return null;

            string? imagePath =
                ResolveImagePath(
                    mapPath,
                    map);

            if (imagePath is null)
            {
                DebugDiagnosticsService.Warn(
                    $"Device map image not found beside its JSON. Map=\"{mapPath}\" | ImageFileName=\"{map.ImageFileName}\"");

                return null;
            }

            return new ResolvedMap
            {
                MapPath = mapPath,
                Map = map,
                ImagePath = imagePath
            };
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"Device map JSON load failed: {mapPath}");

            return null;
        }
    }

    /// <summary>
    /// The image is named inside the JSON and lives in the same folder as the JSON.
    /// Only the file-name portion is honoured, so a shared or imported JSON cannot
    /// point the launcher at a file outside its own folder.
    /// </summary>
    private static string? ResolveImagePath(
        string mapPath,
        DeviceMapDefinition map)
    {
        string imageFileName =
            Path.GetFileName(
                map.ImageFileName ?? "");

        if (string.IsNullOrWhiteSpace(imageFileName) ||
            !IsSupportedImageExtension(
                Path.GetExtension(imageFileName)))
        {
            return null;
        }

        string directory =
            Path.GetDirectoryName(mapPath) ?? "";

        string imagePath =
            Path.Combine(
                directory,
                imageFileName);

        return File.Exists(imagePath)
            ? imagePath
            : null;
    }

    // ------------------------------------------------------------------
    // Matching (mirrors StockDeviceSetupMatcherService, kept separate on purpose)
    // ------------------------------------------------------------------

    /// <summary>
    /// Finds the currently connected device represented by an imported Device Map.
    ///
    /// Matching follows the same Devices policy as normal map resolution:
    ///   1. Device name first.
    ///   2. PID/VID only as a fallback or disambiguator.
    ///
    /// Older maps do not necessarily contain the PidVid JSON property, so the
    /// existing {PIDVID} filename/DeviceName token is also supported.
    /// </summary>
    public DeviceBindingProfile? FindMatchingConnectedDeviceForImport(
        DeviceMapDefinition map,
        string sourceMapFileName,
        IEnumerable<DeviceBindingProfile> devices)
    {
        if (map is null)
            throw new ArgumentNullException(nameof(map));

        List<DeviceBindingProfile> connectedDevices =
            devices
                .Where(device =>
                    device.IsConnected)
                .ToList();

        if (connectedDevices.Count == 0)
            return null;

        string importedFileIdentity =
            Path.GetFileNameWithoutExtension(
                sourceMapFileName ?? "");

        List<DeviceBindingProfile> nameMatches =
            connectedDevices
                .Where(device =>
                    ImportedIdentityMatchesDeviceName(
                        importedFileIdentity,
                        map.DeviceName,
                        device.ProductName)
                    ||
                    ImportedIdentityMatchesDeviceName(
                        importedFileIdentity,
                        map.DeviceName,
                        device.InstanceName))
                .ToList();

        string? importedPidVid =
            NormalizePidVid(
                map.PidVid)
            ??
            ExtractPidVidToken(
                importedFileIdentity)
            ??
            ExtractPidVidToken(
                map.DeviceName);

        /*
         * Name is authoritative when it identifies one connected device.
         * This mirrors normal DeviceMapStore resolution, where the name tier
         * runs before PID/VID fallback.
         */
        if (nameMatches.Count == 1)
            return nameMatches[0];

        /*
         * More than one device can legitimately report the same product name.
         * Use PID/VID to narrow that name match when the imported map supplies it.
         */
        if (nameMatches.Count > 1)
        {
            if (!string.IsNullOrWhiteSpace(importedPidVid))
            {
                List<DeviceBindingProfile> narrowedMatches =
                    nameMatches
                        .Where(device =>
                            string.Equals(
                                device.PidVid,
                                importedPidVid,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (narrowedMatches.Count == 1)
                    return narrowedMatches[0];
            }

            throw new InvalidOperationException(
                "This Device Map matches more than one connected device. The Launcher cannot safely choose which device should receive it.");
        }

        /*
         * Name did not resolve. PID/VID is the fallback for devices whose
         * product name is reported differently by Windows, Wine, or a HID layer.
         */
        if (string.IsNullOrWhiteSpace(importedPidVid))
            return null;

        List<DeviceBindingProfile> pidVidMatches =
            connectedDevices
                .Where(device =>
                    string.Equals(
                        device.PidVid,
                        importedPidVid,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (pidVidMatches.Count == 1)
            return pidVidMatches[0];

        if (pidVidMatches.Count > 1)
        {
            throw new InvalidOperationException(
                "This Device Map matches more than one connected device. The Launcher cannot safely choose which device should receive it.");
        }

        return null;
    }

    private static bool ImportedIdentityMatchesDeviceName(
        string importedFileIdentity,
        string importedDeviceName,
        string? connectedDeviceName)
    {
        string normalizedConnectedName =
            Normalize(
                connectedDeviceName);

        if (normalizedConnectedName.Length == 0)
            return false;

        string normalizedFileIdentity =
            Normalize(
                importedFileIdentity);

        if (normalizedFileIdentity.Contains(
                normalizedConnectedName))
        {
            return true;
        }

        string normalizedDeviceName =
            Normalize(
                importedDeviceName);

        return normalizedDeviceName.Contains(
            normalizedConnectedName);
    }

    private static string? NormalizePidVid(
        string? value)
    {
        if (value is null)
            return null;

        string candidate =
            value
                .Trim()
                .Trim('{', '}');

        if (candidate.Length != 8 ||
            !candidate.All(Uri.IsHexDigit))
        {
            return null;
        }

        return candidate.ToUpperInvariant();
    }

    private static string? ExtractPidVidToken(
        string? value)
    {
        if (value is null ||
            value.Length == 0)
        {
            return null;
        }

        for (int index = 0;
             index <= value.Length - 10;
             index++)
        {
            if (value[index] != '{' ||
                value[index + 9] != '}')
            {
                continue;
            }

            string candidate =
                value.Substring(
                    index + 1,
                    8);

            if (candidate.All(Uri.IsHexDigit))
            {
                return candidate.ToUpperInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the map JSON in one folder that belongs to the device.
    ///
    ///   Tier 1: normalized ProductName, then InstanceName, contained in the
    ///           normalized file name (same rule as the stock XML name match).
    ///   Tier 2: the device's {PIDVID} token appears in the file name. This rescues
    ///           devices whose name is reported differently (Wine / other HID layers).
    ///           The PID/VID lives only in the file name, so there is no list.
    /// </summary>
    private static string? FindMatchingMap(
        string directory,
        DeviceBindingProfile device)
    {
        if (!Directory.Exists(directory))
            return null;

        string[] files =
            Directory.GetFiles(
                directory,
                "*.json",
                SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
            return null;

        // Deterministic order so the same device always resolves to the same file.
        Array.Sort(
            files,
            StringComparer.OrdinalIgnoreCase);

        string pidVidToken =
            string.IsNullOrWhiteSpace(device.PidVid)
                ? ""
                : "{" + device.PidVid + "}";

        string? nameMatch =
            FindNameMatch(
                files,
                device.ProductName,
                pidVidToken)
            ?? FindNameMatch(
                files,
                device.InstanceName,
                pidVidToken);

        if (nameMatch is not null)
            return nameMatch;

        return files.FirstOrDefault(path =>
            FileNameHasToken(
                path,
                pidVidToken));
    }

    private static string? FindNameMatch(
        string[] files,
        string deviceName,
        string pidVidToken)
    {
        string normalizedName =
            Normalize(deviceName);

        // string.Contains("") is always true, so an empty name would match every
        // file. Never match on an empty name.
        if (normalizedName.Length == 0)
            return null;

        List<string> candidates =
            files
                .Where(path =>
                    Normalize(
                            Path.GetFileNameWithoutExtension(path))
                        .Contains(normalizedName))
                .ToList();

        if (candidates.Count == 0)
            return null;

        // Several files can share a name (hardware variants, copies). Prefer the
        // one that also carries this device's exact PID/VID; otherwise the first.
        return candidates.FirstOrDefault(path =>
                   FileNameHasToken(
                       path,
                       pidVidToken))
               ?? candidates[0];
    }

    private static bool FileNameHasToken(
        string path,
        string token)
    {
        return token.Length > 0 &&
               Path.GetFileNameWithoutExtension(path)
                   .IndexOf(
                       token,
                       StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Same normalization as StockDeviceSetupMatcherService: letters and digits only,
    /// upper-cased, so punctuation and spacing differences never break a match.
    /// </summary>
    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
    }

    // ------------------------------------------------------------------
    // Folders and file helpers
    // ------------------------------------------------------------------

    private static string GetUserMapDirectory(
        string baseDir)
    {
        return Path.Combine(
            baseDir,
            "User",
            "Config",
            "Launcher-Backups",
            MapFolderName);
    }

    /// <summary>
    /// Same lookup order as the stock XML matcher: the install's Launcher\Stock
    /// folder first, then the Stock folder beside the running exe.
    /// </summary>
    private static string GetStockMapDirectory(
        string baseDir)
    {
        string installStockDirectory =
            Path.Combine(
                baseDir,
                "Launcher",
                "Stock",
                MapFolderName);

        if (Directory.Exists(installStockDirectory))
            return installStockDirectory;

        return Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Stock",
            MapFolderName);
    }

    /// <summary>
    /// When a user edits a stock map, the JSON is saved to the user folder but still
    /// names the stock image. Copy that image beside the user JSON so the user map is
    /// self-contained and survives future stock image changes.
    /// </summary>
    private static void CopyStockImageToUserFolder(
        string baseDir,
        string? imageFileName,
        string userMapDirectory)
    {
        string fileName =
            Path.GetFileName(
                imageFileName ?? "");

        if (string.IsNullOrWhiteSpace(fileName) ||
            !IsSupportedImageExtension(
                Path.GetExtension(fileName)))
        {
            return;
        }

        string userImagePath =
            Path.Combine(
                userMapDirectory,
                fileName);

        if (File.Exists(userImagePath))
            return;

        string stockImagePath =
            Path.Combine(
                GetStockMapDirectory(baseDir),
                fileName);

        if (!File.Exists(stockImagePath))
            return;

        File.Copy(
            stockImagePath,
            userImagePath,
            overwrite: false);

        // File.Copy keeps the source attributes. Clear read-only so the user copy
        // can be replaced or deleted later.
        File.SetAttributes(
            userImagePath,
            FileAttributes.Normal);
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