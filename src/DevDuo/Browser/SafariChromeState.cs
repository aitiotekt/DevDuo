namespace DevDuo;

// Native input and observed document motion are separate. Neither script-driven
// scrolling nor viewport/document clamping establishes a user scroll gesture.
internal sealed class SafariChromeState
{
    private int direction;
    private long lastInput;
    private double distance;
    internal string InputSource { get; private set; } = "none";
    internal long InputSequence { get; private set; }

    internal void RecordInput(int sign, long now, string source)
    {
        if (sign == 0) return;
        sign = sign > 0 ? 1 : -1;
        if (direction != sign || now - lastInput > 350) distance = 0;
        direction = sign;
        lastInput = now;
        InputSource = source;
        InputSequence++;
    }

    internal void Reset()
    {
        direction = 0;
        distance = 0;
        InputSource = "none";
    }

    internal bool Observe(bool compact, double scrollY, double deltaY, bool viewportChanged, long now, double threshold)
    {
        if (viewportChanged || direction == 0 || deltaY == 0) return compact;
        if (now - lastInput > 350) { Reset(); return compact; }
        if (System.Math.Sign(deltaY) != direction) return compact;
        distance += System.Math.Abs(deltaY);
        if (direction < 0 && (scrollY <= 2 || distance > 8)) return false;
        if (direction > 0 && distance > threshold) return true;
        return compact;
    }
}
