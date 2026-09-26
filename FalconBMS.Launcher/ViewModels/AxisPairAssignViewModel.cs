using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Services;
using FalconBMS.Launcher.Services.Controls;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    private readonly DirectInputCaptureHost _captureHost = new();
    private readonly IReadOnlyList<DeviceBindingProfile> _deviceProfiles;
    private readonly Action<AxisPairAssignViewModel> _saveAxisAssignment;
    private readonly Action _closeWindow;
    private readonly IntPtr _hwnd;

    private readonly string _actionId =
        DebugDiagnosticsService.CreateActionId("AXISPAIRUI");

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

    public RelayCommand RecenterCommand { get; }

    public RelayCommand ClearCenterCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    // Avoid raising CanExecuteChanged on every 16 ms graph update.
    private bool _canRecenterCached;

    // BMS currently has one axismapping.dat table shared by all aircraft, so editing an
    // axis pair from the F-15 profile also changes it for F-16 (and vice versa). Only the
    // F-15 popup shows the notice, since F-16 is the "home" profile pilots expect to edit freely.
    public bool ShowsSharedAxisNotice { get; }

    public AxisPairAssignViewModel(
        AxisPairDefinition pairDefinition,
        IEnumerable<DeviceBindingProfile> deviceProfiles,
        string? initialDeviceKey,
        IntPtr hwnd,
        string aircraftProfile,
        Action<AxisPairAssignViewModel> saveAxisAssignment,
        Action closeWindow)
    {
        PairDefinition = pairDefinition;
        _deviceProfiles = deviceProfiles.ToList();
        _hwnd = hwnd;
        _saveAxisAssignment = saveAxisAssignment;
        _closeWindow = closeWindow;

        ShowsSharedAxisNotice = string.Equals(aircraftProfile, "F-15ABCD", StringComparison.OrdinalIgnoreCase);

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

        // The calibration offset is stored in the raw physical axis scale.
        // The graph needs to know whether to reverse the displayed direction.
        Primary.IsVerticalAxis = !IsHorizontalAxis(Primary);
        Secondary.IsVerticalAxis = !IsHorizontalAxis(Secondary);

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

        RecenterCommand =
            new RelayCommand(
                RecenterAxes,
                CanRecenter);

        ClearCenterCommand =
            new RelayCommand(
                ClearCenter,
                CanClearCenter);

        SaveCommand =
            new RelayCommand(
                SaveAndClose,
                CanSave);

        CancelCommand =
            new RelayCommand(
                CancelAndClose);

        // Update Reset Center when either axis offset changes
        Primary.PropertyChanged += AxisCenter_PropertyChanged;

        if (HasSecondaryAxis)
            Secondary.PropertyChanged += AxisCenter_PropertyChanged;

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

    private bool CanClearCenter()
    {
        return Primary.CenterOffset != 0 ||
               (HasSecondaryAxis && Secondary.CenterOffset != 0);
    }

    private void AxisCenter_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AxisEditViewModel.CenterOffset))
        {
            ClearCenterCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanRecenter()
    {
        if (_captureTarget != AxisPairCaptureTarget.None)
            return false;

        if (!CanRecenterAxis(Primary))
            return false;

        return !HasSecondaryAxis ||
               CanRecenterAxis(Secondary);
    }

    private static bool CanRecenterAxis(
        AxisEditViewModel axis)
    {
        return !axis.IsCleared &&
               !string.IsNullOrWhiteSpace(axis.SelectedDeviceKey) &&
               axis.SelectedPhysicalAxisIndex.HasValue &&
               axis.LastRawAxisValue.HasValue;
    }

    private void RecenterAxes()
    {
        // Require a live sample from every axis before changing anything.
        // This prevents half of a pair from being recentered.
        if (!CanRecenter())
            return;

        RecenterAxis(Primary);

        if (HasSecondaryAxis)
            RecenterAxis(Secondary);

        DebugDiagnosticsService.Info(
            $"Advanced axis recentered. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId} | " +
            $"PrimaryOffset={Primary.CenterOffset} | " +
            $"SecondaryOffset={(HasSecondaryAxis ? Secondary.CenterOffset.ToString() : "<none>")}");

        RefreshCalibratedPlot();
    }

    private static void RecenterAxis(
        AxisEditViewModel axis)
    {
        if (!axis.LastRawAxisValue.HasValue)
            return;

        int rawValue = Math.Max(
            AxisMin,
            Math.Min(
                AxisMax,
                axis.LastRawAxisValue.Value));

        // Physical minimum -> +10000, midpoint -> 0,
        // physical maximum -> -10000.
        //
        // This matches the isolated BMS cursor calibration samples.
        axis.CenterOffset = (int)Math.Round(
            (AxisMax / 2.0 - rawValue) *
            20000.0 / AxisMax,
            MidpointRounding.AwayFromZero);
    }

    private void ClearCenter()
    {
        Primary.CenterOffset = 0;

        if (HasSecondaryAxis)
            Secondary.CenterOffset = 0;

        DebugDiagnosticsService.Info(
            $"Advanced axis center cleared. | " +
            $"ActionId={_actionId} | " +
            $"DefinitionId={PairDefinition.PairId}");

        RefreshCalibratedPlot();
    }

    private void RefreshCalibratedPlot()
    {
        if (Primary.LastRawAxisValue.HasValue)
        {
            UpdatePlotValue(
                Primary,
                RawAxisToSigned(
                    Primary.LastRawAxisValue.Value,
                    Primary.LogicalAxisName,
                    Primary.IsVerticalAxis));
        }

        if (HasSecondaryAxis &&
            Secondary.LastRawAxisValue.HasValue)
        {
            UpdatePlotValue(
                Secondary,
                RawAxisToSigned(
                    Secondary.LastRawAxisValue.Value,
                    Secondary.LogicalAxisName,
                    Secondary.IsVerticalAxis));
        }
    }

    private void RefreshRecenterAvailability()
    {
        bool canRecenter = CanRecenter();

        if (_canRecenterCached == canRecenter)
            return;

        _canRecenterCached = canRecenter;
        RecenterCommand.RaiseCanExecuteChanged();
    }

    private bool CanSave()
    {
        return !HasPendingPairConflict();
    }

    public void Start()
    {
        Stop();

        _baselineByDeviceKey.Clear();
        _stableHitsByCandidate.Clear();

        IEnumerable<DirectInputCaptureDevice> joystickDevices =
            _deviceProfiles
                .Where(device =>
                    device.IsConnected &&
                    device.AxisCount > 0)
                .Select(device =>
                    new DirectInputCaptureDevice(
                        device.DurableDeviceKey,
                        device.InstanceGuid));

        _captureHost.Start(
            System.Windows.Application.Current.Dispatcher,
            _hwnd,
            captureKeyboard: false,
            joystickDevices: joystickDevices);

        // Keep the existing 16 ms evaluation cadence for live plotting and
        // the settle/threshold/dominance/stability capture algorithm. The
        // timer now reads the buffered listener's axis cache instead of
        // polling DirectInput hardware.
        _timer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _timer.Tick += Timer_Tick;
        _timer.Start();

        UpdateAxisConflicts();
        UpdateLiveGraphFromCurrentAssignments();
        RefreshRecenterAvailability();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }

        _captureHost.Stop();
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

        UpdateAxisConflicts();
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

        axis.CenterOffset = 0;
        axis.LastRawAxisValue = null;

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
            // Store the physical value, not the inverted or curved graph value.
            Primary.LastRawAxisValue = primaryValue;

            UpdatePlotValue(
                Primary,
                RawAxisToSigned(
                    primaryValue,
                    Primary.LogicalAxisName,
                    Primary.IsVerticalAxis));

            updated = true;
        }

        if (HasSecondaryAxis &&
            TryGetAxisValueForEdit(
                Secondary,
                device,
                axisValues,
                out int secondaryValue))
        {
            Secondary.LastRawAxisValue = secondaryValue;

            UpdatePlotValue(
                Secondary,
                RawAxisToSigned(
                    secondaryValue,
                    Secondary.LogicalAxisName,
                    Secondary.IsVerticalAxis));

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

            RefreshRecenterAvailability();
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

        bool assignmentChanged =
            !string.Equals(
                targetAxis.SelectedDeviceKey,
                best.Device.DurableDeviceKey,
                StringComparison.OrdinalIgnoreCase) ||
            targetAxis.SelectedPhysicalAxisIndex != best.AxisIndex;

        targetAxis.SelectedDeviceKey =
            best.Device.DurableDeviceKey;

        targetAxis.SelectedPhysicalAxisIndex =
            best.AxisIndex;

        if (assignmentChanged)
        {
            // Calibration belongs to the physical axis that was calibrated.
            targetAxis.CenterOffset = 0;
            targetAxis.LastRawAxisValue = null;
        }

        targetAxis.IsCleared = false;

        RefreshRecenterAvailability();

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

        return _captureHost.TryGetJoystickAxisValues(
            device.DurableDeviceKey,
            out axisValues);
    }

    private void UpdateAxisConflicts()
    {
        if (HasPendingPairConflict())
        {
            ShowPendingPairConflict();
            SaveCommand.RaiseCanExecuteChanged();
            return;
        }

        UpdateAxisConflict(
            Primary);

        if (HasSecondaryAxis)
        {
            UpdateAxisConflict(
                Secondary);
        }

        SaveCommand.RaiseCanExecuteChanged();
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
            GetAxisInputDisplayText(axis) +
            " is currently being used by: " +
            conflictName +
            "\nClick \"Save\" to replace this anyway.";

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

    private string GetAxisInputDisplayText(
    AxisEditViewModel axis)
    {
        DeviceBindingProfile? device =
            _deviceProfiles.FirstOrDefault(
                d =>
                    string.Equals(
                        d.DurableDeviceKey,
                        axis.SelectedDeviceKey,
                        StringComparison.OrdinalIgnoreCase));

        string deviceName =
            device?.ProductName
            ?? device?.InstanceName
            ?? axis.SelectedDeviceKey
            ?? "device";

        string physicalAxisName =
            axis.SelectedPhysicalAxisIndex.HasValue
                ? PhysicalAxisNameService.GetDisplayName(
                    axis.SelectedPhysicalAxisIndex.Value)
                : "axis input";

        return deviceName + " " + physicalAxisName;
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
        string logicalAxisName,
        bool isVerticalAxis)
    {
        double displayed =
            AxisAssignViewModel.NormalizeAxisValue(
                rawValue,
                logicalAxisName,
                invert: false);

        double signed = (displayed - 0.5) * 2.0;

        // The vertical axis in a two-axis control pair uses the opposite
        // direction from this window's plot convention, where +1 is drawn
        // toward "Up"/"Forward" at the top of the graph. Flip the signed
        // display value for whichever axis has the Vertical role in the pair
        // (Pitch in Pitch & Roll, Cursor_Y in Cursor X & Y) so forward
        // movement moves up and aft/back movement moves down. Horizontal
        // axes (Roll, Cursor_X) are left unchanged. NormalizeAxisValue is
        // also unchanged, so axis behavior elsewhere in the Launcher and
        // saved control output are unaffected.
        if (isVerticalAxis)
        {
            signed = -signed;
        }

        return signed;
    }

    public static double CalculateAxisOutput(
        double rawSignedValue,
        AxisEditViewModel axis)
    {
        double centeredValue = rawSignedValue;

        if (axis.CenterOffset != 0)
        {
            // Reconstruct the physical value that the user selected as center,
            // then convert it using the same orientation as the live graph.
            // This keeps the graph independent of axis inversion.
            double centerRawValue =
                AxisMax / 2.0 -
                axis.CenterOffset * AxisMax / 20000.0;

            double centerSignedValue =
                RawAxisToSigned(
                    (int)Math.Round(centerRawValue),
                    axis.LogicalAxisName,
                    axis.IsVerticalAxis);

            centeredValue = Math.Max(
                -1.0,
                Math.Min(
                    1.0,
                    rawSignedValue - centerSignedValue));
        }

        double value =
            axis.Invert
                ? -centeredValue
                : centeredValue;

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
        Primary.PropertyChanged -= AxisCenter_PropertyChanged;
        Secondary.PropertyChanged -= AxisCenter_PropertyChanged;

        // Stop the graph timer and release its event handler before
        // disposing the DirectInput capture session.
        Stop();
        _captureHost.Dispose();
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

        private int _centerOffset;

        // This is the latest physical DirectInput value for the currently
        // selected axis. It is never written directly to device JSON.
        public int? LastRawAxisValue { get; set; }

        public bool IsVerticalAxis { get; set; }

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

        public int CenterOffset
        {
            get => _centerOffset;

            set => Set(
                ref _centerOffset,
                Math.Max(-10000, Math.Min(10000, value)));
        }
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

            // Load the saved center into the popup's working copy.
            // Cancel leaves the original binding untouched.
            CenterOffset =
                mappedBinding.CenterOffset;

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