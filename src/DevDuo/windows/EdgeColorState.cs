namespace DevDuo;

// A missing candidate and an unreadable candidate have different lifetimes.
internal sealed class EdgeColorState
{
    internal uint? Color { get; private set; }
    internal string Reason { get; private set; } = "none";
    private long? uncertainSince;
    private bool absent;

    internal void BeginRequest(long now)
    {
        // A stalled IPC must not keep a previous popup's fill indefinitely.
        // Preserve absence confirmation across the follow-up request.
        if (Color is not null) uncertainSince ??= now;
    }

    internal void Reset()
    {
        Color = null;
        uncertainSince = null;
        absent = false;
        Reason = "none";
    }

    internal bool Observe(uint state, uint rgb, long now)
    {
        if (state == 1)
        {
            Color = rgb;
            uncertainSince = null;
            absent = false;
            Reason = "accepted";
            return true;
        }
        if (state == 0 && (absent || Color is null))
        {
            Reset();
            return true;
        }
        absent = state == 0;
        uncertainSince ??= now;
        Reason = state == 0 ? "confirming-absence" : state == 2 ? "uncertain" : "unavailable";
        Expire(now);
        return false;
    }

    internal bool Expire(long now)
    {
        if (uncertainSince is not { } since || now - since < 600 || Color is null) return false;
        Color = null;
        Reason = "expired";
        return true;
    }
}
