using System.Text.Json;

namespace DevDuo;

internal static class PageInteraction
{
    internal static string Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", "touch-input.js"));
    internal static string Bootstrap(string source, string mode) => "(" + source + ")(" + JsonSerializer.Serialize(mode) + ");";
    internal static string Update(string mode) => "window.__devduoInput?.configure(" + JsonSerializer.Serialize(mode) + ");";
}
