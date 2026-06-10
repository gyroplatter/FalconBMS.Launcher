namespace FalconBMS.Launcher.Models;

public sealed class LegacyImportDeviceScanResult
{
    public string DeviceName { get; init; } = "";

    public string LegacyXmlPath { get; init; } = "";

    public bool LegacyXmlIsReadable { get; init; }

    public bool HasMatchingStockXml { get; init; }

    public string? StockXmlPath { get; init; }

    public bool WillUseStockFallback =>
        !LegacyXmlIsReadable &&
        HasMatchingStockXml;

    public bool CannotImport =>
        !LegacyXmlIsReadable &&
        !HasMatchingStockXml;

    public string StatusText
    {
        get
        {
            if (LegacyXmlIsReadable)
                return "Existing device settings found";

            if (WillUseStockFallback)
                return "Existing device settings could not be read. Stock profile will be used.";

            return "Existing device settings could not be read and no stock profile was found.";
        }
    }
}