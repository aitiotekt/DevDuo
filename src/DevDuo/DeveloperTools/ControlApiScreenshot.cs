using System.Text.Json;

namespace DevDuo;

public sealed record ScreenshotRequest
{
    public string Target { get; init; } = "viewport";
    public string Format { get; init; } = "png";
    public int? Quality { get; init; }

    public static ScreenshotRequest Parse(JsonElement body)
    {
        var request = body.Deserialize<ScreenshotRequest>(ControlApiJson.Options)!;
        if (request.Target is not ("viewport" or "device") || request.Format is not ("png" or "jpeg") ||
            request.Quality is < 0 or > 100 || request.Format == "png" && request.Quality is not null)
            throw new ArgumentException("Use target viewport/device, format png/jpeg and JPEG quality 0–100.");
        return request;
    }

    public static ScreenshotRequest ParseCdp(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Undefined) return new();
        if (parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Screenshot parameters must be an object.");
        var format = "png";
        int? quality = null;
        foreach (var parameter in parameters.EnumerateObject())
        {
            switch (parameter.Name)
            {
                case "format": format = parameter.Value.GetString()!; break;
                case "quality": quality = parameter.Value.GetInt32(); break;
                case "fromSurface":
                    if (!parameter.Value.GetBoolean()) throw new ArgumentException("Only fromSurface:true is supported.");
                    break;
                // Without clip, CDP captures the current viewport even when beyond-viewport capture is permitted.
                case "captureBeyondViewport": case "optimizeForSpeed": _ = parameter.Value.GetBoolean(); break;
                case "clip": throw new ArgumentException("Clip and full-document screenshots are not supported; capture the current viewport without clip.");
                default: throw new ArgumentException("Unsupported screenshot parameter: " + parameter.Name);
            }
        }
        var json = JsonSerializer.SerializeToElement(new { target = "viewport", format, quality }, ControlApiJson.Options);
        return Parse(json);
    }
}

// Transport-neutral encoded image. REST sends the bytes; CDP wraps them as base64.
public sealed record ScreenshotImage(byte[] Data, string Format, int Width, int Height, string Target, double PixelScale)
{
    public string ContentType => Format == "jpeg" ? "image/jpeg" : "image/png";
}
