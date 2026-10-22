using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks(MainWindow window)
{
    private int assertions;
    private HostSettingsPanel Settings => window.GetVisualDescendants().OfType<HostSettingsPanel>().Single();
    private static void Select(ChoiceGroup group, int index) => group.Children.OfType<RadioButton>().ElementAt(index).IsChecked = true;
    private NativeWebView Web => window.GetVisualDescendants().OfType<NativeWebView>().Single();
    private JsonElement State()
    {
        using var document = JsonDocument.Parse(Web.ReadState());
        return document.RootElement.Clone();
    }
    private void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        assertions++;
    }
    private async Task<JsonElement> Wait(Func<JsonElement, bool> accepted, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        JsonElement state = default;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            state = State();
            if (accepted(state))
                return state;
        }
        throw new TimeoutException(description + ": " + state);
    }
    private static bool Text(JsonElement state, string key, string value) =>
        state.TryGetProperty(key, out var property) && property.GetString() == value;
    private void Save(string name, JsonElement state) => File.WriteAllText(
        Path.Combine(Program.Output, name + ".json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    private async Task Ready()
    {
        var state = await Wait(state => state.TryGetProperty("initializationState", out var value) && value.GetString() != "initializing", "Windows WebKit initialization");
        Check(Text(state, "initializationState", "ready"), "Windows WebKit runtime must initialize: " + state);
        var previousDocument = state.TryGetProperty("pageDocumentId", out var document) ? document.GetString() : null;
        window.GetVisualDescendants().OfType<HostSettingsPanel>().Single().ColorDemo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => Text(state, "tintSource", "WebKit native edge average") &&
            state.GetProperty("pageDocumentId").GetString() != previousDocument &&
            state.GetProperty("actualPageViewport").GetProperty("width").GetDouble() > 0, "Fixture observation");
        await Web.EvaluateResultAsync("if(!document.getElementById('native-fixed-check')){const marker=document.createElement('div');marker.id='native-fixed-check';marker.textContent='FIXED';marker.style.cssText='position:fixed;left:0;bottom:0;width:180px;height:20px;background:#ff00ff';document.body.append(marker);for(const unit of ['svh','lvh','dvh','vh']){const node=document.createElement('div');node.dataset.unit=unit;node.style.cssText='position:absolute;top:0;width:0;pointer-events:none;height:100'+unit;document.body.append(node)}};window.__devduoReportContainerObservation();'ready'");
    }
    private async Task Viewport(string name, double width, double height, bool compact, double tolerance = 0)
    {
        var state = await Wait(state =>
        {
            if (!state.TryGetProperty("actualPageViewport", out var viewport))
                return false;
            return Math.Abs(viewport.GetProperty("width").GetDouble() - width) <= tolerance &&
                Math.Abs(viewport.GetProperty("height").GetDouble() - height) <= tolerance &&
                Text(state, "chromeState", compact ? "compact" : "expanded") &&
                state.GetProperty("nativeScreenMask").GetProperty("holderCorners").EnumerateArray().All(point => !point.GetBoolean()) &&
                state.GetProperty("nativeScreenMask").GetProperty("childCorners").EnumerateArray().All(point => !point.GetBoolean());
        }, name + " viewport " + width + "x" + height);
        Check(state.GetProperty("platform").GetProperty("deviceDpi").GetInt32() >= 96, "Record real Windows DPI.");
        Check(state.GetProperty("pageDevicePixelRatio").GetDouble() > 0, "Record actual browser DPR.");
        var viewport = state.GetProperty("actualPageViewport");
        CheckShellWidgets(state, compact);
        var presentation = state.GetProperty("presentation");
        var body = state.GetProperty("bodyRotationDegrees").GetInt32();
        var isClosed = state.GetProperty("referenceScene").GetString()!.StartsWith("closed", StringComparison.Ordinal);
        var expectedTurn = isClosed && body == 180 ? 90 : 0;
        Check(presentation.GetProperty("bodyInCanvasDegrees").GetInt32() == body &&
            presentation.GetProperty("sceneInCanvasDegrees").GetInt32() == expectedTurn,
            "Body/canvas and scene/body transforms remain independent.");
        var screenDip = presentation.GetProperty("screenFrameDip");
        var sceneDip = presentation.GetProperty("sceneFrameDip");
        Check(Math.Abs(screenDip.GetProperty("width").GetDouble() - sceneDip.GetProperty(expectedTurn % 180 == 0 ? "width" : "height").GetDouble()) < .001,
            "Presented physical screen extent follows the body, not the layout scene.");
        var sourceWindow = state.GetProperty("webKit").GetProperty("composition").GetProperty("sourceWindow");
        Check(sourceWindow.GetProperty("detached").GetBoolean() == (expectedTurn != 0), "Only rotated scenes need an independent full-size native source.");
        if (expectedTurn != 0)
        {
            Check(!sourceWindow.GetProperty("parentHasRedirectionBitmap").GetBoolean(), "The source parent contributes no desktop pixels.");
            var physicalScene = state.GetProperty("nativeWebViewFramePixels");
            Check(sourceWindow.GetProperty("parentClientWidth").GetInt32() == physicalScene.GetProperty("width").GetInt32() &&
                sourceWindow.GetProperty("parentClientHeight").GetInt32() == physicalScene.GetProperty("height").GetInt32(),
                "Ancestor clipping must not discard any source raster before rotation.");
        }
        Check(Math.Abs(viewport.GetProperty("width").GetDouble() - viewport.GetProperty("clientWidth").GetDouble()) <= tolerance,
            "Desktop scrollbars must not reserve CSS width.");
        Check(Math.Abs(viewport.GetProperty("width").GetDouble() - viewport.GetProperty("visualWidth").GetDouble()) <= Math.Max(1, tolerance),
            "Visual viewport must not lose width to a scrollbar gutter.");
        var mask = state.GetProperty("nativeScreenMask");
        Check(mask.GetProperty("holderRegionType").GetInt32() > 1 && mask.GetProperty("childRegionType").GetInt32() > 1,
            "Both hosted HWND and Avalonia holder require native screen clipping.");
        Check(mask.GetProperty("holderCorners").EnumerateArray().All(point => !point.GetBoolean()) &&
            mask.GetProperty("childCorners").EnumerateArray().All(point => !point.GetBoolean()),
            "All outer corner pixels must remain outside both native clip regions.");
        Check(state.GetProperty("underChromeContentContinuation").GetBoolean(), "Continuation must describe the active backend.");
        var json = await Web.EvaluateResultAsync("JSON.stringify({inner:[innerWidth,innerHeight],documentHeight:document.scrollingElement.scrollHeight,range:document.scrollingElement.scrollHeight-document.scrollingElement.clientHeight,fixed:[...document.querySelectorAll('*')].filter(e=>getComputedStyle(e).position==='fixed').map(e=>{const r=e.getBoundingClientRect();return [r.left,r.top,r.right,r.bottom]}),units:Object.fromEntries([...document.querySelectorAll('[data-unit]')].map(e=>[e.dataset.unit,e.getBoundingClientRect().height]))})");
        using var actual = JsonDocument.Parse(json ?? throw new InvalidOperationException("WebKit DOM diagnostics missing."));
        Check(actual.RootElement.GetProperty("inner")[0].GetDouble() == width && actual.RootElement.GetProperty("inner")[1].GetDouble() == height,
            "WebKit must retain exact real dimensions, including fractional Fit.");
        var footers = actual.RootElement.GetProperty("fixed").EnumerateArray().ToArray();
        Check(footers.Length > 0 && footers.Any(rect => Math.Abs(rect[3].GetDouble() - height) < .01), "The real fixed footer stays at the viewport bottom.");
        Check(!state.GetProperty("webKit").GetProperty("testOnlyIPCEnabled").GetBoolean(), "Testing IPC remains disabled in Avalonia.");
        var geometry = state.GetProperty("webKit").GetProperty("logicalGeometry");
        var units = actual.RootElement.GetProperty("units");
        Check(units.GetProperty("svh").GetDouble() == geometry.GetProperty("minimumHeight").GetDouble() &&
            units.GetProperty("lvh").GetDouble() == geometry.GetProperty("maximumHeight").GetDouble() &&
            units.GetProperty("vh").GetDouble() == geometry.GetProperty("maximumHeight").GetDouble() &&
            units.GetProperty("dvh").GetDouble() == height, "Real small/large/dynamic viewport units follow the scene through Fit.");
        Check(actual.RootElement.GetProperty("range").GetDouble() == Math.Max(0, actual.RootElement.GetProperty("documentHeight").GetDouble() - height),
            "The real scroll range excludes the obscured drawing area.");
        File.WriteAllText(Path.Combine(Program.Output, name + "-dom.json"), json);
        var frame = state.GetProperty("nativeWebViewFramePixels");
        var resources = state.GetProperty("webKit").GetProperty("composition").GetProperty("resources");
        Check(resources.GetProperty("width").GetDouble() == frame.GetProperty("width").GetDouble() &&
            resources.GetProperty("height").GetDouble() == frame.GetProperty("height").GetDouble(), "Native composition uses actual physical scene bounds.");
        Check(resources.GetProperty("resourceGeneration").GetUInt64() > 0 && resources.GetProperty("sceneGeneration").GetUInt64() > 0,
            "Native composition resources and submitted scenes have independent generations.");
        var dip = state.GetProperty("nativeWebViewFrameDip");
        var dpi = state.GetProperty("platform").GetProperty("deviceDpi").GetDouble() / 96;
        Check(Math.Abs(frame.GetProperty("width").GetDouble() / dpi - dip.GetProperty("width").GetDouble()) < .001,
            "Framebuffer pixels and DIP must be distinct.");
        Save(name, state);
        Console.WriteLine("PASS " + name + ": " + state.GetProperty("actualPageViewport"));
    }
    private void Rotate() => Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
