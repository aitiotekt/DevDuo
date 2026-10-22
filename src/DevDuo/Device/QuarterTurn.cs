namespace DevDuo;

// Edge coordinates, before preview clipping. Inverse input uses pixel centers.
public readonly record struct QuarterTurn(double Width, double Height, int Degrees)
{
    public (double X, double Y) Forward(double x, double y) => Degrees switch
    {
        0 => (x, y), 90 => (Height - y, x), 180 => (Width - x, Height - y), 270 => (y, Width - x),
        _ => throw new ArgumentOutOfRangeException(nameof(Degrees))
    };
    public (double X, double Y) Inverse(double x, double y) => Degrees switch
    {
        0 => (x, y), 90 => (y, Height - x), 180 => (Width - x, Height - y), 270 => (Width - y, x),
        _ => throw new ArgumentOutOfRangeException(nameof(Degrees))
    };
}
