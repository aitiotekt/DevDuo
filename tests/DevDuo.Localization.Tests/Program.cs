using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevDuo;

var assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    assertions++;
}

var english = AppLocalization.Catalog("en");
var chinese = AppLocalization.Catalog("zh-Hans");
Check(english.Keys.Order().SequenceEqual(chinese.Keys.Order()), "Resource keys must match exactly.");
foreach (var key in english.Keys)
{
    Check(!string.IsNullOrWhiteSpace(english[key]) && !string.IsNullOrWhiteSpace(chinese[key]), key + " is empty.");
    string[] Placeholders(string value) => Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order().ToArray();
    Check(Placeholders(english[key]).SequenceEqual(Placeholders(chinese[key])), key + " format placeholders differ.");
    foreach (var culture in new[] { "en-US", "zh-CN", "fr-FR" })
        foreach (var text in new[] { english[key], chinese[key] })
            Check(string.Format(CultureInfo.GetCultureInfo(culture), text, Enumerable.Repeat<object>(12.5, 10).ToArray()).Length > 0, key + " format failed.");
}
Check(AppLocalization.Lookup("xx", "Nav.Reload") == english["Nav.Reload"], "Unknown catalog must fall back to English.");
Check(AppLocalization.Lookup("zh-Hans", "missing-key") == "missing-key", "Missing key must remain identifiable.");
foreach (var (culture, expected) in new[] { ("en-GB", "en"), ("fr-FR", "en"), ("zh-CN", "zh-Hans"), ("zh-SG", "zh-Hans"), ("zh-Hans", "zh-Hans"), ("zh-Hant", "en"), ("zh-TW", "en"), ("zh-HK", "en"), ("zh-MO", "en") })
{
    Check(AppLocalization.ResolveLanguage("system", CultureInfo.GetCultureInfo(culture)) == expected, "System resolution: " + culture);
    Check(AppLocalization.ResolveLanguage("zh-Hans", CultureInfo.GetCultureInfo(culture)) == "zh-Hans", "Explicit selection overrides system.");
}
Check(AppLocalization.ResolveLanguage("invalid", CultureInfo.InvariantCulture) == "en", "Unknown preference falls back safely.");

