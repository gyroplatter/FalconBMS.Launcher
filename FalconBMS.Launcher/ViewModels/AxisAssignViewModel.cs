using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Drives the axis assignment popup. Polling and detection are intentionally separate:
/// mapped axes always poll so the live bar moves, but auto-detection is frozen until the user clicks Clear.
/// This matches the old non-binding launcher and prevents jitter from replacing an existing assignment.
/// </summary>
public sealed class AxisAssignViewModel : ViewModelBase, IDisposable
{
    private const int AxisMin = DetentPosition.MinAxisValue;
    private const int AxisMax = DetentPosition.MaxAxisValue;
    private const int AxisRange = AxisMax - AxisMin;

    // The first 600ms are ignored, then a candidate axis must stay
    // beyond the movement threshold for multiple ticks before it is accepted as the selected physical axis.
    private const int InitialSettleMs = 600;
    private const int StableHitCountRequired = 10;
    private const int MovementThreshold = AxisRange / 4;

    // Jitter guard:
    // Only accept a capture when one axis is clearly stronger than every other moving axis.
    // This prevents a noisy Z/slider axis from winning while the user is moving X/Y/Rx/etc.
    private const double DominantAxisRatio = 1.75;

    private readonly DirectInputManager _di = new();
    private readonly IReadOnlyList<DeviceBindingProfile> _deviceProfiles;
    private readonly ControlGridRowViewModel _axisRow;
    private readonly DeviceAxisDefinition? _definition;
    private readonly Action<AxisAssignViewModel> _saveAxisAssignment;
    private readonly Action _closeWindow;
    private readonly IntPtr _hwnd;

    private readonly Dictionary<string, JoystickSession> _sessionsByDeviceKey = new();
    private readonly Dictionary<string, int[]> _baselineByDeviceKey = new();
    private readonly Dictionary<string, int> _stableHitsByCandidate = new();

    /// <summary>
    /// One possible axis capture candidate for the current polling tick.
    /// We evaluate all devices/axes first, then only count the strongest one if it is clearly dominant.
    /// That avoids accepting background jitter from another axis just because it happened to cross threshold.
    /// </summary>
    private sealed class AxisCaptureCandidate
    {
        public required DeviceBindingProfile Device { get; init; }
        public required int AxisIndex { get; init; }
        public required int Delta { get; init; }

        public string CandidateKey => Device.DurableDeviceKey + ":" + AxisIndex;
    }

    private DispatcherTimer? _timer;
    private DateTime _captureStartedUtc;
    private bool _captureArmed;
    private bool _isCleared;

    private string? _selectedDeviceKey;
    private int? _selectedPhysicalAxisIndex;

    public AxCurve[] AxisCurveOptions { get; } =
    {
        AxCurve.None,
        AxCurve.Small,
        AxCurve.Medium,
        AxCurve.Large
    };

    public string TitleText { get; }
    public string LeftLabel { get; }
    public string RightLabel { get; }

    public Visibility DeadzoneVisibility => _definition?.SupportsDeadzone == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SaturationVisibility => _definition?.SupportsSaturation == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InvertVisibility => _definition?.SupportsInvert == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThrottleDetentVisibility => ShowThrottleDetents ? Visibility.Visible : Visibility.Collapsed;

    public bool ShowThrottleDetents =>
        _definition?.LayoutKind == DeviceAxisAssignmentLayoutKind.Throttle;

    private string _statusText = "Awaiting inputs";
    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    private double _axisBarValue = 0.5;
    public double AxisBarValue
    {
        get => _axisBarValue;
        private set => Set(ref _axisBarValue, value);
    }

    private bool _hasLiveAxis;
    public bool HasLiveAxis
    {
        get => _hasLiveAxis;
        private set => Set(ref _hasLiveAxis, value);
    }

    private string _conflictText = "";
    public string ConflictText
    {
        get => _conflictText;
        private set => Set(ref _conflictText, value);
    }

    private bool _hasAxisConflict;
    public bool HasAxisConflict
    {
        get => _hasAxisConflict;
        private set => Set(ref _hasAxisConflict, value);
    }

    private AxCurve _deadzoneCurve = AxCurve.None;
    public AxCurve DeadzoneCurve
    {
        get => _deadzoneCurve;
        set => Set(ref _deadzoneCurve, value);
    }

    private AxCurve _saturationCurve = AxCurve.None;
    public AxCurve SaturationCurve
    {
        get => _saturationCurve;
        set => Set(ref _saturationCurve, value);
    }

    private bool _invert;
    public bool Invert
    {
        get => _invert;
        set => Set(ref _invert, value);
    }

