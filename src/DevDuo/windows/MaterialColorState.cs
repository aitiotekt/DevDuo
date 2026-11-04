namespace DevDuo;

// Stabilizes a color scalar, never the live backdrop or the page's input stream.
internal sealed class MaterialColorState
{
    internal uint? Raw { get; private set; }
    internal uint? Target { get; private set; }
    internal uint Display { get; private set; }
    internal long AcceptedCount { get; private set; }
    internal bool Pending => candidate.HasValue;
    internal bool Animating { get; private set; }
    private uint? candidate;
    private long candidateSince, animationSince;
    private uint animationFrom;

    internal void Reset(uint color)
    {
        Raw = Target = candidate = null;
        Display = animationFrom = color;
        Animating = false;
    }

    private static bool Near(uint a, uint b, int threshold) =>
        new[] { 0, 8, 16 }.All(shift => Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)) <= threshold);

    internal bool Observe(uint color, long now)
    {
        Raw = color;
        if (Target is { } target && Near(color, target, 3))
        {
            candidate = null;
            return false;
        }
        if (candidate is not { } previous)
        {
            candidate = color;
            candidateSince = now;
            return false;
        }
        candidate = color;
        if (!Near(previous, color, 6) && now - candidateSince < 400) return false;
        Accept(color, now);
        return true;
    }

    internal bool AcceptPending(long now)
    {
        if (candidate is not { } color || now - candidateSince < 400) return false;
        Accept(color, now);
        return true;
    }

    private void Accept(uint color, long now)
    {
        Advance(now);
        animationFrom = Display;
        animationSince = now;
        Target = color;
        candidate = null;
        Animating = Display != color;
        AcceptedCount++;
    }

    internal bool Advance(long now)
    {
        if (!Animating || Target is not { } target) return false;
        var fraction = Math.Clamp((now - animationSince) / 120.0, 0, 1);
        uint result = 0;
        foreach (var shift in new[] { 0, 8, 16 })
        {
            var from = (int)((animationFrom >> shift) & 255);
            var to = (int)((target >> shift) & 255);
            result |= (uint)Math.Round(from + (to - from) * fraction) << shift;
        }
        var changed = Display != result;
        Display = result;
        Animating = fraction < 1;
        return changed;
    }
}
