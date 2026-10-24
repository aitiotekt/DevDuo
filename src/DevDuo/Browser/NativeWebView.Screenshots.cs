using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;

namespace DevDuo;

public sealed partial class NativeWebView
{
#if !WINDOWS
    private static long nextCapture;
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<NativeScreenshot>> Captures = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CaptureCallback(nint bytes, int length, int width, int height, double scale, nint error, long requestId);
    private static readonly CaptureCallback ScreenshotCompleted = CompleteScreenshot;

    private static void CompleteScreenshot(nint bytes, int length, int width, int height, double scale, nint error, long requestId)
    {
        if (!Captures.TryRemove(requestId, out var pending)) return;
        if (error != 0) pending.TrySetException(new ControlApiException(409, Marshal.PtrToStringUTF8(error) ?? "Native capture failed."));
        else if (length < 1 || length > 128 * 1024 * 1024)
            pending.TrySetException(new ControlApiException(409, "Invalid native screenshot size."));
        else
        {
            var data = new byte[length];
            Marshal.Copy(bytes, data, 0, length);
            pending.TrySetResult(new NativeScreenshot(data, width, height, scale));
        }
    }

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_capture_screenshot")]
    private static extern void MacCaptureScreenshot(nint view, int device, double x, double y, double width, double height,
        CaptureCallback callback, long requestId);
#endif

    internal async Task<ScreenshotImage> CaptureScreenshotAsync(ScreenshotRequest request, Rect windowCrop = default,
        PixelRect screenCrop = default, nint windowHandle = 0, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!DeveloperViewReady) throw new ControlApiException(409, "The native page is not ready.");
        var created = generation;
        var frame = viewport;
        NativeScreenshot capture;
#if WINDOWS
        var browser = panel!.Browser;
        var before = await browser.ReadScreenshotStateAsync().WaitAsync(cancellationToken);
        if (request.Target == "viewport") capture = browser.CaptureViewport();
        else capture = await Task.Run(() => WindowsWindowCapture.Capture(windowHandle, screenCrop), cancellationToken).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (created != generation || !ReferenceEquals(panel?.Browser, browser) || before != await browser.ReadScreenshotStateAsync().WaitAsync(cancellationToken))
            throw new ControlApiException(409, "The page changed during capture; retry after layout/scroll settles.");
#else
        var id = Interlocked.Increment(ref nextCapture);
        var pending = new TaskCompletionSource<NativeScreenshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Captures[id] = pending;
        try
        {
            MacCaptureScreenshot(native, request.Target == "device" ? 1 : 0, windowCrop.X, windowCrop.Y, windowCrop.Width, windowCrop.Height, ScreenshotCompleted, id);
            capture = await pending.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch (TimeoutException) { throw new ControlApiException(409, "Native capture timed out; retry after the page settles."); }
        finally { Captures.TryRemove(id, out _); }
#endif
        cancellationToken.ThrowIfCancellationRequested();
        if (created != generation || frame != viewport || !DeveloperViewReady)
            throw new ControlApiException(409, "The device presentation changed during capture; retry after layout settles.");
        return ScreenshotEncoding.Encode(capture, request);
    }
}
