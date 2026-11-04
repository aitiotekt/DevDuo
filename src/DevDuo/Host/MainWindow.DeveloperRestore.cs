namespace DevDuo;

public sealed partial class MainWindow
{
    private string? nativeRestoreError, cdpRestoreError;

    // Called once per window. Container rebuilding only carries running services,
    // so a failed startup cannot become an unsolicited retry after a rotation.
    private void RestoreDeveloperServices()
    {
        if (host.NativeRemoteEnabled) TryRestoreNativeRemote();
        if (host.CdpEnabled) TryRestoreCdp();
        if (host.ControlApiEnabled)
        {
            controlApiError = InvalidSavedListener(PreferenceStore.ControlListenerFields);
            if (controlApiError is null) _ = ChangeControlApiAsync(true, false);
            else RefreshControlApi();
        }
        RefreshDeveloperTools();
    }

    private void TryRestoreNativeRemote()
    {
        nativeRestoreError = InvalidSavedListener(PreferenceStore.NativeListenerFields);
        if (nativeRestoreError is not null) return;
        try { developerTools?.ApplyNativeRemote(true, new(host.DeveloperAllowLan, host.NativeRemotePort)); }
        catch (Exception error) { nativeRestoreError = error.Message; }
    }

    private void TryRestoreCdp()
    {
        cdpRestoreError = InvalidSavedListener(PreferenceStore.CdpListenerFields);
        if (cdpRestoreError is not null) return;
        try { developerTools?.ApplyCdp(true, new(host.DeveloperAllowLan, host.CdpPort, host.CdpBearerToken)); }
        catch (Exception error) { cdpRestoreError = error.Message; }
    }

    private string? InvalidSavedListener(string[] fields)
    {
        var invalid = preferences.InvalidListenerFields.Intersect(fields).ToArray();
        return invalid.Length == 0 ? null : language.Format("Developer.InvalidSavedSettings", string.Join(", ", invalid));
    }
}
