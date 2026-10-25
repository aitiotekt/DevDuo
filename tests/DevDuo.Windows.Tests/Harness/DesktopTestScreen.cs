using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal static class DesktopTestScreen
{
    internal static async Task Select(Window window)
    {
        var requested = Environment.GetEnvironmentVariable("DEVDUO_TEST_SCREEN_SCALE");
        if (string.IsNullOrEmpty(requested)) return;
        if (!double.TryParse(requested, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var scale) || scale <= 0)
            throw new InvalidOperationException("DEVDUO_TEST_SCREEN_SCALE must be a positive display scaling factor, such as 1 or 2.");
        await Move(window, scale, "display-selection");
    }

    internal static async Task Move(Window window, double scale, string receipt)
    {
        var matches = window.Screens.All.Where(screen => Math.Abs(screen.Scaling - scale) < 0.001).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected one actual display at scale {scale}; found {matches.Length}.");
        var area = matches[0].WorkingArea;
        window.WindowState = WindowState.Normal;
        window.Position = new PixelPoint(area.X + 32, area.Y + 32);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Math.Abs(window.RenderScaling - scale) >= 0.001 && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        if (Math.Abs(window.RenderScaling - scale) >= 0.001)
            throw new InvalidOperationException("The actual test window did not acquire the requested display DPI.");
        await Task.Delay(250);
        File.WriteAllText(Path.Combine(Program.Output, receipt + ".json"), JsonSerializer.Serialize(new
        {
            requestedScale = scale, actualScale = window.RenderScaling, window.Position,
            screens = window.Screens.All.Select(screen => new { screen.Bounds, screen.WorkingArea, screen.Scaling, screen.IsPrimary })
        }));
    }
}
