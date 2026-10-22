namespace DevDuo;

public sealed record RemoteInspectionState(bool Enabled, string Transport, string? Address, int? Port)
{
    public static RemoteInspectionState Disabled { get; } = new(false, "none", null, null);
}
