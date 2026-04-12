using FalconBMS.Launcher.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Finds FalconBMS installs and builds install records for the UI.
/// </summary>
public sealed class InstallDiscoveryService
{
    private const string Root = @"SOFTWARE\WOW6432Node\Benchmark Sims";

    public IReadOnlyList<BmsInstall> Discover()
    {
        DebugDiagnosticsService.Info("Start Reading Registry.");

        using var rootKey = Registry.LocalMachine.OpenSubKey(Root, writable: false);
        if (rootKey is null)
        {
            DebugDiagnosticsService.Warn($"Benchmark Sims registry root not found: HKLM\\{Root}");
            return Array.Empty<BmsInstall>();
        }

        var installs = new List<BmsInstall>();

        foreach (var subName in rootKey.GetSubKeyNames())
        {
            try
            {
                using var k = rootKey.OpenSubKey(subName, writable: false);
                var baseDir = k?.GetValue("baseDir") as string;

                if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                {
                    DebugDiagnosticsService.Warn($"Skipping install '{subName}' because baseDir is missing or invalid: {baseDir}");
                    continue;
                }

                var exe = ResolveFalconExe(baseDir);
                if (exe is null)
                {
                    DebugDiagnosticsService.Warn($"Skipping install '{subName}' because Falcon BMS.exe was not found under: {baseDir}");
                    continue;
                }

                var versionDisplay = BuildVersionDisplayFromExe(subName, exe);

                installs.Add(new BmsInstall
                {
                    RegistryKeyName = subName,
                    BaseDir = baseDir,
                    FalconExePath = exe,
                    VersionDisplay = versionDisplay
                });

                DebugDiagnosticsService.Info($"Discovered install '{subName}' at '{baseDir}' using EXE '{exe}'");
                DebugDiagnosticsService.Info($"BMS EXE version info: {versionDisplay}");
            }
            catch (Exception ex)
            {
                DebugDiagnosticsService.Exception(ex, $"Error while reading install '{subName}'");
            }
        }

        installs.Sort((a, b) => string.Compare(b.RegistryKeyName, a.RegistryKeyName, StringComparison.OrdinalIgnoreCase));

        DebugDiagnosticsService.Info($"Finished Reading Registry. Install count: {installs.Count}");
        return installs;
    }

    private static string? ResolveFalconExe(string baseDir)
    {
        var x64 = Path.Combine(baseDir, "Bin", "x64", "Falcon BMS.exe");
        if (File.Exists(x64)) return x64;

        var x86 = Path.Combine(baseDir, "Bin", "x86", "Falcon BMS.exe");
        if (File.Exists(x86)) return x86;

        var bin = Path.Combine(baseDir, "Bin", "Falcon BMS.exe");
        if (File.Exists(bin)) return bin;

        return null;
    }

    private static string BuildVersionDisplayFromExe(string registryKeyName, string falconExePath)
    {
        var isInternal = registryKeyName.IndexOf("(Internal)", StringComparison.OrdinalIgnoreCase) >= 0;

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(falconExePath);

            var major = vi.FileMajorPart;
            var minor = vi.FileMinorPart;
            var build = vi.FileBuildPart;
            var patch = vi.FilePrivatePart;

            if (isInternal)
                return $"{major}.{minor}.{build} (Internal Build {patch})";

            return $"{major}.{minor}.{build}";
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Exception(ex, $"Failed reading EXE version info from '{falconExePath}'");
            return registryKeyName;
        }
    }
}