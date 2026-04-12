namespace FalconBMS.Launcher.Models;

/// <summary>
/// Container that holds information about a FalconBMS version installation
/// </summary>

public sealed class BmsInstall
{
    public required string RegistryKeyName { get; init; }
    public required string BaseDir { get; init; }
    public required string FalconExePath { get; init; }
    public required string VersionDisplay { get; init; }

    public string DisplayName => VersionDisplay;
}