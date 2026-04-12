using FalconBMS.Launcher.Input;
using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Drives the axis assignment popup, including live detection, min/max tracking, and result building.
/// </summary>

public sealed class AxisAssignViewModel : ViewModelBase, IDisposable
{
    private readonly DirectInputManager _di = new();
    private readonly AxisFunction _function;
    private readonly IntPtr _hwnd;

    private static bool AxisSupportsDeadzone(AxisFunction f) => f switch
    {
        AxisFunction.Pitch => true,
        AxisFunction.Roll => true,
        AxisFunction.Yaw => true,
        AxisFunction.Trim_Pitch => true,
        AxisFunction.Trim_Yaw => true,
        AxisFunction.Trim_Roll => true,
        AxisFunction.Radar_Antenna_Elevation => true,
        AxisFunction.Cursor_X => true,
        AxisFunction.Cursor_Y => true,
        AxisFunction.Range_Knob => true,
        _ => false
    };

    private readonly SynchronizationContext? _uiContext;

    private CancellationTokenSource? _cts;

    // Tuning knobs
    // If true, DetectAsync has been started and is polling.
    private bool _detectionStarted;

    // consecutive frames above threshold (~8*16ms = 128ms)
    private const int StableHitCountRequired = 8;

    // ignore jitter right after window opens
    private const int InitialSettleMs = 600;

    // Neutral-band only (original-launcher style):
    // Axis must move away from captured neutral baseline by a fraction of the axis range.
    // We estimate per-axis range via observed min/max while polling.
    private const double NeutralBandFraction = 0.25; // “AXISMAX/4”-style behavior, but adaptive

    // Prevents “band collapse” when an axis hasn't been moved yet (observed min/max is tiny).
    // Still neutral-band only: we’re flooring the assumed RANGE, not adding an absolute delta threshold.
    private const int RangeFloorForBand = 40000;

    private sealed class AxisStats
    {
        public int[] Baseline = Array.Empty<int>();
        public int[] Min = Array.Empty<int>();
        public int[] Max = Array.Empty<int>();

        public AxisStats(int[] baseline)
        {
            Baseline = (int[])baseline.Clone();
            Min = (int[])baseline.Clone();
            Max = (int[])baseline.Clone();
        }

        public void Rebaseline(int[] now)
        {
            Baseline = (int[])now.Clone();
            Min = (int[])now.Clone();
            Max = (int[])now.Clone();
        }

        public void Observe(int[] now)
        {
            int n = Math.Min(now.Length, Min.Length);
            for (int i = 0; i < n; i++)
            {
                int v = now[i];
                if (v < Min[i]) Min[i] = v;
                if (v > Max[i]) Max[i] = v;
            }
        }

        public int Range(int axisIndex)
        {
            if ((uint)axisIndex >= (uint)Min.Length) return FallbackRange;
            int r = Max[axisIndex] - Min[axisIndex];
            return r > 0 ? r : FallbackRange;
        }
    }

    // If an axis range hasn't been observed yet (still basically flat), fall back to this
    // to avoid divide-by-zero and to keep behavior sane.
    private const int FallbackRange = 65535;

    public string TitleText => $"Assign {AxisCatalog.Get(_function).DisplayName} Axis";

    private string _detectedText = "Awaiting inputs: Move your control";
    public string DetectedText
    {
        get => _detectedText;
        set => Set(ref _detectedText, value);
    }

    private bool _invert;
    public bool Invert
    {
        get => _invert;
        set
        {
            if (Set(ref _invert, value))
            {
                // Next UpdateAxisBar tick will reflect the new orientation immediately,
                // but this ensures any dependent visuals refresh right away.
                UpdateThrottleDetentFeedback();
                EnableSaveIfEditing();
            }
        }
    }

    public AxCurve[] CurveOptions { get; } = new[] { AxCurve.None, AxCurve.Small, AxCurve.Medium, AxCurve.Large };

    private AxCurve _deadzoneCurve = AxCurve.None;
    public AxCurve DeadzoneCurve
    {
        get => _deadzoneCurve;
        set
        {
            if (Set(ref _deadzoneCurve, value))
            {
                EnableSaveIfEditing();
            }
        }
    }

    private AxCurve _saturationCurve = AxCurve.None;
    public AxCurve SaturationCurve
    {
        get => _saturationCurve;
        set
        {
            if (Set(ref _saturationCurve, value))
            {
                EnableSaveIfEditing();
            }
        }
    }

