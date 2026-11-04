using System.Text.Json;
using Avalonia.Interactivity;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckChromeMotion()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Motion runtime");
        Select(Settings.Scale, 1);
        foreach (var pose in new[] { (Fold: 0, Angle: 0), (Fold: 1, Angle: 90), (Fold: 1, Angle: 270) })
        {
            Select(Settings.Fold, pose.Fold);
            while (State().GetProperty("bodyRotationDegrees").GetInt32() != pose.Angle)
            {
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
            }
            await Wait(state => Text(state, "initializationState", "ready"), "Motion pose");
            Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-edge-native.html"));
            await WaitForScript("mode", value => value == "nested", "Motion fixture");
            await Task.Delay(400);
            await Web.EvaluateResultAsync("scrollTo(0,400);'positioned'");
            await Task.Delay(250);
            var before = State();
            var samples = new List<JsonElement>();
            Task<DevDuo.NativeScreenshot>? intermediate = null;
            using var monitor = new System.Windows.Forms.Timer { Interval = 10 };
            monitor.Tick += (_, _) =>
            {
                var state = State();
                samples.Add(state.Clone());
                if (intermediate is null && state.GetProperty("chromeAnimation").GetProperty("running").GetBoolean())
                {
                    var bounds = Web.WindowsPanel.Browser.PresentationBounds!(Web.WindowsPanel.Browser.ClientRectangle);
                    var owner = PresentationGetAncestor(Web.WindowsPanel.Parent!.Handle, 2);
                    intermediate = Task.Run(() => DevDuo.WindowsWindowCapture.Capture(owner,
                        new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                }
            };
            monitor.Start();
            try { await ScrollNativeUntil(true); }
            finally { monitor.Stop(); }
            await Wait(state => !state.GetProperty("chromeAnimation").GetProperty("running").GetBoolean(), "Motion settles");
            samples.Add(State());
            if (intermediate is not null)
                File.WriteAllBytes(Path.Combine(Program.Output, $"chrome-motion-{pose.Fold}-{pose.Angle}-intermediate.png"), (await intermediate).Png);
            Save($"chrome-motion-{pose.Fold}-{pose.Angle}", JsonSerializer.SerializeToElement(samples));
            var animated = samples.Where(state => state.GetProperty("chromeAnimation").GetProperty("running").GetBoolean()).ToArray();
            if (State().GetProperty("chromeAnimation").GetProperty("clientAnimationsEnabled").GetBoolean())
            {
                Check(animated.Length > 1, "Compaction has observable intermediate presentation frames");
                Check(animated.Any(state => state.GetProperty("chromeAnimation").GetProperty("fraction").GetDouble() is > .05 and < .95),
                    "Cubic interpolation traverses intermediate sizes");
                Check(animated.Any(state => state.GetProperty("chromeElements").EnumerateArray().Any(item =>
                    item.GetProperty("compactMaterial").GetDouble() is > .05 and < .95)),
                    "Address material role interpolates with geometry rather than switching at the first frame");
                var geometryGenerations = animated.Select(state => FixedExtension(state).GetProperty("geometryGeneration").GetInt64()).Distinct().Count();
                Check(geometryGenerations == 1, "Presentation frames do not repeatedly change the real WebKit viewport");
            }
            var final = State();
            var structureBefore = before.GetProperty("webKit").GetProperty("composition").GetProperty("updates").GetProperty("structure").GetUInt64();
            var structureAfter = final.GetProperty("webKit").GetProperty("composition").GetProperty("updates").GetProperty("structure").GetUInt64();
            Check(structureAfter - structureBefore <= 3, "Animation retains native visual structure between collection changes");
            Check(final.GetProperty("chromeAnimation").GetProperty("fraction").GetDouble() == 1, "Settled geometry is the exact target endpoint");
            await ScrollNativeUntil(false);
            await Wait(state => !state.GetProperty("chromeAnimation").GetProperty("running").GetBoolean(), "Expansion settles");
        }
        Console.WriteLine($"PASS chrome motion: {assertions} assertions; Closed/Open 90/270 intermediate geometry, endpoint and retained-structure checks.");
    }
}
