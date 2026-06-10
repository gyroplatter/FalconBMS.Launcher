using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Models;

public sealed class LegacyImportScanResult
{
    public string ConfigDirectory { get; init; } = "";

    public string? F16AutoKeyPath { get; init; }

    public string? F15AutoKeyPath { get; init; }

    public string? DeviceSortingPath { get; init; }

    public string? UserCfgPath { get; init; }

    public List<LegacyImportDeviceScanResult> Devices { get; } = new();

    public List<string> Warnings { get; } = new();

    public bool HasF16Controls =>
        !string.IsNullOrWhiteSpace(F16AutoKeyPath);

    public bool HasF15Controls =>
        !string.IsNullOrWhiteSpace(F15AutoKeyPath);

    public bool HasAnyAutoKey =>
        HasF16Controls || HasF15Controls;

    public int ConfiguredDeviceCount => Devices.Count;

    public int StockFallbackCount =>
        Devices.Count(device => device.WillUseStockFallback);

    public int UnusableDeviceCount =>
        Devices.Count(device => device.CannotImport);
}