#include "WebKitApi.h"
#include "ContinuationSurface.h"
#include "AcceptanceProbe.h"
#include <commctrl.h>
#include <shlwapi.h>
#include <algorithm>
#include <array>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>
#include <vector>

namespace fs = std::filesystem;

namespace {
constexpr int browserWidth = 382;
constexpr int browserHeight = 678;
constexpr UINT timerId = 1;
constexpr UINT nextButton = 101;
constexpr UINT insetButton = 102;
constexpr UINT compactButton = 103;
constexpr UINT scrollButton = 104;
constexpr UINT barButton = 105;
constexpr UINT saveButton = 106;
constexpr UINT repaintButton = 107;

std::wstring wide(const std::string& text)
{
    auto count = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring result(count, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), count);
    return result;
}

std::string utf8(const std::wstring& text)
{
    auto count = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string result(count, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), result.data(), count, nullptr, nullptr);
    return result;
}

void writeNew(const fs::path& path, const std::string& value)
{
    if (fs::exists(path))
        throw std::runtime_error("Refusing to overwrite probe evidence");
    std::ofstream stream(path, std::ios::binary);
    if (!stream || !(stream << value << '\n'))
        throw std::runtime_error("Cannot write probe evidence");
}

struct Case {
    const char* name;
    int bottom;
    int scroll;
    const char* setup = "";
};
constexpr std::array cases {
    Case { "baseline-full", 0, 0 },
    Case { "bottom98", 98, 0 },
    Case { "bottom98-scrolled", 98, 200 },
    Case { "bottom80-scrolled", 80, 200 },
    Case { "bottom98-end", 98, 2400 },
    Case { "bottom98-near-end", 98, 1770 },
    Case { "bottom98-full-footer-end", 98, 2400, "probeSetFooter('full');" },
    Case { "bottom98-cleared-end", 98, 2400, "probeSetFooter('none');" },
    Case { "bottom98-narrow-end", 98, 2400, "probeSetFooter('narrow');" },
    Case { "bottom80-near-end", 80, 1752 },
    Case { "bottom80-end", 80, 2400 },
    Case { "bottom80-full-footer-end", 80, 2400, "probeSetFooter('full');" },
    Case { "bottom80-cleared-end", 80, 2400, "probeSetFooter('none');" },
    Case { "short-document", 98, 0, "probeSetRows(10);" },
    Case { "grown-document", 98, 0, "probeSetRows(120);probeSetFooter('narrow');" },
    Case { "before-growth-end", 98, 1820 },
    Case { "growth-keeps-position", 98, 1820, "probeSetRows(160);" },
    Case { "shrunk-document-end", 98, 1820, "probeSetRows(120);" },
    Case { "return-to-middle", 98, 1000 },
    Case { "reset-full", 0, 0 },
    Case { "bottom98-live", 98, 0 },
};

class Probe {
public:
    Probe(fs::path runtime, fs::path fixture, fs::path output, bool hold, bool compositing, bool background, bool isolation, bool initialInset, bool raw)
        : api(runtime), fixture(std::move(fixture)), output(std::move(output)), hold(hold),
          forceCompositing(compositing), extendBackground(background), siteIsolation(isolation), startInset(initialInset), raw(raw)
    {
        if (startInset) caseIndex = 1;
    }

    int run()
    {
        if (!fs::is_regular_file(fixture))
            throw std::runtime_error("Probe fixture is missing");
        if (!fs::create_directories(output))
            throw std::runtime_error("Probe output must be a new directory");
        INITCOMMONCONTROLSEX controls { sizeof(controls), ICC_WIN95_CLASSES };
        InitCommonControlsEx(&controls);
        WNDCLASSW type { };
        type.lpfnWndProc = windowProc;
        type.hInstance = GetModuleHandleW(nullptr);
        type.lpszClassName = L"DevDuoWebKitProbe";
        type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        type.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        RegisterClassW(&type);
        window = CreateWindowExW(0, type.lpszClassName, L"DevDuo official Windows WebKit inset probe",
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, CW_USEDEFAULT, CW_USEDEFAULT, 900, 800,
            nullptr, nullptr, type.hInstance, this);
        if (!window)
            throw std::runtime_error("Cannot create probe window");
        dpi = GetDpiForWindow(window);
        RECT work { };
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
        auto width = std::min(px(900), static_cast<int>(work.right - work.left));
        auto height = std::min(px(790), static_cast<int>(work.bottom - work.top));
        SetWindowPos(window, nullptr, work.left, work.top, width, height, SWP_NOZORDER);
        createControls();
        createBrowser();
        ShowWindow(window, SW_SHOW);
        UpdateWindow(window);
        started = GetTickCount64();
        SetTimer(window, timerId, 100, nullptr);
        MSG message { };
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        return failed || !allLayoutsPassed ? 1 : 0;
    }

private:
    int px(int dip) const { return MulDiv(dip, static_cast<int>(dpi), 96); }

