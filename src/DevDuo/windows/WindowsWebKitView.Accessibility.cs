#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Forms = System.Windows.Forms;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private WebAccessibility? webAccessibility;
    private void AccessibilityChanged(uint nativeEvent, ulong objectId)
    {
        if (!IsReady || webAccessibility is null) return;
        webAccessibility.Invalidate();
        NotifyWinEvent(nativeEvent, browserWindow, -4, 0);
        NotifyWebUia(nativeEvent, objectId);
    }

    private nint GetWebAccessibility(nint parameter)
    {
        if (disposed || !IsReady) return 0;
        webAccessibility ??= new(this);
        webAccessibility.Refresh();
        var pointer = Marshal.GetIUnknownForObject(webAccessibility.Root);
        try
        {
            var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
            return LresultFromObject(ref iid, parameter, pointer);
        }
        finally { Marshal.Release(pointer); }
    }

    [DllImport("oleacc.dll")]
    private static extern nint LresultFromObject(ref Guid interfaceId, nint parameter, nint accessible);
    [DllImport("user32.dll")] private static extern void NotifyWinEvent(uint nativeEvent, nint window, int objectId, int childId);

    // The semantic source is WebCore AX, including ARIA and native editing
    // semantics. No user-page scripts participate in this provider.
    private sealed class WebAccessibility
    {
        private readonly WindowsWebKitView owner;
        private Dictionary<ulong, WebAccessibilityNode> nodes = new();
        private Dictionary<ulong, ulong[]> children = new();
        private readonly Dictionary<ulong, WebAccessibleObject> objects = new();
        private readonly Dictionary<(WebAccessibleObject Model, nint Window), WebUiaProvider> providers = new();
        private readonly Dictionary<(nint Child, nint Root), WebUiaHostBridge> hostBridges = new();
        private bool invalidated = true;
        private long navigation = -1;
        private bool refreshing;
        private string? failure;
        internal object Diagnostics => new { source = "WebCore AX over production IPC", nodeCount = nodes.Count, document = Document, failure, invalidated };
        internal string Document { get; private set; } = "";
        internal ulong RootId { get; private set; }
        internal WebAccessibleObject Root { get; }
        internal nint Window => !owner.IsReady ? 0 : owner.AccessibilityHostWindow != 0 ? owner.AccessibilityHostWindow : owner.browserWindow;
        internal bool IsPresentationChild(nint window) => window != 0 &&
            (window == owner.browserWindow || window == owner.Handle || window == owner.Parent?.Handle);
        internal WebUiaHostBridge HostBridge(nint child, nint root)
        {
            var key = (child, root);
            if (!hostBridges.TryGetValue(key, out var bridge)) hostBridges.Add(key, bridge = new(this, child, root));
            return bridge;
        }
        internal WebUiaProvider Provider(WebAccessibleObject model, nint window = 0)
        {
            if (window == 0) window = Window;
            var key = (model, window);
            if (!providers.TryGetValue(key, out var value))
                providers.Add(key, value = ReferenceEquals(model, Root) ? new WebUiaRootProvider(this, model, window) : new WebUiaProvider(this, model, window));
            return value;
        }

        internal WebAccessibility(WindowsWebKitView owner)
        {
            this.owner = owner;
            Root = new(this, 0, "");
            Root.AttachWindow(owner.browserWindow);
        }

        internal void Refresh(bool force = false)
        {
            if (owner.disposed || !owner.IsReady || refreshing) return;
            // A running process may still be bootstrapping its first page.
            // Never synchronously query AX before a committed document exists.
            if (!owner.committed) { Clear(); invalidated = true; return; }
            if (!force && !invalidated && navigation == owner.navigationGeneration) return;
            refreshing = true;
            // Synchronous IPC can pump a newer native AX notification. Retain
            // that invalidation instead of clearing it when this reply returns.
            invalidated = false;
            var generation = owner.navigationGeneration;
            try
            {
                var value = WKViewCopyHostAccessibilityTree(owner.view);
                if (value == 0) { Clear(); failure = "Native AX query unavailable or timed out."; return; }
                string json;
                try { json = WindowsWebKitApi.String(value); }
                finally { WKRelease(value); }
                if (owner.disposed || generation != owner.navigationGeneration || string.IsNullOrEmpty(json)) { Clear(); return; }
                using var tree = JsonDocument.Parse(json);
                var document = tree.RootElement.GetProperty("document").GetString()!;
                if (document != Document) { objects.Clear(); providers.Clear(); }
                Document = document;
                var next = new Dictionary<ulong, WebAccessibilityNode>();
                RootId = 0;
                foreach (var item in tree.RootElement.GetProperty("nodes").EnumerateArray())
                {
                    var id = ulong.Parse(item.GetProperty("id").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    var parent = ulong.Parse(item.GetProperty("parent").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    next.Add(id, new(id, parent, item.GetProperty("name").GetString()!, item.GetProperty("value").GetString()!,
                        item.GetProperty("description").GetString()!, (Forms.AccessibleRole)item.GetProperty("role").GetInt32(),
                        (Forms.AccessibleStates)item.GetProperty("state").GetInt32(),
                        new(item.GetProperty("x").GetInt32(), item.GetProperty("y").GetInt32(),
                            item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32())));
                    if (parent == 0) RootId = id;
                }
                nodes = next;
                failure = null;
                children = next.Values.GroupBy(node => node.Parent).ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToArray());
                foreach (var id in objects.Keys.Where(id => !nodes.ContainsKey(id)).ToArray())
                {
                    foreach (var key in providers.Keys.Where(key => ReferenceEquals(key.Model, objects[id])).ToArray()) providers.Remove(key);
                    objects.Remove(id);
                }
                navigation = generation;
            }
            catch (JsonException error)
            {
                Clear();
                failure = "Invalid native AX response.";
                System.Diagnostics.Trace.WriteLine(error);
            }
            finally { refreshing = false; }
        }

        internal void Invalidate() => invalidated = true;

        private void Clear() { nodes.Clear(); children.Clear(); objects.Clear(); providers.Clear(); Document = ""; RootId = 0; }
        internal WebAccessibilityNode? Node(ulong id, string document)
        {
            Refresh();
            if (!owner.IsReady || navigation != owner.navigationGeneration || id != 0 && Document != document) return null;
            return nodes.GetValueOrDefault(id == 0 ? RootId : id);
        }
        internal WebAccessibleObject? Object(ulong id)
        {
            if (id == RootId) return Root;
            if (!nodes.ContainsKey(id)) return null;
            if (!objects.TryGetValue(id, out var value)) objects.Add(id, value = new(this, id, Document));
            return value;
        }
        internal WebAccessibleObject[] Children(ulong id) => children.TryGetValue(id, out var values)
            ? values.Select(value => Object(value)!).ToArray() : [];
        internal Rectangle ScreenBounds(Rectangle logical)
        {
            var scale = owner.geometry?.Scale ?? 1;
            var left = (int)Math.Floor(logical.Left * scale);
            var top = (int)Math.Floor(logical.Top * scale);
            var rect = new Rectangle(left, top, (int)Math.Ceiling(logical.Right * scale) - left, (int)Math.Ceiling(logical.Bottom * scale) - top);
            return owner.PresentationBounds?.Invoke(rect) ?? new(owner.PointToScreen(rect.Location), rect.Size);
        }
        internal void Act(WebAccessibilityNode node, string document, uint action, string value = "")
        {
            if (!owner.IsReady || navigation != owner.navigationGeneration || document != Document)
                throw new COMException("The web accessibility object is no longer available.", unchecked((int)0x80040201));
            var token = WKStringCreateWithUTF8CString(document);
            var text = WKStringCreateWithUTF8CString(value);
            try
            {
                if (action == 0) AccessibilitySetFocus(owner.browserWindow);
                if (!WKViewPerformHostAccessibilityAction(owner.view, token, node.Id, action, text))
                    throw new COMException("The web accessibility action is unavailable.", unchecked((int)0x80040201));
            }
            finally { WKRelease(token); WKRelease(text); }
            Refresh(true);
        }
    }

    private sealed record WebAccessibilityNode(ulong Id, ulong Parent, string Name, string Value, string Description,
        Forms.AccessibleRole Role, Forms.AccessibleStates State, Rectangle Bounds);

    [ComVisible(true)]
    private sealed class WebAccessibleObject(WebAccessibility tree, ulong id, string document) : Forms.AccessibleObject
    {
        internal ulong Id => id;
        internal string Document => document;
        internal void AttachWindow(nint window) => UseStdAccessibleObjects(window);
        private WebAccessibilityNode? Node => tree.Node(id, document);
        private WebAccessibilityNode RequiredNode => Node ?? throw new COMException(
            "The web accessibility object is no longer available.", unchecked((int)0x80040201));
        internal void EnsureAvailable() { if (id != 0) _ = RequiredNode; }
        public override string? Name { get => Node?.Name ?? ""; set { } }
        public override string? Description => Node?.Description;
        public override string? Value
        {
            get => Node?.Value;
            set => tree.Act(RequiredNode, tree.Document, 2, value ?? "");
        }
        public override Forms.AccessibleRole Role => Node?.Role ?? Forms.AccessibleRole.None;
        public override string? DefaultAction => Role is Forms.AccessibleRole.PushButton or Forms.AccessibleRole.Link or
            Forms.AccessibleRole.CheckButton or Forms.AccessibleRole.RadioButton or Forms.AccessibleRole.ComboBox
            ? AppLocalization.Current.Text("Accessibility.Press") : null;
        public override Forms.AccessibleStates State => Node?.State ?? Forms.AccessibleStates.Unavailable;
        public override Rectangle Bounds => Node is { } node ? tree.ScreenBounds(node.Bounds) : Rectangle.Empty;
        public override Forms.AccessibleObject? Parent => Node is { Parent: not 0 } node ? tree.Object(node.Parent) : null;
        public override int GetChildCount() => Node is { } node ? tree.Children(node.Id).Length : 0;
        public override Forms.AccessibleObject? GetChild(int index) => Node is { } node && index >= 0 ? tree.Children(node.Id).ElementAtOrDefault(index) : null;
        public override void Select(Forms.AccessibleSelection flags)
        {
            if ((flags & Forms.AccessibleSelection.TakeFocus) != 0) tree.Act(RequiredNode, tree.Document, 0);
        }
        public override void DoDefaultAction() => tree.Act(RequiredNode, tree.Document, 1);
        public override Forms.AccessibleObject? GetFocused()
        {
            if (Node is not { } node) return null;
            if ((node.State & Forms.AccessibleStates.Focused) != 0) return this;
            return tree.Children(node.Id).Select(child => child.GetFocused()).FirstOrDefault(child => child is not null);
        }
        public override Forms.AccessibleObject? HitTest(int x, int y)
        {
            if (!Bounds.Contains(x, y) || Node is not { } node) return null;
            return tree.Children(node.Id).Select(child => child.HitTest(x, y)).FirstOrDefault(child => child is not null) ?? this;
        }
        public override Forms.AccessibleObject? Navigate(Forms.AccessibleNavigation direction)
        {
            if (Node is not { } node) return null;
            var children = tree.Children(node.Id);
            if (direction == Forms.AccessibleNavigation.FirstChild) return children.FirstOrDefault();
            if (direction == Forms.AccessibleNavigation.LastChild) return children.LastOrDefault();
            if (direction is Forms.AccessibleNavigation.Next or Forms.AccessibleNavigation.Previous)
            {
                var siblings = tree.Children(node.Parent);
                var index = Array.IndexOf(siblings, this);
                return siblings.ElementAtOrDefault(index + (direction == Forms.AccessibleNavigation.Next ? 1 : -1));
            }
            return null;
        }
    }
    [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern nint AccessibilitySetFocus(nint window);
}
#endif
