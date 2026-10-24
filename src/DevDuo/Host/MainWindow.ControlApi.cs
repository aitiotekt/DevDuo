using System.Text.Json;
using Avalonia.Threading;
using Avalonia.Input.Platform;

namespace DevDuo;

public sealed partial class MainWindow
{
    private readonly ControlApiService controlApi;
    private bool controlApiChanging, controlApiClosed;
    private string? controlApiError;

    private void WireControlApi()
    {
        panel.ControlApi.IsCheckedChanged += async (_, _) =>
        {
            if (applyingSettings || updatingDeveloperControls || controlApiChanging) return;
            await ChangeControlApiAsync(panel.ControlApi.IsChecked == true, panel.ControlApi.IsChecked == true);
        };
        panel.ApplyControlApi.Click += async (_, _) => await ChangeControlApiAsync(controlApi.IsRunning);
        panel.CopyControlApiAddress.Click += async (_, _) =>
        {
            if (controlApi.BaseUri is not { } address || Clipboard is not { } clipboard) return;
            try { await clipboard.SetTextAsync(new Uri(address, "api/openapi").AbsoluteUri); }
            catch (Exception error) { SetDeveloperMessage("Developer.ActionError", error.Message); }
        };
    }

    private async Task ChangeControlApiAsync(bool enabled, bool saveConfiguration = true)
    {
        if (controlApiChanging || controlApiClosed) return;
        var port = host.ControlApiPort;
        if (saveConfiguration && (!int.TryParse(panel.ControlApiPort.Text, out port) || port is < 1 or > 65535))
        {
            SetDeveloperMessage("Developer.InvalidControlApiPort");
            RefreshControlApi();
            return;
        }
        var token = saveConfiguration ? panel.ControlApiBearerToken.Text?.Trim() ?? "" : host.ControlApiBearerToken;
        if (!HostSettings.IsValidBearerToken(token))
        {
            SetDeveloperMessage("Developer.InvalidControlApiToken");
            RefreshControlApi();
            return;
        }
        host.ControlApiPort = port;
        host.ControlApiBearerToken = token;
        QueueSave();
        controlApiChanging = true;
        controlApiError = null;
        RefreshControlApi();
        try
        {
            await controlApi.StopAsync();
            if (enabled && !controlApiClosed) await controlApi.StartAsync(port, token);
        }
        catch (Exception error) { controlApiError = error.Message; }
        finally
        {
            controlApiChanging = false;
            if (!controlApiClosed) RefreshControlApi();
        }
    }

    private async Task StopControlApiAsync()
    {
        await controlApi.StopAsync();
        controlApiError = null;
        if (!controlApiClosed) RefreshControlApi();
    }

    private void RefreshControlApi()
    {
        // Preserve the other developer controls' reentrancy guard during language refresh.
        var previous = updatingDeveloperControls;
        updatingDeveloperControls = true;
        try
        {
            panel.ControlApi.IsChecked = controlApi.IsRunning || controlApiChanging;
            panel.ControlApi.IsEnabled = panel.ApplyControlApi.IsEnabled = !controlApiChanging;
            panel.ControlApiStatus.Text = controlApiError is { } error
                ? language.Format("Developer.ServiceError", error)
                : language.Text(controlApiChanging ? "Developer.Starting" : controlApi.IsRunning ? "Developer.Running" : "Developer.Stopped");
            panel.ControlApiAddress.Text = controlApi.BaseUri is { } address ? new Uri(address, "api/openapi").AbsoluteUri : "";
            panel.ControlApiAddress.IsVisible = controlApi.IsRunning;
            panel.CopyControlApiAddress.IsEnabled = controlApi.IsRunning;
        }
        finally { updatingDeveloperControls = previous; }
    }

