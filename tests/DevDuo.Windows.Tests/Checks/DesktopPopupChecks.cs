using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckDesktopPopups()
    {
        using var server = new BrowserTestServer();
        await LoadDesktopInput();
        for (var fold = 0; fold < 2; fold++)
        {
            Select(Settings.Fold, fold);
            await LoadDesktopInput();
            for (var angle = 0; angle < 4; angle++)
            {
                await Web.EvaluateResultAsync("scrollTo(0,0);document.querySelector('#choice').selectedIndex=0;document.querySelector('#choice').focus({preventScroll:true});'focused'");
                var browser = Web.WindowsPanel!.Browser;
                var baseline = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])");
                using var rectangle = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify((r=>({x:r.x,y:r.y,width:r.width,height:r.height}))(document.querySelector('#choice').getBoundingClientRect()))"))!);
                var r = rectangle.RootElement;
                var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
                var scale = browser.BackingScale;
                var control = browser.PresentationBounds!(new Rectangle(
                    (int)Math.Round((r.GetProperty("x").GetDouble() + geometry.GetProperty("left").GetDouble()) * scale),
                    (int)Math.Round((r.GetProperty("y").GetDouble() + geometry.GetProperty("top").GetDouble()) * scale),
                    (int)Math.Round(r.GetProperty("width").GetDouble() * scale),
                    (int)Math.Round(r.GetProperty("height").GetDouble() * scale)));
                ActivateDesktopInput(browser.BrowserWindow);
                // Keep hover selection outside the keyboard-only menu case.
                DesktopGetWindowRect(window.TryGetPlatformHandle()!.Handle, out var hostBounds);
                System.Windows.Forms.Cursor.Position = new(hostBounds.Left + 50, hostBounds.Top + 20);
                await Task.Delay(120);
                SendDesktopKeys(0x12, 0x28); // Alt+Down opens the real native select.
                nint popup;
                try { popup = await WaitDesktopPopup(); }
                catch
                {
                    Save($"desktop-popup-missing-{fold}-{angle}", JsonSerializer.SerializeToElement(new { interaction = await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"), focus = DesktopGetFocus().ToInt64(), browser = browser.BrowserWindow.ToInt64(), host = State() }));
                    throw;
                }
                try
                {
                    DesktopGetWindowRect(popup, out var bounds);
                    var actual = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                    Save($"desktop-popup-{fold}-{angle}", JsonSerializer.SerializeToElement(new { control, actual, host = State() }));
                    Check(Math.Min(Math.Abs(actual.Top - control.Bottom), Math.Abs(actual.Bottom - control.Top)) <= 4,
                        "The native select anchors to the presented control in desktop pixels, including Closed 180 degrees.");
                    Check(actual.Right > control.Left && actual.Left < control.Right,
                        "The native popup overlaps its presented control horizontally.");
                    Check(System.Windows.Forms.Screen.FromHandle(popup).WorkingArea.Contains(actual),
                        "The native popup fits the target monitor working area.");
                    SendDesktopKeys(0x28);
                    await Task.Delay(80);
                    SendDesktopKeys(0x0D);
                    await WaitForScript("String(document.querySelector('#choice').selectedIndex)", value => value == "1", "Native select keyboard commit");
                    Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])") == baseline,
                        "Native menu selection preserves the document, viewport and scroll position.");
                    await Web.EvaluateResultAsync("document.querySelector('#choice').selectedIndex=0;'reset'");
                    SendDesktopKeys(0x12, 0x28);
                    popup = await WaitDesktopPopup();
                    DesktopGetWindowRect(popup, out var mouseBounds);
                    RequireDesktopInputForeground();
                    System.Windows.Forms.Cursor.Position = new((mouseBounds.Left + mouseBounds.Right) / 2,
                        (mouseBounds.Top + mouseBounds.Bottom) / 2);
                    var click = new[] { new DesktopKey { Type = 0, MouseFlags = 2 }, new DesktopKey { Type = 0, MouseFlags = 4 } };
                    Check(DesktopSendInput(2, click, Marshal.SizeOf<DesktopKey>()) == 2,
                        "The OS accepts a real mouse selection in the presented popup.");
                    await WaitForScript("String(document.querySelector('#choice').selectedIndex)", value => value == "1", "Native select mouse commit");
                    Check(!DesktopIsWindowVisible(popup), "Mouse selection dismisses the native popup.");
                    SendDesktopKeys(0x12, 0x28);
                    popup = await WaitDesktopPopup();
                    SendDesktopKeys(0x28);
                    SendDesktopKeys(0x1B);
                    await Task.Delay(100);
                    Check(!DesktopIsWindowVisible(popup) && await Web.EvaluateResultAsync("String(document.querySelector('#choice').selectedIndex)") == "1",
                        "Escape dismisses the native popup without committing its pending selection.");
                }
                finally { if (DesktopIsWindowVisible(popup)) DesktopPostMessage(popup, 0x100, 0x1B, 0); }
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
            }
        }
        Console.WriteLine($"PASS desktop popups: {assertions} native checks across eight body poses.");
    }

    private void ActivateDesktopInput(nint browser)
    {
        var host = window.TryGetPlatformHandle()!.Handle;
        window.Activate();
        PresentationSetForegroundWindow(host);
        Web.Focus();
        PresentationSetFocus(browser);
        if (PresentationGetAncestor(PresentationGetForegroundWindow(), 3) != PresentationGetAncestor(host, 3) || DesktopGetFocus() != browser)
            throw new InvalidOperationException("Native desktop input requires the foreground acceptance window.");
    }

    private static void SendDesktopKeys(params ushort[] keys)
    {
        RequireDesktopInputForeground();
        var input = keys.Select(key => new DesktopKey { Type = 1, Key = key })
            .Concat(keys.Reverse().Select(key => new DesktopKey { Type = 1, Key = key, Flags = 2 })).ToArray();
        if (DesktopSendInput((uint)input.Length, input, Marshal.SizeOf<DesktopKey>()) != input.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void RequireDesktopInputForeground()
    {
        DesktopGetWindowThreadProcessId(PresentationGetForegroundWindow(), out var process);
        if (process != Environment.ProcessId)
            throw new InvalidOperationException("Native keyboard/mouse injection requires the acceptance process in the foreground.");
    }

    private static async Task<nint> WaitDesktopPopup()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            nint result = 0;
            DesktopEnumWindows((handle, _) =>
            {
                DesktopGetWindowThreadProcessId(handle, out var process);
                if (process != Environment.ProcessId || !DesktopIsWindowVisible(handle)) return true;
                var name = new StringBuilder(128);
                DesktopGetClassName(handle, name, name.Capacity);
                if (name.ToString() == "WebKit2WebPopupMenuProxyWindowClass") result = handle;
                return result == 0;
            }, 0);
            if (result != 0) return result;
            await Task.Delay(30);
        }
        throw new TimeoutException("WebKit must open an actual native select popup.");
    }

    private delegate bool DesktopEnumWindow(nint handle, nint parameter);
    [DllImport("user32.dll", EntryPoint = "GetFocus")] private static extern nint DesktopGetFocus();
    [StructLayout(LayoutKind.Sequential)] private struct DesktopRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "EnumWindows")] private static extern bool DesktopEnumWindows(DesktopEnumWindow callback, nint parameter);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int DesktopGetClassName(nint handle, StringBuilder name, int maximum);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint DesktopGetWindowThreadProcessId(nint handle, out int process);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool DesktopGetWindowRect(nint handle, out DesktopRect rectangle);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")] private static extern bool DesktopIsWindowVisible(nint handle);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool DesktopPostMessage(nint handle, uint message, nint parameter, nint data);
}
