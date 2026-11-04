#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HostFrameEdges
    {
        public ulong FrameId, DocumentEpoch;
        public uint States, Reasons, ColorMask, Top, Right, Bottom, Left, Root;
        public float InsetTop, InsetRight, InsetBottom, InsetLeft;
        public int FlowLeft, FlowTop, FlowRight, FlowBottom;
    }

    private HostFrameEdges? fixedFrameEdges;
    private long fixedColorRevision;
    internal event Action? FixedContainerColorsChanged;

    private bool InvalidateFixedContainerColors(bool retainColors = false)
    {
        var hadColors = fixedFrameEdges is { ColorMask: not 0 };
        // Geometry changes keep the actually painted frame until its replacement
        // arrives. Navigation invalidates diagnostics; the engine owns painting.
        if (!retainColors) fixedFrameEdges = null;
        return hadColors;
    }

    private Task RefreshFixedContainerColorsAsync()
    {
        if (!IsReady || !committed || !WKViewCopyHostFrameEdges(view, out var next)) return Task.CompletedTask;
        if (fixedFrameEdges is { } previous && previous.Equals(next)) return Task.CompletedTask;
        fixedFrameEdges = next;
        fixedColorRevision++;
        FixedContainerColorsChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Color? FixedColor(int side)
    {
        if (fixedFrameEdges is not { } frame || (frame.ColorMask & (1u << side)) == 0) return null;
        var rgb = side switch { 0 => frame.Top, 1 => frame.Right, 2 => frame.Bottom, _ => frame.Left };
        return Color.FromArgb((int)(rgb >> 16) & 255, (int)(rgb >> 8) & 255, (int)rgb & 255);
    }

    internal uint? PaintedSideContinuationColor => geometry is { } current && (current.Left > 0 || current.Right > 0) &&
        FixedColor(current.Left > 0 ? 3 : 1) is { } color ? (uint)color.ToArgb() & 0xffffff : null;

    private object FixedContainerColorDiagnostics()
    {
        int[]? Rgb(int side) => FixedColor(side) is { } color ? [color.R, color.G, color.B] : null;
        string Status(int side) => fixedFrameEdges is { } frame ? ((frame.States >> (side * 2)) & 3) switch
        {
            0 => "none", 1 => "accepted", 2 => "multiple-or-translucent", _ => "unavailable"
        } : "unavailable";
        string Source(int side) => fixedFrameEdges is { } frame ? ((frame.Reasons >> (side * 4)) & 15) switch
        {
            0 => "no-coverage", 2 => "ambiguous-coverage-or-color", 4 => "upstream-fixed-container-color", _ => "unknown"
        } : "frame-unavailable";
        return new
        {
            apiVersion = 2, framePresentationApiVersion = 1, frameId = fixedFrameEdges?.FrameId,
            documentEpoch = fixedFrameEdges?.DocumentEpoch, reasons = fixedFrameEdges?.Reasons,
            edgeSources = Enumerable.Range(0, 4).Select(Source).ToArray(), revision = fixedColorRevision,
            inFlight = false, status = fixedFrameEdges.HasValue ? "accepted" : "frame-unavailable", sampling = "painted-frame",
            topRgb = Rgb(0), rightRgb = Rgb(1), bottomRgb = Rgb(2), leftRgb = Rgb(3),
            topStatus = Status(0), rightStatus = Status(1), bottomStatus = Status(2), leftStatus = Status(3),
            source = "WebKit native fixedContainerEdges; painted in the same page frame", navigationGeneration, geometryGeneration,
            documentId = acceptedDocumentId
        };
    }
}
#endif
