namespace FalconBMS.Launcher.Models;

/// <summary>
/// Enum for the Launcher's main navigation tabs.
/// </summary>

public enum LauncherTab
{
    Main = 0,
    Views = 1,
#if DEBUG
    Styles = 2,
#endif
}