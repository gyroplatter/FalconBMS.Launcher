namespace FalconBMS.Launcher.Services;

/// <summary>
/// Runs the pre-launch preparation flow for non-control launcher outputs only.
/// Control/keymapping/device output generation has intentionally been removed.
/// </summary>
public sealed class LaunchPrepService
{
    private readonly UserCfgOverrideService _userCfg = new();
    private readonly PopFileService _pop = new();

    public void PrepareForLaunch(string baseDir, string installKeyName, bool exportRttTextures, bool vrEnabled)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("LAUNCH");
        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH BEGIN | ActionId={actionId} | InstallKey={installKeyName} | BaseDir={baseDir}");

        _userCfg.SaveOverrides(baseDir, exportRttTextures, vrEnabled);
        _pop.SavePop(baseDir, installKeyName);

        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH END | ActionId={actionId}");
    }
}