using DevDuo;

internal static class BuiltinPageChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var fresh = BuiltinPages.Initial(null, null);
        check(fresh.BuiltinPage == BuiltinPageKind.Welcome && fresh.Address.AbsoluteUri == BuiltinPages.WelcomeAddress,
            "A clean install opens the offline welcome document.");
        var remembered = "http://localhost:8765/?keep=1#notes";
        check(BuiltinPages.Initial(null, remembered).Address.AbsoluteUri == remembered,
            "An existing valid receiver URL remains remembered without a silent rewrite.");
        check(BuiltinPages.Initial("https://example.com/new", remembered).Address.AbsoluteUri == "https://example.com/new",
            "A valid environment URL overrides the remembered page.");
        check(BuiltinPages.Initial("not a url", remembered) is { BuiltinPage: BuiltinPageKind.Welcome, InvalidOverride: true },
            "An invalid explicit URL reports its failure on the welcome page.");
        check(BuiltinPages.Initial(null, "javascript:bad").BuiltinPage == BuiltinPageKind.Welcome,
            "An invalid remembered address never becomes a receiver request.");
        check(BuiltinPages.Initial(null, BuiltinPages.ColorAddress).BuiltinPage == BuiltinPageKind.Welcome,
            "Built-in origins are not remembered developer pages.");
        foreach (var address in new[] { "about:blank", "data:text/html,hello", "https://example.com/", "https://welcome.devduo.invalid/",
            "http://devduo.local/custom", "http://devduo.local/?q=1", "http://user@devduo.local/", "http://welcome.devduo.invalid/#other" })
            check(BuiltinPages.FromAddress(address) == BuiltinPageKind.None, "An arbitrary document is not a built-in fixture: " + address);
        check(NavigationState.FromJson("{\"url\":\"" + BuiltinPages.WelcomeAddress + "\"}") is { BuiltinPage: BuiltinPageKind.Welcome, IsLocalFixture: false },
            "Welcome navigation is distinct from the color laboratory.");
    }
}
