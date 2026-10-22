using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckNativeTabTitles()
    {
        using var server = new NativeTabTitleServer(AvailableDeveloperPort());
        Select(Settings.Mode, 0);
        Select(Settings.Fold, 1);
        Select(Settings.Scale, 1);
        Settings.TabStrip.IsChecked = true;
        await Wait(state => Text(state, "initializationState", "ready"), "Tab title browser initialization");
        Web.Navigate(server.Address);
        await WaitForNativeTabTitle("Initial title");
        var browser = Web.WindowsPanel!.Browser;
        var originalView = Web;
        await Web.EvaluateResultAsync("window.tabTitleSentinel='retained';scrollTo(0,240);'ready'");
        await WaitForScript("String(scrollY)", value => value == "240", "Title fixture scroll settled");
        const string fingerprintScript = "JSON.stringify([window.devduoShellObservation.documentId,performance.timeOrigin,location.href,scrollX,scrollY,innerWidth,innerHeight,visualViewport.width,visualViewport.height,window.tabTitleSentinel])";
        var fingerprint = await Web.EvaluateResultAsync(fingerprintScript);
        var frames = ReadTitleTabFrames(browser);
        var originalLanguage = Settings.Language.SelectedIndex;
        var longTitle = "A long English document title — 很长的中文页面标题 — 👩🏽‍💻 👨‍👩‍👧‍👦 e\u0301 — " +
            "Repeated content preserves the complete native and accessibility title while the painted label uses its original measured width. " +
            "这是用于确认原生字体宽度省略的长文本，完整标题必须保存在辅助功能名称中。";

        foreach (var (name, actual) in new[] { ("short", "Dynamic title"), ("long-unicode", longTitle), ("empty", "") })
        {
            await Web.EvaluateResultAsync("document.title=" + JsonSerializer.Serialize(actual) + ";'changed'");
            var expected = actual.Length == 0 ? AppLocalization.Current.Format("Shell.PageTab", 2) : actual;
            await WaitForNativeTabTitle(expected);
            Check(ReadTitleTabFrames(browser) == frames, "Dynamic title changes preserve every tab frame, font, radius and clipping width.");
            Check(await Web.EvaluateResultAsync(fingerprintScript) == fingerprint, "Dynamic title changes preserve the document, scroll and actual viewport.");
            if (name == "long-unicode") await Task.Delay(200);
            SaveNativeTabTitle(name, browser, expected, name == "long-unicode");
        }

        await Web.EvaluateResultAsync("document.title=" + JsonSerializer.Serialize(longTitle) + ";'changed'");
        await WaitForNativeTabTitle(longTitle);
        try
        {
            foreach (var language in new[] { 1, 2 })
            {
                Settings.Language.SelectedIndex = language;
                await WaitForNativeTabTitle(longTitle);
                Check(ReferenceEquals(originalView, Web) && ReferenceEquals(browser, Web.WindowsPanel!.Browser), "Language changes retain the native title container.");
                Check(ReadTitleTabFrames(browser) == frames, "Language changes retain the measured tab frames, font and clipping width.");
                Check(await Web.EvaluateResultAsync(fingerprintScript) == fingerprint, "Language changes retain document identity, scroll and actual viewport.");
                await Task.Delay(200);
                SaveNativeTabTitle("language-" + language, browser, longTitle, true);
            }
        }
        finally { Settings.Language.SelectedIndex = originalLanguage; }

        // Hold the next response so the provisional state is observable before
        // the replacement document can commit its own title.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationStarted() => started.TrySetResult();
        browser.DocumentNavigationStarted += NavigationStarted;
        try
        {
            Web.Navigate(server.Address + "next");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var fallback = AppLocalization.Current.Format("Shell.PageTab", 2);
            await WaitForNativeTabTitle(fallback);
            Check(ReadTitleTabFrames(browser) == frames, "A provisional navigation preserves the measured tab geometry.");
            SaveNativeTabTitle("provisional", browser, fallback, false);
        }
        finally
        {
            browser.DocumentNavigationStarted -= NavigationStarted;
            server.ReleaseNext();
        }
        await WaitForNativeTabTitle("Replacement document title");
        var replacement = await Web.EvaluateResultAsync(fingerprintScript);
        Check(replacement != fingerprint, "A replacement document establishes a new identity before supplying its title.");
        Check(ReadTitleTabFrames(browser) == frames, "A replacement title keeps the original tab geometry.");
        SaveNativeTabTitle("replacement", browser, "Replacement document title", false);
        Console.WriteLine("PASS native tab titles: dynamic Unicode/empty/provisional/replacement titles, complete accessibility names, stable tab geometry and language-preserved document/scroll/viewport. Saved desktop tab crops require visual ellipsis review.");
    }

    private async Task WaitForNativeTabTitle(string expected)
    {
        await Wait(_ =>
        {
            var browser = Web.WindowsPanel!.Browser;
            var index = Array.FindIndex(browser.Chrome, item => item.Kind == WindowsComposition.Kind.SelectedTab);
            return index >= 0 && browser.Chrome[index].Text == expected &&
                browser.ChromeNames[index] == expected && browser.AccessibilityObject.GetChild(index)?.Name == expected;
        }, "Native selected-tab title and accessibility name: " + expected);
    }

    private static string ReadTitleTabFrames(WindowsWebKitView browser) => JsonSerializer.Serialize(browser.Chrome
        .Where(item => item.Kind is WindowsComposition.Kind.Tab or WindowsComposition.Kind.SelectedTab or WindowsComposition.Kind.TabTrack)
        .Select(item => new { item.Kind, item.X, item.Y, item.Width, item.Height, item.VisualX, item.VisualY, item.VisualWidth, item.VisualHeight, item.Radius, item.FontSize, item.ClipWidth }));

    private void SaveNativeTabTitle(string name, WindowsWebKitView browser, string expected, bool capture)
    {
        var index = Array.FindIndex(browser.Chrome, item => item.Kind == WindowsComposition.Kind.SelectedTab);
        var accessible = browser.AccessibilityObject.GetChild(index)!;
        var bounds = accessible.Bounds;
        var prefix = Path.Combine(Program.Output, "native-tab-title-" + name);
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new
        {
            expected, actualText = browser.Chrome[index].Text, accessibilityName = accessible.Name,
            accessibilityBounds = bounds, tabFrames = JsonSerializer.Deserialize<JsonElement>(ReadTitleTabFrames(browser)), host = State(),
            visualAcceptance = "Desktop crop of the presented selected tab; review ellipsis, Unicode glyphs and close icon visually. Text/AX assertions alone do not establish correct painting. Occluded, locked or off-screen windows cannot establish visual acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!capture) return;
        Check(bounds.Width > 0 && bounds.Height > 0, "The selected tab exposes actual desktop capture bounds.");
        using var image = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        image.Save(prefix + "-desktop-tab.png", ImageFormat.Png);
    }
}
