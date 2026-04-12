using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Utils;
using FalconBMS.Launcher.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// This file can be removed later.
/// This is from start of project when Audio was control from its own tab.
/// View model for the Audio tab, including live axis display and audio-related control bindings.
/// </summary>

public sealed class AudioViewModel : ViewModelBase
{
    private readonly Func<BmsInstall?> _getSelectedInstall;
    private readonly SetupXmlService _setupXml = new();
    private readonly AxisBindingsSnapshotService _axisSnapshot = new();

    private readonly Dictionary<AxisFunction, string> _axisBindingText = new();
    public ObservableCollection<AxisRowViewModel> AxisRows { get; } = new();

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private static readonly HashSet<AxisFunction> AudioFunctions = new()
    {
        AxisFunction.COMM_Channel_1,
        AxisFunction.COMM_Channel_2,
        AxisFunction.MSL_Volume,
        AxisFunction.Threat_Volume,
        AxisFunction.IntercomVolumeVolume,
        AxisFunction.AI_vs_IVC,
        AxisFunction.ILS_Volume_Knob,
    };

    // ===== Live axis bar polling (tabs) =====

    private readonly DirectInputManager _di = new();
    private DispatcherTimer? _axisTimer;
    private IntPtr _axisHwnd;
    private bool _isPollingActive = true;

    private readonly Dictionary<Guid, JoystickSession> _sessions = new();
    private readonly Dictionary<Guid, List<(AxisRowViewModel Row, int AxisIndex)>> _boundRowsByInstance = new();

    public AudioViewModel(Func<BmsInstall?> getSelectedInstall)
    {
        _getSelectedInstall = getSelectedInstall;

        foreach (var def in AxisCatalog.All.Where(d => AudioFunctions.Contains(d.Function)))
        {
            AxisRows.Add(new AxisRowViewModel(
                def,
                canExecute: _ => CanAssign(),
                assign: Assign,
                clear: ClearAxis
            ));
        }
    }

    public void RefreshFromDisk() => LoadFromDisk(null);

    public void RefreshFromSnapshot(AxisBindingsSnapshotService.AxisBindingsSnapshot snapshot) => LoadFromDisk(snapshot);

    public void StartAxisBarLive(IntPtr hwnd)
    {
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
        RebuildLiveBindings();
        _axisTimer.Start();
    }

    public void StopAxisBarLive()
    {
        if (_axisTimer is not null)
            _axisTimer.Stop();

        DisposeSessions();

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

    private void RebuildLiveBindings()
    {
        DisposeSessions();

        var install = _getSelectedInstall();
        if (install is null)
        {
            foreach (var r in AxisRows)
                r.SetLiveSource(null);
            return;
        }

        IReadOnlyList<DirectInputManager.DeviceInfo> devices;
        try
        {
            devices = _di.EnumerateDevices();
        }
        catch
        {
            devices = Array.Empty<DirectInputManager.DeviceInfo>();
        }

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
            d.Name.IndexOf(deviceName, StringComparison.OrdinalIgnoreCase) >= 0 ||
            deviceName.IndexOf(d.Name, StringComparison.OrdinalIgnoreCase) >= 0);

        return contains?.InstanceGuid;
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
            string actionId = DebugDiagnosticsService.CreateActionId("AUDCLR");
            DebugDiagnosticsService.Info($"USER ACTION | ActionId={actionId} | Source=AudioViewModel.ClearAxis | Function={function}");

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

            StatusText = "Cleared.";
            DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=AudioViewModel.ClearAxis | Scope=LocalAudioReload");
            LoadFromDisk();
        }
        catch (Exception ex)
        {
            StatusText = "";
            MessageBox.Show(ex.Message, "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadFromDisk(AxisBindingsSnapshotService.AxisBindingsSnapshot? snapshot = null)
    {
        var install = _getSelectedInstall();
        _axisBindingText.Clear();

        DebugDiagnosticsService.Info($"REFRESH BEGIN | Source=AudioViewModel.LoadFromDisk | Install={(install?.RegistryKeyName ?? "<null>")} | SnapshotMode={(snapshot is null ? "BuildNew" : "ReuseProvided")}");

        if (install is null)
        {
            foreach (var r in AxisRows)
            {
                r.BindingText = "Not set";
                r.SetLiveSource(null);
            }
            RebuildLiveBindings();
            DebugDiagnosticsService.Info("REFRESH END | Source=AudioViewModel.LoadFromDisk | Install=<null>");
            return;
        }

        try
        {
            if (snapshot is null)
                DebugDiagnosticsService.Info($"SNAPSHOT BUILD | Source=AudioViewModel.LoadFromDisk | BaseDir={install.BaseDir}");

            snapshot ??= _axisSnapshot.Build(install.BaseDir, AudioFunctions);

            var live = new Dictionary<AxisFunction, AxisRowViewModel.LiveAxisSource?>();

            foreach (var def in AxisCatalog.All.Where(d => AudioFunctions.Contains(d.Function)))
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

            foreach (var row in AxisRows)
            {
                row.BindingText = GetBindingText(row.Function);
                live.TryGetValue(row.Function, out var src);
                row.SetLiveSource(src);
            }

            RebuildLiveBindings();
            DebugDiagnosticsService.Info($"REFRESH END | Source=AudioViewModel.LoadFromDisk | Install={install.RegistryKeyName}");
        }
        catch
        {
            foreach (var r in AxisRows)
            {
                r.BindingText = "Not set";
                r.SetLiveSource(null);
            }
            RebuildLiveBindings();
            DebugDiagnosticsService.Warn($"REFRESH END | Source=AudioViewModel.LoadFromDisk | Install={install.RegistryKeyName} | Result=FallbackAfterException");
        }
    }

    private string GetBindingText(AxisFunction function)
    {
        return _axisBindingText.TryGetValue(function, out var s) ? s : "Not set";
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
            string actionId = DebugDiagnosticsService.CreateActionId("AUDASN");
            DebugDiagnosticsService.Info($"USER ACTION | ActionId={actionId} | Source=AudioViewModel.Assign | Function={function}");

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

                existing = new AxisExistingBinding(
                    DeviceName: deviceName,
                    ProductGuid: productGuid,
                    PhysicalAxisIndex: existingMap.Value.AxisIndex,
                    Invert: invert,
                    Deadzone: dz,
                    Saturation: sat,
                    Detents: null
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
                DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=AudioViewModel.Assign | Scope=LocalAudioReload");
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
                        : $"{deviceName} {axisName} is already assigned to:\n  - {string.Join("\n  - ", conflictNames)}\n\nReplace those with \"{axisDef.DisplayName}\"?\n\n(If you click Yes, the previous assignments will be cleared.)";

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

            try
            {
                var full = axisDat.ReadAll(install.BaseDir);
                new JoystickCalService().Write(install.BaseDir, full, _setupXml, sortingSvc);
            }
            catch { }

            StatusText = "Saved to User\\Config.";
            DebugDiagnosticsService.Info($"REFRESH REQUEST | ActionId={actionId} | Source=AudioViewModel.Assign | Scope=LocalAudioReload");
            LoadFromDisk();
        }
        catch (Exception ex)
        {
            StatusText = "";
            MessageBox.Show(ex.Message, "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
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