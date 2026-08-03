namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Supplies the Display tab with the same MainViewModel instance used by
/// the Main tab. This keeps duplicate settings controls synchronized.
/// </summary>
public sealed class DisplayViewModel : ViewModelBase
{
    public MainViewModel Main { get; }

    public DisplayViewModel(MainViewModel main)
    {
        Main = main;
    }
}