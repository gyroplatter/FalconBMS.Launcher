using System.Text.Json.Serialization;
using System.Windows.Media;

namespace FalconBMS.Launcher.Models;

/// <summary>
/// One application displayed in the user-editable Community Tools strip.
/// </summary>
public sealed class ThirdPartyToolItem
{
    /// <summary>
    /// Stable identifier for this item.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Name shown underneath the circular icon.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Full executable path selected by the user.
    /// </summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>
    /// Launcher-owned PNG filename stored beside user.config.
    /// </summary>
    public string? IconCacheFileName { get; set; }

    /// <summary>
    /// Identifies the original built-in F4Wx entry.
    /// This entry preserves the launcher's existing picker and website behavior.
    /// </summary>
    public bool IsBuiltInF4Wx { get; set; }

    /// <summary>
    /// Runtime-only image loaded from the cached PNG.
    /// </summary>
    [JsonIgnore]
    public ImageSource? IconSource { get; set; }
}