#if WINDOWS
using System.Runtime.InteropServices;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct HostFrameSurface
    {
        public ulong Sequence, FrameId, DocumentEpoch, AdapterLuid;
        public nint SharedTexture, Pixels;
        public uint RowBytes, Background;
        public int Width, Height;
        public uint Flags, Status, FillCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public WindowsComposition.Fill[] Fills;
    }

    private bool presentingPageFrame;
    private ulong presentedSurfaceSequence, presentedFrameId, presentedDocumentEpoch;
    private ulong sourceAdapterLuid;
    private long sourceFrameAttempts, sourceFrameDrops;
    private long asynchronousPresentations;
    private bool lastFrameWasAsynchronous;
    private int pendingSurfaceRetries;
    private string? lastContentTransport;
    private int receivedWidth, receivedHeight, expectedWidth, expectedHeight;
    private uint receivedFlags;

    internal System.Drawing.Bitmap? CapturePageFramebuffer() => composition?.CapturePageFramebuffer();
    internal event Action<ulong, bool, WindowsComposition.Fill[]>? PageFrameSubmitted;

    private object FrameSurfaceDiagnostics() => new
    {
        apiVersion = 1, presentedSurfaceSequence, presentedFrameId, presentedDocumentEpoch, sourceAdapterLuid,
        sourceFrameAttempts, sourceFrameDrops, pendingSurfaceRetries, lastContentTransport,
        receivedWidth, receivedHeight, expectedWidth, expectedHeight, receivedFlags,
        asynchronousPresentations, lastFrameWasAsynchronous,
        gdiUploads = IsReady ? WKViewGetHostGDIUploadCount(view) : 0,
        strategy = "shared D3D11 or native CPU bitmap into a retained GPU page framebuffer; frame-bound continuation",
        snapshotReadback = "explicit framebuffer capture only; never page presentation"
    };

    private void PresentNativeFrame()
    {
        if (!IsReady || composition is null || disposed || presentationFailed || presentingPageFrame) return;
        presentingPageFrame = true;
        try
        {
            if (!WKViewAcquireHostFrameSurface(view, out var frame)) return;
            sourceFrameAttempts++;
            var presented = false;
            var pending = false;
            WindowsComposition.Fill[] fills = [];
            try
            {
                if (frame.Status != 0)
                    throw new InvalidOperationException($"WebKit shared-frame surface failed: 0x{frame.Status:X8}.");
                if (frame.FillCount > 8 || frame.Fills is null || frame.Fills.Length < frame.FillCount)
                    throw new InvalidOperationException("WebKit returned an invalid frame-fill count.");
                sourceAdapterLuid = frame.AdapterLuid;
                receivedWidth = frame.Width;
                receivedHeight = frame.Height;
                receivedFlags = frame.Flags;
                if (geometry is { } current)
                {
                    // Match WebKit's FloatSize raster extent. The integer HWND
                    // independently rounds each axis, while the scene uses one
                    // uniform backing scale. Their sizes need not be identical.
                    expectedWidth = (int)Math.Ceiling(current.Width * (float)current.Scale);
                    expectedHeight = (int)Math.Ceiling(current.Height * (float)current.Scale);
                    if (frame.Width != expectedWidth || frame.Height != expectedHeight) frame.Flags |= 4;
                }
                fills = frame.Fills.Take((int)frame.FillCount).ToArray();
                var result = composition.PresentPageFrame(new()
                {
                    FrameId = frame.FrameId, DocumentEpoch = frame.DocumentEpoch, SharedTexture = frame.SharedTexture,
                    Pixels = frame.Pixels, RowBytes = frame.RowBytes, Background = frame.Background, Flags = frame.Flags,
                    Width = frame.Width, Height = frame.Height
                }, fills);
                presented = result == WindowsComposition.PageFrameResult.Presented;
                pending = result == WindowsComposition.PageFrameResult.Pending;
                if (pending && ++pendingSurfaceRetries > 3)
                    throw new InvalidOperationException("WebKit shared-frame texture remained unavailable after three retries.");
                if (!pending) pendingSurfaceRetries = 0;
                if (presented)
                {
                    presentedSurfaceSequence = frame.Sequence;
                    presentedFrameId = frame.FrameId;
                    presentedDocumentEpoch = frame.DocumentEpoch;
                    lastFrameWasAsynchronous = (frame.Flags & 8) != 0;
                    if (lastFrameWasAsynchronous) asynchronousPresentations++;
                    if ((frame.Flags & 1) != 0) lastContentTransport = frame.SharedTexture != 0 ? "shared-d3d11" : "native-cpu-bitmap";
                }
                else sourceFrameDrops++;
            }
            finally
            {
                // A pending mutex has not consumed the producer's key. Keep
                // backpressure and replay that lease; acknowledging it would
                // let the producer wait forever for a key we never returned.
                if (pending) WKViewReplayHostFrameSurface(view, false);
                else WKViewAcknowledgeHostFrameSurface(view, frame.Sequence, presented);
            }
            if (presented)
            {
                if (lastFrameWasAsynchronous) FlushPendingTouchPointerAfterScroll();
                PageFrameSubmitted?.Invoke(frame.FrameId, lastFrameWasAsynchronous, fills);
            }
        }
        catch (InvalidOperationException failure)
        {
            presentationFailed = true;
            metadataTimer.Stop();
            WKViewSetIsInWindow(view, false);
            CompositionFailed?.Invoke(failure.Message);
        }
        finally { presentingPageFrame = false; }
    }
}
#endif
