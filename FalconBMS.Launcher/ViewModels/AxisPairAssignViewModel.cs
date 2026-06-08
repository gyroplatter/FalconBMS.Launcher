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
/// Drives the advanced axis assignment popup.
/// The UI can edit one physical axis or one physical X/Y control, but saving
/// still writes normal BMS axis bindings.
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
    private readonly string _actionId =
        DebugDiagnosticsService.CreateActionId("AXISPAIRUI");

    private readonly Dictionary<string, JoystickSession>
        _sessionsByDeviceKey = new();

    private readonly Dictionary<string, int[]>
        _baselineByDeviceKey = new();

    private readonly Dictionary<string, int>
        _stableHitsByCandidate = new();

    private DispatcherTimer? _timer;
    private DateTime _captureStartedUtc;

    private AxisPairCaptureTarget _captureTarget =
        AxisPairCaptureTarget.None;

    private sealed class AxisCaptureCandidate
    {
        public required DeviceBindingProfile Device { get; init; }

        public required int AxisIndex { get; init; }

        public required int Delta { get; init; }

        public string CandidateKey =>
            Device.DurableDeviceKey + ":" + AxisIndex;
    }

    public AxisPairDefinition PairDefinition { get; }

    public bool HasSecondaryAxis =>
        PairDefinition.HasSecondaryAxis;

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

    public RelayCommand ClearCommand { get; }

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

        MapPrimaryCommand =
            new RelayCommand(
                () => StartCapture(
                    AxisPairCaptureTarget.Primary));

        MapSecondaryCommand =
            new RelayCommand(
                () =>
                {
                    if (HasSecondaryAxis)
                    {
                        StartCapture(
                            AxisPairCaptureTarget.Secondary);
                    }
                });

        ClearCommand =
            new RelayCommand(
                ClearAxes);

        SaveCommand =
            new RelayCommand(
                SaveAndClose);

        CancelCommand =
            new RelayCommand(
                CancelAndClose);

        DebugDiagnosticsService.Info(
            $"Advanced axis popup created. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"HasSecondaryAxis={HasSecondaryAxis} | " +
            $"InitialClickedDeviceKey={initialDeviceKey ?? "<null>"} | " +
            $"PrimaryDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PrimaryAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)} | " +
            $"SecondaryDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | " +
            $"SecondaryAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)}");
    }

    public void Start()
    {
        Stop();

        _timer = new DispatcherTimer(
            DispatcherPriority.Background)
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

        foreach (JoystickSession session in
                 _sessionsByDeviceKey.Values)
        {
            session.Dispose();
        }

        _sessionsByDeviceKey.Clear();
    }

    private void StartCapture(
        AxisPairCaptureTarget target)
    {
        if (target == AxisPairCaptureTarget.Secondary &&
            !HasSecondaryAxis)
        {
            return;
        }

        _captureTarget = target;
        _captureStartedUtc = DateTime.UtcNow;

        _baselineByDeviceKey.Clear();
        _stableHitsByCandidate.Clear();

        IsMappingPrimary =
            target == AxisPairCaptureTarget.Primary;

        IsMappingSecondary =
            HasSecondaryAxis &&
            target == AxisPairCaptureTarget.Secondary;

        AxisEditViewModel axis =
            GetAxis(target);

        axis.StatusText =
            "Awaiting input: move this axis clearly";

        axis.ConflictText = "";
        axis.HasAxisConflict = false;

        DebugDiagnosticsService.Info(
            $"Advanced axis capture armed. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"Target={target} | " +
            $"LogicalAxis={axis.LogicalAxisName}");
    }

    private void ClearAxes()
    {
        DebugDiagnosticsService.Info(
            $"Advanced axis clear clicked. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"HasSecondaryAxis={HasSecondaryAxis} | " +
            $"PrimaryLogicalAxis={Primary.LogicalAxisName} | " +
            $"PrimaryPreviousDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PrimaryPreviousPhysicalAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)}");

        if (HasSecondaryAxis)
        {
            DebugDiagnosticsService.Info(
                $"Advanced secondary axis clear. | " +
                $"ActionId={_actionId} | " +
                $"DefinitionId={PairDefinition.PairId} | " +
                $"SecondaryLogicalAxis={Secondary.LogicalAxisName} | " +
                $"SecondaryPreviousDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | " +
                $"SecondaryPreviousPhysicalAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)}");
        }

        _captureTarget =
            AxisPairCaptureTarget.None;

        _baselineByDeviceKey.Clear();
        _stableHitsByCandidate.Clear();

        IsMappingPrimary = false;
        IsMappingSecondary = false;

        ClearAxisEdit(
            Primary,
            $"Cleared. Click {Primary.MapButtonText} to assign this axis.");

        if (HasSecondaryAxis)
        {
            ClearAxisEdit(
                Secondary,
                $"Cleared. Click {Secondary.MapButtonText} to assign this axis.");
        }

        UpdateLiveGraphFromCurrentAssignments();
    }

    private static void ClearAxisEdit(
        AxisEditViewModel axis,
        string statusText)
    {
        axis.SelectedDeviceKey = null;
        axis.SelectedPhysicalAxisIndex = null;
        axis.IsCleared = true;

        axis.DeadzoneCurve = AxCurve.None;
        axis.SaturationCurve = AxCurve.None;
        axis.CurveValue = 1;

        axis.StatusText = statusText;
        axis.ConflictText = "";
        axis.HasAxisConflict = false;
    }

    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        var candidates =
            new List<AxisCaptureCandidate>();

        bool sawAnyAssignedAxis = false;

        IEnumerable<DeviceBindingProfile> connectedDevices =
            _deviceProfiles.Where(
                device =>
                    device.IsConnected &&
                    device.AxisCount > 0);

        foreach (DeviceBindingProfile device in connectedDevices)
        {
            if (!TryReadAxisValues(
                    device,
                    out int[] axisValues))
            {
                continue;
            }

            if (PollAssignedAxes(device, axisValues))
                sawAnyAssignedAxis = true;

            if (_captureTarget ==
                AxisPairCaptureTarget.None)
            {
                continue;
            }

            double captureAgeMilliseconds =
                (DateTime.UtcNow - _captureStartedUtc)
                .TotalMilliseconds;

            if (captureAgeMilliseconds < InitialSettleMs)
            {
                _baselineByDeviceKey[
                    device.DurableDeviceKey] =
                    (int[])axisValues.Clone();

                continue;
            }

            if (!_baselineByDeviceKey.ContainsKey(
                    device.DurableDeviceKey))
            {
                _baselineByDeviceKey[
                    device.DurableDeviceKey] =
                    (int[])axisValues.Clone();
            }

            AddMovedAxisCandidates(
                device,
                axisValues,
                candidates);
        }

        if (_captureTarget != AxisPairCaptureTarget.None)
        {
            AcceptDominantCandidate(candidates);
        }
        else if (!sawAnyAssignedAxis)
        {
            UpdateLiveGraphFromCurrentAssignments();
        }
    }

    private bool PollAssignedAxes(
        DeviceBindingProfile device,
        int[] axisValues)
    {
        bool updated = false;

        if (TryGetAxisValueForEdit(
                Primary,
                device,
                axisValues,
                out int primaryValue))
        {
            UpdatePlotValue(
                Primary,
                RawAxisToSigned(
                    primaryValue,
                    Primary.LogicalAxisName));

            updated = true;
        }

        if (HasSecondaryAxis &&
            TryGetAxisValueForEdit(
                Secondary,
                device,
                axisValues,
                out int secondaryValue))
        {
            UpdatePlotValue(
                Secondary,
                RawAxisToSigned(
                    secondaryValue,
                    Secondary.LogicalAxisName));

            updated = true;
        }

        if (updated)
        {
            DeadzoneRadius =
                HasSecondaryAxis
                    ? Math.Max(
                        GetDeadzoneRadius(
                            Primary.DeadzoneCurve),
                        GetDeadzoneRadius(
                            Secondary.DeadzoneCurve))
                    : GetDeadzoneRadius(
                        Primary.DeadzoneCurve);
        }

        return updated;
    }

    private static bool TryGetAxisValueForEdit(
        AxisEditViewModel axis,
        DeviceBindingProfile device,
        int[] axisValues,
        out int value)
    {
        value = 0;

        if (!string.Equals(
                device.DurableDeviceKey,
                axis.SelectedDeviceKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!axis.SelectedPhysicalAxisIndex.HasValue)
            return false;

        int axisIndex =
            axis.SelectedPhysicalAxisIndex.Value;

        if (axisIndex < 0 ||
            axisIndex >= axisValues.Length)
        {
            return false;
        }

        value = axisValues[axisIndex];

        return true;
    }

    private void UpdateLiveGraphFromCurrentAssignments()
    {
        if (!Primary.SelectedPhysicalAxisIndex.HasValue)
        {
            ClearPlotValue(
                Primary);
        }

        if (HasSecondaryAxis)
        {
            if (!Secondary.SelectedPhysicalAxisIndex.HasValue)
            {
                ClearPlotValue(
                    Secondary);
            }

            DeadzoneRadius =
                Math.Max(
                    GetDeadzoneRadius(
                        Primary.DeadzoneCurve),
                    GetDeadzoneRadius(
                        Secondary.DeadzoneCurve));

            return;
        }

        RawY = 0;
        OutputY = 0;

        DeadzoneRadius =
            GetDeadzoneRadius(
                Primary.DeadzoneCurve);
    }

    private void UpdatePlotValue(
        AxisEditViewModel axis,
        double rawValue)
    {
        double outputValue =
            CalculateAxisOutput(
                rawValue,
                axis);

        if (IsHorizontalAxis(axis))
        {
            RawX = rawValue;
            OutputX = outputValue;
            return;
        }

        RawY = rawValue;
        OutputY = outputValue;
    }

    private void ClearPlotValue(
        AxisEditViewModel axis)
    {
        if (IsHorizontalAxis(axis))
        {
            RawX = 0;
            OutputX = 0;
            return;
        }

        RawY = 0;
        OutputY = 0;
    }

    private bool IsHorizontalAxis(
        AxisEditViewModel axis)
    {
        return string.Equals(
            axis.LogicalAxisName,
            PairDefinition.HorizontalAxis.LogicalAxisName,
            StringComparison.OrdinalIgnoreCase);
    }

    private void AddMovedAxisCandidates(
        DeviceBindingProfile device,
        int[] axisValues,
        List<AxisCaptureCandidate> candidates)
    {
        if (!_baselineByDeviceKey.TryGetValue(
                device.DurableDeviceKey,
                out int[] baseline))
        {
            return;
        }

        int axisLimit = Math.Min(
            axisValues.Length,
            Math.Max(
                0,
                device.AxisCount));

        for (int axisIndex = 0;
             axisIndex < axisLimit;
             axisIndex++)
        {
            int delta = Math.Abs(
                axisValues[axisIndex] -
                baseline[axisIndex]);

            if (delta < MovementThreshold)
                continue;

            candidates.Add(
                new AxisCaptureCandidate
                {
                    Device = device,
                    AxisIndex = axisIndex,
                    Delta = delta
                });
        }
    }

    private void AcceptDominantCandidate(
        List<AxisCaptureCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            _stableHitsByCandidate.Clear();
            return;
        }

        AxisCaptureCandidate best =
            candidates
                .OrderByDescending(
                    candidate => candidate.Delta)
                .First();

        AxisCaptureCandidate? secondBest =
            candidates
                .Where(
                    candidate =>
                        candidate.CandidateKey !=
                        best.CandidateKey)
                .OrderByDescending(
                    candidate => candidate.Delta)
                .FirstOrDefault();

        AxisEditViewModel targetAxis =
            GetAxis(_captureTarget);

        if (secondBest is not null &&
            best.Delta <
            secondBest.Delta * DominantAxisRatio)
        {
            _stableHitsByCandidate.Clear();

            targetAxis.StatusText =
                "Move one axis clearly to assign";

            return;
        }

        foreach (string key in
                 _stableHitsByCandidate.Keys.ToList())
        {
            if (!string.Equals(
                    key,
                    best.CandidateKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                _stableHitsByCandidate[key] = 0;
            }
        }

        int stableHits =
            _stableHitsByCandidate.TryGetValue(
                best.CandidateKey,
                out int current)
                ? current + 1
                : 1;

        _stableHitsByCandidate[
            best.CandidateKey] = stableHits;

        if (stableHits < StableHitCountRequired)
            return;

        targetAxis.SelectedDeviceKey =
            best.Device.DurableDeviceKey;

        targetAxis.SelectedPhysicalAxisIndex =
            best.AxisIndex;

        targetAxis.IsCleared = false;

        targetAxis.StatusText =
            GetDeviceDisplayName(best.Device) +
            " / " +
            PhysicalAxisNameService.GetDisplayName(
                best.AxisIndex);

        _captureTarget =
            AxisPairCaptureTarget.None;

        IsMappingPrimary = false;
        IsMappingSecondary = false;

        _stableHitsByCandidate.Clear();
        _baselineByDeviceKey.Clear();

        DebugDiagnosticsService.Info(
            $"Advanced axis captured. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"LogicalAxis={targetAxis.LogicalAxisName} | " +
            $"Device={GetDeviceDisplayName(best.Device)} | " +
            $"DeviceKey={best.Device.DurableDeviceKey} | " +
            $"PhysicalAxis={FormatPhysicalAxis(best.AxisIndex)} | " +
            $"Delta={best.Delta} | " +
            $"StableHits={stableHits}");

        UpdateAxisConflicts();
    }

    private bool TryReadAxisValues(
        DeviceBindingProfile device,
        out int[] axisValues)
    {
        axisValues = Array.Empty<int>();

        if (!device.IsConnected)
            return false;

        if (!_sessionsByDeviceKey.TryGetValue(
                device.DurableDeviceKey,
                out JoystickSession session))
        {
            try
            {
                session = _di.OpenJoystick(
                    device.InstanceGuid,
                    _hwnd);

                _sessionsByDeviceKey[
                    device.DurableDeviceKey] =
                    session;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            axisValues =
                DirectInputManager.ReadAxisVector(
                    session.ReadState());

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateAxisConflicts()
    {
        if (HasPendingPairConflict())
        {
            ShowPendingPairConflict();
            return;
        }

        UpdateAxisConflict(
            Primary);

        if (HasSecondaryAxis)
        {
            UpdateAxisConflict(
                Secondary);
        }
    }

    private bool HasPendingPairConflict()
    {
        if (!HasSecondaryAxis)
            return false;

        if (string.IsNullOrWhiteSpace(
                Primary.SelectedDeviceKey) ||
            string.IsNullOrWhiteSpace(
                Secondary.SelectedDeviceKey))
        {
            return false;
        }

        if (!Primary.SelectedPhysicalAxisIndex.HasValue ||
            !Secondary.SelectedPhysicalAxisIndex.HasValue)
        {
            return false;
        }

        return
            string.Equals(
                Primary.SelectedDeviceKey,
                Secondary.SelectedDeviceKey,
                StringComparison.OrdinalIgnoreCase) &&
            Primary.SelectedPhysicalAxisIndex.Value ==
            Secondary.SelectedPhysicalAxisIndex.Value;
    }

    private void ShowPendingPairConflict()
    {
        string warningText =
            $"{Primary.TitleText} and " +
            $"{Secondary.TitleText} " +
            "cannot use the same physical axis. " +
            "Map one of them to a different axis.";

        Primary.ConflictText = warningText;
        Primary.HasAxisConflict = true;

        Secondary.ConflictText = warningText;
        Secondary.HasAxisConflict = true;

        DebugDiagnosticsService.Warn(
            $"Advanced axis pair conflict found. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"PrimaryLogicalAxis={Primary.LogicalAxisName} | " +
            $"SecondaryLogicalAxis={Secondary.LogicalAxisName} | " +
            $"DeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PhysicalAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)}");
    }

    private void UpdateAxisConflict(
        AxisEditViewModel axis)
    {
        DeviceAxisBinding? conflict =
            FindAxisConflict(axis);

        if (conflict is null)
        {
            axis.ConflictText = "";
            axis.HasAxisConflict = false;
            return;
        }

        string conflictName =
            GetLogicalAxisDisplayName(
                conflict.LogicalAxisName);

        axis.ConflictText =
            "Axis input currently bound to: " +
            conflictName +
            "\nClick \"Save\" to replace the existing assignment.";

        axis.HasAxisConflict = true;

        DebugDiagnosticsService.Warn(
            $"Advanced axis conflict found. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"LogicalAxis={axis.LogicalAxisName} | " +
            $"SelectedDeviceKey={axis.SelectedDeviceKey ?? "<null>"} | " +
            $"SelectedPhysicalAxis={FormatPhysicalAxis(axis.SelectedPhysicalAxisIndex)} | " +
            $"ConflictingLogicalAxis={conflict.LogicalAxisName} | " +
            $"ConflictingDisplayName={conflictName}");
    }

    private DeviceAxisBinding? FindAxisConflict(
        AxisEditViewModel axis)
    {
        if (string.IsNullOrWhiteSpace(
                axis.SelectedDeviceKey) ||
            !axis.SelectedPhysicalAxisIndex.HasValue)
        {
            return null;
        }

        DeviceBindingProfile? selectedDevice =
            _deviceProfiles.FirstOrDefault(
                device =>
                    string.Equals(
                        device.DurableDeviceKey,
                        axis.SelectedDeviceKey,
                        StringComparison.OrdinalIgnoreCase));

        if (selectedDevice is null)
            return null;

        return selectedDevice.AxisBindings
            .FirstOrDefault(
                binding =>
                    binding.PhysicalAxisIndex ==
                    axis.SelectedPhysicalAxisIndex.Value &&
                    !string.Equals(
                        binding.LogicalAxisName,
                        axis.LogicalAxisName,
                        StringComparison.OrdinalIgnoreCase));
    }

    private void SaveAndClose()
    {
        DebugDiagnosticsService.Info(
            $"Advanced axis save clicked. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"HasSecondaryAxis={HasSecondaryAxis} | " +
            $"PrimaryDeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PrimaryAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)} | " +
            $"PrimaryCleared={Primary.IsCleared}");

        if (HasSecondaryAxis)
        {
            DebugDiagnosticsService.Info(
                $"Advanced secondary axis save. | " +
                $"ActionId={_actionId} | " +
                $"DefinitionId={PairDefinition.PairId} | " +
                $"SecondaryDeviceKey={Secondary.SelectedDeviceKey ?? "<null>"} | " +
                $"SecondaryAxis={FormatPhysicalAxis(Secondary.SelectedPhysicalAxisIndex)} | " +
                $"SecondaryCleared={Secondary.IsCleared}");
        }

        if (!ValidateBeforeSave())
            return;

        _saveAxisAssignment(this);
        _closeWindow();
    }

    private bool ValidateBeforeSave()
    {
        UpdateAxisConflicts();

        if (!HasPendingPairConflict())
            return true;

        DebugDiagnosticsService.Warn(
            $"Advanced axis save blocked because both axes use the same physical input. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"PrimaryLogicalAxis={Primary.LogicalAxisName} | " +
            $"SecondaryLogicalAxis={Secondary.LogicalAxisName} | " +
            $"DeviceKey={Primary.SelectedDeviceKey ?? "<null>"} | " +
            $"PhysicalAxis={FormatPhysicalAxis(Primary.SelectedPhysicalAxisIndex)}");

        return false;
    }

    private void ClearPairValidationWarning()
    {
        if (!HasSecondaryAxis)
            return;

        string warningPrefix =
            $"{Primary.TitleText} and " +
            $"{Secondary.TitleText} " +
            "cannot use the same physical axis.";

        if (Primary.HasAxisConflict &&
            Primary.ConflictText.StartsWith(
                warningPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            Primary.ConflictText = "";
            Primary.HasAxisConflict = false;
        }

        if (Secondary.HasAxisConflict &&
            Secondary.ConflictText.StartsWith(
                warningPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            Secondary.ConflictText = "";
            Secondary.HasAxisConflict = false;
        }
    }

    private void CancelAndClose()
    {
        DebugDiagnosticsService.Info(
            $"Advanced axis assign canceled. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId}");

        _closeWindow();
    }

    private AxisEditViewModel GetAxis(
        AxisPairCaptureTarget target)
    {
        return target ==
               AxisPairCaptureTarget.Secondary
            ? Secondary
            : Primary;
    }

    private static double RawAxisToSigned(
        int rawValue,
        string logicalAxisName)
    {
        double displayed =
            AxisAssignViewModel.NormalizeAxisValue(
                rawValue,
                logicalAxisName,
                invert: false);

        return (displayed - 0.5) * 2.0;
    }

    public static double CalculateAxisOutput(
        double rawSignedValue,
        AxisEditViewModel axis)
    {
        double value =
            axis.Invert
                ? -rawSignedValue
                : rawSignedValue;

        double sign = Math.Sign(value);
        double magnitude = Math.Abs(value);

        double deadzone =
            GetDeadzoneRadius(
                axis.DeadzoneCurve);

        if (magnitude <= deadzone)
            return 0.0;

        if (deadzone > 0.0)
        {
            magnitude =
                (magnitude - deadzone) /
                (1.0 - deadzone);
        }

        double saturationLimit =
            GetSaturationLimit(
                axis.SaturationCurve);

        if (saturationLimit < 1.0)
        {
            magnitude = Math.Min(
                1.0,
                magnitude / saturationLimit);
        }

        double curveValue =
            axis.CurveValue;

        if (curveValue > 1.0)
        {
            magnitude =
                (Math.Pow(magnitude, 3.0) *
                 (curveValue - 1.0) +
                 magnitude) /
                curveValue;
        }

        return Math.Max(
            -1.0,
            Math.Min(
                1.0,
                sign * magnitude));
    }

    private static double GetDeadzoneRadius(
        AxCurve curve)
    {
        return curve switch
        {
            AxCurve.Small => 0.01,
            AxCurve.Medium => 0.05,
            AxCurve.Large => 0.10,
            _ => 0.0
        };
    }

    private static double GetSaturationLimit(
        AxCurve curve)
    {
        return curve switch
        {
            AxCurve.Small => 0.99,
            AxCurve.Medium => 0.95,
            AxCurve.Large => 0.90,
            _ => 1.0
        };
    }

    private static AxCurve ParseCurve(
        string? value)
    {
        return Enum.TryParse(
            value,
            ignoreCase: true,
            out AxCurve curve)
                ? curve
                : AxCurve.None;
    }

    private static string GetLogicalAxisDisplayName(
        string logicalAxisName)
    {
        DeviceAxisDefinition? definition =
            AxisDefinitionService.Find(
                logicalAxisName);

        return definition?.DisplayName ??
               logicalAxisName;
    }

    private static string FormatPhysicalAxis(
        int? physicalAxisIndex)
    {
        return physicalAxisIndex.HasValue
            ? PhysicalAxisNameService.GetDisplayName(
                  physicalAxisIndex.Value) +
              $"({physicalAxisIndex.Value})"
            : "<null>";
    }

    private static string GetDeviceDisplayName(
        DeviceBindingProfile device)
    {
        if (!string.IsNullOrWhiteSpace(
                device.ProductName))
        {
            return device.ProductName;
        }

        if (!string.IsNullOrWhiteSpace(
                device.InstanceName))
        {
            return device.InstanceName;
        }

        return device.DurableDeviceKey;
    }

    public void Dispose()
    {
        Stop();
        _di.Dispose();
    }

    public sealed class AxisEditViewModel :
        ViewModelBase
    {
        private string? _selectedDeviceKey;
        private int? _selectedPhysicalAxisIndex;

        private string _statusText =
            "Not assigned";

        private string _conflictText = "";

        private bool _hasAxisConflict;

        private AxCurve _deadzoneCurve =
            AxCurve.None;

        private AxCurve _saturationCurve =
            AxCurve.None;

        private int _curveValue = 1;
        private bool _invert;
        private bool _isCleared;

        public AxisEditViewModel(
            string logicalAxisName,
            string titleText,
            string mapButtonText,
            string? initialDeviceKey,
            IReadOnlyList<DeviceBindingProfile>
                deviceProfiles)
        {
            LogicalAxisName = logicalAxisName;
            TitleText = titleText;
            MapButtonText = mapButtonText;

            LoadExistingMapping(
                initialDeviceKey,
                deviceProfiles);
        }

        private static int CurveToStep(
            AxCurve curve)
        {
            return curve switch
            {
                AxCurve.Small => 1,
                AxCurve.Medium => 2,
                AxCurve.Large => 3,
                _ => 0
            };
        }

        private static AxCurve StepToCurve(
            int step)
        {
            return step switch
            {
                1 => AxCurve.Small,
                2 => AxCurve.Medium,
                3 => AxCurve.Large,
                _ => AxCurve.None
            };
        }

        private static string CurveToPercentageText(
            AxCurve curve)
        {
            return curve switch
            {
                AxCurve.Small => "1%",
                AxCurve.Medium => "5%",
                AxCurve.Large => "10%",
                _ => "0%"
            };
        }

        public string LogicalAxisName { get; }

        public string TitleText { get; }

        public string MapButtonText { get; }

        public string? SelectedDeviceKey
        {
            get => _selectedDeviceKey;
            set => Set(
                ref _selectedDeviceKey,
                value);
        }

        public int? SelectedPhysicalAxisIndex
        {
            get => _selectedPhysicalAxisIndex;
            set => Set(
                ref _selectedPhysicalAxisIndex,
                value);
        }

        public string StatusText
        {
            get => _statusText;
            set => Set(
                ref _statusText,
                value);
        }

        public string ConflictText
        {
            get => _conflictText;
            set => Set(
                ref _conflictText,
                value);
        }

        public bool HasAxisConflict
        {
            get => _hasAxisConflict;
            set => Set(
                ref _hasAxisConflict,
                value);
        }

        public AxCurve DeadzoneCurve
        {
            get => _deadzoneCurve;

            set
            {
                if (!Set(
                        ref _deadzoneCurve,
                        value))
                {
                    return;
                }

                OnPropertyChanged(
                    nameof(DeadzoneStep));

                OnPropertyChanged(
                    nameof(DeadzonePercentageText));
            }
        }

        public int DeadzoneStep
        {
            get =>
                CurveToStep(
                    DeadzoneCurve);

            set =>
                DeadzoneCurve =
                    StepToCurve(value);
        }

        public string DeadzonePercentageText =>
            CurveToPercentageText(
                DeadzoneCurve);

        public AxCurve SaturationCurve
        {
            get => _saturationCurve;

            set
            {
                if (!Set(
                        ref _saturationCurve,
                        value))
                {
                    return;
                }

                OnPropertyChanged(
                    nameof(SaturationStep));

                OnPropertyChanged(
                    nameof(SaturationPercentageText));
            }
        }

        public int SaturationStep
        {
            get =>
                CurveToStep(
                    SaturationCurve);

            set =>
                SaturationCurve =
                    StepToCurve(value);
        }

        public string SaturationPercentageText =>
            CurveToPercentageText(
                SaturationCurve);

        public int CurveValue
        {
            get => _curveValue;

            set
            {
                int clampedValue =
                    Math.Max(
                        1,
                        Math.Min(
                            5,
                            value));

                if (!Set(
                        ref _curveValue,
                        clampedValue))
                {
                    return;
                }

                OnPropertyChanged(
                    nameof(CurveStep));

                OnPropertyChanged(
                    nameof(CurvePercentageText));
            }
        }

        public int CurveStep
        {
            get => CurveValue - 1;

            set =>
                CurveValue =
                    Math.Max(
                        0,
                        Math.Min(
                            4,
                            value)) +
                    1;
        }

        public string CurvePercentageText =>
            CurveValue switch
            {
                2 => "25%",
                3 => "50%",
                4 => "75%",
                5 => "100%",
                _ => "0%"
            };

        public bool Invert
        {
            get => _invert;

            set => Set(
                ref _invert,
                value);
        }

        public bool IsCleared
        {
            get => _isCleared;

            set => Set(
                ref _isCleared,
                value);
        }

        private void LoadExistingMapping(
            string? initialDeviceKey,
            IReadOnlyList<DeviceBindingProfile>
                deviceProfiles)
        {
            DeviceBindingProfile? mappedDevice =
                null;

            DeviceAxisBinding? mappedBinding =
                null;

            if (!string.IsNullOrWhiteSpace(
                    initialDeviceKey))
            {
                mappedDevice =
                    deviceProfiles.FirstOrDefault(
                        device =>
                            string.Equals(
                                device.DurableDeviceKey,
                                initialDeviceKey,
                                StringComparison.OrdinalIgnoreCase));

                mappedBinding =
                    mappedDevice?.AxisBindings
                        .FirstOrDefault(
                            binding =>
                                string.Equals(
                                    binding.LogicalAxisName,
                                    LogicalAxisName,
                                    StringComparison.OrdinalIgnoreCase) &&
                                binding.PhysicalAxisIndex.HasValue);
            }

            if (mappedBinding is null)
            {
                mappedDevice =
                    deviceProfiles.FirstOrDefault(
                        device =>
                            device.AxisBindings.Any(
                                binding =>
                                    string.Equals(
                                        binding.LogicalAxisName,
                                        LogicalAxisName,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    binding.PhysicalAxisIndex.HasValue));

                mappedBinding =
                    mappedDevice?.AxisBindings
                        .FirstOrDefault(
                            binding =>
                                string.Equals(
                                    binding.LogicalAxisName,
                                    LogicalAxisName,
                                    StringComparison.OrdinalIgnoreCase) &&
                                binding.PhysicalAxisIndex.HasValue);
            }

            if (mappedDevice is null ||
                mappedBinding?.PhysicalAxisIndex
                    is not int physicalAxisIndex)
            {
                return;
            }

            SelectedDeviceKey =
                mappedDevice.DurableDeviceKey;

            SelectedPhysicalAxisIndex =
                physicalAxisIndex;

            DeadzoneCurve =
                ParseCurve(
                    mappedBinding.Deadzone);

            SaturationCurve =
                ParseCurve(
                    mappedBinding.Saturation);

            CurveValue =
                mappedBinding.Curve;

            Invert =
                mappedBinding.Invert;

            StatusText =
                GetDeviceDisplayName(
                    mappedDevice) +
                " / " +
                PhysicalAxisNameService.GetDisplayName(
                    physicalAxisIndex);
        }
    }

    private enum AxisPairCaptureTarget
    {
        None,
        Primary,
        Secondary
    }
}