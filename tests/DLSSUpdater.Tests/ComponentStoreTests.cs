using System.Net;
using DLSSUpdater.Core;

namespace DLSSUpdater.Tests;

/// <summary>Download checks against a fake GitHub: no request leaves the machine.</summary>
public class ComponentStoreTests
{
    private sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(respond(request));
        }
    }

    public ComponentStoreTests()
    {
        AppPaths.Root = Path.Combine(Path.GetTempPath(), "dlssu-store-" + Guid.NewGuid().ToString("N")[..8]);
        AppPaths.Ensure();
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>MFG Unlock's repo answers with <paramref name="mfgReleases"/>, every other API call with an empty list.</summary>
    private static FakeGitHub Releases(string mfgReleases, string dlssReleases = "[]", HttpStatusCode head = HttpStatusCode.OK) => new(r =>
    {
        var url = r.RequestUri!.ToString();
        if (r.Method == HttpMethod.Head) return new HttpResponseMessage(head);
        if (url.Contains(ComponentStore.MfgRepo + "/releases")) return Json(mfgReleases);
        if (url.Contains(ComponentStore.DlssRepo + "/releases")) return Json(dlssReleases);
        if (url.StartsWith("https://api.github.com/")) return Json("[]");
        if (url.EndsWith(".addon64")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task Mfg_AnyOtherAddonInTheRelease_IsNotAccepted()
    {
        var gh = Releases("""[{"tag_name":"2.0","assets":[{"name":"something-else.addon64","browser_download_url":"https://example.invalid/something-else.addon64","digest":null}]}]""");
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Null(store.Mfg);
    }

    [Fact]
    public async Task Mfg_AssetWithoutDigest_IsRefused()
    {
        var gh = Releases($$"""[{"tag_name":"2.0","assets":[{"name":"{{ComponentStore.MfgFile}}","browser_download_url":"https://example.invalid/{{ComponentStore.MfgFile}}"}]}]""");
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("2.0", store.Mfg?.Tag);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureMfgAsync(null, null, default));
        Assert.False(ComponentStore.IsCached(Component.MfgUnlock, "2.0"));
        Assert.False(File.Exists(Path.Combine(ComponentStore.TagDir(Component.MfgUnlock, "2.0"), ComponentStore.MfgFile)));
    }

    [Fact]
    public async Task Dlss_PinnedVersion_RateLimitedCheckFails_InsteadOfSkippingFiles()
    {
        var store = new ComponentStore(new GitHubClient(Releases("[]", head: HttpStatusCode.TooManyRequests)), () => true);
        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsureDlssAsync("v310.1.0", null, default));
        Assert.False(ComponentStore.IsCached(Component.Dlss, "v310.1.0"));
    }

    [Fact]
    public async Task Dlss_Latest_RateLimitedCheck_NeverFallsBackToMain()
    {
        var gh = Releases("[]", dlssReleases: """[{"tag_name":"v310.2.0","assets":[]}]""", head: HttpStatusCode.ServiceUnavailable);
        var store = new ComponentStore(new GitHubClient(gh), () => true);
        await store.RefreshAsync(default);
        Assert.Equal("v310.2.0", store.Dlss?.Tag);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsureDlssAsync(null, null, default));
        Assert.DoesNotContain(gh.Requests, r => r.Contains("/main/"));
        Assert.False(ComponentStore.IsCached(Component.Dlss, "v310.2.0"));
    }
}
