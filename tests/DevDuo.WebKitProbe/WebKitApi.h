#pragma once

#include <windows.h>
#include <filesystem>
#include <stdexcept>
#include <string>

// Minimal C ABI declarations from WebKit 324423@main. The probe binds exports
// explicitly because the official runtime archive does not contain an SDK.
// All WK references below are opaque native pointers, never managed objects.
using WKRef = const void*;
using ScriptCallback = void (*)(WKRef, WKRef, void*);
using CompletionCallback = void (*)(void*);
using RepaintCallback = void (*)(WKRef, void*);
using MessageCallback = void (*)(WKRef, WKRef, const void*);
struct WKSize { double width, height; };

class WebKitApi {
public:
    explicit WebKitApi(const std::filesystem::path& directory)
    {
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        dllDirectory = AddDllDirectory(directory.c_str());
        module = LoadLibraryExW((directory / L"WebKit2.dll").c_str(), nullptr,
            LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module)
            throw std::runtime_error("Cannot load official WebKit2.dll; Win32 error " + std::to_string(GetLastError()));

#define BIND(name) name = bind<decltype(name)>(#name)
        BIND(WKContextCreate);
        BIND(WKPageConfigurationCreate);
        BIND(WKPageConfigurationSetContext);
        BIND(WKPageConfigurationSetWebsiteDataStore);
        BIND(WKPageConfigurationSetPreferences);
        BIND(WKPageConfigurationSetUserContentController);
        BIND(WKUserContentControllerCreate);
        BIND(WKUserContentControllerAddUserScript);
        BIND(WKUserContentControllerAddScriptMessageHandler);
        BIND(WKUserContentControllerRemoveAllUserMessageHandlers);
        BIND(WKUserScriptCreateWithSource);
        BIND(WKScriptMessageGetBody);
        BIND(WKCompletionListenerComplete);
        BIND(WKDictionaryGetItemForKey);
        BIND(WKDictionaryGetTypeID);
        BIND(WKArrayGetSize);
        BIND(WKArrayGetItemAtIndex);
        BIND(WKArrayGetTypeID);
        BIND(WKDoubleGetValue);
        BIND(WKDoubleGetTypeID);
        BIND(WKPreferencesCreate);
        BIND(WKPreferencesSetBoolValueForKeyForTesting);
        BIND(WKPreferencesGetBoolValueForKeyForTesting);
        BIND(WKPageConfigurationSetAllowTestOnlyIPC);
        BIND(WKWebsiteDataStoreCreateNonPersistentDataStore);
        BIND(WKViewCreate);
        BIND(WKViewGetWindow);
        BIND(WKViewGetPage);
        WKViewSetObscuredContentInsets = reinterpret_cast<decltype(WKViewSetObscuredContentInsets)>(GetProcAddress(module, "WKViewSetObscuredContentInsets"));
        WKViewGetViewportHostAPIVersion = reinterpret_cast<decltype(WKViewGetViewportHostAPIVersion)>(GetProcAddress(module, "WKViewGetViewportHostAPIVersion"));
        WKViewSetHostPresentationGeometry = reinterpret_cast<decltype(WKViewSetHostPresentationGeometry)>(GetProcAddress(module, "WKViewSetHostPresentationGeometry"));
        WKViewSetUnobscuredSizeRange = reinterpret_cast<decltype(WKViewSetUnobscuredSizeRange)>(GetProcAddress(module, "WKViewSetUnobscuredSizeRange"));
        BIND(WKViewSetIsInWindow);
        BIND(WKPageLoadURL);
        BIND(WKPageLoadHTMLString);
        BIND(WKPageSetPageZoomFactor);
        BIND(WKPageClose);
        BIND(WKPageSetObscuredContentInsetsForTesting);
        BIND(WKPageEvaluateJavaScriptInMainFrame);
        BIND(WKPageForceRepaint);
        BIND(WKPageSetBackgroundExtendsBeyondPage);
        BIND(WKPageBackgroundExtendsBeyondPage);
        BIND(WKStringCreateWithUTF8CString);
        BIND(WKStringGetMaximumUTF8CStringSize);
        BIND(WKStringGetUTF8CString);
        BIND(WKStringGetTypeID);
        BIND(WKGetTypeID);
        BIND(WKURLCreateWithUTF8CString);
        BIND(WKRelease);
#undef BIND
    }

    // WebKit and Swift process-wide state must outlive the final window callbacks.
    // Keep the module loaded until this short-lived probe process exits.
    ~WebKitApi() { if (dllDirectory) RemoveDllDirectory(dllDirectory); }
    WebKitApi(const WebKitApi&) = delete;
    WebKitApi& operator=(const WebKitApi&) = delete;

    std::string string(WKRef value) const
    {
        if (!value || WKGetTypeID(value) != WKStringGetTypeID())
            throw std::runtime_error("Expected a WebKit string result");
        std::string result(WKStringGetMaximumUTF8CStringSize(value), '\0');
        auto length = WKStringGetUTF8CString(value, result.data(), result.size());
        result.resize(length ? length - 1 : 0);
        return result;
    }

