using FalconBMS.Launcher.Models;
using FalconBMS.Launcher.Utils;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Top-level shell view model that manages tab switching.
/// Control/device/keymapping state has intentionally been removed.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    public MainViewModel Main { get; } = new();
    public ViewsViewModel Views { get; } = new();
    public ControlsViewModel Controls { get; } = new();
    public StylesViewModel Styles { get; } = new();

    private LauncherTab _currentTab = LauncherTab.Main;

    public LauncherTab CurrentTab
    {
        get => _currentTab;
        set
        {
            if (!Set(ref _currentTab, value)) return;
            OnPropertyChanged(nameof(CurrentViewModel));
        }
    }

    public object CurrentViewModel =>
        CurrentTab switch
        {
            LauncherTab.Views => Views,
            LauncherTab.Controls => Controls,
            LauncherTab.Styles => Styles,
            _ => Main
        };

    public RelayCommand SetTabCommand { get; }

    public MainWindowViewModel()
    {
        SetTabCommand = new RelayCommand(() => { }, () => true);

        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Main.CurrentBindingModel))
                Controls.LoadBindingModel(Main.CurrentBindingModel);
        };

        Controls.LoadBindingModel(Main.CurrentBindingModel);
    }

    public void SetTab(LauncherTab tab) => CurrentTab = tab;

    public void SaveOutputsForClose()
    {
        Main.SaveOutputsForClose();
    }
}