    void createControls()
    {
        NONCLIENTMETRICSW metrics { sizeof(metrics) };
        if (!SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0, dpi))
            throw std::runtime_error("Cannot read native UI font metrics");
        font = CreateFontIndirectW(&metrics.lfMessageFont);
        struct Button { const wchar_t* title; UINT id; int x; int width; };
        for (const auto& button : std::array {
            Button { L"Full frame", nextButton, 12, 100 },
            Button { L"Inset 98", insetButton, 120, 100 },
            Button { L"Inset 80", compactButton, 228, 100 },
            Button { L"Scroll +200", scrollButton, 336, 112 },
            Button { L"Toggle native bar", barButton, 456, 150 },
            Button { L"Save observation", saveButton, 614, 150 } }) {
            auto control = CreateWindowExW(0, L"BUTTON", button.title, WS_CHILD | WS_VISIBLE | WS_TABSTOP,
                px(button.x), px(12), px(button.width), px(30), window,
                reinterpret_cast<HMENU>(static_cast<UINT_PTR>(button.id)), nullptr, nullptr);
            SendMessageW(control, WM_SETFONT, reinterpret_cast<WPARAM>(font), true);
        }
        auto repaint = CreateWindowExW(0, L"BUTTON", L"Native repaint", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            px(772), px(12), px(106), px(30), window,
            reinterpret_cast<HMENU>(static_cast<UINT_PTR>(repaintButton)), nullptr, nullptr);
        SendMessageW(repaint, WM_SETFONT, reinterpret_cast<WPARAM>(font), true);
        status = CreateWindowExW(0, L"STATIC", L"Loading the local numbered-row fixture...", WS_CHILD | WS_VISIBLE,
            px(410), px(70), px(450), px(650), window, nullptr, nullptr, nullptr);
        SendMessageW(status, WM_SETFONT, reinterpret_cast<WPARAM>(font), true);
    }

    void createBrowser()
    {
        context = api.WKContextCreate();
        configuration = api.WKPageConfigurationCreate();
        dataStore = api.WKWebsiteDataStoreCreateNonPersistentDataStore();
        preferences = api.WKPreferencesCreate();
        auto key = api.WKStringCreateWithUTF8CString("ForceCompositingMode");
        api.WKPreferencesSetBoolValueForKeyForTesting(preferences, forceCompositing, key);
        if (api.WKPreferencesGetBoolValueForKeyForTesting(preferences, key) != forceCompositing)
            throw std::runtime_error("ForceCompositingMode preference was not applied");
        api.WKRelease(key);
        key = api.WKStringCreateWithUTF8CString("SiteIsolationEnabled");
        api.WKPreferencesSetBoolValueForKeyForTesting(preferences, siteIsolation, key);
        if (api.WKPreferencesGetBoolValueForKeyForTesting(preferences, key) != siteIsolation)
            throw std::runtime_error("SiteIsolationEnabled preference was not applied");
        api.WKRelease(key);
        api.WKPageConfigurationSetContext(configuration, context);
        api.WKPageConfigurationSetWebsiteDataStore(configuration, dataStore);
        api.WKPageConfigurationSetPreferences(configuration, preferences);
        contentController = api.WKUserContentControllerCreate();
        auto name = api.WKStringCreateWithUTF8CString("devduoShell");
        api.WKUserContentControllerAddScriptMessageHandler(contentController, name, messageReceived, this);
        api.WKRelease(name);
        auto observerPath = fixture.parent_path().parent_path().parent_path() / "src/DevDuo/WebAssets/Scripts/browser-observer.js";
        std::ifstream stream(observerPath, std::ios::binary);
        if (!stream) throw std::runtime_error("Cannot read shared read-only container observer");
        auto observer = std::string(std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>());
        auto source = api.WKStringCreateWithUTF8CString(observer.c_str());
        auto script = api.WKUserScriptCreateWithSource(source, 0, true);
        api.WKUserContentControllerAddUserScript(contentController, script);
        api.WKPageConfigurationSetUserContentController(configuration, contentController);
        api.WKRelease(script);
        api.WKRelease(source);
        // This isolated fixture explicitly enables the test-only IPC endpoint.
        api.WKPageConfigurationSetAllowTestOnlyIPC(configuration, true);
        RECT rect { px(12), px(64), px(12 + browserWidth), px(64 + browserHeight) };
        view = api.WKViewCreate(rect, configuration, window);
        if (!view)
            throw std::runtime_error("WKViewCreate returned null");
        browser = api.WKViewGetWindow(view);
        page = api.WKViewGetPage(view);
        api.WKPageSetBackgroundExtendsBeyondPage(page, extendBackground);
        api.WKViewSetIsInWindow(view, true);
        SetWindowPos(browser, nullptr, px(12), px(64), px(browserWidth), px(browserHeight), SWP_NOZORDER | SWP_SHOWWINDOW);
        nativeBar = CreateWindowExW(0, L"STATIC", L"Native address bar (probe)",
            WS_CHILD | SS_CENTER | SS_CENTERIMAGE, px(102), px(664), px(270), px(38),
            window, nullptr, nullptr, nullptr);
        SendMessageW(nativeBar, WM_SETFONT, reinterpret_cast<WPARAM>(font), true);
        auto region = CreateRoundRectRgn(0, 0, px(270), px(38), px(38), px(38));
        if (!SetWindowRgn(nativeBar, region, true)) DeleteObject(region);
        surface = std::make_unique<ContinuationSurface>(api, browser, nativeBar, !raw);
        if (startInset) {
            surface->setInset(98);
            busy = true;
            api.WKPageSetObscuredContentInsetsForTesting(page, 0, 0, 98, 0, this, [](void* data) {
                auto& probe = *static_cast<Probe*>(data);
                if (probe.disposed) return;
                probe.busy = false;
                probe.navigate();
            });
        } else navigate();
    }

    void navigate()
    {
        wchar_t fileUrl[32768] { };
        DWORD length = static_cast<DWORD>(std::size(fileUrl));
        if (FAILED(UrlCreateFromPathW(fs::absolute(fixture).c_str(), fileUrl, &length, 0)))
            throw std::runtime_error("Cannot create a local fixture URL");
        auto url = api.WKURLCreateWithUTF8CString(utf8(fileUrl).c_str());
        api.WKPageLoadURL(page, url);
        api.WKRelease(url);
    }

    static void messageReceived(WKRef message, WKRef reply, const void* data)
    {
        auto& probe = *const_cast<Probe*>(static_cast<const Probe*>(data));
        try {
            if (!probe.disposed && probe.surface)
                probe.surface->observe(probe.api.WKScriptMessageGetBody(message));
        } catch (const std::exception& exception) { probe.fail(exception.what()); }
        auto response = probe.api.WKStringCreateWithUTF8CString("observed");
        probe.api.WKCompletionListenerComplete(reply, response);
        probe.api.WKRelease(response);
    }

    void evaluate(const std::string& source, ScriptCallback callback)
    {
        if (disposed) return;
        auto script = api.WKStringCreateWithUTF8CString(source.c_str());
        busy = true;
        api.WKPageEvaluateJavaScriptInMainFrame(page, script, this, callback);
        api.WKRelease(script);
    }

    static void ready(WKRef result, WKRef error, void* data)
    {
        auto& probe = *static_cast<Probe*>(data);
        if (probe.disposed) return;
        probe.busy = false;
        try {
            if (!error && result && probe.api.string(result) == "ready") {
                probe.loaded = true;
                probe.beginCase();
            }
        } catch (const std::exception& exception) { probe.fail(exception.what()); }
    }

    void beginCase()
    {
        active = cases[caseIndex].name;
        bottom = cases[caseIndex].bottom;
        surface->setInset(bottom);
        busy = true;
        api.WKPageSetObscuredContentInsetsForTesting(page, 0, 0, static_cast<float>(bottom), 0, this,
            [](void* data) {
                auto& probe = *static_cast<Probe*>(data);
                if (probe.disposed) return;
                probe.evaluate(std::string(cases[probe.caseIndex].setup) + "scrollTo(0," + std::to_string(cases[probe.caseIndex].scroll) + "); 'scrolled'",
                    [](WKRef, WKRef error, void* context) {
                        auto& current = *static_cast<Probe*>(context);
                        if (current.disposed) return;
                        current.busy = false;
                        if (error) { current.fail("Scroll command failed"); return; }
                        current.observeAfter = GetTickCount64() + 600;
                    });
            });
        ShowWindow(nativeBar, bottom ? SW_SHOW : SW_HIDE);
        SetWindowPos(nativeBar, HWND_TOP, px(102), px(64 + browserHeight - bottom + 20), px(270), px(38), SWP_NOACTIVATE);
    }

    void observe()
    {
        observeAfter = 0;
        auto expectedHeight = browserHeight - bottom;
        auto script = "(() => { const m = probeMetrics(); const ok = m.innerWidth === 382 && m.innerHeight === " +
            std::to_string(expectedHeight) + " && m.clientWidth === 382 && m.clientHeight === " +
            std::to_string(expectedHeight) + " && (!m.fixedVisible || Math.abs(m.fixed.bottom - " + std::to_string(expectedHeight) +
            ") < 1); return (ok ? 'PASS:' : 'FAIL:') + JSON.stringify(m); })()";
        evaluate(script, observed);
    }

    static void observed(WKRef result, WKRef error, void* data)
    {
        auto& probe = *static_cast<Probe*>(data);
        if (probe.disposed) return;
        probe.busy = false;
        try {
            if (error || !result)
                throw std::runtime_error("WebKit observation failed");
            auto value = probe.api.string(result);
            auto passed = value.starts_with("PASS:");
            if (!passed && !value.starts_with("FAIL:"))
                throw std::runtime_error("Unexpected observation result format");
            auto metrics = value.substr(5);
            auto name = probe.active;
            if (!probe.automatic) name += "-manual-" + std::to_string(++probe.manualIndex);
            writeNew(probe.output / (name + ".json"), metrics);
            RECT native { };
            GetClientRect(probe.browser, &native);
            std::ostringstream record;
            record << "{\"case\":\"" << name << "\",\"dpi\":" << probe.dpi
                << ",\"nativeWidthPx\":" << native.right << ",\"nativeHeightPx\":" << native.bottom
                << ",\"requestedBottomInset\":" << probe.bottom
                << ",\"backgroundExtendsBeyondPage\":" << (probe.api.WKPageBackgroundExtendsBeyondPage(probe.page) ? "true" : "false")
                << ",\"forceCompositingModePreference\":" << (probe.forceCompositing ? "true" : "false")
                << ",\"siteIsolationPreference\":" << (probe.siteIsolation ? "true" : "false")
                << ",\"insetBeforeNavigation\":" << (probe.startInset ? "true" : "false")
                << ",\"layoutChecksPassed\":" << (passed ? "true" : "false")
                << ",\"continuationSurface\":" << probe.surface->json() << '}';
            writeNew(probe.output / (name + "-native.json"), record.str());
            std::cout << (passed ? "PASS " : "FAIL ") << name << ": " << metrics << std::endl;
            probe.allLayoutsPassed = probe.allLayoutsPassed && passed;
            SetWindowTextW(probe.status, wide(name + "\n\n" + record.str() + "\n\n" + metrics +
                "\n\nThe magenta fixed marker must end at the real viewport. Numbered rows and LIVE animation must continue beneath it.\n\nSnapshots are not rendered by this probe.").c_str());
            if (probe.automatic) {
                ++probe.caseIndex;
                if (probe.startInset && probe.caseIndex < cases.size() && cases[probe.caseIndex].bottom == 0)
                    ++probe.caseIndex;
                if (probe.caseIndex < cases.size()) probe.beginCase();
                else {
                    probe.automatic = false;
                    writeNew(probe.output / "layout-result.json", std::string("{\"layoutChecksPassed\":") +
                        (probe.allLayoutsPassed ? "true" : "false") + ",\"nativeContinuationAccepted\":false,\"requiresPresentedWindowObservation\":true}");
                    if (!probe.hold) PostMessageW(probe.window, WM_CLOSE, 0, 0);
                }
            }
        } catch (const std::exception& exception) { probe.fail(exception.what()); }
    }

    void tick()
    {
        if (disposed) return;
        if (!loaded && GetTickCount64() - started > 30000) {
            fail("Local fixture initialization timed out");
            return;
        }
        if (busy) return;
        if (!loaded) {
            evaluate("typeof probeMetrics === 'function' ? 'ready' : 'loading'", ready);
        } else if (observeAfter && GetTickCount64() >= observeAfter)
            observe();
    }

    void command(UINT id)
    {
        if (automatic || busy || !loaded) return;
        if (id == barButton) { ShowWindow(nativeBar, IsWindowVisible(nativeBar) ? SW_HIDE : SW_SHOW); return; }
        if (id == scrollButton) {
            evaluate("scrollBy(0,200); 'scrolled'", [](WKRef, WKRef, void* data) {
                auto& probe = *static_cast<Probe*>(data);
                if (probe.disposed) return;
                probe.busy = false;
                probe.observeAfter = GetTickCount64() + 400;
            });
            return;
        }
        if (id == saveButton) { observe(); return; }
        if (id == repaintButton) {
            busy = true;
            api.WKPageForceRepaint(page, this, [](WKRef error, void* data) {
                auto& probe = *static_cast<Probe*>(data);
                if (probe.disposed) return;
                probe.busy = false;
                if (error) { probe.fail("Native repaint failed"); return; }
                InvalidateRect(probe.browser, nullptr, false);
                probe.observeAfter = GetTickCount64() + 400;
            });
            return;
        }
        caseIndex = id == insetButton ? 1 : id == compactButton ? 3 : 0;
        beginCase();
    }

    void fail(const std::string& message)
    {
        failed = true;
        std::cerr << message << std::endl;
        if (!fs::exists(output / "failure.txt")) writeNew(output / "failure.txt", message);
        if (window) PostMessageW(window, WM_CLOSE, 0, 0);
    }

    void dispose()
    {
        if (disposed) return;
        disposed = true;
        KillTimer(window, timerId);
        if (contentController) api.WKUserContentControllerRemoveAllUserMessageHandlers(contentController);
        // WM_DESTROY closes WebKit's page and removes native event observers.
        // Keep WKView alive until that HWND has finished dispatching messages.
        if (browser && IsWindow(browser)) DestroyWindow(browser);
        else if (page) api.WKPageClose(page);
        browser = nullptr;
        surface.reset();
        if (view) api.WKRelease(view);
        if (configuration) api.WKRelease(configuration);
        if (preferences) api.WKRelease(preferences);
        if (dataStore) api.WKRelease(dataStore);
        if (context) api.WKRelease(context);
        if (contentController) api.WKRelease(contentController);
        page = view = configuration = dataStore = context = preferences = nullptr;
        if (font) DeleteObject(font);
    }

    static LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        auto* probe = reinterpret_cast<Probe*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            probe = static_cast<Probe*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(probe));
        }
        if (probe) {
            if (message == WM_TIMER) { probe->tick(); return 0; }
            if (message == WM_COMMAND) { probe->command(LOWORD(wParam)); return 0; }
            if (message == WM_CLOSE) { probe->dispose(); DestroyWindow(window); return 0; }
            if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
            if (message == WM_CTLCOLORSTATIC && reinterpret_cast<HWND>(lParam) == probe->nativeBar) {
                SetBkColor(reinterpret_cast<HDC>(wParam), RGB(220, 224, 232));
                return reinterpret_cast<LRESULT>(GetStockObject(LTGRAY_BRUSH));
            }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    WebKitApi api;
    fs::path fixture, output;
    bool hold, forceCompositing, extendBackground, siteIsolation, startInset, raw;
    bool loaded = false, busy = false, disposed = false, failed = false;
    bool automatic = true, allLayoutsPassed = true;
    UINT dpi = 96;
    HWND window = nullptr, browser = nullptr, nativeBar = nullptr, status = nullptr;
    HFONT font = nullptr;
    WKRef context = nullptr, configuration = nullptr, dataStore = nullptr, view = nullptr, page = nullptr;
    WKRef preferences = nullptr;
    WKRef contentController = nullptr;
    std::unique_ptr<ContinuationSurface> surface;
    ULONGLONG started = 0, observeAfter = 0;
    size_t caseIndex = 0;
    int bottom = 0, manualIndex = 0;
    std::string active;
};
}

