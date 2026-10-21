using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
namespace DevDuo;

public sealed class NativeWebView(DeviceProfile profile, string initialUrl, SafariContainerOptions options) : NativeControlHost
{
    private IntPtr native;
    private readonly string observer = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "browser-observer.js"));
#if WINDOWS
    private WindowsWebViewPanel? panel;
#endif
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsMacOS())
        {
            native = MacCreate(initialUrl, profile.ScreenMaskPath ?? "", profile.MaskCanvasWidth, profile.MaskCanvasHeight, options.ToJson(), observer);
            if (native == IntPtr.Zero)
                throw new InvalidOperationException(AppLocalization.Current.Text("Error.NativeCreate"));
            return new PlatformHandle(native, "NSView");
        }
#if WINDOWS
        panel = new WindowsWebViewPanel(profile, initialUrl, options, observer);
        native = panel.Handle;
        SetParent(native, parent.Handle);
        return new PlatformHandle(native, "HWND");
#else
        throw new PlatformNotSupportedException(AppLocalization.Current.Text("Error.Platform"));
#endif
    }
    public void Navigate(string url)
    {
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacNavigate(native, url);
#if WINDOWS
        else
            panel?.Navigate(url);
#endif
    }
    public void Configure(SafariContainerOptions current)
    {
        options = current;
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacConfigure(native, current.ToJson());
#if WINDOWS
        else
            panel?.Configure(current);
#endif
        UpdateFixtureLanguage();
    }
    public void ShowColorFixture()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "color-scroll.html"));
        html = html
            .Replace("__DEVDUO_LANGUAGE__", AppLocalization.Current.Language)
            .Replace("__DEVDUO_STRINGS__", JsonSerializer.Serialize(AppLocalization.Current.FixtureStrings()));
        if (native == IntPtr.Zero)
            return;
        if (OperatingSystem.IsMacOS())
            MacHtml(native, html);
#if WINDOWS
        else
            panel?.ShowHtml(html);
#endif
    }
    private void UpdateFixtureLanguage()
    {
        if (native == IntPtr.Zero)
            return;
        var script = "if(document.documentElement.dataset.devduoFixture==='color-lab-v1' && " +
            "typeof window.devduoLocalizeFixture==='function')window.devduoLocalizeFixture(" +
            JsonSerializer.Serialize(AppLocalization.Current.Language) + "," +
            JsonSerializer.Serialize(AppLocalization.Current.FixtureStrings()) + ");";
        if (OperatingSystem.IsMacOS())
            MacEvaluate(native, script);
#if WINDOWS
        else
            panel?.Evaluate(script);
#endif
    }
    public string ReadState()
    {
        if (native == IntPtr.Zero)
            return "{}";
        if (OperatingSystem.IsMacOS())
        {
            var pointer = MacState(native);
            if (pointer == IntPtr.Zero)
                return "{}";
            try
            {
                return Marshal.PtrToStringUTF8(pointer) ?? "{}";
            }
            finally { MacFree(pointer); }
        }
#if WINDOWS
        return panel?.ReadState() ?? "{}";
#else
        return "{}";
#endif
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsMacOS())
            MacDestroy(control.Handle);
#if WINDOWS
        else
        {
            panel?.Dispose();
            panel = null;
        }
#endif
        native = IntPtr.Zero;
    }
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_create")]
    private static extern IntPtr MacCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string url, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, double width, double height, [MarshalAs(UnmanagedType.LPUTF8Str)] string options, [MarshalAs(UnmanagedType.LPUTF8Str)] string observer);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_evaluate")]
    private static extern void MacEvaluate(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string script);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_navigate")]
    private static extern void MacNavigate(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_configure")]
    private static extern void MacConfigure(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string options);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_html")]
    private static extern void MacHtml(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string html);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_state_json")]
    private static extern IntPtr MacState(IntPtr view);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_free")]
    private static extern void MacFree(IntPtr pointer);
    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_destroy")]
    private static extern void MacDestroy(IntPtr view);
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
#endif
}
