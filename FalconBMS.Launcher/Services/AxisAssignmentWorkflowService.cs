using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Views;
using System;
using System.Linq;
using System.Windows;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Handles the full Keymapping axis assignment workflow using the exact behavior
/// currently implemented in KeymappingView.xaml.cs.
/// </summary>
public sealed class AxisAssignmentWorkflowService
{
    private readonly SetupXmlService _setupXml = new();
    private readonly AxisMappingDatService _axisDat = new();

    public bool ShowDialogAndApply(Window owner, string baseDir, AxisFunction function)
    {
        var axisDef = AxisCatalog.Get(function);

        AxisExistingBinding? existing = null;
        var existingMap = _axisDat.ReadAxisMapping(baseDir, axisDef.MappingIndex);

        if (existingMap is not null)
        {
            int slotIndex = existingMap.Value.JoyNum - 2;
            var sortingExisting = new DeviceSortingService();

            string deviceName =
                sortingExisting.GetDeviceNameBySlot(baseDir, slotIndex)
                ?? $"Device Slot {slotIndex}";

            Guid? productGuid =
                sortingExisting.GetProductGuidBySlot(baseDir, slotIndex);

            bool invert = false;
            _setupXml.TryGetInvert(baseDir, function, out invert);

            AxCurve dz = AxCurve.None;
            _setupXml.TryGetDeadzone(baseDir, function, out dz);

            AxCurve sat = AxCurve.None;
            _setupXml.TryGetSaturation(baseDir, function, out sat);

            DetentPosition? det = null;
            if (function == AxisFunction.Throttle)
            {
                if (_setupXml.TryGetDetents(baseDir, deviceName, out var d))
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
            Owner = owner
        };

        bool? ok = win.ShowDialog();
        if (ok != true)
            return false;

        if (win.WasCleared)
        {
            _setupXml.ClearAxisBinding(baseDir, function);
            _axisDat.ClearAxisMapping(baseDir, axisDef.MappingIndex);

            try
            {
                var sorting = new DeviceSortingService();
                var full = _axisDat.ReadAll(baseDir);
                new JoystickCalService().Write(baseDir, full, _setupXml, sorting);
            }
            catch
            {
            }

            return true;
        }

        var sel = win.Result;
        if (sel is null)
            return false;

        var sortingSvc = new DeviceSortingService();

        var slotByProductGuid = sortingSvc.EnsureDevicesAndGetSlots(
            baseDir,
            new[] { (sel.DeviceProductGuid, sel.DeviceName) });

        int slot = slotByProductGuid[sel.DeviceProductGuid];
        int deviceCount = sortingSvc.GetDeviceCount(baseDir);

        int desiredJoyNum = slot + 2;
        int desiredAxisIndex = sel.PhysicalAxisIndex;

        var all = _axisDat.ReadAll(baseDir);

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
                return false;

            foreach (int mappingIdx in conflicts)
            {
                _axisDat.ClearAxisMapping(baseDir, mappingIdx);

                var def = AxisCatalog.All.FirstOrDefault(d => d.MappingIndex == mappingIdx);
                if (def is not null)
                    _setupXml.ClearAxisBinding(baseDir, def.Function);
            }
        }

        _axisDat.SetAxisMapping(
            baseDir: baseDir,
            mappingIndex: axisDef.MappingIndex,
            deviceSlotIndex: slot,
            primaryInstanceGuidForHeader: sel.DeviceInstanceGuid,
            physicalAxisIndex: sel.PhysicalAxisIndex,
            deviceCount: deviceCount,
            deadzone: AxCurveCodec.DeadzoneToInt(sel.Deadzone),
            saturation: AxCurveCodec.SaturationToInt(sel.Saturation),
            updateHeaderPrimary: axisDef.MappingIndex == 0
        );

        _setupXml.ApplyAxisBinding(baseDir, function, sel);

        if (function == AxisFunction.Throttle)
        {
            var detents = win.Detents ?? DetentPosition.Default;
            _setupXml.SetDetents(baseDir, sel.DeviceName, sel.DeviceInstanceGuid, detents);
        }

        try
        {
            var full = _axisDat.ReadAll(baseDir);
            new JoystickCalService().Write(baseDir, full, _setupXml, sortingSvc);
        }
        catch
        {
        }

        return true;
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