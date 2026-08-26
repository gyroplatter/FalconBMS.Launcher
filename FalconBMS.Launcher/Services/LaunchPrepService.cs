using FalconBMS.Launcher.Models;
using System.Collections.Generic;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Runs the output synchronization flow before Falcon BMS starts or when the launcher closes.
///
/// JSON binding files are the persistent Launcher state.
///
/// Legacy key/XML/dat/cal files are generated compatibility outputs for Falcon BMS
/// and third-party tools. Those compatibility outputs can be intentionally skipped
/// when launching without control overrides.
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
        BindingModel bindingModel,
        bool bypassControlOverrides)
    {
        string actionId =
            DebugDiagnosticsService.CreateActionId("LAUNCH");

        DebugDiagnosticsService.Info(
            $"PREPARE FOR LAUNCH BEGIN | ActionId={actionId} | InstallKey={installKeyName} | BaseDir={baseDir} | BypassControlOverrides={bypassControlOverrides}");

        // JSON is the Launcher's persistent source of truth.
        // Always save this, even during a control-override bypass.
        _jsonKeyboardBindingWriter.Write(
            baseDir,
            bindingModel);

        _deviceJsonWriter.Write(
            baseDir,
            bindingModel.DeviceProfiles);

        IReadOnlyList<DeviceBindingProfile> connectedDeviceProfiles =
            bindingModel.DeviceProfiles
                .Where(profile => profile.IsConnected)
                .ToList();

        if (!bypassControlOverrides)
        {
            // These are the BMS/legacy control compatibility outputs.
            // A bypass launch intentionally leaves the existing copies on disk alone.
            _legacyAutoKeyWriter.Write(
                baseDir,
                bindingModel,
                connectedDeviceProfiles);

            IReadOnlyList<DeviceBindingProfile> offlineConfiguredDeviceProfiles =
                bindingModel.DeviceProfiles
                    .Where(profile =>
                        !profile.IsConnected &&
                        HasAnyAssignedBinding(profile))
                    .ToList();

            if (offlineConfiguredDeviceProfiles.Count > 0)
            {
                DebugDiagnosticsService.Warn(
                    $"Configured saved devices are offline. Their JSON profiles and bindings are preserved, but they are excluded from current BMS legacy output for this launch session. OfflineConfiguredDevices={offlineConfiguredDeviceProfiles.Count} | Devices={string.Join(", ", offlineConfiguredDeviceProfiles.Select(profile => profile.ProductName))} | ConnectedDevices={connectedDeviceProfiles.Count} | ActionId={actionId}");
            }

            _legacyDeviceSortingWriter.Write(
                baseDir,
                connectedDeviceProfiles);

            _legacyDeviceSetupXmlWriter.Write(
                baseDir,
                connectedDeviceProfiles);

            _legacyAxisMappingDatWriter.Write(
                baseDir,
                connectedDeviceProfiles);

            // joystick.cal carries assigned/invert flags and throttle detents used by BMS.
            // It is part of the control compatibility output and must also be skipped
            // when control overrides are bypassed.
            _joystickCal.Write(
                baseDir,
                connectedDeviceProfiles);
        }
        else
        {
            DebugDiagnosticsService.Info(
                $"CONTROL OVERRIDES BYPASSED | Existing BMS control files left untouched | ActionId={actionId}");
        }

        // Falcon BMS User.cfg is deliberately NOT part of the control bypass.
        // RTT, VR, and the normal Launcher User.cfg processing still happen.
        _userCfg.SaveOverrides(
            baseDir,
            connectedDeviceProfiles,
            exportRttTextures,
            vrEnabled);

        // POP preferences still save normally. During a bypass launch, however,
        // do not change the active BMS key profile.
        _pop.SavePop(
            baseDir,
            installKeyName,
            applyKeyFileOverride: !bypassControlOverrides);

        DebugDiagnosticsService.Info(
            $"PREPARE FOR LAUNCH END | ActionId={actionId}");
    }

    private static bool HasAnyAssignedBinding(
        DeviceBindingProfile profile)
    {
        if (profile.AxisBindings.Any(
                axis => axis.PhysicalAxisIndex.HasValue))
        {
            return true;
        }

        foreach (DeviceAircraftBindingProfile aircraftProfile
                 in profile.AircraftProfiles)
        {
            if (aircraftProfile.ButtonBindings.Any(
                    binding =>
                        !string.IsNullOrWhiteSpace(
                            binding.CallbackName)) ||
                aircraftProfile.PovBindings.Any(
                    binding =>
                        !string.IsNullOrWhiteSpace(
                            binding.CallbackName)))
            {
                return true;
            }
        }

        return false;
    }
}