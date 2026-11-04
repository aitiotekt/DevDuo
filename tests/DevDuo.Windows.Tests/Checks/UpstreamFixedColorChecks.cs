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
            await CheckFixedDimmingCard();
            Console.WriteLine($"PASS upstream fixed colors: {assertions} assertions; independent patterned-underlay fixtures, legacy export and presented inset pixels.");
        }
        finally { NativeLibrary.Free(module); }
    }

    private async Task CheckFixedDimmingCard()
    {
        var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-dimming-card.html");
        File.WriteAllText(Path.Combine(Program.Output, "fixed-dimming-card.html"), html);
        var white = new[] { 255, 255, 255 };
        var black = new[] { 0, 0, 0 };
        var gray = new[] { 25, 25, 25 };
        await Load("dimming-card");
        await Observe("dimming-card-first-paint", white, cardVisible: true, capture: true);
        await Load("veil-only");
        await Observe("dimming-card-confirmed-veil", black, cardVisible: false, capture: true);
        await Change("overlay.className = ''");
        await Observe("dimming-card-revealed", black, cardVisible: true, capture: true);
        await Change("document.getElementById('veil').style.background = 'rgba(24,72,120,.5)'");
        await Observe("dimming-card-updated-color", [140, 164, 188], cardVisible: true, capture: true);
        await Change("document.getElementById('veil').style.opacity = '.5'");
        await Observe("dimming-card-updated-opacity", [197, 209, 221], cardVisible: true, capture: false);
        await Change("overlay.className = 'partial-veil'");
        await Observe("dimming-card-partial-veil", null, cardVisible: true, capture: true, windowColor: white);
        await Change("overlay.className = 'veil-only'; document.getElementById('veil').style.cssText = 'background:rgba(25,25,25,.9)'");
        await Observe("dimming-card-gray-veil", gray, cardVisible: false, capture: false);
        await Change("overlay.className = ''");
        await Observe("dimming-card-gray-revealed", gray, cardVisible: true, capture: true);
        await Change("overlay.className = 'two-veils'");
        await Observe("dimming-card-two-veils", null, cardVisible: true, capture: true, windowColor: white);
        await Change("overlay.className = 'veil-only'");
        await Observe("dimming-card-veil-confirmed-again", gray, cardVisible: false, capture: false);
        await Change("overlay.className = 'full-card'");
        await Observe("dimming-card-white-full-viewport", white, cardVisible: true, capture: true);
        await Change("overlay.className = 'veil-only'");
        await Observe("dimming-card-before-replacement", gray, cardVisible: false, capture: false);
        await Change("overlay.className = ''; const oldVeil = document.getElementById('veil'); oldVeil.replaceWith(oldVeil.cloneNode())");
        await Observe("dimming-card-replacement-veil", white, cardVisible: true, capture: false);
        await Change("overlay.className = 'veil-only'");
        await Observe("dimming-card-before-close", gray, cardVisible: false, capture: false);
        await Change("overlay.style.display = 'none'");
        await Observe("dimming-card-closed", null, cardVisible: false, capture: false);
        await Change("overlay.style.display = ''; overlay.className = ''");
        await Observe("dimming-card-reopened", white, cardVisible: true, capture: false);
        await Change("overlay.style.display = 'none'; document.getElementById('native-modal').showModal()");
        await Observe("dimming-card-native-backdrop", [128, 128, 128], cardVisible: false, capture: true);

        async Task Load(string mode)
        {
            Web.WindowsPanel!.ShowHtml(html.Replace("__MODE__", mode));
            await WaitForScript("document.getElementById('overlay').className", value => value == mode, mode + " fixture");
        }

        Task<string?> Change(string script) => Web.EvaluateResultAsync(script + ";'changed'");

        async Task Observe(string phase, int[]? expected, bool cardVisible, bool capture, int[]? windowColor = null)
        {
            await Task.Delay(500);
            if (windowColor is not null)
                await Wait(state => FixedExtension(state).GetProperty("bottomRgb").ValueKind == JsonValueKind.Null,
                    phase + " releases the previous color after uncertain coverage expires");
            var state = State();
            var continuation = FixedExtension(state);
            var bottom = continuation.GetProperty("bottomRgb");
            using var dom = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(dimmingFixture())"))!);
            int[]? pixel = null, cardPixel = null;
            if (capture)
            {
                window.Activate();
                await RecordModalFrames(phase, 1);
                using var bitmap = new Bitmap(Path.Combine(Program.Output, phase + "-00.png"));
                var scale = Web.WindowsPanel!.Browser.BackingScale;
                var extension = bitmap.GetPixel((int)Math.Round(72 * scale), bitmap.Height - (int)Math.Round(10 * scale));
                var content = bitmap.GetPixel(bitmap.Width / 3, (int)Math.Round(350 * scale));
                pixel = [extension.R, extension.G, extension.B];
                cardPixel = [content.R, content.G, content.B];
            }
            Save(phase, JsonSerializer.SerializeToElement(new { host = state, dom = dom.RootElement, pixel, cardPixel }));
            Check(dom.RootElement.GetProperty("viewport").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual([382, 580]) &&
                dom.RootElement.GetProperty("scroll").EnumerateArray().All(value => value.GetInt32() == 0),
                phase + " retains the real viewport and scroll");
            Check(expected is null ? bottom.ValueKind == JsonValueKind.Null : bottom.ValueKind == JsonValueKind.Array &&
                bottom.EnumerateArray().Select((color, index) => Math.Abs(color.GetInt32() - expected[index])).Max() <= 2,
                $"{phase} produces its own bottom continuation color. Actual {bottom}.");
            if (windowColor is not null)
                Check(continuation.GetProperty("bottomStatus").GetString() == "multiple-or-translucent",
                    phase + " distinguishes unverified edge coverage from a confirmed color");
            if (capture)
                Check(pixel!.Select((color, index) => Math.Abs(color - (windowColor ?? expected!)[index])).Max() <= 3 &&
                    (!cardVisible || cardPixel!.All(color => color >= 248)),
                    phase + " displays the edge fill while the in-viewport card keeps its own paint");
        }
    }
}
