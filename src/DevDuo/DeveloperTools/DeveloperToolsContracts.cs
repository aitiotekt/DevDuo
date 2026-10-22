namespace DevDuo;

public sealed record DeveloperListenOptions(bool AllowLan, int Port)
{
    public string BindAddress => AllowLan ? "0.0.0.0" : "127.0.0.1";
}

public sealed record DeveloperProxyOptions(string Mode, string? Endpoint);

public sealed record DeveloperServiceStatus(bool Enabled, string? Address = null, string? Error = null, bool Starting = false);

// Service state is deliberately separate from persisted configuration. A launch never starts a listener.
public interface IDeveloperToolsController
{
    event Action? Changed;
    event Action? CdpTargetChanged;
    DeveloperServiceStatus NativeRemoteStatus { get; }
    DeveloperServiceStatus CdpStatus { get; }
    Uri? CdpFrontendUrl { get; }
    void ApplyNativeRemote(bool enabled, DeveloperListenOptions options);
    void ApplyCdp(bool enabled, DeveloperListenOptions options);
    void ApplyProxy(DeveloperProxyOptions options);
}
