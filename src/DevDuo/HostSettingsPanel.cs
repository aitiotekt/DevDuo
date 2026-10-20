using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
namespace DevDuo;

// Grouped host settings. MainWindow owns the behavior; this view only arranges and labels controls.
public sealed class HostSettingsPanel : Border
{
    private readonly AppLocalization language;
    private readonly List<Action> localize = new();

    public ChoiceGroup Fold { get; } = new();
    public ChoiceGroup Mode { get; } = new();
    public ChoiceGroup Scale { get; } = new();
    public ChoiceGroup ThemeChoice { get; } = new();
    public Button Rotate { get; } = new() { Classes = { "command" }, HorizontalAlignment = HorizontalAlignment.Left };
    public TextBlock Angle { get; } = HostUi.Text("metric");
    public TextBlock RotationNote { get; } = HostUi.Text("hint");
    public ToggleSwitch TabStrip { get; } = Toggle();
    public TextBlock TabStripHint { get; } = HostUi.Text("hint");
    public ToggleSwitch Outline { get; } = Toggle();
    public ComboBox Language { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public Slider Tint { get; } = Range(HostSettings.TintMaximum);
    public Slider Glass { get; } = Range(HostSettings.GlassMaximum);
    public Slider Shadow { get; } = Range(HostSettings.ShadowMaximum);
    public Slider Radius { get; } = Range(HostSettings.RadiusMaximum);
    public Button ResetMaterial { get; } = new() { Classes = { "Basic", "command" }, HorizontalAlignment = HorizontalAlignment.Left };
    public Expander Material { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public Expander Diagnostics { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    public SelectableTextBlock Measurements { get; } = new() { Classes = { "metric" } };
    public Button Save { get; } = new() { Classes = { "command" }, HorizontalAlignment = HorizontalAlignment.Left };
    public TextBlock SaveStatus { get; } = HostUi.Text("hint");
    public Button ResetSettings { get; } = new() { Classes = { "Basic", "command" }, HorizontalAlignment = HorizontalAlignment.Left };
    public TextBlock PreferenceStatus { get; } = HostUi.Text("hint");

    public HostSettingsPanel(AppLocalization language)
    {
        this.language = language;
        Classes.Add("sidebar");
        var device = HostUi.Group(Label("section-title", "Control.Device"),
            HostUi.Field(Label("field-label", "Control.Fold"), Fold),
            HostUi.Field(Label("field-label", "Control.BodyAngle"), new StackPanel
            {
                Spacing = 4,
                Children = { Rotate, Angle }
            }, RotationNote));
        var container = HostUi.Group(Label("section-title", "Group.Container"),
            HostUi.Field(Label("field-label", "Control.Mode"), Mode),
            HostUi.Field(Label("field-label", "Control.TabStrip"), TabStrip, TabStripHint),
            Label("hint", "Device.RebuildNote"));
        var preview = HostUi.Group(Label("section-title", "Group.Preview"),
            HostUi.Field(Label("field-label", "Control.Sizing"), Scale),
            HostUi.Field(Label("field-label", "Control.Outline"), Outline));
        var appearance = HostUi.Group(Label("section-title", "Group.Appearance"),
            HostUi.Field(Label("field-label", "Language.Label"), Language),
            HostUi.Field(Label("field-label", "Control.Theme"), ThemeChoice));

        var tuning = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Slide("Material.Tint", Tint, "F2"),
                Slide("Material.Glass", Glass, "F2"),
                Slide("Material.Shadow", Shadow, "F2"),
                Slide("Material.Radius", Radius, "F0"),
                Label("hint", "Material.Note"),
                ResetMaterial
            }
        };
        Material.Content = tuning;
        localize.Add(() => Material.Header = language.Text("Material.Header"));
        localize.Add(() => ResetMaterial.Content = language.Text("Material.Reset"));

        Diagnostics.Content = new StackPanel { Spacing = 8, Children = { Measurements, Save, SaveStatus } };
        localize.Add(() => Diagnostics.Header = language.Text("Group.Diagnostics"));

        var reset = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 4, 0, 0), Children = { ResetSettings, Label("hint", "Settings.ResetHelp"), PreferenceStatus } };
        localize.Add(() =>
        {
            ResetSettings.Content = language.Text("Settings.Reset");
            AutomationProperties.SetHelpText(ResetSettings, language.Text("Settings.ResetHelp"));
        });

        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(14),
                Children = { device, container, preview, appearance, Material, Diagnostics, reset }
            }
        };
    }

    public void Localize()
    {
        foreach (var update in localize)
            update();
    }

    private static ToggleSwitch Toggle() => new() { OnContent = null, OffContent = null };

    private static Slider Range(double maximum) => new() { Minimum = 0, Maximum = maximum, HorizontalAlignment = HorizontalAlignment.Stretch };

    private TextBlock Label(string className, string key)
    {
        var text = HostUi.Text(className);
        localize.Add(() => text.Text = language.Text(key));
        return text;
    }

    // Slider with a live numeric readout; the readout uses the selected UI culture.
    private Grid Slide(string key, Slider slider, string format)
    {
        var label = Label("field-label", key);
        var value = HostUi.Text("metric");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        void Show() => value.Text = slider.Value.ToString(format, language.Culture);
        slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
                Show();
        };
        localize.Add(Show);
        localize.Add(() => AutomationProperties.SetName(slider, language.Text(key)));
        AutomationProperties.SetLabeledBy(slider, label);
        Grid.SetColumn(value, 1);
        Grid.SetRow(slider, 1);
        Grid.SetColumnSpan(slider, 2);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Children = { label, value, slider }
        };
    }
}
