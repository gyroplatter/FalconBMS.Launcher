namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlGridDeviceCellViewModel : ViewModelBase
{
    public string DisplayText { get; init; } = "";

    public bool HasAxisBinding { get; init; }

    public int PhysicalAxisIndex { get; init; } = -1;

    private double _axisBarValue;
    public double AxisBarValue
    {
        get => _axisBarValue;
        set => Set(ref _axisBarValue, value);
    }
}