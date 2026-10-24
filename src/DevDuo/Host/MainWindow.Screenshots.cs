using Avalonia;

namespace DevDuo;

public sealed partial class MainWindow
{
    private async Task<object> CaptureControlScreenshotAsync(ScreenshotRequest request, CancellationToken token)
    {
        var current = RequireControlView();
        if (request.Target == "viewport") return await current.CaptureScreenshotAsync(request, cancellationToken: token);
        if (WindowState == Avalonia.Controls.WindowState.Minimized || !IsVisible ||
            shell.TranslatePoint(default, this) is not { } origin || shell.TranslatePoint(default, nativeLayer) is not { } previewOrigin)
            throw new ControlApiException(409, "The device must be visible for a composed screenshot.");
        var visible = new Rect(default, nativeLayer.Bounds.Size);
        var device = new Rect(previewOrigin, shell.Bounds.Size);
        if (!visible.Contains(device))
            throw new ControlApiException(409, "The complete device must fit in the preview. Select Fit preview or enlarge the window; capture does not resize it.");
        var crop = new Rect(origin, shell.Bounds.Size);
        var a = this.PointToScreen(crop.TopLeft);
        var b = this.PointToScreen(crop.BottomRight);
        var pixels = new PixelRect(a.X, a.Y, b.X - a.X, b.Y - a.Y);
        var result = await current.CaptureScreenshotAsync(request, crop, pixels, TryGetPlatformHandle()?.Handle ?? 0, token);
        if (!ReferenceEquals(web, current) || shell.TranslatePoint(default, this) != origin || shell.Bounds.Size != crop.Size ||
            this.PointToScreen(crop.TopLeft) != a || this.PointToScreen(crop.BottomRight) != b)
            throw new ControlApiException(409, "The device moved during capture; retry after layout settles.");
        return result;
    }
}
