using System.Globalization;
using System.Text.Json;
using DevDuo;

internal static class HostPreferenceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Host settings share the language preference file. Body angle is never persisted.
        var hostFolder = Path.Combine(Path.GetTempPath(), "devduo-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hostFolder);
        try
        {
            var path = Path.Combine(hostFolder, "preferences.json");
            var fresh = new PreferenceStore(path);
            check(fresh.Host.Theme == "system" && fresh.Host.ContainerMode == "safari" && fresh.Host.Fold == "closed" &&
                fresh.Host.InteractionMode == "touch" && fresh.Host.PreviewScale == "native" && fresh.Host.DeviceOutline && fresh.Host.ShowTabStrip &&
                fresh.Host.TintAlpha is null && fresh.Host.LastUrl is null && fresh.InvalidHostFields.Count == 0,
                "Fresh host settings use the documented Safari, Closed, 1:1 defaults.");
            var chosen = new HostSettings
            {
                Theme = "dark", InteractionMode = "mouse", ContainerMode = "app", Fold = "open", PreviewScale = "fit", DeviceOutline = false,
                ShowTabStrip = false, TintAlpha = .5, CornerRadius = 12, SettingsPanelExpanded = false,
                MaterialExpanded = true, DiagnosticsExpanded = true, LastUrl = "https://example.test/a?b=1#c"
            };
            check(fresh.SaveHost(chosen), "Host preference write must succeed.");
            check(new AppLocalization(path, CultureInfo.GetCultureInfo("en-US")).Select("zh-Hans"), "Language write must succeed.");
            var reloaded = new PreferenceStore(path);
            check(reloaded.Language == "zh-Hans", "Host and language preferences share one file.");
            check(JsonSerializer.Serialize(reloaded.Host) == JsonSerializer.Serialize(chosen), "A language write preserves host settings.");
            check(reloaded.SaveHost(new HostSettings()) && new PreferenceStore(path).Language == "zh-Hans", "A host write preserves the language.");
            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
            {
                check(saved.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "Host settings keep preference schema version 1.");
                check(!saved.RootElement.GetProperty("host").EnumerateObject().Any(property =>
                        property.Name.Contains("rotation", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("angle", StringComparison.OrdinalIgnoreCase)),
                    "Body angle is not persisted, preserving the Closed 180° rotation-history qualification.");
            }
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"theme\":\"neon\",\"containerMode\":\"app\"," +
                "\"previewScale\":3,\"deviceOutline\":\"yes\",\"tintAlpha\":2,\"shadowOpacity\":0.4,\"cornerRadius\":-1," +
                "\"lastUrl\":\"file:///etc/hosts\",\"fold\":\"\",\"futureField\":true}}");
            var partial = new PreferenceStore(path);
            check(partial.Host.ContainerMode == "app" && partial.Host.ShadowOpacity == .4, "Valid host fields survive invalid neighbors.");
            check(partial.Host.Theme == "system" && partial.Host.PreviewScale == "native" && partial.Host.DeviceOutline &&
                partial.Host.TintAlpha is null && partial.Host.CornerRadius is null && partial.Host.LastUrl is null && partial.Host.Fold == "closed",
                "Invalid host fields fall back individually.");
            check(partial.InvalidHostFields.Order().SequenceEqual(new[] { "cornerRadius", "deviceOutline", "fold", "lastUrl", "previewScale", "theme", "tintAlpha" }),
                "Invalid host fields are reported by name.");
            var hostTolerant = new AppLocalization(path, CultureInfo.GetCultureInfo("zh-CN"));
            check(hostTolerant.Language == "en" && !hostTolerant.PreferenceErrorOnRead, "Invalid host fields do not invalidate the language preference.");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":[]}");
            check(new PreferenceStore(path).InvalidHostFields.SequenceEqual(["host"]), "A non-object host value is rejected.");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\"}");
            var languageOnly = new PreferenceStore(path);
            check(languageOnly.InvalidHostFields.Count == 0 && languageOnly.Host.ContainerMode == "safari", "Earlier language-only preference files remain valid.");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"glassStrength\":0.5,\"shadowOpacity\":0.6,\"cornerRadius\":40}}");
            var limits = new PreferenceStore(path);
            check(limits.Host.GlassStrength == .5 && limits.Host.ShadowOpacity is null && limits.Host.CornerRadius == 40,
                "Material values are limited to the host slider ranges.");
            var developer = new HostSettings
            {
                DeveloperAllowLan = true, NativeRemotePort = 9931, CdpPort = 9932, CdpBearerToken = "fixture-agent-token", ControlApiPort = 9934, ControlApiBearerToken = "fixture-control-token",
                ProxyMode = "custom", ProxyEndpoint = "socks5://127.0.0.1:1080"
            };
            check(fresh.SaveHost(developer), "Developer configuration can be saved independently of service state.");
            var restoredDeveloper = new PreferenceStore(path);
            check(JsonSerializer.Serialize(restoredDeveloper.Host) == JsonSerializer.Serialize(developer), "Developer configuration survives restart.");
            check(restoredDeveloper.Host.CdpBearerToken == developer.CdpBearerToken, "The explicitly configured CDP credential survives restart.");
            check(!JsonSerializer.Serialize(restoredDeveloper.Host).Contains(developer.CdpBearerToken, StringComparison.Ordinal), "General host diagnostics do not serialize the CDP credential.");
            check(restoredDeveloper.Host.ControlApiPort == 9934 && restoredDeveloper.Host.ControlApiBearerToken == developer.ControlApiBearerToken, "Control API preferences persist independently of CDP.");
            check(!JsonSerializer.Serialize(restoredDeveloper.Host).Contains(developer.ControlApiBearerToken, StringComparison.Ordinal), "General host diagnostics omit the control credential.");
            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
                check(!saved.RootElement.GetProperty("host").EnumerateObject().Any(property => property.Name.EndsWith("Enabled", StringComparison.Ordinal)),
                    "Listener enablement is session state and is never persisted.");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"developerExpanded\":true,\"materialExpanded\":true,\"diagnosticsExpanded\":true,\"futureField\":true}}");
            var legacyExpansion = new PreferenceStore(path);
            check(legacyExpansion.InvalidHostFields.Count == 0 && legacyExpansion.Host.MaterialExpanded && legacyExpansion.Host.DiagnosticsExpanded,
                "Legacy developer expansion and unknown fields are ignored while material and diagnostics expansion still restore.");
            check(legacyExpansion.SaveHost(legacyExpansion.Host), "Legacy preferences remain writable.");
            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
                check(!saved.RootElement.GetProperty("host").TryGetProperty("developerExpanded", out _),
                    "Developer expansion is session state and is no longer persisted.");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"language\":\"en\",\"host\":{\"nativeRemotePort\":0,\"cdpPort\":65536,\"proxyMode\":\"custom\",\"proxyEndpoint\":\"http://user:secret@127.0.0.1:8080\"}}");
            var invalidDeveloper = new PreferenceStore(path);
            check(invalidDeveloper.Host.NativeRemotePort == 9223 && invalidDeveloper.Host.CdpPort == 9222 &&
                invalidDeveloper.Host.ProxyMode == "system" && invalidDeveloper.Host.ProxyEndpoint == "",
                "Invalid listener ports and authenticated proxy endpoints fall back without starting services.");
            check(invalidDeveloper.InvalidHostFields.Order().SequenceEqual(new[] { "cdpPort", "nativeRemotePort", "proxyEndpoint", "proxyMode" }),
                "Invalid developer configuration fields are reported.");
        }
        finally { Directory.Delete(hostFolder, true); }

        foreach (var valid in new[] { "http://127.0.0.1:8080", "https://proxy.example.test:8443", "socks5://[::1]:1080" })
            check(HostSettings.TryProxyEndpoint(valid, out _), "Supported unauthenticated proxy endpoint: " + valid);
        foreach (var invalid in new[] { "", "proxy.example.test:8080", "ftp://proxy.example.test:21", "socks5://proxy.example.test", "http://proxy.example.test:0", "http://u:p@proxy.example.test:8080", "http://proxy.example.test:8080/path", "http://proxy.example.test:8080/?q=1", "http://proxy.example.test:8080/#x" })
            check(!HostSettings.TryProxyEndpoint(invalid, out _), "Unsupported proxy endpoint: " + invalid);
    }
}
