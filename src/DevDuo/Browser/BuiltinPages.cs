namespace DevDuo;

public enum BuiltinPageKind { None, Welcome, ColorLaboratory }

// Built-in documents have distinct synthetic origins. They are HTML strings,
// never receiver requests or remembered developer URLs.
public static class BuiltinPages
{
    public const string WelcomeAddress = "http://welcome.devduo.invalid/";
    public const string ColorAddress = "http://devduo.local/";

    public static BuiltinPageKind FromAddress(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsDefaultPort ||
            uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return BuiltinPageKind.None;
        return uri.Host switch
        {
            "welcome.devduo.invalid" => BuiltinPageKind.Welcome,
            "devduo.local" => BuiltinPageKind.ColorLaboratory,
            _ => BuiltinPageKind.None
        };
    }

    public static Uri Address(BuiltinPageKind kind) => new(kind switch
    {
        BuiltinPageKind.Welcome => WelcomeAddress,
        BuiltinPageKind.ColorLaboratory => ColorAddress,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    });

    public static (Uri Address, BuiltinPageKind BuiltinPage, bool InvalidOverride) Initial(string? overrideUrl, string? rememberedUrl)
    {
        if (overrideUrl is not null)
        {
            if (CalibrationUrl.TryNormalize(overrideUrl, out var target))
                return (target, FromAddress(target.AbsoluteUri), false);
            return (Address(BuiltinPageKind.Welcome), BuiltinPageKind.Welcome, true);
        }
        if (CalibrationUrl.TryNormalize(rememberedUrl, out var remembered) && FromAddress(remembered.AbsoluteUri) == BuiltinPageKind.None)
            return (remembered, BuiltinPageKind.None, false);
        return (Address(BuiltinPageKind.Welcome), BuiltinPageKind.Welcome, false);
    }
}
