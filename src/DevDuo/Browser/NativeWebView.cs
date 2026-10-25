using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
namespace DevDuo;

// Host commands that must keep working while the native browser owns keyboard focus.
public enum HostCommand { FocusAddress, Reload, Back, Forward, SaveState, ToggleSettings, ToggleInspector }

public sealed partial class NativeWebView(DeviceProfile profile, string initialUrl, SafariContainerOptions options) : NativeControlHost
{
    private IntPtr native;
    // Incremented on every native creation/destruction so posted callbacks from an old container are ignored.
    private int generation;
    private GCHandle callbackHandle;
    private readonly string observer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", "browser-observer.js"));
#if WINDOWS
    private WindowsWebViewPanel? panel;
    internal WindowsWebViewPanel? WindowsPanel => panel;
    private WindowsScreenPresenter? clip;
#endif
    // Full container size and the offset of its visible part, in DIPs. This control itself is sized to
    // the visible part because native children are not clipped by Avalonia scrolling ancestors.
    private (double Width, double Height, double OffsetX, double OffsetY, double Scaling, int Rotation)? viewport;
    public bool HasViewport => viewport is not null;
    public bool CanInspect
    {
        get
        {
#if WINDOWS
            return panel?.Browser.IsReady == true;
#else
            return native != IntPtr.Zero && OperatingSystem.IsMacOS();
#endif
        }
    }
    public bool InspectorVisible
    {
        get
        {
#if WINDOWS
            return panel?.Browser.InspectorVisible == true;
#else
            return native != IntPtr.Zero && MacInspectorVisible(native) != 0;
#endif
        }
    }
    public void ToggleInspector()
    {
#if WINDOWS
        panel?.Browser.ToggleInspector();
#else
        if (native != IntPtr.Zero) CheckDeveloperResult(MacSetInspectorVisible(native, InspectorVisible ? 0 : 1));
#endif
    }

    // Use explicit visibility for API PUTs, including closing a not-yet-visible Inspector.
    internal void SetInspectorVisible(bool visible)
    {
#if WINDOWS
        panel?.Browser.SetInspectorVisible(visible);
#else
        if (native != IntPtr.Zero) CheckDeveloperResult(MacSetInspectorVisible(native, visible ? 1 : 0));
#endif
    }

    public void UpdateScene(DeviceProfile geometry, SafariContainerOptions current)
    {
        profile = geometry;
        options = current;
        if (native == IntPtr.Zero) return;
        if (OperatingSystem.IsMacOS())
            MacUpdateScene(native, profile.ScreenMaskPath ?? "", profile.MaskCanvasWidth, profile.MaskCanvasHeight, current.ToJson());
#if WINDOWS
        else
        {
            clip?.UpdateScene(geometry);
            panel?.UpdateScene(geometry, current);
        }
#endif
    }

