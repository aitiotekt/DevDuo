using System.Text.Json;
namespace DevDuo;

public sealed class SafariContainerOptions
{
    // Presentation strings are separate from the measured scene/reference data.
    public string UiLanguage { get; set; } = "en";
    public Dictionary<string, string> UiStrings { get; set; } = new();
    public string InteractionMode { get; set; } = "touch";
    public string Mode { get; set; } = "safari";
    public string UserAgent => BrowserIdentity.MobileUserAgent;
    public string ChromeLayout { get; set; } = "side";
    public string ShellAppearance { get; set; } = "light";
    public bool ShowTabStrip { get; set; } = true;
    // Fixed illustrative tabs; original per-capture tab counts/frames stay in ReferenceScene.
    public SafariTabSlot[] TabStripItems
    {
        get
        {
            var width = (LogicalWidth - 36 - 3) / 2;
            var first = new SafariTabFrame(18, 82, width, 32);
            var selected = first with { X = first.X + width + 3 };
            return [new(false, first, first, null),
                new(true, selected with { X = selected.X + 32, Width = width - 32 }, selected, selected with { Width = 32 })];
        }
    }
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
    public WindowsMaterialOptions WindowsMaterial { get; set; } = new();
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

public readonly record struct SafariTabFrame(double X, double Y, double Width, double Height);
public sealed record SafariTabSlot(bool Active, SafariTabFrame Frame, SafariTabFrame VisualFrame, SafariTabFrame? CloseFrame);

public sealed class WindowsMaterialOptions
{
    public double Blur { get; set; } = 14;
    public double Saturation { get; set; } = 1.8;
    public double Brightness { get; set; } = 1.05;
    public double Contrast { get; set; } = 1.02;
    public double TopHighlight { get; set; } = .35;
    public double EdgeHighlight { get; set; } = .30;
    public double Sheen { get; set; } = .08;
    public double ShadowBlur { get; set; } = 10;
    public double ShadowOffset { get; set; } = 3;
}
