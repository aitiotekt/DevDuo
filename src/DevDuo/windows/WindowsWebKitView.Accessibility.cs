#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation.Provider;
using Forms = System.Windows.Forms;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private WebAccessibility? webAccessibility;
    private sealed record AccessibilityRequest(WebAccessibility Tree, TaskCompletionSource<string?> Completion);
    private static readonly Dictionary<nint, AccessibilityRequest> accessibilityRequests = new();
    private static readonly HostAccessibilityTreeCallback accessibilityTreeCallback = AccessibilityTreeFinished;

    private static void AccessibilityTreeFinished(ulong request, nint value, nint context)
    {
        string? json = null;
        try { if (value != 0) json = WindowsWebKitApi.String(value); }
        finally { if (value != 0) WKRelease(value); }
        if (accessibilityRequests.Remove(context, out var pending))
            pending.Completion.TrySetResult(request == (ulong)context ? json : null);
    }
    private void AccessibilityChanged(uint nativeEvent, ulong objectId)
    {
        if (!IsReady || webAccessibility is null) return;
        webAccessibility.Invalidate(nativeEvent, objectId);
        webAccessibility.Refresh(fromNotification: true);
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWinEventHookInstalled(uint nativeEvent);

    // The semantic source is WebCore AX, including ARIA and native editing
    // semantics. No user-page scripts participate in this provider.
    private sealed class WebAccessibility : IDisposable
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
        private bool refreshRequestedByRead;
        private bool disposed;
        private long nextRefreshTick;
        private readonly Forms.Timer refreshTimer = new() { Interval = 100 };
        private ulong snapshotRequests, completedSnapshots;
        private ulong suppressedNotificationRefreshes;
        private double snapshotMilliseconds, maximumSnapshotMilliseconds;
        private readonly HashSet<(uint Event, ulong Object)> pendingEvents = new();
        private string? failure;
        internal object Diagnostics => new { source = "WebCore AX over asynchronous production IPC", nodeCount = nodes.Count, document = Document, failure, invalidated,
            inFlight = refreshing, snapshotRequests, completedSnapshots, snapshotMilliseconds, maximumSnapshotMilliseconds,
            suppressedNotificationRefreshes, notificationSubscribers = HasNotificationSubscribers() };
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
            refreshTimer.Tick += RefreshPending;
        }

        private void RefreshPending(object? sender, EventArgs args)
        {
            refreshTimer.Stop();
            Refresh(fromNotification: !refreshRequestedByRead);
        }

        private bool HasNotificationSubscribers() => AutomationInteropProvider.ClientsAreListening ||
            pendingEvents.Any(item => IsWinEventHookInstalled(item.Event));

        internal async void Refresh(bool force = false, bool fromNotification = false)
        {
            // A past UIA/MSAA query must not turn every future scroll/layout
            // notification into a permanent full-tree walk. Real reads always
            // refresh; notification work requires an actual event consumer.
            if (!fromNotification) refreshRequestedByRead = true;
            if (fromNotification && !HasNotificationSubscribers())
            {
                suppressedNotificationRefreshes++;
                return;
            }
            if (disposed || owner.disposed || !owner.IsReady || refreshing) return;
            // A running process may still be bootstrapping its first page.
            // Never synchronously query AX before a committed document exists.
            if (!owner.committed) { Clear(); invalidated = true; return; }
            if (!force && !invalidated && navigation == owner.navigationGeneration)
            {
                refreshRequestedByRead = false;
                return;
            }
            // AX notifications can arrive once per layout/scroll update. Do not
            // rebuild the whole semantic tree for each property in a UIA walk.
            if (!force && Environment.TickCount64 < nextRefreshTick)
            {
                refreshTimer.Start();
                return;
            }
            refreshing = true;
            refreshRequestedByRead = false;
            invalidated = false;
            var generation = owner.navigationGeneration;
            var notifications = pendingEvents.ToArray();
            pendingEvents.Clear();
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var requestId = (nint)Interlocked.Increment(ref nextToken);
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            accessibilityRequests.Add(requestId, new(this, completion));
            snapshotRequests++;
            try
            {
                WKViewRequestHostAccessibilityTree(owner.view, (ulong)requestId, requestId, accessibilityTreeCallback);
                var json = await completion.Task;
                if (disposed || owner.disposed || generation != owner.navigationGeneration) return;
                if (string.IsNullOrEmpty(json)) { failure = "Native AX query unavailable."; return; }
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
                completedSnapshots++;
                // Announce the published cache. Raising UIA events never starts
                // a synchronous IPC or reads a half-published replacement tree.
                owner.NotifyWebUia(0x8004, RootId);
                NotifyWinEvent(0x8004, owner.browserWindow, -4, 0);
                foreach (var notification in notifications)
                {
                    // Property/focus notifications must describe the published
                    // cache, not the old value read while an IPC is in flight.
                    NotifyWinEvent(notification.Event, owner.browserWindow, -4, 0);
                    if (notification.Event is 0x8005 or 0x800E)
                        owner.NotifyWebUia(notification.Event, notification.Object);
                }
            }
            catch (JsonException error)
            {
                Clear();
                failure = "Invalid native AX response.";
                System.Diagnostics.Trace.WriteLine(error);
            }
            finally
            {
                accessibilityRequests.Remove(requestId);
                snapshotMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                maximumSnapshotMilliseconds = Math.Max(maximumSnapshotMilliseconds, snapshotMilliseconds);
                nextRefreshTick = Environment.TickCount64 + 100;
                refreshing = false;
                if (!invalidated) refreshRequestedByRead = false;
                if (invalidated && !disposed && !owner.disposed && (refreshRequestedByRead || HasNotificationSubscribers()))
                    refreshTimer.Start();
            }
        }

        internal void Invalidate(uint nativeEvent, ulong objectId)
        {
            invalidated = true;
            if (pendingEvents.Count >= 128) pendingEvents.Clear();
            pendingEvents.Add((nativeEvent, nativeEvent is 0x8005 or 0x800E ? objectId : 0));
        }

        public void Dispose()
        {
            disposed = true;
            refreshTimer.Stop();
            refreshTimer.Dispose();
            pendingEvents.Clear();
            foreach (var pending in accessibilityRequests.Where(item => ReferenceEquals(item.Value.Tree, this)).ToArray())
            {
                accessibilityRequests.Remove(pending.Key);
                pending.Value.Completion.TrySetResult(null);
            }
            Clear();
        }

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
