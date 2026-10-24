using System.Text.Json;
using Avalonia.Threading;

namespace DevDuo;

// A controller belongs to one native container; service workers never own a native handle.
internal sealed class DeveloperToolsController : IDeveloperToolsController, IDisposable
{
    private readonly NativeWebView view;
    private CdpDebuggingService? cdp;
    private bool disposed;
    private bool nativeRequested;
    private bool cdpRequested;
    private int cdpRevision;
    private DeveloperListenOptions nativeOptions = new(false, 9223);
    private DeveloperListenOptions cdpOptions = new(false, 9222);
    private string? initializationError;
    private bool navigationLoading;
    internal DeveloperProxyOptions Proxy { get; private set; } = new("system", null);
    public event Action? Changed;
    public event Action? CdpTargetChanged;
    public DeveloperServiceStatus NativeRemoteStatus { get; private set; } = new(false);
    public DeveloperServiceStatus CdpStatus { get; private set; } = new(false);
    public Uri? CdpFrontendUrl => cdp?.FrontendUri;

    internal DeveloperToolsController(NativeWebView view)
    {
        this.view = view;
        navigationLoading = view.Navigation.IsLoading;
        view.RemoteDebuggingMessageReceived += OnPageMessage;
        view.NavigationChanged += OnNavigationChanged;
    }

    public void ApplyProxy(DeveloperProxyOptions options)
    {
        if (disposed) return;
        if (options.Mode is not ("system" or "direct" or "custom") ||
            options.Mode == "custom" && !HostSettings.TryProxyEndpoint(options.Endpoint, out _))
            throw new ArgumentException("Proxy requires system, direct or a valid unauthenticated custom endpoint.", nameof(options));
        // Native validation happens before persistence or changing the current setting.
        view.ApplyDeveloperProxy(options);
        Proxy = options;
        Changed?.Invoke();
    }

    public void ApplyNativeRemote(bool enabled, DeveloperListenOptions options)
    {
        if (disposed) return;
        ValidateListenOptions(options);
        nativeRequested = enabled;
        nativeOptions = options;
        if (!view.DeveloperViewReady)
        {
            NativeRemoteStatus = new(false, Error: enabled ? initializationError : null, Starting: enabled && initializationError is null);
            Changed?.Invoke();
            return;
        }
        try { NativeRemoteStatus = view.SetNativeRemoteInspection(enabled, options); }
        catch (Exception error)
        {
            nativeRequested = false;
            NativeRemoteStatus = new(false, Error: error.Message);
        }
        Changed?.Invoke();
    }

    public void ApplyCdp(bool enabled, DeveloperListenOptions options)
    {
        if (disposed) return;
        ValidateListenOptions(options);
        if (enabled && !HostSettings.IsValidBearerToken(options.BearerToken ?? ""))
            throw new ArgumentException(AppLocalization.Current.Text("Developer.InvalidCdpBearerToken"), nameof(options));
        if (enabled && options.AllowLan && string.IsNullOrEmpty(options.BearerToken))
            throw new ArgumentException(AppLocalization.Current.Text("Developer.CdpLanTokenRequired"), nameof(options));
        cdpRequested = enabled;
        cdpOptions = options;
        if (!view.DeveloperViewReady)
        {
            CdpStatus = new(false, Error: enabled ? initializationError : null, Starting: enabled && initializationError is null);
            Changed?.Invoke();
            return;
        }
        var revision = ++cdpRevision;
        var previous = cdp;
        cdp = null;
        previous?.Stop();
        if (previous is not null) _ = previous.DisposeAsync();
        CdpStatus = new(false, Starting: enabled);
        Changed?.Invoke();
        _ = ChangeCdpAsync(enabled, options, revision);
    }

