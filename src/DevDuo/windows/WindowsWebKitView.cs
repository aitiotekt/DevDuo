#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using static DevDuo.WindowsWebKitApi;
using Size = DevDuo.WindowsWebKitApi.Size;

namespace DevDuo;

internal delegate bool PointerMessageHandler(ref System.Windows.Forms.Message message);

internal sealed partial class WindowsWebKitView : System.Windows.Forms.Panel
{
    // Numeric callback tokens are never dereferenced. Removing them makes late native
    // callbacks harmless without retaining a disposed view or unmanaged GCHandle.
    private static long nextToken;
    private static readonly Dictionary<nint, WindowsWebKitView> views = new();
    private static readonly Dictionary<nint, (WindowsWebKitView Owner, TaskCompletionSource<string?> Completion)> scripts = new();
    private static readonly Dictionary<nint, (WindowsWebKitView Owner, long Navigation, long Geometry, string? Document, TaskCompletionSource<ContentEdgeColor?> Completion, Rectangle Rect)> colors = new();
    private static readonly ContentColorCallback colorCallback = ColorFinished;
    private static readonly ScriptCallback scriptCallback = ScriptFinished;
    private static readonly MessageCallback messageCallback = MessageReceived;
    private static readonly MessageCallback remoteDebuggingCallback = RemoteDebuggingMessage;
    private static readonly NavigationCallback startCallback = Started, finishCallback = Finished, commitCallback = Committed;
    private static readonly FailureCallback failureCallback = Failed;
    private static readonly SameDocumentCallback sameDocumentCallback = SameDocument;
    private static readonly CrashCallback crashCallback = Crashed;
    private readonly nint token = (nint)Interlocked.Increment(ref nextToken);
    private readonly System.Windows.Forms.Timer metadataTimer = new() { Interval = 200 };
    private readonly System.Diagnostics.Stopwatch navigationClock = new();
    private readonly System.Diagnostics.Stopwatch requestClock = new();
    private double initializationMilliseconds;
    private double? commitMilliseconds, finishMilliseconds, firstObservationMilliseconds;
    private double? requestToStartMilliseconds;
    private double? requestCallMilliseconds;
    // Independent monotonic boundary for native first-pixel acceptance.
    internal long LastNavigationRequestTimestamp { get; private set; }
    private bool inspectorRequested;
    private nint remoteDebuggingScript;
    private string? installedRemoteDebuggingSource;
    private long remoteDebuggingRevision;
    private string? remoteInspectionAddress;
    internal event Action<string>? RemoteDebuggingMessageReceived;
    private Color rootColor = Color.White;
    private nint configuration, store, controller, view, page, browserWindow, activeNavigation;
    private HostKeyboardWindow? keyboardWindow;
    private WindowsComposition? composition;
    private (nint Target, int Degrees, int X, int Y)? presentation;
    internal Func<Rectangle, Rectangle>? PresentationBounds;
    internal Func<Point, Point>? ScenePointFromScreen;
    internal PointerMessageHandler? PointerMessage;
    internal nint BrowserWindow => browserWindow;
    internal WindowsComposition? Composition => composition;
    internal WindowsComposition.Chrome[] Chrome = [];
    internal string[] ChromeNames = [];
    internal WindowsComposition.Material Material;
    internal event Action<WindowsComposition.Action>? ChromeInvoked;
    internal event Action<string>? CompositionFailed;
    protected override System.Windows.Forms.AccessibleObject CreateAccessibilityInstance() => new ChromeAccessibility(this);
    private bool disposed, loading, presentationFailed;
    private bool committed, verifyingObservation, observationVerificationDirty;
    private bool? darkAppearance;
    private long navigationGeneration;
    private long geometryGeneration;
    private long presentationRepaintGeneration;
    private long observationCount, presentCount;
    private ulong? reportedRenderingUpdate;
    internal event Action? ContentRenderingUpdated;
    private string? acceptedDocumentId;
    private NavigationError? error;
    private (int Width, int Height, double Scale, int Top, int Right, int Bottom, int Left, int Minimum, int Maximum)? geometry;
    private JsonElement? observation;
    private Rectangle validFlow;
    private string interactionMode = "touch";
    private nint inputUserScript;
    private string? url;
    public NavigationState Navigation { get; private set; } = NavigationState.Empty;
    public event Action<NavigationState>? NavigationChanged;
    internal event Action? DocumentNavigationStarted;
    public event Action<string>? Observation;
    internal event Action<int, string>? NativeScrollInput;
    public event Action<HostCommand>? CommandRequested;
    internal double BackingScale => geometry?.Scale ?? 0;
    internal Color UnderPageColor => rootColor;
    private bool sourceDestroyed;
    internal bool IsReady => page != 0 && !disposed && !sourceDestroyed;
    internal sealed record ContentEdgeColor(uint Status, uint Rgb, Rectangle SampleRect, long NavigationGeneration, long GeometryGeneration);

