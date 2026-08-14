using FalconBMS.Launcher.Models;
using Microsoft.Win32;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Loads and saves the user-editable Community Tools strip.
///
/// ThirdPartyTools.json and the ToolIcons folder are stored in the same
/// versioned directory as the launcher's existing user.config.
/// </summary>
public sealed class ThirdPartyLauncherStripService
{

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        IntPtr[] largeIcons,
        IntPtr[] smallIcons,
        uint iconCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string RootDirectory
    {
        get
        {
            string userConfigPath = ConfigurationManager
                .OpenExeConfiguration(
                    ConfigurationUserLevel.PerUserRoamingAndLocal)
                .FilePath;

            return Path.GetDirectoryName(userConfigPath)
                ?? throw new InvalidOperationException(
                    "Could not determine the launcher user.config directory.");
        }
    }

    private static string ToolsJsonPath =>
        Path.Combine(
            RootDirectory,
            "ThirdPartyTools.json");

    private static string IconCacheDirectory =>
        Path.Combine(
            RootDirectory,
            "ToolIcons");

    /// <summary>
    /// Loads saved applications in display order.
    ///
    /// When no JSON exists, this creates a new list containing only the
    /// seeded, initially-unmapped TrackIR item.
    /// </summary>
    public IReadOnlyList<ThirdPartyToolItem> LoadTools()
    {
        if (!File.Exists(ToolsJsonPath))
        {
            var defaultTools = new List<ThirdPartyToolItem>
            {
                new()
                {
                    Id = "trackir",
                    DisplayName = "TrackIR",
                    ExecutablePath = "",
                    IconCacheFileName = null,
                    IsSeededTrackIr = true
                }
            };

            SaveTools(
                defaultTools,
                out _);

            return defaultTools;
        }

        try
        {
            string json =
                File.ReadAllText(
                    ToolsJsonPath);

            var tools =
                JsonSerializer.Deserialize<List<ThirdPartyToolItem>>(
                    json,
                    SerializerOptions)
                ?? new List<ThirdPartyToolItem>();

            foreach (ThirdPartyToolItem tool in tools)
                LoadCachedIcon(tool);

            return tools;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.LoadTools failed");

            return Array.Empty<ThirdPartyToolItem>();
        }
    }

    /// <summary>
    /// Builds a Community Tool from a user-selected EXE.
    /// </summary>
    public ThirdPartyToolItem? TryCreateTool(
        string executablePath,
        IEnumerable<ThirdPartyToolItem> existingTools,
        out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            errorMessage =
                "The selected executable could not be found.";

            return null;
        }

        if (!string.Equals(
                Path.GetExtension(executablePath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                "Select an executable file ending in .exe.";

            return null;
        }

        string normalizedSelectedPath;

        try
        {
            normalizedSelectedPath =
                NormalizePath(executablePath);
        }
        catch
        {
            errorMessage =
                "The selected executable path is not valid.";

            return null;
        }

        bool alreadyAdded =
            existingTools.Any(tool =>
            {
                if (string.IsNullOrWhiteSpace(tool.ExecutablePath))
                    return false;

                try
                {
                    return string.Equals(
                        NormalizePath(tool.ExecutablePath),
                        normalizedSelectedPath,
                        StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            });

        if (alreadyAdded)
        {
            errorMessage =
                "That application has already been added.";

            return null;
        }

        string id =
            Guid.NewGuid().ToString("N");

        string? iconCacheFileName =
            ExtractAndCacheIcon(
                executablePath,
                id);

        var tool =
            new ThirdPartyToolItem
            {
                Id = id,
                DisplayName =
                    ReadDisplayName(executablePath),
                ExecutablePath =
                    executablePath,
                IconCacheFileName =
                    iconCacheFileName,
                IsSeededTrackIr =
                    false
            };

        LoadCachedIcon(tool);

        return tool;
    }

    /// <summary>
    /// Saves the list in its current display order.
    /// </summary>
    public bool SaveTools(
        IEnumerable<ThirdPartyToolItem> tools,
        out string? errorMessage)
    {
        errorMessage = null;
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(
                RootDirectory);

            string json =
                JsonSerializer.Serialize(
                    tools.ToList(),
                    SerializerOptions);

            temporaryPath =
                ToolsJsonPath + ".tmp";

            File.WriteAllText(
                temporaryPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.Copy(
                temporaryPath,
                ToolsJsonPath,
                overwrite: true);

            File.Delete(
                temporaryPath);

            temporaryPath = null;

            return true;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.SaveTools failed");

            errorMessage =
                "The Community Tools list could not be saved.";

            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the original save failure.
                }
            }
        }
    }

    /// <summary>
    /// Deletes the cached PNG owned by a removed user application.
    /// </summary>
    public void DeleteCachedIcon(
        ThirdPartyToolItem tool)
    {
        if (string.IsNullOrWhiteSpace(tool.IconCacheFileName))
            return;

        string iconPath =
            Path.Combine(
                IconCacheDirectory,
                tool.IconCacheFileName);

        try
        {
            if (File.Exists(iconPath))
                File.Delete(iconPath);
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"ThirdPartyLauncherStripService.DeleteCachedIcon failed: {iconPath}");
        }
    }

