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

    // Mutiple RSS URLs can be used
    private static readonly Uri[] FeedUrls =
    {
        new("https://www.falcon-bms.com/rss.xml"),
        new("https://www.falcon-lounge.com/feed/")
    };

    public async Task<IReadOnlyList<RssItem>> FetchAsync(int maxItems, CancellationToken ct)
    {
        var tasks = FeedUrls.Select(feedUrl => FetchFeedAsync(feedUrl, ct)).ToList();
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(items => items)
            .OrderByDescending(i => i.Published)
            .Take(maxItems)
            .ToList();
    }

    private async Task<IReadOnlyList<RssItem>> FetchFeedAsync(Uri feedUrl, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(feedUrl, ct);
            resp.EnsureSuccessStatusCode();

            // .NET Framework 4.8 does not expose the CancellationToken overload here.
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });

            var feed = SyndicationFeed.Load(reader);
            if (feed is null)
                return Array.Empty<RssItem>();

            return feed.Items
                .Where(i => i.Links.FirstOrDefault()?.Uri is not null)
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
        }
        catch (Exception ex)
        {
            DebugDiagnosticsService.Warn($"RSS fetch failed for {feedUrl}: {ex.Message}");
            return Array.Empty<RssItem>();
        }
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        // Simple HTML tag removal for RSS summaries.
        return Regex.Replace(input, "<.*?>", string.Empty);
    }
}