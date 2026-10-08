using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// One resolved application displayed in the Community Tools strip
/// </summary>
public sealed class ThirdPartyToolItem : INotifyPropertyChanged
{
    private string _executablePath = "";
    private bool _isVisible = true;
    private ImageSource? _iconSource;

    /// <summary>
    /// Stable identifier. Stock tool IDs come from Stock\ThirdPartyTools.json
    /// custom tools receive a generated ID
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Name shown underneath the circular icon
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Expected executable filename for a stock tool.
    /// Custom tools leave this empty.
    /// </summary>
    public string ExecutableName { get; set; } = "";

    /// <summary>
    /// Stock BMS tools normally use a path relative to the selected BMS install.
    /// Custom tools and external stock-tool overrides use an absolute path.
    /// </summary>
    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (string.Equals(_executablePath, value, StringComparison.Ordinal))
                return;

            _executablePath = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Hidden stock tools remain in the runtime collection so their order
    /// is preserved and restoring them is only a visibility change
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
                return;

            _isVisible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// TrackIR remains the one stock tool with special discovery behavior
    /// </summary>
    [JsonIgnore]
    public bool IsSeededTrackIr =>
        string.Equals(
            Id,
            "trackir",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runtime-only image loaded from the Launcher-owned icon cache
    /// </summary>
    [JsonIgnore]
    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (ReferenceEquals(_iconSource, value))
                return;

            _iconSource = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}