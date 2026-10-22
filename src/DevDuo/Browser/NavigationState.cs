using System.Text.Json;
namespace DevDuo;

public sealed record NavigationError(string Code, string Message);

// Host-facing navigation state reported by the active native WebView. Progress is null
// when the engine cannot supply a loading fraction.
public sealed record NavigationState(
    string? Url,
    string? Title,
    bool IsLoading,
    double? Progress,
    bool CanGoBack,
    bool CanGoForward,
    NavigationError? Error)
{
    public static NavigationState Empty { get; } = new(null, null, false, null, false, false, null);

    public BuiltinPageKind BuiltinPage => BuiltinPages.FromAddress(Url);
    public bool IsLocalFixture => BuiltinPage == BuiltinPageKind.ColorLaboratory;

    // Parses the bridge payload: {url,title,isLoading,progress,canGoBack,canGoForward,error:{code,message}|null}.
    public static NavigationState FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;
            string? Text(JsonElement parent, string name) =>
                parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            bool Flag(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
            double? progress = root.TryGetProperty("progress", out var fraction) && fraction.ValueKind == JsonValueKind.Number
                ? Math.Clamp(fraction.GetDouble(), 0, 1)
                : null;
            NavigationError? error = null;
            if (root.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.Object)
                error = new NavigationError(Text(failure, "code") ?? "unknown", Text(failure, "message") ?? "");
            return new NavigationState(Text(root, "url"), Text(root, "title"), Flag("isLoading"), progress,
                Flag("canGoBack"), Flag("canGoForward"), error);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
