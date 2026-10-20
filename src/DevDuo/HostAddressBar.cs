using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using SukiUI.Content;
namespace DevDuo;

// The real host address bar. It is bound to the active native WebView and is separate from the
// simulated Safari address surface inside the measured device scene.
public sealed class HostAddressBar : Border
{
    private readonly AppLocalization language;
    private readonly TextBox address;
    private readonly ProgressBar progress;
    private readonly Button reloadStop;
    private readonly PathIcon reloadIcon = HostUi.Icon(Icons.Refresh), stopIcon = HostUi.Icon("DevDuoIconStop");
    private string committed = "";
    private bool loading, invalid, settingText, editing;

    public Button SidebarToggle { get; }
    public Button Back { get; }
    public Button Forward { get; }
    public Button ColorDemo { get; }
    public Button Calibration { get; }
    public event Action<Uri>? NavigateRequested;
    public event Action? ReloadRequested;
    public event Action? StopRequested;

    public HostAddressBar(AppLocalization language)
    {
        this.language = language;
        Classes.Add("toolbar");
        SidebarToggle = HostUi.ToolbarButton(HostUi.Icon("DevDuoIconSidebar"));
        Back = HostUi.ToolbarButton(HostUi.Icon(Icons.ChevronLeft));
        Forward = HostUi.ToolbarButton(HostUi.Icon(Icons.ChevronRight));
        reloadStop = HostUi.ToolbarButton(reloadIcon);
        reloadStop.Click += (_, _) => (loading ? StopRequested : ReloadRequested)?.Invoke();
        Back.IsEnabled = Forward.IsEnabled = false;

        address = new TextBox { Classes = { "address" }, VerticalAlignment = VerticalAlignment.Center };
        address.AddHandler(KeyDownEvent, OnAddressKeyDown, RoutingStrategies.Tunnel);
        address.TextChanged += (_, _) =>
        {
            if (settingText)
                return;
            editing = true;
            if (invalid)
                SetInvalid(false);
        };
        address.LostFocus += (_, _) =>
        {
            if (address.Text == committed)
                editing = false;
        };
        progress = new ProgressBar { Classes = { "address-progress" }, Minimum = 0, Maximum = 100, IsVisible = false };
        var field = new Panel { Children = { address, progress } };

        ColorDemo = new Button { Classes = { "Basic", "command" } };
        Calibration = new Button { Classes = { "Basic", "command" } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 6 };
        var controls = new Control[] { SidebarToggle, Back, Forward, reloadStop, field, ColorDemo, Calibration };
        for (var index = 0; index < controls.Length; index++)
        {
            Grid.SetColumn(controls[index], index);
            row.Children.Add(controls[index]);
        }
        Child = row;
    }

    public static string Shortcut(string mac, string other) => OperatingSystem.IsMacOS() ? mac : other;

    public void Localize()
    {
        Describe(SidebarToggle, "Nav.ToggleSettings", Shortcut("⌘\\", "Ctrl+\\"));
        Describe(Back, "Nav.Back", Shortcut("⌘[", "Alt+←"));
        Describe(Forward, "Nav.Forward", Shortcut("⌘]", "Alt+→"));
        DescribeReloadStop();
        address.PlaceholderText = language.Text("Address.Placeholder");
        AutomationProperties.SetName(address, language.Text("Address.Name"));
        AutomationProperties.SetHelpText(address, language.Text("Address.Help"));
        AutomationProperties.SetAcceleratorKey(address, Shortcut("⌘L", "Ctrl+L"));
        ToolTip.SetTip(address, language.Format("Shortcut.Format", language.Text("Address.Help"), Shortcut("⌘L", "Ctrl+L")));
        ColorDemo.Content = language.Text("Control.Fixture");
        Calibration.Content = language.Text("Control.Calibration");
        AutomationProperties.SetName(ColorDemo, language.Text("Control.Fixture"));
        AutomationProperties.SetName(Calibration, language.Text("Control.Calibration"));
        if (invalid)
            SetInvalid(true);
    }

    private void Describe(Button button, string key, string shortcut)
    {
        var name = language.Text(key);
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAcceleratorKey(button, shortcut);
        ToolTip.SetTip(button, language.Format("Shortcut.Format", name, shortcut));
    }

    // Applies engine state. Text the developer is editing is never replaced by a navigation update.
    public void Update(NavigationState state, string display)
    {
        Back.IsEnabled = state.CanGoBack;
        Forward.IsEnabled = state.CanGoForward;
        if (loading != state.IsLoading)
        {
            loading = state.IsLoading;
            reloadStop.Content = loading ? stopIcon : reloadIcon;
            DescribeReloadStop();
        }
        progress.IsVisible = loading;
        progress.IsIndeterminate = state.Progress is null;
        progress.Value = (state.Progress ?? 0) * 100;
        committed = display;
        if (!editing)
            SetText(display);
    }

    private void DescribeReloadStop() =>
        Describe(reloadStop, loading ? "Nav.Stop" : "Nav.Reload", loading ? "Esc" : Shortcut("⌘R", "Ctrl+R"));

    public void FocusAddress()
    {
        address.Focus();
        address.SelectAll();
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            if (!CalibrationUrl.TryNormalize(address.Text, out var target))
            {
                SetInvalid(true);
                return;
            }
            editing = false;
            SetText(target.AbsoluteUri);
            NavigateRequested?.Invoke(target);
        }
        else if (args.Key == Key.Escape)
        {
            args.Handled = true;
            if (editing || invalid)
            {
                editing = false;
                SetInvalid(false);
                SetText(committed);
                address.SelectAll();
            }
            else if (loading)
            {
                StopRequested?.Invoke();
            }
        }
    }

    private void SetText(string text)
    {
        settingText = true;
        address.Text = text;
        settingText = false;
    }

    private void SetInvalid(bool value)
    {
        invalid = value;
        if (value)
            DataValidationErrors.SetError(address, new ArgumentException(language.Text("Error.Url")));
        else
            DataValidationErrors.ClearErrors(address);
    }
}