    private async Task ChangeCdpAsync(bool enabled, DeveloperListenOptions options, int revision)
    {
        CdpDebuggingService? next = null;
        try
        {
            if (disposed || revision != cdpRevision) return;
            if (!enabled)
            {
                await view.SetRemoteDebuggingScriptAsync(null);
                return;
            }
            var script = CdpDebuggingService.CreateInjectionScript();
            next = new CdpDebuggingService(command => DispatchCommandAsync(command, revision),
                captureScreenshot: (parameters, token) => CaptureScreenshotAsync(parameters, token, revision));
            next.TargetChanged += () => Dispatcher.UIThread.Post(() =>
            {
                if (disposed || revision != cdpRevision) return;
                if (cdp?.HasTarget == true)
                {
                    CdpStatus = CdpStatus with { Error = null };
                    CdpTargetChanged?.Invoke();
                }
                Changed?.Invoke();
            });
            next.Error += error => Dispatcher.UIThread.Post(() =>
            {
                if (disposed || revision != cdpRevision) return;
                CdpStatus = CdpStatus with { Error = error };
                Changed?.Invoke();
            });
            await next.StartAsync(options.AllowLan, options.Port, options.BearerToken);
            if (disposed || revision != cdpRevision) { next.Stop(); await next.DisposeAsync(); return; }
            cdp = next;
            await view.SetRemoteDebuggingScriptAsync(script);
            if (disposed || revision != cdpRevision) { next.Stop(); return; }
            CdpStatus = new(true, string.Join(Environment.NewLine, next.AccessUris.Select(uri => uri.AbsoluteUri)), Error: CdpStatus.Error);
        }
        catch (Exception error)
        {
            next?.Stop();
            if (next is not null) await next.DisposeAsync();
            if (disposed || revision != cdpRevision) return;
            cdp = null;
            cdpRequested = false;
            var detail = error.Message;
            try { await view.SetRemoteDebuggingScriptAsync(null); }
            catch (Exception cleanupError) { detail += Environment.NewLine + cleanupError.Message; }
            if (disposed || revision != cdpRevision) return;
            CdpStatus = new(false, Error: detail);
        }
        finally
        {
            if (!disposed && revision == cdpRevision) Changed?.Invoke();
        }
    }

    private async Task DispatchCommandAsync(string command, int revision)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (disposed || revision != cdpRevision || !view.DeveloperViewReady) return;
            await view.EvaluateRemoteDebuggingScriptAsync(
                "window.__devduoRemoteDebugging?.dispatch(" + JsonSerializer.Serialize(command) + ");");
        });
    }

    private async Task<object> CaptureScreenshotAsync(JsonElement parameters, CancellationToken token, int revision)
    {
        var request = ScreenshotRequest.ParseCdp(parameters);
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (disposed || revision != cdpRevision || !view.DeveloperViewReady)
                throw new ControlApiException(409, "The CDP page is no longer available.");
            var image = await view.CaptureScreenshotAsync(request, cancellationToken: token);
            if (disposed || revision != cdpRevision) throw new ControlApiException(409, "The CDP target changed during capture.");
            return (object)new { data = Convert.ToBase64String(image.Data) };
        });
    }

    private void OnPageMessage(string message) => cdp?.HandlePageMessage(message);

    private void OnNavigationChanged(NavigationState state)
    {
        if (disposed) return;
        if (state.IsLoading)
        {
            if (!navigationLoading && cdp is not null)
            {
                cdp.InvalidateTarget();
                Changed?.Invoke();
            }
            navigationLoading = true;
            return;
        }
        if (!navigationLoading) return;
        navigationLoading = false;
        if (cdpRequested && cdp is not null && view.DeveloperViewReady)
            _ = RefreshCdpDocumentAsync(cdpRevision);
    }

    private async Task RefreshCdpDocumentAsync(int revision)
    {
        try
        {
            if (disposed || revision != cdpRevision) return;
            // BFCache restores may contain no bridge, or a bridge with an older native generation.
            // Reevaluate the installed source without replacing the document-start user script.
            await view.RefreshRemoteDebuggingScriptAsync();
        }
        catch (Exception error)
        {
            if (disposed || revision != cdpRevision) return;
            CdpStatus = CdpStatus with { Error = error.Message };
            Changed?.Invoke();
        }
    }

    internal void OnNativeReady()
    {
        initializationError = null;
        if (nativeRequested) ApplyNativeRemote(true, nativeOptions);
        if (cdpRequested) ApplyCdp(true, cdpOptions);
    }

    internal void OnNativeInitializationFailed(string error)
    {
        if (disposed) return;
        initializationError = error;
        if (nativeRequested) NativeRemoteStatus = new(false, Error: error);
        if (cdpRequested) CdpStatus = new(false, Error: error);
        nativeRequested = cdpRequested = false;
        Changed?.Invoke();
    }

    private static void ValidateListenOptions(DeveloperListenOptions options)
    {
        if (options.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), "Listener ports must be from 1 to 65535.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cdpRevision++;
        view.RemoteDebuggingMessageReceived -= OnPageMessage;
        view.NavigationChanged -= OnNavigationChanged;
        cdp?.Stop();
        if (cdp is not null) _ = cdp.DisposeAsync();
        cdp = null;
        try
        {
            if (NativeRemoteStatus.Enabled && view.DeveloperViewReady)
                view.SetNativeRemoteInspection(false, nativeOptions);
            NativeRemoteStatus = new(false);
        }
        catch (Exception error)
        {
            // Native control destruction must continue even if stopping its inspector reports a failure.
            NativeRemoteStatus = new(false, Error: error.Message);
        }
        CdpStatus = new(false);
    }
}
