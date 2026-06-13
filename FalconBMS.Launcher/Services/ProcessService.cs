using FalconBMS.Launcher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Starts FalconBMS, updater, or other executables and opens URLs.
/// </summary>
public sealed class ProcessService
{
    public bool IsBmsUpdaterRunning(
        IReadOnlyList<BmsInstall> installs)
    {
        if (installs.Count == 0)
        {
            DebugDiagnosticsService.Info("BMS updater running check skipped: no installs.");
            return false;
        }

        var expectedUpdaterPaths = new HashSet<string>(
            installs
                .Select(install => Path.Combine(install.BaseDir, "Updater.exe"))
                .Select(NormalizePath)
                .Where(path => !string.IsNullOrWhiteSpace(path)),
            StringComparer.OrdinalIgnoreCase);

        foreach (Process process in Process.GetProcessesByName("Updater"))
        {
            string? processPath = TryGetProcessPath(process);

            if (string.IsNullOrWhiteSpace(processPath))
            {
                DebugDiagnosticsService.Warn(
                    $"Updater process found but path could not be read. ProcessId={process.Id}");

                continue;
            }

            string normalizedProcessPath = NormalizePath(processPath!);

            bool isBmsUpdater =
                expectedUpdaterPaths.Contains(normalizedProcessPath);

            DebugDiagnosticsService.Info(
                $"Updater process found | ProcessId={process.Id} | Path=\"{processPath}\" | IsBmsUpdater={isBmsUpdater}");

            if (isBmsUpdater)
                return true;
        }

        DebugDiagnosticsService.Info("BMS updater running check: false");
        return false;
    }

    public Process StartFalcon(string exePath, string? arguments = null)
    {
        DebugDiagnosticsService.Info($"Launching EXE: {exePath} {arguments}".TrimEnd());

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(arguments))
            psi.Arguments = arguments;

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Falcon BMS.");
    }

    public void StartUpdater(string baseDir)
    {
        var updater = Path.Combine(baseDir, "Updater.exe");
        if (!File.Exists(updater))
            throw new FileNotFoundException("Updater.exe not found.", updater);

        DebugDiagnosticsService.Info($"Launching EXE: {updater}");

        var psi = new ProcessStartInfo
        {
            FileName = updater,
            WorkingDirectory = baseDir,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    public Process StartExecutable(string exePath, string? workingDirectory = null, string? arguments = null)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("Executable not found.", exePath);

        string resolvedWorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            resolvedWorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
        }
        else
        {
            resolvedWorkingDirectory = workingDirectory!;
        }

        DebugDiagnosticsService.Info($"Launching EXE: {exePath} {arguments}".TrimEnd());

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = resolvedWorkingDirectory,
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(arguments))
            psi.Arguments = arguments;

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(exePath)}.");
    }

    public void OpenUrl(string url)
    {
        DebugDiagnosticsService.Info($"Launching URL: {url}");

        var psi = new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static string NormalizePath(
        string path)
    {
        return Path
            .GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? TryGetProcessPath(
        Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException ||
            ex is System.ComponentModel.Win32Exception ||
            ex is NotSupportedException)
        {
            return null;
        }
    }
}