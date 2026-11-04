#if WINDOWS
using System.Runtime.InteropServices;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private readonly Queue<object> hostFrameTimings = new();
    private static readonly Dictionary<nint, TaskCompletionSource<string>> scrollStateRequests = new();
    private static readonly HostScrollingStateCallback scrollStateCallback = ScrollStateFinished;

    private static void ScrollStateFinished(ulong request, nint value, nint context)
    {
        string state;
        try { state = value == 0 ? "unavailable" : WindowsWebKitApi.String(value); }
        finally { if (value != 0) WKRelease(value); }
        if (scrollStateRequests.Remove(context, out var completion))
            completion.TrySetResult(request == (ulong)context ? state : "invalid-request");
    }

    internal async Task<string> ReadNativeScrollingStateAsync()
    {
        if (!IsReady || disposed || RequestHostScrollingState is null)
            throw new InvalidOperationException("Native scrolling state is unavailable in this runtime.");
        var generation = navigationGeneration;
        var id = (nint)Interlocked.Increment(ref nextToken);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        scrollStateRequests.Add(id, completion);
        try
        {
            RequestHostScrollingState(view, (ulong)id, id, scrollStateCallback);
            var state = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (disposed || generation != navigationGeneration) throw new OperationCanceledException("The scrolling document changed.");
            return state;
        }
        finally { scrollStateRequests.Remove(id); }
    }

    private void RecordHostFrameTiming(string kind, long started, long finished)
    {
        if (!IsReady) return;
        var latest = new HostFrameTiming[1];
        var count = WKViewCopyHostFrameTimings(view, latest, 1);
        hostFrameTimings.Enqueue(new
        {
            kind,
            started = started / (double)System.Diagnostics.Stopwatch.Frequency,
            finished = finished / (double)System.Diagnostics.Stopwatch.Frequency,
            frameId = count == 0 ? (ulong?)null : latest[0].FrameId,
            inputSequence = count == 0 ? (ulong?)null : latest[0].InputSequence
        });
        if (hostFrameTimings.Count > 128) hostFrameTimings.Dequeue();
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct HostFrameTiming
    {
        public ulong FrameId, InputSequence;
        public double InputEventSeconds, InputDispatchSeconds, RenderStartSeconds, RenderEndSeconds, CommitSeconds;
        public double GpuStartSeconds, GpuEndSeconds, ReceiveSeconds, PaintStartSeconds, PaintEndSeconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HostScrollFrame
    {
        public ulong FrameId, SourceFrameId, DocumentEpoch, GeometryEpoch, ScrollRevision, InputSequence;
        public double ScrollX, ScrollY, InputEventSeconds, InputDispatchSeconds, CommitSeconds;
        public double GpuStartSeconds, GpuEndSeconds, ReceiveSeconds, PaintStartSeconds, PaintEndSeconds;
    }

    private object FrameTimingDiagnostics()
    {
        if (!IsReady) return new { available = false };
        var history = new HostFrameTiming[Environment.GetEnvironmentVariable("DEVDUO_FRAME_TIMING_HISTORY") == "1" ? 128 : 8];
        var count = WKViewCopyHostFrameTimings(view, history, (uint)history.Length);
        var scrollFrames = new HostScrollFrame[history.Length];
        var scrollCount = CopyHostScrollFrames?.Invoke(view, scrollFrames, (uint)scrollFrames.Length) ?? 0;
        var clockAvailable = WKViewGetHostFrameClock(out var nativeSeconds, out var qpcSeconds, out var uncertaintySeconds);
        return new
        {
            apiVersion = 2,
            clock = new { nativeSeconds, qpcSeconds, uncertaintySeconds, available = clockAvailable,
                domain = "machine-wide Windows QPC seconds",
                conversion = "qpc = native - nativeSeconds + qpcSeconds" },
            scope = "input dispatch, WebKit rendering, GPU transport and host frame copy/commit; excludes DWM scanout",
            hostEvents = hostFrameTimings.TakeLast(history.Length).ToArray(),
            hostEventScope = "QPC seconds; frame ID denotes latest acknowledged page submission, not input acknowledgement or DWM scanout",
            scrollFrameMetadataAvailable = CopyHostScrollFrames is not null,
            scrollFrames = scrollFrames.Take((int)scrollCount).Select(frame => new
            {
                frameId = frame.FrameId, sourceFrameId = frame.SourceFrameId, documentEpoch = frame.DocumentEpoch,
                geometryEpoch = frame.GeometryEpoch, scrollRevision = frame.ScrollRevision, inputSequence = frame.InputSequence,
                kind = frame.ScrollRevision == 0 ? "main-render" : "asynchronous-scroll",
                scrollX = frame.ScrollX, scrollY = frame.ScrollY,
                inputEvent = frame.InputEventSeconds, inputDispatch = frame.InputDispatchSeconds, commit = frame.CommitSeconds,
                gpuStart = frame.GpuStartSeconds, gpuEnd = frame.GpuEndSeconds, receive = frame.ReceiveSeconds,
                paintStart = frame.PaintStartSeconds, paintEnd = frame.PaintEndSeconds
            }).ToArray(),
            frames = history.Take((int)count).Select(frame => new
            {
                frameId = frame.FrameId, inputSequence = frame.InputSequence,
                inputEvent = frame.InputEventSeconds, inputDispatch = frame.InputDispatchSeconds,
                renderStart = frame.RenderStartSeconds, renderEnd = frame.RenderEndSeconds, commit = frame.CommitSeconds,
                gpuStart = frame.GpuStartSeconds, gpuEnd = frame.GpuEndSeconds, receive = frame.ReceiveSeconds,
                paintStart = frame.PaintStartSeconds, paintEnd = frame.PaintEndSeconds
            }).ToArray()
        };
    }
}
#endif
