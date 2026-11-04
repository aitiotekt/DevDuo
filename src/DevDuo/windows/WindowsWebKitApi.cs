#if WINDOWS
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DevDuo;

// C ABI from the pinned Windows Port plus the maintained host viewport extension.
// Keep WebKit loaded for the process lifetime; its workers and globals outlive individual views.
internal static class WindowsWebKitApi
{
    private const string Library = "DevDuoWindowsWebKit";
    private static nint module;
    internal static double VerificationMilliseconds { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint CopyScrollFramesCallback(nint view, [Out] WindowsWebKitView.HostScrollFrame[] output, uint capacity);
    internal static CopyScrollFramesCallback? CopyHostScrollFrames { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint CreateHostFrameViewCallback(Rect rectangle, nint configuration, nint parent);
    internal static CreateHostFrameViewCallback? CreateHostFrameView { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void HostScrollingStateCallback(ulong request, nint state, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RequestScrollingStateCallback(nint view, ulong request, nint context, HostScrollingStateCallback callback);
    internal static RequestScrollingStateCallback? RequestHostScrollingState { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal delegate bool SetTouchEnabledCallback(nint view, [MarshalAs(UnmanagedType.I1)] bool enabled);
    internal static SetTouchEnabledCallback? SetHostTouchEnabled { get; private set; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint CopyTouchDiagnosticsCallback(nint view);
    internal static CopyTouchDiagnosticsCallback? CopyHostTouchDiagnostics { get; private set; }
    internal static void Load()
    {
        if (module != 0) return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBKIT_INSPECTOR_SERVER")))
            throw new InvalidOperationException("Remove WEBKIT_INSPECTOR_SERVER and enable native remote inspection through DevDuo's developer settings.");
        var verification = System.Diagnostics.Stopwatch.StartNew();
        if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows WebKit requires an x64 host process.");
        var directory = RuntimeDirectory();
        var manifestPath = Path.Combine(directory, "..", "runtime-manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        if (!root.TryGetProperty("hostDesktopApiVersion", out var desktop) || desktop.GetInt32() != 1 ||
            root.GetProperty("hostViewportApiVersion").GetInt32() != 2 ||
            !root.TryGetProperty("hostFrameTimingApiVersion", out var timing) || timing.GetInt32() != 2 ||
            !root.TryGetProperty("hostFramePresentationApiVersion", out var presentation) || presentation.GetInt32() != 1 ||
            !root.TryGetProperty("hostFrameSurfaceApiVersion", out var surfaces) || surfaces.GetInt32() != 1 ||
            !root.TryGetProperty("hostAccessibilityApiVersion", out var accessibility) || accessibility.GetInt32() != 2 ||
            !root.TryGetProperty("contentPaintingVersion", out var painting) || painting.GetInt32() != 2 ||
            !root.TryGetProperty("contentColorApiVersion", out var contentColor) || contentColor.GetInt32() != 1 ||
            !root.TryGetProperty("hostAppearanceApiVersion", out var appearance) || appearance.GetInt32() != 1 ||
            !root.TryGetProperty("hostDeveloperApiVersion", out var developer) || developer.GetInt32() != 1 ||
            !root.TryGetProperty("hostFixedContainerColorApiVersion", out var fixedContainer) || fixedContainer.GetInt32() != 2 ||
            !root.TryGetProperty("hostScrollIndicatorApiVersion", out var scrollIndicator) || scrollIndicator.GetInt32() != 1)
            throw new InvalidOperationException("Windows WebKit requires desktop v1, asynchronous accessibility v2, frame timing v2, frame presentation/surface v1, viewport v2, painting v2, content color v1, host appearance v1, host developer v1, fixed-container color v2 and scroll indicator v1. Rebuild the pinned runtime.");
        if (!SetDefaultDllDirectories(0x1000) || AddDllDirectory(directory) == 0)
            throw new InvalidOperationException("Cannot configure the Windows WebKit dependency search path.");
        var loaded = LoadLibraryExW(Path.Combine(directory, "WebKit2.dll"), 0, 0x1100);
        if (loaded == 0) throw new InvalidOperationException("Cannot load WebKit2.dll: " + Marshal.GetLastPInvokeError());
        if (root.TryGetProperty("hostScrollFrameApiVersion", out var scrollFrames))
        {
            if (scrollFrames.GetInt32() != 1 ||
                !NativeLibrary.TryGetExport(loaded, "WKViewGetHostScrollFrameAPIVersion", out var versionExport) ||
                Marshal.GetDelegateForFunctionPointer<VersionCallback>(versionExport)() != 1 ||
                !NativeLibrary.TryGetExport(loaded, "WKViewCopyHostScrollFrames", out var copyExport) ||
                !NativeLibrary.TryGetExport(loaded, "WKViewCreateWithHostFrameSurface", out var createExport) ||
                !NativeLibrary.TryGetExport(loaded, "WKViewRequestHostScrollingState", out var stateExport))
                throw new InvalidOperationException("Scroll frame metadata API v1 is unavailable. Rebuild the pinned runtime.");
            CopyHostScrollFrames = Marshal.GetDelegateForFunctionPointer<CopyScrollFramesCallback>(copyExport);
            CreateHostFrameView = Marshal.GetDelegateForFunctionPointer<CreateHostFrameViewCallback>(createExport);
            RequestHostScrollingState = Marshal.GetDelegateForFunctionPointer<RequestScrollingStateCallback>(stateExport);
        }
        if (root.TryGetProperty("hostTouchApiVersion", out var touch) && touch.GetInt32() != 0)
        {
            if (touch.GetInt32() != 1 ||
                !NativeLibrary.TryGetExport(loaded, "WKViewGetHostTouchAPIVersion", out var touchVersion) ||
                Marshal.GetDelegateForFunctionPointer<VersionCallback>(touchVersion)() != 1 ||
                !NativeLibrary.TryGetExport(loaded, "WKViewSetHostTouchEnabled", out var touchSet) ||
                !NativeLibrary.TryGetExport(loaded, "WKViewCopyHostTouchDiagnostics", out var touchCopy))
                throw new InvalidOperationException("Native single-contact API v1 is unavailable. Rebuild the experimental runtime.");
            SetHostTouchEnabled = Marshal.GetDelegateForFunctionPointer<SetTouchEnabledCallback>(touchSet);
            CopyHostTouchDiagnostics = Marshal.GetDelegateForFunctionPointer<CopyTouchDiagnosticsCallback>(touchCopy);
        }
        foreach (var method in typeof(WindowsWebKitApi).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
        {
            var import = method.GetCustomAttributes(typeof(DllImportAttribute), false).OfType<DllImportAttribute>().SingleOrDefault();
            if (import?.Value == Library && !NativeLibrary.TryGetExport(loaded, import.EntryPoint ?? method.Name, out _))
                throw new InvalidOperationException("Required Windows WebKit export missing: " + method.Name);
        }
        var desktopVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostDesktopAPIVersion"));
        if (desktopVersion() != 1) throw new InvalidOperationException("Host desktop API v1 export missing.");
        var accessibilityVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostAccessibilityAPIVersion"));
        if (accessibilityVersion() != 2) throw new InvalidOperationException("Asynchronous accessibility API v2 export missing.");
        var timingVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostFrameTimingAPIVersion"));
        if (timingVersion() != 2) throw new InvalidOperationException("Frame timing API v2 export missing.");
        var framePresentationVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostFramePresentationAPIVersion"));
        if (framePresentationVersion() != 1) throw new InvalidOperationException("Frame presentation API v1 export missing.");
        var surfaceVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostFrameSurfaceAPIVersion"));
        if (surfaceVersion() != 1) throw new InvalidOperationException("Frame surface API v1 export missing.");
        var version = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetViewportHostAPIVersion"));
        if (version() != 2) throw new InvalidOperationException("Host viewport API v2 export missing.");
        var colorVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetContentColorAPIVersion"));
        if (colorVersion() != 1) throw new InvalidOperationException("Content color API v1 export missing.");
        var appearanceVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostAppearanceAPIVersion"));
        if (appearanceVersion() != 1) throw new InvalidOperationException("Host appearance API v1 export missing.");
        var developerVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostDeveloperAPIVersion"));
        if (developerVersion() != 1) throw new InvalidOperationException("Host developer API v1 export missing.");
        var fixedContainerVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetFixedContainerColorAPIVersion"));
        if (fixedContainerVersion() != 2) throw new InvalidOperationException("Fixed-container color API v2 export missing.");
        var scrollIndicatorVersion = Marshal.GetDelegateForFunctionPointer<VersionCallback>(NativeLibrary.GetExport(loaded, "WKViewGetHostScrollIndicatorAPIVersion"));
        if (scrollIndicatorVersion() != 1) throw new InvalidOperationException("Host scroll indicator API v1 export missing.");
        NativeLibrary.SetDllImportResolver(typeof(WindowsWebKitApi).Assembly,
            (name, _, _) => name == Library ? loaded : 0);
        module = loaded;
        VerificationMilliseconds = verification.Elapsed.TotalMilliseconds;
    }

    internal static string RuntimeDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("DEVDUO_WEBKIT_RUNTIME must be an absolute Windows WebKit bin directory.");
            // An explicit override must fail on its own errors, never select another runtime.
            return Path.GetFullPath(configured);
        }
        var adjacent = Path.Combine(AppContext.BaseDirectory, "webkit", "bin");
        if (Directory.Exists(adjacent)) return adjacent;
        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
        {
            if (!File.Exists(Path.Combine(parent.FullName, "devduo-metadata.toml"))) continue;
            var selectionPath = Path.Combine(parent.FullName, "temp", "build", "windows-webkit-runtime.json");
            if (File.Exists(selectionPath))
            {
                using var selection = JsonDocument.Parse(File.ReadAllText(selectionPath));
                var selected = selection.RootElement;
                if (!selected.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var number) || number != 1 ||
                    !selected.TryGetProperty("binDirectory", out var bin) || bin.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(bin.GetString()))
                    throw new InvalidDataException("Invalid Windows WebKit runtime selection. Rebuild the pinned runtime.");
                // Successful source builds select the checkout runtime explicitly.
                // A stale selection must fail validation, never load an older DLL.
                return Path.GetFullPath(Path.Combine(parent.FullName, bin.GetString()!));
            }
            return Path.Combine(parent.FullName, "temp", "build", "windows-webkit-host", "bin");
        }
        throw new DirectoryNotFoundException("Windows WebKit is required. Build with mise --env windows-webkit exec -- just build-windows-webkit-runtime, or set DEVDUO_WEBKIT_RUNTIME to its absolute bin directory.");
    }

    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Size(double Width, double Height);
    // WKPageNavigationClientV0: two-word base followed by 21 callback pointers.
    [StructLayout(LayoutKind.Sequential)] internal struct NavigationClient
    {
        public int Version;
        public nint Info, PolicyAction, PolicyResponse, PolicyPlugin, Start, Redirect, FailProvisional,
            Commit, Finish, Fail, FailSubframe, DocumentLoaded, SameDocument, Rendering,
            CanAuthenticate, Authenticate, Crash, Crypto, BeginGesture, WillEndGesture, EndGesture, RemoveSnapshot;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ScriptCallback(nint result, nint error, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ContentColorCallback(ulong request, uint status, uint rgb, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewRequestContentEdgeColor(nint view, int x, int y, int width, int height, ulong request, nint context, ContentColorCallback callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void MessageCallback(nint message, nint reply, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void NavigationCallback(nint page, nint navigation, nint data, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FailureCallback(nint page, nint navigation, nint error, nint data, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void SameDocumentCallback(nint page, nint navigation, int kind, nint data, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void CrashCallback(nint page, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VersionCallback();
    // WKPageUIClientV6 from the pinned WKPageUIClient.h. The first 58 callback
    // slots (versions 0–5 and createNewPage) remain unset; no private API is used.
    [StructLayout(LayoutKind.Sequential)] internal struct UiClient
    {
        public int Version;
        public nint Info;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 58)] public nint[] UnusedCallbacks;
        public nint Alert, Confirm, Prompt, Unused;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DialogCallback(nint page, nint message, nint frame, nint origin, nint listener, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void PromptCallback(nint page, nint message, nint value, nint frame, nint origin, nint listener, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageSetPageUIClient(nint page, ref UiClient client);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageRunJavaScriptAlertResultListenerCall(nint listener);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageRunJavaScriptConfirmResultListenerCall(nint listener, [MarshalAs(UnmanagedType.I1)] bool result);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageRunJavaScriptPromptResultListenerCall(nint listener, nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKSecurityOriginCopyToString(nint origin);

    internal static string String(nint value)
    {
        if (value == 0) return "";
        var bytes = new byte[checked((int)WKStringGetMaximumUTF8CStringSize(value))];
        var length = checked((int)WKStringGetUTF8CString(value, bytes, (nuint)bytes.Length));
        return Encoding.UTF8.GetString(bytes, 0, Math.Max(0, length - 1));
    }
    internal static string CopyString(nint value)
    {
        try { return String(value); }
        finally { if (value != 0) WKRelease(value); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetDefaultDllDirectories(uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint AddDllDirectory(string directory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string file, nint reserved, uint flags);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetHostDesktopAPIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewResetHostTextInput(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKViewCopyHostTextInputDiagnostics(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetHostTextInputState(nint view, out uint error, out ulong editorId);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKViewCopyHostAccessibilityTree(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewCopyHostFrameTimings(nint view, [Out] WindowsWebKitView.HostFrameTiming[] output, uint capacity);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool WKViewCopyHostFrameEdges(nint view, out WindowsWebKitView.HostFrameEdges output);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool WKViewGetHostFrameClock(out double nativeSeconds, out double qpcSeconds, out double uncertaintySeconds);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewSetHostFrameSurfaceEnabled(nint view, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool WKViewAcquireHostFrameSurface(nint view, out WindowsWebKitView.HostFrameSurface output);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewAcknowledgeHostFrameSurface(nint view, ulong sequence, [MarshalAs(UnmanagedType.I1)] bool presented);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewReplayHostFrameSurface(nint view, [MarshalAs(UnmanagedType.I1)] bool resourcesLost);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong WKViewGetHostGDIUploadCount(nint view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void HostAccessibilityTreeCallback(ulong request, nint value, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewRequestHostAccessibilityTree(nint view, ulong request, nint context, HostAccessibilityTreeCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewPerformHostAccessibilityAction(nint view, nint document, ulong objectId, uint action, nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewSetHostDesktopGeometry(nint view, nint target, int degrees, int offsetX, int offsetY);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetViewportHostAPIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetHostDeveloperAPIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKInspectorStartHostRemoteServer(nint address, ushort port);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKInspectorStopHostRemoteServer();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ushort WKInspectorGetHostRemoteServerPort();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageSetAllowsRemoteInspection(nint page, [MarshalAs(UnmanagedType.I1)] bool allow);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetFixedContainerColorAPIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern uint WKViewGetHostScrollIndicatorAPIVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewSetHostScrollIndicatorEnabled(nint view, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void HostScrollIndicatorCallback(ulong request, uint status, uint flags, ulong epoch, ulong sequence, double position, double range, double viewportHeight, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewRequestHostScrollIndicatorState(nint view, ulong request, nint context, HostScrollIndicatorCallback callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void FixedContainerColorCallback(ulong request, uint status, ulong epoch, uint states, uint reasons, uint top, uint right, uint bottom, uint left, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewRequestFixedContainerColorsV2(nint view, ulong request, nint context, FixedContainerColorCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageSetCustomUserAgent(nint page, nint userAgent);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreEnableDefaultNetworkProxySettings(nint store);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreDisableNetworkProxySettings(nint store);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreEnableCustomNetworkProxySettings(nint store, nint proxyUrl, nint bypass);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewSetHostUnderPageBackgroundColor(nint view, uint rgb);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewSetHostPresentationGeometry(nint view, Size size, float scale);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewSetObscuredContentInsets(nint view, float top, float right, float bottom, float left);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKViewSetUnobscuredSizeRange(nint view, Size minimum, Size maximum);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKPageConfigurationCreate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKWebsiteDataStoreConfigurationCreate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKWebsiteDataStoreCreateWithConfiguration(nint configuration);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetGeneralStorageDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetLocalStorageDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetIndexedDBDatabaseDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetNetworkCacheDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetCacheStorageDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetServiceWorkerRegistrationDirectory(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKWebsiteDataStoreConfigurationSetCookieStorageFile(nint configuration, nint path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKPageConfigurationGetPreferences(nint configuration);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPreferencesSetDeveloperExtrasEnabled(nint preferences, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewSetUsesOffscreenRendering(nint view, [MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool WKViewGetHostRenderingStatistics(nint view, out ulong updates, out ulong acceleratedTransitions, out ulong staleUpdates);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKPageGetInspector(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKInspectorShow(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKInspectorConnect(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKInspectorIsConnected(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKInspectorDetach(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKInspectorClose(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKInspectorIsAttached(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKInspectorIsVisible(nint inspector);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKRetain(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageConfigurationSetWebsiteDataStore(nint configuration, nint store);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageConfigurationSetUserContentController(nint configuration, nint controller);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKUserContentControllerCreate();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKUserContentControllerAddScriptMessageHandler(nint controller, nint name, MessageCallback callback, nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKUserContentControllerRemoveAllUserMessageHandlers(nint controller);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKUserContentControllerAddUserScript(nint controller, nint script);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKUserContentControllerRemoveHostUserScript(nint controller, nint script);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKUserScriptCreateWithSource(nint source, int time, [MarshalAs(UnmanagedType.I1)] bool mainFrame);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKScriptMessageGetBody(nint message);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKScriptMessageGetFrameInfo(nint message);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKFrameInfoGetIsMainFrame(nint frame);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKFrameInfoGetPage(nint frame);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKCompletionListenerComplete(nint reply, nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKViewCreate(Rect rectangle, nint configuration, nint parent);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKViewGetWindow(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewWindowAncestryDidChange(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKViewGetPage(nint view);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool WKViewSetHostDarkAppearance(nint view, [MarshalAs(UnmanagedType.I1)] bool dark);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKViewSetIsInWindow(nint view, [MarshalAs(UnmanagedType.I1)] bool visible);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageSetPageNavigationClient(nint page, ref NavigationClient client);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageLoadURL(nint page, nint url);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageLoadHTMLString(nint page, nint html, nint baseUrl);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageGoBack(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageGoForward(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKPageCanGoBack(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool WKPageCanGoForward(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageReload(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageStopLoading(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKPageCopyTitle(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKPageCopyActiveURL(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double WKPageGetEstimatedProgress(nint page);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKPageEvaluateJavaScriptInMainFrame(nint page, nint source, nint context, ScriptCallback callback);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKURLCreateWithUTF8CString([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKURLCopyString(nint url);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKStringCreateWithUTF8CString([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint WKStringGetMaximumUTF8CStringSize(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint WKStringGetUTF8CString(nint value, byte[] buffer, nuint length);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong WKGetTypeID(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong WKStringGetTypeID();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKErrorCopyDomain(nint error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int WKErrorGetErrorCode(nint error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint WKErrorCopyLocalizedDescription(nint error);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void WKRelease(nint value);
}
#endif
