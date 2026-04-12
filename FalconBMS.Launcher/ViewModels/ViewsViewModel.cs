namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// View-related launcher options that will later be written into the 4.38 POP output.
/// For now this only provides the UI and persists the values in launcher settings.
/// </summary>
public sealed class ViewsViewModel : ViewModelBase
{
    private bool _useRollAxisForNws = Properties.Settings.Default.Misc_RLNWS;
    public bool UseRollAxisForNws
    {
        get => _useRollAxisForNws;
        set
        {
            if (!Set(ref _useRollAxisForNws, value)) return;
            Properties.Settings.Default.Misc_RLNWS = value;
            Properties.Settings.Default.Save();
        }
    }

    /// <summary>
    /// Matches the original launcher meaning:
    /// false = Head Forward
    /// true = Zoom FOV
    /// </summary>
    private bool _trackIrZoomFov = Properties.Settings.Default.Misc_TrackIRZ;
    public bool TrackIrZoomFov
    {
        get => _trackIrZoomFov;
        set
        {
            if (!Set(ref _trackIrZoomFov, value)) return;

            Properties.Settings.Default.Misc_TrackIRZ = value;
            Properties.Settings.Default.Save();

            OnPropertyChanged(nameof(IsTrackIrHeadForward));
            OnPropertyChanged(nameof(IsTrackIrZoomFov));
        }
    }

    public bool IsTrackIrHeadForward
    {
        get => !TrackIrZoomFov;
        set
        {
            if (!value) return;
            TrackIrZoomFov = false;
        }
    }

    public bool IsTrackIrZoomFov
    {
        get => TrackIrZoomFov;
        set
        {
            if (!value) return;
            TrackIrZoomFov = true;
        }
    }

    private bool _externalMouseLook = Properties.Settings.Default.Misc_ExMouseLook;
    public bool ExternalMouseLook
    {
        get => _externalMouseLook;
        set
        {
            if (!Set(ref _externalMouseLook, value)) return;
            Properties.Settings.Default.Misc_ExMouseLook = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _naturalHeadMovement = Properties.Settings.Default.Misc_NaturalHeadMovement;
    public bool NaturalHeadMovement
    {
        get => _naturalHeadMovement;
        set
        {
            if (!Set(ref _naturalHeadMovement, value)) return;
            Properties.Settings.Default.Misc_NaturalHeadMovement = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _pilotModel = Properties.Settings.Default.Misc_PilotModel;
    public bool PilotModel
    {
        get => _pilotModel;
        set
        {
            if (!Set(ref _pilotModel, value)) return;
            Properties.Settings.Default.Misc_PilotModel = value;
            Properties.Settings.Default.Save();
        }
    }

    private bool _smartScalingEnabled = Properties.Settings.Default.Misc_SmartScalingOverride;
    public bool SmartScalingEnabled
    {
        get => _smartScalingEnabled;
        set
        {
            if (!Set(ref _smartScalingEnabled, value)) return;
            Properties.Settings.Default.Misc_SmartScalingOverride = value;
            Properties.Settings.Default.Save();
        }
    }
}