int wmain(int argc, wchar_t** argv)
{
    // Headless ABI validation is separate from actual layout/presentation acceptance.
    if (argc == 3 && std::wstring(argv[1]) == L"--check-host-api") {
        try {
            WebKitApi api(fs::absolute(argv[2]));
            if (!api.WKViewGetViewportHostAPIVersion || !api.WKViewSetObscuredContentInsets
                || !api.WKViewSetUnobscuredSizeRange || !api.WKViewSetHostPresentationGeometry || api.WKViewGetViewportHostAPIVersion() != 2)
                throw std::runtime_error("Host viewport API v2 missing");
            if (api.WKViewSetObscuredContentInsets(nullptr, 0, 0, 98, 0)
                || api.WKViewSetUnobscuredSizeRange(nullptr, { 382, 580 }, { 382, 598 })
                || api.WKViewSetHostPresentationGeometry(nullptr, { 466, 678 }, 1.5f))
                throw std::runtime_error("Host viewport API accepted a null view");
            std::cout << "{\"hostViewportApiVersion\":2,\"nullViewRejected\":true,\"webViewCreated\":false,\"nativeLayoutAccepted\":false}" << std::endl;
            return 0;
        } catch (const std::exception& error) {
            std::cerr << error.what() << std::endl;
            return 1;
        }
    }
    if (argc >= 5 && std::wstring(argv[1]) == L"--acceptance") {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        OleInitialize(nullptr);
        try {
            bool hostApi = false, hold = false;
            for (int index = 5; index < argc; ++index) {
                if (std::wstring(argv[index]) == L"--host-api") hostApi = true;
                else if (std::wstring(argv[index]) == L"--hold") hold = true;
                else throw std::runtime_error("Unknown acceptance option");
            }
            wchar_t executable[32768] { };
            GetModuleFileNameW(nullptr, executable, static_cast<DWORD>(std::size(executable)));
            AcceptanceProbe probe(fs::path(executable).parent_path(), fs::absolute(argv[2]), fs::absolute(argv[3]), utf8(argv[4]), hostApi, hold);
            auto result = probe.run();
            OleUninitialize(); return result;
        } catch (const std::exception& error) {
            std::cerr << error.what() << std::endl; OleUninitialize(); return 1;
        }
    }
    bool hold = false, compositing = false, background = false, isolation = false, initialInset = false, raw = false;
    for (int index = 3; index < argc; ++index) {
        auto argument = std::wstring(argv[index]);
        if (argument == L"--hold") hold = true;
        else if (argument == L"--force-compositing") compositing = true;
        else if (argument == L"--extend-background") background = true;
        else if (argument == L"--site-isolation") isolation = true;
        else if (argument == L"--start-inset") initialInset = true;
        else if (argument == L"--raw") raw = true;
        else { std::cerr << "Unknown probe option" << std::endl; return 2; }
    }
    if (argc < 3) {
        std::cerr << "Usage: DevDuo.WebKitProbe.exe FIXTURE.html NEW_OUTPUT_DIRECTORY [--hold] [--force-compositing] [--extend-background] [--site-isolation] [--start-inset]" << std::endl;
        return 2;
    }
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    OleInitialize(nullptr);
    try {
        wchar_t executable[32768] { };
        GetModuleFileNameW(nullptr, executable, static_cast<DWORD>(std::size(executable)));
        Probe probe(fs::path(executable).parent_path(), fs::absolute(argv[1]), fs::absolute(argv[2]), hold, compositing, background, isolation, initialInset, raw);
        auto result = probe.run();
        OleUninitialize();
        return result;
    } catch (const std::exception& exception) {
        std::cerr << exception.what() << std::endl;
        OleUninitialize();
        return 1;
    }
}
