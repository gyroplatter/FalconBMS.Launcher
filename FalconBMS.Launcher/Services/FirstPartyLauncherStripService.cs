using FalconBMS.Launcher.Models;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Builds the list of built-in FalconBMS applications shown in the launcher strip.
/// </summary>

public sealed class FirstPartyLauncherStripService
{
    public IReadOnlyList<LauncherStripItem> GetItems(BmsInstall install)
    {
        var baseDir = install.BaseDir;
        var items = new List<LauncherStripItem>();

        AddIfExists(
            items,
            "updater",
            "Updater",
            Path.Combine(baseDir, "Updater.exe"),
            baseDir,
            false);

        AddIfExists(
            items,
            "config",
            "Config",
            Path.Combine(baseDir, "Config.exe"),
            baseDir,
            true);

        AddIfExists(
            items,
            "display-config",
            "Display Config",
            Path.Combine(baseDir, "Launcher", "BmsDisplayConfig.exe"),
            Path.Combine(baseDir, "Launcher"),
            true);

        AddIfExists(
            items,
            "rtt-client",
            "RTT Client",
            Path.Combine(baseDir, "Tools", "RTTRemote", "RTTClient64.exe"),
            Path.Combine(baseDir, "Tools", "RTTRemote"),
            false);

        AddIfExists(
            items,
            "rtt-server",
            "RTT Server",
            Path.Combine(baseDir, "Tools", "RTTRemote", "RTTServer64.exe"),
            Path.Combine(baseDir, "Tools", "RTTRemote"),
            false);

        AddIfExists(
            items,
            "ivc-client",
            "IVC Client",
            FirstExisting(
                Path.Combine(baseDir, "Bin", "x64", "IVC", "IVC Client.exe"),
                Path.Combine(baseDir, "Bin", "x86", "IVC", "IVC Client.exe")),
            FirstExistingDirectory(
                Path.Combine(baseDir, "Bin", "x64", "IVC", "IVC Client.exe"),
                Path.Combine(baseDir, "Bin", "x86", "IVC", "IVC Client.exe")),
            false);

        AddIfExists(
            items,
            "ivc-server",
            "IVC Server",
            FirstExisting(
                Path.Combine(baseDir, "Bin", "x64", "IVC", "IVC Server.exe"),
                Path.Combine(baseDir, "Bin", "x86", "IVC", "IVC Server.exe")),
            FirstExistingDirectory(
                Path.Combine(baseDir, "Bin", "x64", "IVC", "IVC Server.exe"),
                Path.Combine(baseDir, "Bin", "x86", "IVC", "IVC Server.exe")),
            false);

        AddIfExists(
            items,
            "avionics-configurator",
            "Avionics Configurator",
            Path.Combine(baseDir, "Bin", "x86", "Avionics Configurator.exe"),
            Path.Combine(baseDir, "Bin", "x86"),
            true);

        AddIfExists(
            items,
            "editor",
            "Editor",
            FirstExisting(
                Path.Combine(baseDir, "Bin", "x64", "Editor.exe"),
                Path.Combine(baseDir, "Bin", "x86", "Editor.exe")),
            FirstExistingDirectory(
                Path.Combine(baseDir, "Bin", "x64", "Editor.exe"),
                Path.Combine(baseDir, "Bin", "x86", "Editor.exe")),
            false);

        return items;
    }

    public LauncherStripItem? GetItem(BmsInstall install, string id) =>
        GetItems(install).FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    private static void AddIfExists(
        IList<LauncherStripItem> items,
        string id,
        string label,
        string? exePath,
        string? workingDirectory,
        bool minimizeLauncherUntilExit)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        items.Add(new LauncherStripItem(
            id,
            label,
            exePath,
            workingDirectory,
            minimizeLauncherUntilExit));
    }

    private static string? FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists);

    private static string? FirstExistingDirectory(params string[] candidates)
    {
        var exe = candidates.FirstOrDefault(File.Exists);
        return exe is null ? null : Path.GetDirectoryName(exe);
    }
}