    internal void Initialize(string observer, bool safariScrollIndicator = true)
    {
        var initialization = System.Diagnostics.Stopwatch.StartNew();
        Load();
        views.Add(token, this);
        configuration = WKPageConfigurationCreate();
        store = WindowsBrowserProfile.Acquire();
        WKPageConfigurationSetWebsiteDataStore(configuration, store);
        WKPreferencesSetDeveloperExtrasEnabled(WKPageConfigurationGetPreferences(configuration), true);
        controller = WKUserContentControllerCreate();
        var name = WKStringCreateWithUTF8CString("devduoShellJSON");
        try { WKUserContentControllerAddScriptMessageHandler(controller, name, messageCallback, token); }
        finally { WKRelease(name); }
        var source = WKStringCreateWithUTF8CString(observer);
        var script = WKUserScriptCreateWithSource(source, 0, true);
        try
        {
            WKUserContentControllerAddUserScript(controller, script);
            WKPageConfigurationSetUserContentController(configuration, controller);
        }
        finally { WKRelease(script); WKRelease(source); }
        InstallInputScript();
        // The default configuration disables test-only IPC. No testing entry is bound here.
        view = WKViewCreate(new Rect { Right = Math.Max(1, Width), Bottom = Math.Max(1, Height) }, configuration, Handle);
        if (view == 0) throw new InvalidOperationException("WKViewCreate failed.");
        // Keep accelerated WebKit frames in its native retained painter. Direct
        // OpenGL swaps bypass the layered HWND surface consumed by DirectComposition.
        WKViewSetUsesOffscreenRendering(view, true);
        page = WKViewGetPage(view);
        ConfigureScrollIndicator(safariScrollIndicator);
        var userAgent = WKStringCreateWithUTF8CString(BrowserIdentity.MobileUserAgent);
        try { WKPageSetCustomUserAgent(page, userAgent); }
        finally { WKRelease(userAgent); }
        WKPageSetAllowsRemoteInspection(page, false);
        var developerChannel = WKStringCreateWithUTF8CString("devduoRemoteDebugging");
        try { WKUserContentControllerAddScriptMessageHandler(controller, developerChannel, remoteDebuggingCallback, token); }
        finally { WKRelease(developerChannel); }
        browserWindow = WKViewGetWindow(view);
        keyboardWindow = new HostKeyboardWindow(browserWindow, command => CommandRequested?.Invoke(command));
        keyboardWindow.SourceDestroyed = () => sourceDestroyed = true;
        keyboardWindow.AccessibleObjectRequested = GetWebAccessibility;
        keyboardWindow.UiaProviderRequested = GetWebUiaProvider;
        keyboardWindow.AccessibilityChanged = AccessibilityChanged;
        keyboardWindow.PointerMessage = FilterBrowserPointer;
        keyboardWindow.ScrollInput = (direction, source) => { RecordScrollIndicatorInput(); NativeScrollInput?.Invoke(direction, source); };
        composition = new WindowsComposition(Handle, browserWindow);
        touchPointerWindow = new TouchPointerWindow(GetAncestor(Handle, 2), HideTouchPointer);
        if (presentation is { } output)
        {
            composition.SetPresentation(output.Target, output.Degrees, output.X, output.Y);
            SetNativeDesktopGeometry(output.Target, output.Degrees, output.X, output.Y);
        }
        WKViewWindowAncestryDidChange(view);
        keyboardWindow.ChromeHit = (x, y) => composition.Hit(x, y);
        keyboardWindow.ChromeClick = role => ChromeInvoked?.Invoke(role);
        WKViewSetIsInWindow(view, true);
        var client = new NavigationClient
        {
            Info = token,
            Start = Marshal.GetFunctionPointerForDelegate(startCallback),
            Commit = Marshal.GetFunctionPointerForDelegate(commitCallback),
            Finish = Marshal.GetFunctionPointerForDelegate(finishCallback),
            Fail = Marshal.GetFunctionPointerForDelegate(failureCallback),
            FailProvisional = Marshal.GetFunctionPointerForDelegate(failureCallback),
            SameDocument = Marshal.GetFunctionPointerForDelegate(sameDocumentCallback),
            Crash = Marshal.GetFunctionPointerForDelegate(crashCallback)
        };
        WKPageSetPageNavigationClient(page, ref client);
        ConfigurePageDialogs();
        metadataTimer.Tick += (_, _) =>
        {
            // Connect without showing, then detach before making the frontend
            // visible. Restored docking preferences must not resize the page.
            var inspector = WKPageGetInspector(page);
            if (inspector != 0 && WKInspectorIsConnected(inspector))
            {
                if (WKInspectorIsAttached(inspector)) WKInspectorDetach(inspector);
                if (inspectorRequested) WKInspectorShow(inspector);
            }
            if (InspectorVisible) inspectorRequested = false;
            try { if (composition.Refresh()) Present(); }
            catch (InvalidOperationException failure)
            {
                presentationFailed = true;
                metadataTimer.Stop();
                CompositionFailed?.Invoke(failure.Message);
                return;
            }
            ReportNavigation();
            if (WKViewGetHostRenderingStatistics(view, out var updates, out _, out _) && reportedRenderingUpdate != updates)
            {
                reportedRenderingUpdate = updates;
                ContentRenderingUpdated?.Invoke();
            }
            _ = RefreshFixedContainerColorsAsync();
            _ = RefreshScrollIndicatorAsync();
        };
        metadataTimer.Start();
        initializationMilliseconds = initialization.Elapsed.TotalMilliseconds;
    }

    internal bool InspectorVisible => IsReady && WKPageGetInspector(page) is var inspector && inspector != 0 && WKInspectorIsVisible(inspector);
    internal bool InspectorAttached => IsReady && WKPageGetInspector(page) is var inspector && inspector != 0 && WKInspectorIsAttached(inspector);
    internal void ToggleInspector()
    {
        SetInspectorVisible(!(inspectorRequested || InspectorVisible));
    }

    internal void SetInspectorVisible(bool visible)
    {
        if (!IsReady) return;
        if (visible && RemoteInspection.Enabled)
            throw new InvalidOperationException("Close native remote inspection before opening the local Web Inspector.");
        var inspector = WKPageGetInspector(page);
        if (inspector == 0) throw new InvalidOperationException("Native Web Inspector is unavailable.");
        if (!visible)
        {
            inspectorRequested = false;
            WKInspectorClose(inspector);
        }
        else
        {
            inspectorRequested = true;
            WKInspectorConnect(inspector);
        }
    }

    internal RemoteInspectionState RemoteInspection => IsReady && WKInspectorGetHostRemoteServerPort() is var port && port != 0
        ? new(true, "webkit-inspector-socket", remoteInspectionAddress, port)
        : RemoteInspectionState.Disabled;

