namespace FalconBMS.Launcher.Models;

public sealed class LegacyImportSkippedItem
{
    public string SourceName { get; init; } = "";

    public string ControlName { get; init; } = "";

    public string Reason { get; init; } = "";
}