using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckFractionalPageBoundaries()
    {
        Check(WindowsWebKitView.RasterClipEdge(558.3090128755) == 558,
            "94% Fit excludes the unpainted column at 558; damage enclosure is not a content clip.");
        Check(WindowsWebKitView.RasterClipEdge(78.49) == 78 && WindowsWebKitView.RasterClipEdge(78.5) == 79 &&
            WindowsWebKitView.RasterClipEdge(78.51) == 79, "Hard clip edges round half up, including left/top boundaries.");
        Select(Settings.Scale, 1);
        window.MinHeight = 400;
        for (var fold = 0; fold < 2; fold++)
        {
            Select(Settings.Fold, fold);
            for (var angle = 0; angle < 4; angle++)
            {
                await Ready();
                Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/static-boundary.html"));
                await Wait(state => Text(state, "pageTitle", "Static native boundary acceptance"), "Unmodified boundary fixture");
                var document = State().GetProperty("pageDocumentId").GetString();
                foreach (var height in new[] { 960, 874, 862, 823, 740, 564 })
                {
                    window.Height = height;
                    // Observe renderer-confirmed geometry after the real Fit resize.
                    await Task.Delay(350);
                    Web.Evaluate("window.__devduoReportContainerObservation()");
                    await Task.Delay(250);
                    var state = State();
                    var browser = Web.WindowsPanel.Browser;
                    using var bitmap = NativePaintEvidence.Capture(browser, state);
                    var flow = state.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
                    var left = Math.Clamp(flow[0].GetInt32(), 0, bitmap.Width);
                    var right = Math.Clamp(flow[2].GetInt32(), 0, bitmap.Width);
                    var top = Math.Clamp(flow[1].GetInt32(), 0, bitmap.Height);
                    var bottom = Math.Clamp(flow[3].GetInt32(), 0, bitmap.Height);
                    Save($"boundary-{fold}-{angle * 90}-{height}", state);
                    foreach (var point in new[] { new Point(left, top + (bottom - top) / 2), new Point(right - 1, top + (bottom - top) / 2),
                        new Point((left + right) / 2, top), new Point((left + right) / 2, bottom - 1) })
                    {
                        var pixel = bitmap.GetPixel(point.X, point.Y);
                        if (Math.Abs(pixel.R - 36) > 1 || Math.Abs(pixel.G - 104) > 1 || Math.Abs(pixel.B - 172) > 1)
                            NativePaintEvidence.Save(browser, state, Path.Combine(Program.Output, $"boundary-failed-{fold}-{angle * 90}-{height}"));
                        Check(Math.Abs(pixel.R - 36) <= 1 && Math.Abs(pixel.G - 104) <= 1 && Math.Abs(pixel.B - 172) <= 1,
                            $"Live page reaches its declared raster clip: fold={fold}, angle={angle * 90}, height={height}, scale={browser.BackingScale}, point={point}, rgb={pixel}.");
                    }
                    Check(state.GetProperty("pageDocumentId").GetString() == document, "Fractional raster clipping preserves the document.");
                }
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
        Select(Settings.Fold, 0);
        window.Height = 970;
        await Ready();
    }
}
