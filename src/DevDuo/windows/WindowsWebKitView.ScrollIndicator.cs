#if WINDOWS
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private sealed record IndicatorSample(uint Status, uint Flags, ulong Epoch, ulong Sequence, double Position, double Range, double ViewportHeight);
    private sealed record IndicatorRequest(WindowsWebKitView Owner, long Navigation, long Geometry, string Document, TaskCompletionSource<IndicatorSample?> Completion);
    private static readonly Dictionary<nint, IndicatorRequest> indicatorRequests = new();
    private static readonly HostScrollIndicatorCallback indicatorCallback = IndicatorFinished;
    private readonly ScrollIndicatorState scrollIndicator = new();
    private readonly System.Windows.Forms.Timer indicatorAnimation = new() { Interval = 16 };
    private bool indicatorEnabled, indicatorBusy, indicatorWake, indicatorPrimed;
    private ulong? indicatorSampledUpdate, indicatorEpoch;
    private uint indicatorNativeFlags;
    private long indicatorRevision, indicatorQueries, indicatorRequestGeneration;
    private double indicatorViewportHeight;
    private double? indicatorObservedY;
    private bool indicatorPaintedVisible;
    private string indicatorStatus = "not-requested";
    internal event Action? ScrollIndicatorChanged;

    private void ConfigureScrollIndicator(bool enabled)
    {
        indicatorEnabled = enabled;
        WKViewSetHostScrollIndicatorEnabled(view, enabled);
        indicatorAnimation.Tick += (_, _) => PaintScrollIndicator();
    }

    private void RecordScrollIndicatorInput()
    {
        if (!indicatorEnabled || disposed || !committed || !indicatorPrimed) return;
        scrollIndicator.RecordInput(Environment.TickCount64);
        indicatorWake = true;
        ScrollIndicatorChanged?.Invoke();
    }

    private void ObserveScrollIndicatorMotion(System.Text.Json.JsonElement observation)
    {
        if (!indicatorEnabled || !observation.TryGetProperty("scrollY", out var value) ||
            value.ValueKind != System.Text.Json.JsonValueKind.Number || !value.TryGetDouble(out var y) || !double.IsFinite(y)) return;
        // Observation is only a sampling wake-up. It never confirms User motion.
        if (indicatorObservedY != y) indicatorWake = true;
        indicatorObservedY = y;
    }

    private void ResetScrollIndicator(bool newDocument, bool preserveSession = false)
    {
        indicatorRequestGeneration++;
        indicatorRevision++;
        indicatorBusy = false;
        indicatorWake = true;
        indicatorSampledUpdate = null;
        indicatorAnimation.Stop();
        if (!preserveSession) scrollIndicator.Reset(!newDocument);
        if (!preserveSession) indicatorPrimed = false;
        if (newDocument) { indicatorEpoch = null; indicatorObservedY = null; indicatorNativeFlags = 0; }
        indicatorStatus = "invalidated";
        foreach (var pending in indicatorRequests.Where(item => item.Value.Owner == this).ToArray())
        {
            indicatorRequests.Remove(pending.Key);
            pending.Value.Completion.TrySetResult(null);
        }
        PaintScrollIndicator();
    }

    private async Task RefreshScrollIndicatorAsync()
    {
        if (!indicatorEnabled || !IsReady || presentationFailed || !committed || acceptedDocumentId is null || geometry is null || indicatorBusy) return;
        if (!WKViewGetHostRenderingStatistics(view, out var updates, out _, out _))
        {
            if (indicatorStatus != "render-statistics-unavailable")
            {
                scrollIndicator.Reset(true);
                indicatorStatus = "render-statistics-unavailable";
                indicatorSampledUpdate = null;
                PaintScrollIndicator();
            }
            return;
        }
        // Reuse the bounded metadata timer; static idle causes no async IPC.
        // Input and native render changes wake sampling. Fade uses only DComp.
        if (!indicatorWake && indicatorSampledUpdate == updates) return;
        indicatorWake = false;
        indicatorSampledUpdate = updates;
        var generation = indicatorRequestGeneration;
        var navigation = navigationGeneration;
        var geometryVersion = geometryGeneration;
        var document = acceptedDocumentId;
        var id = (nint)Interlocked.Increment(ref nextToken);
        var completion = new TaskCompletionSource<IndicatorSample?>(TaskCreationOptions.RunContinuationsAsynchronously);
        indicatorRequests.Add(id, new(this, navigation, geometryVersion, document, completion));
        indicatorBusy = true;
        indicatorQueries++;
        try
        {
            WKViewRequestHostScrollIndicatorState(view, (ulong)id, id, indicatorCallback);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (disposed || generation != indicatorRequestGeneration || navigation != navigationGeneration ||
                geometryVersion != geometryGeneration || document != acceptedDocumentId || !committed) return;
            indicatorRevision++;
            if (result is not { Status: 0 } || (result.Flags & 1) == 0 ||
                !double.IsFinite(result.Position) || !double.IsFinite(result.Range) || !double.IsFinite(result.ViewportHeight))
            {
                scrollIndicator.Reset(true);
                indicatorStatus = "native-unavailable";
                indicatorPrimed = false;
            }
            else
            {
                // A new native document epoch primes the baseline without
                // borrowing an earlier document's accumulated User sequence.
                if (!indicatorPrimed || indicatorEpoch != result.Epoch)
                {
                    scrollIndicator.Reset();
                    indicatorEpoch = result.Epoch;
                    indicatorPrimed = true;
                }
                indicatorViewportHeight = result.ViewportHeight;
                indicatorNativeFlags = result.Flags;
                scrollIndicator.Observe(result.Sequence, result.Position, result.Range, (result.Flags & 2) != 0, Environment.TickCount64);
                indicatorStatus = "accepted";
            }
            PaintScrollIndicator();
            ScrollIndicatorChanged?.Invoke();
        }
        catch (TimeoutException)
        {
            if (!disposed && generation == indicatorRequestGeneration)
            {
                scrollIndicator.Reset(true);
                indicatorStatus = "timeout";
                indicatorSampledUpdate = null;
                PaintScrollIndicator();
            }
        }
        finally
        {
            indicatorRequests.Remove(id);
            if (generation == indicatorRequestGeneration) indicatorBusy = false;
        }
    }

    private static void IndicatorFinished(ulong request, uint status, uint flags, ulong epoch, ulong sequence,
        double position, double range, double viewportHeight, nint context)
    {
        if (!indicatorRequests.Remove(context, out var pending)) return;
        var owner = pending.Owner;
        var current = request == (ulong)context && !owner.disposed && owner.committed &&
            pending.Navigation == owner.navigationGeneration && pending.Geometry == owner.geometryGeneration &&
            pending.Document == owner.acceptedDocumentId;
        pending.Completion.TrySetResult(current ? new(status, flags, epoch, sequence, position, range, viewportHeight) : null);
    }

    private (double X, double Y, double Width, double Height) IndicatorTrack()
        => geometry is { } g ? (g.Width - 6, 4, 3, Math.Max(0, g.Height - 8)) : (0, 0, 0, 0);

    private (double X, double Y, double Width, double Height) IndicatorThumb()
    {
        var track = IndicatorTrack();
        var length = Math.Clamp(track.Height * indicatorViewportHeight / Math.Max(1, indicatorViewportHeight + scrollIndicator.Range), Math.Min(24, track.Height), track.Height);
        var offset = scrollIndicator.Range > 0 ? scrollIndicator.Position / scrollIndicator.Range * (track.Height - length) : 0;
        return (track.X, track.Y + offset, track.Width, length);
    }

    private void PaintScrollIndicator()
    {
        if (disposed || presentationFailed) return;
        var opacity = indicatorEnabled ? scrollIndicator.Opacity(Environment.TickCount64) : 0;
        var frame = IndicatorThumb();
        var scale = geometry?.Scale ?? 1;
        try { composition?.SetScrollIndicator((float)(frame.X * scale), (float)(frame.Y * scale), (float)(frame.Width * scale), (float)(frame.Height * scale), (float)opacity); }
        catch (InvalidOperationException failure)
        {
            presentationFailed = true;
            indicatorAnimation.Stop();
            metadataTimer.Stop();
            CompositionFailed?.Invoke(failure.Message);
            return;
        }
        if (opacity > 0) indicatorAnimation.Start();
        else indicatorAnimation.Stop();
        // A fade frame performs no WebKit IPC. Publish only the visibility edge;
        // normal bounded native samples publish position/state separately.
        if (indicatorPaintedVisible != (opacity > 0))
        {
            indicatorPaintedVisible = opacity > 0;
            ScrollIndicatorChanged?.Invoke();
        }
    }

    internal object ScrollIndicatorDiagnostics()
    {
        var track = IndicatorTrack();
        var thumb = IndicatorThumb();
        var opacity = indicatorEnabled ? scrollIndicator.Opacity(Environment.TickCount64) : 0;
        return new { enabled = indicatorEnabled, visible = opacity > 0, opacity,
            thumbFrameSceneCssPx = new { x = thumb.X, y = thumb.Y, width = thumb.Width, height = thumb.Height },
            trackFrameSceneCssPx = new { x = track.X, y = track.Y, width = track.Width, height = track.Height },
            cssSuppressed = scrollIndicator.CssSuppressed, scrollRangeCssPx = scrollIndicator.Range,
            nativeViewportHeightCssPx = indicatorViewportHeight,
            nativeScrollbarObject = new { present = (indicatorNativeFlags & 4) != 0, custom = (indicatorNativeFlags & 8) != 0,
                zeroThickness = (indicatorNativeFlags & 16) != 0, hiddenByStyle = (indicatorNativeFlags & 32) != 0,
                computedVisibilityHidden = (indicatorNativeFlags & 64) != 0,
                widthCssPx = (indicatorNativeFlags & 4) != 0 ? (int?)(indicatorNativeFlags >> 16) : null },
            inputSequence = scrollIndicator.InputSequence, confirmedMotionSequence = scrollIndicator.ConfirmedMotionSequence,
            navigationGeneration, geometryGeneration, documentId = acceptedDocumentId, nativeDocumentEpoch = indicatorEpoch,
            engineScrollbarsHidden = indicatorEnabled, colorRgb = new[] { 0, 0, 0 },
            holdMilliseconds = ScrollIndicatorState.HoldMilliseconds, fadeMilliseconds = ScrollIndicatorState.FadeMilliseconds,
            thicknessCssPx = 3, outerInsetCssPx = 3, status = indicatorStatus, source = "native root User motion; host scene overlay approximation",
            queryCount = indicatorQueries, revision = indicatorRevision, inFlight = indicatorBusy };
    }
}
#endif
