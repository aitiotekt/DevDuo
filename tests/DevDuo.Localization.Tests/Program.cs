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
Check(AppLocalization.Lookup("xx", "Control.Open") == english["Control.Open"], "Unknown catalog must fall back to English.");
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
