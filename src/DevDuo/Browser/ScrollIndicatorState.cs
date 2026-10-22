namespace DevDuo;

// These presentation constants are engineering approximations, not measured iOS timings.
internal sealed class ScrollIndicatorState
{
    internal const int HoldMilliseconds = 700, FadeMilliseconds = 300;
    internal const double MaximumOpacity = .55;
    internal long InputSequence { get; private set; }
    internal ulong ConfirmedMotionSequence { get; private set; }
    internal double Position { get; private set; }
    internal double Range { get; private set; }
    internal bool CssSuppressed { get; private set; }
    private long inputAt = long.MinValue, motionAt = long.MinValue;
    private ulong inputBaseline;
    private bool armed, following, disposed;

    internal void RecordInput(long now)
    {
        if (disposed) return;
        InputSequence++;
        inputAt = now;
        inputBaseline = ConfirmedMotionSequence;
        armed = true;
    }

    internal void Observe(ulong userMotion, double position, double range, bool suppressed, long now)
    {
        if (disposed) return;
        Position = Math.Clamp(position, 0, Math.Max(0, range));
        Range = Math.Max(0, range);
        CssSuppressed = suppressed;
        var changed = userMotion > ConfirmedMotionSequence;
        ConfirmedMotionSequence = Math.Max(ConfirmedMotionSequence, userMotion);
        if (following && now - motionAt >= HoldMilliseconds + FadeMilliseconds) following = false;
        if (suppressed || Range <= 0)
        {
            armed = following = false;
            motionAt = long.MinValue;
            return;
        }
        // Input alone never shows a thumb. The engine must confirm actual root
        // vertical User motion; nearby programmatic or horizontal motion cannot.
        if (changed && ((armed && now - inputAt <= 600 && userMotion > inputBaseline) || following))
        {
            motionAt = now;
            following = true;
            armed = false;
        }
        if (armed && now - inputAt > 600) armed = false;
    }

    internal double Opacity(long now)
    {
        if (disposed || CssSuppressed || Range <= 0 || motionAt == long.MinValue) return 0;
        var elapsed = now - motionAt;
        if (elapsed <= HoldMilliseconds) return MaximumOpacity;
        return MaximumOpacity * Math.Clamp(1 - (elapsed - HoldMilliseconds) / (double)FadeMilliseconds, 0, 1);
    }

    internal void Reset(bool preserveMotionBaseline = false)
    {
        armed = following = false;
        inputAt = motionAt = long.MinValue;
        inputBaseline = preserveMotionBaseline ? ConfirmedMotionSequence : 0;
        if (!preserveMotionBaseline) ConfirmedMotionSequence = 0;
        Position = Range = 0;
        CssSuppressed = false;
    }

    internal void Dispose() { Reset(); disposed = true; }
}
