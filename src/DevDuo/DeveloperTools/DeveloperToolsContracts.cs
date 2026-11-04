namespace DevDuo;

public sealed record DeveloperListenOptions(bool AllowLan, int Port, [property: System.Text.Json.Serialization.JsonIgnore] string? BearerToken = null)
{
    public string BindAddress => AllowLan ? "0.0.0.0" : "127.0.0.1";
    public override string ToString() => $"{BindAddress}:{Port}, authentication: {!string.IsNullOrEmpty(BearerToken)}";
}

public sealed record DeveloperProxyOptions(string Mode, string? Endpoint);

public sealed record DeveloperServiceStatus(bool Enabled, string? Address = null, string? Error = null, bool Starting = false);

// Actual service state is separate from the user's persisted startup intent.
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
