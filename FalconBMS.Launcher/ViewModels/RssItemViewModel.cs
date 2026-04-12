using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.ViewModels;

/// <summary>
/// UI wrapper around a single RSS item for display formatting.
/// </summary>
public sealed class RssItemViewModel
{
    public RssItemViewModel(RssItem item)
    {
        Published = item.Published;
        Title = item.Title;
        Description = item.Description;
        Link = item.Link;
    }

    public DateTimeOffset Published { get; }
    public string PublishedDisplay => Published.LocalDateTime.ToString("MMM d, yyyy");
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Uri Link { get; }
}