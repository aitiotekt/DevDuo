#pragma once

#include "WebKitApi.h"
#include <commctrl.h>
#include <algorithm>
#include <cmath>
#include <optional>
#include <sstream>

// Passive native color surface above the browser child, below its address bar.
// WebKit still draws every valid flow pixel; only the region beyond the document
// is filled. Full-width fixed-footer color extension is a separate approximation.
class ContinuationSurface {
public:
    ContinuationSurface(WebKitApi& api, HWND window, HWND addressBar, bool enabled)
        : api(api), window(window), addressBar(addressBar), enabled(enabled)
    {
        WNDCLASSW type { };
        type.lpfnWndProc = windowProc;
        type.hInstance = GetModuleHandleW(nullptr);
        type.lpszClassName = L"DevDuoContinuationBackground";
        if (!RegisterClassW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            throw std::runtime_error("Cannot register native continuation color surface");
        background = CreateWindowExW(WS_EX_NOACTIVATE, type.lpszClassName, L"",
            WS_CHILD | WS_CLIPSIBLINGS, 0, 0, 0, 0, GetParent(window), nullptr, type.hInstance, this);
        if (!background) throw std::runtime_error("Cannot create native continuation color surface");
    }
    ~ContinuationSurface() { if (IsWindow(background)) DestroyWindow(background); }

    void setInset(int value) { setInsets(0, 0, value, 0); }

    void setInsets(int top, int right, int bottom, int left)
    {
        if (top < 0 || right < 0 || bottom < 0 || left < 0)
            throw std::runtime_error("Insets must be nonnegative");
        insetTop = top; insetRight = right; insetBottom = bottom; insetLeft = left;
        configurationDirty = true;
        ShowWindow(background, SW_HIDE);
    }

    void observe(WKRef body)
    {
        if (!body || api.WKGetTypeID(body) != api.WKDictionaryGetTypeID())
            throw std::runtime_error("Continuation observation must be a dictionary");
        auto height = number(item(body, "viewportHeight"));
        auto width = number(item(body, "viewportWidth"));
        auto documentHeight = number(item(body, "documentHeight"));
        auto documentWidth = number(item(body, "documentWidth"));
        auto scroll = number(item(body, "scrollY"));
        auto scrollX = number(item(body, "scrollX"));
        auto scale = number(item(body, "visualScale"));
        auto pixelScale = number(item(body, "devicePixelRatio")) * scale;
        if (height <= 0 || width <= 0 || std::abs(scale - 1) > .001)
            throw std::runtime_error("Unsupported viewport transform for continuation correction");
        RECT client { };
        GetClientRect(window, &client);
        // WebKit includes page zoom in its actual DPR. Native frame rounding
        // can add a pixel; do not stretch CSS coordinates to absorb that pixel.
        if (pixelScale <= 0 || std::abs(client.bottom - (height + insetTop + insetBottom) * pixelScale) > 2
            || std::abs(client.right - (width + insetLeft + insetRight) * pixelScale) > 2)
            return; // Ignore an older inset or zoom observation.
        auto scaleY = pixelScale;
        auto scaleX = pixelScale;
        validLength = std::clamp(documentHeight - scroll - height, 0.0, static_cast<double>(insetBottom));
        auto footerColor = color(item(body, "fixedBottomColor"));
        auto canvasColor = color(item(body, "underPageBackgroundFallback"));
        if (!canvasColor) throw std::runtime_error("Missing explicit page-background fallback");
        auto nextColor = *canvasColor;
        footer = footerColor.has_value();
        auto nextStart = insetTop + height + (footer ? 0 : validLength);
        // Hard paint clips use nearest-pixel edges; outward damage rounding
        // would reserve an unpainted pixel between page and background.
        RECT next { 0, static_cast<LONG>(std::lround(nextStart * scaleY)), client.right, client.bottom };
        RECT live {
            static_cast<LONG>(std::lround((insetLeft - std::clamp(scrollX, 0.0, static_cast<double>(insetLeft))) * scaleX)),
            static_cast<LONG>(std::lround((insetTop - std::clamp(scroll, 0.0, static_cast<double>(insetTop))) * scaleY)),
            static_cast<LONG>(std::lround((insetLeft + width + std::clamp(documentWidth - scrollX - width, 0.0, static_cast<double>(insetRight))) * scaleX)),
            static_cast<LONG>(std::lround((insetTop + height + validLength) * scaleY)) };
        RECT nextFooter { static_cast<LONG>(std::lround(insetLeft * scaleX)),
            static_cast<LONG>(std::lround((insetTop + height) * scaleY)),
            static_cast<LONG>(std::lround((insetLeft + width) * scaleX)), client.bottom };
        bool changed = !EqualRect(&validFlow, &live) || !EqualRect(&footerFill, &nextFooter)
            || fillColor != nextColor || footerRgb != footerColor.value_or(nextColor)
            || footerWasVisible != footer || configurationDirty;
        validFlow = live;
        footerFill = nextFooter;
        footerRgb = footerColor.value_or(nextColor);
        footerWasVisible = footer;
        configurationDirty = false;
        fill = next;
        fillStart = nextStart;
        fillColor = nextColor;
        ++observations;
        if (enabled && changed) {
            // Parent paint alone cannot erase an opaque browser child. A native
            // sibling covers exactly the out-of-document tail in the final scene.
            auto region = CreateRectRgnIndirect(&client);
            auto flowRegion = CreateRectRgnIndirect(&validFlow);
            CombineRgn(region, region, flowRegion, RGN_DIFF);
            DeleteObject(flowRegion);
            if (footer) {
                auto footerRegion = CreateRectRgnIndirect(&footerFill);
                CombineRgn(region, region, footerRegion, RGN_OR);
                DeleteObject(footerRegion);
            }
            RECT bounds { };
            auto empty = GetRgnBox(region, &bounds) == NULLREGION;
            if (!SetWindowRgn(background, region, false)) {
                DeleteObject(region);
                throw std::runtime_error("Cannot apply continuation fill region");
            }
            POINT origin { };
            MapWindowPoints(window, GetParent(window), &origin, 1);
            if (empty) ShowWindow(background, SW_HIDE);
            else {
                SetWindowPos(background, HWND_TOP, origin.x, origin.y,
                    client.right, client.bottom, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                if (addressBar) SetWindowPos(addressBar, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                InvalidateRect(background, nullptr, false);
            }
            InvalidateRect(window, nullptr, false);
            if (addressBar) InvalidateRect(addressBar, nullptr, true);
        }
    }

    std::string json() const
    {
        RECT frame { }, browserFrame { };
        GetWindowRect(background, &frame);
        GetWindowRect(window, &browserFrame);
        std::ostringstream record;
        record << "{\"backend\":\"passive native solid-color sibling above browser\",\"enabled\":" << (enabled ? "true" : "false")
            << ",\"validFlowLengthCss\":" << validLength << ",\"fillStartCss\":" << fillStart
            << ",\"fillTopPx\":" << fill.top << ",\"fillBottomPx\":" << fill.bottom
            << ",\"validFlowRectPx\":[" << validFlow.left << ',' << validFlow.top << ',' << validFlow.right << ',' << validFlow.bottom << ']'
            << ",\"surfaceVisible\":" << (IsWindowVisible(background) ? "true" : "false")
            << ",\"surfaceWindowPx\":[" << frame.left << ',' << frame.top << ',' << frame.right << ',' << frame.bottom << ']'
            << ",\"browserWindowPx\":[" << browserFrame.left << ',' << browserFrame.top << ',' << browserFrame.right << ',' << browserFrame.bottom << ']'
            << ",\"fillRgb\":[" << static_cast<int>(GetRValue(fillColor)) << ','
            << static_cast<int>(GetGValue(fillColor)) << ',' << static_cast<int>(GetBValue(fillColor))
            << "],\"fixedFooterColorApproximation\":" << (footer ? "true" : "false")
            << ",\"backgroundSource\":\"read-only opaque html/body CSS fallback; otherwise white\""
            << ",\"observations\":" << observations << ",\"paintCount\":" << paints << '}';
        return record.str();
    }

private:
    WKRef item(WKRef dictionary, const char* name)
    {
        auto key = api.WKStringCreateWithUTF8CString(name);
        auto result = api.WKDictionaryGetItemForKey(dictionary, key);
        api.WKRelease(key);
        return result;
    }
    double number(WKRef value)
    {
        if (!value || api.WKGetTypeID(value) != api.WKDoubleGetTypeID())
            throw std::runtime_error("Continuation metric must be a JavaScript number");
        auto result = api.WKDoubleGetValue(value);
        if (!std::isfinite(result)) throw std::runtime_error("Nonfinite continuation metric");
        return result;
    }
    std::optional<COLORREF> color(WKRef value)
    {
        if (!value || api.WKGetTypeID(value) != api.WKArrayGetTypeID()) return std::nullopt;
        if (api.WKArrayGetSize(value) != 3) throw std::runtime_error("Invalid observed RGB array");
        int channels[3];
        for (size_t index = 0; index < 3; ++index) {
            auto channel = number(api.WKArrayGetItemAtIndex(value, index));
            if (channel < 0 || channel > 255) throw std::runtime_error("Observed RGB outside byte range");
            channels[index] = static_cast<int>(channel);
        }
        return RGB(channels[0], channels[1], channels[2]);
    }
    void paint(HWND target)
    {
        PAINTSTRUCT paint { };
        auto dc = BeginPaint(target, &paint);
        RECT client { };
        GetClientRect(target, &client);
        auto brush = CreateSolidBrush(fillColor);
        FillRect(dc, &client, brush);
        DeleteObject(brush);
        if (footer) {
            brush = CreateSolidBrush(footerRgb);
            FillRect(dc, &footerFill, brush);
            DeleteObject(brush);
        }
        EndPaint(target, &paint);
        ++paints;
    }
    static LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
    {
        auto* surface = reinterpret_cast<ContinuationSurface*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            surface = static_cast<ContinuationSurface*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(surface));
        }
        if (message == WM_NCHITTEST) return HTTRANSPARENT;
        if (message == WM_PAINT && surface) { surface->paint(window); return 0; }
        return DefWindowProcW(window, message, wParam, lParam);
    }
    WebKitApi& api;
    HWND window, addressBar, background = nullptr;
    bool enabled, footer = false;
    int insetTop = 0, insetRight = 0, insetBottom = 0, insetLeft = 0;
    RECT fill { }, validFlow { }, footerFill { };
    COLORREF fillColor = RGB(255, 255, 255);
    COLORREF footerRgb = RGB(255, 255, 255);
    bool footerWasVisible = false;
    bool configurationDirty = true;
    double validLength = 0, fillStart = 0;
    unsigned long long observations = 0, paints = 0;
};
