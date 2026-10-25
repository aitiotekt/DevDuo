using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Avalonia.Controls;
using DevDuo;
using Forms = System.Windows.Forms;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task LoadDesktopInput()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Desktop input runtime");
        var previousDocument = await Web.EvaluateResultAsync("String(performance.timeOrigin)");
        Web.Navigate(BrowserTestServer.Address + "desktop-input.html");
        await WaitForScript("String(performance.timeOrigin)+'|'+typeof window.inputAcceptanceState+'|'+document.readyState",
            value => value is not null && !value.StartsWith(previousDocument + "|", StringComparison.Ordinal) && value.EndsWith("|function|complete", StringComparison.Ordinal),
            "New desktop input fixture document");
    }

    private async Task CheckPageDialogs()
    {
        using var server = new BrowserTestServer();
        await LoadDesktopInput();
        var before = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])");
        foreach (var language in new[] { "en", "zh-Hans" })
        {
            AppLocalization.Current.Select(language);
            foreach (var item in new[]
            {
                (Script: "alert('Native alert acceptance');'alert returned'", Accept: true, Value: "", Expected: "alert returned"),
                (Script: "String(confirm('Native confirm acceptance'))", Accept: true, Value: "", Expected: "true"),
                (Script: "String(confirm('Native confirm acceptance'))", Accept: false, Value: "", Expected: "false"),
                (Script: "JSON.stringify(prompt('Native prompt acceptance','initial'))", Accept: true, Value: "中文 response", Expected: "\"中文 response\""),
                (Script: "JSON.stringify(prompt('Native prompt cancellation','initial'))", Accept: false, Value: "ignored", Expected: "null")
            })
            {
                var evaluation = Web.EvaluateResultAsync(item.Script);
                var dialog = await WaitPageDialog();
                Check(!evaluation.IsCompleted, "JavaScript waits for an actual presented dialog result.");
                Check(dialog.Text.Contains("127.0.0.1:18879", StringComparison.Ordinal) && dialog.Visible,
                    "The native dialog identifies its page origin and is visible.");
                Check(dialog.DeviceDpi == State().GetProperty("platform").GetProperty("deviceDpi").GetInt32() &&
                    Forms.Screen.FromHandle(dialog.Handle).WorkingArea.Contains(dialog.Bounds),
                    "The actual native dialog follows the host DPI and fits the monitor working area.");
                var controls = Descendants(dialog).ToArray();
                var dialogHandle = dialog.Handle;
                var nativeTree = await Task.Run(() => ReadAutomation(dialogHandle));
                Check(nativeTree.Any(node => node.Name == AppLocalization.Current.Text("PageDialog.Accept") && node.Type == "ControlType.Button"),
                    "The native dialog exposes its localized action through the OS UIA provider.");
                var response = controls.OfType<Forms.TextBox>().SingleOrDefault();
                if (response is not null) { Check(response.Text == "initial", "Prompt preserves its authored default."); response.Text = item.Value; }
                var button = controls.OfType<Forms.Button>().Single(control => control.Name == (item.Accept ? "Accept" : "Cancel"));
                Check(button.Text == AppLocalization.Current.Text(item.Accept ? "PageDialog.Accept" : "PageDialog.Cancel"),
                    "Dialog actions use the active localized catalog.");
                button.PerformClick();
                Check(await evaluation.WaitAsync(TimeSpan.FromSeconds(5)) == item.Expected, "WebKit receives the user's dialog result.");
                Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])") == before,
                    "Native dialogs preserve the document, viewport and scroll position.");
            }
        }
        var keyboard = Web.EvaluateResultAsync("JSON.stringify(prompt('Native keyboard acceptance','initial'))");
        var keyboardDialog = await WaitPageDialog();
        try
        {
            PresentationSetForegroundWindow(keyboardDialog.Handle);
            var input = Descendants(keyboardDialog).OfType<Forms.TextBox>().Single();
            input.Focus();
            input.SelectAll();
            if (PresentationGetForegroundWindow() != keyboardDialog.Handle || !input.ContainsFocus)
                throw new InvalidOperationException("OS keyboard acceptance requires the active native prompt and its focused edit control.");
            var keys = "原生输入".SelectMany(character => new[]
            {
                new DesktopKey { Type = 1, Scan = character, Flags = 4 },
                new DesktopKey { Type = 1, Scan = character, Flags = 6 }
            }).ToArray();
            Check(DesktopSendInput((uint)keys.Length, keys, Marshal.SizeOf<DesktopKey>()) == keys.Length,
                "The OS accepts the prompt's Unicode keyboard input events.");
            await Task.Delay(150);
            Check(input.Text == "原生输入", "Real keyboard input reaches the focused native prompt.");
            var enter = new[] { new DesktopKey { Type = 1, Key = 13 }, new DesktopKey { Type = 1, Key = 13, Flags = 2 } };
            Check(DesktopSendInput(2, enter, Marshal.SizeOf<DesktopKey>()) == 2, "The OS accepts Enter for the dialog's default action.");
            Check(await keyboard.WaitAsync(TimeSpan.FromSeconds(5)) == "\"原生输入\"", "Enter submits actual typed prompt text to WebKit.");
        }
        finally { if (keyboardDialog.Visible) keyboardDialog.Close(); }
        var pending = Web.EvaluateResultAsync("JSON.stringify(prompt('Cancelled by navigation','initial'))");
        await WaitPageDialog();
        Web.Navigate(BrowserTestServer.Address + "desktop-input.html?replacement");
        await WaitForScript("location.search", value => value == "?replacement", "Navigation cancels a pending page dialog");
        Check(!Forms.Application.OpenForms.Cast<Forms.Form>().Any(form => form.Name == "DevDuoPageDialog"),
            "Navigation closes pending native page dialogs.");
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var disposed = Web.EvaluateResultAsync("JSON.stringify(prompt('Cancelled by container disposal','initial'))");
        await WaitPageDialog();
        var originalBrowser = Web.WindowsPanel!.Browser;
        Select(Settings.Mode, 1);
        await Wait(state => originalBrowser.IsDisposed && Text(state, "initializationState", "ready") &&
            !Forms.Application.OpenForms.Cast<Forms.Form>().Any(form => form.Name == "DevDuoPageDialog"),
            "Disposed original, closed dialog and ready replacement immersive container");
        Check(!Forms.Application.OpenForms.Cast<Forms.Form>().Any(form => form.Name == "DevDuoPageDialog"),
            "Container disposal closes pending dialogs before releasing native resources.");
        await disposed.WaitAsync(TimeSpan.FromSeconds(5));
        Save("page-dialogs", JsonSerializer.SerializeToElement(new { assertions, host = State() }));
        Console.WriteLine($"PASS page dialogs: {assertions} assertions; real WebKit listeners, both languages, accept/cancel/prompt values and navigation cancellation.");
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct DesktopKey
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal ushort Key;
        [FieldOffset(10)] internal ushort Scan;
        [FieldOffset(12)] internal uint Flags;
        [FieldOffset(20)] internal uint MouseFlags;
    }
    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint DesktopSendInput(uint count, DesktopKey[] inputs, int size);

    private static IEnumerable<Forms.Control> Descendants(Forms.Control root)
    {
        foreach (Forms.Control control in root.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }

    private static async Task<Forms.Form> WaitPageDialog()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var dialog = Forms.Application.OpenForms.Cast<Forms.Form>().SingleOrDefault(form => form.Name == "DevDuoPageDialog" && form.Visible);
            if (dialog is not null) return dialog;
            await Task.Delay(50);
        }
        throw new TimeoutException("WebKit must present a native page dialog, rather than silently accepting or cancelling it.");
    }

    private async Task CheckDesktopAccessibility()
    {
        using var server = new BrowserTestServer();
        await LoadDesktopInput();
        window.Activate();
        var browser = Web.WindowsPanel!.Browser;
        await CheckDesktopFixturePaint(browser, "desktop-accessibility-initial-paint");
        var handle = window.TryGetPlatformHandle()!.Handle;
        var pageHandle = browser.AccessibilityHostWindow;
        // UIA clients must query on another thread while the UI dispatcher pumps.
        var observed = await Task.Run(() => new
        {
            host = ReadAutomation(handle), page = ReadAutomation(pageHandle)
        }).WaitAsync(TimeSpan.FromSeconds(15));
        Save("desktop-accessibility", JsonSerializer.SerializeToElement(new { observed, host = State() }));
        Check(observed.host.Any(item => item.Name == AppLocalization.Current.Text("Address.Name") && item.Type == "ControlType.Edit"),
            "The real Windows UIA host tree exposes an editable address field.");
        Check(observed.page.Any(item => item.Name == "Composition text" && item.Type == "ControlType.Edit"),
            "The native WebKit UIA tree must expose the authored, labelled web input.");
        Check(observed.page.Any(item => item.Name == "Desktop input acceptance" && item.Type == "ControlType.Document"),
            "The actual web document exposes its title to screen readers.");
        Check(observed.page.Any(item => item.Name == "Desktop input acceptance" && item.Type == "ControlType.Text"),
            "Ordinary WebCore static text is readable through UIA Name without an editable Value pattern.");
        foreach (var name in new[] { "Multiline text", "Editable paragraph" })
            Check(observed.page.Any(item => item.Name == name && item.Type == "ControlType.Edit"), "WebCore exposes labelled textarea/contenteditable semantics through the real UIA tree.");
        for (var pose = 0; pose < 2; pose++)
        {
            if (pose == 1)
            {
                Settings.Rotate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Settings.Rotate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
            }
            var inputBounds = await Task.Run(() =>
            {
                var input = FindWebAutomation(pageHandle, "Composition text", ControlType.Edit);
                input.SetFocus();
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("automation 中文");
                return input.Current.BoundingRectangle;
            }).WaitAsync(TimeSpan.FromSeconds(10));
            await WaitForScript("document.querySelector('#text').value", value => value == "automation 中文", "UIA changes the real WebCore input value");
            Check(await Web.EvaluateResultAsync("document.activeElement?.id") == "text", "UIA focus reaches the actual authored web input.");
            Check(!inputBounds.IsEmpty && inputBounds.Width > 0 && inputBounds.Height > 0, "Web UIA exposes a nonempty physical screen rectangle.");
            window.Activate();
            PresentationSetForegroundWindow(handle);
            if (PresentationGetAncestor(PresentationGetForegroundWindow(), 3) != PresentationGetAncestor(handle, 3))
                throw new InvalidOperationException("Physical UIA discovery requires the foreground acceptance window or its owned source.");
            var discovered = await Task.Run(() =>
            {
                var tree = ReadAutomation(handle);
                var hit = AutomationElement.FromPoint(new System.Windows.Point(inputBounds.X + inputBounds.Width / 2, inputBounds.Y + inputBounds.Height / 2)).Current;
                return new { tree, hit = new { hit.Name, type = hit.ControlType.ProgrammaticName, hit.ClassName, hit.FrameworkId,
                    hit.NativeWindowHandle, bounds = hit.BoundingRectangle.ToString() } };
            });
            Save($"desktop-accessibility-discovery-{pose}", JsonSerializer.SerializeToElement(new
            {
                discovered.tree, inputBounds = inputBounds.ToString(), discovered.hit, host = State()
            }));
            if (discovered.tree.Count(item => item.Name == "Composition text" && item.Type == "ControlType.Edit") != 1)
                Save($"desktop-accessibility-host-paths-{pose}", JsonSerializer.SerializeToElement(await Task.Run(() => ReadAutomationHostPaths(handle))));
            Check(discovered.tree.Count(item => item.Name == "Composition text" && item.Type == "ControlType.Edit") == 1,
                "The visible application tree exposes one authored web input, without duplicate native-host branches or a hidden source HWND.");
            Check(discovered.hit.Name == "Composition text", "OS UIA hit testing on the visible presenter reaches the transformed web input.");
            await Web.EvaluateResultAsync("document.activeElement.blur();'blurred'");
            window.Activate();
            PresentationSetForegroundWindow(handle);
            if (PresentationGetAncestor(PresentationGetForegroundWindow(), 3) != PresentationGetAncestor(handle, 3))
                throw new InvalidOperationException("UIA hit testing requires the foreground acceptance window or its owned source.");
            Forms.Cursor.Position = new((int)(inputBounds.X + inputBounds.Width / 2), (int)(inputBounds.Y + inputBounds.Height / 2));
            var click = new[] { new DesktopKey { Type = 0, MouseFlags = 2 }, new DesktopKey { Type = 0, MouseFlags = 4 } };
            Check(DesktopSendInput(2, click, Marshal.SizeOf<DesktopKey>()) == 2, "The OS accepts a real click at the UIA rectangle center.");
            await WaitForScript("document.activeElement?.id", value => value == "text", "Presented UIA rectangle center hits the actual input, including Closed 180 degrees");
            await Task.Run(() => ((InvokePattern)FindWebAutomation(pageHandle, "Show modal dialog", ControlType.Button).GetCurrentPattern(InvokePattern.Pattern)).Invoke());
            await WaitForScript("String(document.querySelector('#dialog').open)", value => value == "true", "UIA invokes a real authored modal action");
            AutomationObservation[] modalTree = [];
            var deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                modalTree = await Task.Run(() => ReadAutomation(pageHandle));
                if (modalTree.Any(item => item.Name == "Dialog text" && item.Type == "ControlType.Edit")) break;
                await Task.Delay(50);
            } while (DateTime.UtcNow < deadline);
            Save($"desktop-accessibility-modal-{pose}", JsonSerializer.SerializeToElement(new { modalTree, host = State() }));
            Check(modalTree.Any(item => item.Name == "Dialog text" && item.Type == "ControlType.Edit"), "The actual modal's labelled input appears in the WebCore accessibility tree.");
            await Task.Run(() => ((InvokePattern)FindWebAutomation(pageHandle, "Accept dialog", ControlType.Button).GetCurrentPattern(InvokePattern.Pattern)).Invoke());
            await WaitForScript("String(document.querySelector('#dialog').open)", value => value == "false", "UIA closes the authored modal through its actual action");
            var restoredDeadline = DateTime.UtcNow.AddSeconds(2);
            AutomationObservation[] restoredTree;
            do
            {
                restoredTree = await Task.Run(() => ReadAutomation(pageHandle));
                if (restoredTree.Any(item => item.Name == "Composition text" && item.Type == "ControlType.Edit")) break;
                await Task.Delay(50);
            } while (DateTime.UtcNow < restoredDeadline);
            Check(restoredTree.Any(item => item.Name == "Composition text" && item.Type == "ControlType.Edit"),
                "Closing the authored modal restores the real non-modal web input in the public accessibility tree.");
            Save($"desktop-accessibility-actions-{pose}", JsonSerializer.SerializeToElement(new { inputBounds = inputBounds.ToString(), modalTree, host = State() }));
        }
        var initialScale = window.RenderScaling;
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_MONITOR_TRANSITIONS") == "1")
        {
            if (window.Screens.All.Select(screen => screen.Scaling).Distinct().Count() < 2)
                throw new InvalidOperationException("Monitor-transition acceptance requires attached displays with different actual DPI scales.");
            var baseline = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])");
            foreach (var scale in window.Screens.All.Select(screen => screen.Scaling).Distinct().Append(initialScale))
            {
                await DesktopTestScreen.Move(window, scale, "desktop-accessibility-display-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await CheckDesktopFixturePaint(browser, "desktop-accessibility-display-paint-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var bounds = await Task.Run(() => FindWebAutomation(pageHandle, "Composition text", ControlType.Edit).Current.BoundingRectangle);
                Check(!bounds.IsEmpty && window.Screens.ScreenFromWindow(window)!.Bounds.Contains(new Avalonia.PixelPoint((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2))),
                    "The retained web UIA object follows the physical screen after a real monitor/DPI transition.");
                Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])") == baseline,
                    "Cross-monitor DPI changes preserve the document, logical viewport and scroll position.");
            }
        }
        var previousValue = await Task.Run(() => (ValuePattern)FindWebAutomation(pageHandle, "Composition text", ControlType.Edit).GetCurrentPattern(ValuePattern.Pattern));
        await LoadDesktopInput();
        var rejected = await Task.Run(() =>
        {
            try { previousValue.SetValue("stale document write"); return false; }
            catch (ElementNotAvailableException) { return true; }
            catch (COMException error) when (error.HResult == unchecked((int)0x80040201)) { return true; }
        });
        Check(rejected, "A retained UIA value provider rejects actions after its owning document is replaced.");
        Check(await Web.EvaluateResultAsync("document.querySelector('#text').value") == "",
            "A stale accessibility action cannot modify the replacement document.");
        var lifetime = (Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Close();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        Check(browser.IsDisposed, "Closing a rotated window disposes the native browser without late HWND/DPI errors.");
        Console.WriteLine($"PASS desktop accessibility: {assertions} native UIA checks.");
    }

    private async Task CheckDesktopFixturePaint(WindowsWebKitView browser, string receipt)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            using var bitmap = NativePaintEvidence.Capture(browser, State());
            // The fixture's left body padding exposes its opaque html canvas,
            // independently of text, focus, gradient content and chrome.
            var pixel = bitmap.GetPixel((int)Math.Round(8 * browser.BackingScale), (int)Math.Round(64 * browser.BackingScale));
            var painted = Math.Abs(pixel.R - 185) <= 3 && Math.Abs(pixel.G - 228) <= 3 && Math.Abs(pixel.B - 220) <= 3;
            if (painted || DateTime.UtcNow >= deadline)
            {
                bitmap.Save(Path.Combine(Program.Output, receipt + ".png"));
                var paintState = painted ? null : await Web.EvaluateResultAsync("JSON.stringify({visibility:document.visibilityState,focused:document.hasFocus(),htmlBackground:getComputedStyle(document.documentElement).backgroundColor,bodyBackground:getComputedStyle(document.body).backgroundColor,forcedColors:matchMedia('(forced-colors:active)').matches,styleSheets:[...document.styleSheets].map(s=>({disabled:s.disabled,rules:s.cssRules.length})),htmlBounds:document.documentElement.getBoundingClientRect().toJSON(),ready:document.readyState})");
                Save(receipt, JsonSerializer.SerializeToElement(new { rgb = new[] { (int)pixel.R, pixel.G, pixel.B }, paintState, host = State() }));
                Check(painted, "The real native painter retains the authored canvas after a monitor/DPI transition; a populated DOM or UIA tree cannot accept a blank page.");
                return;
            }
            await Task.Delay(50);
        }
    }

    private static AutomationElement FindWebAutomation(nint handle, string name, ControlType type) =>
        AutomationElement.FromHandle(handle).FindFirst(TreeScope.Subtree, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, type)))
        ?? throw new InvalidOperationException($"The native WebKit UIA tree is missing {name} ({type.ProgrammaticName}).");

    private sealed record AutomationObservation(string Name, string Type, string Class, string Bounds, bool Focusable);
    private static string[] ReadAutomationHostPaths(nint handle)
    {
        var rows = new List<string>();
        var seen = new HashSet<string>();
        var walker = TreeWalker.RawViewWalker;
        var pending = new Stack<(AutomationElement Element, string Path, int Depth)>();
        pending.Push((AutomationElement.FromHandle(handle), "", 0));
        while (pending.Count > 0 && seen.Count < 700)
        {
            var (element, parent, depth) = pending.Pop();
            var info = element.Current;
            var key = string.Join('.', element.GetRuntimeId());
            var path = parent + "/" + info.ControlType.ProgrammaticName + ":" + info.NativeWindowHandle;
            if (info.NativeWindowHandle != 0 || info.Name == "Composition text") rows.Add(path + " " + info.Name + " [" + key + "]");
            if (depth >= 18 || !seen.Add(key)) continue;
            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                pending.Push((child, path, depth + 1));
        }
        return rows.ToArray();
    }
    private static AutomationObservation[] ReadAutomation(nint handle)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var root = AutomationElement.FromHandle(handle);
                var elements = root.FindAll(TreeScope.Subtree, Condition.TrueCondition);
                return elements.Cast<AutomationElement>().Take(500).Select(element => new AutomationObservation(
                    element.Current.Name, element.Current.ControlType.ProgrammaticName, element.Current.ClassName,
                    element.Current.BoundingRectangle.ToString(), element.Current.IsKeyboardFocusable)).ToArray();
            }
            catch (ElementNotAvailableException) when (attempt < 2)
            {
                // Modal/AX updates may remove a node between enumeration and
                // property reads. Retry a fresh bounded snapshot, never old IDs.
                Thread.Sleep(40);
            }
        }
    }
}
