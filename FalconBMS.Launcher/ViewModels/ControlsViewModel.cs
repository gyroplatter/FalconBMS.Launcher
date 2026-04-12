using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using FalconBMS.Launcher.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View model for the Controls tab, including axis rows, refresh logic, and live polling integration.
/// </summary>

public sealed class ControlsViewModel : ViewModelBase
{
    private readonly Func<BmsInstall?> _getSelectedInstall;
    private readonly SetupXmlService _setupXml = new();
    private readonly AxisBindingsSnapshotService _axisSnapshot = new();

    private readonly Dictionary<AxisFunction, string> _axisBindingText = new();
    public ObservableCollection<AxisRowViewModel> AxisRows { get; } = new();

    private string _rollBindingText = "Not set";
    public string RollBindingText
    {
        get => _rollBindingText;
        set => Set(ref _rollBindingText, value);
    }

    private string _pitchBindingText = "Not set";
    public string PitchBindingText
    {
        get => _pitchBindingText;
        set => Set(ref _pitchBindingText, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    // ===== Live axis bar polling (tabs) =====

    private readonly DirectInputManager _di = new();
    private DispatcherTimer? _axisTimer;
    private IntPtr _axisHwnd;
    private bool _isPollingActive = true;

    private readonly Dictionary<Guid, JoystickSession> _sessions = new();
    private readonly Dictionary<Guid, List<(AxisRowViewModel Row, int AxisIndex)>> _boundRowsByInstance = new();

    // Cache the last device snapshot used for live axis bars so repeated UI refreshes
    // do not always force another full DirectInput enumeration.
    private IReadOnlyList<DirectInputManager.DeviceInfo>? _cachedLiveDevices;

    // Track the last live-binding signature we built sessions for. If the rows have not
    // changed and sessions are already open, we can skip a full dispose/rebuild cycle.
    private string? _lastLiveBindingSignature;
    private string? _lastLiveBindingInstallBaseDir;

    public ControlsViewModel(Func<BmsInstall?> getSelectedInstall)
    {
        _getSelectedInstall = getSelectedInstall;

        // Display-only regrouping for the Controls tab.
        // Axis functions, mapping indices, assign/clear logic, and all output writing remain unchanged.
        AddGroupedAxis("Side Stick Controller", AxisFunction.Pitch, "Pitch");
        AddGroupedAxis("Side Stick Controller", AxisFunction.Roll, "Roll");

        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Throttle, "Throttle");
        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Throttle_Right, "Throttle Right");
        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Radar_Antenna_Elevation, "Antenna Elevation");
        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Range_Knob, "Range Knob");
        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Cursor_X, "Cursor X");
        AddGroupedAxis("Throttle Quadrant System (TQS)", AxisFunction.Cursor_Y, "Cursor Y");

        AddGroupedAxis("Rudder", AxisFunction.Yaw, "Yaw");
        AddGroupedAxis("Rudder", AxisFunction.Toe_Brake, "Toe Brake");
        AddGroupedAxis("Rudder", AxisFunction.Toe_Brake_Right, "Toe Brake Right");

        AddGroupedAxis("Trim Panel", AxisFunction.Trim_Roll, "Trim Roll");
        AddGroupedAxis("Trim Panel", AxisFunction.Trim_Pitch, "Trim Pitch");
        AddGroupedAxis("Trim Panel", AxisFunction.Trim_Yaw, "Trim Yaw");

        AddGroupedAxis("Integrated Control Panel (ICP)", AxisFunction.HUD_Brightness, "HUD Brightness");
        AddGroupedAxis("Integrated Control Panel (ICP)", AxisFunction.Reticle_Depression, "Reticle Depr");
        AddGroupedAxis("Integrated Control Panel (ICP)", AxisFunction.HMS_Brightness, "HMS Brightness");
        AddGroupedAxis("Integrated Control Panel (ICP)", AxisFunction.FLIR_Brightness, "FLIR Brightness");

        AddGroupedAxis("Horizontal Situation Indicated (HSI)", AxisFunction.HSI_Course_Knob, "Course");
        AddGroupedAxis("Horizontal Situation Indicated (HSI)", AxisFunction.HSI_Heading_Knob, "Heading");

        AddGroupedAxis("Altimeter", AxisFunction.Altimeter_Knob, "Altimeter");

        AddGroupedAxis("Audio Panel", AxisFunction.COMM_Channel_1, "Comm Ch 1");
        AddGroupedAxis("Audio Panel", AxisFunction.COMM_Channel_2, "Comm Ch 2");
        AddGroupedAxis("Audio Panel", AxisFunction.IntercomVolumeVolume, "Intercom");
        AddGroupedAxis("Audio Panel", AxisFunction.ILS_Volume_Knob, "ILS Volume");
        AddGroupedAxis("Audio Panel", AxisFunction.MSL_Volume, "Missile Volume");
        AddGroupedAxis("Audio Panel", AxisFunction.Threat_Volume, "Threat Volume");
        AddGroupedAxis("Audio Panel", AxisFunction.AI_vs_IVC, "AI vs IVC");
    }

    private void AddGroupedAxis(string groupName, AxisFunction function, string displayName)
    {
        var def = AxisCatalog.Get(function);

        AxisRows.Add(new AxisRowViewModel(
            def,
            canExecute: _ => CanAssign(),
            assign: Assign,
            clear: ClearAxis,
            groupName: groupName,
            displayNameOverride: displayName
        ));
    }

    public void RefreshFromDisk() => LoadFromDisk(null);

    public void RefreshFromSnapshot(AxisBindingsSnapshotService.AxisBindingsSnapshot snapshot) => LoadFromDisk(snapshot);

    public void PrepareAxisConfigForRefresh(string baseDir)
    {
        DebugDiagnosticsService.Info($"REFRESH PREP | Source=ControlsViewModel.PrepareAxisConfigForRefresh | BaseDir={baseDir}");

        EnsureSetupXmlsForAttachedDevices(baseDir);
        BootstrapAxisMappingsFromSetupXmlIfNeeded(baseDir);

        DebugDiagnosticsService.Info($"REFRESH PREP COMPLETE | Source=ControlsViewModel.PrepareAxisConfigForRefresh | BaseDir={baseDir}");
    }

    public void StartAxisBarLive(IntPtr hwnd)
    {
        DebugDiagnosticsService.Info("[ControlsVM] StartAxisBarLive CALLED");
        _axisHwnd = hwnd;

        if (_axisTimer is null)
        {
            _axisTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _axisTimer.Tick += (_, _) => PollAxisBars();
        }

        UpdateAxisTimerInterval();

        // When live polling is explicitly starting, force one fresh device scan so the
        // first live-bind build uses the current attached-device state.
        RebuildLiveBindings(forceDeviceRefresh: true);

        _axisTimer.Start();
    }

    public void StopAxisBarLive()
    {
        DebugDiagnosticsService.Info("[ControlsVM] StopAxisBarLive CALLED");
        if (_axisTimer is not null)
            _axisTimer.Stop();

        DisposeSessions();

        // Keep the cached device list, but clear the active-session signature so a future
        // StartAxisBarLive rebuilds the sessions intentionally.
        _lastLiveBindingSignature = null;

        foreach (var r in AxisRows)
            r.SetLiveSource(r.GetLiveSource());
    }

    public void SetPollingActive(bool isActive)
    {
        _isPollingActive = isActive;
        UpdateAxisTimerInterval();
    }

    private void UpdateAxisTimerInterval()
    {
        if (_axisTimer is null)
            return;

        DebugDiagnosticsService.Info("[ControlsVM] Axis LIVE SESSION BUILD");

        _axisTimer.Interval = TimeSpan.FromMilliseconds(_isPollingActive ? 50 : 250);
    }

    private void DisposeSessions()
    {
        foreach (var s in _sessions.Values)
        {
            try { s.Dispose(); } catch { }
        }
        _sessions.Clear();
        _boundRowsByInstance.Clear();
    }

    private void PollAxisBars()
    {
        if (_boundRowsByInstance.Count == 0)
            return;

        foreach (var kvp in _boundRowsByInstance)
        {
            var instanceGuid = kvp.Key;
            if (!_sessions.TryGetValue(instanceGuid, out var session))
                continue;

            int[] vec;
            try
            {
                var state = session.ReadState();
                vec = DirectInputManager.ReadAxisVector(state);
            }
            catch
            {
                continue;
            }

            foreach (var (row, axisIdx) in kvp.Value)
            {
                if ((uint)axisIdx >= (uint)vec.Length)
                    continue;

                row.UpdateFromRawAxisValue(vec[axisIdx]);
            }
        }
    }

    private void RebuildLiveBindings(bool forceDeviceRefresh = false)
    {
        var install = _getSelectedInstall();
        if (install is null)
        {
            DisposeSessions();
            _lastLiveBindingSignature = null;
            _lastLiveBindingInstallBaseDir = null;

            foreach (var r in AxisRows)
                r.SetLiveSource(null);

            return;
        }

        string signature = BuildLiveBindingSignature(install.BaseDir);

        // If the live-source inputs did not change and sessions are already active,
        // avoid tearing everything down and reopening the same devices again.
        if (!forceDeviceRefresh &&
            string.Equals(_lastLiveBindingInstallBaseDir, install.BaseDir, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastLiveBindingSignature, signature, StringComparison.Ordinal) &&
            (_sessions.Count > 0 || !_boundRowsByInstance.Any() && !AxisRows.Any(r => r.GetLiveSource() is not null)))
        {
            return;
        }

        DisposeSessions();

        var devices = GetLiveBindingDevices(forceDeviceRefresh, install.BaseDir);

        foreach (var row in AxisRows)
        {
            var src = row.GetLiveSource();
            if (src is null)
                continue;

            var inst = ResolveInstanceGuid(devices, src.DeviceName, src.ProductGuid);
            if (inst is null)
            {
                row.AxisBarEnabled = false;
                continue;
            }

            if (!_sessions.TryGetValue(inst.Value, out var session))
            {
                try
                {
                    session = _di.Open(inst.Value, _axisHwnd);
                    _sessions[inst.Value] = session;
                }
                catch
                {
                    row.AxisBarEnabled = false;
                    continue;
                }
            }

            if (!_boundRowsByInstance.TryGetValue(inst.Value, out var list))
            {
                list = new List<(AxisRowViewModel Row, int AxisIndex)>();
                _boundRowsByInstance[inst.Value] = list;
            }

            list.Add((row, src.PhysicalAxisIndex));
            row.AxisBarEnabled = true;
        }

        _lastLiveBindingSignature = signature;
        _lastLiveBindingInstallBaseDir = install.BaseDir;
    }

    private static Guid? ResolveInstanceGuid(IReadOnlyList<DirectInputManager.DeviceInfo> devices, string deviceName, Guid? productGuid)
    {
        if (devices.Count == 0) return null;

        var byName = devices.FirstOrDefault(d =>
            string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
            return byName.InstanceGuid;

        if (productGuid is not null)
        {
            var byProd = devices.FirstOrDefault(d => d.ProductGuid == productGuid.Value);
            if (byProd is not null)
                return byProd.InstanceGuid;
        }

        var contains = devices.FirstOrDefault(d =>
            d.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains(d.Name, StringComparison.OrdinalIgnoreCase));

        return contains?.InstanceGuid;
    }

    private IReadOnlyList<DirectInputManager.DeviceInfo> GetLiveBindingDevices(bool forceRefresh, string baseDir)
    {
        if (!forceRefresh &&
            _cachedLiveDevices is not null &&
            string.Equals(_lastLiveBindingInstallBaseDir, baseDir, StringComparison.OrdinalIgnoreCase))
        {
            DebugDiagnosticsService.Info($"ENUM DEVICES | Source=ControlsViewModel.GetLiveBindingDevices | Reason=ReuseCached | Count={_cachedLiveDevices.Count} | BaseDir={baseDir}");
            return _cachedLiveDevices;
        }

        try
        {
            _cachedLiveDevices = _di.EnumerateDevices();
            DebugDiagnosticsService.Info($"ENUM DEVICES | Source=ControlsViewModel.GetLiveBindingDevices | Reason={(forceRefresh ? "ForcedRefresh" : "CacheMiss")} | Count={_cachedLiveDevices.Count} | BaseDir={baseDir}");
        }
        catch
        {
            _cachedLiveDevices = Array.Empty<DirectInputManager.DeviceInfo>();
            DebugDiagnosticsService.Warn($"ENUM DEVICES | Source=ControlsViewModel.GetLiveBindingDevices | Reason={(forceRefresh ? "ForcedRefresh" : "CacheMiss")} | Result=Exception | BaseDir={baseDir}");
        }

        return _cachedLiveDevices;
    }

    private string BuildLiveBindingSignature(string baseDir)
    {
        var parts = new List<string> { baseDir };

        foreach (var row in AxisRows)
        {
            var src = row.GetLiveSource();
            if (src is null)
            {
                parts.Add($"{row.Function}:null");
                continue;
            }

            parts.Add(
                $"{row.Function}:{src.DeviceName}:{src.ProductGuid}:{src.PhysicalAxisIndex}:{src.Invert}:{src.Detents}");
        }

        return string.Join("|", parts);
    }

    private bool CanAssign() => _getSelectedInstall() is not null;

    private void ClearAxis(AxisFunction function)
    {
        var install = _getSelectedInstall();
        if (install is null)
        {
            StatusText = "No install selected.";
            return;
        }

        try
        {
            StatusText = "";
            string actionId = DebugDiagnosticsService.CreateActionId("CTRLCLR");
            DebugDiagnosticsService.Info($"USER ACTION | ActionId={actionId} | Source=ControlsViewModel.ClearAxis | Function={function}");

            var axisDat = new AxisMappingDatService();
            var def = AxisCatalog.Get(function);

            _setupXml.ClearAxisBinding(install.BaseDir, function);
            axisDat.ClearAxisMapping(install.BaseDir, def.MappingIndex);

            try
            {
                var sorting = new DeviceSortingService();
                var full = axisDat.ReadAll(install.BaseDir);
                new JoystickCalService().Write(install.BaseDir, full, _setupXml, sorting);
            }
            catch { }

            try
            {
                var km = new SetupXmlKeymapReader().Read(install.BaseDir);

                _setupXml.SaveAllDeviceXmlsFromJoyAssgns(install.BaseDir, km.Devices);

                string cfgDir = Path.Combine(install.BaseDir, "User", "Config");

                string fullF16 = Path.Combine(cfgDir, "BMS - Full.key");
                if (!File.Exists(fullF16) && File.Exists("BMS - Full.key"))
                    fullF16 = "BMS - Full.key";

                string fullF15 = Path.Combine(cfgDir, "BMS - Full-F15ABCD.key");
                if (!File.Exists(fullF15) && File.Exists("BMS - Full-F15ABCD.key"))
                    fullF15 = "BMS - Full-F15ABCD.key";

                if (File.Exists(fullF16) && File.Exists(fullF15))
                {
                    var keyFileF16 = new KeyFile(fullF16);
                    var keyFileF15 = new KeyFile(fullF15);

                    new KeyMappingOverrideWriter().SaveKeyMapping(
                        install.BaseDir,
                        keyFileF16,
                        keyFileF15,
                        km.Devices,
                        km.RollJoyId,
                        km.ThrottleJoyId
                    );
                }
            }
            catch { }

            StatusText = "Cleared.";
            DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=ControlsViewModel.ClearAxis | Scope=LocalControlsReload");
            LoadFromDisk();
            return;
        }
        catch (Exception ex)
        {
            StatusText = "";
            MessageBox.Show(ex.Message, "Controls", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadFromDisk(AxisBindingsSnapshotService.AxisBindingsSnapshot? snapshot = null)
    {
        var install = _getSelectedInstall();
        _axisBindingText.Clear();

        DebugDiagnosticsService.Info($"REFRESH BEGIN | Source=ControlsViewModel.LoadFromDisk | Install={(install?.RegistryKeyName ?? "<null>")} | SnapshotMode={(snapshot is null ? "BuildNew" : "ReuseProvided")}");

        if (install is null)
        {
            RollBindingText = "Not set";
            PitchBindingText = "Not set";

            foreach (var r in AxisRows)
            {
                r.BindingText = "Not set";
                r.SetLiveSource(null);
            }

            RebuildLiveBindings();
            DebugDiagnosticsService.Info("REFRESH END | Source=ControlsViewModel.LoadFromDisk | Install=<null>");
            return;
        }

        try
        {
            if (snapshot is null)
            {
                PrepareAxisConfigForRefresh(install.BaseDir);
                DebugDiagnosticsService.Info($"SNAPSHOT BUILD | Source=ControlsViewModel.LoadFromDisk | BaseDir={install.BaseDir}");

                snapshot = _axisSnapshot.Build(
                    install.BaseDir,
                    AxisCatalog.All.Select(d => d.Function));
            }

            var live = new Dictionary<AxisFunction, AxisRowViewModel.LiveAxisSource?>();

            foreach (var def in AxisCatalog.All)
            {
                if (!snapshot.Bindings.TryGetValue(def.Function, out var binding) || !binding.IsMapped)
                {
                    _axisBindingText[def.Function] = "Not set";
                    live[def.Function] = null;
                    continue;
                }

                _axisBindingText[def.Function] = binding.BindingText;

                live[def.Function] = new AxisRowViewModel.LiveAxisSource(
                    DeviceName: binding.DeviceName!,
                    ProductGuid: binding.ProductGuid,
                    PhysicalAxisIndex: binding.PhysicalAxisIndex,
                    Invert: binding.Invert,
                    Detents: binding.Detents
                );
            }

            RollBindingText = GetBindingText(AxisFunction.Roll);
            PitchBindingText = GetBindingText(AxisFunction.Pitch);

            foreach (var row in AxisRows)
            {
                row.BindingText = GetBindingText(row.Function);
                live.TryGetValue(row.Function, out var src);
                row.SetLiveSource(src);
            }

            RebuildLiveBindings();
            DebugDiagnosticsService.Info($"REFRESH END | Source=ControlsViewModel.LoadFromDisk | Install={install.RegistryKeyName}");
        }
        catch
        {
            RollBindingText = "Not set";
            PitchBindingText = "Not set";

            foreach (var r in AxisRows)
            {
                r.BindingText = "Not set";
                r.SetLiveSource(null);
            }

            RebuildLiveBindings();
            DebugDiagnosticsService.Warn($"REFRESH END | Source=ControlsViewModel.LoadFromDisk | Install={install.RegistryKeyName} | Result=FallbackAfterException");
        }
    }

    private void BootstrapAxisMappingsFromSetupXmlIfNeeded(string baseDir)
    {
        try
        {
            var axisDat = new AxisMappingDatService();
            var data = axisDat.ReadAll(baseDir);

            // If any axis is already mapped, assume user has config and do nothing.
            // (Official launcher would preserve existing user mappings.)
            if (data.Entries.Any(e => e.JoyNum >= 0 && e.AxisIndex >= 0))
                return;

            // Ensure DeviceSorting exists and has all current devices in one batch write.
            var sorting = new DeviceSortingService();
            DebugDiagnosticsService.Info("[ControlsVM] EnumerateDevices → BootstrapAxisMappingsFromSetupXmlIfNeeded");

            var devices = _di.EnumerateDevices();

            var slotByProductGuid = sorting.EnsureDevicesAndGetSlots(
                baseDir,
                devices.Select(d => (d.ProductGuid, d.Name)));

            int deviceCount = sorting.GetDeviceCount(baseDir);

            // Pick a stable "primary" header GUID (first mapped device)
            Guid headerGuid = Guid.Empty;
            bool headerWritten = false;

            // Collect the mappings first so the bootstrap logic stays easy to follow.
            // This does NOT fully solve the repeated file writes yet, because SetAxisMapping()
            // still writes the file on each call. But it preserves your original comments/flow
            // and avoids the bad type-name change from the previous draft.
            var mappings = new List<(AxisActionDef Def, int SlotIndex, Guid InstanceGuid, int PhysicalAxisIndex)>();

            // Seed axismapping.dat using AxisName slots from Setup XML files.
            // We find which device file contains AxisName=Pitch/Roll/etc and which physical axis index it occupies.
            foreach (var def in AxisCatalog.All)
            {
                if (!_setupXml.TryFindAxisBinding(baseDir, def.Function, out var instanceGuid, out var physicalAxisIndex))
                    continue;

                var match = devices.FirstOrDefault(x => x.InstanceGuid == instanceGuid);
                if (match is null)
                    continue;

                if (!slotByProductGuid.TryGetValue(match.ProductGuid, out int slotIndex))
                    continue;

                if (!headerWritten)
                {
                    headerGuid = instanceGuid;
                    headerWritten = true;
                }

                mappings.Add((def, slotIndex, instanceGuid, physicalAxisIndex));
            }

            axisDat.BeginBatch(baseDir);

            foreach (var mapping in mappings)
            {
                axisDat.SetAxisMapping(
                    baseDir: baseDir,
                    mappingIndex: mapping.Def.MappingIndex,
                    deviceSlotIndex: mapping.SlotIndex,
                    primaryInstanceGuidForHeader: headerGuid,
                    physicalAxisIndex: mapping.PhysicalAxisIndex,
                    deviceCount: deviceCount,
                    deadzone: 0,
                    saturation: null,
                    updateHeaderPrimary: mapping.Def.MappingIndex == 0
                );
            }

            axisDat.EndBatch();
        }
        catch
        {
            // Best-effort bootstrap; do not block UI load.
        }
    }

    private string GetBindingText(AxisFunction function)
    {
        return _axisBindingText.TryGetValue(function, out var s) ? s : "Not set";
    }

    private void EnsureSetupXmlsForAttachedDevices(string baseDir)
    {
        try
        {
            DebugDiagnosticsService.Info("[ControlsVM] EnumerateDevices → BootstrapAxisMappingsFromSetupXmlIfNeeded");

            var devices = _di.EnumerateDevices();
            foreach (var d in devices)
            {
                _setupXml.EnsureUserXmlExistsFromStock(baseDir, d.Name, d.InstanceGuid);
            }
        }
        catch
        {
            // Match stock launcher “best effort”: don’t block UI if DI fails.
        }
    }

    private void Assign(AxisFunction function)
    {
        var install = _getSelectedInstall();
        if (install is null)
        {
            StatusText = "No install selected.";
            return;
        }

        try
        {
            StatusText = "";
            string actionId = DebugDiagnosticsService.CreateActionId("CTRLASN");
            DebugDiagnosticsService.Info($"USER ACTION | ActionId={actionId} | Source=ControlsViewModel.Assign | Function={function}");

            var axisDef = AxisCatalog.Get(function);

            AxisExistingBinding? existing = null;

            var axisDatExisting = new AxisMappingDatService();
            var existingMap = axisDatExisting.ReadAxisMapping(install.BaseDir, axisDef.MappingIndex);

            if (existingMap is not null)
            {
                int slotIndex = existingMap.Value.JoyNum - 2;
                var sortingExisting = new DeviceSortingService();

                string deviceName =
                    sortingExisting.GetDeviceNameBySlot(install.BaseDir, slotIndex)
                    ?? $"Device Slot {slotIndex}";

                Guid? productGuid =
                    sortingExisting.GetProductGuidBySlot(install.BaseDir, slotIndex);

                bool invert = false;
                _setupXml.TryGetInvert(install.BaseDir, function, out invert);

                AxCurve dz = AxCurve.None;
                _setupXml.TryGetDeadzone(install.BaseDir, function, out dz);

                AxCurve sat = AxCurve.None;
                _setupXml.TryGetSaturation(install.BaseDir, function, out sat);

                DetentPosition? det = null;
                if (function == AxisFunction.Throttle)
                {
                    if (_setupXml.TryGetDetents(install.BaseDir, deviceName, out var d))
                        det = d;
                    else
                        det = DetentPosition.Default;
                }

                existing = new AxisExistingBinding(
                    DeviceName: deviceName,
                    ProductGuid: productGuid,
                    PhysicalAxisIndex: existingMap.Value.AxisIndex,
                    Invert: invert,
                    Deadzone: dz,
                    Saturation: sat,
                    Detents: det
                );
            }

            var win = new AxisAssignWindow(function, existing)
            {
                Owner = Application.Current.MainWindow
            };

            bool? ok = win.ShowDialog();
            DebugDiagnosticsService.Info($"ASSIGN DIALOG RESULT | ActionId={actionId} | Function={function} | Accepted={(ok == true)}");
            if (ok != true)
            {
                StatusText = "Cancelled.";
                return;
            }

            var axisDat = new AxisMappingDatService();

            if (win.WasCleared)
            {
                DebugDiagnosticsService.Info($"ASSIGN RESULT | ActionId={actionId} | Function={function} | Result=ClearedInDialog");
                _setupXml.ClearAxisBinding(install.BaseDir, function);
                axisDat.ClearAxisMapping(install.BaseDir, axisDef.MappingIndex);

                try
                {
                    var sorting = new DeviceSortingService();
                    var full = axisDat.ReadAll(install.BaseDir);
                    new JoystickCalService().Write(install.BaseDir, full, _setupXml, sorting);
                }
                catch { }

                StatusText = "Cleared.";
                DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=ControlsViewModel.Assign | Scope=LocalControlsReload");
                LoadFromDisk();
                return;
            }

            var sel = win.Result;
            DebugDiagnosticsService.Info($"ASSIGN RESULT | ActionId={actionId} | Function={function} | Device={win.Result?.DeviceName ?? "<null>"} | Axis={win.Result?.PhysicalAxisIndex.ToString() ?? "<null>"}");
            if (sel is null)
            {
                StatusText = "No axis selected.";
                return;
            }

            var sortingSvc = new DeviceSortingService();

            // Batch-style path, even for one device, so the service can skip rewriting
            // DeviceSorting.txt when the content is unchanged.
            var slotByProductGuid = sortingSvc.EnsureDevicesAndGetSlots(
                install.BaseDir,
                new[] { (sel.DeviceProductGuid, sel.DeviceName) });

            int slot = slotByProductGuid[sel.DeviceProductGuid];
            int deviceCount = sortingSvc.GetDeviceCount(install.BaseDir);

            // Enforce: a physical axis can only be bound to ONE BMS function (across ALL tabs)
            // Conflict key = (slot+2 joynum, physical axis index)
            int desiredJoyNum = slot + 2;
            int desiredAxisIndex = sel.PhysicalAxisIndex;

            var all = axisDat.ReadAll(install.BaseDir);

            var conflicts = all.Entries
                .Where(e =>
                    e.Index != axisDef.MappingIndex &&
                    e.JoyNum == desiredJoyNum &&
                    e.AxisIndex == desiredAxisIndex)
                .Select(e => e.Index)
                .ToList();

            if (conflicts.Count > 0)
            {
                string axisName = AxisIndexToName(desiredAxisIndex);
                string deviceName = sel.DeviceName;

                var conflictNames = conflicts
                    .Select(i =>
                    {
                        var def = AxisCatalog.All.FirstOrDefault(d => d.MappingIndex == i);
                        return def is null ? $"Mapping {i}" : def.DisplayName;
                    })
                    .ToList();

                string msg =
                    conflicts.Count == 1
                        ? $"{deviceName} {axisName} is already assigned to \"{conflictNames[0]}\".\n\nReplace it with \"{axisDef.DisplayName}\"?\n\n(If you click Yes, the previous assignment will be cleared.)"
                        : $"{deviceName} {axisName} is already assigned to:\n  - {string.Join("\n  - ", conflictNames)}\n\nReplace those with \"{axisDef.DisplayName}\"?\n\nIf you click Yes, the previous assignments will be cleared.";

                var res = MessageBox.Show(
                    msg,
                    "Axis Already Assigned",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes)
                {
                    StatusText = "Cancelled.";
                    return;
                }

                // Clear previous assignment(s): both axismapping.dat and setup.xml bindings
                foreach (int mappingIdx in conflicts)
                {
                    axisDat.ClearAxisMapping(install.BaseDir, mappingIdx);

                    var def = AxisCatalog.All.FirstOrDefault(d => d.MappingIndex == mappingIdx);
                    if (def is not null)
                        _setupXml.ClearAxisBinding(install.BaseDir, def.Function);
                }
            }

            axisDat.SetAxisMapping(
                baseDir: install.BaseDir,
                mappingIndex: axisDef.MappingIndex,
                deviceSlotIndex: slot,
                primaryInstanceGuidForHeader: sel.DeviceInstanceGuid,
                physicalAxisIndex: sel.PhysicalAxisIndex,
                deviceCount: deviceCount,
                deadzone: AxCurveCodec.DeadzoneToInt(sel.Deadzone),
                saturation: AxCurveCodec.SaturationToInt(sel.Saturation),
                updateHeaderPrimary: axisDef.MappingIndex == 0
            );

            _setupXml.ApplyAxisBinding(install.BaseDir, function, sel);

            if (function == AxisFunction.Throttle)
            {
                var detents = win.Detents ?? DetentPosition.Default;
                _setupXml.SetDetents(install.BaseDir, sel.DeviceName, sel.DeviceInstanceGuid, detents);
            }

            try
            {
                var full = axisDat.ReadAll(install.BaseDir);
                new JoystickCalService().Write(install.BaseDir, full, _setupXml, sortingSvc);
            }
            catch { }

            try
            {
                // ORIGINAL parity:
                // Any axis save triggers:
                // 1) SaveXml for ALL devices (full structure)
                // 2) Regenerate BOTH Auto key files
                var km = new SetupXmlKeymapReader().Read(install.BaseDir);

                _setupXml.SaveAllDeviceXmlsFromJoyAssgns(install.BaseDir, km.Devices);

                string cfgDir = Path.Combine(install.BaseDir, "User", "Config");

                string fullF16 = Path.Combine(cfgDir, "BMS - Full.key");
                if (!File.Exists(fullF16) && File.Exists("BMS - Full.key"))
                    fullF16 = "BMS - Full.key";

                string fullF15 = Path.Combine(cfgDir, "BMS - Full-F15ABCD.key");
                if (!File.Exists(fullF15) && File.Exists("BMS - Full-F15ABCD.key"))
                    fullF15 = "BMS - Full-F15ABCD.key";

                if (File.Exists(fullF16) && File.Exists(fullF15))
                {
                    var keyFileF16 = new KeyFile(fullF16);
                    var keyFileF15 = new KeyFile(fullF15);

                    new KeyMappingOverrideWriter().SaveKeyMapping(
                        install.BaseDir,
                        keyFileF16,
                        keyFileF15,
                        km.Devices,
                        km.RollJoyId,
                        km.ThrottleJoyId
                    );
                }
            }
            catch { }

            StatusText = "Saved to User\\Config.";
            DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=ControlsViewModel.Assign | Scope=LocalControlsReload");
            LoadFromDisk();
        }
        catch (Exception ex)
        {
            StatusText = "";
            MessageBox.Show(ex.Message, "Controls", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string AxisIndexToName(int idx) =>
        idx switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "Rx",
            4 => "Ry",
            5 => "Rz",
            6 => "Slider0",
            7 => "Slider1",
            _ => idx.ToString()
        };
}