    // Run after Avalonia has arranged the Canvas slot. Its native attachment is moved separately
    // from the explicit clip/content configuration, and both must describe the same layout pass.
    public void SynchronizePlacement()
    {
        TryUpdateNativeControlPosition();
        ApplyViewport();
    }
    public NavigationState Navigation { get; private set; } = NavigationState.Empty;
    public event Action<NavigationState>? NavigationChanged;
#pragma warning disable CS0067 // Raised only by the Windows panel; macOS shortcuts use the native menu.
    public event Action<HostCommand>? CommandRequested;
#pragma warning restore CS0067
    private BuiltinPageKind pageOnCreate;
    public BuiltinPageKind InitialBuiltinPage { get => pageOnCreate; init => pageOnCreate = value; }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        generation++;
        PrepareDeveloperProfile();
        if (OperatingSystem.IsMacOS())
        {
            native = MacCreate(InitialBuiltinPage == BuiltinPageKind.None ? initialUrl : "", profile.ScreenMaskPath ?? "", profile.MaskCanvasWidth, profile.MaskCanvasHeight, options.ToJson(), observer, PageInteraction.Source);
            if (native == IntPtr.Zero)
                throw new InvalidOperationException(AppLocalization.Current.Text("Error.NativeCreate"));
            callbackHandle = GCHandle.Alloc(this);
            MacSetNavigationCallback(native, MacNavigationChanged, GCHandle.ToIntPtr(callbackHandle));
            InitializeDeveloperTools();
            if (InitialBuiltinPage != BuiltinPageKind.None)
                ShowBuiltinPage(InitialBuiltinPage);
            ApplyViewport();
            return new PlatformHandle(MacClipView(native), "NSView");
        }
#if WINDOWS
        panel = new WindowsWebViewPanel(profile, InitialBuiltinPage == BuiltinPageKind.None ? initialUrl : null, options, observer, parent.Handle);
        var created = generation;
        panel.NavigationChanged += state => Dispatcher.UIThread.Post(() => ReportNavigation(created, state));
        panel.CommandRequested += command => Dispatcher.UIThread.Post(() =>
        {
            if (created == generation && native != IntPtr.Zero)
                CommandRequested?.Invoke(command);
        });
        native = panel.Handle;
        InitializeDeveloperTools();
        // Child windows are clipped by their parent window; the panel is offset inside this clip window.
        Focusable = true;
        clip = new WindowsScreenPresenter(panel, profile, parent.Handle, () => { Focus(); });
        SetParent(clip.Handle, parent.Handle);
        if (InitialBuiltinPage != BuiltinPageKind.None)
            ShowBuiltinPage(InitialBuiltinPage);
        ApplyViewport();
        return new PlatformHandle(clip.Handle, "HWND");
#else
        throw new PlatformNotSupportedException(AppLocalization.Current.Text("Error.Platform"));
#endif
    }

    private static readonly NavigationCallback MacNavigationChanged = OnMacNavigation;

    // Invoked by WebKit on the AppKit main thread. Posting decouples it from native calls in progress.
    private static void OnMacNavigation(IntPtr context, IntPtr json)
    {
        if (context == IntPtr.Zero || GCHandle.FromIntPtr(context).Target is not NativeWebView view)
            return;
        var state = NavigationState.FromJson(Marshal.PtrToStringUTF8(json));
        var created = view.generation;
        Dispatcher.UIThread.Post(() => view.ReportNavigation(created, state));
    }

    private void ReportNavigation(int created, NavigationState state)
    {
        if (created != generation || native == IntPtr.Zero)
            return;
        Navigation = state;
        NavigationChanged?.Invoke(state);
    }

    // Sets the full container size and the offset of the visible part. Scaling converts DIPs to Windows pixels.
    public void SetViewport(double width, double height, double offsetX, double offsetY, double scaling, int rotation = 0)
    {
        var next = (width, height, offsetX, offsetY, scaling, rotation);
        if (viewport == next)
            return;
        viewport = next;
        ApplyViewport();
    }

    private void ApplyViewport()
    {
        if (native == IntPtr.Zero || viewport is not { } value)
            return;
        if (OperatingSystem.IsMacOS())
            MacSetViewport(native, value.Width, value.Height, value.OffsetX, value.OffsetY, value.Rotation);
#if WINDOWS
        else
            clip?.Configure(value.Width, value.Height, value.OffsetX, value.OffsetY, value.Scaling, value.Rotation);
#endif
    }

    public void GoBack()
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacGoBack(native);
#if WINDOWS
        else
            panel?.GoBack();
#endif
    }
    public void GoForward()
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacGoForward(native);
#if WINDOWS
        else
            panel?.GoForward();
#endif
    }
    public void Reload()
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacReload(native);
#if WINDOWS
        else
            panel?.Reload();
#endif
    }
    public void Stop()
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacStop(native);
#if WINDOWS
        else
            panel?.Stop();
#endif
    }
    public void Navigate(string url)
    {
        initialUrl = url;
        pageOnCreate = BuiltinPageKind.None;
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacNavigate(native, url);
#if WINDOWS
        else
            panel?.Navigate(url);
#endif
    }
    public void Configure(SafariContainerOptions current)
    {
        var languageChanged = options.UiLanguage != current.UiLanguage;
        options = current;
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacConfigure(native, current.ToJson());
#if WINDOWS
        else
            panel?.Configure(current);
#endif
        if (languageChanged)
        {
            UpdateFixtureLanguage();
            Evaluate(WelcomePage.LocalizationScript(AppLocalization.Current));
        }
    }
    public void ShowBuiltinPage(BuiltinPageKind kind)
    {
        var address = BuiltinPages.Address(kind).AbsoluteUri;
        // Host actions can arrive before Avalonia attaches the native control.
        // The latest requested kind must replace the constructor's initial page.
        pageOnCreate = kind;
        if (native == IntPtr.Zero)
            return;
        string html;
        if (kind == BuiltinPageKind.Welcome)
            html = WelcomePage.Html(AppLocalization.Current);
        else
            html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Pages", "color-scroll.html"))
                .Replace("__DEVDUO_LANGUAGE__", AppLocalization.Current.Language)
                .Replace("__DEVDUO_STRINGS__", JsonSerializer.Serialize(AppLocalization.Current.FixtureStrings()));
        if (OperatingSystem.IsMacOS())
            MacHtml(native, html, address);
#if WINDOWS
        else
            panel?.ShowHtml(html, address);
#endif
    }
    private void UpdateFixtureLanguage()
    {
        if (native == IntPtr.Zero)
            return;
        var script = "if(document.documentElement.dataset.devduoFixture==='color-lab-v1' && " +
            "typeof window.devduoLocalizeFixture==='function')window.devduoLocalizeFixture(" +
            JsonSerializer.Serialize(AppLocalization.Current.Language) + "," +
            JsonSerializer.Serialize(AppLocalization.Current.FixtureStrings()) + ");";
        Evaluate(script);
    }
    internal void Evaluate(string script)
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacEvaluate(native, script);
#if WINDOWS
        else
            panel?.Evaluate(script);
