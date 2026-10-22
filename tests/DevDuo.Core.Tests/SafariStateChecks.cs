using System.Text.Json;
using DevDuo;

internal static class SafariStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var scenes = SafariScene.Load();
        var options = SafariContainerOptions.Load();
        foreach (var mode in new[] { "safari", "app" })
        {
            var identity = options.Copy();
            identity.Mode = mode;
            using var payload = JsonDocument.Parse(identity.ToJson());
            check(payload.RootElement.GetProperty("userAgent").GetString() == BrowserIdentity.MobileUserAgent,
                "Both native containers receive the shared mobile identity before their first navigation.");
        }
        var scrollPolicy = new SafariChromeState();
        check(!scrollPolicy.Observe(false, 500, 500, false, 1000, 24), "Programmatic downward scrolling does not compact Safari chrome.");
        scrollPolicy.RecordInput(1, 1000, "native wheel");
        check(!scrollPolicy.Observe(false, 510, 10, false, 1010, 24), "Small native scroll retains expanded chrome.");
        check(scrollPolicy.Observe(false, 530, 20, false, 1020, 24), "Native downward travel accumulates to compact chrome.");
        check(scrollPolicy.Observe(true, 512, -18, true, 1030, 24), "Viewport clamping cannot expand compact chrome.");
        scrollPolicy.RecordInput(-1, 1040, "native wheel");
        check(!scrollPolicy.Observe(true, 509, -9, false, 1050, 24), "Native reverse scroll expands at nonzero document position.");
        check(scrollPolicy.Observe(true, 0, -509, false, 1500, 24), "Expired native intent cannot authorize a later script scroll.");
        scrollPolicy.RecordInput(1, 1600, "native wheel");
        check(!scrollPolicy.Observe(false, 500, 0, false, 1610, 24), "Blocked native input with no page movement does not compact chrome.");
        check(!scrollPolicy.Observe(false, 490, -10, false, 1620, 24), "Document movement against native intent does not change state.");
        scrollPolicy.Reset();
        check(scrollPolicy.Observe(true, 0, -490, false, 1630, 24), "Reset discards outstanding input before document replacement.");
        foreach (var scene in scenes.Where(scene => scene.Fold == "open"))
        {
            var presentation = options.Copy();
            presentation.ReferenceScene = scene;
            presentation.LogicalWidth = scene.Width;
            var tabs = presentation.TabStripItems;
            check(tabs.Length == 2 && !tabs[0].Active && tabs[1].Active, "Every Open angle presents exactly two tabs, with the second selected.");
            check(tabs[0].VisualFrame.Width == tabs[1].VisualFrame.Width &&
                tabs[0].VisualFrame.X == 18 && tabs[1].VisualFrame.X + tabs[1].VisualFrame.Width == scene.Width - 18,
                "Two equal tabs fill the existing tab track at either Open width.");
            check(tabs[1].CloseFrame is { Width: 32 } close &&
                close.X + close.Width == tabs[1].Frame.X && close.Width + tabs[1].Frame.Width == tabs[1].VisualFrame.Width,
                "The selected surface includes both the close control and title hit frame.");
            using var payload = JsonDocument.Parse(presentation.ToJson());
            check(payload.RootElement.GetProperty("tabStripItems").GetArrayLength() == 2 &&
                payload.RootElement.GetProperty("tabStripItems")[1].GetProperty("active").GetBoolean(),
                "macOS receives the same two-tab presentation as Windows.");
        }
    }
}
