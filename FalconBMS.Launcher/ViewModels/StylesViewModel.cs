using System.Collections.ObjectModel;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// Debug-only style guide view model used to preview common control states in one place.
/// This is intentionally simple and does not write any launcher settings.
/// </summary>
public sealed class StylesViewModel : ViewModelBase
{
    private string _sampleText = "Sample text";
    public string SampleText
    {
        get => _sampleText;
        set => Set(ref _sampleText, value);
    }

    private string _selectedThemeOption = "Dark";
    public string SelectedThemeOption
    {
        get => _selectedThemeOption;
        set => Set(ref _selectedThemeOption, value);
    }

    private bool _sampleCheckBox = true;
    public bool SampleCheckBox
    {
        get => _sampleCheckBox;
        set => Set(ref _sampleCheckBox, value);
    }

    private bool _secondaryCheckBox;
    public bool SecondaryCheckBox
    {
        get => _secondaryCheckBox;
        set => Set(ref _secondaryCheckBox, value);
    }

    private bool _radioOptionA = true;
    public bool RadioOptionA
    {
        get => _radioOptionA;
        set
        {
            if (!Set(ref _radioOptionA, value)) return;
            if (value)
                RadioOptionB = false;
        }
    }

    private bool _radioOptionB;
    public bool RadioOptionB
    {
        get => _radioOptionB;
        set
        {
            if (!Set(ref _radioOptionB, value)) return;
            if (value)
                RadioOptionA = false;
        }
    }

    public ObservableCollection<string> ThemeOptions { get; } = new()
    {
        "Dark",
        "Light",
        "System"
    };

    public ObservableCollection<string> SampleDropdownItems { get; } = new()
    {
        "Dropdown Item 1",
        "Dropdown Item 2",
        "Dropdown Item 3"
    };

    private string _selectedDropdownItem = "Dropdown Item 2";
    public string SelectedDropdownItem
    {
        get => _selectedDropdownItem;
        set => Set(ref _selectedDropdownItem, value);
    }
}