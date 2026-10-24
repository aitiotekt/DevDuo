#if WINDOWS
using System.Drawing;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private sealed record FixedContainerColors(uint Status, uint States, uint Top, uint Right, uint Bottom, uint Left);
    private sealed record FixedColorRequest(WindowsWebKitView Owner, long Navigation, long Geometry, string Document, TaskCompletionSource<FixedContainerColors?> Completion);
    private static readonly Dictionary<nint, FixedColorRequest> fixedColorRequests = new();
    private static readonly FixedContainerColorCallback fixedColorCallback = FixedColorsFinished;
    private FixedContainerColors? fixedContainerColors;
    private bool fixedColorBusy;
    private string fixedColorStatus = "not-requested";
    private long fixedColorRevision;
    private readonly FixedColorSampleSchedule fixedColorSchedule = new();
    internal event Action? FixedContainerColorsChanged;

    private bool InvalidateFixedContainerColors()
    {
        var hadColors = FixedColor(0) is not null || FixedColor(1) is not null || FixedColor(2) is not null || FixedColor(3) is not null;
        fixedColorRevision++;
        fixedColorSchedule.Reset();
        fixedColorBusy = false;
        fixedColorStatus = "invalidated";
        fixedContainerColors = null;
        foreach (var pending in fixedColorRequests.Where(item => item.Value.Owner == this).ToArray())
        {
            fixedColorRequests.Remove(pending.Key);
            pending.Value.Completion.TrySetResult(null);
        }
        return hadColors;
    }

    private async Task RefreshFixedContainerColorsAsync()
    {
        if (!IsReady || !committed || acceptedDocumentId is null || geometry is null || fixedColorBusy) return;
        if (!WKViewGetHostRenderingStatistics(view, out var updates, out _, out _))
        {
            if (fixedColorStatus != "render-statistics-unavailable") fixedColorSchedule.Wake();
            fixedColorStatus = "render-statistics-unavailable";
            var hadColors = fixedContainerColors is not null;
            fixedContainerColors = null;
            if (hadColors) { fixedColorRevision++; Present(); FixedContainerColorsChanged?.Invoke(); }
            return;
        }
        // The existing 200 ms UI timer bounds sampling. Rendering updates cover
        // CSS animation, dialog state and computed-style changes without DOM hooks.
        if (!fixedColorSchedule.TryBegin(updates, out var sample)) return;
        var revision = ++fixedColorRevision;
        var navigation = navigationGeneration;
        var geometryVersion = geometryGeneration;
        var document = acceptedDocumentId;
        var id = (nint)Interlocked.Increment(ref nextToken);
        var completion = new TaskCompletionSource<FixedContainerColors?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixedColorRequests.Add(id, new(this, navigation, geometryVersion, document, completion));
        fixedColorBusy = true;
        fixedColorStatus = "pending";
        try
        {
            WKViewRequestFixedContainerColors(view, (ulong)id, id, fixedColorCallback);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (disposed || revision != fixedColorRevision || navigation != navigationGeneration ||
                geometryVersion != geometryGeneration || document != acceptedDocumentId || !committed) return;
            fixedContainerColors = result;
            fixedColorSchedule.Complete(sample, result is { Status: 0 });
            fixedColorStatus = result is { Status: 0 } ? "accepted" : result is null ? "stale-or-unavailable" : "native-unavailable";
            Present();
        }
        catch (TimeoutException)
        {
            if (!disposed && revision == fixedColorRevision)
            {
                fixedContainerColors = null;
                fixedColorStatus = "timeout";
                fixedColorSchedule.Complete(sample, false);
                Present();
            }
        }
        finally
        {
            fixedColorRequests.Remove(id);
            if (revision == fixedColorRevision)
            {
                fixedColorBusy = false;
                if (!disposed) FixedContainerColorsChanged?.Invoke();
            }
        }
    }

    private static void FixedColorsFinished(ulong request, uint status, uint states, uint top, uint right, uint bottom, uint left, nint context)
    {
        if (!fixedColorRequests.Remove(context, out var pending)) return;
        var owner = pending.Owner;
        var current = request == (ulong)context && !owner.disposed && owner.committed &&
            pending.Navigation == owner.navigationGeneration && pending.Geometry == owner.geometryGeneration &&
            pending.Document == owner.acceptedDocumentId;
        pending.Completion.TrySetResult(current ? new(status, states, top, right, bottom, left) : null);
    }

    private Color? FixedColor(int side)
    {
        if (fixedContainerColors is not { Status: 0 } colors || ((colors.States >> (side * 2)) & 3) != 1) return null;
        var rgb = side switch { 0 => colors.Top, 1 => colors.Right, 2 => colors.Bottom, _ => colors.Left };
        return Color.FromArgb((int)(rgb >> 16) & 255, (int)(rgb >> 8) & 255, (int)rgb & 255);
    }

    private object FixedContainerColorDiagnostics()
    {
        int[]? Rgb(int side) => FixedColor(side) is { } color ? [color.R, color.G, color.B] : null;
        string Status(int side) => fixedContainerColors is { Status: 0 } colors ? ((colors.States >> (side * 2)) & 3) switch
        {
            0 => "none", 1 => "accepted", _ => "multiple-or-translucent"
        } : "unavailable";
        return new { apiVersion = 1, revision = fixedColorRevision, inFlight = fixedColorBusy, status = fixedColorStatus,
            topRgb = Rgb(0), rightRgb = Rgb(1), bottomRgb = Rgb(2), leftRgb = Rgb(3),
            topStatus = Status(0), rightStatus = Status(1), bottomStatus = Status(2), leftStatus = Status(3),
            source = "WebKit native fixedContainerEdges", navigationGeneration, geometryGeneration, documentId = acceptedDocumentId };
    }
}
#endif
