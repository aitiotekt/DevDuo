using System.Drawing;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckTouchPerformance(bool accelerated)
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Performance runtime");
        Select(Settings.Interaction, 0);
        Select(Settings.Scale, 1);
        SetPageCompositing(Web.WindowsPanel!.Browser, accelerated);
        var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/touch-performance.html");
        if (accelerated) html = html.Replace("const accelerated = false", "const accelerated = true");
        Web.WindowsPanel.ShowHtml(html);
        await WaitForScript("typeof inputPerformance", value => value == "function", "Performance fixture");
        var browser = Web.WindowsPanel.Browser;
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(200);
        Check(PresentationGetForegroundWindow() == owner, "Real drag performance requires the active test window");
        await Web.EvaluateResultAsync("scrollTo(0,400);'positioned'");
        await Task.Delay(300);
        await InteractionPointer(0x201, 240, 380);
        await Web.EvaluateResultAsync("resetPerformance()");
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var left = geometry.GetProperty("left").GetDouble();
        var top = geometry.GetProperty("top").GetDouble();
        var scale = browser.BackingScale;
        var timer = Stopwatch.StartNew();
        try
        {
            for (var index = 1; index <= 120; index++)
            {
                var point = new Point((int)((left+240)*scale), (int)((top+380-index*2)*scale));
                System.Windows.Forms.Cursor.Position = browser.PresentationBounds!(new Rectangle(point, new Size(1,1))).Location;
                await Task.Delay(4);
            }
        }
        finally { await InteractionPointer(0x202, 240, 140); }
        timer.Stop();
        await Task.Delay(150);
        using var touchDocument = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(inputPerformance())"))!);
        var touch = touchDocument.RootElement.Clone();
        Save("touch-performance-drag-" + (accelerated ? "gpu" : "cpu"),
            JsonSerializer.SerializeToElement(new { result = touch, host = State(), dragMilliseconds = timer.Elapsed.TotalMilliseconds,
                nativeScrolling = await browser.ReadNativeScrollingStateAsync() }));
        Check(touch.GetProperty("scrollY").GetDouble() > 560, "Real drag delivers accumulated motion through deep ancestry");
        Check(touch.GetProperty("wheelEvents").GetInt32() == 0, "Native touch scrolling does not dispatch DOM wheel events on either paint path");
        Check(WindowsWebKitApi.SetHostTouchEnabled is not null
                ? !State().GetProperty("webKit").GetProperty("nativeTouch").GetProperty("active").GetBoolean()
                : !touch.GetProperty("input").GetProperty("active").GetBoolean(), "Release leaves no queued contact");
        if (touch.GetProperty("input").TryGetProperty("receivedMoves", out var received))
        {
            var processed = touch.GetProperty("input").GetProperty("processedMoves").GetInt64();
            Check(processed > 0 && processed <= received.GetInt64(), "Move processing is bounded by input and animation frames");
        }
        var touchState = State();
        Select(Settings.Interaction, 1);
        await WaitForScript("window.__devduoInput.state.mode", value => value == "mouse", "Mouse comparison");
        await Web.EvaluateResultAsync("scrollTo(0,400);resetPerformance()");
        for (var index = 0; index < 6; index++) { NativeWheel(true); await Task.Delay(80); }
        using var mouseDocument = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(inputPerformance())"))!);
        var mouse = mouseDocument.RootElement.Clone();
        Check(mouse.GetProperty("scrollY").GetDouble() > 560, "Mouse comparison scrolls the same deep fixture");
        Check(mouse.GetProperty("wheelEvents").GetInt32() > 0, "Ordinary mouse wheel input retains authored wheel events on either paint path");
        var timing = State().GetProperty("webKit").GetProperty("frameTiming");
        var clock = timing.GetProperty("clock");
        Check(clock.GetProperty("available").GetBoolean() && clock.GetProperty("uncertaintySeconds").GetDouble() < .05,
            "Native and host timestamps have a measured clock anchor with bounded uncertainty");
        var producedFrames = timing.GetProperty("frames").EnumerateArray().ToArray();
        var asyncFrameIds = timing.GetProperty("scrollFrames").EnumerateArray()
            .Where(frame => frame.GetProperty("scrollRevision").GetUInt64() > 0)
            .Select(frame => frame.GetProperty("frameId").GetUInt64()).ToHashSet();
        var surface = State().GetProperty("webKit").GetProperty("frameSurface");
        Check(surface.GetProperty("lastContentTransport").GetString() == (accelerated ? "shared-d3d11" : "native-cpu-bitmap"),
            "Drag and wheel checks exercise the requested actual page transport");
        Check(surface.GetProperty("gdiUploads").GetUInt64() == 0, "Input-driven page presentation performs no GDI uploads");
        Check(producedFrames.Length > 1 && producedFrames.Any(frame => frame.GetProperty("inputSequence").GetUInt64() > 0),
            "Native input sequences survive transport to produced and painted page frames");
        Check(producedFrames.All(frame =>
            (frame.GetProperty("inputSequence").GetUInt64() == 0 ||
                frame.GetProperty("inputEvent").GetDouble() <= frame.GetProperty("inputDispatch").GetDouble() &&
                frame.GetProperty("inputDispatch").GetDouble() <= frame.GetProperty(asyncFrameIds.Contains(frame.GetProperty("frameId").GetUInt64()) ? "commit" : "renderStart").GetDouble()) &&
            (!asyncFrameIds.Contains(frame.GetProperty("frameId").GetUInt64()) ||
                frame.GetProperty("renderStart").GetDouble() == 0 && frame.GetProperty("renderEnd").GetDouble() == 0) &&
            frame.GetProperty("renderStart").GetDouble() <= frame.GetProperty("renderEnd").GetDouble() &&
            frame.GetProperty("renderEnd").GetDouble() <= frame.GetProperty("commit").GetDouble() &&
            (frame.GetProperty("gpuEnd").GetDouble() == 0 ||
                frame.GetProperty("commit").GetDouble() <= frame.GetProperty("gpuStart").GetDouble() &&
                frame.GetProperty("gpuStart").GetDouble() <= frame.GetProperty("gpuEnd").GetDouble() &&
                frame.GetProperty("gpuEnd").GetDouble() <= frame.GetProperty("receive").GetDouble()) &&
            frame.GetProperty("commit").GetDouble() <= frame.GetProperty("receive").GetDouble() &&
            frame.GetProperty("receive").GetDouble() <= frame.GetProperty("paintStart").GetDouble() &&
            frame.GetProperty("paintStart").GetDouble() <= frame.GetProperty("paintEnd").GetDouble()),
            "Main and asynchronous frames retain their actual stage order; cached paints have no new main render cost");
        var scripts = new[] { "touch-input.js", "browser-observer.js" }.Select(name => new {
            name, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", name))))
        });
        foreach (var script in scripts)
            if (!File.Exists(Path.Combine(Program.Output, script.name)))
                File.Copy(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", script.name), Path.Combine(Program.Output, script.name));
        Save("touch-performance-" + (accelerated ? "gpu" : "cpu"), JsonSerializer.SerializeToElement(new { accelerated, touch, mouse, dragMilliseconds=timer.Elapsed.TotalMilliseconds,
            touchState, mouseState=State(), scripts, caveat="JS frame cadence in a local fixture, not DWM FPS or arbitrary-site acceptance" }));
        Console.WriteLine($"PASS touch performance: {assertions} assertions; touch={touch.GetRawText()}; mouse={mouse.GetRawText()}; evidence={Program.Output}");
    }
}
