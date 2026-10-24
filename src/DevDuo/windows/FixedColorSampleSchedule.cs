namespace DevDuo;

// Render counts can stay unchanged across document readiness and failed IPC.
// Keep explicit readiness wakes separate from successful render-count caching.
internal sealed class FixedColorSampleSchedule
{
    internal readonly record struct Request(ulong Update, long Wake, int Attempt);
    private Request? latest;
    private long wake;
    private bool accepted;

    internal void Wake() => wake++;

    internal void Reset()
    {
        Wake();
        latest = null;
        accepted = false;
    }

    internal bool TryBegin(ulong update, out Request request)
    {
        var sameInput = latest is { } previous && previous.Update == update && previous.Wake == wake;
        if (sameInput && (accepted || latest!.Value.Attempt >= 3))
        {
            request = default;
            return false;
        }
        request = new(update, wake, sameInput ? latest!.Value.Attempt + 1 : 1);
        latest = request;
        accepted = false;
        return true;
    }

    internal void Complete(Request request, bool available)
    {
        if (latest == request) accepted = available;
    }
}
