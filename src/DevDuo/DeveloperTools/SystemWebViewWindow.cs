using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DevDuo;

// This browser displays the debugging frontend only. The measured page keeps its WebKit backend.
public sealed class SystemWebViewWindow : Window
{
    private readonly TextBlock status = new() { Margin = new Avalonia.Thickness(12), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly SystemWebViewHost browser;
    private readonly Button installRuntime = new() { Margin = new Avalonia.Thickness(12, 0, 12, 12), IsVisible = false };
    private string? errorDetail;
    private AppLocalization language;

    public SystemWebViewWindow(Uri address, AppLocalization language)
    {
        this.language = language;
        Width = 1100;
        Height = 780;
        MinWidth = 600;
        MinHeight = 400;
        Title = language.Text("Developer.ChromeDevTools");
        browser = new SystemWebViewHost(address, (message, missingRuntime) =>
        {
            errorDetail = message;
            status.Text = missingRuntime ? this.language.Text("Developer.WebView2Missing") : this.language.Format("Developer.WindowError", message);
            status.IsVisible = true;
            installRuntime.IsVisible = missingRuntime;
        });
        installRuntime.Content = language.Text("Developer.InstallWebView2");
        installRuntime.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true });
            }
            catch (Exception error)
            {
                errorDetail = error.Message;
                status.Text = this.language.Format("Developer.WindowError", error.Message);
            }
        };
        status.IsVisible = false;
        var content = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(installRuntime, Dock.Bottom);
        content.Children.Add(installRuntime);
        content.Children.Add(status);
        content.Children.Add(browser);
        Content = content;
        Closed += (_, _) => browser.Dispose();
    }

    public void Localize(AppLocalization language)
    {
        this.language = language;
        Title = language.Text("Developer.ChromeDevTools");
        installRuntime.Content = language.Text("Developer.InstallWebView2");
        if (errorDetail is not null)
            status.Text = installRuntime.IsVisible ? language.Text("Developer.WebView2Missing") : language.Format("Developer.WindowError", errorDetail);
    }

    public void Reload() => browser.Reload();
}

internal sealed class SystemWebViewHost(Uri address, Action<string, bool> failed) : NativeControlHost, IDisposable
{
    private IntPtr macView;
    private int generation;
#if WINDOWS
    private Microsoft.Web.WebView2.WinForms.WebView2? windowsView;
#endif

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        generation++;
        if (OperatingSystem.IsMacOS())
        {
            macView = MacCreate(address.AbsoluteUri);
            if (macView == IntPtr.Zero)
            {
                var message = AppLocalization.Current.Text("Error.NativeCreate");
                failed(message, false);
                throw new InvalidOperationException(message);
            }
            return new PlatformHandle(macView, "NSView");
        }
#if WINDOWS
        windowsView = new Microsoft.Web.WebView2.WinForms.WebView2();
        var handle = windowsView.Handle;
        SetParent(handle, parent.Handle);
        _ = InitializeWindowsAsync(windowsView, generation);
        return new PlatformHandle(handle, "HWND");
#else
        throw new PlatformNotSupportedException(AppLocalization.Current.Text("Error.Platform"));
#endif
    }

#if WINDOWS
    private async Task InitializeWindowsAsync(Microsoft.Web.WebView2.WinForms.WebView2 view, int created)
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "DevToolsWebView2");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(userDataFolder: data);
            if (created != generation || view.IsDisposed)
                return;
            await view.EnsureCoreWebView2Async(environment);
            if (created != generation || view.IsDisposed)
                return;
            view.CoreWebView2.Navigate(address.AbsoluteUri);
        }
        catch (Exception error)
        {
            if (created == generation)
                Dispatcher.UIThread.Post(() =>
                {
                    if (created == generation)
                        failed(error.Message, error is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException);
                });
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
#endif

    protected override void DestroyNativeControlCore(IPlatformHandle control) => Dispose();

    public void Reload()
    {
#if WINDOWS
        windowsView?.CoreWebView2?.Reload();
#endif
        if (macView != IntPtr.Zero)
            MacReload(macView);
    }

    public void Dispose()
    {
        generation++;
#if WINDOWS
        windowsView?.Dispose();
        windowsView = null;
#endif
        if (macView != IntPtr.Zero)
        {
            MacDestroy(macView);
            macView = IntPtr.Zero;
        }
    }

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_tools_webview_create")]
    private static extern IntPtr MacCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string address);

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_tools_webview_destroy")]
    private static extern void MacDestroy(IntPtr view);

    [DllImport("libDevDuoWebView.dylib", EntryPoint = "devduo_tools_webview_reload")]
    private static extern void MacReload(IntPtr view);
}
