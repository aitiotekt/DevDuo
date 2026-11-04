using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckNativeTouchLifecycle()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Native touch lifecycle runtime");
        Check(WindowsWebKitApi.SetHostTouchEnabled is not null, "Lifecycle checks require the native contact API");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Scale, 1);
        Select(Settings.Interaction, 0);
        var fixture = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/native-touch.html");
        File.WriteAllText(Path.Combine(Program.Output, "native-touch.html"), fixture);
        Web.WindowsPanel!.ShowHtml(fixture);
        await WaitForScript("typeof nativeTouchResult", value => value == "function", "Lifecycle fixture");
        var browser = Web.WindowsPanel.Browser;
        SetPageCompositing(browser, true);
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(150);
        Check(PresentationGetForegroundWindow() == owner, "Lifecycle checks require the independent foreground window");

        var failures = new List<string>();
        void Verify(bool condition, string message)
        {
            if (condition) assertions++;
            else failures.Add(message);
        }
        async Task Prepare()
        {
            Select(Settings.Interaction, 1);
            Select(Settings.Interaction, 0);
            await Web.EvaluateResultAsync("document.body.style.height='6000px';configureTouchFixture('auto');scrollTo(0,1200);'positioned'");
            await Task.Delay(250);
        }

        async Task BeginPan()
        {
            await InteractionPointer(0x201, 240, 400, 20);
            await InteractionPointer(0x200, 240, 340, 20);
            await Wait(state => state.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("panning").GetBoolean(),
                "The native contact acquires a pan");
        }

        async Task<double> Root() => (await NativeTouchResult()).GetProperty("scrollY").GetDouble();

        async Task Stopped(string name)
        {
            await Task.Delay(150);
            var first = await Root();
            await Task.Delay(220);
            var result = await NativeTouchResult();
            var host = State();
            Save("native-touch-lifecycle-" + name, JsonSerializer.SerializeToElement(new { first, result, host }));
            Verify(Math.Abs(result.GetProperty("scrollY").GetDouble() - first) < .05, name + " stops native scroll motion");
            Verify(!host.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("active").GetBoolean(), name + " retires the native contact");
            Verify(result.GetProperty("events").EnumerateArray().All(e => !Text(e, "type", "wheel")), name + " does not dispatch a DOM wheel");
        }

        await Prepare();
        await BeginPan();
        try
        {
            foreach (var y in new[] { 290, 240, 190 }) await InteractionPointer(0x200, 240, y, 20);
        }
        finally { await InteractionPointer(0x202, 240, 190, 20); }
        var released = await Root();
        await Task.Delay(120);
        var decelerating = await Root();
        Save("native-touch-inertia", JsonSerializer.SerializeToElement(new { released, decelerating, host = State() }));
        Verify(decelerating > released + 1, "A real released flick continues through native kinetic scrolling");
        await InteractionPointer(0x201, 240, 320, 20);
        try
        {
            var held = await Root();
            var tree = await browser.ReadNativeScrollingStateAsync();
            await Task.Delay(180);
            var afterHold = await Root();
            Save("native-touch-inertia-stop", JsonSerializer.SerializeToElement(new { held, afterHold, host = State() }));
            File.WriteAllText(Path.Combine(Program.Output, "native-touch-inertia-stop-tree.txt"), tree + "\nAFTER HOLD\n" + await browser.ReadNativeScrollingStateAsync());
            Verify(Math.Abs(afterHold - held) < .05, "The next stationary contact stops previous inertia");
        }
        finally { await InteractionPointer(0x202, 240, 320, 20); }

        await Prepare();
        await Web.EvaluateResultAsync("configureTouchFixture('nested');document.getElementById('nested').scrollTop=200;'nested'");
        await Task.Delay(250);
        File.WriteAllText(Path.Combine(Program.Output, "native-touch-nested-before-tree.txt"), await browser.ReadNativeScrollingStateAsync());
        await InteractionPointer(0x201, 120, 340, 20);
        try
        {
            foreach (var y in new[] { 300, 260, 220, 180 }) await InteractionPointer(0x200, 120, y, 20);
        }
        finally { await InteractionPointer(0x202, 120, 180, 20); }
        var nestedReleased = (await NativeTouchResult()).GetProperty("nestedY").GetDouble();
        await Task.Delay(120);
        var nestedResult = await NativeTouchResult();
        var nestedMoving = nestedResult.GetProperty("nestedY").GetDouble();
        Save("native-touch-nested-inertia", JsonSerializer.SerializeToElement(new { nestedReleased, nestedMoving, result = nestedResult, host = State() }));
        File.WriteAllText(Path.Combine(Program.Output, "native-touch-nested-inertia-tree.txt"), await browser.ReadNativeScrollingStateAsync());
        Verify(nestedMoving > nestedReleased + 1, "Nested overflow scrolling has native inertia");
        // Touch outside the nested panel: cancellation must use the old pan's
        // hit chain, rather than the new contact location or only the root.
        await InteractionPointer(0x201, 240, 80, 20);
        try
        {
            var held = await NativeTouchResult();
            await Task.Delay(180);
            var afterHold = await NativeTouchResult();
            Save("native-touch-nested-inertia-stop", JsonSerializer.SerializeToElement(new { held, afterHold, host = State() }));
            Verify(Math.Abs(afterHold.GetProperty("nestedY").GetDouble() - held.GetProperty("nestedY").GetDouble()) < .05,
                "A new outside contact stops inertia on the preceding nested hit chain");
            Verify(afterHold.GetProperty("scrollY").GetDouble() == held.GetProperty("scrollY").GetDouble(),
                "Stopping nested inertia does not move the root");
        }
        finally { await InteractionPointer(0x202, 240, 80, 20); }

        Select(Settings.Interaction, 1);
        await Web.EvaluateResultAsync("document.getElementById('nested').scrollTop=200;'nested positioned'");
        await Task.Delay(250);
        var beforeWheel = await NativeTouchResult();
        // The existing native wheel helper targets the center of this Closed
        // scene, inside the fixed nested panel at its nonzero scroll offset.
        InteractionWheel();
        await Task.Delay(400);
        var afterWheel = await NativeTouchResult();
        Save("native-touch-nested-wheel", JsonSerializer.SerializeToElement(new { beforeWheel, afterWheel, host = State() }));
        Verify(afterWheel.GetProperty("nestedY").GetDouble() > beforeWheel.GetProperty("nestedY").GetDouble(),
            "Ordinary mouse wheel hit testing preserves a scrolled nested target");
        Verify(afterWheel.GetProperty("scrollY").GetDouble() == beforeWheel.GetProperty("scrollY").GetDouble(),
            "Nested mouse wheel input does not scroll the root");
        Verify(afterWheel.GetProperty("events").EnumerateArray().Any(e => Text(e, "type", "wheel") && e.GetProperty("trusted").GetBoolean()),
            "Ordinary wheel retains trusted DOM delivery after touch mode");

        await Prepare();
        await BeginPan();
        try
        {
            Select(Settings.Interaction, 1);
            await WaitForScript("String(navigator.maxTouchPoints)", value => value == "0", "Mode capability restoration");
        }
        finally { await InteractionPointer(0x202, 240, 340, 20); }
        await Stopped("mode-change");

        await Prepare();
        await BeginPan();
        try { PresentationSetFocus(owner); }
        finally { await InteractionPointer(0x202, 240, 340, 20); }
        await Stopped("focus-loss");

        await Prepare();
        await BeginPan();
        var beforeChromeRelease = State();
        try
        {
            var back = browser.Chrome.First(item => item.Action == WindowsComposition.Action.Back);
            var hit = new Rectangle((int)(back.X + back.Width / 2), (int)(back.Y + back.Height / 2), 1, 1);
            System.Windows.Forms.Cursor.Position = browser.PresentationBounds!(hit).Location;
            await Task.Delay(250);
        }
        finally { await InteractionPointer(0x202, 240, 340, 20); }
        var afterChromeRelease = State();
        Verify(!afterChromeRelease.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("active").GetBoolean(),
            "Releasing a page drag over Safari chrome ends the native contact");
        Verify(afterChromeRelease.GetProperty("lastChromeAction").GetRawText() == beforeChromeRelease.GetProperty("lastChromeAction").GetRawText(),
            "A page drag released over chrome does not activate that control");
        await Task.Delay(150);
        var hoverOrigin = await Root();
        await InteractionPointer(0x200, 240, 220, 20);
        var hoverEnd = await Root();
        Save("native-touch-release-over-chrome", JsonSerializer.SerializeToElement(new { beforeChromeRelease, afterChromeRelease, hoverOrigin, hoverEnd, host = State() }));
        Verify(Math.Abs(hoverEnd - hoverOrigin) < .05, "Moving with no button pressed after a chrome release does not drag the page");

        await Prepare();
        await BeginPan();
        try
        {
            // Model a lost button-up followed by the first Windows mouse move
            // reporting no held button. Do not send WM_LBUTTONUP before it.
            var x = (int)Math.Round(240 * browser.BackingScale);
            var y = (int)Math.Round(300 * browser.BackingScale);
            PresentationSendMessage(browser.BrowserWindow, 0x200, 0, (nint)((x & 0xffff) | ((y & 0xffff) << 16)));
            await Task.Delay(100);
            var recovered = State();
            Save("native-touch-lost-release", recovered);
            Verify(!recovered.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("active").GetBoolean(),
                "A buttonless move cancels an orphaned native contact");
        }
        finally { await InteractionPointer(0x202, 240, 340, 20); }

        var bookmarks = browser.Chrome.First(item => item.Action == WindowsComposition.Action.Bookmarks);
        var bookmarkX = (bookmarks.X + bookmarks.Width / 2) / browser.BackingScale;
        var bookmarkY = (bookmarks.Y + bookmarks.Height / 2) / browser.BackingScale;
        var contactsBeforeChrome = State().GetProperty("webKit").GetProperty("nativeTouch").GetProperty("contacts").GetUInt64();
        await InteractionPointer(0x201, bookmarkX, bookmarkY, 20);
        await InteractionPointer(0x202, bookmarkX, bookmarkY, 20);
        var bookmarkRelease = State();
        Verify(Text(bookmarkRelease, "lastChromeAction", "Bookmarks"), "A matching chrome press and release still activates its control");
        Verify(bookmarkRelease.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("contacts").GetUInt64() == contactsBeforeChrome,
            "A chrome click does not start a native page contact");

        await Prepare();
        await BeginPan();
        try
        {
            await Web.EvaluateResultAsync("configureTouchFixture('locked');'locked'");
            await Task.Delay(250);
            var locked = await Root();
            await InteractionPointer(0x200, 240, 220, 20);
            var result = await NativeTouchResult();
            Save("native-touch-live-overflow-lock", JsonSerializer.SerializeToElement(new { locked, result, host = State() }));
            Verify(Math.Abs(result.GetProperty("scrollY").GetDouble() - locked) < .05,
                "A committed overflow lock blocks further motion within an existing native pan");
        }
        finally { await InteractionPointer(0x202, 240, 220, 20); }

        await Prepare();
        await BeginPan();
        try
        {
            await Web.EvaluateResultAsync("document.body.style.height='1600px';'shortened'");
            await Task.Delay(250);
            await InteractionPointer(0x200, 240, 200, 20);
            var result = await NativeTouchResult();
            Save("native-touch-document-shrink", JsonSerializer.SerializeToElement(new { result, host = State() }));
            Verify(result.GetProperty("scrollY").GetDouble() <= result.GetProperty("range").GetDouble() + .05,
                "Document shortening clamps the actual native pan to the new range");
            Verify(Math.Abs(result.GetProperty("range").GetDouble() + result.GetProperty("viewport")[1].GetDouble() - 1600) < .05,
                "Dynamic content height retains genuine viewport and scroll range");
        }
        finally { await InteractionPointer(0x202, 240, 200, 20); }

        await Prepare();
        await BeginPan();
        var oldDocument = State().GetProperty("pageDocumentId").GetString();
        try
        {
            Web.WindowsPanel.ShowHtml(fixture);
            await Wait(state => state.GetProperty("pageDocumentId").GetString() != oldDocument,
                "A replacement native document commits");
            await WaitForScript("typeof nativeTouchResult", value => value == "function", "Replacement fixture");
        }
        finally { await InteractionPointer(0x202, 240, 340, 20); }
        await Stopped("navigation");
        var replacement = await NativeTouchResult();
        Verify(replacement.GetProperty("scrollY").GetDouble() == 0, "Old pan positions do not scroll the replacement document");
        Verify(replacement.GetProperty("clicks").GetInt32() == 0, "The old contact cannot activate the replacement page");
        await NativeTouchDrag(240, 360, 220);
        Verify(await Root() > 100, "The replacement document accepts a new native gesture");
        Save("native-touch-lifecycle-result", JsonSerializer.SerializeToElement(new { failures, host = State() }));
        Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine($"PASS native touch lifecycle: {assertions} assertions; kinetic stop, mode/focus/navigation and live scroll constraints: {Program.Output}");
    }
}