    internal void StartRemoteInspection(string bindAddress, int port)
    {
        if (!IsReady) throw new InvalidOperationException("Windows WebKit is not initialized.");
        if (!System.Net.IPAddress.TryParse(bindAddress, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Native remote inspection requires an IPv4 bind address.", nameof(bindAddress));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var inspector = WKPageGetInspector(page);
        if (inspectorRequested || InspectorVisible || inspector != 0 && WKInspectorIsConnected(inspector))
            throw new InvalidOperationException("Close the local Web Inspector before starting native remote inspection.");
        if (RemoteInspection.Enabled) throw new InvalidOperationException("Native remote inspection is already running.");
        var value = WKStringCreateWithUTF8CString(address.ToString());
        try
        {
            if (!WKInspectorStartHostRemoteServer(value, (ushort)port))
                throw new InvalidOperationException("Cannot bind the native WebKit inspector server. Check the address and port.");
            remoteInspectionAddress = address.ToString();
            WKPageSetAllowsRemoteInspection(page, true);
        }
        finally { WKRelease(value); }
    }

    internal void StopRemoteInspection()
    {
        if (!IsReady) return;
        WKPageSetAllowsRemoteInspection(page, false);
        WKInspectorStopHostRemoteServer();
        remoteInspectionAddress = null;
    }

    internal async Task SetRemoteDebuggingScriptAsync(string? source)
    {
        if (!IsReady) throw new InvalidOperationException("Windows WebKit is not initialized.");
        var revision = ++remoteDebuggingRevision;
        var hadScript = remoteDebuggingScript != 0;
        installedRemoteDebuggingSource = null;
        if (remoteDebuggingScript != 0)
        {
            WKUserContentControllerRemoveHostUserScript(controller, remoteDebuggingScript);
            WKRelease(remoteDebuggingScript);
            remoteDebuggingScript = 0;
        }
        if (source is null)
        {
            // Submit disconnect only after disabling messages/removing the owned script.
            // No state is mutated after await: an old stop cannot remove a new script.
            if (hadScript) await EvaluateRemoteDebuggingScriptAsync("window.__devduoRemoteDebugging?.disconnect(); delete window.__devduoPostDeveloperMessage;");
            return;
        }
        var scriptSource = "window.__devduoPostDeveloperMessage = value => window.webkit.messageHandlers.devduoRemoteDebugging.postMessage(JSON.stringify({generation:"
            + revision + ",message:String(value)}));\n" + source;
        installedRemoteDebuggingSource = scriptSource;
        var value = WKStringCreateWithUTF8CString(scriptSource);
        try
        {
            remoteDebuggingScript = WKUserScriptCreateWithSource(value, 0, true);
            WKUserContentControllerAddUserScript(controller, remoteDebuggingScript);
        }
        finally { WKRelease(value); }
        var document = navigationGeneration;
        try { await EvaluateRemoteDebuggingScriptAsync(scriptSource); }
        catch (InvalidOperationException) when (!disposed && document != navigationGeneration && revision == remoteDebuggingRevision)
        {
            // The next document already owns the document-start script. Its ready
            // message completes setup without restarting the native view.
        }
    }

    internal async Task EvaluateRemoteDebuggingScriptAsync(string source)
    {
        if (!IsReady) throw new InvalidOperationException("Windows WebKit is not initialized.");
        var navigation = navigationGeneration;
        var result = await EvaluateAsync("(function(){\n" + source + "\n;return 'devduo-remote-ok';})()");
        if (disposed || navigation != navigationGeneration)
            throw new InvalidOperationException("The remote debugging evaluation belongs to a stale document.");
        if (result != "devduo-remote-ok")
            throw new InvalidOperationException("The remote debugging script could not be evaluated in the current document.");
    }

    internal Task RefreshRemoteDebuggingScriptAsync() => installedRemoteDebuggingSource is { } source && IsReady
        ? EvaluateRemoteDebuggingScriptAsync(source)
        : Task.CompletedTask;

    private static void RemoteDebuggingMessage(nint message, nint reply, nint context)
    {
        try
        {
            if (!views.TryGetValue(context, out var owner) || !owner.IsReady || owner.remoteDebuggingScript == 0) return;
            var frame = WKScriptMessageGetFrameInfo(message);
            if (frame == 0 || !WKFrameInfoGetIsMainFrame(frame) || WKFrameInfoGetPage(frame) != owner.page) return;
            var body = WKScriptMessageGetBody(message);
            if (body == 0 || WKGetTypeID(body) != WKStringGetTypeID()) return;
            using var payload = JsonDocument.Parse(String(body));
            if (payload.RootElement.GetProperty("generation").GetInt64() == owner.remoteDebuggingRevision &&
                payload.RootElement.GetProperty("message").GetString() is { } json)
                owner.RemoteDebuggingMessageReceived?.Invoke(json);
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or OverflowException)
        {
            // A developer bridge consumer must not unwind through WebKit's C callback.
        }
        finally { WKCompletionListenerComplete(reply, 0); }
    }

    internal void CloseInspector()
    {
        inspectorRequested = false;
        if (IsReady && WKPageGetInspector(page) is var inspector && inspector != 0) WKInspectorClose(inspector);
    }

    internal void SetGeometry(int width, int height, double scale, int top, int right, int bottom, int left, int minimumHeight, int maximumHeight)
    {
        if (!IsReady || width <= 0 || height <= 0) return;
        // Physical HWND bounds are independent of the logical viewport and its
        // backing scale. Rounded/temporarily clipped host sizes can change while
        // the logical geometry key stays identical.
        var resized = SynchronizeNativeBounds();
        var next = (width, height, scale, top, right, bottom, left, minimumHeight, maximumHeight);
        if (geometry == next)
        {
            if (resized) InvalidatePresentation();
            return;
        }
        var indicatorSameScene = geometry is { } oldGeometry && oldGeometry.Width == width && oldGeometry.Height == height &&
            oldGeometry.Scale == scale && oldGeometry.Left == left && oldGeometry.Right == right;
        geometry = next;
        geometryGeneration++;
        var clearedFixedColors = InvalidateFixedContainerColors();
        ResetScrollIndicator(false, indicatorSameScene);
        if (observation is null) ShowUnobscuredContent();
        if (!WKViewSetHostPresentationGeometry(view, new Size(width, height), (float)scale) ||
            !WKViewSetObscuredContentInsets(view, top, right, bottom, left) ||
            !WKViewSetUnobscuredSizeRange(view, new Size(width - left - right, minimumHeight), new Size(width - left - right, maximumHeight)))
            throw new InvalidOperationException("Windows WebKit rejected the logical scene/insets.");
        InvalidatePresentation();
        if (clearedFixedColors) Present();
        _ = EvaluateAsync("window.__devduoReportContainerObservation?.()");
    }

    private bool SynchronizeNativeBounds()
    {
        if (!GetClientRect(browserWindow, out var bounds))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (bounds.Right == ClientSize.Width && bounds.Bottom == ClientSize.Height) return false;
        // Fit changes the raster contents throughout the source. Win32's default
        // resize copy-bits optimization cannot preserve any of those old pixels.
        if (!SetWindowPos(browserWindow, 0, 0, 0, ClientSize.Width, ClientSize.Height, 0x114))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return true;
    }

    internal void InvalidatePresentation()
    {
        if (!IsReady) return;
        // Repaint the existing live backing store after a size, scale or ancestor
        // clip change. This is event-driven damage, never a capture/repaint loop.
        if (!InvalidateRect(browserWindow, 0, false))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        presentationRepaintGeneration++;
    }
    internal void Navigate(string target)
    {
        if (!IsReady) return;
        ClosePageDialogs();
        url = target;
        var callStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        LastNavigationRequestTimestamp = callStarted;
        requestCallMilliseconds = null;
        requestClock.Restart();
        var value = WKURLCreateWithUTF8CString(target);
        try { WKPageLoadURL(page, value); }
        finally { WKRelease(value); requestCallMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(callStarted).TotalMilliseconds; }
    }
    internal void SetDarkAppearance(bool dark)
    {
        if (!IsReady || darkAppearance == dark) return;
        if (!WKViewSetHostDarkAppearance(view, dark)) throw new InvalidOperationException("WebKit rejected host appearance.");
        darkAppearance = dark;
        _ = EvaluateAsync("window.__devduoReportContainerObservation?.()");
    }
    internal void ShowHtml(string html, string? baseUrl = null)
    {
        if (!IsReady) return;
        ClosePageDialogs();
        var callStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        LastNavigationRequestTimestamp = callStarted;
        requestCallMilliseconds = null;
        requestClock.Restart();
        var text = WKStringCreateWithUTF8CString(html);
        var origin = WKURLCreateWithUTF8CString(baseUrl ?? BuiltinPages.ColorAddress);
        try { WKPageLoadHTMLString(page, text, origin); }
        finally { WKRelease(text); WKRelease(origin); requestCallMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(callStarted).TotalMilliseconds; }
    }
    internal void GoBack() { if (IsReady && WKPageCanGoBack(page)) { ClosePageDialogs(); WKPageGoBack(page); } }
    internal void GoForward() { if (IsReady && WKPageCanGoForward(page)) { ClosePageDialogs(); WKPageGoForward(page); } }
    internal void Reload() { if (IsReady) { ClosePageDialogs(); WKPageReload(page); } }
    internal void Stop() { if (IsReady) { ClosePageDialogs(); WKPageStopLoading(page); loading = false; ReportNavigation(); } }
    internal float MeasureText(string text, float pixels) => composition?.MeasureText(text, pixels) ?? 0;
    internal WindowsComposition.Action HitChrome(int x, int y) => composition?.Hit(x, y) ?? WindowsComposition.Action.None;
    internal void InvokeChrome(WindowsComposition.Action action) => ChromeInvoked?.Invoke(action);
    internal Task<ContentEdgeColor?> RequestContentEdgeColorAsync(bool bottom)
    {
        if (!IsReady || !committed || acceptedDocumentId is null || geometry is not { } g)
            return Task.FromResult<ContentEdgeColor?>(null);
        var height = Math.Min(18, g.Height - g.Top - g.Bottom);
        var rect = new Rectangle(g.Left, bottom ? g.Height - g.Bottom - height : g.Top, g.Width - g.Left - g.Right, height);
        var id = (nint)Interlocked.Increment(ref nextToken);
        var completion = new TaskCompletionSource<ContentEdgeColor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        colors.Add(id, (this, navigationGeneration, geometryGeneration, acceptedDocumentId, completion, rect));
        WKViewRequestContentEdgeColor(view, rect.X, rect.Y, rect.Width, rect.Height, (ulong)id, id, colorCallback);
        return completion.Task;
    }
    private static void ColorFinished(ulong request, uint status, uint rgb, nint context)
    {
        if (!colors.Remove(context, out var pending)) return;
        var owner = pending.Owner;
        var current = request == (ulong)context && !owner.disposed && owner.navigationGeneration == pending.Navigation &&
            owner.geometryGeneration == pending.Geometry && owner.acceptedDocumentId == pending.Document;
        pending.Completion.TrySetResult(current ? new(status, rgb, pending.Rect, pending.Navigation, pending.Geometry) : null);
    }
    internal Task<string?> EvaluateAsync(string source)
    {
        if (!IsReady) return Task.FromResult<string?>(null);
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = (nint)Interlocked.Increment(ref nextToken);
        scripts.Add(id, (this, completion));
        var text = WKStringCreateWithUTF8CString(source);
        try { WKPageEvaluateJavaScriptInMainFrame(page, text, id, scriptCallback); }
        finally { WKRelease(text); }
        return completion.Task;
    }
    private static void ScriptFinished(nint result, nint failure, nint context)
    {
        if (!scripts.Remove(context, out var pending)) return;
        if (pending.Owner.disposed || failure != 0) { pending.Completion.TrySetResult(null); return; }
        pending.Completion.TrySetResult(result != 0 && WKGetTypeID(result) == WKStringGetTypeID() ? String(result) : null);
    }
    private static void MessageReceived(nint message, nint reply, nint context)
    {
        try
        {
            if (!views.TryGetValue(context, out var owner) || owner.disposed) return;
            var frame = WKScriptMessageGetFrameInfo(message);
            if (frame == 0 || !WKFrameInfoGetIsMainFrame(frame) || WKFrameInfoGetPage(frame) != owner.page || !owner.committed) return;
            var body = WKScriptMessageGetBody(message);
            if (body == 0 || WKGetTypeID(body) != WKStringGetTypeID()) return;
            var json = String(body);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.GetProperty("type").GetString() != "devduo-shell-observation") return;
            var id = document.RootElement.GetProperty("documentId").GetString();
            if (id is null) return;
            if (!owner.verifyingObservation && id == owner.acceptedDocumentId) owner.Observe(json, document.RootElement);
            else
            {
                owner.observationVerificationDirty = true;
                if (!owner.verifyingObservation) _ = owner.VerifyObservation(owner.navigationGeneration);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or IndexOutOfRangeException or FormatException)
        {
            // Arbitrary pages cannot let malformed observation data escape a native callback.
        }
        finally { WKCompletionListenerComplete(reply, 0); }
    }
    private void Observe(string json, JsonElement root)
    {
        firstObservationMilliseconds ??= navigationClock.Elapsed.TotalMilliseconds;
        ObserveScrollIndicatorMotion(root);
        if (observation is null) fixedColorSchedule.Wake();
        observation = root.Clone();
        observationCount++;
        // The panel's observation handler lays out and presents the scene. Give
        // it current continuation bounds so a scroll produces one final scene.
        var handler = Observation;
        UpdateContinuation(handler is null);
        handler?.Invoke(json);
    }
    private async Task VerifyObservation(long generation)
    {
        verifyingObservation = true;
        try
        {
            // Queued messages can belong to a previous document. Read the current
            // renderer snapshot rather than letting an old message choose its ID.
            // Messages arriving during this read mark another read as necessary.
            while (!disposed && generation == navigationGeneration && committed && observationVerificationDirty)
            {
                observationVerificationDirty = false;
                var json = await EvaluateAsync("JSON.stringify(window.devduoShellObservation)");
                if (disposed || generation != navigationGeneration || !committed) return;
                if (json is null) continue;
                try
                {
                    using var document = JsonDocument.Parse(json);
                    var root = document.RootElement;
                    if (root.GetProperty("type").GetString() != "devduo-shell-observation" ||
                        root.GetProperty("documentId").GetString() is not { Length: > 0 } id) continue;
                    acceptedDocumentId = id;
                    Observe(json, root);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or IndexOutOfRangeException or FormatException) { }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException or IndexOutOfRangeException or FormatException) { }
        finally { if (generation == navigationGeneration) verifyingObservation = false; }
    }
    private void UpdateContinuation(bool present = true)
    {
        if (geometry is not { } g || observation is not { } r || disposed) return;
        double Number(string name) => r.GetProperty(name).GetDouble();
        var width = Number("viewportWidth"); var height = Number("viewportHeight");
        var scale = Number("devicePixelRatio") * Number("visualScale");
        var x = Number("scrollX"); var y = Number("scrollY");
        var dw = Number("documentWidth"); var dh = Number("documentHeight");
        if (new[] { width, height, scale, x, y, dw, dh }.Any(v => !double.IsFinite(v)) ||
            width != g.Width - g.Left - g.Right || height != g.Height - g.Top - g.Bottom || Math.Abs(scale - g.Scale) > .001) return;
        var x0 = g.Left - Math.Clamp(x, 0, g.Left);
        var y0 = g.Top - Math.Clamp(y, 0, g.Top);
        var x1 = g.Left + width + Math.Clamp(dw - x - width, 0, g.Right);
        var y1 = g.Top + height + Math.Clamp(dh - y - height, 0, g.Bottom);
        // This is a paint clip, not a damage region. Skia's non-antialiased
        // rectangle clips round edges to the nearest device pixel (half up).
        // Expanding with floor/ceil leaves an unpainted black row/column between
        // the live page and the host's under-page fill at fractional Fit/DPI.
        validFlow = Rectangle.FromLTRB(RasterClipEdge(x0 * scale), RasterClipEdge(y0 * scale), RasterClipEdge(x1 * scale), RasterClipEdge(y1 * scale));
        static Color ColorOf(JsonElement rgb) => Color.FromArgb(Math.Clamp(rgb[0].GetInt32(), 0, 255), Math.Clamp(rgb[1].GetInt32(), 0, 255), Math.Clamp(rgb[2].GetInt32(), 0, 255));
        rootColor = ColorOf(r.GetProperty("underPageBackgroundFallback"));
        WKViewSetHostUnderPageBackgroundColor(view, (uint)rootColor.ToArgb() & 0xffffff);
        // Native fixed-container colors own all fixed edges, including opaque
        // footers. The DOM footer observation remains diagnostic only.
        if (present) Present();
    }
    internal static int RasterClipEdge(double pixelEdge) => checked((int)Math.Floor(pixelEdge + .5));
    private void ShowUnobscuredContent()
    {
        // Before DOMContentLoaded there may be no document-extent observation.
        // Cover only unverified continuation, never the real layout viewport:
        // hiding that viewport would mask WebKit's first paints until load/DOM readiness.
        validFlow = geometry is { } g ? Rectangle.FromLTRB(RasterClipEdge(g.Left * g.Scale), RasterClipEdge(g.Top * g.Scale),
            RasterClipEdge((g.Width - g.Right) * g.Scale), RasterClipEdge((g.Height - g.Bottom) * g.Scale)) : Rectangle.Empty;
    }
    internal void Present()
    {
        if (composition is null || disposed || presentationFailed) return;
        presentCount++;
        var rectangles = new List<WindowsComposition.Fill>();
        void Add(Rectangle r, Color color)
        {
            r.Intersect(ClientRectangle);
            if (r.Width > 0 && r.Height > 0) rectangles.Add(new() { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Color = (uint)color.ToArgb() & 0xffffff });
        }
        var root = rootColor;
        Add(Rectangle.FromLTRB(0, 0, Width, validFlow.Top), root);
        Add(Rectangle.FromLTRB(0, validFlow.Bottom, Width, Height), root);
        Add(Rectangle.FromLTRB(0, validFlow.Top, validFlow.Left, validFlow.Bottom), root);
        Add(Rectangle.FromLTRB(validFlow.Right, validFlow.Top, Width, validFlow.Bottom), root);
        if (geometry is { } g)
        {
            // Cover actual obscured insets even where valid document flow exists.
            // The real layout viewport and the page's fixed rectangles stay intact.
            var horizontalLeft = RasterClipEdge(g.Left * g.Scale);
            var horizontalRight = RasterClipEdge((g.Width - g.Right) * g.Scale);
            // Side continuation owns the intersections, including the ordinary
            // under-page background when that side has no accepted fixed color.
            if (FixedColor(0) is { } top) Add(Rectangle.FromLTRB(horizontalLeft, 0, horizontalRight, RasterClipEdge(g.Top * g.Scale)), top);
            if (FixedColor(2) is { } bottom) Add(Rectangle.FromLTRB(horizontalLeft, RasterClipEdge((g.Height - g.Bottom) * g.Scale), horizontalRight, Height), bottom);
            if (FixedColor(1) is { } right) Add(Rectangle.FromLTRB(RasterClipEdge((g.Width - g.Right) * g.Scale), 0, Width, Height), right);
            if (FixedColor(3) is { } left) Add(Rectangle.FromLTRB(0, 0, RasterClipEdge(g.Left * g.Scale), Height), left);
        }
        try { composition.Update(Width, Height, Chrome, rectangles.ToArray(), Material); PaintScrollIndicator(); touchPointerFrame = null; UpdateTouchPointer(); }
        catch (InvalidOperationException failure)
        {
            presentationFailed = true;
            metadataTimer.Stop();
            CompositionFailed?.Invoke(failure.Message);
        }
    }
    internal void SetPresentation(nint target, int degrees, int offsetX, int offsetY)
    {
        var next = (target, degrees, offsetX, offsetY);
        if (presentation == next) return;
        presentation = next;
        geometryGeneration++;
        var clearedFixedColors = InvalidateFixedContainerColors();
        ResetScrollIndicator(false);
        composition?.SetPresentation(target, degrees, offsetX, offsetY);
        if (clearedFixedColors) Present();
        if (view != 0)
        {
            SetNativeDesktopGeometry(target, degrees, offsetX, offsetY);
            WKViewWindowAncestryDidChange(view);
        }
    }
    private void SetNativeDesktopGeometry(nint target, int degrees, int x, int y)
    {
        if (!WKViewSetHostDesktopGeometry(view, target, degrees, x, y))
            throw new InvalidOperationException("Windows WebKit rejected the native desktop coordinate mapping.");
    }
    internal void SetInteractionMode(string mode)
    {
        if (mode is not ("touch" or "mouse")) throw new ArgumentException("Unknown interaction mode.", nameof(mode));
        if (interactionMode == mode) return;
        interactionMode = mode;
        UpdateTouchPointer();
        if (controller == 0 || disposed) return;
        InstallInputScript();
        if (IsReady) _ = EvaluateAsync(PageInteraction.Update(mode));
    }
    private void InstallInputScript()
    {
        if (inputUserScript != 0)
        {
            WKUserContentControllerRemoveHostUserScript(controller, inputUserScript);
            WKRelease(inputUserScript);
        }
        var source = WKStringCreateWithUTF8CString(PageInteraction.Bootstrap(PageInteraction.Source, interactionMode));
        try
        {
            inputUserScript = WKUserScriptCreateWithSource(source, 0, false);
            WKUserContentControllerAddUserScript(controller, inputUserScript);
        }
        finally { WKRelease(source); }
    }
    private bool FilterBrowserPointer(ref System.Windows.Forms.Message message)
    {
        ObserveTouchPointer(ref message);
        // Block at the HWND, before WebKit's async scrolling or DOM wheel listeners.
        if (interactionMode == "touch" && message.Msg is 0x20A or 0x20E or 0x114 or 0x115)
        {
            message.Result = 0;
            return true;
        }
        return ForwardPointer(ref message);
    }
    private bool ForwardPointer(ref System.Windows.Forms.Message message) => PointerMessage?.Invoke(ref message) == true;
    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == 0x3D && unchecked((int)message.LParam) == -25)
        {
            message.Result = GetPresentedUiaProvider(Handle, message.WParam, message.LParam);
            if (message.Result != 0) return;
        }
        ObserveTouchPointer(ref message);
        if (!ForwardPointer(ref message)) base.WndProc(ref message);
    }
    internal object Diagnostics => new
    {
        hostDesktopApiVersion = 1, hostViewportApiVersion = 2, contentPaintingVersion = 2, contentColorApiVersion = 1, hostAppearanceApiVersion = 1, hostFixedContainerColorApiVersion = 1, hostScrollIndicatorApiVersion = 1,
        accessibility = webAccessibility?.Diagnostics,
        textInput = TextInputDiagnostics(),
        touchPointer = new { visible = touchPointerVisible, systemCursorHidden = touchPointerVisible, diameterDip = 20, error = touchPointerError, source = "host composition; excluded from WebKit snapshots" },
        interactionMode, nativeWheelBlocked = interactionMode == "touch",
        pageInput = observation is { } inputObservation && inputObservation.TryGetProperty("pageInput", out var inputState) ? inputState : (JsonElement?)null,
        darkAppearance, testOnlyIPCEnabled = false, nonPersistentStore = false,
        websiteStore = "persistent DevDuo user profile",
        inspector = new { visible = InspectorVisible, attached = InspectorAttached },
        timings = new { runtimeVerificationMilliseconds = VerificationMilliseconds, initializationMilliseconds,
            requestToStartMilliseconds, requestCallMilliseconds, navigationCommitMilliseconds = commitMilliseconds, navigationFinishMilliseconds = finishMilliseconds, firstObservationMilliseconds,
            navigationElapsedMilliseconds = finishMilliseconds ?? (navigationClock.IsRunning ? navigationClock.Elapsed.TotalMilliseconds : (double?)null) },
        backingScaleFactor = BackingScale, pageZoomFactor = 1,
        renderingPipeline = RenderingPipelineDiagnostics(),
        logicalGeometry = geometry is { } g ? new { width = g.Width, height = g.Height, top = g.Top, right = g.Right, bottom = g.Bottom, left = g.Left, minimumHeight = g.Minimum, maximumHeight = g.Maximum } : null,
        composition = new { apiVersion = 4, error = composition?.Error, resources = composition?.Diagnostics, sourceWindow = SourceWindowDiagnostics(), strategy = "live DWM WebKit surface; DirectComposition blur/color matrix; layered Direct2D chrome", screenshotsUsedForRendering = false },
        continuation = new { cornerPriority = "side-over-horizontal", validFlowRectanglePx = new[] { validFlow.Left, validFlow.Top, validFlow.Right, validFlow.Bottom }, visible = validFlow != ClientRectangle, fixedFooterColorApproximation = false, fixedContainerColorExtension = FixedContainerColorDiagnostics(), backgroundSource = "read-only opaque html/body CSS fallback; otherwise white" }
    };
    private object TextInputDiagnostics()
    {
        uint error = 0;
        ulong editorId = 0;
        var flags = view != 0 && !disposed ? WKViewGetHostTextInputState(view, out error, out editorId) : 0;
        var diagnostics = view != 0 && !disposed ? WKViewCopyHostTextInputDiagnostics(view) : 0;
        var trace = diagnostics != 0 ? String(diagnostics) : null;
        if (diagnostics != 0) WKRelease(diagnostics);
        return new { flags, error, editorId, trace, editable = (flags & 16) != 0, tsfActive = (flags & 1) != 0, composing = (flags & 2) != 0 };
    }
    private object RenderingPipelineDiagnostics()
    {
        ulong updates = 0, acceleratedTransitions = 0, staleUpdates = 0;
        var available = view != 0 && !disposed && WKViewGetHostRenderingStatistics(view, out updates, out acceleratedTransitions, out staleUpdates);
        return new { transport = "native WC offscreen framebuffer -> retained GDI painter -> live DWM surface",
            available, updates, acceleratedTransitions, staleUpdates, observationCount, presentCount,
            nativeWindow = keyboardWindow?.Diagnostics };
    }
    private object SourceWindowDiagnostics()
    {
        var parent = browserWindow == 0 ? 0 : GetParent(browserWindow);
        GetClientRect(parent, out var bounds);
        GetClientRect(browserWindow, out var client);
        return new { detached = parent != 0 && parent != Handle,
            visibleStyle = browserWindow != 0 && (GetWindowLongPtr(browserWindow, -16).ToInt64() & 0x10000000) != 0,
            keyboardFocus = GetFocus() == browserWindow ? "WebKit source" : GetFocus() == presentation?.Target ? "physical presenter" : "other host control",
            parentHasRedirectionBitmap = parent != 0 && (GetWindowLongPtr(parent, -20).ToInt64() & 0x00200000) == 0,
            parentClientWidth = bounds.Right, parentClientHeight = bounds.Bottom,
            clientWidth = client.Right, clientHeight = client.Bottom, presentationRepaintGeneration };
    }

    private static WindowsWebKitView? Owner(nint page, nint context) => views.TryGetValue(context, out var owner) && owner.IsReady && owner.page == page ? owner : null;
    private static void Started(nint page, nint navigation, nint data, nint context)
    {
        if (Owner(page, context) is not { } owner) return;
        owner.ClosePageDialogs();
        WKViewResetHostTextInput(owner.view);
        owner.navigationGeneration++;
        owner.reportedRenderingUpdate = null;
        owner.InvalidateFixedContainerColors();
        owner.ResetScrollIndicator(true);
        if (!owner.requestClock.IsRunning) owner.requestCallMilliseconds = null;
        owner.requestToStartMilliseconds = owner.requestClock.IsRunning ? owner.requestClock.Elapsed.TotalMilliseconds : null;
        owner.requestClock.Reset();
        owner.navigationClock.Restart();
        owner.commitMilliseconds = owner.finishMilliseconds = owner.firstObservationMilliseconds = null;
        owner.acceptedDocumentId = null;
        owner.verifyingObservation = owner.observationVerificationDirty = owner.committed = false;
        owner.observation = null;
        owner.activeNavigation = navigation; owner.loading = true; owner.error = null;
        owner.DocumentNavigationStarted?.Invoke();
        // Unverified inset pixels use a canvas fallback. The layout viewport
        // remains live while WebKit loads/parses the new document.
        owner.rootColor = Color.White;
        WKViewSetHostUnderPageBackgroundColor(owner.view, 0xffffff);
        owner.ShowUnobscuredContent(); owner.Present(); owner.ReportNavigation();
    }
    private static void Committed(nint page, nint navigation, nint data, nint context)
    {
        if (Owner(page, context) is not { } owner || navigation != owner.activeNavigation) return;
        WKViewResetHostTextInput(owner.view);
        owner.committed = true;
        owner.commitMilliseconds = owner.navigationClock.Elapsed.TotalMilliseconds;
        owner.ReportNavigation();
    }
    private static void Finished(nint page, nint navigation, nint data, nint context)
    {
        if (Owner(page, context) is not { } owner || navigation != owner.activeNavigation) return;
        owner.loading = false; owner.ReportNavigation();
        owner.finishMilliseconds = owner.navigationClock.Elapsed.TotalMilliseconds;
        // A valid early "none" or unavailable result must not own the final
        // document just because readiness produced no extra backing-store update.
        owner.fixedColorSchedule.Wake();
        owner.InvalidatePresentation();
        owner.ContentRenderingUpdated?.Invoke();
        _ = owner.EvaluateAsync("window.__devduoReportContainerObservation?.()");
    }
    private static void Failed(nint page, nint navigation, nint failure, nint data, nint context)
    {
        if (Owner(page, context) is not { } owner || navigation != owner.activeNavigation) return;
        owner.loading = false;
        owner.finishMilliseconds = owner.navigationClock.Elapsed.TotalMilliseconds;
        var code = WKErrorGetErrorCode(failure);
        if (code is not (302 or -999)) owner.error = new NavigationError(CopyString(WKErrorCopyDomain(failure)) + ":" + code, CopyString(WKErrorCopyLocalizedDescription(failure)));
        owner.ReportNavigation();
    }
    private static void SameDocument(nint page, nint navigation, int kind, nint data, nint context) => Owner(page, context)?.ReportNavigation();
    private static void Crashed(nint page, nint context)
    {
        if (Owner(page, context) is not { } owner) return;
        owner.InvalidateFixedContainerColors();
        owner.loading = false; owner.error = new NavigationError("WebKit:ProcessTerminated", "WebKit web process terminated."); owner.Present(); owner.ReportNavigation();
        owner.ResetScrollIndicator(true);
    }
    private void ReportNavigation()
    {
        if (!IsReady) return;
        var activeUrl = WKPageCopyActiveURL(page);
        try { if (activeUrl != 0) url = CopyString(WKURLCopyString(activeUrl)); }
        finally { if (activeUrl != 0) WKRelease(activeUrl); }
        var next = new NavigationState(url, CopyString(WKPageCopyTitle(page)), loading, Math.Clamp(WKPageGetEstimatedProgress(page), 0, 1), WKPageCanGoBack(page), WKPageCanGoForward(page), error);
        if (next == Navigation) return;
        Navigation = next; NavigationChanged?.Invoke(next);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            ClosePageDialogs();
            if (view != 0) WKViewResetHostTextInput(view);
            StopRemoteInspection();
            CloseInspector();
            disposed = true; views.Remove(token); metadataTimer.Stop(); metadataTimer.Dispose();
            InvalidateFixedContainerColors();
            ResetScrollIndicator(true);
            indicatorAnimation.Stop(); indicatorAnimation.Dispose(); scrollIndicator.Dispose(); ScrollIndicatorChanged = null;
            foreach (var pending in scripts.Where(entry => entry.Value.Owner == this).ToArray())
            { scripts.Remove(pending.Key); pending.Value.Completion.TrySetResult(null); }
            foreach (var pending in colors.Where(entry => entry.Value.Owner == this).ToArray())
            { colors.Remove(pending.Key); pending.Value.Completion.TrySetResult(null); }
            if (controller != 0) WKUserContentControllerRemoveAllUserMessageHandlers(controller);
            if (remoteDebuggingScript != 0) { WKRelease(remoteDebuggingScript); remoteDebuggingScript = 0; }
            if (inputUserScript != 0) { WKRelease(inputUserScript); inputUserScript = 0; }
            installedRemoteDebuggingSource = null;
            RemoteDebuggingMessageReceived = null;
            DocumentNavigationStarted = null;
            FixedContainerColorsChanged = null;
            ContentRenderingUpdated = null;
            keyboardWindow?.ReleaseHandle();
            keyboardWindow = null;
            HideTouchPointer();
            touchPointerWindow?.ReleaseHandle();
            touchPointerWindow = null;
            composition?.Dispose();
            composition = null;
            if (browserWindow != 0) DestroyWindow(browserWindow);
            foreach (var value in new[] { view, configuration, store, controller }) if (value != 0) WKRelease(value);
            view = page = browserWindow = configuration = store = controller = 0;
        }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetParent(nint window);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    private sealed class HostKeyboardWindow : System.Windows.Forms.NativeWindow
    {
        private readonly Action<HostCommand> commandRequested;
        internal Func<int, int, WindowsComposition.Action>? ChromeHit;
        internal Action<WindowsComposition.Action>? ChromeClick;
        internal PointerMessageHandler? PointerMessage;
        internal Action<int, string>? ScrollInput;
        internal Func<nint, nint>? AccessibleObjectRequested;
        internal Func<nint, nint, nint>? UiaProviderRequested;
        internal Action<uint, ulong>? AccessibilityChanged;
        internal Action? SourceDestroyed;
        private WindowsComposition.Action pressed;
        private long paints, wheels, lastPaintTick;
        private long compositionStarts, compositionUpdates, compositionResults, compositionEnds;
        private double paintWorkMilliseconds, maximumPaintWorkMilliseconds;
        private readonly Queue<double> recentPaintMilliseconds = new();
        internal object Diagnostics => new { paints, wheels, paintWorkMilliseconds, maximumPaintWorkMilliseconds,
            compositionStarts, compositionUpdates, compositionResults, compositionEnds,
            lastPaintMilliseconds = lastPaintTick * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
            recentPaintMilliseconds = recentPaintMilliseconds.ToArray() };
        internal HostKeyboardWindow(nint window, Action<HostCommand> callback)
        {
            commandRequested = callback;
            AssignHandle(window);
        }
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            // The OS can destroy an owned source before Avalonia detaches its
            // managed host. Reject late layout/DPI work during that interval.
            if (message.Msg == 0x2) SourceDestroyed?.Invoke();
            if (message.Msg == 0x3D && unchecked((int)message.LParam) == -25 && UiaProviderRequested is { } uia)
            {
                message.Result = uia(message.WParam, message.LParam);
                if (message.Result != 0) return;
            }
            if (message.Msg == 0x10D) compositionStarts++;
            if (message.Msg == 0x10E) compositionEnds++;
            if (message.Msg == 0x10F)
            {
                if ((message.LParam.ToInt64() & 8) != 0) compositionUpdates++;
                if ((message.LParam.ToInt64() & 0x800) != 0) compositionResults++;
            }
            if (message.Msg == 0x803D)
            {
                AccessibilityChanged?.Invoke((uint)message.WParam, unchecked((ulong)message.LParam.ToInt64()));
                message.Result = 0;
                return;
            }
            if (message.Msg == 0x3D && unchecked((int)message.LParam) == -4 && AccessibleObjectRequested is { } accessibility)
            {
                message.Result = accessibility(message.WParam);
                if (message.Result != 0) return;
            }
            if (message.Msg == 0x20A) wheels++;
            if (PointerMessage?.Invoke(ref message) == true) return;
            if (message.Msg == 0x20A)
            {
                var delta = (short)(message.WParam.ToInt64() >> 16);
                ScrollInput?.Invoke(-Math.Sign(delta), "native wheel");
            }
            if (message.Msg is 0x201 or 0x202)
            {
                var value = message.LParam.ToInt64();
                var role = ChromeHit?.Invoke((short)value, (short)(value >> 16)) ?? WindowsComposition.Action.None;
                if (message.Msg == 0x201) pressed = role;
                if (role != WindowsComposition.Action.None)
                {
                    if (message.Msg == 0x202 && pressed == role) { pressed = WindowsComposition.Action.None; ChromeClick?.Invoke(role); }
                    message.Result = 0;
                    return;
                }
            }
            if (message.Msg is 0x100 or 0x104)
            {
                var keys = (System.Windows.Forms.Keys)(int)message.WParam | System.Windows.Forms.Control.ModifierKeys;
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
                if (command is { } value) { commandRequested(value); message.Result = 0; return; }
                if ((keys & (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt)) == 0)
                {
                    var direction = keys switch
                    {
                        System.Windows.Forms.Keys.Down or System.Windows.Forms.Keys.PageDown or System.Windows.Forms.Keys.End or System.Windows.Forms.Keys.Space => 1,
                        System.Windows.Forms.Keys.Up or System.Windows.Forms.Keys.PageUp or System.Windows.Forms.Keys.Home or (System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Space) => -1,
                        _ => 0
                    };
                    if (direction != 0) ScrollInput?.Invoke(direction, "native keyboard");
                }
            }
            if (message.Msg != 0xF) { base.WndProc(ref message); return; }
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            base.WndProc(ref message);
            lastPaintTick = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsed = (lastPaintTick - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            paints++;
            paintWorkMilliseconds += elapsed;
            maximumPaintWorkMilliseconds = Math.Max(maximumPaintWorkMilliseconds, elapsed);
            recentPaintMilliseconds.Enqueue(lastPaintTick * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            if (recentPaintMilliseconds.Count > 64) recentPaintMilliseconds.Dequeue();
        }
    }
    private sealed class ChromeAccessibility(WindowsWebKitView owner) : System.Windows.Forms.Control.ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner.disposed ? 0 : owner.Chrome.Length;
        public override System.Windows.Forms.AccessibleObject? GetChild(int index) =>
            index >= 0 && index < GetChildCount() ? new ChromeItemAccessibility(owner, index, this) : null;
    }
    private sealed class ChromeItemAccessibility(WindowsWebKitView owner, int index, System.Windows.Forms.AccessibleObject parent) : System.Windows.Forms.AccessibleObject
    {
        private WindowsComposition.Chrome? Frame => !owner.disposed && index < owner.Chrome.Length ? owner.Chrome[index] : null;
        public override System.Windows.Forms.AccessibleObject Parent => parent;
        public override string? Name
        {
            get => index < owner.ChromeNames.Length ? owner.ChromeNames[index] : "";
            set { }
        }
        public override System.Windows.Forms.AccessibleRole Role => Frame is { } f ? f.Action != WindowsComposition.Action.None ? System.Windows.Forms.AccessibleRole.PushButton :
            f.Kind == WindowsComposition.Kind.Clock ? System.Windows.Forms.AccessibleRole.StaticText :
            f.Kind is WindowsComposition.Kind.Camera or WindowsComposition.Kind.Wifi ? System.Windows.Forms.AccessibleRole.Graphic : System.Windows.Forms.AccessibleRole.Grouping : System.Windows.Forms.AccessibleRole.None;
        public override Rectangle Bounds => Frame is { } f ? owner.PresentationBounds?.Invoke(new((int)f.X, (int)f.Y, (int)f.Width, (int)f.Height)) ??
            new(owner.PointToScreen(new Point((int)f.X, (int)f.Y)), new System.Drawing.Size((int)f.Width, (int)f.Height)) : Rectangle.Empty;
        public override string DefaultAction => Frame is { Action: not WindowsComposition.Action.None } ? AppLocalization.Current.Text("Accessibility.Press") : "";
        public override void DoDefaultAction() { if (Frame is { Action: not WindowsComposition.Action.None } f) owner.ChromeInvoked?.Invoke(f.Action); }
    }
}
#endif
