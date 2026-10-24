using SkiaSharp;

namespace DevDuo;

internal sealed record NativeScreenshot(byte[] Png, int Width, int Height, double PixelScale);

internal static class ScreenshotEncoding
{
    internal static ScreenshotImage Encode(NativeScreenshot source, ScreenshotRequest request)
    {
        if (source.Width < 1 || source.Height < 1 || (long)source.Width * source.Height > 32_000_000)
            throw new ControlApiException(409, "Screenshot dimensions are unavailable or exceed 32 million pixels.");
        using var input = SKImage.FromEncodedData(source.Png)
            ?? throw new ControlApiException(409, "The native screenshot could not be decoded.");
        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
        surface.Canvas.Clear(request.Format == "jpeg" ? SKColors.White : SKColors.Transparent);
        using var sampling = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(input, new SKRect(0, 0, source.Width, source.Height), SKSamplingOptions.Default, sampling);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(request.Format == "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, request.Quality ?? 80);
        return new ScreenshotImage(encoded.ToArray(), request.Format, source.Width, source.Height, request.Target, source.PixelScale);
    }
}
