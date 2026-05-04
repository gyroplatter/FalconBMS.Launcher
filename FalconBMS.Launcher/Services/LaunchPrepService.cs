using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Runs the pre-launch preparation flow so launcher-managed output files are updated before Falcon BMS starts.
/// 
/// Runs the output synchronization flow before Falcon BMS starts or when the launcher closes.
/// JSON binding files are the persistent launcher state, while legacy key/XML/dat/cal files
/// are generated compatibility outputs for Falcon BMS and third-party tools.
/// </summary>
public sealed class LaunchPrepService
{
    private readonly JsonKeyboardBindingWriterService _jsonKeyboardBindingWriter = new();
    private readonly DeviceJsonWriterService _deviceJsonWriter = new();
    private readonly LegacyAutoKeyWriterService _legacyAutoKeyWriter = new();
    private readonly LegacyDeviceSortingWriterService _legacyDeviceSortingWriter = new();
    private readonly LegacyDeviceSetupXmlWriterService _legacyDeviceSetupXmlWriter = new();
    private readonly LegacyAxisMappingDatWriterService _legacyAxisMappingDatWriter = new();
    private readonly JoystickCalService _joystickCal = new();
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

        // Device JSON is the persistent HOTAS/axis binding output.
        // Legacy device files below are generated compatibility artifacts from this
        // same in-memory binding model.
        _deviceJsonWriter.Write(baseDir, bindingModel.DeviceProfiles);

        _legacyAutoKeyWriter.Write(baseDir, bindingModel);

        _legacyDeviceSortingWriter.Write(baseDir, bindingModel.DeviceProfiles);
        _legacyDeviceSetupXmlWriter.Write(baseDir, bindingModel.DeviceProfiles);
        _legacyAxisMappingDatWriter.Write(baseDir, bindingModel.DeviceProfiles);

        // joystick.cal carries assigned/invert flags and throttle detents used by BMS.
        // Without this generated compatibility file, detents can appear correct in the
        // launcher but not take effect in game.
        _joystickCal.Write(baseDir, bindingModel.DeviceProfiles);

        _userCfg.SaveOverrides(baseDir, bindingModel.DeviceProfiles, exportRttTextures, vrEnabled);

        // BMS reads the active key profile from the pilot .pop file.
        // This must set byte offset 336 to "BMS - Auto"; otherwise BMS keeps loading
        // "BMS - Full" and device DX callbacks show as "No Function Assigned" in game.
        _pop.SavePop(baseDir, installKeyName);

        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH END | ActionId={actionId}");
    }
}