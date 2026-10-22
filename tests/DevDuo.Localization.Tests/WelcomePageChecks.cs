using DevDuo;

internal static class WelcomePageChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var remembered = "http://localhost:8765/?keep=1#notes";
        var temporary = Path.Combine(Path.GetTempPath(), "devduo-welcome-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var path = Path.Combine(temporary, "preferences.json");
            var store = new PreferenceStore(path);
            store.SaveHost(new HostSettings { LastUrl = remembered });
            foreach (var selection in new[] { "en", "zh-Hans" })
            {
                var language = new AppLocalization(path, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                language.Select(selection);
                var html = WelcomePage.Html(language);
                check(!html.Contains("__DEVDUO_"), "The welcome template has no unresolved placeholders.");
                check(language.WelcomeStrings()["Welcome.Title"] == AppLocalization.Catalog(selection)["Welcome.Title"],
                    "Welcome uses the selected catalog.");
                check(new PreferenceStore(path).Host.LastUrl == remembered,
                    "Changing language or rendering welcome does not overwrite the remembered developer URL.");
            }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
