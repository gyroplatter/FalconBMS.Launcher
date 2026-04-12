using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using FalconBMS.Launcher.Models;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Downloads and parses RSS/news feed data for the launcher home screen.
/// </summary>
public sealed class RssService
{
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Can add multiple feeds here if desired.
    private static readonly Uri FeedUrl = new("https://www.falcon-lounge.com/feed/");

    public async Task<IReadOnlyList<RssItem>> FetchAsync(int maxItems, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(FeedUrl, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

        var feed = SyndicationFeed.Load(reader);
        if (feed is null) return Array.Empty<RssItem>();

        var items = feed.Items
            .Where(i => i.Links.FirstOrDefault()?.Uri is not null)
            .Take(maxItems)
            .Select(i =>
            {
                var link = i.Links.First().Uri;
                var published = i.PublishDate != default ? i.PublishDate : i.LastUpdatedTime;
                var title = i.Title?.Text?.Trim() ?? "(Untitled)";
                var desc = i.Summary?.Text ?? "";
                desc = StripHtml(desc).Trim();

                return new RssItem
                {
                    Published = published,
                    Title = title,
                    Description = desc,
                    Link = link
                };
            })
            .ToList();

        return items;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        // Simple HTML tag removal for RSS summaries
        return Regex.Replace(input, "<.*?>", string.Empty);
    }
}