    /// <summary>
    /// Tries only targeted TrackIR locations. This intentionally does not
    /// enumerate installed applications or search the filesystem.
    /// </summary>
    public string? TryFindTrackIrExecutable()
    {
        string? registryCandidate =
            TryReadTrackIrExecutableFromRegistry();

        if (!string.IsNullOrWhiteSpace(registryCandidate))
            return registryCandidate;

        string[] programFilesRoots =
        {
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles)
        };

        foreach (string root in programFilesRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate =
                Path.Combine(
                    root,
                    "NaturalPoint",
                    "TrackIR5",
                    "TrackIR5.exe");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Maps only the seeded TrackIR tile. TrackIR added later through Customize
    /// never calls this path and remains a normal custom tool.
    /// </summary>
    public bool TryMapSeededTrackIr(
        ThirdPartyToolItem tool,
        string executablePath,
        out string? errorMessage)
    {
        errorMessage = null;

        if (!tool.IsSeededTrackIr)
        {
            errorMessage =
                "Only the default TrackIR shortcut can use automatic setup.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            errorMessage =
                "The TrackIR executable could not be found.";

            return false;
        }

        if (!string.Equals(
                Path.GetFileName(executablePath),
                "TrackIR5.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                "Select TrackIR5.exe.";

            return false;
        }

        DeleteCachedIcon(tool);

        tool.ExecutablePath =
            executablePath;

        tool.IconCacheFileName =
            ExtractAndCacheIcon(
                executablePath,
                tool.Id);

        LoadCachedIcon(tool);

        return true;
    }

    public void ClearSeededTrackIrMapping(
        ThirdPartyToolItem tool)
    {
        DeleteCachedIcon(tool);

        tool.ExecutablePath = "";
        tool.IconCacheFileName = null;
        tool.IconSource = null;
    }

    private static string? TryReadTrackIrExecutableFromRegistry()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\NaturalPoint\NATURALPOINT\NPClient Location",
                    writable: false);

            string? installDirectory =
                key?.GetValue("Path") as string;

            if (installDirectory == null ||
                string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            string candidate =
                Path.Combine(
                    installDirectory.Trim().Trim('"'),
                    "TrackIR5.exe");

            return File.Exists(candidate)
                ? candidate
                : null;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "TrackIR registry detection failed");

            return null;
        }
    }

    private static string ReadDisplayName(
        string executablePath)
    {
        try
        {
            string? productName =
                FileVersionInfo
                    .GetVersionInfo(executablePath)
                    .ProductName;

            if (!string.IsNullOrWhiteSpace(productName))
                return productName.Trim();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"ThirdPartyLauncherStripService.ReadDisplayName failed: {executablePath}");
        }

        return Path.GetFileNameWithoutExtension(
            executablePath);
    }

    private static string? ExtractAndCacheIcon(
        string executablePath,
        string id)
    {
        var largeIcons =
            new IntPtr[1];

        var smallIcons =
            new IntPtr[1];

        try
        {
            uint extractedCount =
                ExtractIconEx(
                    executablePath,
                    0,
                    largeIcons,
                    smallIcons,
                    1);

            IntPtr selectedIcon =
                largeIcons[0] != IntPtr.Zero
                    ? largeIcons[0]
                    : smallIcons[0];

            if (extractedCount == 0 ||
                selectedIcon == IntPtr.Zero)
            {
                return null;
            }

            var bitmapSource =
                Imaging.CreateBitmapSourceFromHIcon(
                    selectedIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();

            Directory.CreateDirectory(
                IconCacheDirectory);

            string iconCacheFileName =
                id + ".png";

            string iconPath =
                Path.Combine(
                    IconCacheDirectory,
                    iconCacheFileName);

            using var output =
                new FileStream(
                    iconPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

            var encoder =
                new PngBitmapEncoder();

            encoder.Frames.Add(
                BitmapFrame.Create(bitmapSource));

            encoder.Save(output);

            return iconCacheFileName;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"ThirdPartyLauncherStripService.ExtractAndCacheIcon failed: {executablePath}");

            return null;
        }
        finally
        {
            if (largeIcons[0] != IntPtr.Zero)
                DestroyIcon(largeIcons[0]);

            if (smallIcons[0] != IntPtr.Zero &&
                smallIcons[0] != largeIcons[0])
            {
                DestroyIcon(smallIcons[0]);
            }
        }
    }

    private static void LoadCachedIcon(
        ThirdPartyToolItem tool)
    {
        tool.IconSource = null;

        if (string.IsNullOrWhiteSpace(tool.IconCacheFileName))
            return;

        string iconPath =
            Path.Combine(
                IconCacheDirectory,
                tool.IconCacheFileName);

        if (!File.Exists(iconPath))
            return;

        try
        {
            var bitmap =
                new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;
            bitmap.UriSource =
                new Uri(
                    iconPath,
                    UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            tool.IconSource =
                bitmap;
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"ThirdPartyLauncherStripService.LoadCachedIcon failed: {iconPath}");
        }
    }

    private static string NormalizePath(
        string path)
    {
        return Path
            .GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }
}