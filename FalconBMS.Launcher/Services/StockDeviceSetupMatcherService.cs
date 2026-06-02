using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Matches discovered input devices to stock Setup.v100 XML files.
/// Existing name/file-name matching remains the primary path. If that fails,
/// a Launcher-owned PID/VID manifest is used as a fallback for devices whose
/// names are reported differently by Wine or other HID layers.
/// </summary>
public sealed class StockDeviceSetupMatcherService
{
    private const string StockManifestFileName = "StockDeviceManifest.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<StockDeviceSetupMatch> Match(string installBaseDir, IReadOnlyList<InputDeviceInfo> devices)
    {
        string stockDir = Path.Combine(installBaseDir, "Launcher", "Stock");

        if (!Directory.Exists(stockDir))
        {
            stockDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stock");
        }

        string[] stockFiles = Directory.Exists(stockDir)
            ? Directory.GetFiles(stockDir, "Setup.v100.*.xml", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

        IReadOnlyList<StockDeviceManifestEntry> manifestEntries = LoadManifest();

        var matches = new List<StockDeviceSetupMatch>();

        foreach (InputDeviceInfo device in devices)
        {
            StockDeviceSetupMatch match = FindBestStockXmlMatch(device, stockFiles, manifestEntries, stockDir);
            matches.Add(match);
        }

        return matches;
    }

    private static StockDeviceSetupMatch FindBestStockXmlMatch(
        InputDeviceInfo device,
        string[] stockFiles,
        IReadOnlyList<StockDeviceManifestEntry> manifestEntries,
        string stockDir)
    {
        // Keep the existing Windows behavior first: detected device name to stock file name.
        string? nameMatch = FindNameMatch(device, stockFiles);

        if (nameMatch is not null)
        {
            return new StockDeviceSetupMatch
            {
                Device = device,
                StockXmlPath = nameMatch,
                MatchMethod = "Name"
            };
        }

        // Fallback for Wine/Linux or other HID layers that report a different display name
        // while preserving the same USB PID/VID.
        string? manifestMatch = FindManifestPidVidMatch(device, manifestEntries, stockDir);

        if (manifestMatch is not null)
        {
            return new StockDeviceSetupMatch
            {
                Device = device,
                StockXmlPath = manifestMatch,
                MatchMethod = "ManifestPidVid"
            };
        }

        return new StockDeviceSetupMatch
        {
            Device = device,
            StockXmlPath = null,
            MatchMethod = "None"
        };
    }

    private static string? FindNameMatch(InputDeviceInfo device, string[] stockFiles)
    {
        string normalizedProduct = Normalize(device.ProductName);
        string normalizedInstance = Normalize(device.InstanceName);

        string? productMatch = stockFiles.FirstOrDefault(path =>
            Normalize(Path.GetFileNameWithoutExtension(path)).Contains(normalizedProduct));

        if (productMatch is not null)
            return productMatch;

        string? instanceMatch = stockFiles.FirstOrDefault(path =>
            Normalize(Path.GetFileNameWithoutExtension(path)).Contains(normalizedInstance));

        return instanceMatch;
    }

    private static string? FindManifestPidVidMatch(
        InputDeviceInfo device,
        IReadOnlyList<StockDeviceManifestEntry> manifestEntries,
        string stockDir)
    {
        string devicePidVid = NormalizePidVid(device.PidVid);
        if (string.IsNullOrWhiteSpace(devicePidVid))
            return null;

        StockDeviceManifestEntry? entry = manifestEntries.FirstOrDefault(manifestEntry =>
            string.Equals(NormalizePidVid(manifestEntry.PidVid), devicePidVid, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return null;

        if (string.IsNullOrWhiteSpace(entry.StockFile))
        {
            DebugDiagnosticsService.Warn(
                $"Stock manifest entry missing stockFile. PIDVID={device.PidVid} | Device=\"{device.ProductName}\"");

            return null;
        }

        string stockPath = Path.Combine(stockDir, entry.StockFile);

        if (File.Exists(stockPath))
            return stockPath;

        DebugDiagnosticsService.Warn(
            $"Stock manifest file missing. PIDVID={device.PidVid} | Device=\"{device.ProductName}\" | File=\"{entry.StockFile}\" | Path=\"{stockPath}\"");

        return null;
    }

    private static IReadOnlyList<StockDeviceManifestEntry> LoadManifest()
    {
        string manifestPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Services",
            "Controls",
            StockManifestFileName);

        if (!File.Exists(manifestPath))
            return Array.Empty<StockDeviceManifestEntry>();

        try
        {
            string json = File.ReadAllText(manifestPath);
            List<StockDeviceManifestEntry>? entries =
                JsonSerializer.Deserialize<List<StockDeviceManifestEntry>>(json, ManifestJsonOptions);

            return entries ?? new List<StockDeviceManifestEntry>();
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Failed reading stock device manifest. Path=\"{manifestPath}\"");
            return Array.Empty<StockDeviceManifestEntry>();
        }
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string NormalizePidVid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private sealed class StockDeviceManifestEntry
    {
        public string PidVid { get; init; } = "";

        public string StockFile { get; init; } = "";

        public string DisplayName { get; init; } = "";
    }
}