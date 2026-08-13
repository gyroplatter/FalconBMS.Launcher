namespace FalconBMS.Launcher.Services;

/// <summary>
/// Handles Main-view background artwork selection for theaters,
/// including DEBUG-only preview theater entries.
/// </summary>
public sealed class TheaterBackgroundService
{
    private const string DebugTheaterSuffix = " - only for debug";

    private const string DefaultBackground = "/Assets/background-main-korea.jpg";

    private static readonly IReadOnlyDictionary<string, string> TheaterBackgrounds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Balkans"] = "/Assets/background-main-balkans.jpg",
            ["Hellas"] = "/Assets/background-main-hellas.jpg",
            ["Israel"] = "/Assets/background-main-israel.jpg"
        };

    /// <summary>
    /// Returns the Main background image for the selected theater.
    /// Unknown theaters use the default background.
    /// </summary>
    public string GetBackgroundImage(string? theater)
    {
        string theaterName = GetRealTheaterName(theater);

        if (TheaterBackgrounds.TryGetValue(theaterName, out string? background))
            return background;

        return DefaultBackground;
    }

    /// <summary>
    /// Returns true when the dropdown entry exists only for DEBUG previewing.
    /// </summary>
    public bool IsDebugOnlyTheater(string? theater)
    {
        if (theater is null)
            return false;

        return theater.EndsWith(
            DebugTheaterSuffix,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds preview entries for theaters that have custom artwork but are not installed.
    /// In Release builds this method does nothing.
    /// </summary>
    public void AddDebugPreviewTheaters(ICollection<string> theaters)
    {
#if DEBUG
        foreach (string theaterName in TheaterBackgrounds.Keys)
        {
            bool installed = theaters.Any(
                existing => string.Equals(
                    existing,
                    theaterName,
                    StringComparison.OrdinalIgnoreCase));

            if (!installed)
                theaters.Add(theaterName + DebugTheaterSuffix);
        }
#endif
    }

    /// <summary>
    /// Removes the DEBUG-only label before using the theater name
    /// for background artwork lookup.
    /// </summary>
    private static string GetRealTheaterName(string? theater)
    {
        if (theater is null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(theater))
            return string.Empty;

        if (theater.EndsWith(
                DebugTheaterSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return theater.Substring(
                0,
                theater.Length - DebugTheaterSuffix.Length);
        }

        return theater;
    }
}