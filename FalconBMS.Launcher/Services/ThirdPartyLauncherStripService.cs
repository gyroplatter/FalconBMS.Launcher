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
/// Resolves the Community Tools strip from stock tool definitions plus the
/// users saved order, hidden tools, path overrides, and custom tools.
///
/// ThirdPartyTools.json and the ToolIcons folder remain in the same versioned
/// directory as the launcher's existing user.config.
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyList<ThirdPartyToolItem> _stockTools;

    public ThirdPartyLauncherStripService()
    {
        _stockTools = LoadStockTools();

        if (_stockTools.Count == 0)
        {
            DebugDiagnosticsService.Warn(
                "Community Tools stored settings are unavailable. Community Tools changes will not be saved. Controls and Falcon BMS launching are unaffected.");
        }
    }

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

    private static string StockToolsJsonPath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Stock",
            "ThirdPartyTools.json");

    private static string IconCacheDirectory =>
        Path.Combine(
            RootDirectory,
            "ToolIcons");

    /// <summary>
    /// Builds the complete runtime list. Hidden stock tools stay in this list
    /// with IsVisible=false so their position is preserved.
    /// </summary>
    public IReadOnlyList<ThirdPartyToolItem> LoadTools()
    {
        ThirdPartyToolsUserState userState =
            LoadUserState();

        var tools =
            new List<ThirdPartyToolItem>();

        foreach (ThirdPartyToolItem stockTool in _stockTools)
        {
            ThirdPartyToolItem tool =
                CloneTool(stockTool);

            tool.IsVisible =
                !ContainsId(
                    userState.Hidden,
                    tool.Id);

            string? pathOverride =
                FindOverride(
                    userState.Overrides,
                    tool.Id);

            if (pathOverride is not null)
                tool.ExecutablePath = pathOverride;

            LoadCachedIcon(tool);

            tools.Add(tool);
        }

        foreach (ThirdPartyCustomTool customTool in userState.Custom)
        {
            if (string.IsNullOrWhiteSpace(customTool.Id) ||
                string.IsNullOrWhiteSpace(customTool.ExecutablePath) ||
                FindStockTool(customTool.Id) is not null ||
                tools.Any(tool =>
                    string.Equals(
                        tool.Id,
                        customTool.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var tool =
                new ThirdPartyToolItem
                {
                    Id = customTool.Id,
                    DisplayName = customTool.DisplayName,
                    ExecutablePath = customTool.ExecutablePath,
                    IsVisible = true
                };

            LoadCachedIcon(tool);

            tools.Add(tool);
        }

        return ApplySavedOrder(
            tools,
            userState.Order);
    }

    /// <summary>
    /// Builds a custom Community Tool from a user selected EXE
    /// </summary>
    public ThirdPartyToolItem? TryCreateTool(
        string executablePath,
        IEnumerable<ThirdPartyToolItem> existingTools,
        string? bmsBaseDirectory,
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
                string? existingPath =
                    ResolveExecutablePath(
                        tool,
                        bmsBaseDirectory);

                if (existingPath is null ||
                    string.IsNullOrWhiteSpace(existingPath))
                {
                    return false;
                }

                try
                {
                    return string.Equals(
                        NormalizePath(existingPath),
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

        var tool =
            new ThirdPartyToolItem
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = ReadDisplayName(executablePath),
                ExecutablePath = executablePath,
                IsVisible = true
            };

        RefreshToolIcon(
            tool,
            bmsBaseDirectory);

        return tool;
    }

    /// <summary>
    /// Finds a stock tool that should consume the selected EXE rather than
    /// allowing Add to create a duplicate custom tile.
    ///
    /// Hidden stock tools can be restored, and a visible stock tool with no
    /// mapped path, like a new TrackIR shortcut, can be mapped in place
    /// </summary>
    public ThirdPartyToolItem? FindStockToolForExecutable(
        IEnumerable<ThirdPartyToolItem> tools,
        string executablePath)
    {
        string executableName =
            Path.GetFileName(executablePath);

        return tools.FirstOrDefault(tool =>
            IsStockTool(tool) &&
            (!tool.IsVisible ||
             string.IsNullOrWhiteSpace(tool.ExecutablePath)) &&
            string.Equals(
                GetExpectedExecutableName(tool),
                executableName,
                StringComparison.OrdinalIgnoreCase));
    }

    public bool IsStockTool(
        ThirdPartyToolItem tool) =>
        FindStockTool(tool.Id) is not null;

    public string? GetExpectedExecutableName(
        ThirdPartyToolItem tool)
    {
        ThirdPartyToolItem? stockTool =
            FindStockTool(tool.Id);

        if (stockTool is null ||
            string.IsNullOrWhiteSpace(stockTool.ExecutableName))
        {
            return null;
        }

        return stockTool.ExecutableName;
    }

    /// <summary>
    /// Validates a selected replacement for a stock tool and updates only its
    /// runtime path. The caller saves user state before refreshing the icon.
    /// </summary>
    public bool TrySetStockExecutablePath(
        ThirdPartyToolItem tool,
        string executablePath,
        string? bmsBaseDirectory,
        out string? errorMessage)
    {
        errorMessage = null;

        ThirdPartyToolItem? stockTool =
            FindStockTool(tool.Id);

        if (stockTool is null)
        {
            errorMessage =
                "The selected Community Tool is not a stock tool.";

            return false;
        }

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            errorMessage =
                "The selected executable could not be found.";

            return false;
        }

        if (!string.IsNullOrWhiteSpace(stockTool.ExecutableName) &&
            !string.Equals(
                Path.GetFileName(executablePath),
                stockTool.ExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            errorMessage =
                $"Select {stockTool.ExecutableName}.";

            return false;
        }

        try
        {
            tool.ExecutablePath =
                GetPortableExecutablePath(
                    executablePath,
                    bmsBaseDirectory);

            return true;
        }
        catch
        {
            errorMessage =
                "The selected executable path is not valid.";

            return false;
        }
    }

    /// <summary>
    /// Saves only user-owned state. Stock tool definitions remain in
    /// Stock\ThirdPartyTools.json.
    ///
    /// If stock definitions could not be loaded, do not rewrite the user file.
    /// A partial runtime list must never erase saved stock state.
    /// </summary>
    public bool SaveTools(
        IEnumerable<ThirdPartyToolItem> tools,
        out string? errorMessage)
    {
        errorMessage = null;

        if (_stockTools.Count == 0)
        {
            errorMessage =
                "Community Tools could not be changed because the stock tool definitions are unavailable.";

            DebugDiagnosticsService.Warn(
                "Community Tools save blocked because stock definitions are unavailable.");

            return false;
        }

        List<ThirdPartyToolItem> currentTools =
            tools.ToList();

        ThirdPartyToolsUserState existingState =
            LoadUserState();

        var userState =
            new ThirdPartyToolsUserState
            {
                Version = 1,
                Order = currentTools
                    .Where(tool =>
                        !string.IsNullOrWhiteSpace(tool.Id))
                    .Select(tool => tool.Id)
                    .ToList()
            };

        foreach (ThirdPartyToolItem tool in currentTools)
        {
            ThirdPartyToolItem? stockTool =
                FindStockTool(tool.Id);

            if (stockTool is not null)
            {
                if (!tool.IsVisible)
                    userState.Hidden.Add(tool.Id);

                if (!StoredPathsEqual(
                        tool.ExecutablePath,
                        stockTool.ExecutablePath))
                {
                    userState.Overrides[tool.Id] =
                        tool.ExecutablePath;
                }

                continue;
            }

            userState.Custom.Add(
                new ThirdPartyCustomTool
                {
                    Id = tool.Id,
                    DisplayName = tool.DisplayName,
                    ExecutablePath = tool.ExecutablePath
                });
        }

        HashSet<string> currentIds =
            new(
                currentTools
                    .Where(tool =>
                        !string.IsNullOrWhiteSpace(tool.Id))
                    .Select(tool => tool.Id),
                StringComparer.OrdinalIgnoreCase);

        // Keep state belonging to a stock ID that is not present in the
        // current stock file. If that stock shortcut returns in a later release,
        // its hidden state and path override are still available.
        foreach (string hiddenId in existingState.Hidden)
        {
            if (!currentIds.Contains(hiddenId) &&
                !ContainsId(
                    userState.Hidden,
                    hiddenId))
            {
                userState.Hidden.Add(
                    hiddenId);
            }
        }

        foreach (KeyValuePair<string, string> pathOverride
                 in existingState.Overrides)
        {
            if (currentIds.Contains(pathOverride.Key))
                continue;

            if (userState.Overrides.Keys.Any(id =>
                    string.Equals(
                        id,
                        pathOverride.Key,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            userState.Overrides[pathOverride.Key] =
                pathOverride.Value;
        }

        return SaveUserState(
            userState,
            out errorMessage);
    }

    /// <summary>
    /// Resolves an absolute custom/override path directly, or a relative stock
    /// path against the selected BMS installation.
    /// </summary>
    public string? ResolveExecutablePath(
        ThirdPartyToolItem tool,
        string? bmsBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(tool.ExecutablePath))
            return null;

        if (Path.IsPathRooted(tool.ExecutablePath))
            return tool.ExecutablePath;

        if (string.IsNullOrWhiteSpace(bmsBaseDirectory))
            return null;

        try
        {
            return Path.GetFullPath(
                Path.Combine(
                    bmsBaseDirectory,
                    tool.ExecutablePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads an existing cached icon or extracts one from the resolved EXE
    /// </summary>
    public void EnsureToolIcon(
        ThirdPartyToolItem tool,
        string? bmsBaseDirectory)
    {
        if (tool.IconSource is not null)
            return;

        LoadCachedIcon(tool);

        if (tool.IconSource is not null)
            return;

        string? executablePath =
            ResolveExecutablePath(
                tool,
                bmsBaseDirectory);

        if (executablePath is null ||
            string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            return;
        }

        ExtractAndCacheIcon(
            executablePath,
            tool.Id);

        LoadCachedIcon(tool);
    }

    /// <summary>
    /// Rebuilds the cached icon after a stock tool is remapped
    /// </summary>
    public void RefreshToolIcon(
        ThirdPartyToolItem tool,
        string? bmsBaseDirectory)
    {
        DeleteCachedIcon(tool);

        EnsureToolIcon(
            tool,
            bmsBaseDirectory);
    }

    /// <summary>
    /// Deletes the Launcher-owned cached PNG for one tool
    /// </summary>
    public void DeleteCachedIcon(
        ThirdPartyToolItem tool)
    {
        string iconPath =
            GetIconPath(tool.Id);

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

        tool.IconSource = null;
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
    /// Maps only the stock TrackIR tile. TrackIR added later through Customize
    /// remains a normal custom tool.
    /// </summary>
    public bool TryMapSeededTrackIr(
        ThirdPartyToolItem tool,
        string executablePath,
        out string? errorMessage)
    {
        if (!tool.IsSeededTrackIr ||
            !IsStockTool(tool))
        {
            errorMessage =
                "Only the default TrackIR shortcut can use automatic setup.";

            return false;
        }

        return TrySetStockExecutablePath(
            tool,
            executablePath,
            bmsBaseDirectory: null,
            out errorMessage);
    }

    public void ClearSeededTrackIrMapping(
        ThirdPartyToolItem tool)
    {
        ThirdPartyToolItem? stockTool =
            FindStockTool(tool.Id);

        tool.ExecutablePath =
            stockTool?.ExecutablePath ?? "";

        DeleteCachedIcon(tool);
    }

    private IReadOnlyList<ThirdPartyToolItem> LoadStockTools()
    {
        try
        {
            if (!File.Exists(StockToolsJsonPath))
            {
                DebugDiagnosticsService.Warn(
                    $"Community Tools stock file not found: {StockToolsJsonPath}");

                return Array.Empty<ThirdPartyToolItem>();
            }

            string json =
                File.ReadAllText(
                    StockToolsJsonPath);

            return JsonSerializer.Deserialize<List<ThirdPartyToolItem>>(
                       json,
                       SerializerOptions)
                   ?? new List<ThirdPartyToolItem>();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.LoadStockTools failed");

            return Array.Empty<ThirdPartyToolItem>();
        }
    }

    private ThirdPartyToolsUserState LoadUserState()
    {
        if (!File.Exists(ToolsJsonPath))
            return new ThirdPartyToolsUserState();

        try
        {
            string json =
                File.ReadAllText(
                    ToolsJsonPath);

            if (json.TrimStart().StartsWith(
                    "[",
                    StringComparison.Ordinal))
            {
                ThirdPartyToolsUserState migratedState =
                    MigrateLegacyUserState(json);

                BackupLegacyUserFile();

                if (SaveUserState(
                        migratedState,
                        out _))
                {
                    DebugDiagnosticsService.Info(
                        "Community Tools user file migrated to layered format.");
                }

                return migratedState;
            }

            return JsonSerializer.Deserialize<ThirdPartyToolsUserState>(
                       json,
                       SerializerOptions)
                   ?? new ThirdPartyToolsUserState();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.LoadUserState failed");

            PreserveCorruptUserFile();

            return new ThirdPartyToolsUserState();
        }
    }

    private static void BackupLegacyUserFile()
    {
        try
        {
            if (!File.Exists(ToolsJsonPath))
                return;

            string backupPath =
                Path.Combine(
                    RootDirectory,
                    "ThirdPartyTools.legacy.bak");

            if (File.Exists(backupPath))
                return;

            File.Copy(
                ToolsJsonPath,
                backupPath,
                overwrite: false);

            DebugDiagnosticsService.Info(
                $"Legacy Community Tools user file backed up: {backupPath}");
        }
        catch (Exception ex)
        {
            // The backup is rollback protection only. A backup failure must not
            // interfere with Community Tools loading or any core Launcher feature.
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.BackupLegacyUserFile failed");
        }
    }

    /// <summary>
    /// Converts the current full-list JSON format once so existing custom tools
    /// and TrackIR mappings survive the move to layered user state
    /// </summary>
    private ThirdPartyToolsUserState MigrateLegacyUserState(
        string json)
    {
        List<LegacyThirdPartyToolItem> legacyTools =
            JsonSerializer.Deserialize<List<LegacyThirdPartyToolItem>>(
                json,
                SerializerOptions)
            ?? new List<LegacyThirdPartyToolItem>();

        var userState =
            new ThirdPartyToolsUserState();

        foreach (LegacyThirdPartyToolItem legacyTool in legacyTools)
        {
            if (string.IsNullOrWhiteSpace(legacyTool.Id))
                continue;

            ThirdPartyToolItem? stockTool =
                FindStockTool(legacyTool.Id);

            if (stockTool is null)
            {
                if (!legacyTool.IsVisible)
                    continue;

                userState.Order.Add(
                    legacyTool.Id);

                userState.Custom.Add(
                    new ThirdPartyCustomTool
                    {
                        Id = legacyTool.Id,
                        DisplayName = legacyTool.DisplayName,
                        ExecutablePath = legacyTool.ExecutablePath
                    });

                continue;
            }

            userState.Order.Add(
                stockTool.Id);

            if (!legacyTool.IsVisible)
                userState.Hidden.Add(stockTool.Id);

            if (!StoredPathsEqual(
                    legacyTool.ExecutablePath,
                    stockTool.ExecutablePath))
            {
                userState.Overrides[stockTool.Id] =
                    legacyTool.ExecutablePath;
            }
        }

        return userState;
    }

    private bool SaveUserState(
        ThirdPartyToolsUserState userState,
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
                    userState,
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
                "ThirdPartyLauncherStripService.SaveUserState failed");

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

    private static void PreserveCorruptUserFile()
    {
        try
        {
            if (!File.Exists(ToolsJsonPath))
                return;

            string backupPath =
                Path.Combine(
                    RootDirectory,
                    $"ThirdPartyTools.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bad.json");

            File.Move(
                ToolsJsonPath,
                backupPath);

            DebugDiagnosticsService.Warn(
                $"Corrupt Community Tools user file preserved: {backupPath}");
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                "ThirdPartyLauncherStripService.PreserveCorruptUserFile failed");
        }
    }

    private ThirdPartyToolItem? FindStockTool(
        string id) =>
        _stockTools.FirstOrDefault(tool =>
            string.Equals(
                tool.Id,
                id,
                StringComparison.OrdinalIgnoreCase));

    private static ThirdPartyToolItem CloneTool(
        ThirdPartyToolItem source) =>
        new()
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            ExecutableName = source.ExecutableName,
            ExecutablePath = source.ExecutablePath,
            IsVisible = true
        };

    private static IReadOnlyList<ThirdPartyToolItem> ApplySavedOrder(
        IEnumerable<ThirdPartyToolItem> tools,
        IEnumerable<string> savedOrder)
    {
        List<ThirdPartyToolItem> remaining =
            tools.ToList();

        var ordered =
            new List<ThirdPartyToolItem>();

        foreach (string id in savedOrder)
        {
            int index =
                remaining.FindIndex(tool =>
                    string.Equals(
                        tool.Id,
                        id,
                        StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                continue;

            ordered.Add(
                remaining[index]);

            remaining.RemoveAt(
                index);
        }

        // New stock definitions are not in an older saved order, so they
        // naturally appear at the end without disturbing the user's order.
        ordered.AddRange(
            remaining);

        return ordered;
    }

    private static bool ContainsId(
        IEnumerable<string> ids,
        string id) =>
        ids.Any(candidate =>
            string.Equals(
                candidate,
                id,
                StringComparison.OrdinalIgnoreCase));

    private static string? FindOverride(
        IEnumerable<KeyValuePair<string, string>> overrides,
        string id)
    {
        foreach (KeyValuePair<string, string> item in overrides)
        {
            if (string.Equals(
                    item.Key,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }

    private static string GetPortableExecutablePath(
        string executablePath,
        string? bmsBaseDirectory)
    {
        string fullExecutablePath =
            NormalizePath(executablePath);

        if (bmsBaseDirectory is null ||
            string.IsNullOrWhiteSpace(bmsBaseDirectory))
        {
            return fullExecutablePath;
        }

        string fullBaseDirectory =
            NormalizePath(bmsBaseDirectory);

        string basePrefix =
            fullBaseDirectory +
            Path.DirectorySeparatorChar;

        if (!fullExecutablePath.StartsWith(
                basePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return fullExecutablePath;
        }

        return fullExecutablePath.Substring(
            basePrefix.Length);
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

            if (installDirectory is null ||
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

    private static void ExtractAndCacheIcon(
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
                return;
            }

            var bitmapSource =
                Imaging.CreateBitmapSourceFromHIcon(
                    selectedIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();

            Directory.CreateDirectory(
                IconCacheDirectory);

            string iconPath =
                GetIconPath(id);

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
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(
                ex,
                $"ThirdPartyLauncherStripService.ExtractAndCacheIcon failed: {executablePath}");
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

        string iconPath =
            GetIconPath(tool.Id);

        if (!File.Exists(iconPath))
            return;

        try
        {
            var bitmap =
                new BitmapImage();

            bitmap.BeginInit();
            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;
            bitmap.CreateOptions =
                BitmapCreateOptions.IgnoreImageCache;
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

    private static string GetIconPath(
        string id) =>
        Path.Combine(
            IconCacheDirectory,
            id + ".png");

    private static bool StoredPathsEqual(
        string? left,
        string? right)
    {
        string normalizedLeft =
            (left ?? "")
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar)
                .Trim();

        string normalizedRight =
            (right ?? "")
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar)
                .Trim();

        return string.Equals(
            normalizedLeft,
            normalizedRight,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(
        string path) =>
        Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

    private sealed class LegacyThirdPartyToolItem
    {
        public string Id { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public string ExecutablePath { get; set; } = "";

        public bool IsVisible { get; set; } = true;
    }
}