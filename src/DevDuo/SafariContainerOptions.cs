using System.Text.Json;
namespace DevDuo;

public sealed class SafariContainerOptions
{
    // Presentation strings are separate from the measured scene/reference data.
    public string UiLanguage { get; set; } = "en";
    public Dictionary<string, string> UiStrings { get; set; } = new();
    public string Mode { get; set; } = "safari";
    public string ChromeLayout { get; set; } = "side";
    public bool ShowTabStrip { get; set; } = true;
    public double OpenTopSingleExpanded { get; set; } = 80;
    public double TabFontSize { get; set; } = 14;
    public SafariScene? ReferenceScene { get; set; }
    public int RotationClockwiseDegrees { get; set; }
    public double ClosedLandscapeRightNavTop { get; set; } = 24;
    public double ClosedLandscapeLeftNavTop { get; set; } = 82;
    public double ClosedLandscapeRightTabsBottom { get; set; } = 82;
    public double ClosedLandscapeLeftTabsBottom { get; set; } = 34;
    public double LogicalWidth { get; set; } = 466;
    public double LogicalHeight { get; set; } = 678;
    public double TintAlpha { get; set; } = .32;
    public double GlassStrength { get; set; } = .92;
    public double ShadowOpacity { get; set; } = .16;
    public double CornerRadius { get; set; } = 24;
    public double CollapseThresholdCssPx { get; set; } = 24;
    public double ClosedSideWidth { get; set; } = 84;
    public double ClosedBottomExpanded { get; set; } = 98;
    public double ClosedBottomCompactPortrait { get; set; } = 80;
    public double ClosedBottomCompactLandscape { get; set; } = 80;
    public double OpenTopExpanded { get; set; } = 126;
    public double OpenTopCompact { get; set; } = 51;
    public double OpenAddressWidth { get; set; } = 360;
    public double OpenAddressHeight { get; set; } = 44;
    public double OpenCompactWidth { get; set; } = 252;
    public double OpenCompactHeight { get; set; } = 30.8;
    public double OpenAddressTop { get; set; } = 26;
    public double OpenCompactTop { get; set; } = 18.2667;
    public double ClosedAddressHeight { get; set; } = 48;
    public double ClosedAddressBottom { get; set; } = 34;
    public double ClosedAddressMaxWidth { get; set; } = 334;
    public double ClosedCompactWidth { get; set; } = 84;
    public double ClosedCompactHeight { get; set; } = 30;
    public double OpenCompactVisualMinWidth { get; set; } = 69.3;
    public double OpenCompactVisualHeight { get; set; } = 20.7;
    public double ClosedCompactVisualMinWidth { get; set; } = 86.33;
    public double ClosedCompactVisualHeight { get; set; } = 34.67;
    public double CompactFontSize { get; set; } = 12;
    public double ClosedCompactBottom { get; set; } = 34;
    public SafariContainerOptions Copy() => (SafariContainerOptions)MemberwiseClone();
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    public static SafariContainerOptions Load() => JsonSerializer.Deserialize<SafariContainerOptions>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "config", "safari-shell.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}
