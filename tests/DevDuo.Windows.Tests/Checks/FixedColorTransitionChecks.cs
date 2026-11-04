using System.Reflection;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckFixedColorTransitions()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Fixed transition runtime");
        Select(Settings.Fold, 0);
        Select(Settings.Scale, 1);
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-edge-native.html"));
        await WaitForScript("mode", value => value == "nested", "Nested translucent footer");
        bool Black(JsonElement state) => FixedExtension(state).GetProperty("bottomRgb") is var rgb &&
            rgb.ValueKind == JsonValueKind.Array && rgb.EnumerateArray().All(channel => channel.GetInt32() == 0);
        await Wait(Black, "Initial accepted footer color");
        var browser = Web.WindowsPanel.Browser;
        var geometry = ((int Width, int Height, double Scale, int Top, int Right, int Bottom, int Left, int Min, int Max))
            typeof(DevDuo.WindowsWebKitView).GetField("geometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
        var identity = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,scrollX,scrollY,innerWidth,innerHeight])");
        for (var index = 0; index < 8; index++)
        {
            // Observe synchronously, before a new IPC color result can arrive.
            // The previous implementation removed the fill in this exact gap.
            browser.SetGeometry(geometry.Width, geometry.Height, geometry.Scale, geometry.Top, geometry.Right,
                geometry.Bottom - 18, geometry.Left, geometry.Min, geometry.Max);
            Check(Black(State()), "Accepted fill survives compact-inset invalidation before resampling");
            browser.SetGeometry(geometry.Width, geometry.Height, geometry.Scale, geometry.Top, geometry.Right,
                geometry.Bottom, geometry.Left, geometry.Min, geometry.Max);
            Check(Black(State()), "Accepted fill survives restored-inset invalidation before resampling");
        }
        await Wait(state => Black(state) && FixedExtension(state).GetProperty("status").GetString() == "accepted", "Fresh geometry color accepted");
        Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,scrollX,scrollY,innerWidth,innerHeight])") == identity,
            "Restoring native inset geometry preserves document, scroll and real viewport");
        var observations = new List<JsonElement>();
        using var monitor = new System.Windows.Forms.Timer { Interval = 10 };
        monitor.Tick += (_, _) => observations.Add(State().Clone());
        await Web.EvaluateResultAsync("scrollTo(0,400);'ready'");
        await Task.Delay(500);
        monitor.Start();
        try
        {
            for (var index = 0; index < 8; index++)
            {
                await ScrollNativeUntil(index % 2 == 0);
                // Capture this process's composed window; the solid extension
                // color does not depend on activation or material appearance.
                var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                var owner = PresentationGetAncestor(Web.WindowsPanel.Parent!.Handle, 2);
                var frame = await Task.Run(() => DevDuo.WindowsWindowCapture.Capture(owner,
                    new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                using var stream = new MemoryStream(frame.Png);
                using var bitmap = new Bitmap(stream);
                bitmap.Save(Path.Combine(Program.Output, $"fixed-color-transition-{index:D2}.png"), ImageFormat.Png);
                var pixel = bitmap.GetPixel(12, bitmap.Height - 10);
                Check(Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) <= 3,
                    "Presented bottom-inset pixels stay black across native wheel transitions");
            }
        }
        finally { monitor.Stop(); }
        Save("fixed-color-transition-observations", JsonSerializer.SerializeToElement(observations));
        Check(observations.Count > 0 && observations.All(Black), "Native wheel expansion/compaction never exposes a missing bottom fill");
        Console.WriteLine($"PASS fixed color transitions: {assertions} assertions; synchronous inset invalidation, {observations.Count} native-wheel observations and eight composed-window bottom-inset samples. Full temporal material acceptance remains separate.");
    }
}
