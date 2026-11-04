using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevDuo.Windows.Tests;

// Launches real application processes with an isolated preference/profile path.
// A listener owned by the probe supplies the occupied-port failure, not a mock.
internal static class DeveloperRestoreProbe
{
    internal static async Task<int> Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "devduo-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine("captures", "desktop-validation", "windows-runtime", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(output);
        Evidence.Freeze(output);
        using var controlReservation = Reserve();
        using var nativeReservation = Reserve();
        using var cdpReservation = Reserve();
        var controlPort = Port(controlReservation);
        var nativePort = Port(nativeReservation);
        var cdpPort = Port(cdpReservation);
        var settings = new HostSettings {
            PreviewScale = "fit", ControlApiEnabled = true, CdpEnabled = true, NativeRemoteEnabled = true,
            ControlApiPort = controlPort, CdpPort = cdpPort, NativeRemotePort = nativePort,
            ControlApiBearerToken = "restore-probe", CdpBearerToken = "restore-cdp"
        };
        var store = new PreferenceStore(Path.Combine(folder, "preferences.json"));
        if (!store.SaveHost(settings)) throw new IOException("Cannot save isolated restore preferences.");
        controlReservation.Stop(); nativeReservation.Stop(); cdpReservation.Stop();
        using var control = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{controlPort}/"), Timeout = TimeSpan.FromSeconds(2) };
        control.DefaultRequestHeaders.Authorization = new("Bearer", settings.ControlApiBearerToken);
        using var cdp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{cdpPort}/"), Timeout = TimeSpan.FromSeconds(2) };
        cdp.DefaultRequestHeaders.Authorization = new("Bearer", settings.CdpBearerToken);
        try
        {
            for (var run = 0; run < 8; run++)
            {
                var invalidField = run switch { 5 => "cdpBearerToken", 6 => "controlApiBearerToken", 7 => "nativeRemotePort", _ => null };
                if (invalidField is not null)
                {
                    store.SaveHost(settings);
                    var malformed = JsonNode.Parse(File.ReadAllText(store.Path))!;
                    malformed["host"]![invalidField] = run == 7 ? JsonValue.Create(0) : JsonValue.Create("invalid\ntoken");
                    File.WriteAllText(store.Path, malformed.ToJsonString());
                }
                using var fallbackReservation = run == 7 ? Reserve(9223) : null;
                fallbackReservation?.Stop();
                using var occupied = run switch {
                    0 => Reserve(cdpPort), 2 => Reserve(controlPort), 3 => Reserve(nativePort), _ => null
                };
                var start = new ProcessStartInfo(Path.GetFullPath("src/DevDuo/bin/Debug/net10.0-windows/DevDuo.exe")) {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };
                start.Environment["DEVDUO_PREFERENCES_PATH"] = store.Path;
                start.Environment["DEVDUO_WEBKIT_PROFILE"] = Path.Combine(folder, "profile");
                start.Environment.Remove("DEVDUO_URL");
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch restore probe.");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    var state = run is 2 or 6
                        ? await WaitState(cdp, _ => true, "json/version")
                        : await WaitState(control, element => element.GetProperty("nativeReady").GetBoolean());
                    if (run is 0 or 5)
                    {
                        await Task.Delay(1000);
                        state = await WaitState(control, element => element.GetProperty("cdpAddress").ValueKind == JsonValueKind.Null);
                        occupied?.Stop();
                        using var change = await control.PatchAsJsonAsync("api/settings", new { fold = "open" });
                        change.EnsureSuccessStatusCode();
                        await Task.Delay(1200);
                        state = await WaitState(control, element => element.GetProperty("nativeReady").GetBoolean());
                        if (state.GetProperty("cdpAddress").ValueKind != JsonValueKind.Null)
                            throw new InvalidOperationException("A failed service retried during container rebuilding.");
                    }
                    else if (run is not (2 or 6))
                    {
                        state = await WaitState(control, element => element.GetProperty("cdpAddress").ValueKind == JsonValueKind.String);
                        using var version = await cdp.GetAsync("json/version");
                        version.EnsureSuccessStatusCode();
                    }
                    if (run is 2 or 3 or 5 or 6 or 7)
                    {
                        occupied?.Stop();
                        await Task.Delay(500);
                        using var stillStopped = Reserve(run is 2 or 6 ? controlPort : run == 5 ? cdpPort : nativePort);
                        if (run == 7) { using var noFallback = Reserve(9223); }
                    }
                    if (run is not (3 or 7))
                    {
                        using var native = new TcpClient();
                        await native.ConnectAsync(IPAddress.Loopback, nativePort);
                    }
                    File.WriteAllText(Path.Combine(output, $"restore-{run}.json"), state.GetRawText());
                    if (!process.CloseMainWindow()) throw new InvalidOperationException("Application has no closeable main window.");
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    var restoredStore = new PreferenceStore(store.Path);
                    var restored = restoredStore.Host;
                    if (!restored.ControlApiEnabled || !restored.CdpEnabled || !restored.NativeRemoteEnabled)
                        throw new InvalidOperationException("Shutdown or failed startup erased listener intent.");
                    if (invalidField is not null && !restoredStore.InvalidListenerFields.Contains(invalidField))
                        throw new InvalidOperationException("An invalid saved listener input was silently replaced with a default.");
                    using var releasedControl = new TcpListener(IPAddress.Loopback, controlPort);
                    using var releasedCdp = new TcpListener(IPAddress.Loopback, cdpPort);
                    using var releasedNative = new TcpListener(IPAddress.Loopback, nativePort);
                    releasedControl.Start(); releasedCdp.Start(); releasedNative.Start();
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
                    File.WriteAllText(Path.Combine(output, $"restore-{run}.log"), await stdout + await stderr);
                }
            }
            Console.WriteLine("PASS developer restore: eight real application starts, independent port conflicts, invalid port/token blocking, next-launch recovery, no rebuild retry, credentials and socket release. " + output);
            return 0;
        }
        finally { Directory.Delete(folder, true); }
    }

    private static TcpListener Reserve(int port = 0)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return listener;
    }
    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;
    private static async Task<JsonElement> WaitState(HttpClient client, Func<JsonElement, bool> accept, string path = "api/state")
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(45))
        {
            try
            {
                using var response = await client.GetAsync(path);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (accept(document.RootElement)) return document.RootElement.Clone();
                }
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { }
            await Task.Delay(150);
        }
        throw new TimeoutException("The restored service did not reach the expected state.");
    }
}
