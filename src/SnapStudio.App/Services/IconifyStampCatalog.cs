using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SnapStudio.Core.Documents;

namespace SnapStudio.App.Services;

/// <summary>
/// Fetches only approved Iconify collections. The injected client must use a handler with
/// AllowAutoRedirect = false so redirects cannot make requests outside the fixed API origin.
/// The caller owns the client and its lifetime.
/// </summary>
public sealed class IconifyStampCatalog(HttpClient client) : IOnlineStampCatalog
{
    public const int PageSize = 32;
    private const int MaximumResponseBytes = 1_000_000;
    private const string AllPrefixes = "heroicons,tabler,lucide";
    private static readonly Uri ApiBase = new("https://api.iconify.design/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly BoundedCache<string, OnlineStampSearchResult> _searchCache = new(16);
    private readonly BoundedCache<string, string> _svgCache = new(32);

    public async Task<OnlineStampSearchResult> SearchAsync(string query, string? prefix, int start, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        query = query.Trim();
        if (query.Length is < 1 or > 100) throw new ArgumentException("Enter a search between 1 and 100 characters.", nameof(query));
        if (prefix is not null && !IsAllowedPrefix(prefix)) throw new ArgumentException("Choose Heroicons, Tabler, or Lucide.", nameof(prefix));
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        token.ThrowIfCancellationRequested();
        string relativeUri = $"search?query={Uri.EscapeDataString(query)}&prefixes={prefix ?? AllPrefixes}&limit={PageSize}&start={start.ToString(CultureInfo.InvariantCulture)}";
        if (_searchCache.TryGet(relativeUri, out OnlineStampSearchResult cached)) return cached;

        byte[] bytes = await ReadAsync(new Uri(ApiBase, relativeUri), token).ConfigureAwait(false);
        OnlineStampSearchResult result;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("icons", out JsonElement icons) || icons.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("collections", out JsonElement collections) || collections.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Iconify returned an invalid search response. Try again later.");

            var matches = new List<OnlineStampIcon>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in icons.EnumerateArray().Take(PageSize))
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { } id || !TrySplitId(id, out string collectionPrefix, out string name)
                    || (prefix is not null && collectionPrefix != prefix) || !seen.Add(id)
                    || !collections.TryGetProperty(collectionPrefix, out JsonElement metadata)
                    || !TryReadAttribution(metadata, id, collectionPrefix, name, out StampAttribution attribution)) continue;
                string displayName = name.Replace('-', ' ');
                if (displayName.Length > 80) displayName = displayName[..77] + "...";
                matches.Add(new OnlineStampIcon(id, displayName, attribution.Collection, attribution));
            }
            // Iconify's total is the returned page length, not the count of all matching icons.
            result = new OnlineStampSearchResult(matches.AsReadOnly(), icons.GetArrayLength() >= PageSize);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Iconify returned an invalid search response. Try again later.", error);
        }
        token.ThrowIfCancellationRequested();
        _searchCache.Add(relativeUri, result);
        return result;
    }

    public async Task<string> DownloadAsync(OnlineStampIcon icon, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(icon);
        if (!TrySplitId(icon.Id, out string prefix, out string name)) throw new ArgumentException("This icon is not from an approved Iconify collection.", nameof(icon));
        token.ThrowIfCancellationRequested();
        if (_svgCache.TryGet(icon.Id, out string cached)) return cached;
        byte[] bytes = await ReadAsync(new Uri(ApiBase, $"{prefix}/{name}.svg?color=%23000000&height=auto"), token).ConfigureAwait(false);
        string normalized;
        try { normalized = SvgShape.Normalize(Utf8.GetString(bytes)); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Iconify returned an invalid SVG document.", error); }
        token.ThrowIfCancellationRequested();
        _svgCache.Add(icon.Id, normalized);
        return normalized;
    }

    private async Task<byte[]> ReadAsync(Uri uri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is { } actualUri && actualUri != uri)
                throw new InvalidDataException("Iconify returned an unexpected redirect. Try again later.");
            if (!response.IsSuccessStatusCode)
            {
                string message = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "Iconify is receiving too many requests. Wait a moment and try again.",
                    HttpStatusCode.NotFound => "This Iconify icon or search is no longer available. Try another search.",
                    _ when (int)response.StatusCode is >= 300 and < 400 => "Iconify returned a redirect that was blocked. Try again later.",
                    _ => $"Iconify could not complete the request (HTTP {(int)response.StatusCode}). Try again later."
                };
                throw new HttpRequestException(message, null, response.StatusCode);
            }
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes) throw ResponseTooLarge();
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes) throw ResponseTooLarge();
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Iconify took too long to respond. Check your connection and try again.", error);
        }
        catch (HttpRequestException error) when (error.StatusCode is null)
        {
            throw new HttpRequestException("Cannot reach Iconify. Check your internet connection and try again.", error);
        }
    }

    private static InvalidDataException ResponseTooLarge() => new("Iconify returned a response larger than 1 MB. Try another icon or search.");

    private static bool IsAllowedPrefix(string prefix) => prefix is "heroicons" or "tabler" or "lucide";

    private static bool TrySplitId(string? id, out string prefix, out string name)
    {
        prefix = "";
        name = "";
        if (id is null || id.Length > 160) return false;
        int separator = id.IndexOf(':');
        if (separator < 1) return false;
        prefix = id[..separator];
        name = id[(separator + 1)..];
        return IsAllowedPrefix(prefix) && name.Length > 0 && name[0] != '-' && name[^1] != '-'
            && name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    private static bool TryReadAttribution(JsonElement metadata, string id, string prefix, string name, out StampAttribution attribution)
    {
        attribution = null!;
        if (metadata.ValueKind != JsonValueKind.Object || !TryReadText(metadata, "name", 80, out string collection)
            || !metadata.TryGetProperty("author", out JsonElement author) || !TryReadText(author, "name", 200, out string authorName)
            || !metadata.TryGetProperty("license", out JsonElement license) || !TryReadText(license, "title", 120, out string licenseName)
            || !TryReadText(license, "url", 2048, out string licenseUrl)
            || !Uri.TryCreate(licenseUrl, UriKind.Absolute, out Uri? parsedUrl) || parsedUrl.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(parsedUrl.UserInfo)) return false;
        attribution = new StampAttribution("Iconify", id, collection, authorName, licenseName, licenseUrl, $"https://icon-sets.iconify.design/{prefix}/{name}/");
        return true;
    }

    private static bool TryReadText(JsonElement value, string property, int maximumLength, out string text)
    {
        text = "";
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out JsonElement element) || element.ValueKind != JsonValueKind.String
            || element.GetString() is not { } result || string.IsNullOrWhiteSpace(result) || result.Length > maximumLength || result.Any(char.IsControl)) return false;
        text = result;
        return true;
    }

    private sealed class BoundedCache<TKey, TValue>(int capacity) where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _entries = [];
        private readonly Queue<TKey> _order = [];
        private readonly object _sync = new();

        public bool TryGet(TKey key, out TValue value)
        {
            lock (_sync) return _entries.TryGetValue(key, out value!);
        }

        public void Add(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_entries.ContainsKey(key)) return;
                while (_entries.Count >= capacity) _entries.Remove(_order.Dequeue());
                _entries.Add(key, value);
                _order.Enqueue(key);
            }
        }
    }
}
