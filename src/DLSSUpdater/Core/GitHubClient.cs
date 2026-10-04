using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLSSUpdater.Core;

public sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("published_at")] public DateTime? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = [];
}

public sealed class GhAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    [JsonPropertyName("digest")] public string? Digest { get; set; }

    public string? Sha256 => Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
        ? d[7..].ToLowerInvariant()
        : null;
}

public sealed class ApiCacheEntry
{
    public string ETag { get; set; } = "";
    public string Body { get; set; } = "";
}

public readonly record struct TransferProgress(string Text, double? Fraction);

/// <summary>Minimal GitHub REST client with ETag caching (304s don't count against the rate limit).</summary>
public sealed class GitHubClient
{
    private static readonly HttpClient SharedHttp = CreateHttp(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(15),
    });
    private readonly HttpClient _http;
    private readonly object _cacheGate = new();
    private Dictionary<string, ApiCacheEntry>? _cache;

    public GitHubClient() => _http = SharedHttp;

    /// <summary>For tests: every request goes to <paramref name="handler"/>.</summary>
    internal GitHubClient(HttpMessageHandler handler) => _http = CreateHttp(handler);

    public string? Token { get; set; }

    private static HttpClient CreateHttp(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DLSSUpdater", "1.0"));
        return http;
    }

    public async Task<List<GhRelease>> GetReleasesAsync(string repo, int perPage, CancellationToken ct)
    {
        var body = await GetApiAsync($"https://api.github.com/repos/{repo}/releases?per_page={perPage}", ct);
        return JsonSerializer.Deserialize(body, JsonCtx.Default.ListGhRelease) ?? [];
    }

    public async Task<List<string>> GetTagsAsync(string repo, int perPage, CancellationToken ct)
    {
        var body = await GetApiAsync($"https://api.github.com/repos/{repo}/tags?per_page={perPage}", ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.EnumerateArray()
            .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
            .OfType<string>()
            .ToList();
    }

    /// <summary>Plain GET of a web page (not the GitHub API), 30 s timeout.</summary>
    public async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var res = await _http.GetAsync(url, cts.Token);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(cts.Token);
    }

    private async Task<string> GetApiAsync(string url, CancellationToken ct)
    {
        var cache = LoadCache();
        ApiCacheEntry? cached;
        lock (_cacheGate) cache.TryGetValue(url, out cached);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(Token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token.Trim());
        if (cached is not null) req.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var res = await _http.SendAsync(req, cts.Token);

        if (res.StatusCode == HttpStatusCode.NotModified && cached is not null) return cached.Body;
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && cached is not null)
        {
            Log.Info("GitHub rate limit hit, using cached release data");
            return cached.Body;
        }
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync(cts.Token);
        if (res.Headers.ETag is { } etag)
        {
            lock (_cacheGate)
            {
                cache[url] = new ApiCacheEntry { ETag = etag.ToString(), Body = body };
                SaveCache(cache);
            }
        }
        return body;
    }

    public async Task DownloadAsync(string url, string dest, string label, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".part";
        using (var res = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            var buffer = new byte[1 << 20];
            long done = 0;
            var lastReport = Environment.TickCount64;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (progress is not null && Environment.TickCount64 - lastReport > 100)
                {
                    lastReport = Environment.TickCount64;
                    progress.Report(new TransferProgress(
                        $"Downloading {label}  {done / 1048576.0:0.0} / {(total ?? 0) / 1048576.0:0.0} MB",
                        total > 0 ? (double)done / total.Value : null));
                }
            }
        }
        File.Move(tmp, dest, true);
    }

    /// <summary>True when the file is there, false only on a 404; anything else (rate limit, server error) throws.</summary>
    public async Task<bool> ExistsAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return false;
        res.EnsureSuccessStatusCode();
        return true;
    }

    private Dictionary<string, ApiCacheEntry> LoadCache()
    {
        lock (_cacheGate)
        {
            if (_cache is not null) return _cache;
            try
            {
                if (File.Exists(AppPaths.ApiCacheFile))
                    _cache = JsonSerializer.Deserialize(File.ReadAllText(AppPaths.ApiCacheFile), JsonCtx.Default.DictionaryStringApiCacheEntry);
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
            return _cache ??= new Dictionary<string, ApiCacheEntry>();
        }
    }

    private static void SaveCache(Dictionary<string, ApiCacheEntry> cache)
    {
        try { FileUtil.AtomicWriteText(AppPaths.ApiCacheFile, JsonSerializer.Serialize(cache, JsonCtx.Default.DictionaryStringApiCacheEntry)); }
        catch (IOException) { }
    }
}
