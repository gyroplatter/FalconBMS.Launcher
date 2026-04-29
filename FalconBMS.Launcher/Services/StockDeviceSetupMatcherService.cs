using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Represents the result of matching a discovered input device to a stock
/// Setup.v100 XML file. Used to determine baseline bindings on first run
/// when no JSON binding file exists.
/// </summary>

public sealed class StockDeviceSetupMatcherService
{
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

        var matches = new List<StockDeviceSetupMatch>();

        foreach (InputDeviceInfo device in devices)
        {
            string? match = FindBestStockXmlMatch(device, stockFiles);

            matches.Add(new StockDeviceSetupMatch
            {
                Device = device,
                StockXmlPath = match
            });
        }

        return matches;
    }

    private static string? FindBestStockXmlMatch(InputDeviceInfo device, string[] stockFiles)
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

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}