    private async Task<object> DispatchControlApiAsync(ControlApiRequest request, CancellationToken cancellationToken)
    {
        // The priority+cancellation overload returns the delegate's Task as its
        // result. Await both stages so Kestrel receives an image, never a Task.
        var operation = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (controlApiClosed || !ready) throw new ControlApiException(503, "The application window is not ready.");
            if (request.Path == "/api/screenshot")
                return CaptureControlScreenshotAsync(ScreenshotRequest.Parse(request.Body), cancellationToken);
            return Task.FromResult(HandleControlApi(request));
        }, DispatcherPriority.Normal, cancellationToken);
        return await operation;
    }

    private object HandleControlApi(ControlApiRequest request)
    {
        switch (request.Path)
        {
            case "/api/state": break;
            case "/api/settings": ApplyControlSettings(ControlSettings.Merge(ReadControlSettings(), request.Body)); break;
            case "/api/navigation":
                var navigation = request.Body.Deserialize<ControlNavigationRequest>(ControlApiJson.Options)!;
                if (navigation.Action == "navigate")
                {
                    if (!CalibrationUrl.TryNormalize(navigation.Url, out var address))
                        throw new ArgumentException("navigate requires an http or https URL.");
                    Navigate(address);
                }
                else
                {
                    if (navigation.Url is not null) throw new ArgumentException("url applies only to navigate.");
                    switch (navigation.Action)
                    {
                        case "back": RequireControlView().GoBack(); break;
                        case "forward": RequireControlView().GoForward(); break;
                        case "reload": RunCommand(HostCommand.Reload); break;
                        case "stop": RequireControlView().Stop(); break;
                        case "home": ShowBuiltinPage(BuiltinPageKind.Welcome); break;
                        case "color-demo": ShowBuiltinPage(BuiltinPageKind.ColorLaboratory); break;
                        case "calibration": Navigate(new Uri(CalibrationUrl.DefaultAddress)); break;
                        default: throw new ArgumentException("Unknown navigation action.");
                    }
                }
                break;
            case "/api/inspector":
                var inspector = request.Body.Deserialize<ControlInspectorRequest>(ControlApiJson.Options)!;
                var view = RequireControlView();
                if (!view.CanInspect) throw new ControlApiException(409, "Native Inspector is not ready.");
                view.SetInspectorVisible(inspector.Visible);
                UpdateInspectorButton();
                break;
            case "/api/proxy":
                var proxy = request.Body.Deserialize<ControlProxyRequest>(ControlApiJson.Options)!;
                if (proxy.Mode is not ("system" or "direct" or "custom") ||
                    proxy.Mode == "custom" && !HostSettings.TryProxyEndpoint(proxy.Endpoint, out _) ||
                    proxy.Mode != "custom" && proxy.Endpoint is not null)
                    throw new ArgumentException("Use system/direct without endpoint, or custom with an unauthenticated proxy endpoint.");
                if (OperatingSystem.IsMacOS() && proxy.Mode == "direct")
                    throw new ControlApiException(409, "Direct proxy bypass is unavailable on macOS.");
                if (developerTools is null) throw new ControlApiException(409, "Native container is unavailable.");
                developerTools.ApplyProxy(new DeveloperProxyOptions(proxy.Mode, proxy.Endpoint));
                host.ProxyMode = proxy.Mode;
                host.ProxyEndpoint = proxy.Endpoint ?? "";
                ApplyHostSettingsToControls();
                QueueSave();
                break;
            default: throw new ControlApiException(404, "Unknown API resource.");
        }
        return ReadControlState();
    }

    private NativeWebView RequireControlView() => web is { DeveloperViewReady: true } view
        ? view : throw new ControlApiException(409, "Native container is not ready.");

    private ControlSettings ReadControlSettings()
    {
        CollectHostSettings();
        return new ControlSettings
        {
            Fold = host.Fold, BodyAngle = rotation * 90, ContainerMode = host.ContainerMode,
            ShowTabStrip = host.ShowTabStrip, PreviewScale = host.PreviewScale, InteractionMode = host.InteractionMode, DeviceOutline = host.DeviceOutline,
            Theme = host.Theme, Language = language.Preference, TintAlpha = host.TintAlpha,
            GlassStrength = host.GlassStrength, ShadowOpacity = host.ShadowOpacity, CornerRadius = host.CornerRadius
        };
    }

    private object ReadControlState()
    {
        using var telemetry = JsonDocument.Parse(web?.ReadState() ?? "{}");
        return new
        {
            settings = ReadControlSettings(),
            navigation = web?.Navigation ?? NavigationState.Empty,
            scene = new
            {
                id = CurrentScene().Id, CurrentPose().BodyInCanvasDegrees,
                CurrentPose().ContentInBodyDegrees, CurrentPose().SceneInCanvasDegrees
            },
            previewScale,
            inspectorVisible = web?.InspectorVisible == true,
            proxy = new { mode = host.ProxyMode, endpoint = host.ProxyEndpoint },
            cdpAddress = developerTools?.CdpStatus.Address,
            nativeReady = web?.DeveloperViewReady == true,
            error = previewError?.Message,
            telemetry = telemetry.RootElement.Clone()
        };
    }

    private void ApplyControlSettings(ControlSettings next)
    {
        var rebuild = next.Fold != host.Fold || next.ContainerMode != host.ContainerMode;
        var angleChanged = next.BodyAngle != rotation * 90;
        host.Fold = next.Fold;
        host.ContainerMode = next.ContainerMode;
        host.ShowTabStrip = next.ShowTabStrip;
        host.PreviewScale = next.PreviewScale;
        host.InteractionMode = next.InteractionMode;
        host.DeviceOutline = next.DeviceOutline;
        host.Theme = next.Theme;
        host.TintAlpha = next.TintAlpha;
        host.GlassStrength = next.GlassStrength;
        host.ShadowOpacity = next.ShadowOpacity;
        host.CornerRadius = next.CornerRadius;
        rotation = next.BodyAngle / 90;
        ApplyHostSettingsToControls();
        language.Select(next.Language);
        RefreshLanguage();
        ApplyTheme();
        if (rebuild || web is null) Rebuild("Status.Rebuilt");
        else
        {
            if (angleChanged) web.UpdateScene(CurrentProfile(), CurrentOptions());
            Configure();
            ApplyPreviewSize();
            PositionNativeView();
            RelabelCalibrationPage();
        }
        QueueSave();
        UpdateTelemetry();
    }
}
