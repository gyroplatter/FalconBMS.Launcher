using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Drives the axis-pair assignment popup.
/// The UI edits one physical X/Y control, but saving still writes two normal BMS axis bindings.
/// </summary>
public sealed class AxisPairAssignViewModel : ViewModelBase, IDisposable
{
    private const int AxisMin = DetentPosition.MinAxisValue;
    private const int AxisMax = DetentPosition.MaxAxisValue;
    private const int AxisRange = AxisMax - AxisMin;

    private const int InitialSettleMs = 600;
    private const int StableHitCountRequired = 10;
    private const int MovementThreshold = AxisRange / 4;
    private const double DominantAxisRatio = 1.5;

    private readonly DirectInputManager _di = new();
    private readonly IReadOnlyList<DeviceBindingProfile> _deviceProfiles;
    private readonly Action<AxisPairAssignViewModel> _saveAxisAssignment;
    private readonly Action _closeWindow;
    private readonly IntPtr _hwnd;
    private readonly string _actionId = DebugDiagnosticsService.CreateActionId("AXISPAIRUI");

    private readonly Dictionary<string, JoystickSession> _sessionsByDeviceKey = new();
    private readonly Dictionary<string, int[]> _baselineByDeviceKey = new();
    private readonly Dictionary<string, int> _stableHitsByCandidate = new();

    private DispatcherTimer? _timer;
    private DateTime _captureStartedUtc;
    private AxisPairCaptureTarget _captureTarget = AxisPairCaptureTarget.None;

    private sealed class AxisCaptureCandidate
    {
        public required DeviceBindingProfile Device { get; init; }
        public required int AxisIndex { get; init; }
        public required int Delta { get; init; }

        public string CandidateKey => Device.DurableDeviceKey + ":" + AxisIndex;
    }

    public AxisPairDefinition PairDefinition { get; }

    public AxCurve[] AxisCurveOptions { get; } =
    {
        AxCurve.None,
        AxCurve.Small,
        AxCurve.Medium,
        AxCurve.Large
    };

    public AxisEditViewModel Primary { get; }
    public AxisEditViewModel Secondary { get; }

    private double _rawX;
    public double RawX
    {
        get => _rawX;
        private set => Set(ref _rawX, value);
    }

    private double _rawY;
    public double RawY
    {
        get => _rawY;
        private set => Set(ref _rawY, value);
    }

    private double _outputX;
    public double OutputX
    {
        get => _outputX;
        private set => Set(ref _outputX, value);
    }

    private double _outputY;
    public double OutputY
    {
        get => _outputY;
        private set => Set(ref _outputY, value);
    }

    private double _deadzoneRadius;
    public double DeadzoneRadius
    {
        get => _deadzoneRadius;
        private set => Set(ref _deadzoneRadius, value);
    }

    private bool _isMappingPrimary;
    public bool IsMappingPrimary
    {
        get => _isMappingPrimary;
        private set => Set(ref _isMappingPrimary, value);
    }

    private bool _isMappingSecondary;
    public bool IsMappingSecondary
    {
        get => _isMappingSecondary;
        private set => Set(ref _isMappingSecondary, value);
    }

