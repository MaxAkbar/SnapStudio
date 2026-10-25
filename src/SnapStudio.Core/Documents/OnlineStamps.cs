namespace SnapStudio.Core.Documents;

public sealed record StampAttribution(
    string Provider,
    string IconId,
    string Collection,
    string Author,
    string License,
    string LicenseUrl,
    string SourceUrl);

public sealed record OnlineStampIcon(string Id, string Name, string Collection, StampAttribution Attribution)
{
    public override string ToString() => $"{Name} · {Collection}";
}

public sealed record OnlineStampSearchResult(IReadOnlyList<OnlineStampIcon> Icons, bool HasMore);

public interface IOnlineStampCatalog
{
    Task<OnlineStampSearchResult> SearchAsync(string query, string? prefix, int start, CancellationToken token);
    Task<string> DownloadAsync(OnlineStampIcon icon, CancellationToken token);
}
