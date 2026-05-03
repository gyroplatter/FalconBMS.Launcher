using System;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.ViewModels;

public sealed class ControlGridDeviceCellViewModel : ViewModelBase
{
    private const double AxisBarUpdateThreshold = 0.003;

    private string _displayText = "";
    public string DisplayText
    {
        get => _displayText;
        set => Set(ref _displayText, value ?? "");
    }

    private bool _hasAxisBinding;
    public bool HasAxisBinding
    {
        get => _hasAxisBinding;
        set => Set(ref _hasAxisBinding, value);
    }

    private int _physicalAxisIndex = -1;
    public int PhysicalAxisIndex
    {
        get => _physicalAxisIndex;
        set => Set(ref _physicalAxisIndex, value);
    }

    private double _axisBarValue = 0.5;
    public double AxisBarValue
    {
        get => _axisBarValue;
        set
        {
            double clampedValue = Math.Max(0.0, Math.Min(1.0, value));

            // Live axis polling can fire about 60 times per second. Do not notify WPF
            // for tiny jitter-only changes because every notification can trigger a redraw.
            if (Math.Abs(_axisBarValue - clampedValue) < AxisBarUpdateThreshold)
                return;

            Set(ref _axisBarValue, clampedValue);
        }
    }

    private bool _showDetents;
    public bool ShowDetents
    {
        get => _showDetents;
        set => Set(ref _showDetents, value);
    }

    private double _idleDetentFraction = DetentPosition.DefaultIdleDetent / (double)DetentPosition.MaxAxisValue;
    public double IdleDetentFraction
    {
        get => _idleDetentFraction;
        set => Set(ref _idleDetentFraction, Math.Max(0.0, Math.Min(1.0, value)));
    }

    private double _afterburnerDetentFraction = DetentPosition.DefaultAfterburnerDetent / (double)DetentPosition.MaxAxisValue;
    public double AfterburnerDetentFraction
    {
        get => _afterburnerDetentFraction;
        set => Set(ref _afterburnerDetentFraction, Math.Max(0.0, Math.Min(1.0, value)));
    }
}