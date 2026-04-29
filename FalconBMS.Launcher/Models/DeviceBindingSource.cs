namespace FalconBMS.Launcher.Models;

/// <summary>
/// Identifies where a device binding profile was initialized from.
/// JSON will become the normal source of truth later; StockXml is used
/// only as a bootstrap source when no JSON exists yet.
/// </summary>
public enum DeviceBindingSource
{
    Empty = 0,
    StockXml = 1,
    Json = 2
}