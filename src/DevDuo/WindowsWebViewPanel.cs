#if WINDOWS
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace DevDuo;

internal sealed class WindowsWebViewPanel : System.Windows.Forms.Panel
{
    private readonly DeviceProfile profile;
    private readonly string observer;
    private readonly WebView2 browser = new();
    private readonly ChromeSurface address = new(), back = new(), bookmarks = new(), tabs = new(), top = new();
    private readonly System.Windows.Forms.Timer colorTimer = new() { Interval = 200 };
    private SafariContainerOptions options;
    private string? target;
    private string host = "127.0.0.1", title = "Safari approximation", tintSource = "initial fallback";
    private bool compact, hasTheme, capturing, loading;
    private NavigationError? navigationError;
    public event Action<NavigationState>? NavigationChanged;
    public event Action<HostCommand>? CommandRequested;
    private double scrollY, actualWidth, actualHeight;
    private System.Windows.Forms.Label? initializationError;
    private string? initializationErrorDetail;
    private string? pendingHtml;
    private string Ui(string key) => options.UiStrings.TryGetValue(key, out var value) ? value : AppLocalization.Lookup("en", key);
    private Color tintColor = Color.FromArgb(239, 243, 248);
    // A null URL leaves the browser blank until the host loads the color laboratory.
    public WindowsWebViewPanel(DeviceProfile geometry, string? url, SafariContainerOptions configuration, string observerSource)
    {
        profile = geometry;
        target = url;
        options = configuration;
        observer = observerSource;
        BackColor = tintColor;
        Controls.Add(browser);
        foreach (var c in new[] { top, address, back, bookmarks, tabs })
            Controls.Add(c);
        browser.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "WebView2") };
        UpdateLocalizedLabels();
        back.Click += (_, _) =>
        {
            if (browser.CoreWebView2?.CanGoBack == true)
                browser.CoreWebView2.GoBack();
        };
        address.Click += (_, _) =>
        {
            if (compact)
            {
                compact = false;
                ApplyLayout();
            }
            else
            {
                browser.CoreWebView2?.Reload();
            }
        };
        bookmarks.Click += (_, _) =>
        {
            compact = false;
            ApplyLayout();
        };
        tabs.Click += async (_, _) =>
        {
            if (browser.CoreWebView2 is { } core)
                await core.ExecuteScriptAsync("scrollTo({top:0,behavior:'smooth'})");
        };
        Resize += (_, _) =>
        {
            ApplyMask();
            ApplyLayout();
        };
        colorTimer.Tick += async (_, _) =>
        {
            colorTimer.Stop();
            await SampleContent();
        };
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await browser.EnsureCoreWebView2Async();
                await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(observer);
                browser.CoreWebView2.WebMessageReceived += Observation;
                browser.CoreWebView2.NavigationStarting += (_, _) =>
                {
                    compact = false;
                    ApplyLayout();
                    loading = true;
                    navigationError = null;
                    ReportNavigation();
                };
                browser.CoreWebView2.SourceChanged += (_, _) => ReportNavigation();
                browser.CoreWebView2.DocumentTitleChanged += (_, _) => ReportNavigation();
                browser.CoreWebView2.HistoryChanged += (_, _) => ReportNavigation();
                browser.CoreWebView2.NavigationCompleted += (_, args) =>
                {
                    loading = false;
                    // A superseded or stopped navigation is not a page failure.
                    if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        navigationError = new NavigationError("WebView2:" + args.WebErrorStatus, args.WebErrorStatus.ToString());
                    ReportNavigation();
                };
                if (pendingHtml is not null)
                    browser.CoreWebView2.NavigateToString(pendingHtml);
                else if (target is not null)
                    browser.CoreWebView2.Navigate(target);
                pendingHtml = null;
            }
            catch (Exception e)
            {
                initializationErrorDetail = e.Message;
                navigationError = new NavigationError("WebView2:Initialization", e.Message);
                ReportNavigation();
                initializationError = new System.Windows.Forms.Label
                {
                    ForeColor = Color.Black,
                    Dock = System.Windows.Forms.DockStyle.Fill
                };
                Controls.Clear();
                Controls.Add(initializationError);
                UpdateLocalizedLabels();
            }
        });
    }
    public void Navigate(string url)
    {
        target = url;
        pendingHtml = null;
        compact = false;
        ApplyLayout();
        browser.CoreWebView2?.Navigate(url);
    }
    public void ShowHtml(string html)
    {
        compact = false;
        ApplyLayout();
        if (browser.CoreWebView2 is { } core)
            core.NavigateToString(html);
        else
            pendingHtml = html; // Loaded once asynchronous initialization completes.
    }
    public void GoBack()
    {
        if (browser.CoreWebView2?.CanGoBack == true)
            browser.CoreWebView2.GoBack();
    }
    public void GoForward()
    {
        if (browser.CoreWebView2?.CanGoForward == true)
            browser.CoreWebView2.GoForward();
    }
    public void Reload() => browser.CoreWebView2?.Reload();
    public void Stop() => browser.CoreWebView2?.Stop();
    private void ReportNavigation()
    {
        if (IsDisposed)
            return;
        var core = browser.CoreWebView2;
        NavigationChanged?.Invoke(new NavigationState(core?.Source ?? target, core?.DocumentTitle, loading, null,
            core?.CanGoBack == true, core?.CanGoForward == true, navigationError));
    }
    // WebView2 forwards unhandled accelerator keys through the WinForms command-key chain.
    // Runtime behavior is a Windows native acceptance item.
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
            _ => null
        };
        if (command is { } value)
        {
            CommandRequested?.Invoke(value);
            return true;
        }
        return base.ProcessCmdKey(ref message, keys);
    }
    public void Configure(SafariContainerOptions current)
    {
        options = current;
        UpdateLocalizedLabels();
        ApplyLayout();
    }
    public void Evaluate(string script)
    {
        if (browser.CoreWebView2 is { } core)
            _ = core.ExecuteScriptAsync(script);
    }
    private void UpdateLocalizedLabels()
    {
        address.AccessibleName = Ui("Shell.Address");
        back.AccessibleName = Ui("Shell.Back");
        bookmarks.AccessibleName = Ui("Shell.Bookmarks");
        tabs.AccessibleName = Ui("Shell.TabGroup");
        top.AccessibleName = Ui("Shell.TabStrip");
        top.UiStrings = options.UiStrings;
        if (initializationError is not null)
            initializationError.Text = AppLocalization.Current.Format("Error.WebView2", initializationErrorDetail);
    }
    private void Observation(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var r = doc.RootElement;
            if (r.GetProperty("type").GetString() != "devduo-shell-observation")
                return;
            scrollY = r.GetProperty("scrollY").GetDouble();
            var delta = r.GetProperty("deltaY").GetDouble();
            actualWidth = r.GetProperty("viewportWidth").GetDouble();
            actualHeight = r.GetProperty("viewportHeight").GetDouble();
            host = r.GetProperty("host").GetString() ?? host;
            title = r.GetProperty("title").GetString() ?? title;
            if (scrollY <= 2 || delta < -8)
                compact = false;
            else if (scrollY > options.CollapseThresholdCssPx && delta > 1)
                compact = true;
            var theme = r.GetProperty("themeColor");
            hasTheme = theme.ValueKind == JsonValueKind.Array;
            if (hasTheme)
            {
                tintColor = ReadColor(theme);
                tintSource = "document theme-color";
                colorTimer.Stop();
            }
            else
            {
                if (tintSource != "WebView2 native preview edge sample")
                {
                    tintColor = ReadColor(r.GetProperty("domBackgroundColor"));
                    tintSource = "computed content background fallback";
                }
                colorTimer.Stop();
                colorTimer.Start();
            }
            ApplyLayout();
            PublishState();
        }
        catch (JsonException) { }
    }
    private static Color ReadColor(JsonElement values) => Color.FromArgb(Math.Clamp(values[0].GetInt32(), 0, 255), Math.Clamp(values[1].GetInt32(), 0, 255), Math.Clamp(values[2].GetInt32(), 0, 255));
    private async Task SampleContent()
    {
        if (capturing || hasTheme || browser.CoreWebView2 is null || IsDisposed)
            return;
        capturing = true;
        try
        {
            using var stream = new MemoryStream();
            await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Position = 0;
            using var image = new Bitmap(stream);
            long red = 0, green = 0, blue = 0, count = 0;
            var y = options.ChromeLayout == "side" ? Math.Max(0, image.Height - 4) : Math.Min(4, image.Height - 1);
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
            {
                var c = image.GetPixel(x, y);
                red += c.R;
                green += c.G;
                blue += c.B;
                count++;
            }
            if (count > 0 && !hasTheme && !IsDisposed)
            {
                tintColor = Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count));
                tintSource = "WebView2 native preview edge sample";
                ApplyLayout();
                PublishState();
            }
        }
        catch (Exception) {/* Keep the explicitly labeled computed-style fallback. */}
        finally { capturing = false; }
    }
    private void ApplyLayout()
    {
        if (Width <= 0 || Height <= 0)
            return;
        var o = options;
        var sx = Width / o.LogicalWidth;
        var sy = Height / o.LogicalHeight;
        browser.ZoomFactor = Math.Max(.25, Math.Min(5, Math.Min(sx, sy)));
        var safari = o.Mode == "safari";
        var side = o.ChromeLayout == "side";
        Rectangle Rect(double x, double y, double w, double h) => new((int)Math.Round(x * sx), (int)Math.Round(y * sy), (int)Math.Round(w * sx), (int)Math.Round(h * sy));
        foreach (var c in new[] { top, address, back, bookmarks, tabs })
        {
            c.Visible = safari;
            c.Tint = tintColor;
            c.Alpha = o.TintAlpha;
            c.GlassBlend = o.GlassStrength;
            c.Shadow = o.ShadowOpacity;
            c.Radius = o.CornerRadius * sx;
        }
        var neutral = Color.FromArgb((tintColor.R + 2 * 239) / 3, (tintColor.G + 2 * 243) / 3, (tintColor.B + 2 * 248) / 3);
        BackColor = neutral;
        if (!safari)
        {
            browser.Bounds = ClientRectangle;
            return;
        }
        var w = o.LogicalWidth;
        var h = o.LogicalHeight;
        if (side)
        {
            var bottom = compact ? (w > h ? o.ClosedBottomCompactLandscape : o.ClosedBottomCompactPortrait) : o.ClosedBottomExpanded;
            var cw = w - o.ClosedSideWidth;
            var left = w > h && o.RotationClockwiseDegrees == 270;
            browser.Bounds = Rect(left ? o.ClosedSideWidth : 0, 0, cw, h - bottom);
            top.Visible = false;
            var aw = compact ? o.ClosedCompactWidth : Math.Min(o.ClosedAddressMaxWidth, cw - 48);
            var ah = compact ? o.ClosedCompactHeight : o.ClosedAddressHeight;
            var center = w > h ? w / 2 : cw / 2;
            var opticalHeight = compact ? o.ClosedCompactVisualHeight : ah;
            address.Bounds = Rect(center - aw / 2, h - (compact ? o.ClosedCompactBottom : o.ClosedAddressBottom) - opticalHeight / 2 - ah / 2, aw, ah);
            var sideX = left ? 24 : w - 72;
            var navTop = w > h ? (left ? o.ClosedLandscapeLeftNavTop : o.ClosedLandscapeRightNavTop) : 174;
            var tabsBottom = w > h ? (left ? o.ClosedLandscapeLeftTabsBottom : o.ClosedLandscapeRightTabsBottom) : 34;
            back.Bounds = Rect(sideX, navTop, 48, 48);
            bookmarks.Bounds = Rect(sideX, navTop + 60, 48, 48);
            tabs.Bounds = Rect(sideX, h - tabsBottom - 102, 48, 102);
            tabs.VerticalIcons = true;
        }
        else
        {
            var inset = compact ? o.OpenTopCompact : (o.ShowTabStrip ? o.OpenTopExpanded : o.OpenTopSingleExpanded);
            browser.Bounds = Rect(0, inset, w, h - inset);
            top.Bounds = Rect(0, 0, w, inset);
            top.Radius = 0;
            top.Text = compact ? "" : title;
            var aw = Math.Min(w - 120, compact ? o.OpenCompactWidth : o.OpenAddressWidth);
            address.Bounds = Rect((w - aw) / 2, compact ? o.OpenCompactTop : o.OpenAddressTop, aw, compact ? o.OpenCompactHeight : o.OpenAddressHeight);
            back.Bounds = Rect(16, 26, 44, 44);
            bookmarks.Bounds = Rect(72, 26, 44, 44);
            tabs.Bounds = Rect(w - 112, 26, 88, 44);
            back.Visible = bookmarks.Visible = tabs.Visible = !compact;
            tabs.VerticalIcons = false;
        }
        if (o.ReferenceScene is { } reference)
        {
            var states = !side && !o.ShowTabStrip ? reference.SingleStates : reference.States;
            var state = states.GetProperty(compact ? "scrolled" : "initial");
            var frames = state.GetProperty("frames");
            Rectangle NativeRect(JsonElement r) => Rect(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("width").GetDouble(), r.GetProperty("height").GetDouble());
            if (frames.TryGetProperty("address", out var a))
                address.Bounds = NativeRect(a);
            if (frames.TryGetProperty("BackButton", out var b))
                back.Bounds = NativeRect(b);
            if (frames.TryGetProperty("SidebarButton", out var bm))
                bookmarks.Bounds = NativeRect(bm);
            if (frames.TryGetProperty("NewTabButton", out var nt))
            {
                var r = NativeRect(nt);
                tabs.Bounds = new Rectangle(r.X, r.Y, (int)Math.Round((side ? 48 : 88) * sx), (int)Math.Round((side ? 102 : 44) * sy));
            }
        }
        top.TabItems = default;
        top.Text = "";
        top.SceneScaleX = sx;
        top.SceneScaleY = sy;
        top.TabFontSize = o.TabFontSize;
        if (!side && !compact && o.ShowTabStrip && o.ReferenceScene is { } tabScene)
            top.TabItems = tabScene.States.GetProperty("initial").GetProperty("tabs");
        using var compactFont = new Font("Segoe UI", (float)o.CompactFontSize);
        address.VisualWidth = compact ? Math.Max(side ? o.ClosedCompactVisualMinWidth : o.OpenCompactVisualMinWidth, System.Windows.Forms.TextRenderer.MeasureText(host, compactFont).Width / sx + 18) * sx : 0;
        address.VisualHeight = compact ? (side ? o.ClosedCompactVisualHeight : o.OpenCompactVisualHeight) * sy : 0;
        address.CompactFontPixels = compact ? o.CompactFontSize * sx : 0;
        address.Text = compact ? host : "☰   " + host + "   ↻";
        back.Glyph = "back";
        bookmarks.Glyph = "book";
        tabs.Glyph = "tabs";
        foreach (var c in new[] { top, address, back, bookmarks, tabs })
        {
            c.BringToFront();
            c.Invalidate();
        }
        PublishState();
    }
    private void PublishState()
    {
        if (browser.CoreWebView2 is { } core)
            _ = core.ExecuteScriptAsync("window.devduoContainerTelemetry=" + ReadState() + ";window.dispatchEvent(new CustomEvent('devduo:container',{detail:window.devduoContainerTelemetry}));");
    }
    public string ReadState()
    {
        var dpi = DeviceDpi / 96.0;
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            engine = "Windows Microsoft Edge WebView2",
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
                x = browser.Left / dpi,
                y = browser.Top / dpi,
                width = browser.Width / dpi,
                height = browser.Height / dpi
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
                height = actualHeight
            },
            tintSource,
            tintRgb = new[] { (int)tintColor.R, (int)tintColor.G, (int)tintColor.B },
            materialBackend = options.Mode == "safari" ? "Windows GDI tint/alpha approximation; no native backdrop blur" : "none (App WebView)",
            inactiveMaterialBackend = options.Mode == "safari" ? null : "Windows GDI tint/alpha approximation; no native backdrop blur",
            materialParameters = options,
            opticalStatus = "Windows material fallback; iOS optical kernel/refraction unknown; runtime validation pending"
        });
    }
    private void ApplyMask()
    {
        if (string.IsNullOrWhiteSpace(profile.ScreenMaskPath) || Width <= 0 || Height <= 0)
            return;
        using var path = ParsePath(profile.ScreenMaskPath);
        using var matrix = new Matrix((float)(Width / profile.MaskCanvasWidth), 0, 0, (float)(Height / profile.MaskCanvasHeight), 0, 0);
        path.Transform(matrix);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }
    private static GraphicsPath ParsePath(string svg)
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
        if (disposing)
        {
            colorTimer.Stop();
            colorTimer.Dispose();
            browser.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class ChromeSurface : System.Windows.Forms.Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Tint { get; set; } = Color.White;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Alpha { get; set; } = .32;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double GlassBlend { get; set; } = .92;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Shadow { get; set; } = .16;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double Radius { get; set; } = 24;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Dictionary<string, string> UiStrings { get; set; } = new();
        private string Ui(string key) => UiStrings.TryGetValue(key, out var value) ? value : AppLocalization.Lookup("en", key);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public JsonElement TabItems
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double SceneScaleX { get; set; } = 1;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double SceneScaleY { get; set; } = 1;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public double TabFontSize { get; set; } = 14;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Glyph { get; set; } = "";
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool VerticalIcons
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double VisualWidth
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double VisualHeight
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double CompactFontPixels
        {
            get; set;
        }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public RectangleF VisualBounds => new((float)(VisualWidth > 0 ? (Width - VisualWidth) / 2 : 2), (float)(VisualHeight > 0 ? (Height - VisualHeight) / 2 : 2), (float)(VisualWidth > 0 ? VisualWidth : Math.Max(1, Width - 5)), (float)(VisualHeight > 0 ? VisualHeight : Math.Max(1, Height - 5)));
        public ChromeSurface()
        {
            SetStyle(System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var box = VisualBounds;
            using var path = Rounded(box, (float)Math.Min(Radius, Math.Min(box.Width, box.Height) / 2));
            using var shade = new SolidBrush(Color.FromArgb((int)(Math.Clamp(Shadow, 0, 1) * 255), 0, 0, 0));
            e.Graphics.TranslateTransform(1, 2);
            e.Graphics.FillPath(shade, path);
            e.Graphics.ResetTransform();
            using var baseBrush = new SolidBrush(Color.FromArgb((int)(100 + Math.Clamp(GlassBlend, 0, 1) * 100), 250, 252, 255));
            e.Graphics.FillPath(baseBrush, path);
            using var tint = new SolidBrush(Color.FromArgb((int)(Math.Clamp(Alpha, 0, 1) * 255), Tint));
            e.Graphics.FillPath(tint, path);
            using var border = new Pen(Color.FromArgb(160, 255, 255, 255), 1);
            e.Graphics.DrawPath(border, path);
            var dark = Tint.R * .2126 + Tint.G * .7152 + Tint.B * .0722 < 90;
            using var text = new SolidBrush(dark ? Color.White : Color.FromArgb(20, 28, 40));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            using var font = new Font("Segoe UI", (float)(CompactFontPixels > 0 ? CompactFontPixels : Math.Max(10, Math.Min(16, Height / 3f))));
            if (Glyph.Length > 0)
                DrawGlyphs(e.Graphics, dark ? Color.White : Color.FromArgb(20, 28, 40));
            else
                e.Graphics.DrawString(Text, font, text, box, format);
            if (TabItems.ValueKind == JsonValueKind.Array)
                DrawTabStrip(e.Graphics, dark ? Color.White : Color.FromArgb(20, 28, 40));
        }
        private void DrawTabStrip(Graphics g, Color foreground)
        {
            var items = TabItems.EnumerateArray().ToArray();
            if (items.Length == 0)
                return;
            RectangleF Box(JsonElement r) => new((float)(r.GetProperty("x").GetDouble() * SceneScaleX), (float)(r.GetProperty("y").GetDouble() * SceneScaleY), (float)(r.GetProperty("width").GetDouble() * SceneScaleX), (float)(r.GetProperty("height").GetDouble() * SceneScaleY));
            RectangleF Full(JsonElement item) => Box(item.TryGetProperty("visualFrame", out var r) ? r : item.GetProperty("frame"));
            using var trackPath = Rounded(new RectangleF((float)(16 * SceneScaleX), (float)(80 * SceneScaleY), (float)(Width - 32 * SceneScaleX), (float)(36 * SceneScaleY)), (float)(18 * SceneScaleY));
            using var trackBrush = new SolidBrush(Color.FromArgb(20, foreground));
            g.FillPath(trackBrush, trackPath);
            using var tabFont = new Font("Segoe UI", (float)(TabFontSize * SceneScaleY), GraphicsUnit.Pixel);
            using var iconFont = new Font("Segoe UI", (float)(12 * SceneScaleY), GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(foreground);
            using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                var active = item.GetProperty("active").GetBoolean();
                var full = Full(item);
                var exposed = full;
                if (index + 1 < items.Length)
                    exposed.Width = Math.Min(exposed.Width, Math.Max(0, Full(items[index + 1]).X - full.X - (float)(3 * SceneScaleX)));
                if (exposed.Width <= 0)
                    continue;
                if (!active && index + 1 < items.Length && !items[index + 1].GetProperty("active").GetBoolean())
                {
                    using var separator = new Pen(Color.FromArgb(50, foreground), (float)(.7 * SceneScaleX));
                    g.DrawLine(separator, exposed.Right + (float)(1.5 * SceneScaleX), (float)(87 * SceneScaleY), exposed.Right + (float)(1.5 * SceneScaleX), (float)(109 * SceneScaleY));
                }
                var saved = g.Save();
                g.SetClip(exposed, CombineMode.Intersect);
                if (active)
                {
                    using var path = Rounded(full, (float)(16 * SceneScaleY));
                    using var fill = new SolidBrush(Color.FromArgb(235, Color.White));
                    using var edge = new Pen(Color.FromArgb(46, foreground), (float)(.65 * SceneScaleX));
                    g.FillPath(fill, path);
                    g.DrawPath(edge, path);
                    var cx = full.X + (float)(16 * SceneScaleX);
                    var cy = full.Y + full.Height / 2;
                    g.FillEllipse(textBrush, cx - (float)(7 * SceneScaleX), cy - (float)(7 * SceneScaleY), (float)(14 * SceneScaleX), (float)(14 * SceneScaleY));
                    using var cross = new Pen(Color.White, (float)(1.4 * SceneScaleX));
                    g.DrawLine(cross, cx - (float)(2.5 * SceneScaleX), cy - (float)(2.5 * SceneScaleY), cx + (float)(2.5 * SceneScaleX), cy + (float)(2.5 * SceneScaleY));
                    g.DrawLine(cross, cx - (float)(2.5 * SceneScaleX), cy + (float)(2.5 * SceneScaleY), cx + (float)(2.5 * SceneScaleX), cy - (float)(2.5 * SceneScaleY));
                }
                var title = active ? Ui("Shell.CalibrationTab") : string.Format(AppLocalization.Current.Culture, Ui("Shell.PageTab"), index + 1);
                var contentBox = active ? full : exposed;
                var contentX = contentBox.X + (float)(((active ? 32 : 0) + 8) * SceneScaleX);
                var contentWidth = Math.Max(0, contentBox.Width - (float)(((active ? 32 : 0) + 16) * SceneScaleX));
                var titleWidth = Math.Min(Math.Max(0, contentWidth - (float)(24 * SceneScaleX)), g.MeasureString(title, tabFont).Width);
                var iconX = contentX + Math.Max(0, (contentWidth - ((float)(24 * SceneScaleX) + titleWidth)) / 2);
                if (contentWidth >= 24 * SceneScaleX)
                {
                    var icon = new RectangleF(iconX, full.Y + full.Height / 2 - (float)(8 * SceneScaleY), (float)(16 * SceneScaleX), (float)(16 * SceneScaleY));
                    using var iconPath = Rounded(icon, (float)(4 * SceneScaleY));
                    using var iconBrush = new SolidBrush(Color.FromArgb(36, foreground));
                    g.FillPath(iconBrush, iconPath);
                    using var numberBrush = new SolidBrush(Color.White);
                    using var numberFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("1", iconFont, numberBrush, icon, numberFormat);
                    g.DrawString(title, tabFont, textBrush, new RectangleF(iconX + (float)(24 * SceneScaleX), full.Y, Math.Max(0, contentX + contentWidth - iconX - (float)(24 * SceneScaleX)), full.Height), format);
                }
                g.Restore(saved);
            }
        }

        private void DrawGlyphs(Graphics graphics, Color color)
        {
            // Fixed vector slots avoid font-dependent Unicode substitutions and multiline overlap.
            var scale = VerticalIcons ? Width / 48f : Height / 48f;
            void Icon(string kind, float cx, float cy)
            {
                var saved = graphics.Save();
                graphics.TranslateTransform(cx - 12 * scale, cy - 12 * scale);
                graphics.ScaleTransform(scale, scale);
                using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                if (kind == "back")
                    graphics.DrawLines(pen, new[] { new PointF(15, 3), new PointF(6, 12), new PointF(15, 21) });
                else if (kind == "plus")
                {
                    graphics.DrawLine(pen, 12, 3, 12, 21);
                    graphics.DrawLine(pen, 3, 12, 21, 12);
                }
                else if (kind == "tabs")
                {
                    graphics.DrawRectangle(pen, 3, 3, 14, 16);
                    graphics.DrawRectangle(pen, 8, 8, 14, 16);
                }
                else
                {
                    graphics.DrawLines(pen, new[] { new PointF(12, 5), new PointF(6, 3), new PointF(1, 4), new PointF(1, 21), new PointF(6, 20), new PointF(12, 22), new PointF(18, 20), new PointF(23, 21), new PointF(23, 4), new PointF(18, 3), new PointF(12, 5), new PointF(12, 22) });
                }
                graphics.Restore(saved);
            }
            if (Glyph == "tabs")
            {
                Icon("plus", VerticalIcons ? Width / 2f : Width * .25f, VerticalIcons ? 24 * scale : Height / 2f);
                Icon("tabs", VerticalIcons ? Width / 2f : Width * .75f, VerticalIcons ? 78 * scale : Height / 2f);
            }
            else
                Icon(Glyph, Width / 2f, Height / 2f);
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius < .5)
            {
                p.AddRectangle(r);
                return p;
            }
            var d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
#endif