#endif
    }
    public string ReadState()
    {
        var json = ReadNativeState();
        if (json == "{}" || viewport is not { } value) return json;
        var state = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        state["presentation"] = JsonSerializer.SerializeToNode(new
        {
            sceneInCanvasDegrees = value.Rotation,
            bodyInCanvasDegrees = options.ReferenceScene?.BodyRotationDegrees,
            // The two frames are relative; their sum is the scene's final canvas transform.
            contentInBodyDegrees = (value.Rotation - (options.ReferenceScene?.BodyRotationDegrees ?? 0) + 360) % 360,
            screenFrameDip = new { width = value.Width, height = value.Height, offsetX = value.OffsetX, offsetY = value.OffsetY },
            sceneFrameDip = new { width = value.Rotation % 180 == 0 ? value.Width : value.Height,
                height = value.Rotation % 180 == 0 ? value.Height : value.Width },
            rendering = "live native composition; no snapshots"
        });
        return state.ToJsonString();
    }

    private string ReadNativeState()
    {
        if (native == IntPtr.Zero)
            return "{}";
        if (OperatingSystem.IsMacOS())
        {
            var pointer = MacState(native);
            if (pointer == IntPtr.Zero)
                return "{}";
            try
            {
                return Marshal.PtrToStringUTF8(pointer) ?? "{}";
            }
            finally { MacFree(pointer); }
        }
#if WINDOWS
        return panel?.ReadState() ?? "{}";
#else
        return "{}";
#endif
    }
#if WINDOWS
    internal Task<string?> EvaluateResultAsync(string script) => panel?.EvaluateResultAsync(script) ?? Task.FromResult<string?>(null);
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new WindowsWebViewHostPeer(this);
#endif
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        ReleaseDeveloperTools();
        generation++;
        if (OperatingSystem.IsMacOS())
        {
            // Destroy clears the native callback before its context handle is released. The platform
            // handle is the clip view; the container pointer is what the bridge owns.
            if (native != IntPtr.Zero)
                MacDestroy(native);
            if (callbackHandle.IsAllocated)
                callbackHandle.Free();
        }
#if WINDOWS
        else
        {
            panel?.Dispose();
            panel = null;
            clip?.Dispose();
            clip = null;
        }
#endif
        native = IntPtr.Zero;
    }
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_create")]
    private static extern IntPtr MacCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string url, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, double width, double height, [MarshalAs(UnmanagedType.LPUTF8Str)] string options, [MarshalAs(UnmanagedType.LPUTF8Str)] string observer, [MarshalAs(UnmanagedType.LPUTF8Str)] string inputSource);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_evaluate")]
    private static extern void MacEvaluate(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string script);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_navigate")]
    private static extern void MacNavigate(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_configure")]
    private static extern void MacConfigure(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string options);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_update_scene")]
    private static extern void MacUpdateScene(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, double width, double height, [MarshalAs(UnmanagedType.LPUTF8Str)] string options);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_html")]
    private static extern void MacHtml(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string html, [MarshalAs(UnmanagedType.LPUTF8Str)] string baseUrl);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_state_json")]
    private static extern IntPtr MacState(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_free")]
    private static extern void MacFree(IntPtr pointer);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_destroy")]
    private static extern void MacDestroy(IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NavigationCallback(IntPtr context, IntPtr json);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_navigation_callback")]
    private static extern void MacSetNavigationCallback(IntPtr view, NavigationCallback callback, IntPtr context);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_clip_view")]
    private static extern IntPtr MacClipView(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_viewport")]
    private static extern void MacSetViewport(IntPtr view, double width, double height, double offsetX, double offsetY, int rotation);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_go_back")]
    private static extern void MacGoBack(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_go_forward")]
    private static extern void MacGoForward(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_reload")]
    private static extern void MacReload(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_stop")]
    private static extern void MacStop(IntPtr view);
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
#endif
}
