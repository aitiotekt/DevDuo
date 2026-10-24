using Avalonia;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal static class Program
{
    internal static string Output = "";
    internal static bool InitializationFailure;
    internal static bool Presented;
    internal static bool PresentedColor;
    internal static bool PresentedLeft;
    internal static bool SafariReference;
    internal static string? StorageMode;
    internal static bool NavigationLatency;
    internal static bool NavigationLatencyInline;
    internal static bool NavigationNetworkRetry;
    internal static Task<InheritedHandleSentinel.NetworkRestart>? NetworkRetryTask;
    internal static BrowserTestServer? NavigationServer;
    internal static InheritedHandleSentinel? NavigationSentinel;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && args[0] is not ("--initialization-failure" or "--presented" or "--presented-color" or "--presented-left" or "--safari-reference" or "--storage-write" or "--storage-read" or "--browser-tools" or "--browser-performance" or "--developer-tools" or "--presentation-damage" or "--presentation-insets" or "--navigation-latency" or "--navigation-latency-inline" or "--navigation-latency-network-retry" or "--browser-identity" or "--fixed-overlay" or "--scroll-indicators" or "--interaction" or "--screenshots"))
        {
            Console.Error.WriteLine("Usage: DevDuo.Windows.Tests [--initialization-failure|--presented|--presented-color|--presented-left|--safari-reference|--storage-write|--storage-read|--browser-tools|--browser-performance|--developer-tools|--presentation-damage|--presentation-insets|--navigation-latency|--navigation-latency-inline|--navigation-latency-network-retry|--browser-identity|--fixed-overlay|--scroll-indicators|--interaction|--screenshots]");
            return 2;
        }
        InitializationFailure = args.Contains("--initialization-failure");
        NavigationLatencyInline = args.Contains("--navigation-latency-inline");
        NavigationNetworkRetry = args.Contains("--navigation-latency-network-retry");
        NavigationLatency = args.Contains("--navigation-latency") || NavigationLatencyInline || NavigationNetworkRetry;
        if (NavigationLatency) NavigationSentinel = new InheritedHandleSentinel();
        if (NavigationLatency) NavigationServer = new BrowserTestServer(latency: true);
        StorageMode = args.FirstOrDefault(argument => argument.StartsWith("--storage-", StringComparison.Ordinal));
        if (StorageMode is not null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVDUO_WEBKIT_PROFILE")))
            throw new InvalidOperationException("Storage tests require a shared absolute DEVDUO_WEBKIT_PROFILE test directory.");
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVDUO_WEBKIT_PROFILE")))
            Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_PROFILE", Path.Combine(Path.GetTempPath(), "devduo-webkit-tests-" + Guid.NewGuid().ToString("N")));
        SafariReference = args.Contains("--safari-reference");
        Presented = args.Contains("--presented") || args.Contains("--presented-color") || args.Contains("--presented-left");
        PresentedColor = args.Contains("--presented-color");
        PresentedLeft = args.Contains("--presented-left");
        Output = Path.Combine("captures", "desktop-validation", "windows-runtime",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        Directory.CreateDirectory(Output);
        Evidence.Freeze(Output);
        // Never depend on the receiver, external network, or private Safari evidence.
        Environment.SetEnvironmentVariable("DEVDUO_URL", NavigationLatency ? BrowserTestServer.Address : Presented ? Environment.GetEnvironmentVariable("DEVDUO_URL") ?? "http://127.0.0.1:8877/standards.html" : "http://127.0.0.1:1/");
        var runtimeFolder = Path.Combine(Path.GetTempPath(), "devduo-empty-runtime-" + Guid.NewGuid().ToString("N"));
        if (InitializationFailure)
        {
            Directory.CreateDirectory(runtimeFolder);
            Environment.SetEnvironmentVariable("DEVDUO_WEBKIT_RUNTIME", runtimeFolder);
        }
        // Native failures must not leave the user's real preferences replaced by test defaults.
        Environment.SetEnvironmentVariable("DEVDUO_PREFERENCES_PATH", Path.Combine(Path.GetTempPath(), "devduo-windows-preferences-" + Guid.NewGuid().ToString("N") + ".json"));
        var preferencesPath = PreferenceStore.DefaultPath;
        var preferencesBytes = File.Exists(preferencesPath) ? File.ReadAllBytes(preferencesPath) : null;
        PreferenceStore.Default.SaveHost(new HostSettings { PreviewScale = Presented ? "fit" : "native" });
        using var networkRetryCancellation = new CancellationTokenSource();
        try
        {
            if (NavigationNetworkRetry)
                NetworkRetryTask = Task.Run(() => NavigationSentinel!.InterruptFirstNetworkConnection(networkRetryCancellation.Token));
            var builder = AppBuilder.Configure<RuntimeTestApp>().UsePlatformDetect();
            return builder
                .StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            networkRetryCancellation.Cancel();
            NavigationServer?.Dispose();
            NavigationSentinel?.Dispose();
            if (preferencesBytes is null) File.Delete(preferencesPath);
            else File.WriteAllBytes(preferencesPath, preferencesBytes);
            if (Directory.Exists(runtimeFolder))
                Directory.Delete(runtimeFolder);
        }
    }
}
