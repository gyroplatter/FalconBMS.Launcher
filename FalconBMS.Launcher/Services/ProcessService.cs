using System;
using System.Diagnostics;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Starts FalconBMS, updater, or other executables and opens URLs.
/// </summary>
public sealed class ProcessService
{
    public bool IsUpdaterRunning()
    {
        bool running = Process.GetProcessesByName("Updater").Length > 0;
        DebugDiagnosticsService.Info($"Updater running check: {running}");
        return running;
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
}