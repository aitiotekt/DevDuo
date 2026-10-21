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
    public static AppLocalization Current { get; } = new();
    public string Preference { get; private set; } = SystemLanguage;
    public string Language { get; private set; } = "en";
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "zh-Hans" ? "zh-CN" : "en-US");
    private string? preferenceError;
    private string? preferenceErrorResourceKey;
    public string? PreferenceError => preferenceErrorResourceKey is { } key ? Text(key) : preferenceError;
    public bool PreferenceErrorOnRead { get; private set; }

    public AppLocalization(string? preferencePath = null, CultureInfo? systemCulture = null)
    {
        this.preferencePath = preferencePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "preferences.json");
        this.systemCulture = systemCulture ?? CultureInfo.CurrentUICulture;
        try
        {
            if (File.Exists(this.preferencePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(this.preferencePath));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("schemaVersion", out var version) &&
                    version.ValueKind == JsonValueKind.Number &&
                    version.TryGetInt32(out var schemaVersion) && schemaVersion == 1 &&
                    root.TryGetProperty("language", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    Preference = NormalizePreference(value.GetString());
                }
                else
                {
                    preferenceErrorResourceKey = "Error.Preferences";
                    PreferenceErrorOnRead = true;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            preferenceError = error.Message;
            PreferenceErrorOnRead = true;
        }
        Language = ResolveLanguage(Preference, this.systemCulture);
    }

    private readonly string preferencePath;
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
        preferenceError = null;
        preferenceErrorResourceKey = null;
        PreferenceErrorOnRead = false;
        var temporary = preferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preferencePath))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                language = Preference
            }));
            File.Move(temporary, preferencePath, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            preferenceError = error.Message;
            return false; // The current session still uses the selected language.
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
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
