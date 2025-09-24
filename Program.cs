using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Collections.Generic;

class Program
{
    static async Task Main()
    {
        var allResults = new List<object>();

        // 1️⃣ Google Trends (US & India)
        allResults.AddRange(await FetchGoogleTrends("US", "United States"));
        allResults.AddRange(await FetchGoogleTrends("IN", "India"));

        // 2️⃣ Reddit Hot Posts
        allResults.AddRange(await FetchRedditHot("news", 10));
        allResults.AddRange(await FetchRedditHot("worldnews", 10));
        allResults.AddRange(await FetchRedditHot("india", 10)); // regional for India

        // 3️⃣ Hacker News Top Stories
        allResults.AddRange(await FetchHackerNewsTop(10));

        // 4️⃣ BBC News Top Stories
        allResults.AddRange(await FetchRssNews("http://feeds.bbci.co.uk/news/rss.xml", "BBC", 10));

        // 5️⃣ Yahoo News Top Stories
        allResults.AddRange(await FetchRssNews("https://news.yahoo.com/rss/", "Yahoo News", 10));

        // 6️⃣ Google News Top Stories (US & India)
        allResults.AddRange(await FetchRssNews("https://news.google.com/rss?hl=en-US&gl=US&ceid=US:en", "Google News US", 10));
        allResults.AddRange(await FetchRssNews("https://news.google.com/rss?hl=en-IN&gl=IN&ceid=IN:en", "Google News India", 10));

        // Date stamp
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // ---------------- Console Output ----------------
        Console.WriteLine($"\n================ Trending Topics ({date}) ================\n");
        foreach (var item in allResults)
        {
            switch (item)
            {
                case TrendResult t:
                    Console.WriteLine($"[Google Trends - {t.Country}] {t.Rank}. {t.Title}");
                    Console.WriteLine($"Link: {t.Link}\n");
                    break;
                case RedditTrend r:
                    Console.WriteLine($"[Reddit] {r.Rank}. {r.Title} (Score: {r.Score})");
                    Console.WriteLine($"Link: {r.Link}\n");
                    break;
                case HackerNewsTrend hn:
                    Console.WriteLine($"[HackerNews] {hn.Rank}. {hn.Title} (Score: {hn.Score})");
                    Console.WriteLine($"Link: {hn.Link}\n");
                    break;
                case RssTrend rss:
                    Console.WriteLine($"[{rss.Source}] {rss.Rank}. {rss.Title}");
                    Console.WriteLine($"Link: {rss.Link}\n");
                    break;
            }
        }

        // ---------------- Save JSON ----------------
        string jsonFile = $"trending_{date}.json";
        string json = JsonSerializer.Serialize(allResults, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonFile, json);

        // ---------------- Save CSV ----------------
        string csvFile = $"trending_{date}.csv";
        var csv = new StringBuilder();
        csv.AppendLine("Type,Source,Title,Link,Extra");

        foreach (var item in allResults)
        {
            switch (item)
            {
                case TrendResult t:
                    csv.AppendLine($"Search,Google,{t.Title},{t.Link},");
                    break;
                case RedditTrend r:
                    csv.AppendLine($"Post,Reddit,{r.Title},{r.Link},{r.Score}");
                    break;
                case HackerNewsTrend hn:
                    csv.AppendLine($"Post,HackerNews,{hn.Title},{hn.Link},{hn.Score}");
                    break;
                case RssTrend rss:
                    csv.AppendLine($"Article,{rss.Source},{rss.Title},{rss.Link},");
                    break;
            }
        }

        await File.WriteAllTextAsync(csvFile, csv.ToString());
        Console.WriteLine($"\n✅ Trending topics saved to JSON: {jsonFile} and CSV: {csvFile}");
    }

    // ---------------- Helper Functions ----------------

    // Google Trends
    static async Task<List<TrendResult>> FetchGoogleTrends(string geo, string country)
    {
        var results = new List<TrendResult>();
        string url = $"https://trends.google.com/trending/rss?geo={geo}";
        using var client = new HttpClient();
        var stream = await client.GetStreamAsync(url);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        int rank = 1;
        foreach (var item in feed.Items.Take(10))
        {
            results.Add(new TrendResult
            {
                Country = country,
                Rank = rank++,
                Title = item.Title.Text,
                Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? ""
            });
        }
        return results;
    }

    // Reddit Hot Posts
    static async Task<List<RedditTrend>> FetchRedditHot(string subreddit, int count)
    {
        var results = new List<RedditTrend>();
        string url = $"https://www.reddit.com/r/{subreddit}/hot.json?limit={count}";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "dotnet-trending-bot");
        var json = await client.GetStringAsync(url);

        var data = JsonSerializer.Deserialize<RedditResponse>(json);
        int rank = 1;
        foreach (var post in data.Data.Children)
        {
            results.Add(new RedditTrend
            {
                Rank = rank++,
                Title = post.Data.Title,
                Link = "https://reddit.com" + post.Data.Permalink,
                Score = post.Data.Score
            });
        }
        return results;
    }

    // Hacker News Top Stories
    static async Task<List<HackerNewsTrend>> FetchHackerNewsTop(int count)
    {
        var results = new List<HackerNewsTrend>();
        using var client = new HttpClient();
        var topIdsJson = await client.GetStringAsync("https://hacker-news.firebaseio.com/v0/topstories.json");
        var topIds = JsonSerializer.Deserialize<List<int>>(topIdsJson).Take(count);

        int rank = 1;
        foreach (var id in topIds)
        {
            var storyJson = await client.GetStringAsync($"https://hacker-news.firebaseio.com/v0/item/{id}.json");
            var story = JsonSerializer.Deserialize<HackerNewsItem>(storyJson);

            results.Add(new HackerNewsTrend
            {
                Rank = rank++,
                Title = story.Title,
                Link = story.Url ?? $"https://news.ycombinator.com/item?id={story.Id}",
                Score = story.Score
            });
        }
        return results;
    }

    // RSS News (BBC, Yahoo, Google News)
    static async Task<List<RssTrend>> FetchRssNews(string url, string sourceName, int count)
    {
        var results = new List<RssTrend>();
        using var client = new HttpClient();
        var stream = await client.GetStreamAsync(url);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        int rank = 1;
        foreach (var item in feed.Items.Take(count))
        {
            results.Add(new RssTrend
            {
                Rank = rank++,
                Source = sourceName,
                Title = item.Title.Text,
                Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? ""
            });
        }
        return results;
    }

    // ---------------- Model Classes ----------------
    public class TrendResult { public string Country; public int Rank; public string Title; public string Link; }
    public class RedditTrend { public int Rank; public string Title; public string Link; public int Score; }
    public class HackerNewsTrend { public int Rank; public string Title; public string Link; public int Score; }
    public class RssTrend { public int Rank; public string Source; public string Title; public string Link; }

    // Reddit API models
    public class RedditResponse { public RedditData Data { get; set; } = new(); }
    public class RedditData { public List<RedditChild> Children { get; set; } = new(); }
    public class RedditChild { public RedditPost Data { get; set; } = new(); }
    public class RedditPost { public string Title { get; set; } = ""; public string Permalink { get; set; } = ""; public int Score { get; set; } }

    // Hacker News API model
    public class HackerNewsItem { public int Id { get; set; } public string Title { get; set; } = ""; public string Url { get; set; } = ""; public int Score { get; set; } }
}
