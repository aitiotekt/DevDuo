using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LegacyEdgeCallback(ulong requestID, uint status, uint states, uint top, uint right, uint bottom, uint left, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LegacyEdgeRequest(nint view, ulong requestID, nint context, LegacyEdgeCallback callback);

    private async Task CheckUpstreamFixedColors()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Native edge runtime");
        Select(Settings.Fold, 0);
        Select(Settings.Scale, 1);
        var runtime = DevDuo.WindowsWebKitApi.RuntimeDirectory();
        var module = NativeLibrary.Load(Path.Combine(runtime, "WebKit2.dll"));
        try
        {
            var request = Marshal.GetDelegateForFunctionPointer<LegacyEdgeRequest>(NativeLibrary.GetExport(module, "WKViewRequestFixedContainerColors"));
            var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-edge-native.html");
            foreach (var mode in new[] { "direct", "nested", "low-alpha", "opaque", "mixed", "partial", "flow", "sticky" })
            {
                // A fresh document prevents the legacy path's history cache
                // from acting as the expected color for the next fixture.
                Web.WindowsPanel!.ShowHtml(html.Replace("get('mode') || 'nested'", $"get('mode') || '{mode}'"));
                await WaitForScript("mode", value => value == mode, mode + " fixture");
                await Task.Delay(800);
                var browser = Web.WindowsPanel.Browser;
                var view = (nint)typeof(DevDuo.WindowsWebKitView).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
                var completion = new TaskCompletionSource<(uint Status, uint States, uint Top, uint Bottom)>();
                LegacyEdgeCallback callback = (requestID, status, states, top, right, bottom, left, context) => completion.TrySetResult((status, states, top, bottom));
                request(view, 1, 0, callback);
                var legacy = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
                GC.KeepAlive(callback);
                var state = State();
                var colors = FixedExtension(state);
                Save("native-edge-" + mode, JsonSerializer.SerializeToElement(new { legacy = new { legacy.Status, legacy.States, legacy.Top, legacy.Bottom }, host = state }));
                var rgb = colors.GetProperty(mode == "sticky" ? "topRgb" : "bottomRgb");
                if (mode is "direct" or "nested" or "low-alpha" or "opaque" or "sticky")
                {
                    var shift = mode == "sticky" ? 0 : 4;
                    Check(legacy.Status == 0 && ((legacy.States >> shift) & 3) == 1, mode + " upstream path has an edge color");
                    int[] expected = mode == "low-alpha" ? [128, 128, 128] : mode == "opaque" ? [40, 80, 160] : [0, 0, 0];
                    Check(rgb.ValueKind == JsonValueKind.Array && rgb.EnumerateArray().Select((c, i) => Math.Abs(c.GetInt32() - expected[i])).Max() <= 3,
                        mode + " follows upstream alpha/background policy independently of the patterned underlay");
                    var actual = rgb.EnumerateArray().Select(c => c.GetUInt32()).ToArray();
                    Check((actual[0] << 16 | actual[1] << 8 | actual[2]) == (mode == "sticky" ? legacy.Top : legacy.Bottom), mode + " v2 agrees with the preserved upstream export");
                    if (mode != "sticky")
                    {
                        window.Activate();
                        await RecordModalFrames("native-edge-" + mode, 1);
                        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                        using var bitmap = new Bitmap(Path.Combine(Program.Output, "native-edge-" + mode + "-00.png"));
                        // Sample beneath the floating address widget, in the real
                        // obscured inset, rather than sampling the viewport itself.
                        var point = browser.PresentationBounds!(new Rectangle(12, browser.ClientRectangle.Height - 10, 1, 1)).Location;
                        var pixel = bitmap.GetPixel(point.X - bounds.X, point.Y - bounds.Y);
                        Check(new[] { pixel.R, pixel.G, pixel.B }.Select((c, i) => Math.Abs(c - expected[i])).Max() <= 3,
                            mode + " presents the accepted native color below the viewport");
                    }
                }
                else
                    Check(rgb.ValueKind == JsonValueKind.Null, mode + " cannot invent a whole-side solid fill");
                Check(await Web.EvaluateResultAsync("JSON.stringify([innerWidth,innerHeight,scrollX,scrollY])") == "[382,580,0,0]",
                    mode + " preserves the real viewport and scroll");
            }
            Console.WriteLine($"PASS upstream fixed colors: {assertions} assertions; independent patterned-underlay fixtures, legacy export and presented inset pixels.");
        }
        finally { NativeLibrary.Free(module); }
    }
}
