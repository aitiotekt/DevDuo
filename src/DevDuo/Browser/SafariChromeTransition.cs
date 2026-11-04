namespace DevDuo;

// Presentation timing only. Endpoints come from measured chrome geometry.
internal sealed class SafariChromeTransition
{
    internal bool Compact { get; private set; }
    internal bool Running { get; private set; }
    internal double Fraction { get; private set; } = 1;
    private long started;
    private int duration;

    internal bool Retarget(bool compact, long now, int milliseconds, bool animate)
    {
        if (Compact == compact) return false;
        Compact = compact;
        started = now;
        duration = Math.Max(0, milliseconds);
        Running = animate && duration > 0;
        Fraction = Running ? 0 : 1;
        return true;
    }

    internal void Advance(long now)
    {
        if (!Running) return;
        var t = Math.Clamp((now - started) / (double)duration, 0, 1);
        // Smoothstep is cubic Bezier (1/3,0,2/3,1), an explicit approximation.
        Fraction = t * t * (3 - 2 * t);
        Running = t < 1;
    }

    internal void Reset(bool compact)
    {
        Compact = compact;
        Running = false;
        Fraction = 1;
    }
}