var temporary = Path.Combine(Path.GetTempPath(), "devduo-localization-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var path = Path.Combine(temporary, "preferences.json");
    var localized = new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN"));
    Check(localized.Preference == "system" && localized.Language == "zh-Hans", "Fresh preferences use system culture.");
    Check(localized.Select("en"), "Preference write must succeed.");
    Check(new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN")).Language == "en", "Explicit preference survives restart.");
    Check(localized.Select("system"), "System preference write must succeed.");
    Check(new AppLocalization(path, CultureInfo.GetCultureInfo("en-US")).Language == "en", "System preference resolves at the next launch.");
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"unsupported\"}");
    Check(new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN")).Language == "zh-Hans", "Unsupported saved language uses system default.");
    foreach (var invalid in new[]
    {
        "null", "[]", "[1]", "42", "true", "\"en\"", "{}",
        "{\"language\":\"en\"}", "{\"schemaVersion\":2,\"language\":\"en\"}",
        "{\"schemaVersion\":\"1\",\"language\":\"en\"}",
        "{\"schemaVersion\":1.5,\"language\":\"en\"}",
        "{\"schemaVersion\":null,\"language\":\"en\"}",
        "{\"schemaVersion\":1}", "{\"schemaVersion\":1,\"language\":null}",
        "{\"schemaVersion\":1,\"language\":[\"en\"]}"
    })
    {
        File.WriteAllText(path, invalid);
        var unsupported = new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN"));
        Check(unsupported.PreferenceErrorOnRead && unsupported.Preference == "system" && unsupported.Language == "zh-Hans",
            "Invalid root/schema/language must recover using system culture: " + invalid);
        Check(unsupported.PreferenceError == chinese["Error.Preferences"], "Invalid preference structure has a localized explanation.");
    }
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"futureOptionalField\":true}");
    Check(new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN")).Language == "en", "Version 1 accepts unrelated optional fields.");
    File.WriteAllText(path, "corrupt");
    var corrupt = new AppLocalization(path, CultureInfo.GetCultureInfo("en-US"));
    Check(corrupt.PreferenceErrorOnRead && corrupt.Language == "en", "Corrupt preference recovers with a readable failure.");
    File.WriteAllText(path, "{}");
    var cannotWrite = new AppLocalization(Path.Combine(path, "blocked.json"), CultureInfo.GetCultureInfo("en-US"));
    Check(!cannotWrite.Select("zh-Hans") && cannotWrite.Language == "zh-Hans" && cannotWrite.PreferenceError is not null, "A failed write must preserve session choice and report failure.");
    Check(localized.NativeStrings().Keys.All(key => key.StartsWith("Shell.")), "Native payload contains only shell presentation strings.");
    Check(localized.FixtureStrings().Keys.All(key => key.StartsWith("Fixture.")), "Fixture payload contains only fixture strings.");
}
finally { Directory.Delete(temporary, true); }

// Host settings share the language preference file. Body angle is never persisted.
var hostFolder = Path.Combine(Path.GetTempPath(), "devduo-host-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(hostFolder);
try
{
    var path = Path.Combine(hostFolder, "preferences.json");
    var fresh = new PreferenceStore(path);
    Check(fresh.Host.Theme == "system" && fresh.Host.ContainerMode == "safari" && fresh.Host.Fold == "closed" &&
        fresh.Host.PreviewScale == "native" && fresh.Host.DeviceOutline && fresh.Host.ShowTabStrip &&
        fresh.Host.TintAlpha is null && fresh.Host.LastUrl is null && fresh.InvalidHostFields.Count == 0,
        "Fresh host settings use the documented Safari, Closed, 1:1 defaults.");
    var chosen = new HostSettings
    {
        Theme = "dark", ContainerMode = "app", Fold = "open", PreviewScale = "fit", DeviceOutline = false,
        ShowTabStrip = false, TintAlpha = .5, CornerRadius = 12, SettingsPanelExpanded = false,
        MaterialExpanded = true, DiagnosticsExpanded = true, LastUrl = "https://example.test/a?b=1#c"
    };
    Check(fresh.SaveHost(chosen), "Host preference write must succeed.");
    Check(new AppLocalization(path, CultureInfo.GetCultureInfo("en-US")).Select("zh-Hans"), "Language write must succeed.");
    var reloaded = new PreferenceStore(path);
    Check(reloaded.Language == "zh-Hans", "Host and language preferences share one file.");
    Check(JsonSerializer.Serialize(reloaded.Host) == JsonSerializer.Serialize(chosen), "A language write preserves host settings.");
    Check(reloaded.SaveHost(new HostSettings()) && new PreferenceStore(path).Language == "zh-Hans", "A host write preserves the language.");
    using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
    {
        Check(saved.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "Host settings keep preference schema version 1.");
        Check(!saved.RootElement.GetProperty("host").EnumerateObject().Any(property =>
                property.Name.Contains("rotation", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("angle", StringComparison.OrdinalIgnoreCase)),
            "Body angle is not persisted, preserving the Closed 180° rotation-history qualification.");
    }
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"theme\":\"neon\",\"containerMode\":\"app\"," +
        "\"previewScale\":3,\"deviceOutline\":\"yes\",\"tintAlpha\":2,\"shadowOpacity\":0.4,\"cornerRadius\":-1," +
        "\"lastUrl\":\"file:///etc/hosts\",\"fold\":\"\",\"futureField\":true}}");
    var partial = new PreferenceStore(path);
    Check(partial.Host.ContainerMode == "app" && partial.Host.ShadowOpacity == .4, "Valid host fields survive invalid neighbors.");
    Check(partial.Host.Theme == "system" && partial.Host.PreviewScale == "native" && partial.Host.DeviceOutline &&
        partial.Host.TintAlpha is null && partial.Host.CornerRadius is null && partial.Host.LastUrl is null && partial.Host.Fold == "closed",
        "Invalid host fields fall back individually.");
    Check(partial.InvalidHostFields.Order().SequenceEqual(new[] { "cornerRadius", "deviceOutline", "fold", "lastUrl", "previewScale", "theme", "tintAlpha" }),
        "Invalid host fields are reported by name.");
    var hostTolerant = new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN"));
    Check(hostTolerant.Language == "en" && !hostTolerant.PreferenceErrorOnRead, "Invalid host fields do not invalidate the language preference.");
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":[]}");
    Check(new PreferenceStore(path).InvalidHostFields.SequenceEqual(["host"]), "A non-object host value is rejected.");
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\"}");
    var languageOnly = new PreferenceStore(path);
    Check(languageOnly.InvalidHostFields.Count == 0 && languageOnly.Host.ContainerMode == "safari", "Earlier language-only preference files remain valid.");
    File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"glassStrength\":0.5,\"shadowOpacity\":0.6,\"cornerRadius\":40}}");
    var limits = new PreferenceStore(path);
    Check(limits.Host.GlassStrength == .5 && limits.Host.ShadowOpacity is null && limits.Host.CornerRadius == 40,
        "Material values are limited to the host slider ranges.");
}
finally { Directory.Delete(hostFolder, true); }

