using System.Text.Json;
using System.Text.Json.Nodes;
namespace DevDuo;

// Host presentation preferences. Body angle is deliberately absent: every launch starts at the
// documented body 0° scene so the Closed 180° rotation-history qualification is never skipped.
public sealed class HostSettings
{
    public const double TintMaximum = 1, GlassMaximum = 1, ShadowMaximum = .5, RadiusMaximum = 40;
    public static readonly string[] Themes = ["system", "light", "dark"];
    public static readonly string[] ContainerModes = ["safari", "app"];
    public static readonly string[] PreviewScales = ["native", "fit"];

    public string Theme { get; set; } = "system";
    public string ContainerMode { get; set; } = "safari";
    // A device-profile id; MainWindow falls back to the first profile when it is unknown.
    public string Fold { get; set; } = "closed";
    public string PreviewScale { get; set; } = "native";
    public bool DeviceOutline { get; set; } = true;
    public bool ShowTabStrip { get; set; } = true;
    // Null keeps the material defaults from config/safari-shell.json.
    public double? TintAlpha { get; set; }
    public double? GlassStrength { get; set; }
    public double? ShadowOpacity { get; set; }
    public double? CornerRadius { get; set; }
    public bool SettingsPanelExpanded { get; set; } = true;
    public bool MaterialExpanded { get; set; }
    public bool DiagnosticsExpanded { get; set; }
    public string? LastUrl { get; set; }

    public HostSettings Copy() => (HostSettings)MemberwiseClone();
}

// Owns LocalApplicationData/DevDuo/preferences.json: {schemaVersion: 1, language, host?}.
// Version 1 readers ignore unknown fields, so the optional host object stays compatible.
public sealed class PreferenceStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "preferences.json");
    public static PreferenceStore Default { get; } = new(DefaultPath);

    public string Path { get; }
    public string Language { get; private set; } = AppLocalization.SystemLanguage;
    public HostSettings Host { get; private set; } = new();
    public string? ReadError { get; }
    public string? ReadErrorResourceKey { get; }
    public IReadOnlyList<string> InvalidHostFields { get; } = [];
    public string? WriteError { get; private set; }

    public PreferenceStore(string path)
    {
        Path = path;
        try
        {
            if (!File.Exists(path))
                return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
                !root.TryGetProperty("language", out var language) || language.ValueKind != JsonValueKind.String)
            {
                ReadErrorResourceKey = "Error.Preferences";
                return;
            }
            Language = language.GetString()!;
            var invalid = new List<string>();
            if (root.TryGetProperty("host", out var host))
                Host = ReadHost(host, invalid);
            InvalidHostFields = invalid;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            ReadError = error.Message;
        }
    }

    public bool SaveLanguage(string language)
    {
        Language = language;
        return Write();
    }

    public bool SaveHost(HostSettings host)
    {
        Host = host.Copy();
        return Write();
    }

    // Each field is validated independently; an invalid value falls back to its default and is reported.
    private static HostSettings ReadHost(JsonElement host, List<string> invalid)
    {
        var settings = new HostSettings();
        if (host.ValueKind != JsonValueKind.Object)
        {
            invalid.Add("host");
            return settings;
        }
        string? Choice(string name, string[] allowed, string fallback)
        {
            if (!host.TryGetProperty(name, out var value))
                return fallback;
            if (value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString(), StringComparer.Ordinal))
                return value.GetString();
            invalid.Add(name);
            return fallback;
        }
        bool Flag(string name, bool fallback)
        {
            if (!host.TryGetProperty(name, out var value))
                return fallback;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
            invalid.Add(name);
            return fallback;
        }
        double? Number(string name, double maximum)
        {
            if (!host.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
                double.IsFinite(number) && number >= 0 && number <= maximum)
                return number;
            invalid.Add(name);
            return null;
        }
        settings.Theme = Choice("theme", HostSettings.Themes, settings.Theme)!;
        settings.ContainerMode = Choice("containerMode", HostSettings.ContainerModes, settings.ContainerMode)!;
        settings.PreviewScale = Choice("previewScale", HostSettings.PreviewScales, settings.PreviewScale)!;
        if (host.TryGetProperty("fold", out var fold))
        {
            if (fold.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(fold.GetString()))
                settings.Fold = fold.GetString()!;
            else
                invalid.Add("fold");
        }
        settings.DeviceOutline = Flag("deviceOutline", settings.DeviceOutline);
        settings.ShowTabStrip = Flag("showTabStrip", settings.ShowTabStrip);
        settings.SettingsPanelExpanded = Flag("settingsPanelExpanded", settings.SettingsPanelExpanded);
        settings.MaterialExpanded = Flag("materialExpanded", settings.MaterialExpanded);
        settings.DiagnosticsExpanded = Flag("diagnosticsExpanded", settings.DiagnosticsExpanded);
        settings.TintAlpha = Number("tintAlpha", HostSettings.TintMaximum);
        settings.GlassStrength = Number("glassStrength", HostSettings.GlassMaximum);
        settings.ShadowOpacity = Number("shadowOpacity", HostSettings.ShadowMaximum);
        settings.CornerRadius = Number("cornerRadius", HostSettings.RadiusMaximum);
        if (host.TryGetProperty("lastUrl", out var url) && url.ValueKind != JsonValueKind.Null)
        {
            if (url.ValueKind == JsonValueKind.String && CalibrationUrl.TryNormalize(url.GetString(), out var parsed))
                settings.LastUrl = parsed.AbsoluteUri;
            else
                invalid.Add("lastUrl");
        }
        return settings;
    }

    private bool Write()
    {
        WriteError = null;
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var host = new JsonObject
            {
                ["theme"] = Host.Theme,
                ["containerMode"] = Host.ContainerMode,
                ["fold"] = Host.Fold,
                ["previewScale"] = Host.PreviewScale,
                ["deviceOutline"] = Host.DeviceOutline,
                ["showTabStrip"] = Host.ShowTabStrip,
                ["tintAlpha"] = Host.TintAlpha,
                ["glassStrength"] = Host.GlassStrength,
                ["shadowOpacity"] = Host.ShadowOpacity,
                ["cornerRadius"] = Host.CornerRadius,
                ["settingsPanelExpanded"] = Host.SettingsPanelExpanded,
                ["materialExpanded"] = Host.MaterialExpanded,
                ["diagnosticsExpanded"] = Host.DiagnosticsExpanded,
                ["lastUrl"] = Host.LastUrl
            };
            var root = new JsonObject { ["schemaVersion"] = 1, ["language"] = Language, ["host"] = host };
            File.WriteAllText(temporary, root.ToJsonString(WriteOptions));
            File.Move(temporary, Path, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            WriteError = error.Message;
            return false;
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
}
