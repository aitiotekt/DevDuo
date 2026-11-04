using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;

namespace DevDuo.Windows.Tests;

internal sealed class ComposedWindowSequence
{
    internal sealed record Frame(long Time100Ns, byte[] Bgra);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ready => ready.Task;
    internal Task<List<Frame>> Completed { get; }

    internal ComposedWindowSequence(nint window, PixelRect bounds, uint durationMs)
    {
        Completed = Task.Run(() =>
        {
            var frames = new List<Frame>();
            var result = DDWindowCaptureFrames(window, bounds.X, bounds.Y, bounds.Width, bounds.Height, durationMs,
                (pixels, timestamp) =>
                {
                    var data = new byte[checked(bounds.Width * bounds.Height * 4)];
                    Marshal.Copy(pixels, data, 0, data.Length);
                    frames.Add(new(timestamp, data));
                    ready.TrySetResult();
                });
            if (result < 0)
                throw new InvalidOperationException(Marshal.PtrToStringUni(DDWindowCaptureError()) ?? "Window sequence capture failed.");
            return frames;
        });
    }

    internal static void Save(Frame frame, PixelRect bounds, string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(frame.Bgra, 0, bitmap.GetPixels(), frame.Bgra.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CaptureFrame(nint pixels, long timestamp);

    [DllImport("DevDuo.Windows.Compositor.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DDWindowCaptureFrames(nint window, int x, int y, int width, int height, uint duration, CaptureFrame observe);

    [DllImport("DevDuo.Windows.Compositor.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint DDWindowCaptureError();
}
