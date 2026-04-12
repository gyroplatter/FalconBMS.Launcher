namespace FalconBMS.Launcher.Models;

/// <summary>
/// Record for a launcher strip button/item, including label and execution metadata.
/// </summary>

public sealed record LauncherStripItem(
    string Id,
    string Label,
    string ExePath,
    string? WorkingDirectory,
    bool MinimizeLauncherUntilExit,
    string? DownloadUrl = null,
    string? ExpectedExeName = null);