    private int _idleDetent = DetentPosition.DefaultIdleDetent;
    public int IdleDetent
    {
        get => _idleDetent;
        private set
        {
            if (Set(ref _idleDetent, ClampAxisValue(value)))
                OnPropertyChanged(nameof(IdleDetentFraction));
        }
    }

    private int _afterburnerDetent = DetentPosition.DefaultAfterburnerDetent;
    public int AfterburnerDetent
    {
        get => _afterburnerDetent;
        private set
        {
            if (Set(ref _afterburnerDetent, ClampAxisValue(value)))
                OnPropertyChanged(nameof(AfterburnerDetentFraction));
        }
    }

    public double IdleDetentFraction => IdleDetent / (double)AxisMax;
    public double AfterburnerDetentFraction => AfterburnerDetent / (double)AxisMax;

    public string? SelectedDeviceKey => _selectedDeviceKey;
    public int? SelectedPhysicalAxisIndex => _selectedPhysicalAxisIndex;
    public string LogicalAxisName => _axisRow.AxisLogicalAxisName;
    public bool IsCleared => _isCleared;

    public RelayCommand ClearCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SetIdleDetentCommand { get; }
    public RelayCommand SetAfterburnerDetentCommand { get; }

    public AxisAssignViewModel(
        ControlGridRowViewModel axisRow,
        IEnumerable<DeviceBindingProfile> deviceProfiles,
        string? initialDeviceKey,
        IntPtr hwnd,
        Action<AxisAssignViewModel> saveAxisAssignment,
        Action closeWindow)
    {
        _axisRow = axisRow;
        _deviceProfiles = deviceProfiles.ToList();
        _hwnd = hwnd;
        _saveAxisAssignment = saveAxisAssignment;
        _closeWindow = closeWindow;

        _definition = AxisDefinitionService.Find(axisRow.AxisLogicalAxisName);
        TitleText = "Assign " + (_definition?.DisplayName ?? axisRow.Mapping) + " Axis";
        LeftLabel = _definition?.LeftLabel ?? "";
        RightLabel = _definition?.RightLabel ?? "";

        LoadExistingMapping(initialDeviceKey);

        ClearCommand = new RelayCommand(ClearMapping);
        SaveCommand = new RelayCommand(SaveAndClose);
        CancelCommand = new RelayCommand(_closeWindow);
        SetIdleDetentCommand = new RelayCommand(() => SetDetentFromLiveAxis(isIdle: true));
        SetAfterburnerDetentCommand = new RelayCommand(() => SetDetentFromLiveAxis(isIdle: false));
    }

    public void Start()
    {
        Stop();

        _captureStartedUtc = DateTime.UtcNow;
        _stableHitsByCandidate.Clear();

        // An existing assignment should only poll the assigned axis. Auto-capture is off until Clear is clicked,
        // which is the critical old-launcher behavior that stops random jitter from remapping Pitch/Roll/etc.
        _captureArmed = !_selectedPhysicalAxisIndex.HasValue;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();

        StatusText = _captureArmed
            ? "Awaiting inputs: move the axis to assign"
            : "Assigned to " + GetSelectedDeviceName() + " / " + PhysicalAxisNameService.GetDisplayName(_selectedPhysicalAxisIndex!.Value);

        UpdateAxisConflict();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        foreach (JoystickSession session in _sessionsByDeviceKey.Values)
            session.Dispose();

        _sessionsByDeviceKey.Clear();
    }

