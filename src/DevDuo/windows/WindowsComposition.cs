#if WINDOWS
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevDuo;

internal sealed class WindowsComposition : IDisposable
{
    internal enum Kind { Top, Address, Back, Bookmarks, TabGroup, SelectedTab, Tab, Camera, Wifi, Clock, Menu, Reload, Plus, Overview, TabTrack, Divider, CompactAddress }
    internal enum Action { None, Address, Back, Bookmarks, NewTab, Overview, Menu, Reload }
    private const string Library = "DevDuo.Windows.Compositor.dll";
    private static readonly JsonSerializerOptions SceneOptions = new() { IncludeFields = true };
    private nint native;
    private string? previousScene;
    private (string Text, float FontSize, float Width)? textMeasurement;
    private string? textMeasurementLanguage;
    private ulong nativeTextMeasurementCount, textMeasurementCacheHits;
    private (nint Target, int Degrees, int X, int Y)? presentation;
    private (float X, float Y, float Width, float Height, float Opacity)? scrollIndicator;
    internal string? Error => Marshal.PtrToStringUni(DDCompositionError());
    [StructLayout(LayoutKind.Sequential)]
    internal struct State
    {
        public uint ApiVersion, SoftwareDevice;
        public ulong ResourceGeneration, SceneGeneration;
        public int Width, Height;
    }
    internal object Diagnostics
    {
        get
        {
            var state = native == 0 ? default : DDCompositionGetState(native);
            return new { apiVersion = state.ApiVersion, softwareDevice = state.SoftwareDevice != 0, resourceGeneration = state.ResourceGeneration,
                sceneGeneration = state.SceneGeneration, width = state.Width, height = state.Height,
                hostTextMeasurement = new { nativeCalls = nativeTextMeasurementCount, cacheHits = textMeasurementCacheHits },
                presentationApiVersion = 1, sceneInCanvasDegrees = presentation?.Degrees ?? 0, offsetX = presentation?.X ?? 0, offsetY = presentation?.Y ?? 0 };
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Chrome
    {
        public Kind Kind;
        public Action Action;
        public float X, Y, Width, Height, VisualX, VisualY, VisualWidth, VisualHeight, Radius, FontSize, ClipWidth;
        public uint Foreground;
        [MarshalAs(UnmanagedType.LPWStr)] public string Text;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Fill { public float X, Y, Width, Height; public uint Color; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Material
    {
        public uint Tint;
        public float Alpha, Strength, Shadow, Scale;
        public float Blur, Saturation, Brightness, Contrast, TopHighlight, EdgeHighlight, Sheen, ShadowBlur, ShadowOffset;
    }
    internal WindowsComposition(nint parent, nint browser)
    {
        if (DDCompositionVersion() != 3) throw new InvalidOperationException("Windows composition API v3 is required.");
        if (DDCompositionPresentationVersion() != 1) throw new InvalidOperationException("Windows composition presentation API v1 is required.");
        if (DDCompositionScrollIndicatorVersion() != 1) throw new InvalidOperationException("Windows composition scroll indicator API v1 is required.");
        native = DDCompositionCreate(parent, browser);
        if (native == 0) throw new InvalidOperationException(Marshal.PtrToStringUni(DDCompositionError()));
    }
    internal void Update(int width, int height, Chrome[] chrome, Fill[] fill, Material material)
    {
        if (native == 0 || width <= 0 || height <= 0) return;
        var scene = JsonSerializer.Serialize(new { width, height, chrome, fill, material }, SceneOptions);
        if (scene == previousScene) return;
        var result = DDCompositionUpdate(native, width, height, chrome, chrome.Length, fill, fill.Length, material);
        if (result < 0) throw new InvalidOperationException(Marshal.PtrToStringUni(DDCompositionError()));
        previousScene = scene;
    }
    internal Action Hit(int x, int y) => native == 0 ? Action.None : (Action)DDCompositionHit(native, x, y);
    internal void SetScrollIndicator(float x, float y, float width, float height, float opacity)
    {
        var next = (x, y, width, height, opacity);
        if (native == 0 || scrollIndicator == next) return;
        if (DDCompositionSetScrollIndicator(native, x, y, width, height, opacity) < 0)
            throw new InvalidOperationException(Error);
        scrollIndicator = next;
    }
    internal void SetPresentation(nint target, int degrees, int offsetX, int offsetY)
    {
        if (native != 0 && DDCompositionSetPresentation(native, target, degrees, offsetX, offsetY) < 0)
            throw new InvalidOperationException(Error);
        presentation = (target, degrees, offsetX, offsetY);
    }
    internal float MeasureText(string text, float fontSize)
    {
        if (native == 0) return 0;
        var language = AppLocalization.Current.Language;
        if (textMeasurementLanguage != language)
        {
            textMeasurement = null;
            textMeasurementLanguage = language;
        }
        if (textMeasurement is { } cached && cached.FontSize == fontSize && string.Equals(cached.Text, text, StringComparison.Ordinal))
        {
            textMeasurementCacheHits++;
            return cached.Width;
        }
        nativeTextMeasurementCount++;
        if (DDCompositionMeasureText(native, text, fontSize, out var width) < 0)
            throw new InvalidOperationException(Error);
        if (float.IsFinite(width)) textMeasurement = (text, fontSize, width);
        return width;
    }
    internal bool Refresh()
    {
        if (native == 0) return false;
        var result = DDCompositionCheck(native);
        if (result < 0) throw new InvalidOperationException(Error);
        if (result == 0) return false;
        previousScene = null;
        scrollIndicator = null;
        textMeasurement = null;
        textMeasurementLanguage = null;
        return true;
    }
    public void Dispose()
    {
        textMeasurement = null;
        textMeasurementLanguage = null;
        if (native == 0) return;
        DDCompositionDestroy(native);
        native = 0;
    }
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DDCompositionVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DDCompositionPresentationVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DDCompositionScrollIndicatorVersion();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DDCompositionSetScrollIndicator(nint native, float x, float y, float width, float height, float opacity);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DDCompositionSetPresentation(nint native, nint target, int degrees, int offsetX, int offsetY);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern nint DDCompositionCreate(nint parent, nint browser);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DDCompositionUpdate(nint native, int width, int height, [In] Chrome[] chrome, int count, [In] Fill[] fill, int fillCount, Material material);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DDCompositionHit(nint native, int x, int y);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private static extern int DDCompositionMeasureText(nint native, string text, float fontSize, out float width);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DDCompositionCheck(nint native);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern State DDCompositionGetState(nint native);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern nint DDCompositionError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void DDCompositionDestroy(nint native);
}
#endif
