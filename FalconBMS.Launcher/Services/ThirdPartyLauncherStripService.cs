using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds and manages the Launcher strip entries for user-configured third-party tools.
/// Third-party tool paths are stored using .NET user-scoped settings
/// (Properties.Settings.Default.*).
///
/// These are persisted automatically to:
/// %LOCALAPPDATA%\FalconBMS.Launcher\FalconBMS.Launcher_Url_<hash>\<version>\user.config
///
/// See Properties/Settings.settings for definitions.
/// </summary>

public sealed class ThirdPartyLauncherStripService
{
    private static readonly LauncherStripItem[] Items =
    [
        new LauncherStripItem(
            "wdp",
            "Weapon Delivery Planner",
            "",
            null,
            false,
            "http://www.weapondeliveryplanner.nl/",
            "WeaponDeliveryPlanner.exe"),

        new LauncherStripItem(
            "mission-commander",
            "Mission Commander",
            "",
            null,
            false,
            "http://www.weapondeliveryplanner.nl/",
            "Mission Commander.exe"),

        new LauncherStripItem(
            "weather-commander",
            "Weather Commander",
            "",
            null,
            false,
            "http://www.weapondeliveryplanner.nl/",
            "Weather Commander.exe"),

        new LauncherStripItem(
            "f4wx",
            "F4Wx",
            "",
            null,
            false,
            "https://forum.falcon-bms.com/topic/8267/f4wx-real-weather-converter",
            "F4Wx.exe"),

        new LauncherStripItem(
            "f4radar",
            "F4Radar",
            "",
            null,
            false,
            "https://forum.falcon-bms.com/topic/18356/f4radar-lightweight-standalone-radar-application",
            "F4Radar.exe")
    ];

    public IReadOnlyList<LauncherStripItem> GetItems() => Items;

    public LauncherStripItem? GetItem(string id) =>
        Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    public string GetSavedExecutablePath(string id) =>
        id.ToLowerInvariant() switch
        {
            "wdp" => Properties.Settings.Default.ThirdPartyWdpExePath,
            "mission-commander" => Properties.Settings.Default.ThirdPartyMissionCommanderExePath,
            "weather-commander" => Properties.Settings.Default.ThirdPartyWeatherCommanderExePath,
            "f4wx" => Properties.Settings.Default.ThirdPartyF4WxExePath,
            "f4radar" => Properties.Settings.Default.ThirdPartyF4RadarExePath,
            _ => ""
        };

    public void SaveExecutablePath(string id, string exePath)
    {
        switch (id.ToLowerInvariant())
        {
            case "wdp":
                Properties.Settings.Default.ThirdPartyWdpExePath = exePath;
                break;
            case "mission-commander":
                Properties.Settings.Default.ThirdPartyMissionCommanderExePath = exePath;
                break;
            case "weather-commander":
                Properties.Settings.Default.ThirdPartyWeatherCommanderExePath = exePath;
                break;
            case "f4wx":
                Properties.Settings.Default.ThirdPartyF4WxExePath = exePath;
                break;
            case "f4radar":
                Properties.Settings.Default.ThirdPartyF4RadarExePath = exePath;
                break;
            default:
                return;
        }

        Properties.Settings.Default.Save();
    }

    // Clear the stale saved path when EXE no longer exists
    public void ClearExecutablePath(string id)
    {
        switch (id.ToLowerInvariant())
        {
            case "wdp":
                Properties.Settings.Default.ThirdPartyWdpExePath = "";
                break;
            case "mission-commander":
                Properties.Settings.Default.ThirdPartyMissionCommanderExePath = "";
                break;
            case "weather-commander":
                Properties.Settings.Default.ThirdPartyWeatherCommanderExePath = "";
                break;
            case "f4wx":
                Properties.Settings.Default.ThirdPartyF4WxExePath = "";
                break;
            case "f4radar":
                Properties.Settings.Default.ThirdPartyF4RadarExePath = "";
                break;
            default:
                return;
        }

        Properties.Settings.Default.Save();
    }

}