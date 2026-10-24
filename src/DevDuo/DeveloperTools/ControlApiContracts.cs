using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DevDuo;

// API values are canonical identifiers, independent of the application's display language.
public sealed record ControlSettings
{
    public string Fold { get; init; } = "closed";
    public int BodyAngle { get; init; }
    public string ContainerMode { get; init; } = "safari";
    public bool ShowTabStrip { get; init; } = true;
    public string InteractionMode { get; init; } = "touch";
    public string PreviewScale { get; init; } = "native";
    public bool DeviceOutline { get; init; } = true;
    public string Theme { get; init; } = "system";
    public string Language { get; init; } = "system";
    public double? TintAlpha { get; init; }
    public double? GlassStrength { get; init; }
    public double? ShadowOpacity { get; init; }
    public double? CornerRadius { get; init; }

    public static ControlSettings Merge(ControlSettings current, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Settings must be a JSON object.");
        var merged = JsonSerializer.SerializeToNode(current, ControlApiJson.Options)!.AsObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in patch.EnumerateObject())
        {
            if (!seen.Add(field.Name) || !merged.ContainsKey(field.Name))
                throw new ArgumentException("Unknown or duplicate setting: " + field.Name);
            if (field.Value.ValueKind == JsonValueKind.Null && field.Name is not ("tintAlpha" or "glassStrength" or "shadowOpacity" or "cornerRadius"))
                throw new ArgumentException("Setting cannot be null: " + field.Name);
            merged[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        }
        var result = merged.Deserialize<ControlSettings>(ControlApiJson.Options)!;
        if (result.Fold is not ("closed" or "open") || result.BodyAngle is not (0 or 90 or 180 or 270) ||
            result.ContainerMode is not ("safari" or "app") || result.PreviewScale is not ("native" or "fit") ||
            result.InteractionMode is not ("touch" or "mouse") ||
            result.Theme is not ("system" or "light" or "dark") || result.Language is not ("system" or "en" or "zh-Hans"))
            throw new ArgumentException("Invalid fold, bodyAngle, containerMode, interactionMode, previewScale, theme or language.");
        ValidateRange(result.TintAlpha, 1, "tintAlpha");
        ValidateRange(result.GlassStrength, 1, "glassStrength");
        ValidateRange(result.ShadowOpacity, .5, "shadowOpacity");
        ValidateRange(result.CornerRadius, 40, "cornerRadius");
        return result;
    }

    private static void ValidateRange(double? value, double maximum, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0 || number > maximum))
            throw new ArgumentException($"{name} must be null or between 0 and {maximum}.");
    }
}

public sealed record ControlNavigationRequest(string Action, string? Url = null);
public sealed record ControlInspectorRequest
{
    public required bool Visible { get; init; }
}
public sealed record ControlProxyRequest(string Mode, string? Endpoint = null);
public sealed record ControlApiRequest(string Method, string Path, JsonElement Body);

internal static class ControlApiJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
}

public sealed class ControlApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
