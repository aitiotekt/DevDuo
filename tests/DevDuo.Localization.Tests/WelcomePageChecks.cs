using System.Net;
using System.Text.RegularExpressions;
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
                check(html.Contains("href=\"" + WebUtility.HtmlEncode(ProjectLinks.Repository) + "\"") &&
                    html.Contains("href=\"" + WebUtility.HtmlEncode(ProjectLinks.Documentation) + "\""),
                    "The welcome page uses the metadata-derived project links.");
                check(!Regex.IsMatch(html, "<(?:script|img)[^>]+src=|<link[^>]+href=", RegexOptions.IgnoreCase),
                    "The welcome document needs no remote assets or receiver.");
                check(html.Contains("data-devduo-page=\"welcome-v1\"") && !html.Contains("data-devduo-fixture"),
                    "Welcome has its own marker, separate from measurement fixtures.");
                foreach (Match key in Regex.Matches(html, "data-i18n=\"([^\"]+)\""))
                    check(language.WelcomeStrings().ContainsKey(key.Groups[1].Value), "Welcome text has a catalog entry: " + key.Groups[1].Value);
                check(language.WelcomeStrings()["Welcome.Title"] == AppLocalization.Catalog(selection)["Welcome.Title"],
                    "Welcome uses the selected catalog.");
                var script = WelcomePage.LocalizationScript(language);
                check(script.Contains("welcome-v1") && !script.Contains("location") && !script.Contains("loadHTML"),
                    "Welcome localization only calls its guarded in-place translator.");
                check(new PreferenceStore(path).Host.LastUrl == remembered,
                    "Changing language or rendering welcome does not overwrite the remembered developer URL.");
            }
        }
        finally { Directory.Delete(temporary, true); }
    }
}
