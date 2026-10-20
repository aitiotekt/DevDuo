using System.Globalization;
using System.Text.Json;

namespace DevDuo;

public sealed class AppMessageException(string resourceKey) : Exception(resourceKey)
{
    public string ResourceKey { get; } = resourceKey;
    public override string Message => AppLocalization.Current.Text(ResourceKey);
}

// Host presentation only. Locale selection never modifies measured profiles or telemetry values.
public sealed class AppLocalization
{
    public const string SystemLanguage = "system";
    public static readonly string[] SupportedLanguages = ["en", "zh-Hans"];
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs =
        SupportedLanguages.ToDictionary(language => language, LoadCatalog);
    public static AppLocalization Current { get; } = new(PreferenceStore.Default);
    public string Preference { get; private set; } = SystemLanguage;
    public string Language { get; private set; } = "en";
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "zh-Hans" ? "zh-CN" : "en-US");
    private string? preferenceError;
    private string? preferenceErrorResourceKey;
    public string? PreferenceError => preferenceErrorResourceKey is { } key ? Text(key) : preferenceError;
    public bool PreferenceErrorOnRead { get; private set; }

    public AppLocalization(string? preferencePath = null, CultureInfo? systemCulture = null)
        : this(new PreferenceStore(preferencePath ?? PreferenceStore.DefaultPath), systemCulture) { }

    // The store owns the shared preferences file; host settings written elsewhere are preserved.
    public AppLocalization(PreferenceStore store, CultureInfo? systemCulture = null)
    {
        this.store = store;
        this.systemCulture = systemCulture ?? CultureInfo.CurrentUICulture;
        preferenceError = store.ReadError;
        preferenceErrorResourceKey = store.ReadErrorResourceKey;
        PreferenceErrorOnRead = preferenceError is not null || preferenceErrorResourceKey is not null;
        Preference = NormalizePreference(store.Language);
        Language = ResolveLanguage(Preference, this.systemCulture);
    }

    private readonly PreferenceStore store;
    private readonly CultureInfo systemCulture;
    public static string NormalizePreference(string? language) =>
        SupportedLanguages.Contains(language, StringComparer.Ordinal) ? language! : SystemLanguage;
    public static string ResolveLanguage(string? preference, CultureInfo systemCulture)
    {
        if (NormalizePreference(preference) != SystemLanguage)
            return preference!;
        var name = systemCulture.Name;
        // Traditional Chinese is not silently presented as Simplified Chinese.
        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("Hant", StringComparison.OrdinalIgnoreCase) &&
            !new[] { "zh-TW", "zh-HK", "zh-MO" }.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? "zh-Hans" : "en";
    }

    public bool Select(string? language)
    {
        Preference = NormalizePreference(language);
        Language = ResolveLanguage(Preference, systemCulture);
        preferenceErrorResourceKey = null;
        PreferenceErrorOnRead = false;
        // The current session still uses the selected language when the write fails.
        preferenceError = store.SaveLanguage(Preference) ? null : store.WriteError;
        return preferenceError is null;
    }

    public string Text(string key) => Lookup(Language, key);
    public string Format(string key, params object?[] values) => string.Format(Culture, Text(key), values);
    public static string Lookup(string language, string key)
    {
        if (Catalogs.TryGetValue(language, out var selected) && selected.TryGetValue(key, out var value))
            return value;
        return Catalogs["en"].TryGetValue(key, out var fallback) ? fallback : key;
    }
    public static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[language];
    public Dictionary<string, string> NativeStrings() => Catalogs["en"].Keys
        .Where(key => key.StartsWith("Shell.", StringComparison.Ordinal))
        .ToDictionary(key => key, Text);
    public Dictionary<string, string> FixtureStrings() => Catalogs["en"].Keys
        .Where(key => key.StartsWith("Fixture.", StringComparison.Ordinal))
        .ToDictionary(key => key, Text);
    private static Dictionary<string, string> LoadCatalog(string language)
    {
        using var stream = typeof(AppLocalization).Assembly.GetManifestResourceStream("DevDuo.Localization." + language + ".json")
            ?? throw new InvalidOperationException("Missing application language catalog: " + language);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Invalid application language catalog: " + language);
    }
}