    private void LoadExistingMapping(string? initialDeviceKey)
    {
        DeviceBindingProfile? mappedDevice = null;
        DeviceAxisBinding? mappedBinding = null;

        if (!string.IsNullOrWhiteSpace(initialDeviceKey))
        {
            mappedDevice = _deviceProfiles.FirstOrDefault(device => string.Equals(device.DurableDeviceKey, initialDeviceKey, StringComparison.OrdinalIgnoreCase));
            mappedBinding = mappedDevice?.AxisBindings.FirstOrDefault(binding =>
                string.Equals(binding.LogicalAxisName, _axisRow.AxisLogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                binding.PhysicalAxisIndex.HasValue);
        }

        if (mappedBinding is null)
        {
            mappedDevice = _deviceProfiles.FirstOrDefault(device => device.AxisBindings.Any(binding =>
                string.Equals(binding.LogicalAxisName, _axisRow.AxisLogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                binding.PhysicalAxisIndex.HasValue));

            mappedBinding = mappedDevice?.AxisBindings.FirstOrDefault(binding =>
                string.Equals(binding.LogicalAxisName, _axisRow.AxisLogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                binding.PhysicalAxisIndex.HasValue);
        }

        if (mappedDevice is null || mappedBinding is null)
            return;

        _selectedDeviceKey = mappedDevice.DurableDeviceKey;
        _selectedPhysicalAxisIndex = mappedBinding.PhysicalAxisIndex;
        DeadzoneCurve = ParseCurve(mappedBinding.Deadzone);
        SaturationCurve = ParseCurve(mappedBinding.Saturation);
        Invert = mappedBinding.Invert;
        IdleDetent = mappedBinding.IdleDetent ?? DetentPosition.DefaultIdleDetent;
        AfterburnerDetent = mappedBinding.AfterburnerDetent ?? DetentPosition.DefaultAfterburnerDetent;
        HasLiveAxis = true;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var candidates = new List<AxisCaptureCandidate>();

        foreach (DeviceBindingProfile device in _deviceProfiles.Where(device => device.IsConnected && device.AxisCount > 0))
        {
            if (!TryReadAxisValues(device, out int[] axisValues))
                continue;

            PollSelectedAxis(device, axisValues);

            // During the settle window, keep refreshing the baseline from the current hardware state.
            // This is important for noisy devices: we do not want the baseline to be whatever value
            // happened to exist on the very first tick after the popup opened.
            if ((DateTime.UtcNow - _captureStartedUtc).TotalMilliseconds < InitialSettleMs)
            {
                _baselineByDeviceKey[device.DurableDeviceKey] = (int[])axisValues.Clone();
                continue;
            }

            if (!_baselineByDeviceKey.ContainsKey(device.DurableDeviceKey))
                _baselineByDeviceKey[device.DurableDeviceKey] = (int[])axisValues.Clone();

            if (_captureArmed)
                AddMovedAxisCandidates(device, axisValues, candidates);
        }

        if (_captureArmed)
            AcceptDominantCandidate(candidates);
    }

    private void PollSelectedAxis(DeviceBindingProfile device, int[] axisValues)
    {
        if (!string.Equals(device.DurableDeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase))
            return;

        if (!_selectedPhysicalAxisIndex.HasValue)
            return;

        int axisIndex = _selectedPhysicalAxisIndex.Value;
        if (axisIndex < 0 || axisIndex >= axisValues.Length)
            return;

        // Match the official launcher: visual axis direction depends on both the
        // logical axis type and the saved Invert checkbox state. This affects only
        // the live UI bar; it does not force Invert=true in output files.
        AxisBarValue = NormalizeAxisValue(axisValues[axisIndex], LogicalAxisName, Invert);
        HasLiveAxis = true;
    }

    private void AddMovedAxisCandidates(
        DeviceBindingProfile device,
        int[] axisValues,
        List<AxisCaptureCandidate> candidates)
    {
        if (!_baselineByDeviceKey.TryGetValue(device.DurableDeviceKey, out int[] baseline))
            return;

        int axisLimit = Math.Min(axisValues.Length, Math.Max(0, device.AxisCount));

        for (int axisIndex = 0; axisIndex < axisLimit; axisIndex++)
        {
            int delta = Math.Abs(axisValues[axisIndex] - baseline[axisIndex]);

            if (delta < MovementThreshold)
                continue;

            candidates.Add(new AxisCaptureCandidate
            {
                Device = device,
                AxisIndex = axisIndex,
                Delta = delta
            });
        }
    }

    private void AcceptDominantCandidate(List<AxisCaptureCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            _stableHitsByCandidate.Clear();
            return;
        }

        AxisCaptureCandidate best = candidates
            .OrderByDescending(candidate => candidate.Delta)
            .First();

        AxisCaptureCandidate? secondBest = candidates
            .Where(candidate => candidate.CandidateKey != best.CandidateKey)
            .OrderByDescending(candidate => candidate.Delta)
            .FirstOrDefault();

        // A noisy axis can cross the raw movement threshold while the user is moving another control.
        // Only count the candidate when it is clearly stronger than the next moving axis.
        if (secondBest is not null && best.Delta < secondBest.Delta * DominantAxisRatio)
        {
            _stableHitsByCandidate.Clear();
            StatusText = "Move one axis clearly to assign";
            return;
        }

        foreach (string key in _stableHitsByCandidate.Keys.ToList())
        {
            if (!string.Equals(key, best.CandidateKey, StringComparison.OrdinalIgnoreCase))
                _stableHitsByCandidate[key] = 0;
        }

        int stableHits = _stableHitsByCandidate.TryGetValue(best.CandidateKey, out int current)
            ? current + 1
            : 1;

        _stableHitsByCandidate[best.CandidateKey] = stableHits;

        if (stableHits < StableHitCountRequired)
            return;

        _selectedDeviceKey = best.Device.DurableDeviceKey;
        _selectedPhysicalAxisIndex = best.AxisIndex;
        _captureArmed = false;
        HasLiveAxis = true;

        StatusText = "Captured " + GetSelectedDeviceName() + " / " + PhysicalAxisNameService.GetDisplayName(best.AxisIndex);
        UpdateAxisConflict();
    }

    private bool TryReadAxisValues(DeviceBindingProfile device, out int[] axisValues)
    {
        axisValues = Array.Empty<int>();

        if (!device.IsConnected)
            return false;

        if (!_sessionsByDeviceKey.TryGetValue(device.DurableDeviceKey, out JoystickSession session))
        {
            try
            {
                session = _di.OpenJoystick(device.InstanceGuid, _hwnd);
                _sessionsByDeviceKey[device.DurableDeviceKey] = session;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            axisValues = DirectInputManager.ReadAxisVector(session.ReadState());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ClearMapping()
    {
        _selectedDeviceKey = null;
        _selectedPhysicalAxisIndex = null;
        _isCleared = true;
        _captureArmed = true;
        _captureStartedUtc = DateTime.UtcNow;
        _stableHitsByCandidate.Clear();
        _baselineByDeviceKey.Clear();
        HasLiveAxis = false;
        AxisBarValue = 0.5;
        ConflictText = "";
        HasAxisConflict = false;
        StatusText = "Cleared. Move the axis to assign, or Save to leave this unmapped.";
    }

    private void SaveAndClose()
    {
        _saveAxisAssignment(this);
        _closeWindow();
    }

    private void SetDetentFromLiveAxis(bool isIdle)
    {
        int current = ClampAxisValue((int)Math.Round(AxisBarValue * AxisMax));

        if (isIdle)
            IdleDetent = current;
        else
            AfterburnerDetent = current;
    }

    private string GetSelectedDeviceName()
    {
        DeviceBindingProfile? device = _deviceProfiles.FirstOrDefault(d => string.Equals(d.DurableDeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase));
        return device?.ProductName ?? device?.InstanceName ?? _selectedDeviceKey ?? "device";
    }

    private void UpdateAxisConflict()
    {
        DeviceAxisBinding? conflict = FindAxisConflict();

        if (conflict is null)
        {
            ConflictText = "";
            HasAxisConflict = false;
            return;
        }

        string conflictName = GetLogicalAxisDisplayName(conflict.LogicalAxisName);

        ConflictText = "Axis input currently bound to: " + conflictName + "\nClick \"Save\" to replace the existing assignment.";
        HasAxisConflict = true;
    }

    private DeviceAxisBinding? FindAxisConflict()
    {
        if (string.IsNullOrWhiteSpace(_selectedDeviceKey) || !_selectedPhysicalAxisIndex.HasValue)
            return null;

        DeviceBindingProfile? selectedDevice = _deviceProfiles.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, _selectedDeviceKey, StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null)
            return null;

        return selectedDevice.AxisBindings.FirstOrDefault(binding =>
            binding.PhysicalAxisIndex == _selectedPhysicalAxisIndex.Value &&
            !string.Equals(binding.LogicalAxisName, LogicalAxisName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetLogicalAxisDisplayName(string logicalAxisName)
    {
        DeviceAxisDefinition? definition = AxisDefinitionService.Find(logicalAxisName);
        return definition?.DisplayName ?? logicalAxisName;
    }

    private static AxCurve ParseCurve(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out AxCurve curve)
            ? curve
            : AxCurve.None;
    }

    private static int ClampAxisValue(int value)
    {
        if (value < AxisMin) return AxisMin;
        if (value > AxisMax) return AxisMax;
        return value;
    }

    public static double NormalizeAxisValue(int rawValue, string logicalAxisName, bool invert)
    {
        int clamped = ClampAxisValue(rawValue);
        double normalized = clamped / (double)AxisMax;

        /*
        Match the official launcher.

        Some axes are visually reversed by default because their physical movement
        reads opposite to the direction label shown in the UI. That default visual
        reversal is separate from the saved Invert checkbox.

        Opposite-movement axes:
        - Invert unchecked = reversed visual bar
        - Invert checked   = normal visual bar

        Normal axes:
        - Invert unchecked = normal visual bar
        - Invert checked   = reversed visual bar
        */
        bool reverseDisplay = HasOppositeVisualMovement(logicalAxisName)
            ? !invert
            : invert;

        return reverseDisplay
            ? 1.0 - normalized
            : normalized;
    }

    private static bool HasOppositeVisualMovement(string logicalAxisName)
    {
        string normalizedName = AxisDefinitionService.NormalizeLogicalAxisName(logicalAxisName);

        return string.Equals(normalizedName, "Throttle", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Throttle_Right", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Toe_Brake", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Toe_Brake_Right", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Intercom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "IntercomVolumeVolume", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "COMM_Channel_1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "COMM_Channel_2", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "MSL_Volume", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "Threat_Volume", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "AI_vs_IVC", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedName, "ILS_Volume_Knob", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Stop();
        _di.Dispose();
    }
}
