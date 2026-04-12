using System.Collections.ObjectModel;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Represents one device-slot cell in the unified Keymapping grid.
/// This is additive for now and mirrors the existing per-slot text/axis properties.
/// The UI will migrate to this model in a later step.
/// </summary>
public sealed class KeymappingDeviceCellViewModel : ViewModelBase
{
    public int SlotIndex { get; }
    public string Text { get; }
    public AxisRowViewModel? AxisRow { get; }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasAxisRow => AxisRow is not null;
    public bool IsEmpty => !HasText && !HasAxisRow;

    public KeymappingDeviceCellViewModel(int slotIndex, string text, AxisRowViewModel? axisRow)
    {
        SlotIndex = slotIndex;
        Text = text;
        AxisRow = axisRow;
    }

    public static ObservableCollection<KeymappingDeviceCellViewModel> BuildForRow(KeymappingGridRowViewModel row)
    {
        var cells = new ObservableCollection<KeymappingDeviceCellViewModel>();

        for (int slot = 0; slot < 16; slot++)
        {
            cells.Add(new KeymappingDeviceCellViewModel(
                slotIndex: slot,
                text: GetTextForSlot(row, slot),
                axisRow: GetAxisForSlot(row, slot)));
        }

        return cells;
    }

    private static string GetTextForSlot(KeymappingGridRowViewModel row, int slotIndex)
    {
        if (!row.IsKeyRow || row.KeyRow is null)
            return "";

        return slotIndex switch
        {
            0 => row.KeyRow.Z_Joy_0,
            1 => row.KeyRow.Z_Joy_1,
            2 => row.KeyRow.Z_Joy_2,
            3 => row.KeyRow.Z_Joy_3,
            4 => row.KeyRow.Z_Joy_4,
            5 => row.KeyRow.Z_Joy_5,
            6 => row.KeyRow.Z_Joy_6,
            7 => row.KeyRow.Z_Joy_7,
            8 => row.KeyRow.Z_Joy_8,
            9 => row.KeyRow.Z_Joy_9,
            10 => row.KeyRow.Z_Joy_10,
            11 => row.KeyRow.Z_Joy_11,
            12 => row.KeyRow.Z_Joy_12,
            13 => row.KeyRow.Z_Joy_13,
            14 => row.KeyRow.Z_Joy_14,
            15 => row.KeyRow.Z_Joy_15,
            _ => ""
        };
    }

    private static AxisRowViewModel? GetAxisForSlot(KeymappingGridRowViewModel row, int slotIndex)
    {
        if (!row.IsAxisRow || row.AssignedDeviceSlot != slotIndex)
            return null;

        return row.AxisRow;
    }
}