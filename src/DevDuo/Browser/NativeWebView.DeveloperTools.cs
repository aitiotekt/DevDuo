using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Avalonia.Threading;

namespace DevDuo;

public sealed partial class NativeWebView
{
    private DeveloperToolsController? developerTools;
    private bool developerReady;
#if !WINDOWS
    private static long nextDeveloperCallbackToken;
#endif
    private static readonly ConcurrentDictionary<IntPtr, WeakReference<NativeWebView>> DeveloperCallbacks = new();
    private IntPtr developerCallbackToken = IntPtr.Zero;
    public IDeveloperToolsController DeveloperTools => developerTools ??= new DeveloperToolsController(this);
    internal bool DeveloperViewReady => native != IntPtr.Zero &&
#if WINDOWS
        panel?.Browser.IsReady == true;
#else
        true;
#endif
    internal event Action<string>? RemoteDebuggingMessageReceived;

    private void PrepareDeveloperProfile()
    {
        var proxy = developerTools?.Proxy ?? new DeveloperProxyOptions("system", null);
#if WINDOWS
        WindowsBrowserProfile.ApplyProxy(proxy.Mode, proxy.Endpoint);
#else
        CheckInitialProxy(MacSetInitialProxy(proxy.Mode, proxy.Endpoint));
#endif
    }

    private void InitializeDeveloperTools()
    {
#if WINDOWS
        if (panel is not null)
        {
            panel.Browser.RemoteDebuggingMessageReceived += OnDeveloperMessage;
            panel.NavigationChanged += OnDeveloperNavigationChanged;
        }
#else
        developerCallbackToken = new IntPtr(Interlocked.Increment(ref nextDeveloperCallbackToken));
        DeveloperCallbacks[developerCallbackToken] = new WeakReference<NativeWebView>(this);
        MacSetRemoteDebuggingCallback(native, MacDeveloperMessage, developerCallbackToken);
#endif
        NotifyDeveloperReady();
    }

    private void NotifyDeveloperReady()
    {
        if (developerReady || !DeveloperViewReady) return;
        developerReady = true;
        developerTools?.OnNativeReady();
    }

#if WINDOWS
    private void OnDeveloperNavigationChanged(NavigationState state)
    {
        var created = generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (created != generation || native == IntPtr.Zero) return;
            if (DeveloperViewReady) NotifyDeveloperReady();
            else if (state.Error is { } error) developerTools?.OnNativeInitializationFailed(error.Message);
        });
    }
