using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Runs the pre-launch preparation flow so launcher-managed output files are updated before Falcon BMS starts.
/// 
/// Control device discovery is still intentionally disabled. Keyboard JSON files
/// are written as the new launcher/future-BMS state format, and legacy AUTO key
/// files are generated as compatibility output.
/// </summary>
public sealed class LaunchPrepService
{
    private readonly JsonKeyboardBindingWriterService _jsonKeyboardBindingWriter = new();
    private readonly LegacyAutoKeyWriterService _legacyAutoKeyWriter = new();
    private readonly UserCfgOverrideService _userCfg = new();
    private readonly PopFileService _pop = new();

    public void PrepareForLaunch(
        string baseDir,
        string installKeyName,
        bool exportRttTextures,
        bool vrEnabled,
        BindingModel bindingModel)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("LAUNCH");
        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH BEGIN | ActionId={actionId} | InstallKey={installKeyName} | BaseDir={baseDir}");

        _jsonKeyboardBindingWriter.Write(baseDir, bindingModel);
        _legacyAutoKeyWriter.Write(baseDir, bindingModel);
        _userCfg.SaveOverrides(baseDir, exportRttTextures, vrEnabled);
        _pop.SavePop(baseDir, installKeyName);

        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH END | ActionId={actionId}");
    }
}