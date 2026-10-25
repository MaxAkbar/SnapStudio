using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SnapStudio.App.Services;
using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class IconifyStampCatalogTests
{
    private const string Svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' width='1em' height='1em'><path fill='#000000' d='M1 1h22v22H1z'/></svg>";

    [TestMethod]
    public async Task Search_UsesEncodedQueryApprovedCollectionsAndExplicitPagination()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody(["tabler:arrow-right"]))));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://unrelated.example/") };
        var catalog = new IconifyStampCatalog(client);

        OnlineStampSearchResult result = await catalog.SearchAsync("  heart & arrow/+  ", null, 64, CancellationToken.None);

        Assert.AreEqual("https://api.iconify.design/search?query=heart%20%26%20arrow%2F%2B&prefixes=heroicons,tabler,lucide&limit=32&start=64", handler.Requests.Single().AbsoluteUri);
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsFalse(result.HasMore);
        OnlineStampIcon icon = result.Icons.Single();
        Assert.AreEqual("tabler:arrow-right", icon.Id);
        Assert.AreEqual("arrow right", icon.Name);
        Assert.AreEqual("Tabler Icons", icon.Collection);
        Assert.AreEqual(new StampAttribution("Iconify", icon.Id, "Tabler Icons", "Paweł Kuna", "MIT", "https://github.com/tabler/tabler-icons/blob/main/LICENSE", "https://icon-sets.iconify.design/tabler/arrow-right/"), icon.Attribution);
        Assert.AreEqual("arrow right · Tabler Icons", icon.ToString());
    }

    [TestMethod]
    public async Task Search_FiltersUnapprovedUnsafeDuplicateAndWrongCollectionIds()
    {
        string[] ids = ["tabler:check", "tabler:check", "lucide:check", "other:check", "tabler:../check", "tabler:check?url=evil", "tabler:CHECK", "tabler:", "tabler:check:extra", "tabler:-check", "tabler:check-"];
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody(ids))));
        using var client = new HttpClient(handler);

        OnlineStampSearchResult result = await new IconifyStampCatalog(client).SearchAsync("check", "tabler", 0, CancellationToken.None);

        Assert.AreEqual("tabler:check", result.Icons.Single().Id);
        Assert.IsTrue(handler.Requests.Single().Query.Contains("prefixes=tabler", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Search_PaginationUsesRawPageSizeAndBoundsDisplayedResults()
    {
        string[] ids = Enumerable.Range(0, 40).Select(index => $"tabler:check-{index}").ToArray();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody(ids))));
        using var client = new HttpClient(handler);

        OnlineStampSearchResult result = await new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None);

        Assert.AreEqual(32, result.Icons.Count);
        Assert.IsTrue(result.HasMore);
    }

    [TestMethod]
    public async Task Search_DoesNotInventMissingAttributionOrFetchMetadataUrls()
    {
        object[] incomplete =
        [
            new { name = "Tabler Icons", license = new { title = "MIT", url = "https://example.com/license" } },
            new { name = "Tabler Icons", author = new { name = "Author" } },
            new { name = "Tabler Icons", author = new { name = "Author" }, license = new { title = "MIT", url = "javascript:alert(1)" } },
            new { name = "Tabler Icons", author = new { name = "" }, license = new { title = "MIT", url = "https://example.com/license" } }
        ];
        foreach (object metadata in incomplete)
        {
            using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody(["tabler:check"], metadata))));
            using var client = new HttpClient(handler);
            OnlineStampSearchResult result = await new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None);
            Assert.IsEmpty(result.Icons);
            Assert.AreEqual(1, handler.Requests.Count);
        }
    }

    [TestMethod]
    public async Task Search_ProducesNamesAndAttributionWithinLibraryLimits()
    {
        string id = "tabler:" + new string('a', 100);
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody([id]))));
        using var client = new HttpClient(handler);
        OnlineStampIcon icon = (await new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None)).Icons.Single();
        Assert.AreEqual(id, icon.Id);
        Assert.AreEqual(80, icon.Name.Length);
        Assert.AreEqual(icon.Attribution, StampCatalog.ValidateAttribution(icon.Attribution));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{\"icons\":[]}")]
    [DataRow("{\"icons\":{},\"collections\":{}}")]
    [DataRow("[]")]
    public async Task Search_ReportsMalformedResponses(string body)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(body)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task Search_RejectsInvalidArgumentsBeforeMakingRequests()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => catalog.SearchAsync("  ", null, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => catalog.SearchAsync(new string('a', 101), null, 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => catalog.SearchAsync("check", "other", 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => catalog.SearchAsync("check", "tabler,lucide", 0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => catalog.SearchAsync("check", null, -1, CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task Download_UsesFixedSvgRouteNormalizesAndReusesThePreview()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(Svg)));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        OnlineStampIcon icon = Icon("tabler:arrow-right");

        string first = await catalog.DownloadAsync(icon, CancellationToken.None);
        string second = await catalog.DownloadAsync(icon, CancellationToken.None);

        Assert.AreEqual("https://api.iconify.design/tabler/arrow-right.svg?color=%23000000&height=auto", handler.Requests.Single().AbsoluteUri);
        Assert.AreEqual(first, second);
        XElement svg = XElement.Parse(first);
        Assert.AreEqual("24", (string?)svg.Attribute("width"));
        Assert.AreEqual("24", (string?)svg.Attribute("height"));
        Assert.AreEqual("#000000", (string?)svg.Descendants().Single().Attribute("fill"));
    }

    [TestMethod]
    [DataRow("other:check")]
    [DataRow("https://example.com/icon.svg")]
    [DataRow("tabler:../check")]
    [DataRow("tabler:check.svg?x=1")]
    [DataRow("tabler:check#fragment")]
    [DataRow("tabler:check:other")]
    [DataRow("tabler:CHECK")]
    [DataRow("tabler:")]
    public async Task Download_RejectsUnapprovedAndUnsafeIdsWithoutRequests(string id)
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new IconifyStampCatalog(client).DownloadAsync(Icon(id), CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    [DataRow("<svg><use href='https://example.com/icon.svg#shape'/></svg>")]
    [DataRow("<svg><rect fill='url(https://example.com/paint)'/></svg>")]
    [DataRow("<svg><script>alert(1)</script><rect/></svg>")]
    [DataRow("<svg><path onclick='bad()'/></svg>")]
    public async Task Download_RejectsActiveOrExternalSvgAndDoesNotCacheFailures(string svg)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(svg)));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => catalog.DownloadAsync(Icon("tabler:check"), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => catalog.DownloadAsync(Icon("tabler:check"), CancellationToken.None));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Responses_AreBoundedWithAndWithoutContentLength(bool download, bool omitContentLength)
    {
        byte[] oversized = new byte[1_000_001];
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = omitContentLength ? new StreamContent(new NonSeekableStream(oversized)) : new ByteArrayContent(oversized)
        }));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => download
            ? catalog.DownloadAsync(Icon("tabler:check"), CancellationToken.None)
            : catalog.SearchAsync("check", null, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task Search_CachesOnlyTheSameQueryCollectionAndPage()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Ok(SearchBody(["tabler:check"]))));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);

        OnlineStampSearchResult first = await catalog.SearchAsync("check", null, 0, CancellationToken.None);
        OnlineStampSearchResult repeat = await catalog.SearchAsync(" check ", null, 0, CancellationToken.None);
        Assert.AreSame(first, repeat);
        await catalog.SearchAsync("check", "tabler", 0, CancellationToken.None);
        await catalog.SearchAsync("check", null, 32, CancellationToken.None);
        await catalog.SearchAsync("other", null, 0, CancellationToken.None);
        Assert.AreEqual(4, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Caches_EvictOldEntriesAndDoNotShareStateAcrossInstances()
    {
        using var handler = new RecordingHandler((request, _) => Task.FromResult(Ok(request.RequestUri!.AbsolutePath == "/search" ? SearchBody([]) : Svg)));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        for (int index = 0; index < 17; index++) await catalog.SearchAsync($"query-{index}", null, 0, CancellationToken.None);
        await catalog.SearchAsync("query-0", null, 0, CancellationToken.None);
        Assert.AreEqual(18, handler.Requests.Count);
        for (int index = 0; index < 33; index++) await catalog.DownloadAsync(Icon($"tabler:item-{index}"), CancellationToken.None);
        await catalog.DownloadAsync(Icon("tabler:item-0"), CancellationToken.None);
        Assert.AreEqual(52, handler.Requests.Count);
        await new IconifyStampCatalog(client).DownloadAsync(Icon("tabler:item-0"), CancellationToken.None);
        Assert.AreEqual(53, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.TooManyRequests, "Wait a moment")]
    [DataRow(HttpStatusCode.NotFound, "no longer available")]
    [DataRow(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    [DataRow(HttpStatusCode.Redirect, "redirect")]
    public async Task Requests_ReportHelpfulHttpErrorsWithoutFollowingRedirects(HttpStatusCode status, string expected)
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri("https://untrusted.example/icon.svg");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpRequestException error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None));
        Assert.AreEqual(status, error.StatusCode);
        Assert.IsTrue(error.Message.Contains(expected, StringComparison.Ordinal));
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Requests_RejectAnUnexpectedFinalResponseUri()
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            HttpResponseMessage response = Ok(SearchBody([]));
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://untrusted.example/result");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new IconifyStampCatalog(client).SearchAsync("check", null, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task Requests_ReportOfflineAndTimeoutSeparatelyFromCancellation()
    {
        using var offline = new RecordingHandler((_, _) => throw new HttpRequestException("DNS failed"));
        using var offlineClient = new HttpClient(offline);
        HttpRequestException error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new IconifyStampCatalog(offlineClient).SearchAsync("check", null, 0, CancellationToken.None));
        Assert.IsTrue(error.Message.Contains("internet connection", StringComparison.Ordinal));

        using var timeout = new RecordingHandler((_, _) => throw new TaskCanceledException("Client timed out"));
        using var timeoutClient = new HttpClient(timeout);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => new IconifyStampCatalog(timeoutClient).SearchAsync("check", null, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task Cancellation_StopsAnInFlightRequestAndDoesNotCacheIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok(SearchBody([]));
        });
        using var client = new HttpClient(handler);
        Task<OnlineStampSearchResult> search = new IconifyStampCatalog(client).SearchAsync("check", null, 0, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => search);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Cancellation_IsHonoredEvenForCachedResults()
    {
        using var handler = new RecordingHandler((request, _) => Task.FromResult(Ok(request.RequestUri!.AbsolutePath == "/search" ? SearchBody([]) : Svg)));
        using var client = new HttpClient(handler);
        var catalog = new IconifyStampCatalog(client);
        await catalog.SearchAsync("check", null, 0, CancellationToken.None);
        await catalog.DownloadAsync(Icon("tabler:check"), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.SearchAsync("check", null, 0, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.DownloadAsync(Icon("tabler:check"), cancellation.Token));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Cancellation_StopsTheResponseBodyRead()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new WaitingStream(started))
        }));
        using var client = new HttpClient(handler);
        Task<string> download = new IconifyStampCatalog(client).DownloadAsync(Icon("tabler:check"), cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => download);
    }

    private static OnlineStampIcon Icon(string id) => new(id, "Test icon", "Test collection", new StampAttribution("Iconify", id, "Test collection", "Author", "MIT", "https://example.com/license", "https://untrusted.example/never-fetch"));

    private static string SearchBody(string[] icons, object? metadata = null) => JsonSerializer.Serialize(new
    {
        icons,
        total = icons.Length,
        limit = 32,
        start = 0,
        collections = new Dictionary<string, object>
        {
            ["tabler"] = metadata ?? new
            {
                name = "Tabler Icons",
                author = new { name = "Paweł Kuna", url = "https://untrusted.example/never-fetch" },
                license = new { title = "MIT", spdx = "MIT", url = "https://github.com/tabler/tabler-icons/blob/main/LICENSE" }
            },
            ["lucide"] = new { name = "Lucide", author = new { name = "Lucide Contributors" }, license = new { title = "ISC", url = "https://example.com/license" } }
        }
    });

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return send(request, cancellationToken);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class WaitingStream(TaskCompletionSource started) : MemoryStream
    {
        public override bool CanSeek => false;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
