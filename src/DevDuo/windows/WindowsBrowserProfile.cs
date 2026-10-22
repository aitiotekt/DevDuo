#if WINDOWS
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

// One data store per process: multiple native views must share the same storage session.
// Like the loaded WebKit module, the owner reference lives until process shutdown.
internal static class WindowsBrowserProfile
{
    private static nint store;
    private static string proxyMode = "system";
    private static string? proxyEndpoint;

    internal static void ApplyProxy(string mode, string? endpoint)
    {
        if (mode is not ("system" or "direct" or "custom"))
            throw new ArgumentException("Unknown proxy mode.", nameof(mode));
        if (mode == "custom" && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "socks5") || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is < 1 or > 65535 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0))
            throw new ArgumentException("Proxy requires an unauthenticated http, https or socks5 endpoint with a valid port.", nameof(endpoint));
        if (proxyMode == mode && proxyEndpoint == endpoint) return;
        if (store != 0) SetProxy(store, mode, endpoint);
        proxyMode = mode;
        proxyEndpoint = endpoint;
    }

    private static void SetProxy(nint dataStore, string mode, string? endpoint)
    {
        if (mode == "system") WKWebsiteDataStoreEnableDefaultNetworkProxySettings(dataStore);
        else if (mode == "direct") WKWebsiteDataStoreDisableNetworkProxySettings(dataStore);
        else
        {
            var proxy = WKURLCreateWithUTF8CString(endpoint!);
            var bypass = WKStringCreateWithUTF8CString("");
            try { WKWebsiteDataStoreEnableCustomNetworkProxySettings(dataStore, proxy, bypass); }
            finally { WKRelease(proxy); WKRelease(bypass); }
        }
    }
    internal static string DirectoryPath
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("DEVDUO_WEBKIT_PROFILE");
            if (configured is { Length: > 0 })
            {
                if (!Path.IsPathFullyQualified(configured))
                    throw new InvalidOperationException("DEVDUO_WEBKIT_PROFILE must be an absolute directory.");
                return Path.GetFullPath(configured);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "WebKit");
        }
    }

    internal static nint Acquire()
    {
        if (store != 0) return WKRetain(store);
        var root = DirectoryPath;
        Directory.CreateDirectory(root);
        var configuration = WKWebsiteDataStoreConfigurationCreate();
        try
        {
            void Set(string name, Action<nint, nint> setter, bool file = false)
            {
                var path = Path.Combine(root, name);
                Directory.CreateDirectory(file ? Path.GetDirectoryName(path)! : path);
                var value = WKStringCreateWithUTF8CString(path);
                try { setter(configuration, value); }
                finally { WKRelease(value); }
            }
            Set("Storage", WKWebsiteDataStoreConfigurationSetGeneralStorageDirectory);
            Set("LocalStorage", WKWebsiteDataStoreConfigurationSetLocalStorageDirectory);
            Set("IndexedDB", WKWebsiteDataStoreConfigurationSetIndexedDBDatabaseDirectory);
            Set("NetworkCache", WKWebsiteDataStoreConfigurationSetNetworkCacheDirectory);
            Set("CacheStorage", WKWebsiteDataStoreConfigurationSetCacheStorageDirectory);
            Set("ServiceWorkers", WKWebsiteDataStoreConfigurationSetServiceWorkerRegistrationDirectory);
            Set("Cookies/cookies.db", WKWebsiteDataStoreConfigurationSetCookieStorageFile, file: true);
            store = WKWebsiteDataStoreCreateWithConfiguration(configuration);
            if (store == 0) throw new InvalidOperationException("Cannot create the persistent Windows WebKit website store.");
            SetProxy(store, proxyMode, proxyEndpoint);
            return WKRetain(store);
        }
        finally { WKRelease(configuration); }
    }
}
#endif
