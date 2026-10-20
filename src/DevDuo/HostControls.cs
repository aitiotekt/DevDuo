using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SukiUI.Controls;
namespace DevDuo;

// A labelled single-choice row of SukiUI chips. Selection changes raised by Select() are silent,
// so restoring preferences or refreshing labels never triggers a native rebuild.
public sealed class ChoiceGroup : WrapPanel
{
    private readonly string group = "choice-" + Guid.NewGuid().ToString("N");
    private readonly List<RadioButton> buttons = new();
    private bool updating;
    public event Action<int>? SelectionChanged;
    public int SelectedIndex { get; private set; }

    public ChoiceGroup()
    {
        Orientation = Orientation.Horizontal;
    }

    public void SetItems(IReadOnlyList<string> labels)
    {
        updating = true;
        try
        {
            SelectedIndex = Math.Min(SelectedIndex, Math.Max(0, labels.Count - 1));
            while (buttons.Count < labels.Count)
            {
                var index = buttons.Count;
                var button = new RadioButton { GroupName = group, Classes = { "Chips" } };
                button.IsCheckedChanged += (_, _) =>
                {
                    if (updating || button.IsChecked != true || SelectedIndex == index)
                        return;
                    SelectedIndex = index;
                    SelectionChanged?.Invoke(index);
                };
                buttons.Add(button);
                Children.Add(button);
            }
            for (var index = 0; index < buttons.Count; index++)
            {
                buttons[index].IsVisible = index < labels.Count;
                if (index < labels.Count)
                {
                    buttons[index].Content = labels[index];
                    AutomationProperties.SetName(buttons[index], labels[index]);
                }
                buttons[index].IsChecked = index == SelectedIndex;
            }
        }
        finally
        {
            updating = false;
        }
    }

    public void Select(int index)
    {
        updating = true;
        try
        {
            SelectedIndex = Math.Max(0, index);
            for (var item = 0; item < buttons.Count; item++)
                buttons[item].IsChecked = item == SelectedIndex;
        }
        finally
        {
            updating = false;
        }
    }
}

internal static class HostUi
{
    public static PathIcon Icon(Geometry geometry) => new() { Data = geometry };

    public static PathIcon Icon(string resourceKey) =>
        Application.Current!.TryFindResource(resourceKey, out var value) && value is Geometry geometry
            ? Icon(geometry)
            : new PathIcon();

    public static Button ToolbarButton(PathIcon icon) => new() { Content = icon, Classes = { "Basic", "toolbar" } };

    public static TextBlock Text(string className, TextWrapping wrapping = TextWrapping.Wrap) =>
        new() { Classes = { className }, TextWrapping = wrapping };

    // A titled settings card; the title doubles as an accessibility heading.
    public static GlassCard Group(TextBlock title, params Control[] rows)
    {
        AutomationProperties.SetHeadingLevel(title, 2);
        var stack = new StackPanel { Spacing = 6, Children = { title } };
        foreach (var row in rows)
            stack.Children.Add(row);
        return new GlassCard { Content = stack, Classes = { "setting-group" } };
    }

    // A field label paired with its control so assistive technology reads them together.
    public static StackPanel Field(TextBlock label, Control control, TextBlock? hint = null)
    {
        AutomationProperties.SetLabeledBy(control, label);
        var stack = new StackPanel { Spacing = 2, Children = { label, control } };
        if (hint is not null)
            stack.Children.Add(hint);
        return stack;
    }
}
