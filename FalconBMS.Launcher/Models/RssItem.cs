namespace FalconBMS.Launcher.Models;

/// <summary>
/// For displaying news/RSS feed
/// </summary>

public sealed class RssItem
{
    public required DateTimeOffset Published { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required Uri Link { get; init; }
}