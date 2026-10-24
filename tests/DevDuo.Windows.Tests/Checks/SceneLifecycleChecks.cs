using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    internal async Task Run()
    {
        if (Environment.GetCommandLineArgs().Contains("--interaction"))
        {
            await CheckInteraction();
            return;
        }
        // Existing wheel-driven scene/paint scenarios explicitly use desktop input.
        Select(Settings.Interaction, 1);
        if (Environment.GetCommandLineArgs().Contains("--scroll-indicators"))
        {
            await CheckScrollIndicators();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--fixed-overlay"))
        {
            await CheckFixedOverlay();
            return;
        }
        if (Program.NavigationLatency)
        {
            await CheckNavigationLatency();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--browser-identity"))
        {
            await CheckBrowserIdentity();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--presentation-insets"))
        {
            await CheckInsetPresentationOnly();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--presentation-damage"))
        {
            await CheckPresentationDamage();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--developer-tools"))
        {
            await CheckDeveloperTools();
            return;
        }
        if (Program.StorageMode is not null)
        {
            await CheckPersistentStorage(Program.StorageMode == "--storage-write");
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--browser-tools"))
        {
            await CheckBrowserTools();
            return;
        }
        if (Environment.GetCommandLineArgs().Contains("--browser-performance"))
        {
            await CheckLoadingPerformance();
            return;
        }
        if (Program.SafariReference)
        {
            await CheckSafariReferenceAlignment();
            return;
        }
        var runtimeOverride = Environment.GetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME");
        try
        {
            Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME", null);
            Check(WindowsWebKitApi.RuntimeDirectory() == Path.GetFullPath("temp/build/windows-webkit-host/bin"),
                "Default startup discovers the source-built checkout runtime without selecting a backend.");
            var missing = Path.Combine(Path.GetTempPath(), "devduo-missing-webkit-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME", missing);
            Check(WindowsWebKitApi.RuntimeDirectory() == missing, "An explicit missing runtime must remain authoritative.");
            Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME", "relative-runtime");
            try
            {
                WindowsWebKitApi.RuntimeDirectory();
                throw new InvalidOperationException("Relative runtime override was accepted.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains("must be an absolute", StringComparison.Ordinal))
            {
                Check(true, "Reject ambiguous relative runtime overrides.");
            }
        }
        finally { Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME", runtimeOverride); }
        if (Program.InitializationFailure)
        {
            var failed = await Wait(state => Text(state, "initializationState", "failed"), "Missing WebKit runtime must fail clearly");
            Check(Text(failed, "engine", "Windows WebKit"), "Runtime failure keeps the sole Windows backend.");
            Check(!string.IsNullOrWhiteSpace(failed.GetProperty("initializationError").GetString()), "Keep actionable initialization detail.");
            Save("initialization-failure", failed);
            Console.WriteLine("PASS initialization failure with an empty WebKit runtime folder; no alternative backend is selected.");
            return;
        }
        var device = Settings.Fold;
        await CheckBrowserTools();
        CheckShellStatePolicy();
        await CheckFractionalPageBoundaries();
        var sizing = Settings.Scale;
        var mode = Settings.Mode;
        var tabs = Settings.TabStrip;
        var address = window.GetVisualDescendants().OfType<HostAddressBar>().Single()
            .GetVisualDescendants().OfType<TextBox>().Single();
        // Exact CSS dimensions at 1:1; all 24 public target states, no private reference images.
        for (var fold = 0; fold < 2; fold++)
        {
            Select(device, fold);
            for (var angle = 0; angle < 4; angle++)
            {
                await Ready();
                var landscape = fold == 0 ? angle != 0 : angle % 2 == 0;
                var width = fold == 0 ? (landscape ? 594 : 382) : (landscape ? 951 : 669);
                var initialHeight = fold == 0 ? (landscape ? 368 : 580) : (landscape ? 543 : 825);
                var compactHeight = fold == 0 ? (landscape ? 386 : 598) : (landscape ? 618 : 900);
                foreach (var multiple in fold == 0 ? new[] { true } : new[] { true, false })
                {
                    tabs.IsChecked = multiple;
                    Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.Menu);
                    Web.Evaluate("scrollTo(0,0);window.__devduoReportContainerObservation()");
                    var name = (fold == 0 ? "closed" : "open") + "-" + angle * 90 + (multiple ? "-multiple" : "-single");
                    await Viewport(name + "-initial", width, initialHeight + (fold == 1 && !multiple ? 46 : 0), false);
                    await ScrollNativeUntil(true);
                    Web.Evaluate("scrollTo(0,480);window.__devduoReportContainerObservation()");
                    await Viewport(name + "-scrolled", width, compactHeight, true);
                }
                Rotate();
            }
        }
        // Fit, outline and language are presentation changes and retain the same document.
        Select(device, 0);
        await Ready();
        Check(address.Text == AppLocalization.Current.Text("Address.Fixture"),
            "Programmatic navigation must update the visible address without treating its own text event as user editing.");
        await ScrollNativeUntil(true);
        Web.Evaluate("scrollTo(0,1000000);window.__devduoReportContainerObservation()");
        await Viewport("document-end-compact", 382, 598, true);
        await Task.Delay(300);
        Check(Text(State(), "chromeState", "compact"), "Viewport growth clamping at document end must not expand chrome again.");
        Web.WindowsPanel!.Browser.InvokeChrome(WindowsComposition.Action.Menu);
        Web.Evaluate("scrollTo(0,0);window.__devduoReportContainerObservation()");
        await Viewport("document-returned-top", 382, 580, false);
        await ScrollNativeUntil(true);
        Web.Evaluate("scrollTo(0,480);document.title='windows-preserved-document';window.__devduoReportContainerObservation()");
        await Viewport("preservation-before", 382, 598, true);
        var original = Web;
        var before = State();
        Select(sizing, 1);
        await Viewport("fit-closed", 382, 598, true);
        Settings.Outline.IsChecked = false;
        await Viewport("outline-off", 382, 598, true);
        var previousFrameWidth = State().GetProperty("nativeWebViewFramePixels").GetProperty("width").GetDouble();
        window.Width = 900;
        window.Height = 720;
        await Wait(state => state.GetProperty("nativeWebViewFramePixels").GetProperty("width").GetDouble() < previousFrameWidth,
            "Fit responds to available preview space");
        await Viewport("fit-resized", 382, 598, true);
        Check(ReferenceEquals(original, Web), "Window resizing keeps the native host.");
        window.Width = 1180;
        window.Height = 970;
        foreach (var language in new[] { 1, 2, 0 })
        {
            Settings.Language.SelectedIndex = language;
            var after = await Wait(state => Math.Abs(state.GetProperty("scrollY").GetDouble() - before.GetProperty("scrollY").GetDouble()) < 1,
                "Language and Fit settle at the preserved renderer-confirmed scroll position");
            Save("language-" + language, after);
            Check(address.Text == AppLocalization.Current.Text("Address.Fixture"), "The visible fixture address follows language changes.");
            Check(ReferenceEquals(original, Web), "Presentation changes must retain the native host.");
            Check(Text(after, "pageDocumentId", before.GetProperty("pageDocumentId").GetString()!), "Language keeps the document.");
            Check(Text(after, "pageUrl", before.GetProperty("pageUrl").GetString()!), "Language keeps the URL.");
            Check(Math.Abs(after.GetProperty("scrollY").GetDouble() - before.GetProperty("scrollY").GetDouble()) < 1,
                "Language and Fit keep scroll position: " + before.GetProperty("scrollY") + " -> " + after.GetProperty("scrollY"));
        }
        Select(mode, 1);
        var app = await Wait(state => Text(state, "chromeState", "absent") &&
            Math.Abs(state.GetProperty("actualPageViewport").GetProperty("height").GetDouble() - 678) <= 1,
            "Immersive mode clears reservations");
        Check(!ReferenceEquals(original, Web), "Container mode changes intentionally recreate the native host.");
        Save("app-fit", app);
        Select(mode, 0);
        await Ready();
        await ScrollNativeUntil(true);
        Web.Evaluate("scrollTo(0,480);window.__devduoReportContainerObservation()");
        await Viewport("safari-restored", 382, 598, true);
        // Meta hints remain observations while native sampling follows page content.
        Web.Evaluate("document.querySelector('meta[name=theme-color]')?.remove();window.__devduoReportContainerObservation()");
        var sample = await Wait(state => Text(state, "tintSource", "WebKit native edge average"), "Content color source");
        Save("content-sample", sample);
        Web.Evaluate("const meta=document.createElement('meta');meta.name='theme-color';meta.content='#24b6b3';document.head.append(meta);window.__devduoReportContainerObservation()");
        var theme = await Wait(state => Text(state, "tintSource", "WebKit native edge average") &&
            state.GetProperty("themeColorHintRgb").ValueKind == JsonValueKind.Array &&
            state.GetProperty("themeColorHintRgb")[0].GetInt32() == 36, "Theme hint is diagnostic only");
        Save("theme-priority", theme);
        await CheckShellActions();
        await CheckFractionalSmoothScroll();
        await CheckContentColor();
        // Remove a host while initialization or script callbacks can still be outstanding.
        for (var index = 0; index < 12; index++)
        {
            Select(device, index % 2);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        await Ready();
        await Task.Delay(500);
        var lastWeb = Web;
        window.Content = null;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Check(lastWeb.ReadState() == "{}", "Detached native hosts must release their resources.");
        Save("result", JsonSerializer.SerializeToElement(new
        {
            status = "passed",
            assertions,
            safariStates = 24,
            platform = theme.GetProperty("platform"),
            inputMethod = "programmatic host controls and scripts in real WebKit; keyboard/mouse acceptance separate"
        }));
        Console.WriteLine($"Windows runtime checks passed: {assertions} assertions. Evidence: {Program.Output}");
    }
}
