using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace DevDuo;

public enum StatusTone { Normal, Success, Error }

// Actionable loading/error state on the left; a short measured-viewport summary on the right.
// Detailed telemetry lives in the Diagnostics panel.
public sealed class HostStatusBar : Border
{
    private readonly TextBlock message = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
    private readonly TextBlock viewport = new() { Classes = { "metric" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
    public Button Retry { get; } = new() { Classes = { "Basic", "command" }, IsVisible = false, Padding = new Avalonia.Thickness(8, 0), MinHeight = 24 };

    public HostStatusBar()
    {
        Classes.Add("status-bar");
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        Grid.SetColumn(Retry, 1);
        Grid.SetColumn(viewport, 2);
        Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, Children = { message, Retry, viewport } };
    }

    public void Show(string text, StatusTone tone, bool canRetry, string retryLabel)
    {
        message.Text = text;
        ToolTip.SetTip(message, text);
        message.Classes.Set("error", tone == StatusTone.Error);
        message.Classes.Set("success", tone == StatusTone.Success);
        Retry.IsVisible = canRetry;
        Retry.Content = retryLabel;
        AutomationProperties.SetName(Retry, retryLabel);
    }

    public void SetViewport(string text) => viewport.Text = text;
}
