namespace FalconBMS.Launcher.Models;

public sealed class StockDeviceSetupMatch
{
    public InputDeviceInfo Device { get; init; } = new();

    public string? StockXmlPath { get; init; }

    public string MatchMethod { get; init; } = "";

    public bool HasStockXml => !string.IsNullOrWhiteSpace(StockXmlPath);
}