    private Visibility _deadzoneControlsVisibility = Visibility.Visible;
    public Visibility DeadzoneControlsVisibility
    {
        get => _deadzoneControlsVisibility;
        set => Set(ref _deadzoneControlsVisibility, value);
    }

    private bool _canSave;
    public bool CanSave
    {
        get => _canSave;
        set => Set(ref _canSave, value);
    }

    private bool _clearRequested;
    public bool ClearRequested
    {
        get => _clearRequested;
        private set => Set(ref _clearRequested, value);
    }

    public RelayCommand ClearCommand { get; }

    public RelayCommand SetAbDetentCommand { get; }
    public RelayCommand SetIdleDetentCommand { get; }

    public Visibility DetentControlsVisibility =>
        (_function == AxisFunction.Throttle)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private DirectInputManager.DeviceInfo? _lockedDevice;
    private int _lockedAxisIndex = -1;

    // Axis bar (live position display)
    private double _axisBarValue = 0.0;
    public double AxisBarValue
    {
        get => _axisBarValue;
        set => Set(ref _axisBarValue, value);
    }

    private bool _axisBarEnabled;
    public bool AxisBarEnabled
    {
        get => _axisBarEnabled;
        set => Set(ref _axisBarEnabled, value);
    }

    // Axis bar visual orientation:
    // - When false: bar shows norm as-is.
    // - When true: bar shows 1 - norm.
    // We’ll combine this with the Invert checkbox so the bar “feels” correct.
    private bool _flipAxisBar = true;
    public bool FlipAxisBar
    {
        get => _flipAxisBar;
        set => Set(ref _flipAxisBar, value);
    }

    private string _axisBarLeftLabel = string.Empty;
    public string AxisBarLeftLabel
    {
        get => _axisBarLeftLabel;
        set => Set(ref _axisBarLeftLabel, value);
    }

