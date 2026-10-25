#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChromeKind = DevDuo.WindowsComposition.Kind;
using ChromeAction = DevDuo.WindowsComposition.Action;
namespace DevDuo;

internal sealed class WindowsWebViewPanel : System.Windows.Forms.Panel
{
    private DeviceProfile profile;
    private readonly IntPtr clippingParent;
    private readonly string observer;
    private readonly WindowsWebKitView webkit = new();
    private readonly ChromeWidget address = new(), back = new(), bookmarks = new(), tabs = new(), top = new();
    private SafariContainerOptions options;
    private string? target;
    private string host = "127.0.0.1", title = "", tintSource = "initial fallback";
    private readonly PageTitleState pageTitle = new();
    private bool compact, loading;
    private NavigationError? navigationError;
    public event Action<NavigationState>? NavigationChanged;
    public event Action<HostCommand>? CommandRequested;
    private bool disposed;
    private string? pendingHtml, pendingHtmlBaseUrl, runtimeVersion, pageDocumentId;
    private string initializationState = "initializing";
    private double scrollX, scrollY, actualWidth, actualHeight, clientWidth, clientHeight, visualWidth, visualHeight, pageDevicePixelRatio;
    private System.Windows.Forms.Label? initializationError;
    private string? initializationErrorDetail;
    private long observationCount, layoutCount, telemetryPublicationCount;
    private long touchPanSequence;
    private string? touchPanDocument;
    private string Ui(string key) => options.UiStrings.TryGetValue(key, out var value) ? value : AppLocalization.Lookup("en", key);
    private Color tintColor = Color.FromArgb(239, 243, 248);
    private const string NativeColorSource = "WebKit native edge average";
    private readonly System.Windows.Forms.Timer colorTimer = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer clockTimer = new() { Interval = 1000 };
    private readonly record struct ColorObservationKey(string? Document, double X, double Y, double Width,
        double Height, string Layout, double BackingScale, int Background, double DocumentWidth, double DocumentHeight);
    private ColorObservationKey? colorKey;
    private long colorRevision;
    private bool colorBusy;
    private string colorStatus = "not-requested", clockText = "";
    private Color computedColor;
    private Color? themeColorHint;
    private readonly SafariChromeState chromeScroll = new();
    private WindowsWebKitView.ContentEdgeColor? sampledColor;
    private ChromeAction lastChromeAction;
    internal WindowsWebKitView Browser => webkit;
    internal PointerMessageHandler? PointerMessage;
    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == 0x3D && unchecked((int)message.LParam) == -25)
        {
            message.Result = webkit.GetPresentedUiaProvider(Handle, message.WParam, message.LParam);
            if (message.Result != 0) return;
        }
        if (PointerMessage?.Invoke(ref message) != true) base.WndProc(ref message);
    }
    public WindowsWebViewPanel(DeviceProfile geometry, string? url, SafariContainerOptions configuration, string observerSource, IntPtr nativeParent)
    {
        profile = geometry;
        clippingParent = nativeParent;
        target = url;
        options = configuration;
        observer = observerSource;
        BackColor = tintColor;
        webkit.Observation += ObserveJson;
        webkit.DocumentNavigationStarted += OnDocumentNavigationStarted;
        webkit.FixedContainerColorsChanged += PublishState;
        webkit.CompositionFailed += detail =>
        {
            initializationState = "failed";
            initializationErrorDetail = detail;
            navigationError = new NavigationError("Composition:DeviceUnavailable", detail);
            initializationError ??= new System.Windows.Forms.Label { ForeColor = Color.Black, BackColor = Color.White, Dock = System.Windows.Forms.DockStyle.Fill };
            Controls.Add(initializationError);
            initializationError.BringToFront();
            UpdateLocalizedLabels();
            ReportNavigation();
        };
        webkit.ChromeInvoked += action =>
        {
            if (disposed) return;
            lastChromeAction = action;
            switch (action)
            {
                case WindowsComposition.Action.Reload: Reload(); break;
                case WindowsComposition.Action.Back: GoBack(); break;
                case WindowsComposition.Action.NewTab: compact = false; chromeScroll.Reset(); ApplyLayout(); Evaluate("scrollTo({top:0,behavior:'smooth'})"); break;
                case WindowsComposition.Action.Address:
                case WindowsComposition.Action.Bookmarks:
                case WindowsComposition.Action.Menu:
                case WindowsComposition.Action.Overview:
                    compact = false; ApplyLayout(); break;
            }
        };
        colorTimer.Tick += async (_, _) => await SampleContentColor();
        // The DOM can finish reporting before a delayed image/mask reaches a
        // rendered frame. A successful early snapshot is not the final color.
        webkit.ContentRenderingUpdated += () =>
        {
            if (disposed || options.Mode != "safari" || colorKey is null) return;
            ScheduleContentColor();
        };
        clockTimer.Tick += (_, _) =>
        {
            var nextClock = DateTime.Now.ToString("HH:mm", AppLocalization.Current.Culture);
            if (nextClock == clockText) return;
            clockText = nextClock;
            if (webkit.Chrome.Any(element => element.Kind == WindowsComposition.Kind.Clock)) ApplyLayout();
        };
        clockText = DateTime.Now.ToString("HH:mm", AppLocalization.Current.Culture);
        clockTimer.Start();
        webkit.CommandRequested += command => CommandRequested?.Invoke(command);
        webkit.NativeScrollInput += (direction, source) => chromeScroll.RecordInput(direction, Environment.TickCount64, source);
        webkit.ScrollIndicatorChanged += () => { if (!disposed) PublishState(); };
        webkit.NavigationChanged += state =>
        {
            if (disposed) return;
            if (state.IsLoading && !loading)
            {
                pageDocumentId = null;
                compact = false;
                chromeScroll.Reset();
                actualWidth = actualHeight = scrollX = scrollY = 0;
                tintSource = "initial fallback";
                InvalidateContentColor();
            }
            loading = state.IsLoading;
            pageTitle.UpdateNativeTitle(state.Title);
            title = pageTitle.Title ?? "";
            NavigationChanged?.Invoke(state);
            ApplyLayout();
        };
        Controls.Add(webkit);
        UpdateLocalizedLabels();
        Resize += (_, _) =>
        {
            ApplyMask();
            ApplyLayout();
        };
        LocationChanged += (_, _) => ApplyMask();
        Avalonia.Threading.Dispatcher.UIThread.Post(InitializeBrowser);
    }
    private void InitializeBrowser()
    {
        if (disposed)
            return;
        try
        {
            webkit.SetInteractionMode(options.InteractionMode);
            webkit.Initialize(observer, options.Mode == "safari");
            initializationState = "ready";
            runtimeVersion = "324423@main + DevDuo host viewport API v2 + content painting v2 + content color API v1 + host appearance API v1 + fixed-container color API v1";
            ApplyMask();
            ApplyLayout();
            if (pendingHtml is { } content) webkit.ShowHtml(content, pendingHtmlBaseUrl);
            else if (target is not null) webkit.Navigate(target);
            pendingHtml = null;
            pendingHtmlBaseUrl = null;
        }
        catch (Exception e)
        {
            if (disposed)
                return;
            initializationState = "failed";
            initializationErrorDetail = e.Message;
            navigationError = new NavigationError("WebKit:Initialization", e.Message);
            ReportNavigation();
            initializationError = new System.Windows.Forms.Label
            {
                ForeColor = Color.Black,
                BackColor = Color.White,
                Dock = System.Windows.Forms.DockStyle.Fill
            };
            Controls.Add(initializationError);
            initializationError.BringToFront();
            UpdateLocalizedLabels();
        }
    }
    public void Navigate(string url)
    {
        if (disposed)
            return;
        target = url;
        pendingHtml = null;
        pendingHtmlBaseUrl = null;
        compact = false;
        chromeScroll.Reset();
        ApplyLayout();
        webkit.Navigate(url);
    }
    public void ShowHtml(string html, string? baseUrl = null)
    {
        if (disposed)
            return;
        pendingHtml = html;
        pendingHtmlBaseUrl = baseUrl;
        compact = false;
        chromeScroll.Reset();
        ApplyLayout();
        webkit.ShowHtml(html, baseUrl);
    }
    public void GoBack() { if (!disposed) webkit.GoBack(); }
    public void GoForward() { if (!disposed) webkit.GoForward(); }
    public void Reload() { if (!disposed) webkit.Reload(); }
    public void Stop() { if (!disposed) webkit.Stop(); }
    private void ReportNavigation()
    {
        if (disposed) return;
        NavigationChanged?.Invoke(initializationState == "ready" ? webkit.Navigation :
            new NavigationState(target, null, loading, null, false, false, navigationError));
    }
    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message message, System.Windows.Forms.Keys keys)
    {
        HostCommand? command = keys switch
        {
            System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.L => HostCommand.FocusAddress,
            System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.R or System.Windows.Forms.Keys.F5 => HostCommand.Reload,
            System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Left => HostCommand.Back,
            System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Right => HostCommand.Forward,
            System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S => HostCommand.SaveState,
            System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.OemPipe => HostCommand.ToggleSettings,
            System.Windows.Forms.Keys.F12 or (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.I) => HostCommand.ToggleInspector,
            _ => null
        };
        if (command is not { } value) return base.ProcessCmdKey(ref message, keys);
        CommandRequested?.Invoke(value);
        return true;
    }
    public void Configure(SafariContainerOptions current)
    {
        if (disposed)
            return;
        if (options.ShellAppearance != current.ShellAppearance) InvalidateContentColor();
        options = current;
        webkit.SetInteractionMode(current.InteractionMode);
        UpdateLocalizedLabels();
        ApplyLayout();
    }
    internal void UpdateScene(DeviceProfile geometry, SafariContainerOptions current)
    {
        if (disposed) return;
        profile = geometry;
        compact = false;
        chromeScroll.Reset();
        InvalidateContentColor();
        Configure(current);
        ApplyMask();
    }
    public void Evaluate(string script) => _ = EvaluateResultAsync(script);
    internal Task<string?> EvaluateResultAsync(string script) =>
        disposed ? Task.FromResult<string?>(null) : webkit.EvaluateAsync(script);
    private void UpdateLocalizedLabels()
    {
        address.AccessibleName = Ui("Shell.Address");
        back.AccessibleName = Ui("Shell.Back");
        bookmarks.AccessibleName = Ui("Shell.Bookmarks");
        tabs.AccessibleName = Ui("Shell.TabGroup");
        top.AccessibleName = Ui("Shell.TabStrip");
        if (initializationError is not null)
            initializationError.Text = AppLocalization.Current.Format("Error.WebKit", initializationErrorDetail);
    }
    private void ObserveJson(string json)
    {
        if (disposed) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("type").GetString() != "devduo-shell-observation")
                return;
            if (r.TryGetProperty("documentId", out var documentId))
                pageDocumentId = documentId.GetString();
            if (touchPanDocument != pageDocumentId) { touchPanDocument = pageDocumentId; touchPanSequence = 0; }
            if (options.InteractionMode == "touch" && r.TryGetProperty("pageInput", out var input) && input.ValueKind == JsonValueKind.Object &&
                input.TryGetProperty("panSequence", out var sequence) && sequence.TryGetInt64(out var nextSequence) && nextSequence > touchPanSequence)
            {
                touchPanSequence = nextSequence;
                var direction = input.GetProperty("direction").GetInt32();
                if (direction is -1 or 1) chromeScroll.RecordInput(direction, Environment.TickCount64, "emulated touch pan");
            }
            var previousHeight = actualHeight;
            scrollY = r.GetProperty("scrollY").GetDouble();
            var delta = r.GetProperty("deltaY").GetDouble();
            actualWidth = r.GetProperty("viewportWidth").GetDouble();
            actualHeight = r.GetProperty("viewportHeight").GetDouble();
            clientWidth = r.GetProperty("clientWidth").GetDouble();
            clientHeight = r.GetProperty("clientHeight").GetDouble();
            visualWidth = r.GetProperty("visualWidth").GetDouble();
            visualHeight = r.GetProperty("visualHeight").GetDouble();
            if (r.TryGetProperty("devicePixelRatio", out var ratio))
                pageDevicePixelRatio = ratio.GetDouble();
            host = r.GetProperty("host").GetString() ?? host;
            title = r.GetProperty("title").GetString() ?? "";
            pageTitle.ConfirmDocument(title);
            if (r.TryGetProperty("scrollX", out var horizontalScroll))
                scrollX = horizontalScroll.GetDouble();
            // Changing the unobscured height can clamp scrollY at the document
            // end. That layout adjustment is not an upward scrolling gesture.
            compact = chromeScroll.Observe(compact, scrollY, delta, previousHeight != actualHeight, Environment.TickCount64, options.CollapseThresholdCssPx);
            var theme = r.GetProperty("themeColor");
            computedColor = ReadColor(r.GetProperty("domBackgroundColor"));
            themeColorHint = theme.ValueKind == JsonValueKind.Array ? ReadColor(theme) : null;
            // The controlled Safari cohort follows the rendered page, even with
            // opposing meta hints. Appearance is an independent host input.
            if (tintSource != NativeColorSource)
            {
                tintColor = computedColor;
                tintSource = "computed content background fallback";
            }
            var nextKey = new ColorObservationKey(pageDocumentId, scrollX, scrollY, actualWidth, actualHeight,
                options.ChromeLayout, webkit.BackingScale, computedColor.ToArgb(),
                r.GetProperty("documentWidth").GetDouble(), r.GetProperty("documentHeight").GetDouble());
            if (options.Mode == "safari" && colorKey != nextKey)
            {
                colorKey = nextKey;
                ScheduleContentColor();
            }
            observationCount++;
            ApplyLayout();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IndexOutOfRangeException or ArgumentOutOfRangeException) { }
    }
    private void OnDocumentNavigationStarted()
    {
        pageTitle.BeginNavigation();
        title = "";
        tintSource = "initial fallback";
        InvalidateContentColor();
        ApplyLayout();
    }
    private static Color ReadColor(JsonElement values) => Color.FromArgb(Math.Clamp(values[0].GetInt32(), 0, 255), Math.Clamp(values[1].GetInt32(), 0, 255), Math.Clamp(values[2].GetInt32(), 0, 255));
    private void ScheduleContentColor()
    {
        colorRevision++;
        colorTimer.Stop();
        colorTimer.Start();
    }
    private void InvalidateContentColor()
    {
        colorRevision++;
        colorKey = null;
        colorTimer.Stop();
        sampledColor = null;
        colorStatus = "invalidated";
    }
    private async Task SampleContentColor()
    {
        colorTimer.Stop();
        if (disposed || colorBusy || colorKey is null || options.Mode != "safari") return;
        var revision = colorRevision;
        colorBusy = true;
        colorStatus = "pending";
        try
        {
            var result = await webkit.RequestContentEdgeColorAsync(options.ChromeLayout == "side");
            if (disposed || revision != colorRevision) return;
            sampledColor = result;
            if (result is { Status: 0 })
            {
                tintColor = Color.FromArgb((int)(result.Rgb >> 16) & 255, (int)(result.Rgb >> 8) & 255, (int)result.Rgb & 255);
                tintSource = NativeColorSource;
                colorStatus = "accepted";
            }
            else
            {
                colorStatus = result is null ? "stale-or-unavailable" : "native-status-" + result.Status;
                // A transient readback failure is not a new page color. Keep a
                // successful sample until navigation resets its source instead
                // of flashing between rendered pixels and the DOM approximation.
                if (tintSource == NativeColorSource)
                    colorStatus += "; retained native color";
                else
                {
                    tintColor = computedColor;
                    tintSource = "computed content background fallback";
                }
            }
            ApplyLayout();
        }
        finally
        {
            colorBusy = false;
            if (!disposed && revision != colorRevision && colorKey is not null) colorTimer.Start();
        }
    }
    private void ApplyLayout()
    {
        if (disposed || Width <= 0 || Height <= 0)
            return;
        layoutCount++;
        var o = options;
        var sx = Width / o.LogicalWidth;
        var sy = Height / o.LogicalHeight;
        var safari = o.Mode == "safari";
        webkit.SetDarkAppearance(o.ShellAppearance == "dark");
        var side = o.ChromeLayout == "side";
        var optics = o.WindowsMaterial;
        webkit.Material = new WindowsComposition.Material { Tint = (uint)tintColor.ToArgb() & 0xffffff,
            Alpha = (float)o.TintAlpha, Strength = (float)o.GlassStrength, Shadow = (float)o.ShadowOpacity, Scale = (float)Math.Min(sx, sy),
            Blur = (float)optics.Blur, Saturation = (float)optics.Saturation, Brightness = (float)optics.Brightness, Contrast = (float)optics.Contrast,
            TopHighlight = (float)optics.TopHighlight, EdgeHighlight = (float)optics.EdgeHighlight, Sheen = (float)optics.Sheen,
            ShadowBlur = (float)optics.ShadowBlur, ShadowOffset = (float)optics.ShadowOffset };
        Rectangle Rect(double x, double y, double w, double h) => new((int)Math.Round(x * sx), (int)Math.Round(y * sy), (int)Math.Round(w * sx), (int)Math.Round(h * sy));
        foreach (var c in new[] { top, address, back, bookmarks, tabs })
        {
            c.Radius = o.CornerRadius * sx;
        }
        // Avoid briefly showing then hiding compact controls on every observation,
        // especially when the desktop compositor retains layered child surfaces.
        top.Visible = safari && !side;
        address.Visible = safari;
        back.Visible = bookmarks.Visible = tabs.Visible = safari && (side || !compact);
        var neutral = Color.FromArgb((tintColor.R + 2 * 239) / 3, (tintColor.G + 2 * 243) / 3, (tintColor.B + 2 * 248) / 3);
        BackColor = neutral;
        if (!safari)
        {
            LayoutContent(0, 0, 0, 0, (int)o.LogicalHeight, (int)o.LogicalHeight);
            webkit.Chrome = [];
            webkit.Present();
            PublishState();
            return;
        }
        var w = o.LogicalWidth;
        var h = o.LogicalHeight;
        var addressFrame = address.Bounds;
        var backFrame = back.Bounds;
        var bookmarksFrame = bookmarks.Bounds;
        var tabsFrame = tabs.Bounds;
        var topFrame = top.Bounds;
        if (side)
        {
            var bottom = compact ? (w > h ? o.ClosedBottomCompactLandscape : o.ClosedBottomCompactPortrait) : o.ClosedBottomExpanded;
            var cw = w - o.ClosedSideWidth;
            var left = w > h && o.RotationClockwiseDegrees == 270;
            LayoutContent(0,
                left ? 0 : (int)o.ClosedSideWidth, (int)bottom, left ? (int)o.ClosedSideWidth : 0,
                (int)(h - o.ClosedBottomExpanded), (int)(h - (w > h ? o.ClosedBottomCompactLandscape : o.ClosedBottomCompactPortrait)));
            top.Visible = false;
            var aw = compact ? o.ClosedCompactWidth : Math.Min(o.ClosedAddressMaxWidth, cw - 48);
            var ah = compact ? o.ClosedCompactHeight : o.ClosedAddressHeight;
            var center = w > h ? w / 2 : cw / 2;
            var opticalHeight = compact ? o.ClosedCompactVisualHeight : ah;
            addressFrame = Rect(center - aw / 2, h - (compact ? o.ClosedCompactBottom : o.ClosedAddressBottom) - opticalHeight / 2 - ah / 2, aw, ah);
            var sideX = left ? 24 : w - 72;
            var navTop = w > h ? (left ? o.ClosedLandscapeLeftNavTop : o.ClosedLandscapeRightNavTop) : 174;
            var tabsBottom = w > h ? (left ? o.ClosedLandscapeLeftTabsBottom : o.ClosedLandscapeRightTabsBottom) : 34;
            backFrame = Rect(sideX, navTop, 48, 48);
            bookmarksFrame = Rect(sideX, navTop + 60, 48, 48);
            tabsFrame = Rect(sideX, h - tabsBottom - 102, 48, 102);
        }
        else
        {
            var inset = compact ? o.OpenTopCompact : (o.ShowTabStrip ? o.OpenTopExpanded : o.OpenTopSingleExpanded);
            LayoutContent((int)inset, 0, 0, 0,
                (int)(h - (o.ShowTabStrip ? o.OpenTopExpanded : o.OpenTopSingleExpanded)), (int)(h - o.OpenTopCompact));
            topFrame = Rect(0, 0, w, inset);
            top.Radius = 0;
            top.Text = compact ? "" : pageTitle.DisplayTitle(Ui("Shell.Title"));
            var aw = Math.Min(w - 120, compact ? o.OpenCompactWidth : o.OpenAddressWidth);
            addressFrame = Rect((w - aw) / 2, compact ? o.OpenCompactTop : o.OpenAddressTop, aw, compact ? o.OpenCompactHeight : o.OpenAddressHeight);
            backFrame = Rect(16, 26, 44, 44);
            bookmarksFrame = Rect(72, 26, 44, 44);
            tabsFrame = Rect(w - 232, 26, 88, 44);
            back.Visible = bookmarks.Visible = tabs.Visible = !compact;
        }
        if (o.ReferenceScene is { } reference)
        {
            var states = !side && !o.ShowTabStrip ? reference.SingleStates : reference.States;
            var state = states.GetProperty(compact ? "scrolled" : "initial");
            var frames = state.GetProperty("frames");
            Rectangle NativeRect(JsonElement r) => Rect(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("width").GetDouble(), r.GetProperty("height").GetDouble());
            if (frames.TryGetProperty("address", out var a))
                addressFrame = NativeRect(a);
            if (frames.TryGetProperty("BackButton", out var b))
                backFrame = NativeRect(b);
            if (frames.TryGetProperty("SidebarButton", out var bm))
                bookmarksFrame = NativeRect(bm);
            if (frames.TryGetProperty("NewTabButton", out var nt))
            {
                var r = NativeRect(nt);
                tabsFrame = new Rectangle(r.X, r.Y, (int)Math.Round((side ? 48 : 88) * sx), (int)Math.Round((side ? 102 : 44) * sy));
            }
        }
        // Commit only final measured frames. Moving to defaults and then to
        // reference frames on each live observation leaves compositor ghosts.
        address.Bounds = addressFrame;
        back.Bounds = backFrame;
        bookmarks.Bounds = bookmarksFrame;
        tabs.Bounds = tabsFrame;
        top.Bounds = topFrame;
        top.TabItems = [];
        top.Text = "";
        if (!side && !compact && o.ShowTabStrip)
            top.TabItems = o.TabStripItems;
        var scale = Math.Min(sx, sy);
        address.VisualWidth = compact ? Math.Max((side ? o.ClosedCompactVisualMinWidth : o.OpenCompactVisualMinWidth) * scale,
            webkit.MeasureText(host, (float)(o.CompactFontSize * scale)) + 18 * scale) : 0;
        address.VisualHeight = compact ? (side ? o.ClosedCompactVisualHeight : o.OpenCompactVisualHeight) * sy : 0;
        address.CompactFontPixels = compact ? o.CompactFontSize * scale : 0;
        address.Text = host;
        var surfaces = new[] { top, address, back, bookmarks, tabs };
        var elements = new List<WindowsComposition.Chrome>();
        var names = new List<string>();
        var dark = o.ShellAppearance == "dark";
        var foreground = dark ? 0xffffffu : 0x000000u;
        void Add(ChromeKind kind, ChromeAction action, RectangleF hit, string name, string text = "", float fontSize = 0, float radius = 0)
        {
            elements.Add(new() { Kind = kind, Action = action, X = hit.X, Y = hit.Y, Width = hit.Width, Height = hit.Height,
                VisualWidth = hit.Width, VisualHeight = hit.Height, Radius = radius, FontSize = fontSize > 0 ? fontSize : (float)(17 * scale),
                Foreground = foreground, Text = text });
            names.Add(name);
        }
        for (var index = 0; index < surfaces.Length; index++)
        {
            var control = surfaces[index];
            if (!control.Visible) continue;
            var box = control.VisualBounds;
            var action = index switch { 1 => ChromeAction.Address, 2 => ChromeAction.Back, 3 => ChromeAction.Bookmarks, _ => ChromeAction.None };
            Add(index == 1 && compact ? ChromeKind.CompactAddress : (ChromeKind)index, action, control.Bounds, control.AccessibleName, index == 1 ? host : "",
                (float)(control.CompactFontPixels > 0 ? control.CompactFontPixels : 17 * scale));
            var element = elements[^1];
            element.VisualX = box.X; element.VisualY = box.Y;
            element.VisualWidth = box.Width; element.VisualHeight = box.Height;
            element.Radius = (float)Math.Min(control.Radius, Math.Min(box.Width, box.Height) / 2);
            if (index == 1) element.ClipWidth = Math.Max(1, box.Width - (float)((compact ? 4 : 84) * scale));
            elements[^1] = element;
        }
        if (!compact)
        {
            var box = address.VisualBounds;
            Add(ChromeKind.Menu, ChromeAction.Menu, new((float)(address.Left + box.X + 8 * sx), address.Top + box.Y, (float)(30 * sx), box.Height), Ui("Shell.Menu"));
            Add(ChromeKind.Reload, ChromeAction.Reload, new((float)(address.Left + box.Right - 38 * sx), address.Top + box.Y, (float)(30 * sx), box.Height), Ui("Shell.Reload"));
        }
        if (tabs.Visible)
        {
            Add(ChromeKind.Plus, ChromeAction.NewTab, new(tabs.Left, tabs.Top, (float)((side ? 48 : 44) * sx), (float)((side ? 48 : 44) * sy)), Ui("Shell.NewTab"));
            Add(ChromeKind.Overview, ChromeAction.Overview, new((float)(tabs.Left + (side ? 0 : 44) * sx), (float)(tabs.Top + (side ? 54 : 0) * sy),
                (float)((side ? 48 : 44) * sx), (float)((side ? 48 : 44) * sy)), Ui("Shell.Tabs"));
        }
        if (side)
        {
            var left = w > h && o.RotationClockwiseDegrees == 270;
            var cameraX = left ? 48 : w - 48;
            var cameraY = w > h && !left ? h - 48 : 48;
            Add(ChromeKind.Camera, ChromeAction.None, Rect(cameraX - 18, cameraY - 18, 36, 36), Ui("Shell.Camera"));
        }
        if (side ? w <= h : !compact)
        {
            var root = webkit.UnderPageColor;
            var statusForeground = side ? (root.R * .2126 + root.G * .7152 + root.B * .0722 < .36 * 255 ? 0xffffffu : 0u) : foreground;
            Add(ChromeKind.Clock, ChromeAction.None, side ? Rect(w - 82, 80, 72, 30) : Rect(w - 126, 37, 50, 24),
                string.Format(AppLocalization.Current.Culture, Ui("Shell.Clock"), clockText), clockText, (float)((side ? 21 : 15) * scale));
            var clock = elements[^1]; clock.Foreground = statusForeground; elements[^1] = clock;
            Add(ChromeKind.Wifi, ChromeAction.None, side ? Rect(w - 65, 112, 38, 32) : Rect(w - 65, 37, 34, 24), Ui("Shell.Wifi"));
            var wifi = elements[^1]; wifi.Foreground = statusForeground; elements[^1] = wifi;
        }
        if (top.TabItems.Length > 0)
        {
            Add(ChromeKind.TabTrack, ChromeAction.None, Rect(16, 80, w - 32, 36), Ui("Shell.TabStrip"), radius: (float)(18 * scale));
            var items = top.TabItems;
            RectangleF TabFrame(SafariTabSlot item)
            {
                var r = item.VisualFrame;
                return new((float)(r.X * sx), (float)(r.Y * sy), (float)(r.Width * sx), (float)(r.Height * sy));
            }
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var r = TabFrame(item);
                var active = item.Active;
                var exposedWidth = index + 1 < items.Length ? Math.Min(r.Width, Math.Max(0, TabFrame(items[index + 1]).Left - r.Left - (float)(3 * sx))) : r.Width;
                var fallback = string.Format(AppLocalization.Current.Culture, Ui("Shell.PageTab"), index + 1);
                var label = active ? pageTitle.DisplayTitle(fallback) : fallback;
                Add(active ? ChromeKind.SelectedTab : ChromeKind.Tab, ChromeAction.None, r, label, label, (float)(o.TabFontSize * scale), (float)(16 * scale));
                var element = elements[^1]; element.ClipWidth = exposedWidth; elements[^1] = element;
                if (!active && index + 1 < items.Length && !items[index + 1].Active)
                    Add(ChromeKind.Divider, ChromeAction.None, new(r.Left + exposedWidth + (float)(1.5 * sx), (float)(87 * sy), (float)sx, (float)(22 * sy)), "");
            }
        }
        webkit.Chrome = elements.ToArray();
        webkit.ChromeNames = names.ToArray();
        webkit.Present();
        PublishState();
    }
    private void LayoutContent(int topInset, int rightInset, int bottomInset, int leftInset, int minimumHeight, int maximumHeight)
    {
        webkit.Bounds = ClientRectangle;
        webkit.SetGeometry((int)options.LogicalWidth, (int)options.LogicalHeight,
            Math.Min(Width / options.LogicalWidth, Height / options.LogicalHeight), topInset, rightInset, bottomInset, leftInset, minimumHeight, maximumHeight);
    }
    private void PublishState()
    {
        telemetryPublicationCount++;
        Evaluate("window.devduoContainerTelemetry=" + ReadState() + ";window.dispatchEvent(new CustomEvent('devduo:container',{detail:window.devduoContainerTelemetry}));");
    }
    public string ReadState()
    {
        var dpi = DeviceDpi / 96.0;
        var scale = webkit.BackingScale > 0 ? webkit.BackingScale : 1;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            engine = "Windows WebKit",
            initializationState,
            observerPipeline = new { observations = observationCount, layouts = layoutCount, telemetryPublications = telemetryPublicationCount },
            initializationError = initializationErrorDetail,
            platform = new { osVersion = Environment.OSVersion.Version.ToString(), runtimeVersion, deviceDpi = DeviceDpi },
            webViewZoomFactor = 1,
            scrollbarPolicy = options.Mode == "safari" ? "native root vertical paint suppressed; input-confirmed scene overlay" : "Windows Port native overlay",
            scrollIndicator = webkit.ScrollIndicatorDiagnostics(),
            webKit = webkit.Diagnostics,
            nativeScreenMask = ReadMaskState(),
            pageDevicePixelRatio,
            pageUrl = webkit.Navigation.Url,
            pageTitle = title,
            pageDocumentId,
            nativeWebViewFramePixels = new { x = webkit.Left, y = webkit.Top, width = webkit.Width, height = webkit.Height },
            renderingStrategy = "full native WebKit frame; real obscured insets; independent logical scene/backing scale",
            underChromeContentContinuation = initializationState == "ready" && webkit.IsReady && options.Mode == "safari",
            containerMode = options.Mode == "safari" ? "safari-approximation" : "app-webview",
            chromeState = options.Mode == "safari" ? (compact ? "compact" : "expanded") : "absent",
            chromeVisible = options.Mode == "safari",
            addressGeometryActive = options.Mode == "safari",
            chromeLayout = options.ChromeLayout,
            tabStripVisible = options.Mode == "safari" && options.ChromeLayout != "side" && !compact && options.ShowTabStrip,
            referenceScene = options.ReferenceScene?.Id,
            bodyRotationDegrees = options.ReferenceScene?.BodyRotationDegrees,
            rotationClockwiseDegrees = options.RotationClockwiseDegrees,
            sidebarSide = options.Mode == "safari" && options.ChromeLayout == "side" ? (options.LogicalWidth > options.LogicalHeight && options.RotationClockwiseDegrees == 270 ? "left" : "right") : "absent",
            scrollY,
            requestedSceneCssPx = new
            {
                width = options.LogicalWidth,
                height = options.LogicalHeight
            },
            nativeWebViewFrameDip = new
            {
                x = webkit.Left / dpi,
                y = webkit.Top / dpi,
                width = webkit.Width / dpi,
                height = webkit.Height / dpi
            },
            addressHitFrameDip = new
            {
                x = address.Left / dpi,
                y = address.Top / dpi,
                width = address.Width / dpi,
                height = address.Height / dpi
            },
            addressVisualFrameDip = new
            {
                x = (address.Left + address.VisualBounds.X) / dpi,
                y = (address.Top + address.VisualBounds.Y) / dpi,
                width = address.VisualBounds.Width / dpi,
                height = address.VisualBounds.Height / dpi
            },
            actualPageViewport = new
            {
                width = actualWidth,
                height = actualHeight,
                clientWidth,
                clientHeight,
                visualWidth,
                visualHeight
            },
            tintSource,
            lastChromeAction = lastChromeAction.ToString(),
            statusWidgets = new { cameraVisible = webkit.Chrome.Any(element => element.Kind == ChromeKind.Camera),
                wifiVisible = webkit.Chrome.Any(element => element.Kind == ChromeKind.Wifi),
                clockVisible = webkit.Chrome.Any(element => element.Kind == ChromeKind.Clock), clockText },
            chromeElements = webkit.Chrome.Select((element, index) => new { kind = element.Kind.ToString(), action = element.Action.ToString(),
                name = index < webkit.ChromeNames.Length ? webkit.ChromeNames[index] : "",
                hitFrameSceneCssPx = new { x = element.X / scale, y = element.Y / scale, width = element.Width / scale, height = element.Height / scale },
                visualFrameSceneCssPx = new { x = (element.X + element.VisualX) / scale, y = (element.Y + element.VisualY) / scale,
                    width = element.VisualWidth / scale, height = element.VisualHeight / scale } }),
            contentColor = new { apiVersion = 1, revision = colorRevision, inFlight = colorBusy, status = colorStatus,
                sampleRectSceneCssPx = sampledColor is { } sample ? new { x = sample.SampleRect.X, y = sample.SampleRect.Y,
                    width = sample.SampleRect.Width, height = sample.SampleRect.Height } : null,
                navigationGeneration = sampledColor?.NavigationGeneration, geometryGeneration = sampledColor?.GeometryGeneration },
            tintRgb = new[] { (int)tintColor.R, (int)tintColor.G, (int)tintColor.B },
            shellAppearance = options.ShellAppearance,
            chromeScrollInput = new { source = chromeScroll.InputSource, sequence = chromeScroll.InputSequence,
                policy = "native intent plus matching document motion; 350ms correlation; viewport clamps excluded" },
            themeColorHintRgb = themeColorHint is { } hint ? new[] { (int)hint.R, (int)hint.G, (int)hint.B } : null,
            themeColorPolicy = "observed hint only; rendered backdrop and shell appearance are independent",
            materialBackend = options.Mode == "safari" ? "Windows live WebKit surface with DirectComposition blur and Direct2D chrome" : "none (App WebView)",
            inactiveMaterialBackend = options.Mode == "safari" ? null : "Windows live WebKit surface with DirectComposition blur and Direct2D chrome",
            materialParameters = options,
            opticalStatus = "Live native surface blur; iOS optical kernel/refraction unknown; presented acceptance is separate"
        });
    }
    private void ApplyMask()
    {
        if (disposed || string.IsNullOrWhiteSpace(profile.ScreenMaskPath) || Width <= 0 || Height <= 0)
            return;
        using var path = ParsePath(profile.ScreenMaskPath);
        using var matrix = new Matrix((float)(Width / profile.MaskCanvasWidth), 0, 0, (float)(Height / profile.MaskCanvasHeight), 0, 0);
        path.Transform(matrix);
        var previousMask = Region;
        Region = new Region(path);
        previousMask?.Dispose();
        // The physical presenter applies the rotated mask to its own HWND and the
        // Avalonia holder. This source keeps the independent mask in scene coordinates.
    }
    private object ReadMaskState()
    {
        var points = new[] { new Point(0, 0), new Point(Width - 1, 0), new Point(Width - 1, Height - 1), new Point(0, Height - 1) };
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            var holderRegionType = GetWindowRgn(clippingParent, region);
            // The preview scroll viewport may cut through the middle of the screen.
            // Check the full mask's corners, not the rectangular visible crop's corners.
            GetRgnBox(region, out var holderBounds);
            // Sample the outermost pixels. Two pixels inward can legitimately
            // fall inside a small measured hinge corner at fractional Fit.
            var holderCorners = new[] { new Point(holderBounds.Left, holderBounds.Top), new Point(holderBounds.Right - 1, holderBounds.Top),
                new Point(holderBounds.Right - 1, holderBounds.Bottom - 1), new Point(holderBounds.Left, holderBounds.Bottom - 1) }
                .Select(point => PtInRegion(region, point.X, point.Y)).ToArray();
            var childRegionType = GetWindowRgn(Handle, region);
            var childCorners = points.Select(point => PtInRegion(region, point.X, point.Y)).ToArray();
            return new { backend = "screen path on hosted HWND and Avalonia holder HWND", holderRegionType, childRegionType, holderCorners, childCorners };
        }
        finally { DeleteObject(region); }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMask();
    }
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out NativeRect bounds);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        if (disposed || Disposing || !webkit.IsReady) return;
        ApplyMask();
        ApplyLayout();
    }
    internal static GraphicsPath ParsePath(string svg)
    {
        var tokens = Regex.Matches(svg, @"[MLCZ]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?").Select(m => m.Value).ToArray();
        var path = new GraphicsPath();
        var i = 0;
        PointF cursor = default;
        float Number() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);
        PointF Point() => new(Number(), Number());
        while (i < tokens.Length)
        {
            switch (tokens[i++])
            {
                case "M":
                    path.StartFigure();
                    cursor = Point();
                    break;
                case "L":
                    var next = Point();
                    path.AddLine(cursor, next);
                    cursor = next;
                    break;
                case "C":
                    var a = Point();
                    var b = Point();
                    var end = Point();
                    path.AddBezier(cursor, a, b, end);
                    cursor = end;
                    break;
                case "Z":
                    path.CloseFigure();
                    break;
                default:
                    throw new FormatException("Unsupported mask command");
            }
        }
        return path;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            colorTimer.Stop(); colorTimer.Dispose();
            clockTimer.Stop(); clockTimer.Dispose();
            webkit.Observation -= ObserveJson;
            webkit.DocumentNavigationStarted -= OnDocumentNavigationStarted;
            webkit.FixedContainerColorsChanged -= PublishState;
            var mask = Region;
            Region = null;
            mask?.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class ChromeWidget
    {
        public Rectangle Bounds { get; set; }
        public int Left => Bounds.Left;
        public int Top => Bounds.Top;
        public int Width => Bounds.Width;
        public int Height => Bounds.Height;
        public bool Visible { get; set; }
        public string Text { get; set; } = "";
        public string AccessibleName { get; set; } = "";
        public double Radius { get; set; }
        public double VisualWidth { get; set; }
        public double VisualHeight { get; set; }
        public double CompactFontPixels { get; set; }
        public SafariTabSlot[] TabItems { get; set; } = [];
        public RectangleF VisualBounds => new((float)(VisualWidth > 0 ? (Width - VisualWidth) / 2 : 0),
            (float)(VisualHeight > 0 ? (Height - VisualHeight) / 2 : 0),
            (float)(VisualWidth > 0 ? VisualWidth : Math.Max(1, Width)),
            (float)(VisualHeight > 0 ? VisualHeight : Math.Max(1, Height)));
    }

}
#endif