    public RelayCommand MapPrimaryCommand { get; }
    public RelayCommand MapSecondaryCommand { get; }
    public RelayCommand ClearBothCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public AxisPairAssignViewModel(
        AxisPairDefinition pairDefinition,
        IEnumerable<DeviceBindingProfile> deviceProfiles,
        string? initialDeviceKey,
        IntPtr hwnd,
        Action<AxisPairAssignViewModel> saveAxisAssignment,
        Action closeWindow)
    {
        PairDefinition = pairDefinition;
        _deviceProfiles = deviceProfiles.ToList();
        _hwnd = hwnd;
        _saveAxisAssignment = saveAxisAssignment;
        _closeWindow = closeWindow;

        Primary = new AxisEditViewModel(
            pairDefinition.PrimaryLogicalAxisName,
            pairDefinition.PrimaryTitle,
            pairDefinition.PrimaryMapButtonText,
            initialDeviceKey,
            _deviceProfiles);

        Secondary = new AxisEditViewModel(
            pairDefinition.SecondaryLogicalAxisName,
            pairDefinition.SecondaryTitle,
            pairDefinition.SecondaryMapButtonText,
            initialDeviceKey,
            _deviceProfiles);

        MapPrimaryCommand = new RelayCommand(() => StartCapture(AxisPairCaptureTarget.Primary));
        MapSecondaryCommand = new RelayCommand(() => StartCapture(AxisPairCaptureTarget.Secondary));
        ClearBothCommand = new RelayCommand(ClearBothAxes);
        SaveCommand = new RelayCommand(SaveAndClose);
        CancelCommand = new RelayCommand(CancelAndClose);

        DebugDiagnosticsService.Info(
            $"Axis pair popup created. | ActionId={_actionId} | PairId={PairDefinition.PairId} | InitialClickedDeviceKey={initialDeviceKey ?? "<null>"} | PrimaryDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | PrimaryAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)} | SecondaryDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | SecondaryAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)}");
    }

