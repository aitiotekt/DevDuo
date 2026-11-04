using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using DevDuo;
using ChromeKind = DevDuo.WindowsComposition.Kind;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckRenderStability()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Render stability runtime");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Fold, 0);
        Select(Settings.Scale, 1);
        Select(Settings.ThemeChoice, 1);
        var panel = Web.WindowsPanel!;
        var browser = panel.Browser;
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(250);
        Check(PresentationGetForegroundWindow() == owner, "Render stability requires the independent foreground window");
        var outsetOnly = Environment.GetEnvironmentVariable("DEVDUO_TEST_RENDER_STABILITY_OUTSET_ONLY") == "1";
        var failures = new List<string>();
        if (!outsetOnly && await CheckSideChromeSequence(panel, owner, "right") is { } rightFailure)
            failures.Add(rightFailure);
        Select(Settings.ThemeChoice, 1);
        var modes = new[] { ("touch", true, false, false), ("wheel", false, false, false),
            ("script-transform", false, true, false), ("outset-touch", true, false, true), ("outset-wheel", false, false, true) };
        foreach (var (input, touch, script, outset) in modes)
        {
            if (outsetOnly && !outset) continue;
            var fixture = outset ? "outset-footer.html" : "translated-footer.html";
            var html = File.ReadAllText(Path.Combine("tests/DevDuo.WebKitProbe/pages", fixture));
            File.WriteAllText(Path.Combine(Program.Output, fixture), html);
            Select(Settings.Interaction, touch ? 0 : 1);
            panel.ShowHtml(html);
            await WaitForScript("typeof footerTestState", value => value == "function", "Translated footer fixture");
            if (script) await Web.EvaluateResultAsync("window.footerTestUseScript=true;'configured'");
            await Web.EvaluateResultAsync("scrollTo(0,400);'ready'");
            if (touch) await InteractionPointer(0x200, 240, 360, 20);
            await Task.Delay(600);
            var before = State();
            var bounds = browser.PresentationBounds!(browser.ClientRectangle);
            var captureBounds = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var scale = browser.BackingScale;
            var buttonBounds = browser.Chrome.Where(element => element.Kind is ChromeKind.Back or ChromeKind.Bookmarks or ChromeKind.TabGroup)
                .ToDictionary(element => element.Kind.ToString(), element => Rectangle.Round(new RectangleF(
                    element.X + element.VisualX, element.Y + element.VisualY, element.VisualWidth, element.VisualHeight)));
            Check(buttonBounds.Count == 3, "Closed portrait has three independently visible right-side button surfaces");
            var host = new List<object>();
            using var monitor = new System.Windows.Forms.Timer { Interval = 12 };
            monitor.Tick += (_, _) =>
            {
                var state = State();
                host.Add(new { time = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, state = state.GetProperty("chromeState"),
                    edges = FixedExtension(state), color = state.GetProperty("contentColor"),
                    updates = state.GetProperty("webKit").GetProperty("composition").GetProperty("updates") });
            };
            var capture = new ComposedWindowSequence(owner, captureBounds, 2700);
            await Task.WhenAny(capture.Ready, capture.Completed);
            if (capture.Completed.IsCompleted) await capture.Completed;
            monitor.Start();
            try
            {
                if (touch) await NativeTouchDrag(240, 360, 135);
                else NativeWheel(true);
                await WaitForScript("String(footerTestState().revealed)", value => value == "true", "Input reveals fixed footer");
                await Task.Delay(500);
                var shown = await Web.EvaluateResultAsync("JSON.stringify(footerTestState())");
                if (touch) await NativeTouchDrag(240, 135, 390);
                else NativeWheel(false);
                await WaitForScript("String(footerTestState().revealed)", value => value == "false", "Reverse input hides fixed footer");
                var frames = await capture.Completed;
                var after = await Web.EvaluateResultAsync("JSON.stringify(footerTestState())");
                var failure = InspectRenderStability(input, captureBounds, frames, buttonBounds, scale, host, before, shown, after, outset ? 10 : 0);
                if (failure is not null) failures.Add(failure);
            }
            finally { monitor.Stop(); }
        }
        await CheckOutsetFixedVisibilityAndFill(panel, owner);
        if (!outsetOnly)
        {
            await CheckFinishedFixedTransform();
            while (State().GetProperty("bodyRotationDegrees").GetInt32() != 270)
            {
                Rotate();
                await Task.Delay(100);
            }
            if (await CheckSideChromeSequence(panel, owner, "left") is { } leftFailure)
                failures.Add(leftFailure);
        }
        Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine($"PASS translated fixed footer and Safari buttons: {assertions} assertions; consecutive composed-window frames at {Program.Output}");
    }

    private async Task CheckOutsetFixedVisibilityAndFill(WindowsWebViewPanel panel, nint owner)
    {
        Select(Settings.Interaction, 0);
        var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/outset-footer.html");
        panel.ShowHtml(html);
        await WaitForScript("typeof footerTestState", value => value == "function", "Fixed marker fixture");
        await Web.EvaluateResultAsync("window.footerTestUseDirection=true;scrollTo(0,400);'ready'");
        await InteractionPointer(0x200, 240, 360, 20);
        await Task.Delay(550);
        var browser = panel.Browser;
        var device = browser.PresentationBounds!(browser.ClientRectangle);
        var captureBounds = new PixelRect(device.X, device.Y, device.Width, device.Height);
        var scale = browser.BackingScale;
        int Pixel(double css) => (int)Math.Round(css * scale);
        int[] Rgb(ComposedWindowSequence.Frame frame, double x, double y)
        {
            var i = (Pixel(y) * captureBounds.Width + Pixel(x)) * 4;
            return [frame.Bgra[i + 2], frame.Bgra[i + 1], frame.Bgra[i]];
        }
        static bool Red(int[] rgb) => rgb[0] > 115 && rgb[0] - rgb[1] > 55 && rgb[0] - rgb[2] > 55;
        var bottomProbe = Pixel(665);
        bool RedFill(WindowsComposition.Fill fill) => fill.Y <= bottomProbe && fill.Y + fill.Height > bottomProbe &&
            Red([(int)(fill.Color >> 16 & 255), (int)(fill.Color >> 8 & 255), (int)(fill.Color & 255)]);
        var submissions = new List<(ulong Id, bool Async, WindowsComposition.Fill[] Fills)>();
        void Submitted(ulong id, bool asynchronous, WindowsComposition.Fill[] fills) => submissions.Add((id, asynchronous, fills));
        browser.PageFrameSubmitted += Submitted;
        try
        {
            await Web.EvaluateResultAsync("scrollTo(0,0);'at-top'");
            await Task.Delay(250);
            submissions.Clear();
            var modalCapture = new ComposedWindowSequence(owner, captureBounds, 1150);
            await Task.WhenAny(modalCapture.Ready, modalCapture.Completed);
            if (modalCapture.Completed.IsCompleted) await modalCapture.Completed;
            await Web.EvaluateResultAsync("document.querySelector('.modal').hidden=false;'opened'");
            var modalFrames = await modalCapture.Completed;
            var modalPixels = modalFrames.Select(frame => new { time = frame.Time100Ns, card = Rgb(frame, 191, 300), extension = Rgb(frame, 80, 665) }).ToArray();
            var modalColors = submissions.SelectMany(frame => frame.Fills)
                .Where(fill => fill.Y <= bottomProbe && fill.Y + fill.Height > bottomProbe).Select(fill => fill.Color).ToArray();
            var stableModal = modalPixels.SkipWhile(pixel => !pixel.extension.All(channel => Math.Abs(channel - 25) <= 3)).ToArray();
            var modalEdge = FixedExtension(State());
            Save("outset-authored-modal-continuation", JsonSerializer.SerializeToElement(new { modalPixels, modalColors, edge = modalEdge }));

            submissions.Clear();
            var coloredCapture = new ComposedWindowSequence(owner, captureBounds, 1150);
            await Task.WhenAny(coloredCapture.Ready, coloredCapture.Completed);
            if (coloredCapture.Completed.IsCompleted) await coloredCapture.Completed;
            await Web.EvaluateResultAsync("""
                document.documentElement.style.background='#6699cc';
                document.body.style.background='rgba(0,0,0,.9)';
                document.querySelector('.modal-host').style.cssText='height:120vh;background:#99ccff';
                'changed'
                """);
            var coloredFrames = await coloredCapture.Completed;
            var coloredPixels = coloredFrames.Select(frame => Rgb(frame, 80, 665)).ToArray();
            var coloredEdge = FixedExtension(State());
            var coloredFills = submissions.SelectMany(frame => frame.Fills)
                .Where(fill => fill.Y <= bottomProbe && fill.Y + fill.Height > bottomProbe).Select(fill => fill.Color).ToArray();
            Save("outset-authored-modal-single-blend", JsonSerializer.SerializeToElement(new { coloredPixels, coloredFills, edge = coloredEdge }));
            Check(coloredEdge.GetProperty("bottomStatus").GetString() == "accepted" && coloredFills.Contains(0x0F1419u) &&
                coloredPixels.Any(pixel => pixel.SequenceEqual(new[] { 15, 20, 25 })),
                "The modal blends once over its opaque backing, independent of the differently colored root");
            await Web.EvaluateResultAsync("""
                document.querySelector('.modal-host').removeAttribute('style');
                document.querySelector('main').style.background='transparent';
                'changed'
                """);
            var layered = await Wait(current => FixedExtension(current).GetProperty("bottomRgb").GetRawText() == "[0,1,1]",
                "Both translucent layers contribute when no opaque backing separates them");
            Save("outset-authored-modal-two-layers", FixedExtension(layered));
            await Web.EvaluateResultAsync("""
                document.documentElement.style.removeProperty('background');
                document.body.style.removeProperty('background');
                document.querySelector('main').style.removeProperty('background');
                'changed'
                """);
            await Wait(current => FixedExtension(current).GetProperty("bottomRgb").GetRawText() == "[25,25,25]",
                "The white root restores the original modal gray");
            foreach (var (label, change) in new[]
            {
                ("painted-overlap", "document.querySelector('.modal-pass-through-decoration').hidden=false"),
                ("occluded", """
                    document.querySelector('.modal-pass-through-decoration').hidden=true;
                    document.querySelector('.modal-obstruction').hidden=false
                    """),
                ("partial", "document.querySelector('.modal-obstruction').hidden=true;document.querySelector('.modal-after').style.width='70%'"),
                ("hidden", "document.querySelector('.modal-after').hidden=true"),
                ("ancestor-clipped", """
                    document.querySelector('.modal-after').hidden=false;
                    document.querySelector('.modal-after').style.removeProperty('width');
                    document.querySelector('.modal-host').classList.add('clipped')
                    """),
                ("different-color", """
                    document.querySelector('.modal-host').classList.remove('clipped');
                    document.querySelector('.modal-sheet').style.background='rgba(0,48,0,.9)'
                    """),
                ("different-owner", """
                    const host=document.querySelector('.modal-host');
                    document.querySelector('.modal-sheet').style.removeProperty('background');
                    host.appendChild(document.querySelector('.modal-after'))
                    """),
            })
            {
                var frameBefore = FixedExtension(State()).GetProperty("frameId").GetUInt64();
                await Web.EvaluateResultAsync(change + ";'changed'");
                var state = await Wait(current =>
                {
                    var edge = FixedExtension(current);
                    var color = edge.GetProperty("bottomRgb");
                    return edge.GetProperty("frameId").GetUInt64() > frameBefore &&
                        (color.ValueKind != JsonValueKind.Array || color.EnumerateArray().Any(channel => channel.GetInt32() != 25));
                }, "The retained gray fill expires for an " + label + " exterior piece");
                var edge = FixedExtension(state);
                Save("outset-authored-modal-" + label, edge);
                var rgb = edge.GetProperty("bottomRgb");
                Check(rgb.ValueKind != JsonValueKind.Array || rgb.EnumerateArray().Any(channel => channel.GetInt32() != 25),
                    "An " + label + " exterior piece cannot authorize a whole-edge gray continuation");
            }
            await Web.EvaluateResultAsync("document.querySelector('.modal-after').remove();document.querySelector('.modal').hidden=true;scrollTo(0,400);'closed'");
            await Task.Delay(180);
            submissions.Clear();

            var capture = new ComposedWindowSequence(owner, captureBounds, 2550);
            await Task.WhenAny(capture.Ready, capture.Completed);
            if (capture.Completed.IsCompleted) await capture.Completed;
            await InteractionPointer(0x201, 240, 360, 20);
            try
            {
                foreach (var y in new[] { 215, 360, 220, 360, 225, 360, 230, 360 })
                    await InteractionPointer(0x200, 240, y, 35);
                await Task.Delay(180);
            }
            finally { await InteractionPointer(0x202, 240, 360, 20); }
            var frames = await capture.Completed;
            var visibility = frames.Select(frame => new { time = frame.Time100Ns, portal = Rgb(frame, 70, 280), direct = Rgb(frame, 320, 365) }).ToArray();
            bool MarkerVisible(int[] rgb, int[] target) => rgb.Select((channel, i) => Math.Abs(channel - target[i])).Max() <= 18;
            var absentPortal = visibility.Count(frame => !MarkerVisible(frame.portal, [17, 120, 108]));
            var absentDirect = visibility.Count(frame => !MarkerVisible(frame.direct, [62, 35, 110]));
            var firstAbsent = Array.FindIndex(visibility, frame => !MarkerVisible(frame.portal, [17, 120, 108]) || !MarkerVisible(frame.direct, [62, 35, 110]));
            var rapidRedFills = submissions.Where(frame => frame.Fills.Any(RedFill)).Select(frame => frame.Id).ToArray();
            var rapidRedFrames = frames.Count(frame => Red(Rgb(frame, 80, 665)));
            foreach (var index in new[] { 0, Math.Max(0, firstAbsent), frames.Count - 1 }.Distinct())
                ComposedWindowSequence.Save(frames[index], captureBounds, Path.Combine(Program.Output, $"outset-fixed-visibility-{index:D3}.png"));
            Save("outset-rapid-fixed-visibility", JsonSerializer.SerializeToElement(new { visibility, absentPortal, absentDirect,
                rapidRedFills, rapidRedFrames, submissions = submissions.Select(s => new { s.Id, s.Async }) }));
            Check(frames.Count > 10 && submissions.Count(s => s.Async) >= 5,
                "Fixed visibility is observed across independent native scroll frames");

            submissions.Clear();
            var straddlingCapture = new ComposedWindowSequence(owner, captureBounds, 1150);
            await Task.WhenAny(straddlingCapture.Ready, straddlingCapture.Completed);
            if (straddlingCapture.Completed.IsCompleted) await straddlingCapture.Completed;
            await Web.EvaluateResultAsync("document.documentElement.classList.add('straddling');'changed'");
            var settled = await straddlingCapture.Completed;
            var viewportHeight = State().GetProperty("actualPageViewport").GetProperty("height").GetDouble();
            var sampled = settled.Select(frame => new { time = frame.Time100Ns, pageButton = Rgb(frame, 80, viewportHeight - 5), extension = Rgb(frame, 80, 665) }).ToArray();
            var filled = submissions.Where(frame => frame.Fills.Any(RedFill)).ToArray();
            var redOnWindow = sampled.Count(frame => Red(frame.extension));
            Save("outset-straddling-native-fill", JsonSerializer.SerializeToElement(new { sampled, redOnWindow, redFills = filled.Select(s => s.Id),
                submissions = submissions.Select(s => new { s.Id, s.Async, fills = s.Fills.Select(f => new { f.X, f.Y, f.Width, f.Height, f.Color }) }) }));
            foreach (var (frame, index) in settled.Select((frame, index) => (frame, index)).Where(pair =>
                pair.index == 0 || pair.index == settled.Count - 1 || Red(sampled[pair.index].extension)).Take(8))
                ComposedWindowSequence.Save(frame, captureBounds, Path.Combine(Program.Output, $"outset-straddling-{index:D3}.png"));
            Check(sampled.Any(frame => Red(frame.pageButton)) && submissions.Count > 0,
                "The stationary red foreground genuinely crosses the viewport sampling strip");
            Console.WriteLine($"Fixed visibility: {frames.Count} frames, {absentPortal} portal and {absentDirect} direct marker absences; edge paint: {filled.Length} red native fills and {redOnWindow} red window frames.");
            Check(absentPortal == 0 && absentDirect == 0,
                $"Both fixed markers retain their pixels through rapid scroll reversals ({absentPortal} portal, {absentDirect} direct absences)");
            Check(filled.Length == 0 && redOnWindow == 0,
                $"A partial foreground does not become a whole-edge continuation ({filled.Length} native fills, {redOnWindow} window frames)");
            Check(rapidRedFills.Length == 0 && rapidRedFrames == 0,
                $"A reversing fixed animation preserves the edge color ({rapidRedFills.Length} native fills, {rapidRedFrames} window frames)");
            Check(modalColors.Contains(0x191919u) && modalEdge.GetProperty("bottomStatus").GetString() == "accepted" &&
                stableModal.Length >= 5 && stableModal.All(pixel =>
                    pixel.card.All(channel => channel >= 252) && pixel.extension.All(channel => Math.Abs(channel - 25) <= 3)),
                "The white card and gray authored continuation remain visible together on every settled modal frame");

            submissions.Clear();
            var stripCapture = new ComposedWindowSequence(owner, captureBounds, 1250);
            await Task.WhenAny(stripCapture.Ready, stripCapture.Completed);
            if (stripCapture.Completed.IsCompleted) await stripCapture.Completed;
            foreach (var inset in new[] { 14, 15, 16 })
            {
                await Web.EvaluateResultAsync($"document.querySelector('.action').style.bottom='{inset}px';'changed'");
                await Task.Delay(110);
            }
            var stripFrames = await stripCapture.Completed;
            var stripObservations = stripFrames.Select(frame => new
            {
                time = frame.Time100Ns,
                inside = Rgb(frame, 80, viewportHeight - 9),
                extension = Rgb(frame, 80, 665),
            }).ToArray();
            var stripRedFills = submissions.Where(frame => frame.Fills.Any(RedFill)).Select(s => s.Id).ToArray();
            var stripRedFrames = stripObservations.Count(frame => Red(frame.extension));
            Save("outset-inside-sample-strip", JsonSerializer.SerializeToElement(new { stripObservations, stripRedFills, stripRedFrames }));
            Check(stripObservations.Any(frame => Red(frame.inside)) && submissions.Count >= 3,
                "The partial foreground paints inside the sample strip across native submissions");
            Check(stripRedFills.Length == 0 && stripRedFrames == 0,
                $"A partial foreground inside the sampled strip cannot color the extension ({stripRedFills.Length} native fills, {stripRedFrames} window frames)");

            submissions.Clear();
            var viewportCoverCapture = new ComposedWindowSequence(owner, captureBounds, 1100);
            await Task.WhenAny(viewportCoverCapture.Ready, viewportCoverCapture.Completed);
            if (viewportCoverCapture.Completed.IsCompleted) await viewportCoverCapture.Completed;
            await Web.EvaluateResultAsync("""
                document.querySelector('.action').style.removeProperty('bottom');
                document.documentElement.classList.add('with-viewport-cover');
                'changed'
                """);
            await WaitForScript("""
                String([.05,.25,.5,.75,.95].every(x =>
                    document.elementFromPoint(innerWidth * x, innerHeight - 4)?.classList.contains('viewport-cover')))
                """, value => value == "true", "Transparent viewport layer owns every edge hit");
            var coveredFrames = await viewportCoverCapture.Completed;
            var coveredSamples = coveredFrames.Select(frame => new
            {
                inside = Rgb(frame, 80, viewportHeight - 7),
                extension = Rgb(frame, 80, 665),
            }).ToArray();
            var coveredRedFills = submissions.Where(frame => frame.Fills.Any(RedFill)).Select(frame => frame.Id).ToArray();
            var coveredRedFrames = coveredSamples.Count(frame => Red(frame.extension));
            Save("outset-transparent-viewport-cover", JsonSerializer.SerializeToElement(new { coveredSamples, coveredRedFills, coveredRedFrames }));
            Check(coveredSamples.Any(frame => Red(frame.inside)) && submissions.Count > 0,
                "The local red foreground is visible beneath a transparent viewport-sized hit target");
            Check(coveredRedFills.Length == 0 && coveredRedFrames == 0,
                $"A transparent full-viewport hit target cannot turn local foreground paint into a whole-edge color ({coveredRedFills.Length} native fills, {coveredRedFrames} window frames)");

            submissions.Clear();
            var uniformCapture = new ComposedWindowSequence(owner, captureBounds, 1000);
            await Task.WhenAny(uniformCapture.Ready, uniformCapture.Completed);
            if (uniformCapture.Completed.IsCompleted) await uniformCapture.Completed;
            await Web.EvaluateResultAsync("""
                document.documentElement.classList.remove('with-viewport-cover');
                document.querySelector('.action').style.display = 'none';
                document.querySelector('footer').style.backgroundImage = 'linear-gradient(#174e8d, #174e8d)';
                'changed'
                """);
            var uniformFrames = await uniformCapture.Completed;
            var uniformSamples = uniformFrames.Select(frame => Rgb(frame, 80, 665)).ToArray();
            var uniformFills = submissions.SelectMany(frame => frame.Fills).Where(fill => fill.Y <= bottomProbe &&
                fill.Y + fill.Height > bottomProbe).Select(fill => fill.Color).ToArray();
            Save("outset-uniform-sample-strip", JsonSerializer.SerializeToElement(new { uniformSamples, uniformFills }));
            Check(uniformFills.Contains(0x174e8du) && uniformSamples.Any(rgb => MarkerVisible(rgb, [23, 78, 141])),
                "A painted background that reaches both ends continues beyond the viewport");
        }
        finally { browser.PageFrameSubmitted -= Submitted; }
    }

    private async Task<string?> CheckSideChromeSequence(WindowsWebViewPanel panel, nint owner, string side)
    {
        Select(Settings.ThemeChoice, 2);
        Select(Settings.Interaction, 1);
        var fixture = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/chrome-side-backdrop.html");
        File.WriteAllText(Path.Combine(Program.Output, "chrome-side-backdrop.html"), fixture);
        panel.ShowHtml(fixture);
        await WaitForScript("document.title", value => value == "Safari side chrome backdrop", "Independent side glass fixture");
        await Web.EvaluateResultAsync("scrollTo(0,400);'ready'");
        await Task.Delay(700);
        var browser = panel.Browser;
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        var pixels = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var buttons = browser.Chrome.Where(element => element.Kind is ChromeKind.Back or ChromeKind.Bookmarks or ChromeKind.TabGroup)
            .ToDictionary(element => element.Kind.ToString(), element => Rectangle.Round(new RectangleF(
                element.X + element.VisualX, element.Y + element.VisualY, element.VisualWidth, element.VisualHeight)));
        Check(buttons.Count == 3, "Independent dark side glass fixture presents all three button surfaces");
        var start = State();
        var initialGeometry = start.GetProperty("webKit").GetProperty("logicalGeometry");
        var sideRect = start.GetProperty("sideChromeColor").GetProperty("sampleRectSceneCssPx");
        Check(sideRect.ValueKind == JsonValueKind.Object && sideRect.GetProperty("x").GetInt32() ==
            (side == "left" ? 0 : initialGeometry.GetProperty("width").GetInt32() - initialGeometry.GetProperty("right").GetInt32()) &&
            sideRect.GetProperty("width").GetInt32() == initialGeometry.GetProperty(side).GetInt32(),
            "The optical sample is confined to the actual " + side + " inset");
        var host = new List<object>();
        var capture = new ComposedWindowSequence(owner, pixels, 3750);
        await Task.WhenAny(capture.Ready, capture.Completed);
        if (capture.Completed.IsCompleted) await capture.Completed;
        for (var phase = 0; phase < 4; phase++)
        {
            NativeWheel(phase % 2 == 0);
            for (var sample = 0; sample < 10; sample++)
            {
                await Task.Delay(65);
                var state = State();
                host.Add(new { time = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, phase,
                    state = state.GetProperty("chromeState"), y = state.GetProperty("scrollY"), color = state.GetProperty("contentColor"),
                    sideColor = state.GetProperty("sideChromeColor"),
                    updates = state.GetProperty("webKit").GetProperty("composition").GetProperty("updates") });
            }
        }
        var frames = await capture.Completed;
        var edgeX = side == "left" ? 12 : pixels.Width - 12;
        var observations = frames.Select(frame => new
        {
            time = frame.Time100Ns / 10_000_000.0,
            backdrop = frame.Bgra[((pixels.Height / 2) * pixels.Width + pixels.Width / 3) * 4 + 1],
            edgeBackdrop = frame.Bgra[((pixels.Height / 2) * pixels.Width + edgeX) * 4 + 1],
            surfaces = buttons.ToDictionary(entry => entry.Key, entry => SideButtonPixels(frame, pixels.Width, entry.Value)),
        }).ToArray();
        var extremes = buttons.Keys.ToDictionary(key => key, key => new
        {
            minimum = observations.Min(frame => frame.surfaces[key].Mean), maximum = observations.Max(frame => frame.surfaces[key].Mean),
            minimumInk = observations.Min(frame => frame.surfaces[key].Ink), maximumInk = observations.Max(frame => frame.surfaces[key].Ink),
        });
        Save($"render-stability-{side}-buttons", JsonSerializer.SerializeToElement(new { pixels, buttons, start, host, observations, extremes }));
        var dark = Array.FindIndex(observations, observation => observation.backdrop < 50);
        foreach (var index in new[] { 0, Math.Max(0, dark),
            Array.FindIndex(observations, observation => observation.surfaces["Back"].Mean == extremes["Back"].minimum), frames.Count - 1 }.Distinct())
            ComposedWindowSequence.Save(frames[index], pixels, Path.Combine(Program.Output, $"render-stability-{side}-{index:D3}.png"));
        Check(observations.Length > 12 && dark >= 0 && observations.Any(frame => frame.backdrop > 200),
            "The dark chrome sequence records contrasting real page pixels across multiple submitted phases");
        Check(extremes.Values.All(extreme => extreme.maximumInk > 8), "All three side-button glyphs are visible in the dark fixture");
        Check(observations.Max(frame => frame.edgeBackdrop) - observations.Min(frame => frame.edgeBackdrop) <= 3,
            "The composed local side backdrop stays stable while contrasting page pixels scroll");
        var bookmark = buttons["Bookmarks"];
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var x = (bookmark.Left + bookmark.Width / 2.0) / browser.BackingScale - geometry.GetProperty("left").GetInt32();
        var y = (bookmark.Top + bookmark.Height / 2.0) / browser.BackingScale - geometry.GetProperty("top").GetInt32();
        await InteractionPointer(0x201, x, y);
        await InteractionPointer(0x202, x, y);
        await Wait(state => Text(state, "lastChromeAction", "Bookmarks"), "Retained side glass button stays clickable after repeated chrome transitions");
        var failure = extremes.Where(entry => entry.Value.maximum - entry.Value.minimum > 8 || entry.Value.minimumInk < entry.Value.maximumInk * .75)
            .Select(entry => $"{entry.Key} {entry.Value.minimum:F1}–{entry.Value.maximum:F1} ({entry.Value.minimumInk}–{entry.Value.maximumInk} glyph pixels)").ToArray();
        Console.WriteLine($"dark {side} controls: {frames.Count} composed frames, {string.Join(", ", extremes.Select(entry => $"{entry.Key} {entry.Value.minimum:F1}–{entry.Value.maximum:F1}"))}");
        return failure.Length == 0 ? null : $"Dark {side} controls changed against their stable local backdrop: " + string.Join(", ", failure);
    }

    private async Task CheckFinishedFixedTransform()
    {
        await Web.EvaluateResultAsync("""
            footer.style.transition = 'none';
            footer.style.background = '#2850a0';
            footer.classList.add('revealed');
            window.finishedFooterTransform = footer.animate([{transform:'translateY(35px)'},{transform:'translateY(0px)'}],
                {duration:220,fill:'forwards'});
            'started';
            """);
        await WaitForScript("finishedFooterTransform.playState", value => value == "finished", "The fixed transform finishes with forwards fill");
        var settled = await Wait(state =>
        {
            var edge = FixedExtension(state).GetProperty("bottomRgb");
            return edge.ValueKind == JsonValueKind.Array && edge.EnumerateArray().Select(value => value.GetInt32()).SequenceEqual([40, 80, 160]);
        }, "A finished fill-forwards transform establishes the settled bottom color");
        Save("render-stability-finished-transform", settled);
    }

    private sealed record SidePixels(double Mean, int Ink);

    private static SidePixels SideButtonPixels(ComposedWindowSequence.Frame frame, int stride, Rectangle box)
    {
        var sum = 0;
        var count = 0;
        var ink = 0;
        for (var y = box.Top + box.Height / 4; y < box.Bottom - box.Height / 4; y++)
            for (var x = box.Left + box.Width / 4; x < box.Right - box.Width / 4; x++)
            {
                var green = frame.Bgra[(y * stride + x) * 4 + 1];
                if (green > 238) ink++;
                else { sum += green; count++; }
            }
        return new(count == 0 ? 255 : sum / (double)count, ink);
    }

    private string? InspectRenderStability(string input, PixelRect bounds, List<ComposedWindowSequence.Frame> frames,
        Dictionary<string, Rectangle> buttons, double scale, List<object> host, JsonElement before, string? shown, string? after, int outset)
    {
        var x = (int)Math.Round(67 * scale);
        var bottomY = bounds.Height - (int)Math.Round(13 * scale);
        int Channel(ComposedWindowSequence.Frame frame, int px, int py, int channel) => frame.Bgra[(py * bounds.Width + px) * 4 + channel];
        bool Red(ComposedWindowSequence.Frame frame, int px, int py) => Channel(frame, px, py, 2) - Channel(frame, px, py, 1) > 65;
        int Ink(ComposedWindowSequence.Frame frame, Rectangle box)
        {
            var dark = 0;
            for (var py = box.Top + 5; py < box.Bottom - 5; py++)
                for (var px = box.Left + 5; px < box.Right - 5; px++)
                    if (Channel(frame, px, py, 1) < 95) dark++;
            return dark;
        }
        int SurfaceDelta(ComposedWindowSequence.Frame frame, Rectangle box)
        {
            var changed = 0;
            for (var py = box.Top + box.Height / 4; py < box.Bottom - box.Height / 4; py++)
                for (var px = box.Left + box.Width / 4; px < box.Right - box.Width / 4; px++)
                    if (Math.Abs(Channel(frame, px, py, 1) - Channel(frames[0], px, py, 1)) > 24) changed++;
            return changed;
        }
        var observations = frames.Select(frame => new
        {
            time = frame.Time100Ns / 10_000_000.0,
            bottomRgb = new[] { Channel(frame, x, bottomY, 2), Channel(frame, x, bottomY, 1), Channel(frame, x, bottomY, 0) },
            redExtension = Red(frame, x, bottomY),
            redButton = Enumerable.Range(bounds.Height - (int)Math.Round(180 * scale), (int)Math.Round(135 * scale)).Any(y => Red(frame, x, y)),
            buttons = buttons.ToDictionary(entry => entry.Key, entry => Ink(frame, entry.Value)),
            surfaceChanges = buttons.ToDictionary(entry => entry.Key, entry => SurfaceDelta(frame, entry.Value)),
        }).ToArray();
        var red = observations.Count(frame => frame.redExtension);
        var missing = observations.Count(frame => buttons.Keys.Any(key => frame.buttons[key] < observations[0].buttons[key] * .8));
        var flashes = observations.Count(frame => buttons.Any(entry => frame.surfaceChanges[entry.Key] > entry.Value.Width * entry.Value.Height * .05));
        int[]? portalFlow = null;
        if (outset != 0)
        {
            var flowX = (int)Math.Round(16 * scale);
            var flowY = (int)Math.Round(624 * scale);
            portalFlow = new[] { Channel(frames[0], flowX, flowY, 2), Channel(frames[0], flowX, flowY, 1), Channel(frames[0], flowX, flowY, 0) };
        }
        Save("render-stability-" + input, JsonSerializer.SerializeToElement(new { bounds, scale, host, before, shown, after, observations, red, missing, flashes, portalFlow }));
        var samples = new[] { 0, Math.Max(0, Array.FindIndex(observations, frame => frame.redButton)),
            Math.Max(0, Array.FindIndex(observations, frame => frame.redExtension)),
            Math.Max(0, Array.FindIndex(observations, frame => buttons.Keys.Any(key => frame.buttons[key] < observations[0].buttons[key] * .8))),
            Math.Max(0, Array.FindIndex(observations, frame => buttons.Any(entry => frame.surfaceChanges[entry.Key] > entry.Value.Width * entry.Value.Height * .05))), frames.Count - 1 };
        foreach (var index in samples.Distinct())
            ComposedWindowSequence.Save(frames[index], bounds, Path.Combine(Program.Output, $"render-stability-{input}-{index:D3}.png"));
        Check(observations.Length > 10 && observations.Zip(observations.Skip(1)).All(pair => pair.First.time < pair.Second.time),
            $"{input} records a sequence of ordered composed frames");
        Check(observations.Any(frame => frame.redButton), $"{input} genuinely displays the red page button during the sequence");
        using var visible = JsonDocument.Parse(shown ?? "null");
        using var hidden = JsonDocument.Parse(after ?? "null");
        Check(visible.RootElement.GetProperty("bottom").GetDouble() == visible.RootElement.GetProperty("height").GetDouble() + outset &&
            hidden.RootElement.GetProperty("top").GetDouble() >= hidden.RootElement.GetProperty("height").GetDouble() &&
            visible.RootElement.GetProperty("width").GetDouble() == hidden.RootElement.GetProperty("width").GetDouble(),
            $"{input} keeps the real footer geometry and the same viewport width");
        Check(buttons.Keys.All(key => observations[0].buttons[key] > 8), $"{input} first frame shows all right-side button glyphs");
        if (portalFlow is not null)
            Check(portalFlow[2] - portalFlow[0] > 120, $"{input} ordinary portal content still paints beyond the layout viewport");
        var result = $"{input}: {frames.Count} consecutive composed frames, {red} red continuation, {missing} missing button ink, {flashes} button surface flashes";
        Console.WriteLine(result);
        return red == 0 && missing == 0 && flashes == 0 ? null : result;
    }
}
