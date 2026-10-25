namespace SnapStudio.App.Help;

internal static class HelpContent
{
    public const string HostName = "snapstudio-help.invalid";
    public const string HomeUrl = "https://" + HostName + "/index.html";

    public static bool IsHelpPage(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/index.html"
        && uri.Query.Length == 0;
}