// Calibration labels apply only to the local measurement page and keep the established query format.
var labels = new CalibrationLabels("open", "macOS-WKWebView", "app-webview", "body-clockwise-90-scene-0-1to1");
foreach (var developerUrl in new[] { "https://example.test/app?view=1#section", "http://127.0.0.1:3000/?x=1", "http://127.0.0.1:8765/docs/", "https://127.0.0.1:8765/" })
    Check(CalibrationUrl.ApplyLabels(new Uri(developerUrl), labels) == new Uri(developerUrl), "Developer URLs are not labelled: " + developerUrl);
var legacy = CalibrationUrl.ApplyLabels(new Uri(CalibrationUrl.DefaultAddress),
    new CalibrationLabels("closed", "macOS-WKWebView", "safari-shell-approx", "body-clockwise-0-scene-0-1to1"));
Check(legacy.AbsoluteUri == "http://127.0.0.1:8765/?device=iPhone%20Duo&pose=closed&browser=macOS-WKWebView&state=initial&displayMode=safari-shell-approx&notes=body-clockwise-0-scene-0-1to1",
    "Calibration labels keep the established query format.");
var labelled = CalibrationUrl.ApplyLabels(new Uri("http://127.0.0.1:8765/?keep=1#notes"), labels);
Check(labelled.Fragment == "#notes" && labelled.Query.StartsWith("?keep=1&device=iPhone%20Duo&pose=open&", StringComparison.Ordinal),
    "Labels follow retained parameters and precede the fragment.");
var relabelled = CalibrationUrl.ApplyLabels(labelled, labels with { Pose = "closed" });
Check(Regex.Matches(relabelled.Query, "(^|[?&])pose=").Count == 1 && relabelled.Query.Contains("pose=closed"), "Relabelling replaces labels without duplicates.");
Check(CalibrationUrl.RemoveLabels(relabelled).AbsoluteUri == "http://127.0.0.1:8765/?keep=1#notes", "Removing labels restores the developer page.");
Check(CalibrationUrl.ApplyLabels(new Uri("http://localhost:8765/index.html"), labels).Query.Contains("displayMode=app-webview"), "The loopback receiver page is labelled.");
foreach (var (input, expected) in new[]
{
    ("localhost:5173/app", "http://localhost:5173/app"), ("127.0.0.1:8765", "http://127.0.0.1:8765/"),
    ("192.168.1.20:8080", "http://192.168.1.20:8080/"), ("[::1]:3000", "http://[::1]:3000/"),
    ("example.com/path?q=1#top", "https://example.com/path?q=1#top"), ("HTTPS://Example.com", "https://example.com/"),
    (" http://127.0.0.1:8765/?a=1 ", "http://127.0.0.1:8765/?a=1")
})
    Check(CalibrationUrl.TryNormalize(input, out var normalized) && normalized.AbsoluteUri == expected, "Address normalization: " + input);
foreach (var invalid in new[] { "", "   ", "file:///etc/hosts", "javascript:alert(1)", "mailto:x@example.com", "ftp://example.com", "about:blank", "http://", "two words.com" })
    Check(!CalibrationUrl.TryNormalize(invalid, out _), "Rejected address: " + invalid);

