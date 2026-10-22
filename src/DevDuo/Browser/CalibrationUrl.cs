using System.Net;
using System.Text.RegularExpressions;
namespace DevDuo;

// Measurement labels read by web/calibration.js. They describe the requested host scene;
// the page still records its own observed viewport.
public sealed record CalibrationLabels(string Pose, string Browser, string DisplayMode, string Notes);

public static class CalibrationUrl
{
    public const string DefaultAddress = "http://127.0.0.1:8765/";
    public const int ReceiverPort = 8765;
    private static readonly string[] LabelKeys = ["device", "pose", "browser", "state", "displayMode", "notes"];

    // Accepts http/https addresses. A missing scheme is inferred: http for loopback and
    // local hosts, https otherwise. Other schemes are rejected rather than rewritten.
    public static bool TryNormalize(string? input, out Uri uri)
    {
        uri = null!;
        var value = input?.Trim() ?? "";
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
            return false;
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            // "mailto:x", "about:blank" and similar carry another scheme; "localhost:5173" carries a port.
            if (Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9+.-]*:(?!\d)"))
                return false;
            var authority = value.Split('/', '?', '#')[0];
            var host = authority.StartsWith('[') ? authority.Split(']')[0].TrimStart('[') : authority.Split(':')[0];
            if (host.Length == 0)
                return false;
            value = (IsLocalHost(host) ? "http://" : "https://") + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(parsed.Host))
            return false;
        uri = parsed;
        return true;
    }

    // Only the local measurement page served by `devduo-tools serve` receives labels.
    public static bool IsCalibrationPage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback && uri.Port == ReceiverPort &&
        (uri.AbsolutePath == "/" || uri.AbsolutePath == "/index.html");

    // Replaces previous labels, keeps unrelated query parameters in order and keeps the fragment.
    // Arbitrary developer URLs are returned unchanged.
    public static Uri ApplyLabels(Uri uri, CalibrationLabels labels)
    {
        if (!IsCalibrationPage(uri))
            return uri;
        var parameters = RetainedParameters(uri);
        parameters.Add("device=" + Uri.EscapeDataString("iPhone Duo"));
        parameters.Add("pose=" + Uri.EscapeDataString(labels.Pose));
        parameters.Add("browser=" + Uri.EscapeDataString(labels.Browser));
        parameters.Add("state=initial");
        parameters.Add("displayMode=" + Uri.EscapeDataString(labels.DisplayMode));
        parameters.Add("notes=" + Uri.EscapeDataString(labels.Notes));
        return new UriBuilder(uri) { Query = string.Join('&', parameters) }.Uri;
    }

    // The address bar shows the page the developer chose; host-added labels are presentation noise there.
    public static Uri RemoveLabels(Uri uri)
    {
        if (!IsCalibrationPage(uri))
            return uri;
        return new UriBuilder(uri) { Query = string.Join('&', RetainedParameters(uri)) }.Uri;
    }

    private static List<string> RetainedParameters(Uri uri) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Where(pair => !LabelKeys.Contains(Uri.UnescapeDataString(pair.Split('=')[0]), StringComparer.Ordinal))
        .ToList();

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && (IPAddress.IsLoopback(address) || IsPrivate(address)));

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }
}
