using System.Diagnostics;
using System.IO;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Handles pilot callsign, name, uniqueness checks, and logbook/name file creation and updating.
/// </summary>

public sealed class CallsignService
{
    public const string DefaultCallsign = "Viper";
    public const string DefaultPilotName = "Joe Pilot";

    private readonly RegistryService _registry = new();
    private readonly ProcessService _process = new();

    public string ReadPilotCallsign(string installKeyName) =>
        _registry.ReadZeroPaddedAsciiBinary(installKeyName, "PilotCallsign", DefaultCallsign);

    public string ReadPilotName(string installKeyName) =>
        _registry.ReadZeroPaddedAsciiBinary(installKeyName, "PilotName", DefaultPilotName);

    public bool IsUniqueNameDefined(string installKeyName, string baseDir)
    {
        // A pilot is considered valid only if registry values exist AND the matching <callsign>.lbk logbook exists, otherwise BMS will revert to Joe Pilot / Viper.
        byte[]? callsign = _registry.ReadBinary(installKeyName, "PilotCallsign");
        byte[]? pilotName = _registry.ReadBinary(installKeyName, "PilotName");

        if (callsign is null || pilotName is null)
            return false;

        string pilotCallsign = ReadPilotCallsign(installKeyName);
        string pilotNameText = ReadPilotName(installKeyName);

        if (string.Equals(pilotCallsign, DefaultCallsign, StringComparison.Ordinal))
            return false;

        if (string.Equals(pilotNameText, DefaultPilotName, StringComparison.Ordinal))
            return false;

        string lbkPath = Path.Combine(baseDir, "User", "Config", pilotCallsign + ".lbk");

        if (!File.Exists(lbkPath))
            return false;

        return true;
    }

    public void ChangeName(string installKeyName, string callsign, string pilotName)
    {
        _registry.WriteZeroPaddedAsciiBinary(installKeyName, "PilotCallsign", callsign, 12);
        _registry.WriteZeroPaddedAsciiBinary(installKeyName, "PilotName", pilotName, 20);
    }

    public void CreateLogbookIfMissing(string baseDir, string pilotCallsign, string pilotName)
    {
        string configDir = Path.Combine(baseDir, "User", "Config");
        Directory.CreateDirectory(configDir);

        string lbkPath = Path.Combine(configDir, pilotCallsign + ".lbk");
        if (File.Exists(lbkPath))
        {
            DebugDiagnosticsService.Info("Logbook file already exists - avoiding overwrite!");
            return;
        }

        string logcatExePath = Path.Combine(AppContext.BaseDirectory, "Tools", "bms-logcat.exe");
        if (!File.Exists(logcatExePath))
        {
            DebugDiagnosticsService.Warn("Could not find bms-logcat.exe");
            return;
        }

        const char dq = '\"';
        string lbkPathDQ = $"{dq}{lbkPath}{dq}";
        string pilotNameDQ = $"{dq}{pilotName}{dq}";
        string pilotCallsignDQ = $"{dq}{pilotCallsign}{dq}";
        string args = $"-o {lbkPathDQ} write-default --name {pilotNameDQ} --callsign {pilotCallsignDQ}";

        Process process = _process.StartExecutable(logcatExePath, Path.GetDirectoryName(logcatExePath), args);
        process.WaitForExit();

        if (process.ExitCode != 0)
            DebugDiagnosticsService.Warn($"Error code returned from bms-logcat.exe: {process.ExitCode}");
    }
}