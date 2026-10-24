using System.Drawing;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckInteraction()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Interaction runtime");
        Check(Settings.Interaction.SelectedIndex == 0, "A fresh profile defaults to touch interaction.");
        Select(Settings.Scale, 1);
        window.WindowState = WindowState.Maximized;
        window.Activate();
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/interaction.html"));
        await WaitForScript("window.__devduoInput?.state.mode", value => value == "touch", "Document-start touch input");
        await WaitForScript("document.readyState", value => value == "complete", "Interaction fixture load");
        var initial = await InteractionSnapshot();
        CheckTouchPointer(true);
        await InteractionPointer(0x201, 70, 36);
        await InteractionPointer(0x202, 70, 36);
        await WaitForScript("String(interactionSnapshot().tapCount)", value => value == "1", "One tap activates the button once");
        await InteractionPointer(0x201, 280, 300);
        await InteractionPointer(0x200, 280, 160);
        await InteractionPointer(0x202, 280, 160);
        await Wait(state => state.GetProperty("scrollY").GetDouble() > 80, "Touch pans actual document");
        var afterPan = await InteractionSnapshot();
        Check(afterPan.GetProperty("events").EnumerateArray().Any(item => Text(item, "pointerType", "touch") && !item.GetProperty("trusted").GetBoolean()),
            "Page receives explicitly synthetic touch PointerEvents.");
        Check(afterPan.GetProperty("mode").GetProperty("active").GetBoolean() == false && afterPan.GetProperty("tapCount").GetInt32() == 1,
            "Panning ends the contact and does not produce a stray tap.");
        InteractionWheel();
        await Task.Delay(250);
        var afterWheel = await InteractionSnapshot();
        Check(afterWheel.GetProperty("scrollY").GetDouble() == afterPan.GetProperty("scrollY").GetDouble(), "Touch blocks native mouse-wheel scrolling.");
        Check(State().GetProperty("chromeScrollInput").GetProperty("source").GetString() == "emulated touch pan",
            "Safari chrome consumes emulated gesture direction independently of script positioning.");

        // Exercise the real REST/UI boundary, retaining this native document.
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        Settings.ControlApiPort.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Settings.ControlApi.IsChecked = true;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                try { using var ready = await http.GetAsync("api/openapi.json"); if (ready.IsSuccessStatusCode) break; }
                catch (HttpRequestException) { }
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("Control API did not start.");
                await Task.Delay(100);
            }
            using var response = await http.PatchAsJsonAsync("api/settings", new { interactionMode = "mouse" });
            Check(response.IsSuccessStatusCode, "REST selects mouse interaction.");
            await WaitForScript("window.__devduoInput.state.mode", value => value == "mouse", "Existing document selects mouse mode");
            var mouse = await InteractionSnapshot();
            Check(mouse.GetProperty("timeOrigin").GetDouble() == initial.GetProperty("timeOrigin").GetDouble() &&
                mouse.GetProperty("documentId").GetString() == initial.GetProperty("documentId").GetString() &&
                mouse.GetProperty("scrollY").GetDouble() == afterWheel.GetProperty("scrollY").GetDouble(),
                "Interaction switching preserves document, viewport and scroll.");
            Check(Settings.Interaction.SelectedIndex == 1, "REST and the UI report the same input mode.");
            CheckTouchPointer(false);
            InteractionWheel();
            await Wait(state => state.GetProperty("scrollY").GetDouble() > mouse.GetProperty("scrollY").GetDouble(), "Mouse mode restores native wheel scrolling");
            using var touch = await http.PatchAsJsonAsync("api/settings", new { interactionMode = "touch" });
            Check(touch.IsSuccessStatusCode, "REST restores touch mode.");
            await WaitForScript("window.__devduoInput.state.mode", value => value == "touch", "Restored touch mode");
            CheckTouchPointer(true);
        }
        finally { Settings.ControlApi.IsChecked = false; }
        Save("interaction-result", JsonSerializer.SerializeToElement(new { initial, afterPan, afterWheel, final = await InteractionSnapshot(), host = State() }));
        Console.WriteLine($"PASS interaction: {assertions} assertions; real tap/pan, wheel policy and REST/UI mode changes.");
    }

    private async Task<JsonElement> InteractionSnapshot()
    {
        using var data = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(interactionSnapshot())"))!);
        return data.RootElement.Clone();
    }

    private void CheckTouchPointer(bool expected)
    {
        var browser = Web.WindowsPanel!.Browser;
        var saved = System.Windows.Forms.Cursor.Position;
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var page = new Point((int)((geometry.GetProperty("left").GetDouble() + 100) * browser.BackingScale),
            (int)((geometry.GetProperty("top").GetDouble() + 100) * browser.BackingScale));
        var shell = new Point((int)((geometry.GetProperty("width").GetDouble() - 42) * browser.BackingScale),
            (int)(geometry.GetProperty("height").GetDouble() / 2 * browser.BackingScale));
        try
        {
            foreach (var point in new[] { page, shell })
            {
                System.Windows.Forms.Cursor.Position = browser.PresentationBounds!(new Rectangle(point, new Size(1, 1))).Location;
                browser.UpdateTouchPointer();
                var pointer = State().GetProperty("webKit").GetProperty("touchPointer");
                Check(pointer.GetProperty("visible").GetBoolean() == expected &&
                    pointer.GetProperty("systemCursorHidden").GetBoolean() == expected,
                    "The page and Safari shell show a circular pointer only in touch mode.");
            }
            var outside = window.Position;
            System.Windows.Forms.Cursor.Position = new Point(outside.X + 20, outside.Y + 70);
            browser.UpdateTouchPointer();
            Check(!State().GetProperty("webKit").GetProperty("touchPointer").GetProperty("visible").GetBoolean(),
                "Leaving the phone screen restores the system cursor.");
        }
        finally { System.Windows.Forms.Cursor.Position = saved; browser.UpdateTouchPointer(); }
    }

    private async Task InteractionPointer(uint message, double cssX, double cssY)
    {
        var browser = Web.WindowsPanel!.Browser;
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var point = new Point((int)Math.Round((geometry.GetProperty("left").GetDouble() + cssX) * browser.BackingScale),
            (int)Math.Round((geometry.GetProperty("top").GetDouble() + cssY) * browser.BackingScale));
        PresentationSetFocus(browser.BrowserWindow);
        PresentationSendMessage(browser.BrowserWindow, message, message == 0x202 ? 0 : 1,
            (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16)));
        await Task.Delay(80);
    }

    private void InteractionWheel()
    {
        var browser = Web.WindowsPanel!.Browser;
        var screen = browser.PointToScreen(new Point(browser.Width / 2, browser.Height / 2));
        PresentationSendMessage(browser.BrowserWindow, 0x20A, (nint)(-120 << 16),
            (nint)((screen.X & 0xffff) | ((screen.Y & 0xffff) << 16)));
    }
}
