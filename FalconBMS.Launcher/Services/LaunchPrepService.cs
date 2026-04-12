using FalconBMS.Launcher.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Runs the pre-launch preparation flow so all launcher-managed files are updated before FalconBMS starts.
/// </summary>
public sealed class LaunchPrepService
{
    private readonly SetupXmlService _setupXml = new();
    private readonly DeviceSortingService _sorting = new();
    private readonly AxisMappingDatService _axisDat = new();
    private readonly JoystickCalService _joyCal = new();
    private readonly SetupXmlKeymapReader _setupKeymap = new();
    private readonly KeyMappingOverrideWriter _keyWriter = new();
    private readonly UserCfgOverrideService _userCfg = new();
    private readonly PopFileService _pop = new();

    public void PrepareForLaunch(string baseDir, string installKeyName, bool exportRttTextures, bool vrEnabled)
    {
        string actionId = DebugDiagnosticsService.CreateActionId("LAUNCH");
        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH BEGIN | ActionId={actionId} | InstallKey={installKeyName} | BaseDir={baseDir}");

        var devices = EnumerateDevices();
        DebugDiagnosticsService.Info($"ENUM DEVICES | ActionId={actionId} | Source=LaunchPrepService.PrepareForLaunch | Reason=LaunchPrep | Count={devices.Count}");

        EnsureUserXmlsExist(baseDir, devices);

        Dictionary<Guid, int> slotByProductGuid = _sorting.EnsureDevicesAndGetSlots(
            baseDir,
            devices.Select(d => (d.ProductGuid, d.Name)));

        int deviceCount = _sorting.GetDeviceCount(baseDir);
        DebugDiagnosticsService.Info($"DEVICE SORTING | ActionId={actionId} | SlotCount={slotByProductGuid.Count} | DeviceCount={deviceCount}");

        var data = _axisDat.ReadAll(baseDir);
        bool anyMapped = data.Entries.Any(e => e.JoyNum >= 0 && e.AxisIndex >= 0);
        DebugDiagnosticsService.Info($"AXIS DATA | ActionId={actionId} | AnyMapped={anyMapped}");

        if (!anyMapped)
        {
            bool changed = BootstrapAxisMappingsFromSetupXml(
                baseDir,
                devices,
                slotByProductGuid,
                deviceCount);

            DebugDiagnosticsService.Info($"BOOTSTRAP AXIS | ActionId={actionId} | Changed={changed}");

            if (changed)
                data = _axisDat.ReadAll(baseDir);
        }

        DebugDiagnosticsService.Info($"WRITE REQUEST | ActionId={actionId} | File=joystick.cal | Caller=LaunchPrepService.PrepareForLaunch | Reason=LaunchPrep");
        _joyCal.Write(baseDir, data, _setupXml, _sorting);

        var km = _setupKeymap.Read(baseDir);

        string srcF16 = ResolveKeySource(baseDir, isF15: false);
        string srcF15 = ResolveKeySource(baseDir, isF15: true);

        DebugDiagnosticsService.Info($"KEY SOURCE | ActionId={actionId} | F16={srcF16}");
        DebugDiagnosticsService.Info($"KEY SOURCE | ActionId={actionId} | F15={srcF15}");

        var keyFileF16 = new KeyFile(srcF16);
        var keyFileF15 = new KeyFile(srcF15);

        _keyWriter.SaveKeyMapping(baseDir, keyFileF16, keyFileF15, km.Devices, km.RollJoyId, km.ThrottleJoyId);
        _userCfg.SaveOverrides(baseDir, km.RollJoyId, km.ThrottleJoyId, exportRttTextures, vrEnabled);
        _pop.SavePop(baseDir, installKeyName);

        DebugDiagnosticsService.Info($"PREPARE FOR LAUNCH END | ActionId={actionId}");
    }

    private static List<DirectInputManager.DeviceInfo> EnumerateDevices()
    {
        var di = new DirectInputManager();
        return di.EnumerateDevices().ToList();
    }

    private void EnsureUserXmlsExist(string baseDir, IEnumerable<DirectInputManager.DeviceInfo> devices)
    {
        foreach (var d in devices)
            _setupXml.EnsureUserXmlExistsForWrite(baseDir, d.Name, d.InstanceGuid);
    }

    private bool BootstrapAxisMappingsFromSetupXml(
        string baseDir,
        List<DirectInputManager.DeviceInfo> devices,
        Dictionary<Guid, int> slotByProductGuid,
        int deviceCount)
    {
        Guid? headerGuid = null;
        bool headerWritten = false;
        bool changed = false;

        // Batch all axismapping.dat updates into one read/write cycle during bootstrap.
        // This keeps the final file content the same while avoiding repeated disk writes.
        _axisDat.BeginBatch(baseDir);

        try
        {
            foreach (var def in Models.AxisCatalog.All)
            {
                if (!_setupXml.TryFindAxisBinding(baseDir, def.Function, out var instanceGuid, out var physicalAxisIndex))
                    continue;

                var match = devices.FirstOrDefault(x => x.InstanceGuid == instanceGuid);
                if (match is null)
                    continue;

                if (!slotByProductGuid.TryGetValue(match.ProductGuid, out int slotIndex))
                {
                    slotIndex = _sorting.EnsureDeviceAndGetSlot(baseDir, match.ProductGuid, match.Name);
                    slotByProductGuid[match.ProductGuid] = slotIndex;
                }

                if (headerGuid is null)
                    headerGuid = instanceGuid;

                _axisDat.SetAxisMapping(
                    baseDir: baseDir,
                    mappingIndex: def.MappingIndex,
                    deviceSlotIndex: slotIndex,
                    primaryInstanceGuidForHeader: headerGuid.Value,
                    physicalAxisIndex: physicalAxisIndex,
                    deviceCount: deviceCount,
                    deadzone: 0,
                    saturation: null,
                    updateHeaderPrimary: !headerWritten
                );

                headerWritten = true;
                changed = true;
            }
        }
        finally
        {
            _axisDat.EndBatch();
        }

        return changed;
    }

    private static string ResolveKeySource(string baseDir, bool isF15)
    {
        string configDir = Path.Combine(baseDir, "User", "Config");

        string auto = Path.Combine(configDir, isF15 ? "BMS - Auto-F15ABCD.key" : "BMS - Auto.key");
        if (File.Exists(auto)) return auto;

        string full = Path.Combine(configDir, isF15 ? "BMS - Full-F15ABCD.key" : "BMS - Full.key");
        if (File.Exists(full)) return full;

        string stockName = isF15 ? "BMS - Full-F15ABCD.key" : "BMS - Full.key";
        string exeDirFull = Path.Combine(AppContext.BaseDirectory, stockName);
        if (File.Exists(exeDirFull)) return exeDirFull;

        if (File.Exists(stockName)) return stockName;

        throw new FileNotFoundException(isF15 ? "Missing key file: BMS - Full-F15ABCD.key" : "Missing key file: BMS - Full.key");
    }
}
