#pragma once

#include "ContinuationSurface.h"
#include <array>
#include <fstream>
#include <iostream>
#include <memory>
#include <vector>

// Independent ordinary-page matrix. Each extended drawing frame is compared
// with a smaller native frame having exactly the same real layout viewport.
class AcceptanceProbe {
public:
    AcceptanceProbe(const std::filesystem::path& runtime, std::filesystem::path repository,
        std::filesystem::path output, std::string baseUrl, bool hostApi, bool hold)
        : api(runtime), repository(std::move(repository)), output(std::move(output)),
          baseUrl(std::move(baseUrl)), hostApi(hostApi), hold(hold) { }

    ~AcceptanceProbe() { dispose(); }

    int run()
    {
        if (!hostApi) scenes.resize(8);
        if (hostApi && (!api.WKViewSetObscuredContentInsets || !api.WKViewGetViewportHostAPIVersion
            || !api.WKViewSetUnobscuredSizeRange || !api.WKViewSetHostPresentationGeometry || api.WKViewGetViewportHostAPIVersion() != 2))
            throw std::runtime_error("Host viewport API v2 missing; a source-built runtime is required. Testing IPC remains disabled.");
        if (!std::filesystem::create_directories(output)) throw std::runtime_error("Output must be new");
        measureScript = read(repository / "tests/DevDuo.WebKitProbe/acceptance-observer.js");
        WNDCLASSW type { };
        type.lpfnWndProc = windowProc;
        type.hInstance = GetModuleHandleW(nullptr);
        type.lpszClassName = L"DevDuoWebKitAcceptance";
        type.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        type.hbrBackground = reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1);
        if (!RegisterClassW(&type)) throw std::runtime_error("Cannot register acceptance window");
        window = CreateWindowExW(0, type.lpszClassName, L"DevDuo WebKit ordinary-page acceptance",
            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, 0, 0, 900, 800, nullptr, nullptr, type.hInstance, this);
        if (!window) throw std::runtime_error("Cannot create acceptance window");
        dpi = GetDpiForWindow(window);
        RECT work { }; SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
        SetWindowPos(window, nullptr, work.left, work.top, std::min(px(900), static_cast<int>(work.right - work.left)),
            std::min(px(790), static_cast<int>(work.bottom - work.top)), SWP_NOZORDER);
        for (int i = 0; i < 4; ++i) {
            const wchar_t* labels[] { L"Save observation", L"Top inset", L"Left inset", L"Right + bottom" };
            CreateWindowExW(0, L"BUTTON", labels[i], WS_CHILD | WS_VISIBLE,
                px(12 + i * 190), px(8), px(180), px(30), window,
                reinterpret_cast<HMENU>(static_cast<INT_PTR>(101 + i)), nullptr, nullptr);
        }
        status = CreateWindowExW(0, L"STATIC", L"Running paired native-frame comparisons...", WS_CHILD | WS_VISIBLE,
            px(500), px(70), px(370), px(600), window, nullptr, nullptr, nullptr);
        createBrowser();
        ShowWindow(window, SW_SHOW);
        nextCase();
        SetTimer(window, 1, 100, nullptr);
        MSG message { };
        while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
        return failed || !passed ? 1 : 0;
    }