    private string _axisBarRightLabel = string.Empty;
    public string AxisBarRightLabel
    {
        get => _axisBarRightLabel;
        set => Set(ref _axisBarRightLabel, value);
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

    public bool ShowDetentMarkers => _function == AxisFunction.Throttle;

    public double IdleDetentFraction =>
        (double)IdleDetent / (double)DetentPosition.AxisMax;

    public double AbDetentFraction =>
        (double)AbDetent / (double)DetentPosition.AxisMax;

    // Throttle detents (Falcon-native axis units: 0..65535)
    private int _abDetent = DetentPosition.AxisMax;
    public int AbDetent
    {
        get => _abDetent;
        private set
        {
            if (Set(ref _abDetent, DetentPosition.Clamp(value)))
            {
                OnPropertyChanged(nameof(AbDetentFraction));
            }
        }
    }

    private int _idleDetent = DetentPosition.AxisMin;
    public int IdleDetent
    {
        get => _idleDetent;
        private set
        {
            if (Set(ref _idleDetent, DetentPosition.Clamp(value)))
            {
                OnPropertyChanged(nameof(IdleDetentFraction));
            }
        }
    }

    // Updated every poll when a display axis is active.
    // Raw is the device reading clamped into 0..65535.
    // Logical is the Falcon-space value you currently use elsewhere.
    private int _lastRawAxisValue;
    private int _lastLogicalAxisValue;

    // One-shot request: next poll loop will capture "neutral" baselines from current positions.
    private volatile bool _rebaselineRequested;

    // If we opened the dialog with an existing binding displayed ("Current: ..."),
    // do NOT allow detection to override it unless user clicks Clear.
    private bool _hasExistingDisplayed;

    // When true, detection is "frozen" (won't select a new axis) until Clear is clicked.
    // Used for:
    // - opening on an existing binding ("Current:")
    // - after a new detection has been made (to prevent accidental remap before Save)
    private bool _freezeUntilClear;

    // Axis bar display source (separate from detection candidate)
    private bool _hasDisplayAxis;
    private Guid _displayDeviceGuid = Guid.Empty;
    private int _displayAxisIndex = -1;

    // If we opened with an existing mapping, prefer ProductGuid for matching (stable),
    // and only fall back to name matching if needed.
    private readonly string? _existingDeviceName;
    private readonly Guid? _existingProductGuid;
    private readonly int _existingAxisIndex = -1;

    // When opening on an existing binding, DetectAsync resolves which current device instance matches.
    // Store that resolved info so BuildResult() can return a valid result even without a new detection.
    private Guid _resolvedExistingInstanceGuid = Guid.Empty;
    private Guid _resolvedExistingProductGuid = Guid.Empty;
    private string? _resolvedExistingDeviceName;

    // “candidate” detection state (for stability requirement)
    private Guid _candGuid = Guid.Empty;
    private int _candAxis = -1;
    private int _candHits = 0;

    public AxisAssignViewModel(AxisFunction function, IntPtr hwnd, AxisExistingBinding? existing)
    {
        _function = function;
        _hwnd = hwnd;
        ClearCommand = new RelayCommand(ClearDetection);
        SetAbDetentCommand = new RelayCommand(SetAbDetent, () => AxisBarEnabled);
        SetIdleDetentCommand = new RelayCommand(SetIdleDetent, () => AxisBarEnabled);

        DeadzoneControlsVisibility = AxisSupportsDeadzone(_function) ? Visibility.Visible : Visibility.Collapsed;

        if (!AxisSupportsDeadzone(_function))
        {
            DeadzoneCurve = AxCurve.None;
        }

        _uiContext = SynchronizationContext.Current;

        AxisBarEnabled = false;
        AxisBarValue = 0.0;

        var def = AxisCatalog.Get(_function);
        AxisBarLeftLabel = def.LeftLabel ?? string.Empty;
        AxisBarRightLabel = def.RightLabel ?? string.Empty;

        // Display existing mapping (if any), but do NOT allow detection to override it unless user clicks Clear.
        if (existing is not null)
        {
            _hasExistingDisplayed = true;
            _freezeUntilClear = true; // detection frozen until Clear
            Invert = existing.Invert;
            DetectedText = $"Current: {existing.DeviceName}  Axis {AxisIndexToName(existing.PhysicalAxisIndex)}";

            // Save existing mapping info so DetectAsync can resolve the actual device GUID later.
            _existingDeviceName = existing.DeviceName;
            _existingProductGuid = existing.ProductGuid;
            _existingAxisIndex = existing.PhysicalAxisIndex;
            DeadzoneCurve = existing.Deadzone;
            SaturationCurve = existing.Saturation;

            // Show the bar immediately (centered), even before we resolve the device GUID.
            AxisBarEnabled = true;
            AxisBarValue = 0.5;

            // We'll resolve the actual display device GUID in DetectAsync after enumeration.
            SetDisplayAxis(Guid.Empty, -1);

            // If this is a throttle axis, preload detents from disk (if provided).
            if ((_function == AxisFunction.Throttle || _function == AxisFunction.Throttle_Right) && existing?.Detents is not null)
            {
                AbDetent = existing.Detents.AB;
                IdleDetent = existing.Detents.IDLE;
            }
        }
        else
        {
            _hasExistingDisplayed = false;
            _freezeUntilClear = false;
            DetectedText = "Awaiting inputs";

            _existingDeviceName = null;
            _existingProductGuid = null;
            _existingAxisIndex = -1;

            AxisBarEnabled = false;
            AxisBarValue = 0.0;

            SetDisplayAxis(Guid.Empty, -1);
        }

        ClearRequested = false;
        CanSave = false;

        // Important: do NOT set _lockedDevice/_lockedAxisIndex from existing.
        // Only a real detection should lock a selection and enable Save.
    }

    public void StartDetect(bool preserveExisting = false)
    {
        // Always cancel any existing loop and start a fresh one
        try { _cts?.Cancel(); } catch { }
        _cts = new CancellationTokenSource();

        _detectionStarted = true;

        // Always clear locked selection at the start. Only NEW detection should lock + enable Save.
        _lockedDevice = null;
        _lockedAxisIndex = -1;

        // Clearing should remove the active bar display (green). Keep the control visible but inactive.
        SetDisplayAxis(Guid.Empty, -1);
        UI(() => AxisBarValue = 0.0);

        // If we're opening with an existing mapping, keep the axis bar visible immediately.
        // We'll resolve the device GUID inside DetectAsync (by name) and start live updates there.
        if (!(preserveExisting && _hasExistingDisplayed))
        {
            SetDisplayAxis(Guid.Empty, -1);
        }

        // Reset candidate detection state
        _candGuid = Guid.Empty;
        _candAxis = -1;
        _candHits = 0;

        ClearRequested = false;

        // If we're opening on an existing binding ("Current: ..."), we still start polling so the bar can move,
        // but we do NOT change CanSave/DetectedText and we keep detection frozen until Clear.
        if (!(preserveExisting && _hasExistingDisplayed))
        {
            CanSave = false;
            DetectedText = "Awaiting inputs";
        }

        _ = DetectAsync(_cts.Token);
    }

    private void ClearDetection()
    {
        // User intent: clear/unassign. After clearing, allow:
        // - Save (to keep it cleared), OR
        // - moving an axis (to remap)
        ClearRequested = true;

        // Once user clears, allow detection to pick a new axis.
        _hasExistingDisplayed = false;
        _freezeUntilClear = false; // re-arm detection

        // If detection has not started yet (because we opened on "Current: ..."),
        // start it now so the user can move an axis to remap.
        if (!_detectionStarted)
        {
            // Start detection fresh (this will set DetectedText = "Awaiting inputs")
            StartDetect(preserveExisting: false);
        }

        // Capture a fresh "neutral" baseline from CURRENT positions to avoid instant detection.
        _rebaselineRequested = true;

        _lockedDevice = null;
        _lockedAxisIndex = -1;

        _candGuid = Guid.Empty;
        _candAxis = -1;
        _candHits = 0;

        // Save is allowed to keep it cleared, but detection is also live.
        CanSave = true;
        DetectedText = "Cleared. Move an axis to remap, or click Save to keep it cleared.";

        // Reset detents to defaults when cleared (detents only meaningful when throttle assigned).
        AbDetent = DetentPosition.AxisMax;
        IdleDetent = DetentPosition.AxisMin;
        AxisBarFillBrush = SystemColors.HighlightBrush;
    }

    private void SetAbDetent()
    {
        if (_function != AxisFunction.Throttle)
            return;

        // 1:1 with original launcher:
        // if inverted: AB = AXISMIN + raw
        // else:        AB = AXISMAX - raw
        int ab = Invert
            ? DetentPosition.AxisMin + _lastRawAxisValue
            : DetentPosition.AxisMax - _lastRawAxisValue;

        if (ab > DetentPosition.AxisMax) ab = DetentPosition.AxisMax;
        if (ab < DetentPosition.AxisMin) ab = DetentPosition.AxisMin;

        AbDetent = ab;
        EnableSaveIfEditing();
        UpdateThrottleDetentFeedback();
    }

    private void SetIdleDetent()
    {
        if (_function != AxisFunction.Throttle)
            return;

        // 1:1 with original launcher:
        // if inverted: IDLE = AXISMIN + raw
        // else:        IDLE = AXISMAX - raw
        int idle = Invert
            ? DetentPosition.AxisMin + _lastRawAxisValue
            : DetentPosition.AxisMax - _lastRawAxisValue;

        if (idle > DetentPosition.AxisMax) idle = DetentPosition.AxisMax;
        if (idle < DetentPosition.AxisMin) idle = DetentPosition.AxisMin;

        IdleDetent = idle;
        EnableSaveIfEditing();
        UpdateThrottleDetentFeedback();
    }

    private void UpdateThrottleDetentFeedback()
    {
        if (_function != AxisFunction.Throttle)
        {
            AxisBarFillBrush = SystemColors.HighlightBrush;
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;
            return;
        }

        // Match original launcher check logic by comparing in "detent space".
        // This uses the same transform as the original code:
        // Invert unchecked => AxisMax - raw
        // Invert checked   => AxisMin + raw
        int current = Invert
            ? DetentPosition.AxisMin + _lastRawAxisValue
            : DetentPosition.AxisMax - _lastRawAxisValue;

        if (current < IdleDetent)
        {
            AxisBarFillBrush = Brushes.IndianRed;
            AxisBarOverlayText = "IDLE CUTOFF";
            AxisBarOverlayVisibility = Visibility.Visible;
        }
        else if (current > AbDetent)
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
            AxisBarOverlayText = string.Empty;
            AxisBarOverlayVisibility = Visibility.Collapsed;
        }
    }