// Native navigation payloads.
var navigation = NavigationState.FromJson("{\"url\":\"http://127.0.0.1:8765/\",\"title\":\"T\",\"isLoading\":true,\"progress\":1.5," +
    "\"canGoBack\":true,\"canGoForward\":false,\"error\":{\"code\":\"NSURLErrorDomain:-1004\",\"message\":\"Could not connect\"}}");
Check(navigation.IsLoading && navigation.Progress == 1 && navigation.CanGoBack && !navigation.CanGoForward &&
    navigation.Error == new NavigationError("NSURLErrorDomain:-1004", "Could not connect"), "Navigation payload fields are parsed and bounded.");
Check(NavigationState.FromJson("not json") == NavigationState.Empty && NavigationState.FromJson(null) == NavigationState.Empty, "Malformed navigation payloads are ignored.");
Check(NavigationState.FromJson("{\"progress\":null}").Progress is null, "An engine without progress fractions reports indeterminate loading.");
Check(NavigationState.FromJson("{\"url\":\"http://devduo.local/\"}").IsLocalFixture && NavigationState.FromJson("{\"url\":\"about:blank\"}").IsLocalFixture &&
    !NavigationState.FromJson("{\"url\":\"https://example.test/\"}").IsLocalFixture, "The color laboratory address is recognized.");

var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "color-scroll.html"));
foreach (Match match in Regex.Matches(fixture, "Fixture\\.[A-Za-z]+"))
    Check(english.ContainsKey(match.Value) && chinese.ContainsKey(match.Value), "Missing fixture key: " + match.Value);
Check(fixture.Contains("__DEVDUO_LANGUAGE__") && fixture.Contains("__DEVDUO_STRINGS__"), "Fixture bootstrap must receive selected language.");

var scenes = SafariScene.Load();
Check(scenes.Length == 8, "Retain all eight body/fold mappings.");
Check(scenes.Sum(scene => scene.States.EnumerateObject().Count() + scene.SingleStates.EnumerateObject().Count()) == 24,
    "Retain all 24 Safari initial/scrolled states, including Open single-tab variants.");
var sceneSnapshot = JsonSerializer.Serialize(scenes);
var profiles = DeviceProfile.Load();
foreach (var profile in profiles.Where(profile => profile.Id is "closed" or "open"))
{
    var key = "Device." + profile.Id;
    Check(english[key] == profile.Label, "English device identity must match the measured profile label: " + profile.Id);
    var measuredNumbers = Regex.Matches(profile.Label, @"\d+").Select(match => match.Value).ToArray();
    foreach (var catalog in new[] { english, chinese })
    {
        var translatedNumbers = Regex.Matches(catalog[key], @"\d+").Select(match => match.Value);
        Check(measuredNumbers.SequenceEqual(translatedNumbers), "Translation must retain the measured display number: " + profile.Id);
    }
}
var geometrySnapshot = JsonSerializer.Serialize(profiles);
var options = SafariContainerOptions.Load();
options.ReferenceScene = scenes[0];
var baseline = options.ToJson();
foreach (var language in AppLocalization.SupportedLanguages)
{
    var copy = options.Copy();
    copy.UiLanguage = language;
    copy.UiStrings = AppLocalization.Catalog(language).Where(pair => pair.Key.StartsWith("Shell.")).ToDictionary();
    using var document = JsonDocument.Parse(copy.ToJson());
    using var original = JsonDocument.Parse(baseline);
    foreach (var property in original.RootElement.EnumerateObject().Where(property => property.Name is not "uiLanguage" and not "uiStrings"))
        Check(property.Value.GetRawText() == document.RootElement.GetProperty(property.Name).GetRawText(), "Locale changed measured option " + property.Name);
}
Check(JsonSerializer.Serialize(scenes) == sceneSnapshot, "Locale presentation must preserve loaded scenes.");
Check(JsonSerializer.Serialize(DeviceProfile.Load()) == geometrySnapshot, "Locale presentation must preserve geometry data.");
Console.WriteLine($"Localization checks passed: {assertions} assertions; {english.Count} resource keys per language; {scenes.Length} scene mappings.");