private:
    struct Insets { int top, right, bottom, left; };
    struct Scene { const char* name; Insets insets; int x, y; int height = 580; double zoom = 1; bool html = false; };
    std::vector<Scene> scenes {
        { "right-bottom-initial", { 0, 84, 98, 0 }, 0, 0 },
        { "right-bottom-middle", { 0, 84, 98, 0 }, 0, 200 },
        { "left-bottom-middle", { 0, 0, 98, 84 }, 0, 200 },
        { "top-initial", { 98, 0, 0, 0 }, 0, 0 },
        { "top-middle", { 98, 0, 0, 0 }, 0, 200 },
        { "four-sides-middle", { 48, 42, 50, 42 }, 100, 200 },
        { "four-sides-end", { 48, 42, 50, 42 }, 10000, 10000 },
        { "right-bottom-end", { 0, 84, 98, 0 }, 10000, 10000 },
        { "right-bottom-compact", { 0, 84, 80, 0 }, 0, 200, 598 },
        { "half-left-bottom", { 0, 0, 98, 84 }, 0, 200, 580, .5 },
        { "half-top", { 98, 0, 0, 0 }, 0, 200, 580, .5 },
        { "three-quarter-left", { 0, 0, 98, 84 }, 0, 200, 580, .75 },
        { "three-quarter-top", { 98, 0, 0, 0 }, 0, 200, 580, .75 },
        { "fractional-right", { 0, 84, 98, 0 }, 0, 200, 580, .94 },
        { "fractional-top", { 98, 0, 0, 0 }, 0, 200, 580, .94 },
        { "html-right", { 0, 84, 98, 0 }, 0, 200, 580, 1, true },
        { "html-fractional-right", { 0, 84, 98, 0 }, 0, 200, 580, .94, true },
        { "html-three-quarter-top", { 98, 0, 0, 0 }, 0, 200, 580, .75, true },
        { "html-half-left", { 0, 0, 98, 84 }, 0, 200, 580, .5, true },
    };
    const std::vector<std::string> pages { "standards.html", "overflow.html", "quirks.html",
        "static-gradient.html", "transparent.html", "static-quirks.html", "dynamic-height.html", "live-glass.html" };
    int px(int value) const { return MulDiv(value, static_cast<int>(dpi), 96); }
    static std::string read(const std::filesystem::path& path)
    {
        std::ifstream stream(path, std::ios::binary);
        if (!stream) throw std::runtime_error("Cannot read acceptance source");
        return { std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>() };
    }
    void write(const std::string& name, const std::string& contents)
    {
        auto path = output / name;
        if (std::filesystem::exists(path)) throw std::runtime_error("Refusing to overwrite evidence");
        std::ofstream stream(path, std::ios::binary);
        if (!(stream << contents << '\n')) throw std::runtime_error("Cannot write evidence");
    }
    void createBrowser()
    {
        configuration = api.WKPageConfigurationCreate();
        dataStore = api.WKWebsiteDataStoreCreateNonPersistentDataStore();
        api.WKPageConfigurationSetWebsiteDataStore(configuration, dataStore);
        // Only the diagnostic route permits testing IPC; host mode never enables it.
        api.WKPageConfigurationSetAllowTestOnlyIPC(configuration, !hostApi);
        controller = api.WKUserContentControllerCreate();
        auto name = api.WKStringCreateWithUTF8CString("devduoShell");
        api.WKUserContentControllerAddScriptMessageHandler(controller, name, messageReceived, this);
        api.WKRelease(name);
        auto text = read(repository / "src/DevDuo/WebAssets/Scripts/browser-observer.js");
        auto source = api.WKStringCreateWithUTF8CString(text.c_str());
        auto script = api.WKUserScriptCreateWithSource(source, 0, true);
        api.WKUserContentControllerAddUserScript(controller, script);
        api.WKPageConfigurationSetUserContentController(configuration, controller);
        api.WKRelease(script); api.WKRelease(source);
        RECT frame { px(12), px(60), px(394), px(640) };
        view = api.WKViewCreate(frame, configuration, window);
        if (!view) throw std::runtime_error("WKViewCreate failed");
        browser = api.WKViewGetWindow(view); page = api.WKViewGetPage(view);
        api.WKViewSetIsInWindow(view, true);
        surface = std::make_unique<ContinuationSurface>(api, browser, nullptr, true);
        if (hostApi && !api.WKViewSetUnobscuredSizeRange(view, { 382, 580 }, { 382, 598 }))
            throw std::runtime_error("Host API rejected the real small/large viewport range");
    }
    void inset(Insets value)
    {
        currentInsets = value;
        surface->setInsets(value.top, value.right, value.bottom, value.left);
        auto& scene = scenes[sceneIndex];
        api.WKPageSetPageZoomFactor(page, hostApi ? 1 : scene.zoom);
        SetWindowPos(browser, nullptr, px(12), px(60), static_cast<int>(std::ceil((382 + value.left + value.right) * scene.zoom * dpi / 96.0)),
            static_cast<int>(std::ceil((scene.height + value.top + value.bottom) * scene.zoom * dpi / 96.0)), SWP_NOZORDER | SWP_SHOWWINDOW);
        RECT client { };
        GetClientRect(browser, &client);
        if (hostApi && !api.WKViewSetHostPresentationGeometry(view,
            { static_cast<double>(382 + value.left + value.right), static_cast<double>(scene.height + value.top + value.bottom) },
            static_cast<float>(scene.zoom * dpi / 96.0)))
            throw std::runtime_error("Host API rejected logical presentation geometry");
        // The pinned Windows Port rounds its internal view size up to integer
        // DIPs. Reserve that trailing rounding strip through real native insets
        // so it cannot enlarge the actual CSS viewport during fractional Fit.
        auto extraRight = std::ceil(client.right * 96.0 / dpi) - (382 + value.left + value.right) * scene.zoom;
        auto extraBottom = std::ceil(client.bottom * 96.0 / dpi) - (scene.height + value.top + value.bottom) * scene.zoom;
        engineInsets = { static_cast<float>(value.top * scene.zoom), static_cast<float>(value.right * scene.zoom + extraRight),
            static_cast<float>(value.bottom * scene.zoom + extraBottom), static_cast<float>(value.left * scene.zoom) };
        if (hostApi) engineInsets = { static_cast<float>(value.top), static_cast<float>(value.right), static_cast<float>(value.bottom), static_cast<float>(value.left) };
        if (hostApi) {
            if (!api.WKViewSetObscuredContentInsets(view, engineInsets[0], engineInsets[1], engineInsets[2], engineInsets[3]))
                throw std::runtime_error("Host API rejected insets");
            afterInset();
        } else api.WKPageSetObscuredContentInsetsForTesting(page, engineInsets[0], engineInsets[1], engineInsets[2], engineInsets[3], this, [](void* data) {
                auto& p = *static_cast<AcceptanceProbe*>(data); if (!p.disposed) p.afterInset();
            });
    }
    void afterInset()
    {
        busy = false;
        if (phase == 0) {
            requestedUrl = baseUrl + pages[pageIndex] + "?case=" + std::to_string(sceneIndex);
            auto url = api.WKURLCreateWithUTF8CString(requestedUrl.c_str());
            if (scenes[sceneIndex].html) {
                auto html = read(repository / "tests/DevDuo.WebKitProbe/pages" / pages[pageIndex]);
                auto source = api.WKStringCreateWithUTF8CString(html.c_str());
                api.WKPageLoadHTMLString(page, source, url);
                api.WKRelease(source);
            } else api.WKPageLoadURL(page, url);
            api.WKRelease(url);
            deadline = GetTickCount64() + 30000;
            phase = 1;
        } else if (automatic) {
            auto& scene = scenes[sceneIndex];
            evaluate("scrollTo(" + std::to_string(scene.x) + ',' + std::to_string(scene.y) + ");window.__devduoReportContainerObservation?.();'scrolled'", scrolled);
        } else evaluate("window.__devduoReportContainerObservation?.();'observed'", scrolled);
    }
    void nextCase()
    {
        busy = true;
        if (sceneIndex == scenes.size()) { sceneIndex = 0; ++pageIndex; }
        if (pageIndex == pages.size()) {
            sceneIndex = scenes.size() - 1;
            automatic = false;
            write("acceptance-result.json", std::string("{\"pairedLayoutChecksPassed\":") + (passed ? "true" : "false")
                + ",\"pairs\":" + std::to_string(pages.size() * scenes.size()) + ",\"testOnlyIPCEnabled\":" + (hostApi ? "false" : "true")
                + ",\"productionIntegrationAccepted\":false,\"requiresPresentedWindowAcceptance\":true}");
            if (!hold) PostMessageW(window, WM_CLOSE, 0, 0);
            else { busy = false; SetWindowTextW(status, L"Matrix finished. Save observation / Top / Left / Right + bottom are live native controls."); }
            return;
        }
        phase = 0;
        inset({ 0, 0, 0, 0 });
    }
    void evaluate(const std::string& source, ScriptCallback callback)
    {
        busy = true;
        auto script = api.WKStringCreateWithUTF8CString(source.c_str());
        api.WKPageEvaluateJavaScriptInMainFrame(page, script, this, callback);
        api.WKRelease(script);
    }
    static void scrolled(WKRef, WKRef error, void* data)
    {
        auto& p = *static_cast<AcceptanceProbe*>(data); if (p.disposed) return;
        if (error) { p.fail("Scrolling failed"); return; }
        p.busy = false; p.observeAfter = GetTickCount64() + 600;
    }
    static void ready(WKRef result, WKRef error, void* data)
    {
        auto& p = *static_cast<AcceptanceProbe*>(data); if (p.disposed) return;
        p.busy = false;
        if (!error && result && p.api.string(result) == "ready") { p.phase = 2; p.afterInset(); }
    }
    void observe()
    {
        observeAfter = 0;
        auto script = "(() => { window.__devduoReportContainerObservation?.(); const m = " + measureScript + ";";
        if (phase == 2) {
            auto height = std::to_string(scenes[sceneIndex].height);
            auto unitChecks = hostApi ? "&&(!Object.keys(m.units).length||(m.units.svh===580&&m.units.lvh===598&&m.units.vh===598&&m.units.dvh===" + height + "))" : "";
            script += "window.__devduoAcceptanceBaseline = m; return JSON.stringify({pass:m.inner[0]===382&&m.inner[1]===" + height + unitChecks + ",metrics:m});})()";
        }
        else script += "const b=window.__devduoAcceptanceBaseline;return JSON.stringify({pass:JSON.stringify(m)===JSON.stringify(b),metrics:m,baseline:b});})()";
        evaluate(script, measured);
    }
    static void measured(WKRef result, WKRef error, void* data)
    {
        auto& p = *static_cast<AcceptanceProbe*>(data); if (p.disposed) return;
        try {
            if (error || !result) throw std::runtime_error("Measurement evaluation failed");
            auto json = p.api.string(result);
            auto ok = json.starts_with("{\"pass\":true,");
            auto name = p.pages[p.pageIndex == p.pages.size() ? p.pages.size() - 1 : p.pageIndex] + "-" + p.scenes[p.sceneIndex].name;
            name += p.phase == 2 ? "-baseline" : "-extended";
            if (!p.automatic) name += "-manual-" + std::to_string(++p.manualIndex);
            p.write(name + ".json", json);
            RECT client { }; GetClientRect(p.browser, &client);
            auto i = p.currentInsets;
            std::ostringstream native;
            native << "{\"dpi\":" << p.dpi << ",\"nativeClientPx\":[" << client.right << ',' << client.bottom
                << "],\"insetsCss\":[" << i.top << ',' << i.right << ',' << i.bottom << ',' << i.left
                << "],\"presentationScale\":" << p.scenes[p.sceneIndex].zoom
                << ",\"pageZoomRequested\":" << (p.hostApi ? 1 : p.scenes[p.sceneIndex].zoom)
                << ",\"engineInsetsNativeDip\":[" << p.engineInsets[0] << ',' << p.engineInsets[1] << ',' << p.engineInsets[2] << ',' << p.engineInsets[3] << ']'
                << ",\"testOnlyIPCEnabled\":" << (p.hostApi ? "false" : "true")
                << ",\"surface\":" << p.surface->json() << '}';
            p.write(name + "-native.json", native.str());
            std::cout << (ok ? "PASS " : "FAIL ") << name << std::endl;
            if (p.automatic) p.passed = p.passed && ok;
            p.busy = false;
            if (p.automatic) {
                if (p.phase == 2) { p.phase = 3; p.busy = true; p.inset(p.scenes[p.sceneIndex].insets); }
                else { ++p.sceneIndex; p.nextCase(); }
            }
        } catch (const std::exception& errorValue) { p.fail(errorValue.what()); }
    }
    void tick()
    {
        if (disposed || busy) return;
        if (phase == 1) {
            if (GetTickCount64() > deadline) { fail("Ordinary page navigation timed out"); return; }
            evaluate("document.readyState==='complete'&&location.href==='" + requestedUrl + "'&&window.devduoShellObservation?'ready':'loading'", ready);
        } else if (observeAfter && GetTickCount64() >= observeAfter) observe();
    }
    static void messageReceived(WKRef message, WKRef reply, const void* data)
    {
        auto& p = *const_cast<AcceptanceProbe*>(static_cast<const AcceptanceProbe*>(data));
        try { if (!p.disposed && p.surface) p.surface->observe(p.api.WKScriptMessageGetBody(message)); }
        catch (const std::exception& error) { p.fail(error.what()); }
        p.api.WKCompletionListenerComplete(reply, nullptr);
    }
    void fail(const std::string& message)
    {
        failed = true; std::cerr << message << std::endl;
        if (!std::filesystem::exists(output / "failure.txt")) write("failure.txt", message);
        PostMessageW(window, WM_CLOSE, 0, 0);
    }
    void dispose()
    {
        if (disposed) return;
        disposed = true;
        if (window) KillTimer(window, 1);
        if (controller) api.WKUserContentControllerRemoveAllUserMessageHandlers(controller);
        surface.reset();
        if (IsWindow(browser)) DestroyWindow(browser);
        if (view) api.WKRelease(view);
        if (configuration) api.WKRelease(configuration);
        if (dataStore) api.WKRelease(dataStore);
        if (controller) api.WKRelease(controller);
    }
    static LRESULT CALLBACK windowProc(HWND target, UINT message, WPARAM wParam, LPARAM lParam)
    {
        auto* p = reinterpret_cast<AcceptanceProbe*>(GetWindowLongPtrW(target, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            p = static_cast<AcceptanceProbe*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
            SetWindowLongPtrW(target, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(p));
        }
        if (p) {
            if (message == WM_TIMER) { p->tick(); return 0; }
            if (message == WM_COMMAND && !p->automatic && !p->busy) {
                if (LOWORD(wParam) == 101) p->observe();
                else {
                    p->phase = 3; p->busy = true;
                    p->inset(LOWORD(wParam) == 102 ? Insets { 98, 0, 0, 0 }
                        : LOWORD(wParam) == 103 ? Insets { 0, 0, 98, 84 } : Insets { 0, 84, 98, 0 });
                }
                return 0;
            }
            if (message == WM_CLOSE) { p->dispose(); DestroyWindow(target); return 0; }
            if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
        }
        return DefWindowProcW(target, message, wParam, lParam);
    }
    WebKitApi api;
    std::filesystem::path repository, output;
    std::string baseUrl, measureScript, requestedUrl;
    bool hostApi, hold, disposed = false, failed = false, passed = true, busy = false, automatic = true;
    HWND window = nullptr, browser = nullptr, status = nullptr;
    UINT dpi = 96;
    WKRef configuration = nullptr, dataStore = nullptr, controller = nullptr, view = nullptr, page = nullptr;
    std::unique_ptr<ContinuationSurface> surface;
    Insets currentInsets { };
    std::array<float, 4> engineInsets { };
    size_t pageIndex = 0, sceneIndex = 0;
    int phase = 0, manualIndex = 0;
    ULONGLONG deadline = 0, observeAfter = 0;
};