    public DetentPosition GetDetents() => new(AbDetent, IdleDetent);
    private void EnableSaveIfEditing()
    {
        // Same idea as AB/IDLE detents: changing a setting should enable Save,
        // but only when the dialog represents something we can actually write:
        // - Existing mapping shown ("Current: ..."), OR
        // - A new detection has been locked.
        if (_hasExistingDisplayed || (_lockedDevice is not null && _lockedAxisIndex >= 0))
        {
            CanSave = true;
        }
    }

    public AxisSelectionResult? BuildResult()
    {
        // Normal case: user moved an axis and we locked a new detection.
        if (_lockedDevice is not null && _lockedAxisIndex >= 0)
        {
            return new AxisSelectionResult
            {
                DeviceName = _lockedDevice.Name,
                DeviceInstanceGuid = _lockedDevice.InstanceGuid,
                DeviceProductGuid = _lockedDevice.ProductGuid,
                PhysicalAxisIndex = _lockedAxisIndex,
                Invert = Invert,
                Deadzone = AxisSupportsDeadzone(_function) ? DeadzoneCurve : AxCurve.None,
                Saturation = SaturationCurve
            };
        }

        // Detents-only edit case: window opened with an existing mapping.
        // Once DetectAsync resolves the display device GUID, allow Save even without a "new detection".
        if (_hasExistingDisplayed &&
            _existingAxisIndex >= 0 &&
            _resolvedExistingInstanceGuid != Guid.Empty &&
            _resolvedExistingProductGuid != Guid.Empty &&
            !string.IsNullOrWhiteSpace(_resolvedExistingDeviceName))
        {
            return new AxisSelectionResult
            {
                DeviceName = _resolvedExistingDeviceName!,
                DeviceInstanceGuid = _resolvedExistingInstanceGuid,
                DeviceProductGuid = _resolvedExistingProductGuid,
                PhysicalAxisIndex = _existingAxisIndex,
                Invert = Invert,
                Deadzone = AxisSupportsDeadzone(_function) ? DeadzoneCurve : AxCurve.None,
                Saturation = SaturationCurve
            };
        }

        return null;
    }