    public void Start()
    {
        Stop();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();

        UpdateAxisConflicts();
        UpdateLiveGraphFromCurrentAssignments();
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

    private void StartCapture(AxisPairCaptureTarget target)
    {
        _captureTarget = target;
        _captureStartedUtc = DateTime.UtcNow;
        _baselineByDeviceKey.Clear();
        _stableHitsByCandidate.Clear();

        IsMappingPrimary = target == AxisPairCaptureTarget.Primary;
        IsMappingSecondary = target == AxisPairCaptureTarget.Secondary;

        AxisEditViewModel axis = GetAxis(target);
        axis.StatusText = "Awaiting input: move this axis clearly";
        axis.ConflictText = "";
        axis.HasAxisConflict = false;

        DebugDiagnosticsService.Info(
            $"Axis pair capture armed. | ActionId={_actionId} | PairId={PairDefinition.PairId} | Target={target} | LogicalAxis={axis.LogicalAxisName}");
    }

    private void ClearBothAxes()
    {
        DebugDiagnosticsService.Info(
            $"Axis pair clear both clicked. | ActionId={_actionId} | PairId={PairDefinition.PairId} | PrimaryLogicalAxis={Primary.LogicalAxisName} | PrimaryPreviousDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | PrimaryPreviousPhysicalAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)} | SecondaryLogicalAxis={Secondary.LogicalAxisName} | SecondaryPreviousDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | SecondaryPreviousPhysicalAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)}");

        // Clearing the pair should never leave capture/listening mode active.
        _captureTarget = AxisPairCaptureTarget.None;
        _baselineByDeviceKey.Clear();
        _stableHitsByCandidate.Clear();
        IsMappingPrimary = false;
        IsMappingSecondary = false;

        ClearAxisEdit(Primary, "Cleared. Click Map Pitch to assign this axis.");
        ClearAxisEdit(Secondary, "Cleared. Click Map Roll to assign this axis.");

        UpdateLiveGraphFromCurrentAssignments();
    }

    private static void ClearAxisEdit(AxisEditViewModel axis, string statusText)
    {
        axis.SelectedDeviceKey = null;
        axis.SelectedPhysicalAxisIndex = null;
        axis.IsCleared = true;
        axis.StatusText = statusText;
        axis.ConflictText = "";
        axis.HasAxisConflict = false;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var candidates = new List<AxisCaptureCandidate>();
        bool sawAnyAssignedAxis = false;

        foreach (DeviceBindingProfile device in _deviceProfiles.Where(device => device.IsConnected && device.AxisCount > 0))
        {
            if (!TryReadAxisValues(device, out int[] axisValues))
                continue;

            if (PollAssignedAxes(device, axisValues))
                sawAnyAssignedAxis = true;

            if (_captureTarget == AxisPairCaptureTarget.None)
                continue;

            if ((DateTime.UtcNow - _captureStartedUtc).TotalMilliseconds < InitialSettleMs)
            {
                _baselineByDeviceKey[device.DurableDeviceKey] = (int[])axisValues.Clone();
                continue;
            }

            if (!_baselineByDeviceKey.ContainsKey(device.DurableDeviceKey))
                _baselineByDeviceKey[device.DurableDeviceKey] = (int[])axisValues.Clone();

            AddMovedAxisCandidates(device, axisValues, candidates);
        }

        if (_captureTarget != AxisPairCaptureTarget.None)
            AcceptDominantCandidate(candidates);
        else if (!sawAnyAssignedAxis)
            UpdateLiveGraphFromCurrentAssignments();
    }

    private bool PollAssignedAxes(DeviceBindingProfile device, int[] axisValues)
    {
        bool updated = false;

        if (TryGetAxisValueForEdit(Primary, device, axisValues, out int primaryValue))
        {
            RawY = RawAxisToSigned(primaryValue, Primary.LogicalAxisName);
            OutputY = ApplyAxisOutputCurve(RawY, Primary);
            updated = true;
        }

        if (TryGetAxisValueForEdit(Secondary, device, axisValues, out int secondaryValue))
        {
            RawX = RawAxisToSigned(secondaryValue, Secondary.LogicalAxisName);
            OutputX = ApplyAxisOutputCurve(RawX, Secondary);
            updated = true;
        }

        if (updated)
            DeadzoneRadius = Math.Max(GetDeadzoneRadius(Primary.DeadzoneCurve), GetDeadzoneRadius(Secondary.DeadzoneCurve));

        return updated;
    }

    private static bool TryGetAxisValueForEdit(AxisEditViewModel axis, DeviceBindingProfile device, int[] axisValues, out int value)
    {
        value = 0;

        if (!string.Equals(device.DurableDeviceKey, axis.SelectedDeviceKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!axis.SelectedPhysicalAxisIndex.HasValue)
            return false;

        int axisIndex = axis.SelectedPhysicalAxisIndex.Value;
        if (axisIndex < 0 || axisIndex >= axisValues.Length)
            return false;

        value = axisValues[axisIndex];
        return true;
    }

    private void UpdateLiveGraphFromCurrentAssignments()
    {
        if (!Primary.SelectedPhysicalAxisIndex.HasValue)
        {
            RawY = 0;
            OutputY = 0;
        }

        if (!Secondary.SelectedPhysicalAxisIndex.HasValue)
        {
            RawX = 0;
            OutputX = 0;
        }

        DeadzoneRadius = Math.Max(GetDeadzoneRadius(Primary.DeadzoneCurve), GetDeadzoneRadius(Secondary.DeadzoneCurve));
    }

    private void AddMovedAxisCandidates(DeviceBindingProfile device, int[] axisValues, List<AxisCaptureCandidate> candidates)
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

        AxisEditViewModel targetAxis = GetAxis(_captureTarget);

        if (secondBest is not null && best.Delta < secondBest.Delta * DominantAxisRatio)
        {
            _stableHitsByCandidate.Clear();
            targetAxis.StatusText = "Move one axis clearly to assign";
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

        targetAxis.SelectedDeviceKey = best.Device.DurableDeviceKey;
        targetAxis.SelectedPhysicalAxisIndex = best.AxisIndex;
        targetAxis.IsCleared = false;
        targetAxis.StatusText = GetDeviceDisplayName(best.Device) + " / " + PhysicalAxisNameService.GetDisplayName(best.AxisIndex);

        _captureTarget = AxisPairCaptureTarget.None;
        IsMappingPrimary = false;
        IsMappingSecondary = false;
        _stableHitsByCandidate.Clear();
        _baselineByDeviceKey.Clear();

        DebugDiagnosticsService.Info(
            $"Axis pair axis captured. | ActionId={_actionId} | PairId={PairDefinition.PairId} | LogicalAxis={targetAxis.LogicalAxisName} | Device={GetDeviceDisplayName(best.Device)} | DeviceKey={best.Device.DurableDeviceKey} | PhysicalAxis={FormatPhysicalAxis(best.AxisIndex)} | Delta={best.Delta} | StableHits={stableHits}");

        UpdateAxisConflicts();
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

    private void UpdateAxisConflicts()
    {
        UpdateAxisConflict(Primary);
        UpdateAxisConflict(Secondary);
    }

    private void UpdateAxisConflict(AxisEditViewModel axis)
    {
        DeviceAxisBinding? conflict = FindAxisConflict(axis);

        if (conflict is null)
        {
            axis.ConflictText = "";
            axis.HasAxisConflict = false;
            return;
        }

        string conflictName = GetLogicalAxisDisplayName(conflict.LogicalAxisName);
        axis.ConflictText = "Axis input currently bound to: " + conflictName + "\nClick \"Save\" to replace the existing assignment.";
        axis.HasAxisConflict = true;

        DebugDiagnosticsService.Warn(
            $"Axis pair conflict found. | ActionId={_actionId} | PairId={PairDefinition.PairId} | LogicalAxis={axis.LogicalAxisName} | SelectedDeviceKey={axis.SelectedDeviceKey ?? "<null>"} | SelectedPhysicalAxis={FormatPhysicalAxis(axis.SelectedPhysicalAxisIndex)} | ConflictingLogicalAxis={conflict.LogicalAxisName} | ConflictingDisplayName={conflictName}");
    }

    private DeviceAxisBinding? FindAxisConflict(AxisEditViewModel axis)
    {
        if (string.IsNullOrWhiteSpace(axis.SelectedDeviceKey) || !axis.SelectedPhysicalAxisIndex.HasValue)
            return null;

        DeviceBindingProfile? selectedDevice = _deviceProfiles.FirstOrDefault(device =>
            string.Equals(device.DurableDeviceKey, axis.SelectedDeviceKey, StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null)
            return null;

        return selectedDevice.AxisBindings.FirstOrDefault(binding =>
            binding.PhysicalAxisIndex == axis.SelectedPhysicalAxisIndex.Value &&
            !string.Equals(binding.LogicalAxisName, axis.LogicalAxisName, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveAndClose()
    {
        DebugDiagnosticsService.Info(
            $"Axis pair save clicked. | ActionId={_actionId} | PairId={PairDefinition.PairId} | PrimaryDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | PrimaryAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)} | PrimaryCleared={Primary.IsCleared} | SecondaryDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | SecondaryAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)} | SecondaryCleared={Secondary.IsCleared}");

        if (!ValidateBeforeSave())
            return;

        _saveAxisAssignment(this);
        _closeWindow();
    }

    private bool ValidateBeforeSave()
    {
        ClearPairValidationWarning();

        if (Primary.SelectedPhysicalAxisIndex.HasValue &&
            Secondary.SelectedPhysicalAxisIndex.HasValue &&
            string.Equals(Primary.SelectedDeviceKey, Secondary.SelectedDeviceKey, StringComparison.OrdinalIgnoreCase) &&
            Primary.SelectedPhysicalAxisIndex.Value == Secondary.SelectedPhysicalAxisIndex.Value)
        {
            string warningText = "Pitch and Roll cannot use the same physical axis. Map one of them to a different axis before saving.";

            Primary.ConflictText = warningText;
            Primary.HasAxisConflict = true;

            Secondary.ConflictText = warningText;
            Secondary.HasAxisConflict = true;

            DebugDiagnosticsService.Warn(
                $"Axis pair save blocked because both axes use the same physical input. | ActionId={_actionId} | PairId={PairDefinition.PairId} | PrimaryLogicalAxis={Primary.LogicalAxisName} | SecondaryLogicalAxis={Secondary.LogicalAxisName} | DeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | PhysicalAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)}");

            return false;
        }

        return true;
    }

    private void ClearPairValidationWarning()
    {
        if (Primary.HasAxisConflict &&
            Primary.ConflictText.StartsWith("Pitch and Roll cannot use the same physical axis.", StringComparison.OrdinalIgnoreCase))
        {
            Primary.ConflictText = "";
            Primary.HasAxisConflict = false;
        }

        if (Secondary.HasAxisConflict &&
            Secondary.ConflictText.StartsWith("Pitch and Roll cannot use the same physical axis.", StringComparison.OrdinalIgnoreCase))
        {
            Secondary.ConflictText = "";
            Secondary.HasAxisConflict = false;
        }
    }

    private void CancelAndClose()
    {
        DebugDiagnosticsService.Info($"Axis pair assign canceled. | ActionId={_actionId} | PairId={PairDefinition.PairId}");
        _closeWindow();
    }

    private AxisEditViewModel GetAxis(AxisPairCaptureTarget target)
    {
        return target == AxisPairCaptureTarget.Secondary ? Secondary : Primary;
    }

    private static double RawAxisToSigned(int rawValue, string logicalAxisName)
    {
        // Reuse the existing single-axis direction normalization so Pitch/Roll graph movement
        // matches the labels users already see in the old Assign Axis popup.
        double displayed = AxisAssignViewModel.NormalizeAxisValue(rawValue, logicalAxisName, invert: false);
        return (displayed - 0.5) * 2.0;
    }

    private static double ApplyAxisOutputCurve(double rawSignedValue, AxisEditViewModel axis)
    {
        double value = axis.Invert ? -rawSignedValue : rawSignedValue;
        double sign = Math.Sign(value);
        double magnitude = Math.Abs(value);

        double deadzone = GetDeadzoneRadius(axis.DeadzoneCurve);
        if (magnitude <= deadzone)
            return 0.0;

        if (deadzone > 0.0)
            magnitude = (magnitude - deadzone) / (1.0 - deadzone);

        double saturation = GetSaturationLimit(axis.SaturationCurve);
        if (saturation < 1.0)
            magnitude = Math.Min(1.0, magnitude / saturation);

        return Math.Max(-1.0, Math.Min(1.0, sign * magnitude));
    }

    private static double GetDeadzoneRadius(AxCurve curve)
    {
        return curve switch
        {
            AxCurve.Small => 0.08,
            AxCurve.Medium => 0.15,
            AxCurve.Large => 0.25,
            _ => 0.0
        };
    }

    private static double GetSaturationLimit(AxCurve curve)
    {
        return curve switch
        {
            AxCurve.Small => 0.85,
            AxCurve.Medium => 0.70,
            AxCurve.Large => 0.55,
            _ => 1.0
        };
    }

    private static AxCurve ParseCurve(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out AxCurve curve)
            ? curve
            : AxCurve.None;
    }

    private static string GetLogicalAxisDisplayName(string logicalAxisName)
    {
        DeviceAxisDefinition? definition = AxisDefinitionService.Find(logicalAxisName);
        return definition?.DisplayName ?? logicalAxisName;
    }

    private static string FormatPhysicalAxis(int? physicalAxisIndex)
    {
        return physicalAxisIndex.HasValue
            ? PhysicalAxisNameService.GetDisplayName(physicalAxisIndex.Value) + $"({physicalAxisIndex.Value})"
            : "<null>";
    }

    private static string GetDeviceDisplayName(DeviceBindingProfile device)
    {
        if (!string.IsNullOrWhiteSpace(device.ProductName))
            return device.ProductName;

        if (!string.IsNullOrWhiteSpace(device.InstanceName))
            return device.InstanceName;

        return device.DurableDeviceKey;
    }

    public void Dispose()
    {
        Stop();
        _di.Dispose();
    }

    public sealed class AxisEditViewModel : ViewModelBase
    {
        private string? _selectedDeviceKey;
        private int? _selectedPhysicalAxisIndex;
        private string _statusText = "Not assigned";
        private string _conflictText = "";
        private bool _hasAxisConflict;
        private AxCurve _deadzoneCurve = AxCurve.None;
        private AxCurve _saturationCurve = AxCurve.None;
        private bool _invert;
        private bool _isCleared;

        public AxisEditViewModel(
            string logicalAxisName,
            string titleText,
            string mapButtonText,
            string? initialDeviceKey,
            IReadOnlyList<DeviceBindingProfile> deviceProfiles)
        {
            LogicalAxisName = logicalAxisName;
            TitleText = titleText;
            MapButtonText = mapButtonText;

            LoadExistingMapping(initialDeviceKey, deviceProfiles);
        }

        public string LogicalAxisName { get; }
        public string TitleText { get; }
        public string MapButtonText { get; }

        public string? SelectedDeviceKey
        {
            get => _selectedDeviceKey;
            set => Set(ref _selectedDeviceKey, value);
        }

        public int? SelectedPhysicalAxisIndex
        {
            get => _selectedPhysicalAxisIndex;
            set => Set(ref _selectedPhysicalAxisIndex, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public string ConflictText
        {
            get => _conflictText;
            set => Set(ref _conflictText, value);
        }

        public bool HasAxisConflict
        {
            get => _hasAxisConflict;
            set => Set(ref _hasAxisConflict, value);
        }

        public AxCurve DeadzoneCurve
        {
            get => _deadzoneCurve;
            set => Set(ref _deadzoneCurve, value);
        }

        public AxCurve SaturationCurve
        {
            get => _saturationCurve;
            set => Set(ref _saturationCurve, value);
        }

        public bool Invert
        {
            get => _invert;
            set => Set(ref _invert, value);
        }

        public bool IsCleared
        {
            get => _isCleared;
            set => Set(ref _isCleared, value);
        }

        private void LoadExistingMapping(string? initialDeviceKey, IReadOnlyList<DeviceBindingProfile> deviceProfiles)
        {
            DeviceBindingProfile? mappedDevice = null;
            DeviceAxisBinding? mappedBinding = null;

            if (!string.IsNullOrWhiteSpace(initialDeviceKey))
            {
                mappedDevice = deviceProfiles.FirstOrDefault(device =>
                    string.Equals(device.DurableDeviceKey, initialDeviceKey, StringComparison.OrdinalIgnoreCase));

                mappedBinding = mappedDevice?.AxisBindings.FirstOrDefault(binding =>
                    string.Equals(binding.LogicalAxisName, LogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                    binding.PhysicalAxisIndex.HasValue);
            }

            if (mappedBinding is null)
            {
                mappedDevice = deviceProfiles.FirstOrDefault(device => device.AxisBindings.Any(binding =>
                    string.Equals(binding.LogicalAxisName, LogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                    binding.PhysicalAxisIndex.HasValue));

                mappedBinding = mappedDevice?.AxisBindings.FirstOrDefault(binding =>
                    string.Equals(binding.LogicalAxisName, LogicalAxisName, StringComparison.OrdinalIgnoreCase) &&
                    binding.PhysicalAxisIndex.HasValue);
            }

            if (mappedDevice is null || mappedBinding?.PhysicalAxisIndex is not int physicalAxisIndex)
                return;

            SelectedDeviceKey = mappedDevice.DurableDeviceKey;
            SelectedPhysicalAxisIndex = physicalAxisIndex;
            DeadzoneCurve = ParseCurve(mappedBinding.Deadzone);
            SaturationCurve = ParseCurve(mappedBinding.Saturation);
            Invert = mappedBinding.Invert;
            StatusText = GetDeviceDisplayName(mappedDevice) + " / " + PhysicalAxisNameService.GetDisplayName(physicalAxisIndex);
        }
    }

    private enum AxisPairCaptureTarget
    {
        None,
        Primary,
        Secondary
    }
}