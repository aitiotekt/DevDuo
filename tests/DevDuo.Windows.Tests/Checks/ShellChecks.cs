using System.Text.Json;
using System.Text.RegularExpressions;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private void CheckShellStatePolicy()
    {
        var policy = new SafariChromeState();
        Check(!policy.Observe(false, 480, 480, false, 1000, 24), "Script scrolling alone does not compact chrome.");
        policy.RecordInput(1, 1000, "native wheel");
        Check(policy.Observe(false, 510, 30, false, 1010, 24), "Native downward motion compacts chrome.");
        Check(policy.Observe(true, 492, -18, true, 1020, 24), "Viewport growth clamping is not a gesture.");
        policy.RecordInput(-1, 1030, "native wheel");
        Check(!policy.Observe(true, 509, -9, false, 1040, 24), "Reverse native motion expands at nonzero Y.");
    }

    private void CheckShellWidgets(JsonElement state, bool compact)
    {
        var side = Text(state, "chromeLayout", "side");
        var scene = state.GetProperty("requestedSceneCssPx");
        var width = scene.GetProperty("width").GetDouble();
        var height = scene.GetProperty("height").GetDouble();
        var portrait = width <= height;
        var status = state.GetProperty("statusWidgets");
        Check(status.GetProperty("cameraVisible").GetBoolean() == side, "The camera follows Closed/Open visibility.");
        var showStatus = side ? portrait : !compact;
        Check(status.GetProperty("wifiVisible").GetBoolean() == showStatus && status.GetProperty("clockVisible").GetBoolean() == showStatus,
            "Clock and Wi-Fi follow side/orientation/compact visibility.");
        Check(Regex.IsMatch(status.GetProperty("clockText").GetString() ?? "", @"^\d{2}:\d{2}$"), "Clock uses local HH:mm.");
        var browser = Web.WindowsPanel!.Browser;
        var elements = browser.Chrome;
        foreach (var element in elements.Where(element => element.Action != WindowsComposition.Action.None))
        {
            var hit = browser.HitChrome((int)(element.X + element.Width / 2), (int)(element.Y + element.Height / 2));
            Check(hit == element.Action, "Native hit testing preserves the independent " + element.Kind + " action.");
        }
        foreach (var element in elements.Where(element => element.Kind is WindowsComposition.Kind.Camera or WindowsComposition.Kind.Clock or WindowsComposition.Kind.Wifi))
        {
            Check(element.Action == WindowsComposition.Action.None, "Status artwork is passive.");
            Check(browser.HitChrome((int)(element.X + element.Width / 2), (int)(element.Y + element.Height / 2)) == WindowsComposition.Action.None,
                "Status artwork does not capture page input.");
        }
        if (side)
        {
            var camera = elements.Single(element => element.Kind == WindowsComposition.Kind.Camera);
            var scale = browser.BackingScale;
            var left = Text(state, "sidebarSide", "left");
            var centerX = left ? 48 : width - 48;
            var centerY = !portrait && !left ? height - 48 : 48;
            Check(Math.Abs((camera.X + camera.Width / 2) / scale - centerX) <= 1 / scale &&
                Math.Abs((camera.Y + camera.Height / 2) / scale - centerY) <= 1 / scale,
                "The camera uses the existing macOS scene position.");
            Check(Math.Abs(camera.Width / scale - 36) <= 1 / scale, "Camera diameter remains a scene-unit illustration.");
        }
        Check(elements.Any(element => element.Kind == WindowsComposition.Kind.Menu) == !compact &&
            elements.Any(element => element.Kind == WindowsComposition.Kind.Reload) == !compact, "Compact address hides menu/reload glyphs.");
        Check(state.GetProperty("webKit").GetProperty("composition").GetProperty("apiVersion").GetInt32() == 3,
            "Native shell uses composition API v3.");
        Check(state.GetProperty("webKit").GetProperty("contentColorApiVersion").GetInt32() == 1, "Native edge color capability is versioned separately.");
    }

    private async Task CheckShellActions()
    {
        Select(Settings.Fold, 0);
        await Ready();
        foreach (var action in new[] { WindowsComposition.Action.Address, WindowsComposition.Action.Bookmarks, WindowsComposition.Action.Overview })
        {
            Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.Menu);
            Web.Evaluate("scrollTo(0,0);window.__devduoReportContainerObservation()");
            await Wait(state => state.GetProperty("scrollY").GetDouble() <= 2, "Top before " + action);
            await ScrollNativeUntil(true);
            Web.Evaluate("scrollTo(0,480);window.__devduoReportContainerObservation()");
            await Wait(state => Text(state, "chromeState", "compact") && state.GetProperty("scrollY").GetDouble() == 480,
                "Observed target scroll before " + action);
            var document = State().GetProperty("pageDocumentId").GetString();
            Web.WindowsPanel!.Browser.InvokeChrome(action);
            var expanded = await Wait(state => Text(state, "chromeState", "expanded"), "Expand via " + action);
            Check(expanded.GetProperty("pageDocumentId").GetString() == document, action + " must not navigate the document.");
            Check(expanded.GetProperty("scrollY").GetDouble() == 480, action + " must not return to the top.");
        }
        var beforeMenu = State().GetProperty("pageDocumentId").GetString();
        Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.Menu);
        await Task.Delay(150);
        Check(State().GetProperty("pageDocumentId").GetString() == beforeMenu, "Menu expands without reloading.");
        Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.NewTab);
        await Wait(state => state.GetProperty("scrollY").GetDouble() <= 2, "New-tab illustration returns to the top");
        var browser = Web.WindowsPanel!.Browser;
        var shortWidth = browser.MeasureText("a.test", 12);
        var longWidth = browser.MeasureText("a-longer-hostname.example", 12);
        Check(shortWidth > 0 && longWidth > shortWidth, "DirectWrite measures the same font used for shell text.");
        Save("shell-actions", State());
    }

    private async Task CheckFractionalSmoothScroll()
    {
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/static-gradient.html"));
        await Wait(state => Text(state, "pageTitle", "Static gradient painting acceptance"), "Marker-free smooth-scroll page");
        var document = State().GetProperty("pageDocumentId").GetString();
        Select(Settings.Scale, 1);
        window.MinHeight = 400;
        foreach (var height in new[] { 874, 740, 564 })
        {
            window.Height = height;
            await Wait(state => state.GetProperty("webKit").GetProperty("backingScaleFactor").GetDouble() > 0,
                "Fractional presentation settles");
            await ScrollNativeUntil(true);
            Web.Evaluate("scrollTo({top:510,behavior:'smooth'})");
            await Wait(state => Math.Abs(state.GetProperty("scrollY").GetDouble() - 510) < 1 && Text(state, "chromeState", "compact"),
                "Smooth downward scroll settles");
            Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.NewTab);
            var restored = await Wait(state => state.GetProperty("scrollY").GetDouble() == 0 && Text(state, "chromeState", "expanded"),
                "Fractional smooth return to top settles");
            var viewport = restored.GetProperty("actualPageViewport");
            Check(viewport.GetProperty("width").GetDouble() == 382 && viewport.GetProperty("height").GetDouble() == 580,
                "Fractional smooth scrolling retains the measured real viewport.");
            Check(restored.GetProperty("pageDocumentId").GetString() == document, "Smooth scrolling never replaces the document.");
            var geometry = await Web.EvaluateResultAsync("JSON.stringify({height:document.scrollingElement.scrollHeight,range:document.scrollingElement.scrollHeight-innerHeight,positioned:[...document.querySelectorAll('*')].filter(e=>['fixed','sticky'].includes(getComputedStyle(e).position)).length})");
            using var actual = JsonDocument.Parse(geometry!);
            Check(actual.RootElement.GetProperty("height").GetInt32() == 2400 && actual.RootElement.GetProperty("range").GetInt32() == 1820,
                "Smooth scrolling preserves authored document height and scroll range.");
            Check(actual.RootElement.GetProperty("positioned").GetInt32() == 0, "The scroll painting fixture has no fixed/sticky markers.");
            Save("fractional-smooth-scroll-" + height, restored);
        }
        window.Height = 970;
        await Ready();
    }

    private async Task CheckContentColor()
    {
        Select(Settings.Fold, 0);
        await Ready();
        var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/content-color.html");
        Web.WindowsPanel!.ShowHtml(html);
        var solid = await WaitColor(36, 104, 172, "solid");
        Check(solid.GetProperty("contentColor").GetProperty("sampleRectSceneCssPx").GetProperty("height").GetInt32() == 18,
            "Bottom color sampling stays within the real unobscured viewport.");
        var positioned = await Web.EvaluateResultAsync("String([...document.querySelectorAll('*')].filter(e=>['fixed','sticky'].includes(getComputedStyle(e).position)).length)");
        Check(positioned == "0", "Color acceptance page has no fixed/sticky test markers.");
        Web.Evaluate("scrollTo(0,900);window.__devduoReportContainerObservation()");
        await WaitColor(64, 96, 128, "gradient");
        Web.Evaluate("scrollTo(0,1700);window.__devduoReportContainerObservation()");
        await WaitColor(64, 160, 208, "svg-image");
        Web.Evaluate("document.documentElement.style.background='transparent';scrollTo(0,2500);window.__devduoReportContainerObservation()");
        await WaitColor(255, 255, 255, "transparent-canvas");

        var previous = Web.WindowsPanel!.Browser.RequestContentEdgeColorAsync(true);
        Web.WindowsPanel.ShowHtml("<!doctype html><meta name='theme-color' content='#11bb77'><title>New color generation</title><body style='margin:0;height:2400px;background:#df2040'>New document");
        await Wait(state => Text(state, "tintSource", "WebKit native edge average") && state.GetProperty("tintRgb")[0].GetInt32() == 223,
            "New document backdrop wins over an outstanding old sample");
        await previous;
        await Task.Delay(250);
        Check(State().GetProperty("tintRgb")[1].GetInt32() == 32, "Late native samples cannot replace the new document backdrop.");
        Save("content-color-new-generation", State());

        Select(Settings.Fold, 1);
        await Ready();
        Web.WindowsPanel!.ShowHtml(html);
        var top = await WaitColor(255, 255, 255, "open-top");
        Check(top.GetProperty("contentColor").GetProperty("sampleRectSceneCssPx").GetProperty("y").GetInt32() > 0,
            "Open sampling skips the real obscured top inset.");
        Web.Evaluate("scrollTo(0,900);window.__devduoReportContainerObservation()");
        await WaitColor(64, 96, 128, "open-gradient");

        Settings.Rotate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Ready();
        Web.WindowsPanel!.ShowHtml(html);
        await WaitColor(255, 255, 255, "open-landscape-top");
        Web.Evaluate("scrollTo(0,900);window.__devduoReportContainerObservation()");
        await WaitColor(64, 96, 128, "open-landscape-gradient");

        var invalid = new TaskCompletionSource<uint>();
        WindowsWebKitApi.ContentColorCallback callback = (_, status, _, _) => invalid.TrySetResult(status);
        WindowsWebKitApi.WKViewRequestContentEdgeColor(0, -1, 0, 1, 1, 1, 0, callback);
        Check(await invalid.Task == 1, "Invalid native sample input fails explicitly.");
        GC.KeepAlive(callback);
        Select(Settings.Fold, 0);
        await Ready();
    }

    private async Task<JsonElement> WaitColor(int red, int green, int blue, string name)
    {
        var expected = new[] { red, green, blue };
        var result = await Wait(state => Text(state, "tintSource", "WebKit native edge average") &&
            state.GetProperty("tintRgb").EnumerateArray().Select(value => value.GetInt32()).Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 3),
            "Native rendered edge color: " + name);
        Check(Text(result.GetProperty("contentColor"), "status", "accepted"), "Native color result is accepted: " + name);
        Save("content-color-" + name, result);
        return result;
    }
}
