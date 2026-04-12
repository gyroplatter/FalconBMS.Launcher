using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Utils;
using System;
using System.Windows;
using System.Windows.Media;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Represents a single axis row in the UI, including labels, live values, binding state,
/// and optional grouping metadata used by the Controls tab.
/// </summary>
public sealed class AxisRowViewModel : ViewModelBase
{
    public AxisFunction Function { get; }
    public int MappingIndex { get; }
    public string DisplayName { get; }
    public string GroupName { get; }

    private string _bindingText = "Not set";
    public string BindingText
    {
        get => _bindingText;
        set => Set(ref _bindingText, value);
    }

    // ===== Axis bar (tabs) =====

    private bool _axisBarEnabled;
    public bool AxisBarEnabled
    {
        get => _axisBarEnabled;
        set => Set(ref _axisBarEnabled, value);
    }

    private double _axisBarValue;
    public double AxisBarValue
    {
        get => _axisBarValue;
        set => Set(ref _axisBarValue, value);
    }

    private Brush _axisBarFillBrush = SystemColors.HighlightBrush;
    public Brush AxisBarFillBrush
    {
        get => _axisBarFillBrush;
        set => Set(ref _axisBarFillBrush, value);
    }

    private string _axisBarOverlayText = string.Empty;
    public string AxisBarOverlayText
    {
        get => _axisBarOverlayText;
        set => Set(ref _axisBarOverlayText, value);
    }

    private Visibility _axisBarOverlayVisibility = Visibility.Collapsed;
    public Visibility AxisBarOverlayVisibility
    {
        get => _axisBarOverlayVisibility;
        set => Set(ref _axisBarOverlayVisibility, value);
    }

    // ===== Throttle detent marker lines (tabs) =====

    private bool _showDetentMarkers;
    public bool ShowDetentMarkers
    {
        get => _showDetentMarkers;
        set => Set(ref _showDetentMarkers, value);
    }

    private double _idleDetentFraction;
    public double IdleDetentFraction
    {
        get => _idleDetentFraction;
        set => Set(ref _idleDetentFraction, value);
    }

    private double _abDetentFraction = 1.0;
    public double AbDetentFraction
    {
        get => _abDetentFraction;
        set => Set(ref _abDetentFraction, value);
    }

    // Live source info set by the parent tab VM.
    private LiveAxisSource? _live;

    public void SetLiveSource(LiveAxisSource? src)
    {
        _live = src;

        if (src is null)
        {
            AxisBarEnabled = false;
            AxisBarValue = 0.0;
            AxisBarFillBrush = SystemColors.HighlightBrush;
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;

            ShowDetentMarkers = false;
            IdleDetentFraction = 0.0;
            AbDetentFraction = 1.0;
        }
        else
        {
            AxisBarEnabled = true;
            AxisBarFillBrush = SystemColors.HighlightBrush;
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;

            if (Function == AxisFunction.Throttle)
            {
                ShowDetentMarkers = true;
                IdleDetentFraction = (double)src.Detents.IDLE / (double)DetentPosition.AxisMax;
                AbDetentFraction = (double)src.Detents.AB / (double)DetentPosition.AxisMax;
            }
            else
            {
                ShowDetentMarkers = false;
                IdleDetentFraction = 0.0;
                AbDetentFraction = 1.0;
            }
        }
    }

    public LiveAxisSource? GetLiveSource() => _live;

    public void UpdateFromRawAxisValue(int rawAxisValue)
    {
        if (_live is null)
            return;

        int v = rawAxisValue;
        if (v < DetentPosition.AxisMin) v = DetentPosition.AxisMin;
        if (v > DetentPosition.AxisMax) v = DetentPosition.AxisMax;

        double rawNorm = (double)v / (double)DetentPosition.AxisMax; // 0..1

        bool reverseDisplay = IsSpecialAxisForDisplay(Function) ? !_live.Invert : _live.Invert;
        double displayNorm = reverseDisplay ? (1.0 - rawNorm) : rawNorm;

        AxisBarValue = displayNorm;

        if (Function == AxisFunction.Throttle)
        {
            UpdateThrottleDetentFeedback(v, _live.Invert, _live.Detents);
        }
        else
        {
            AxisBarFillBrush = SystemColors.HighlightBrush;
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;
        }
    }

    private void UpdateThrottleDetentFeedback(int raw, bool invert, DetentPosition detents)
    {
        // Match AxisAssignViewModel behavior:
        // compare in "detent space" using the original transform.
        int current = invert
            ? DetentPosition.AxisMin + raw
            : DetentPosition.AxisMax - raw;

        if (current < detents.IDLE)
        {
            AxisBarFillBrush = Brushes.IndianRed;
            AxisBarOverlayText = "IDLE CUTOFF";
            AxisBarOverlayVisibility = Visibility.Visible;
        }
        else if (current > detents.AB)
        {
            AxisBarFillBrush = Brushes.LightGreen;
            AxisBarOverlayText = "AFTERBURNER";
            AxisBarOverlayVisibility = Visibility.Visible;
        }
        else
        {
            AxisBarFillBrush = SystemColors.HighlightBrush;
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;
        }
    }

    private static bool IsSpecialAxisForDisplay(AxisFunction f)
    {
        return
            f == AxisFunction.Throttle ||
            f == AxisFunction.Throttle_Right ||
            f == AxisFunction.COMM_Channel_1 ||
            f == AxisFunction.COMM_Channel_2 ||
            f == AxisFunction.MSL_Volume ||
            f == AxisFunction.Threat_Volume ||
            f == AxisFunction.IntercomVolumeVolume ||
            f == AxisFunction.AI_vs_IVC ||
            f == AxisFunction.ILS_Volume_Knob;
    }

    public RelayCommand AssignCommand { get; }
    public RelayCommand ClearCommand { get; }

    public AxisRowViewModel(
        AxisActionDef def,
        Func<AxisFunction, bool> canExecute,
        Action<AxisFunction> assign,
        Action<AxisFunction> clear,
        string groupName = "",
        string? displayNameOverride = null)
    {
        Function = def.Function;
        MappingIndex = def.MappingIndex;
        DisplayName = string.IsNullOrWhiteSpace(displayNameOverride) ? def.DisplayName : displayNameOverride;
        GroupName = groupName;

        AssignCommand = new RelayCommand(() => assign(Function), () => canExecute(Function));
        ClearCommand = new RelayCommand(() => clear(Function), () => canExecute(Function));
    }

    public sealed record LiveAxisSource(
        string DeviceName,
        Guid? ProductGuid,
        int PhysicalAxisIndex,
        bool Invert,
        DetentPosition Detents
    );
}