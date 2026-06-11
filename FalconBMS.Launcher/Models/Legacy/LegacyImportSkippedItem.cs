namespace FalconBMS.Launcher.Models.Legacy;

public sealed class LegacyImportSkippedItem
{
    public string SourceName { get; init; } = "";

    public string ControlName { get; init; } = "";

    public string Reason { get; init; } = "";
}