    private async Task DetectAsync(CancellationToken ct)
    {
        Dictionary<Guid, JoystickSession> sessions = new();
        Dictionary<Guid, AxisStats> stats = new();

        try
        {
            var devices = _di.EnumerateDevices();
            if (devices.Count == 0)
            {
                UI(() => DetectedText = "No DirectInput devices detected.");
                return;
            }

            // Open sessions for polling (needs HWND)
            // IMPORTANT: do not let one bad device kill the whole window.
            foreach (var d in devices)
            {
                try
                {
                    var sess = _di.Open(d.InstanceGuid, _hwnd);
                    sessions[d.InstanceGuid] = sess;

                    // Seed stats immediately so the bar can update right away (no settle delay required for the bar)
                    var s = sess.ReadState();
                    var vec = DirectInputManager.ReadAxisVector(s);
                    stats[d.InstanceGuid] = new AxisStats(vec);
                }
                catch
                {
                    // ignore device open failures
                }
            }

            if (sessions.Count == 0)
            {
                UI(() => DetectedText = "No DirectInput devices could be opened.");
                return;
            }

            // If we opened with an existing mapping, resolve display device GUID.
            // Prefer ProductGuid (stable). Fall back to name only if needed.
            if (_hasExistingDisplayed && !_hasDisplayAxis && _existingAxisIndex >= 0)
            {
                DirectInputManager.DeviceInfo? match = null;

                if (_existingProductGuid.HasValue)
                {
                    match = devices.FirstOrDefault(d => d.ProductGuid == _existingProductGuid.Value);
                }

                if (match is null && !string.IsNullOrWhiteSpace(_existingDeviceName))
                {
                    match = devices.FirstOrDefault(d =>
                        string.Equals(d.Name, _existingDeviceName, StringComparison.OrdinalIgnoreCase));
                }

                if (match is not null)
                {
                    // Remember which actual device instance we matched so we can build a result for detent-only saves.
                    _resolvedExistingInstanceGuid = match.InstanceGuid;
                    _resolvedExistingProductGuid = match.ProductGuid;
                    _resolvedExistingDeviceName = match.Name;

                    SetDisplayAxis(match.InstanceGuid, _existingAxisIndex);
                }
            }

            // We want bar updates immediately, but we do NOT want detection to consider movement
            // until the settle window has passed and we capture a clean baseline.
            int startTick = Environment.TickCount;
            bool baselineCaptured = false;

            _rebaselineRequested = false;

            while (!ct.IsCancellationRequested)
            {
                // One-shot rebaseline requested (user clicked Clear)
                if (_rebaselineRequested)
                {
                    foreach (var kvp in sessions)
                    {
                        try
                        {
                            var now0 = DirectInputManager.ReadAxisVector(kvp.Value.ReadState());
                            stats[kvp.Key].Rebaseline(now0);
                        }
                        catch
                        {
                            // ignore per-device read failures
                        }
                    }

                    _rebaselineRequested = false;

                    // Reset candidate detection so we require fresh movement after rebaseline.
                    _candGuid = Guid.Empty;
                    _candAxis = -1;
                    _candHits = 0;

                    baselineCaptured = true; // baseline is now valid
                    await Task.Delay(16, ct);
                    continue;
                }

                // Capture baseline once after settle time.
                // During settle, we still update the bar, but we do not allow detection.
                if (!baselineCaptured)
                {
                    int elapsed = unchecked(Environment.TickCount - startTick);
                    if (elapsed >= InitialSettleMs)
                    {
                        foreach (var kvp in sessions)
                        {
                            try
                            {
                                var now0 = DirectInputManager.ReadAxisVector(kvp.Value.ReadState());
                                stats[kvp.Key].Rebaseline(now0);
                            }
                            catch
                            {
                                // ignore per-device read failures
                            }
                        }

                        baselineCaptured = true;

                        // Reset candidate detection so we require fresh movement after baseline capture.
                        _candGuid = Guid.Empty;
                        _candAxis = -1;
                        _candHits = 0;
                    }
                }

                bool allowDetection = baselineCaptured && !_freezeUntilClear;

                // Poll all opened sessions
                foreach (var kvp in sessions)
                {
                    Guid deviceGuid = kvp.Key;
                    JoystickSession session = kvp.Value;

                    int[] now;

                    try
                    {
                        now = DirectInputManager.ReadAxisVector(session.ReadState());
                    }
                    catch
                    {
                        // ignore per-device read failures
                        continue;
                    }

                    var st = stats[deviceGuid];

                    // Always update bar if this is the display axis (even while frozen and even during settle)
                    if (_hasDisplayAxis && deviceGuid == _displayDeviceGuid && _displayAxisIndex >= 0)
                    {
                        UpdateAxisBar(st, now, _displayAxisIndex);
                    }

                    if (!allowDetection)
                        continue;

                    int bestAxis = -1;
                    double bestScore = 0.0;

                    int n = Math.Min(now.Length, st.Baseline.Length);

                    for (int i = 0; i < n; i++)
                    {
                        int delta = Math.Abs(now[i] - st.Baseline[i]);

                        // ORIGINAL LAUNCHER STYLE:
                        // neutral band is based on AXISMAX (65536), not observed min/max.
                        // threshold = AXISMAX/4
                        int threshold = (int)(FallbackRange * NeutralBandFraction); // 65535 * 0.25 ~= 16383

                        if (delta < threshold)
                            continue;

                        // Score by normalized delta (also against AXISMAX, not observed range)
                        double score = (double)delta / FallbackRange;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestAxis = i;
                        }
                    }

                    if (bestAxis >= 0)
                    {
                        // Require the same device+axis to be above threshold for several frames
                        if (_candGuid == deviceGuid && _candAxis == bestAxis)
                        {
                            _candHits++;
                        }
                        else
                        {
                            _candGuid = deviceGuid;
                            _candAxis = bestAxis;
                            _candHits = 1;
                        }

                        if (_candHits >= StableHitCountRequired)
                        {
                            // Need DeviceInfo for BuildResult / UI text; recover it by GUID from devices list.
                            var dInfo = devices.FirstOrDefault(d => d.InstanceGuid == deviceGuid);
                            if (dInfo is null)
                                continue;

                            _lockedDevice = dInfo;
                            _lockedAxisIndex = bestAxis;

                            SetDisplayAxis(deviceGuid, bestAxis);

                            _hasExistingDisplayed = false;

                            UI(() =>
                            {
                                DetectedText = $"Detected: {dInfo.Name}  Axis {AxisIndexToName(bestAxis)}";
                                ClearRequested = false;
                                CanSave = true;
                            });

                            _freezeUntilClear = true;

                            _candGuid = Guid.Empty;
                            _candAxis = -1;
                            _candHits = 0;

                            break;
                        }
                    }
                    else
                    {
                        if (_candGuid == deviceGuid)
                        {
                            if (_candHits > 0) _candHits--;
                            if (_candHits == 0)
                            {
                                _candAxis = -1;
                                _candGuid = Guid.Empty;
                            }
                        }
                    }
                }

                await Task.Delay(16, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                DetectedText = $"Axis detection failed: {ex.GetType().Name}: {ex.Message}";
                CanSave = false;
            });
        }
        finally
        {
            foreach (var s in sessions.Values)
            {
                try { s.Dispose(); } catch { }
            }
        }
    }
    private void UI(Action a)
    {
        if (_uiContext is null)
        {
            a();
            return;
        }

        _uiContext.Post(_ => a(), null);
    }
    private void SetDisplayAxis(Guid deviceGuid, int axisIndex)
    {
        _hasDisplayAxis = deviceGuid != Guid.Empty && axisIndex >= 0;
        _displayDeviceGuid = deviceGuid;
        _displayAxisIndex = axisIndex;

        UI(() =>
        {
            AxisBarEnabled = _hasDisplayAxis;
            AxisBarValue = _hasDisplayAxis ? AxisBarValue : 0.0;

            // IMPORTANT: RelayCommand doesn't auto-requery CanExecute in WPF.
            // Without this, AB/IDLE buttons can stay disabled even after AxisBarEnabled becomes true.
            SetAbDetentCommand.RaiseCanExecuteChanged();
            SetIdleDetentCommand.RaiseCanExecuteChanged();
        });
    }

    private void UpdateAxisBar(AxisStats st, int[] now, int axisIndex)
    {
        if ((uint)axisIndex >= (uint)now.Length) return;

        // Read raw device axis value.
        int v = now[axisIndex];

        // Clamp to Falcon-native range (0..65535).
        if (v < 0) v = 0;
        if (v > FallbackRange) v = FallbackRange;

        // 1:1: detents capture from RAW axis state.
        _lastRawAxisValue = v;

        // Keep your logical value for any existing non-display usage (leave semantics untouched).
        _lastLogicalAxisValue = Invert ? v : (FallbackRange - v);

        // 1:1 with original launcher display:
        // The bar uses RAW normalized, then a function-based "invert display" rule is applied.
        double rawNorm = (double)v / (double)FallbackRange; // 0..1

        // Original launcher "InvertAxisDisp" concept:
        // Special axes (Throttle / Throttle_Right): unchecked invert => reversed display, checked => normal.
        // Other axes: unchecked invert => normal, checked => reversed.
        bool isSpecial =
            _function == AxisFunction.Throttle ||
            _function == AxisFunction.Throttle_Right ||
            _function == AxisFunction.COMM_Channel_1 ||
            _function == AxisFunction.COMM_Channel_2 ||
            _function == AxisFunction.MSL_Volume ||
            _function == AxisFunction.Threat_Volume ||
            _function == AxisFunction.IntercomVolumeVolume ||
            _function == AxisFunction.AI_vs_IVC ||
            _function == AxisFunction.ILS_Volume_Knob;

        bool reverseDisplay = isSpecial ? !Invert : Invert;

        double displayNorm = reverseDisplay ? (1.0 - rawNorm) : rawNorm;

        UI(() =>
        {
            AxisBarValue = displayNorm;
            UpdateThrottleDetentFeedback();
        });
    }

    private int InvertNumForAxisBarDisplay()
    {
        // 1:1 with original launcher InvertAxisDisp(),
        // but only for axis functions that exist in this rewrite.

        bool isSpecial =
            _function == AxisFunction.Throttle ||
            _function == AxisFunction.Throttle_Right ||
            _function == AxisFunction.COMM_Channel_1 ||
            _function == AxisFunction.COMM_Channel_2 ||
            _function == AxisFunction.MSL_Volume ||
            _function == AxisFunction.Threat_Volume ||
            _function == AxisFunction.IntercomVolumeVolume ||
            _function == AxisFunction.AI_vs_IVC ||
            _function == AxisFunction.ILS_Volume_Knob;

        if (isSpecial)
        {
            // Special group: unchecked => -1, checked => +1
            return Invert ? 1 : -1;
        }

        // Default group: unchecked => +1, checked => -1
        return Invert ? -1 : 1;
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

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _di.Dispose(); } catch { }
    }
}