    WKRef (*WKContextCreate)() = nullptr;
    WKRef (*WKPageConfigurationCreate)() = nullptr;
    void (*WKPageConfigurationSetContext)(WKRef, WKRef) = nullptr;
    void (*WKPageConfigurationSetWebsiteDataStore)(WKRef, WKRef) = nullptr;
    void (*WKPageConfigurationSetPreferences)(WKRef, WKRef) = nullptr;
    void (*WKPageConfigurationSetUserContentController)(WKRef, WKRef) = nullptr;
    WKRef (*WKUserContentControllerCreate)() = nullptr;
    void (*WKUserContentControllerAddUserScript)(WKRef, WKRef) = nullptr;
    void (*WKUserContentControllerAddScriptMessageHandler)(WKRef, WKRef, MessageCallback, const void*) = nullptr;
    void (*WKUserContentControllerRemoveAllUserMessageHandlers)(WKRef) = nullptr;
    WKRef (*WKUserScriptCreateWithSource)(WKRef, int, bool) = nullptr;
    WKRef (*WKScriptMessageGetBody)(WKRef) = nullptr;
    void (*WKCompletionListenerComplete)(WKRef, WKRef) = nullptr;
    WKRef (*WKDictionaryGetItemForKey)(WKRef, WKRef) = nullptr;
    unsigned long long (*WKDictionaryGetTypeID)() = nullptr;
    size_t (*WKArrayGetSize)(WKRef) = nullptr;
    WKRef (*WKArrayGetItemAtIndex)(WKRef, size_t) = nullptr;
    unsigned long long (*WKArrayGetTypeID)() = nullptr;
    double (*WKDoubleGetValue)(WKRef) = nullptr;
    unsigned long long (*WKDoubleGetTypeID)() = nullptr;
    WKRef (*WKPreferencesCreate)() = nullptr;
    void (*WKPreferencesSetBoolValueForKeyForTesting)(WKRef, bool, WKRef) = nullptr;
    bool (*WKPreferencesGetBoolValueForKeyForTesting)(WKRef, WKRef) = nullptr;
    void (*WKPageConfigurationSetAllowTestOnlyIPC)(WKRef, bool) = nullptr;
    WKRef (*WKWebsiteDataStoreCreateNonPersistentDataStore)() = nullptr;
    WKRef (*WKViewCreate)(RECT, WKRef, HWND) = nullptr;
    HWND (*WKViewGetWindow)(WKRef) = nullptr;
    WKRef (*WKViewGetPage)(WKRef) = nullptr;
    bool (*WKViewSetObscuredContentInsets)(WKRef, float, float, float, float) = nullptr;
    unsigned (*WKViewGetViewportHostAPIVersion)() = nullptr;
    bool (*WKViewSetHostPresentationGeometry)(WKRef, WKSize, float) = nullptr;
    bool (*WKViewSetUnobscuredSizeRange)(WKRef, WKSize, WKSize) = nullptr;
    void (*WKViewSetIsInWindow)(WKRef, bool) = nullptr;
    void (*WKPageLoadURL)(WKRef, WKRef) = nullptr;
    void (*WKPageLoadHTMLString)(WKRef, WKRef, WKRef) = nullptr;
    void (*WKPageSetPageZoomFactor)(WKRef, double) = nullptr;
    void (*WKPageClose)(WKRef) = nullptr;
    void (*WKPageSetObscuredContentInsetsForTesting)(WKRef, float, float, float, float, void*, CompletionCallback) = nullptr;
    void (*WKPageEvaluateJavaScriptInMainFrame)(WKRef, WKRef, void*, ScriptCallback) = nullptr;
    void (*WKPageForceRepaint)(WKRef, void*, RepaintCallback) = nullptr;
    void (*WKPageSetBackgroundExtendsBeyondPage)(WKRef, bool) = nullptr;
    bool (*WKPageBackgroundExtendsBeyondPage)(WKRef) = nullptr;
    WKRef (*WKStringCreateWithUTF8CString)(const char*) = nullptr;
    size_t (*WKStringGetMaximumUTF8CStringSize)(WKRef) = nullptr;
    size_t (*WKStringGetUTF8CString)(WKRef, char*, size_t) = nullptr;
    unsigned long long (*WKStringGetTypeID)() = nullptr;
    unsigned long long (*WKGetTypeID)(WKRef) = nullptr;
    WKRef (*WKURLCreateWithUTF8CString)(const char*) = nullptr;
    void (*WKRelease)(WKRef) = nullptr;

private:
    template<typename Function> Function bind(const char* name)
    {
        auto address = GetProcAddress(module, name);
        if (!address)
            throw std::runtime_error(std::string("Required WebKit export is unavailable: ") + name);
        return reinterpret_cast<Function>(address);
    }
    HMODULE module = nullptr;
    DLL_DIRECTORY_COOKIE dllDirectory = nullptr;
};