#endif

    private void ReleaseDeveloperTools()
    {
        developerReady = false;
        if (developerCallbackToken != IntPtr.Zero)
        {
            DeveloperCallbacks.TryRemove(developerCallbackToken, out _);
            developerCallbackToken = IntPtr.Zero;
        }
#if WINDOWS
        if (panel is not null)
        {
            panel.Browser.RemoteDebuggingMessageReceived -= OnDeveloperMessage;
            panel.NavigationChanged -= OnDeveloperNavigationChanged;
        }
#else
        if (native != IntPtr.Zero) MacSetRemoteDebuggingCallback(native, null, IntPtr.Zero);
#endif
        developerTools?.Dispose();
    }

    private void OnDeveloperMessage(string message)
    {
        var created = generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (created == generation && native != IntPtr.Zero)
                RemoteDebuggingMessageReceived?.Invoke(message);
        });
    }

    internal void ApplyDeveloperProxy(DeveloperProxyOptions proxy)
    {
#if WINDOWS
        WindowsBrowserProfile.ApplyProxy(proxy.Mode, proxy.Endpoint);
#else
        if (native == IntPtr.Zero) CheckInitialProxy(MacSetInitialProxy(proxy.Mode, proxy.Endpoint));
        else CheckDeveloperResult(MacSetProxy(native, proxy.Mode, proxy.Endpoint));
#endif
    }

    internal DeveloperServiceStatus SetNativeRemoteInspection(bool enabled, DeveloperListenOptions options)
    {
        if (native == IntPtr.Zero) return new(false);
#if WINDOWS
        panel!.Browser.StopRemoteInspection();
        if (enabled) panel.Browser.StartRemoteInspection(options.BindAddress, options.Port);
        var state = panel!.Browser.RemoteInspection;
        return new(state.Enabled, state.Enabled ? $"{state.Address}:{state.Port}" : null);
#else
        CheckDeveloperResult(MacSetRemoteInspection(native, enabled ? 1 : 0));
        return new(MacRemoteInspectionEnabled(native) != 0);
#endif
    }

    internal Task SetRemoteDebuggingScriptAsync(string? script)
    {
        if (native == IntPtr.Zero) return Task.CompletedTask;
#if WINDOWS
        return panel!.Browser.SetRemoteDebuggingScriptAsync(script);
#else
        CheckDeveloperResult(MacSetRemoteDebuggingScript(native, script));
        return Task.CompletedTask;
#endif
    }

    internal Task EvaluateRemoteDebuggingScriptAsync(string script)
    {
        if (native == IntPtr.Zero) return Task.CompletedTask;
#if WINDOWS
        return panel!.Browser.EvaluateRemoteDebuggingScriptAsync(script);
#else
        CheckDeveloperResult(MacEvaluateRemoteDebuggingScript(native, script));
        return Task.CompletedTask;
#endif
    }

    internal Task RefreshRemoteDebuggingScriptAsync()
    {
        if (!DeveloperViewReady) return Task.CompletedTask;
#if WINDOWS
        return panel!.Browser.RefreshRemoteDebuggingScriptAsync();
#else
        CheckDeveloperResult(MacRefreshRemoteDebuggingScript(native));
        return Task.CompletedTask;
#endif
    }

    private void CheckDeveloperResult(int result)
    {
        if (result != 0) return;
        var pointer = MacDeveloperError(native);
        try { throw new InvalidOperationException(Marshal.PtrToStringUTF8(pointer) ?? "WebKit developer operation failed."); }
        finally { if (pointer != IntPtr.Zero) MacFree(pointer); }
    }

    private static void CheckInitialProxy(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        try { throw new InvalidOperationException(Marshal.PtrToStringUTF8(error)); }
        finally { MacFree(error); }
    }

    private static readonly NavigationCallback MacDeveloperMessage = OnMacDeveloperMessage;
    private static void OnMacDeveloperMessage(IntPtr context, IntPtr json)
    {
        try
        {
            if (context != IntPtr.Zero && DeveloperCallbacks.TryGetValue(context, out var reference) && reference.TryGetTarget(out var view))
                view.OnDeveloperMessage(Marshal.PtrToStringUTF8(json) ?? "{}");
        }
        catch (Exception)
        {
            // Managed callback failures must never unwind through WebKit's C callback.
        }
    }

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_inspector_visible")]
    private static extern int MacSetInspectorVisible(IntPtr view, int visible);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_inspector_visible")]
    private static extern int MacInspectorVisible(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_remote_inspection")]
    private static extern int MacSetRemoteInspection(IntPtr view, int enabled);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_remote_inspection_enabled")]
    private static extern int MacRemoteInspectionEnabled(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_initial_proxy")]
    private static extern IntPtr MacSetInitialProxy([MarshalAs(UnmanagedType.LPUTF8Str)] string mode, [MarshalAs(UnmanagedType.LPUTF8Str)] string? endpoint);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_proxy")]
    private static extern int MacSetProxy(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string mode, [MarshalAs(UnmanagedType.LPUTF8Str)] string? endpoint);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_developer_error")]
    private static extern IntPtr MacDeveloperError(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_remote_debugging_callback")]
    private static extern void MacSetRemoteDebuggingCallback(IntPtr view, NavigationCallback? callback, IntPtr context);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_set_remote_debugging_script")]
    private static extern int MacSetRemoteDebuggingScript(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string? script);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_evaluate_remote_debugging_script")]
    private static extern int MacEvaluateRemoteDebuggingScript(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string script);

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_refresh_remote_debugging_script")]
    private static extern int MacRefreshRemoteDebuggingScript(IntPtr view);
}
