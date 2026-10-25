#include "HostCompositor.h"
#include <algorithm>
#include <cmath>
#include <d2d1_1.h>
#include <d3d11.h>
#include <dcomp.h>
#include <dwmapi.h>
#include <dwrite.h>
#include <memory>
#include <source_location>
#include <stdexcept>
#include <string>
#include <vector>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;
namespace {
thread_local std::wstring lastError;
class CompositionFailure : public std::runtime_error {
public:
    HRESULT result;
    explicit CompositionFailure(HRESULT value)
        : std::runtime_error("Windows composition operation failed")
        , result(value)
    {
    }
};
void check(HRESULT result, const std::source_location location = std::source_location::current())
{
    if (FAILED(result)) {
        wchar_t text[96];
        swprintf_s(text, L"Windows composition HRESULT 0x%08X at native line %u", static_cast<unsigned>(result),
            location.line());
        lastError = text;
        throw CompositionFailure(result);
    }
}
D2D1_COLOR_F color(unsigned value, float alpha = 1)
{
    return { ((value >> 16) & 255) / 255.f, ((value >> 8) & 255) / 255.f, (value & 255) / 255.f, alpha };
}
struct Item {
    DDChrome frame;
    std::wstring text;
};
enum Kind { Top,
    Address,
    Back,
    Bookmarks,
    TabGroup,
    SelectedTab,
    Tab,
    Camera,
    Wifi,
    Clock,
    Menu,
    Reload,
    Plus,
    Overview,
    TabTrack,
    Divider,
    CompactAddress };

bool hasGlass(int kind) { return kind <= TabGroup || kind == CompactAddress; }

// Role-dependent artistic optics; the page and foreground are separate inputs.
DDMaterial materialFor(const DDChrome& frame, DDMaterial material)
{
    const bool dark = frame.foreground == 0xffffff;
    const float luminance = (((material.tint >> 16) & 255) * .2126f + ((material.tint >> 8) & 255) * .7152f + (material.tint & 255) * .0722f) / 255;
    // Preserve the dark/light recipes at the ends, but blend through the old
    // .36 cutoff. A one-channel sampling change must not switch glass opacity.
    const float position = std::clamp((luminance - .24f) / .24f, 0.f, 1.f);
    const float lightBackdrop = position * position * (3 - 2 * position);
    const auto blend = [lightBackdrop](float onDark, float onLight) { return std::lerp(onDark, onLight, lightBackdrop); };
    const auto blendTint = [&blend](unsigned onDark, unsigned onLight) {
        unsigned result = 0;
        for (int shift : { 0, 8, 16 })
            result |= static_cast<unsigned>(std::round(blend(static_cast<float>((onDark >> shift) & 255), static_cast<float>((onLight >> shift) & 255)))) << shift;
        return result;
    };
    if (frame.kind == Top) {
        material.tint = dark ? 0x2b3c4f : 0xffffff;
        material.alpha = std::min(.95f, material.alpha * (dark ? 2.4f : 2.7f));
        material.strength = std::min(1.f, material.strength * 2);
        material.blur *= 1.2f;
        material.topHighlight = material.edgeHighlight = material.sheen = 0;
        material.shadow = 0;
    } else if (frame.kind == CompactAddress) {
        material.tint = dark ? blendTint(0x74808f, 0x657382) : 0xffffff;
        material.alpha *= dark ? blend(.65f, 2.5f) : blend(2.f, .2f);
        material.topHighlight *= .08f;
        material.edgeHighlight *= .1f;
        material.sheen = 0;
        material.shadow *= .15f;
        material.blur *= .7f;
    } else if (frame.kind <= TabGroup) {
        material.tint = dark ? blendTint(0x8b97a5, 0x657382) : 0xffffff;
        material.alpha *= dark ? blend(1.1f, 2.5f) : blend(2.f, .55f);
        material.topHighlight *= dark ? .35f : .65f;
        material.edgeHighlight *= dark ? .55f : .75f;
        material.sheen *= .5f;
    }
    return material;
}
class Compositor {
public:
    HWND browser, parent;
    LONG_PTR originalStyle;
    LONG_PTR originalWindowStyle;
    HWND originalParent;
    HWND sourceHost = nullptr;
    bool detachedSource = false;
    ComPtr<ID3D11Device> device;
    ComPtr<IDCompositionDevice3> composition;
    ComPtr<IDCompositionDevice3> pointerComposition;
    ComPtr<IDCompositionDesktopDevice> desktop;
    ComPtr<IDCompositionTarget> target;
    ComPtr<IDCompositionVisual2> root;
    ComPtr<IUnknown> page;
    ComPtr<ID2D1Factory1> factory;
    ComPtr<ID2D1Device> drawing;
    ComPtr<IDWriteFactory> textFactory;
    ComPtr<IDCompositionSurface> boundary, decorations;
    ComPtr<IDCompositionSurface> indicatorSurface;
    ComPtr<IDCompositionVisual2> indicatorVisual;
    ComPtr<IDCompositionSurface> pointerSurface;
    ComPtr<IDCompositionVisual2> pointerGroup, pointerVisual;
    float pointerDiameter = 0;
    D2D1_RECT_F indicatorRect { };
    float indicatorOpacity = 0;
    std::vector<Item> items;
    int width = 0, height = 0;
    bool softwareDevice = false;
    unsigned long long resourceGeneration = 0, sceneGeneration = 0;
    HWND presentationWindow = nullptr;
    int rotation = 0, offsetX = 0, offsetY = 0;

    Compositor(HWND owner, HWND source)
        : browser(source)
        , parent(owner)
        , originalStyle(GetWindowLongPtrW(source, GWL_EXSTYLE))
        , originalWindowStyle(GetWindowLongPtrW(source, GWL_STYLE))
        , originalParent(GetParent(source))
    {
    }
    void initialize(bool forceSoftware = false)
    {
        // The DWM redirection surface is the live source, never a captured image.
        SetWindowLongPtrW(browser, GWL_EXSTYLE, originalStyle | WS_EX_LAYERED);
        SetWindowLongPtrW(browser, GWL_STYLE, originalWindowStyle & ~WS_CLIPSIBLINGS);
        if (!SetLayeredWindowAttributes(browser, 0, 255, LWA_ALPHA))
            check(HRESULT_FROM_WIN32(GetLastError()));
        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        auto result = forceSoftware ? E_FAIL
                                    : D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
                                          D3D11_SDK_VERSION, &device, nullptr, nullptr);
        softwareDevice = forceSoftware || FAILED(result);
        if (softwareDevice)
            check(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags, nullptr, 0, D3D11_SDK_VERSION,
                &device, nullptr, nullptr));
        ComPtr<IDXGIDevice> dxgi;
        check(device.As(&dxgi));
        check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, IID_PPV_ARGS(&factory)));
        check(factory->CreateDevice(dxgi.Get(), &drawing));
        check(DCompositionCreateDevice3(drawing.Get(), IID_PPV_ARGS(&desktop)));
        check(desktop.As(&composition));
        // Cross-device child visuals retain their own commit stream. Pointer
        // motion must not submit the page/glass device at mouse-message rates.
        ComPtr<IDCompositionDesktopDevice> pointerDesktop;
        check(DCompositionCreateDevice3(drawing.Get(), IID_PPV_ARGS(&pointerDesktop)));
        check(pointerDesktop.As(&pointerComposition));
        check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(textFactory.GetAddressOf())));
        check(desktop->CreateTargetForHwnd(presentationWindow ? presentationWindow : parent, TRUE, &target));
        check(composition->CreateVisual(&root));
        check(target->SetRoot(root.Get()));
        check(desktop->CreateSurfaceFromHwnd(browser, &page));
        applyPresentation();
        ++resourceGeneration;
        lastError.clear();
    }
    void initializeWithFallback()
    {
        try {
            initialize();
        } catch (const CompositionFailure&) {
            // The fallback retains the same surface/material pipeline.
            resetResources();
            initialize(true);
        }
    }
    ~Compositor()
    {
        if (target)
            target->SetRoot(nullptr);
        if (composition)
            composition->Commit();
        if (IsWindow(browser)) {
            if (detachedSource) {
                SetWindowLongPtrW(browser, GWL_STYLE, originalWindowStyle);
                SetParent(browser, originalParent);
            }
            BOOL cloak = FALSE;
            DwmSetWindowAttribute(browser, DWMWA_CLOAK, &cloak, sizeof(cloak));
            SetWindowLongPtrW(browser, GWL_EXSTYLE, originalStyle);
            SetWindowLongPtrW(browser, GWL_STYLE, originalWindowStyle);
        }
        if (sourceHost) DestroyWindow(sourceHost);
    }
    void applyPresentation()
    {
        D2D_MATRIX_3X2_F matrix = D2D1::Matrix3x2F::Identity();
        if (rotation == 90) matrix = { 0, 1, -1, 0, static_cast<float>(height), 0 };
        if (rotation == 180) matrix = { -1, 0, 0, -1, static_cast<float>(width), static_cast<float>(height) };
        if (rotation == 270) matrix = { 0, -1, 1, 0, 0, static_cast<float>(width) };
        matrix.dx -= static_cast<float>(offsetX);
        matrix.dy -= static_cast<float>(offsetY);
        check(root->SetTransform(matrix));
        if (detachedSource) {
            SetWindowPos(sourceHost, nullptr, 0, 0, std::max(1, width), std::max(1, height), SWP_NOZORDER | SWP_NOACTIVATE);
            BOOL cloak = TRUE;
            check(DwmSetWindowAttribute(browser, DWMWA_CLOAK, &cloak, sizeof(cloak)));
        }
    }
    void setPresentation(HWND window, int degrees, int x, int y)
    {
        if (!IsWindow(window) || (degrees != 0 && degrees != 90 && degrees != 180 && degrees != 270)) check(E_INVALIDARG);
        if (presentationWindow != window) {
            check(target->SetRoot(nullptr));
            target.Reset();
            check(desktop->CreateTargetForHwnd(window, TRUE, &target));
            check(target->SetRoot(root.Get()));
            presentationWindow = window;
        }
        rotation = degrees;
        offsetX = x;
        offsetY = y;
        if (rotation != 0 && !detachedSource) {
            // A child DC is clipped by its physical ancestors before DWM receives it.
            // Keep a full-size cloaked CHILD outside that clipped tree. The owned
            // source parent has no redirection bitmap, so it contributes no pixels.
            auto owner = GetAncestor(originalParent, GA_ROOT);
            RECT bounds;
            GetClientRect(browser, &bounds);
            sourceHost = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
                L"Static", L"", WS_POPUP, 0, 0, bounds.right, bounds.bottom, owner, nullptr, GetModuleHandleW(nullptr), nullptr);
            if (!sourceHost) check(HRESULT_FROM_WIN32(GetLastError()));
            BOOL cloak = TRUE;
            check(DwmSetWindowAttribute(browser, DWMWA_CLOAK, &cloak, sizeof(cloak)));
            SetParent(browser, sourceHost);
            SetWindowPos(browser, nullptr, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            check(DwmSetWindowAttribute(browser, DWMWA_CLOAK, &cloak, sizeof(cloak)));
            ShowWindow(sourceHost, SW_SHOWNOACTIVATE);
            detachedSource = true;
        }
        if (rotation == 0 && detachedSource) {
            SetParent(browser, originalParent);
            BOOL cloak = FALSE;
            check(DwmSetWindowAttribute(browser, DWMWA_CLOAK, &cloak, sizeof(cloak)));
            DestroyWindow(sourceHost);
            sourceHost = nullptr;
            detachedSource = false;
        }
        applyPresentation();
        check(composition->Commit());
    }
    bool refresh()
    {
        if (SUCCEEDED(device->GetDeviceRemovedReason()))
            return false;
        resetResources();
        initializeWithFallback();
        return true;
    }
    void resetResources()
    {
        if (target)
            target->SetRoot(nullptr);
        if (composition)
            composition->Commit();
        decorations.Reset();
        indicatorSurface.Reset();
        indicatorVisual.Reset();
        pointerVisual.Reset();
        pointerGroup.Reset();
        pointerSurface.Reset();
        pointerComposition.Reset();
        pointerDiameter = 0;
        indicatorRect = { };
        indicatorOpacity = 0;
        boundary.Reset();
        page.Reset();
        root.Reset();
        target.Reset();
        composition.Reset();
        desktop.Reset();
        drawing.Reset();
        factory.Reset();
        textFactory.Reset();
        device.Reset();
    }
    ComPtr<IDCompositionVisual2> visual(IUnknown* content)
    {
        ComPtr<IDCompositionVisual2> result;
        check(composition->CreateVisual(&result));
        check(result->SetContent(content));
        return result;
    }
    template <typename Paint>
    void paintSurface(ComPtr<IDCompositionSurface>& surface, Paint paint)
    {
        check(composition->CreateSurface(
            width, height, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED, &surface));
        ComPtr<ID2D1DeviceContext> context;
        POINT offset;
        check(surface->BeginDraw(nullptr, IID_PPV_ARGS(&context), &offset));
        context->SetDpi(96, 96);
        context->SetTransform(
            D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
        context->Clear(D2D1::ColorF(0, 0));
        try {
            paint(context.Get());
        } catch (...) {
            surface->EndDraw();
            throw;
        }
        check(surface->EndDraw());
    }
    void addBase(IDCompositionVisual2* group)
    {
        auto source = visual(page.Get());
        check(group->AddVisual(source.Get(), FALSE, nullptr));
        auto fill = visual(boundary.Get());
        check(group->AddVisual(fill.Get(), FALSE, nullptr));
    }
    void update(
        int w, int h, const DDChrome* frames, int count, const DDFill* fills, int fillCount, DDMaterial material)
    {
        if (w < 1 || h < 1 || w > 32767 || h > 32767 || count < 0 || count > 100 || fillCount < 0 || fillCount > 16
            || (count && !frames) || (fillCount && !fills) || !std::isfinite(material.scale) || material.scale <= 0
            || !std::isfinite(material.alpha) || !std::isfinite(material.strength) || !std::isfinite(material.shadow))
            check(E_INVALIDARG);
        for (int i = 0; i < count; ++i) {
            const auto& f = frames[i];
            const float values[] = { f.x, f.y, f.width, f.height, f.visualX, f.visualY, f.visualWidth, f.visualHeight,
                f.radius, f.fontSize, f.clipWidth };
            if (f.kind < Top || f.kind > CompactAddress || f.action < 0 || f.action > 7
                || std::any_of(std::begin(values), std::end(values), [](float value) { return !std::isfinite(value); })
                || f.width < 0 || f.height < 0 || f.visualWidth < 0 || f.visualHeight < 0 || f.radius < 0
                || f.fontSize <= 0)
                check(E_INVALIDARG);
        }
        const float optical[] = { material.blur, material.saturation, material.brightness, material.contrast,
            material.topHighlight, material.edgeHighlight, material.sheen, material.shadowBlur, material.shadowOffset };
        if (std::any_of(std::begin(optical), std::end(optical), [](float value) { return !std::isfinite(value) || value < 0; })
            || material.blur > 100 || material.shadowBlur > 100 || material.saturation > 4 || material.brightness > 4 || material.contrast > 4
            || material.topHighlight > 1 || material.edgeHighlight > 1 || material.sheen > 1 || material.shadowOffset > 100)
            check(E_INVALIDARG);
        for (int i = 0; i < fillCount; ++i) {
            const auto& f = fills[i];
            if (!std::isfinite(f.x) || !std::isfinite(f.y) || !std::isfinite(f.width) || !std::isfinite(f.height)
                || f.width < 0 || f.height < 0)
                check(E_INVALIDARG);
        }
        width = w;
        height = h;
        applyPresentation();
        items.clear();
        for (int i = 0; i < count; ++i)
            items.push_back({ frames[i], frames[i].text ? frames[i].text : L"" });
        paintSurface(boundary, [&](ID2D1DeviceContext* context) {
            for (int i = 0; i < fillCount; ++i) {
                const auto& f = fills[i];
                ComPtr<ID2D1SolidColorBrush> brush;
                check(context->CreateSolidColorBrush(color(f.color), &brush));
                context->FillRectangle(D2D1::RectF(f.x, f.y, f.x + f.width, f.y + f.height), brush.Get());
            }
        });
        check(root->RemoveAllVisuals());
        addBase(root.Get());
        // Shadows have their own alpha mask. Text and icons never pass through
        // the blur or an opacity effect, so their contrast stays independent.
        ComPtr<IDCompositionSurface> shadowMask;
        paintSurface(shadowMask, [&](ID2D1DeviceContext* context) {
            ComPtr<ID2D1SolidColorBrush> brush;
            check(context->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &brush));
            for (const auto& item : items) {
                const auto& f = item.frame;
                if (f.kind == Top || !hasGlass(f.kind))
                    continue;
                const auto optics = materialFor(f, material);
                brush->SetOpacity(std::clamp(optics.shadow, 0.f, 1.f));
                const auto rect = D2D1::RectF(f.x + f.visualX, f.y + f.visualY + material.shadowOffset * material.scale,
                    f.x + f.visualX + f.visualWidth, f.y + f.visualY + f.visualHeight + material.shadowOffset * material.scale);
                context->FillRoundedRectangle(D2D1::RoundedRect(rect, f.radius, f.radius), brush.Get());
            }
        });
        auto shadow = visual(shadowMask.Get());
        ComPtr<IDCompositionGaussianBlurEffect> shadowBlur;
        check(composition->CreateGaussianBlurEffect(&shadowBlur));
        check(shadowBlur->SetStandardDeviation(material.shadowBlur * material.scale));
        check(shadow->SetEffect(shadowBlur.Get()));
        check(root->AddVisual(shadow.Get(), FALSE, nullptr));
        for (const auto& item : items) {
            const auto& f = item.frame;
            if (!hasGlass(f.kind))
                continue;
            const auto optics = materialFor(f, material);
            ComPtr<IDCompositionVisual2> clipped, blur;
            check(composition->CreateVisual(&clipped));
            check(composition->CreateVisual(&blur));
            addBase(blur.Get());
            ComPtr<IDCompositionGaussianBlurEffect> effect;
            check(composition->CreateGaussianBlurEffect(&effect));
            check(effect->SetStandardDeviation(optics.blur * material.scale));
            check(effect->SetBorderMode(D2D1_BORDER_MODE_HARD));
            // CSS-like saturation can exceed one. ColorMatrix supports this
            // without the [0,1] restriction of the saturation effect.
            ComPtr<IDCompositionColorMatrixEffect> correction;
            check(composition->CreateColorMatrixEffect(&correction));
            const float s = material.saturation, gain = material.brightness * material.contrast;
            const float lumaR = .2126f * (1 - s), lumaG = .7152f * (1 - s), lumaB = .0722f * (1 - s);
            const float offset = material.brightness * .5f * (1 - material.contrast);
            D2D1_MATRIX_5X4_F matrix = {
                (lumaR + s) * gain, lumaR * gain, lumaR * gain, 0,
                lumaG * gain, (lumaG + s) * gain, lumaG * gain, 0,
                lumaB * gain, lumaB * gain, (lumaB + s) * gain, 0,
                0, 0, 0, 1,
                offset, offset, offset, 0
            };
            check(correction->SetMatrix(matrix));
            check(correction->SetAlphaMode(D2D1_COLORMATRIX_ALPHA_MODE_PREMULTIPLIED));
            check(correction->SetClampOutput(TRUE));
            check(correction->SetInput(0, effect.Get(), 0));
            check(blur->SetEffect(correction.Get()));
            ComPtr<IDCompositionVisual3> opacityVisual;
            check(blur.As(&opacityVisual));
            check(opacityVisual->SetOpacity(std::clamp(optics.strength, 0.f, 1.f)));
            check(clipped->AddVisual(blur.Get(), FALSE, nullptr));
            ComPtr<IDCompositionRectangleClip> clip;
            check(composition->CreateRectangleClip(&clip));
            float left = f.x + f.visualX, top = f.y + f.visualY, right = left + f.visualWidth,
                  bottom = top + f.visualHeight;
            check(clip->SetLeft(left));
            check(clip->SetTop(top));
            check(clip->SetRight(right));
            check(clip->SetBottom(bottom));
            auto r = f.radius;
            check(clip->SetTopLeftRadiusX(r));
            check(clip->SetTopLeftRadiusY(r));
            check(clip->SetTopRightRadiusX(r));
            check(clip->SetTopRightRadiusY(r));
            check(clip->SetBottomLeftRadiusX(r));
            check(clip->SetBottomLeftRadiusY(r));
            check(clip->SetBottomRightRadiusX(r));
            check(clip->SetBottomRightRadiusY(r));
            check(clipped->SetClip(clip.Get()));
            check(root->AddVisual(clipped.Get(), FALSE, nullptr));
        }
        paintSurface(decorations, [&](ID2D1DeviceContext* context) {
            for (const auto& item : items)
                paintItem(context, item, material);
        });
        auto decoration = visual(decorations.Get());
        check(root->AddVisual(decoration.Get(), FALSE, nullptr));
        // A passive retained visual has no host hit rectangle or accessibility item.
        // Fade updates change this visual alone, without rebuilding glass/chrome.
        if (indicatorVisual) check(root->AddVisual(indicatorVisual.Get(), FALSE, nullptr));
        if (pointerGroup) check(root->AddVisual(pointerGroup.Get(), FALSE, nullptr));
        check(composition->Commit());
        ++sceneGeneration;
        lastError.clear();
    }
    void setTouchPointer(float x, float y, float diameter, float left, float top, float right, float bottom)
    {
        const float values[] = { x, y, diameter, left, top, right, bottom };
        if (std::any_of(std::begin(values), std::end(values), [](float value) { return !std::isfinite(value); })
            || diameter < 0 || diameter > 256 || right < left || bottom < top) check(E_INVALIDARG);
        if (!pointerGroup && diameter == 0) return;
        if (diameter > 0 && (!pointerVisual || pointerDiameter != diameter)) {
            auto extent = std::max(1, static_cast<int>(std::ceil(diameter + 2)));
            check(pointerComposition->CreateSurface(extent, extent, DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE_PREMULTIPLIED, &pointerSurface));
            ComPtr<ID2D1DeviceContext> context;
            POINT offset;
            check(pointerSurface->BeginDraw(nullptr, IID_PPV_ARGS(&context), &offset));
            context->SetDpi(96, 96);
            context->SetTransform(D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
            context->Clear(D2D1::ColorF(0, 0));
            ComPtr<ID2D1SolidColorBrush> fill, rim;
            auto result = context->CreateSolidColorBrush(D2D1::ColorF(.35f, .35f, .35f, .5f), &fill);
            if (SUCCEEDED(result)) result = context->CreateSolidColorBrush(D2D1::ColorF(1, 1, 1, .85f), &rim);
            if (SUCCEEDED(result)) {
                auto radius = diameter / 2;
                auto circle = D2D1::Ellipse(D2D1::Point2F(radius + 1, radius + 1), radius, radius);
                context->FillEllipse(circle, fill.Get());
                context->DrawEllipse(circle, rim.Get(), diameter / 20);
            }
            auto endResult = pointerSurface->EndDraw();
            check(result);
            check(endResult);
            if (pointerVisual) check(pointerVisual->SetContent(pointerSurface.Get()));
            else {
                check(pointerComposition->CreateVisual(&pointerVisual));
                check(pointerVisual->SetContent(pointerSurface.Get()));
                check(pointerComposition->CreateVisual(&pointerGroup));
                check(pointerGroup->AddVisual(pointerVisual.Get(), FALSE, nullptr));
                check(root->AddVisual(pointerGroup.Get(), FALSE, nullptr));
                check(composition->Commit());
            }
            pointerDiameter = diameter;
        }
        ComPtr<IDCompositionVisual3> visibility;
        check(pointerGroup.As(&visibility));
        check(visibility->SetOpacity(diameter > 0 ? 1.f : 0.f));
        if (diameter > 0) {
            check(pointerVisual->SetOffsetX(x - diameter / 2 - 1));
            check(pointerVisual->SetOffsetY(y - diameter / 2 - 1));
            check(pointerGroup->SetClip(D2D1::RectF(left, top, right, bottom)));
        }
        check(pointerComposition->Commit());
    }
    void setScrollIndicator(float x, float y, float w, float h, float opacity)
    {
        if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(w) || !std::isfinite(h)
            || !std::isfinite(opacity) || w < 0 || h < 0 || opacity < 0 || opacity > 1)
            check(E_INVALIDARG);
        if (width < 1 || height < 1) return;
        bool resized = !indicatorVisual || indicatorRect.right - indicatorRect.left != w
            || indicatorRect.bottom - indicatorRect.top != h;
        if (!resized && indicatorRect.left == x && indicatorRect.top == y && indicatorOpacity == opacity) return;
        if (resized) {
            check(composition->CreateSurface(std::max(1, static_cast<int>(std::ceil(w))),
                std::max(1, static_cast<int>(std::ceil(h))), DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE_PREMULTIPLIED, &indicatorSurface));
            ComPtr<ID2D1DeviceContext> context;
            POINT offset;
            check(indicatorSurface->BeginDraw(nullptr, IID_PPV_ARGS(&context), &offset));
            context->SetDpi(96, 96);
            context->SetTransform(D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
            context->Clear(D2D1::ColorF(0, 0));
            ComPtr<ID2D1SolidColorBrush> brush;
            auto result = context->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &brush);
            if (SUCCEEDED(result)) context->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(0, 0, w, h), w / 2, w / 2), brush.Get());
            auto endResult = indicatorSurface->EndDraw();
            check(result);
            check(endResult);
            if (indicatorVisual) check(indicatorVisual->SetContent(indicatorSurface.Get()));
            else {
                indicatorVisual = visual(indicatorSurface.Get());
                check(root->AddVisual(indicatorVisual.Get(), FALSE, nullptr));
            }
        }
        indicatorRect = D2D1::RectF(x, y, x + w, y + h);
        check(indicatorVisual->SetOffsetX(x));
        check(indicatorVisual->SetOffsetY(y));
        ComPtr<IDCompositionVisual3> opacityVisual;
        check(indicatorVisual.As(&opacityVisual));
        check(opacityVisual->SetOpacity(opacity));
        indicatorOpacity = opacity;
        check(composition->Commit());
    }
    ComPtr<IDWriteTextFormat> textFormat(float size, bool semibold = false)
    {
        ComPtr<IDWriteTextFormat> format;
        check(textFactory->CreateTextFormat(L"Segoe UI", nullptr,
            semibold ? DWRITE_FONT_WEIGHT_SEMI_BOLD : DWRITE_FONT_WEIGHT_MEDIUM,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, L"", &format));
        check(format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER));
        check(format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER));
        return format;
    }
    float measureText(const wchar_t* text, float size)
    {
        if (!text || !std::isfinite(size) || size <= 0 || size > 1000)
            check(E_INVALIDARG);
        auto format = textFormat(size);
        ComPtr<IDWriteTextLayout> layout;
        check(textFactory->CreateTextLayout(text, static_cast<UINT32>(wcslen(text)), format.Get(), 32767, 2000, &layout));
        DWRITE_TEXT_METRICS metrics;
        check(layout->GetMetrics(&metrics));
        return metrics.widthIncludingTrailingWhitespace;
    }
    void paintGlass(ID2D1DeviceContext* context, D2D1_ROUNDED_RECT round, DDMaterial material)
    {
        ComPtr<ID2D1SolidColorBrush> tint;
        check(context->CreateSolidColorBrush(color(material.tint, std::clamp(material.alpha, 0.f, 1.f)), &tint));
        context->FillRoundedRectangle(round, tint.Get());
        const D2D1_GRADIENT_STOP stops[] = {
            { 0, D2D1::ColorF(1, 1, 1, material.topHighlight * material.strength) },
            { .25f, D2D1::ColorF(1, 1, 1, material.sheen * material.strength) },
            { .65f, D2D1::ColorF(1, 1, 1, 0) },
            { 1, D2D1::ColorF(0, 0, 0, .06f * material.strength) }
        };
        ComPtr<ID2D1GradientStopCollection> collection;
        check(context->CreateGradientStopCollection(stops, ARRAYSIZE(stops), &collection));
        ComPtr<ID2D1LinearGradientBrush> highlight;
        check(context->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
                                                     { round.rect.left, round.rect.top }, { round.rect.left, round.rect.bottom }),
            collection.Get(), &highlight));
        context->FillRoundedRectangle(round, highlight.Get());
        const D2D1_GRADIENT_STOP rimStops[] = {
            { 0, D2D1::ColorF(1, 1, 1, material.edgeHighlight) },
            { .5f, D2D1::ColorF(1, 1, 1, material.edgeHighlight * .3f) },
            { 1, D2D1::ColorF(1, 1, 1, material.edgeHighlight * .65f) }
        };
        check(context->CreateGradientStopCollection(rimStops, ARRAYSIZE(rimStops), &collection));
        check(context->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
                                                     { round.rect.left, round.rect.top }, { round.rect.left, round.rect.bottom }),
            collection.Get(), &highlight));
        context->DrawRoundedRectangle(round, highlight.Get(), .5f * material.scale);
    }
    void paintItem(ID2D1DeviceContext* context, const Item& item, DDMaterial material)
    {
        const auto& f = item.frame;
        material = materialFor(f, material);
        auto rect = D2D1::RectF(
            f.x + f.visualX, f.y + f.visualY, f.x + f.visualX + f.visualWidth, f.y + f.visualY + f.visualHeight);
        auto round = D2D1::RoundedRect(rect, f.radius, f.radius);
        ComPtr<ID2D1SolidColorBrush> white, edge, ink;
        check(context->CreateSolidColorBrush(D2D1::ColorF(1, 1, 1), &white));
        check(context->CreateSolidColorBrush(color(f.foreground, .18f), &edge));
        check(context->CreateSolidColorBrush(color(f.foreground), &ink));
        if (f.kind == SelectedTab || f.kind == Tab)
            context->PushAxisAlignedClip(D2D1::RectF(rect.left, rect.top, rect.left + f.clipWidth, rect.bottom),
                D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        if (f.kind == SelectedTab) {
            ComPtr<ID2D1SolidColorBrush> selected;
            auto fill = f.foreground == 0xffffff ? D2D1::ColorF(.35f, .4f, .46f, .82f) : D2D1::ColorF(1, 1, 1, .92f);
            check(context->CreateSolidColorBrush(fill, &selected));
            context->FillRoundedRectangle(round, selected.Get());
            context->DrawRoundedRectangle(round, edge.Get(), .65f * material.scale);
        } else if (hasGlass(f.kind))
            paintGlass(context, round, material);
        else if (f.kind == TabTrack) {
            ComPtr<ID2D1SolidColorBrush> track;
            check(context->CreateSolidColorBrush(color(f.foreground, .08f), &track));
            context->FillRoundedRectangle(round, track.Get());
        }
        auto cx = (rect.left + rect.right) / 2, cy = (rect.top + rect.bottom) / 2;
        float s = material.scale, stroke = 1.8f * s;
        if (f.kind == Back) {
            context->DrawLine({ cx + 4 * s, cy - 10 * s }, { cx - 6 * s, cy }, ink.Get(), stroke);
            context->DrawLine({ cx - 6 * s, cy }, { cx + 4 * s, cy + 10 * s }, ink.Get(), stroke);
        } else if (f.kind == Bookmarks) {
            ComPtr<ID2D1PathGeometry> book;
            check(factory->CreatePathGeometry(&book));
            ComPtr<ID2D1GeometrySink> path;
            check(book->Open(&path));
            path->BeginFigure({ cx, cy - 8 * s }, D2D1_FIGURE_BEGIN_HOLLOW);
            path->AddBezier({ { cx - 4 * s, cy - 11 * s }, { cx - 8 * s, cy - 11 * s }, { cx - 12 * s, cy - 9 * s } });
            path->AddLine({ cx - 12 * s, cy + 9 * s });
            path->AddBezier({ { cx - 8 * s, cy + 7 * s }, { cx - 4 * s, cy + 7 * s }, { cx, cy + 10 * s } });
            path->AddBezier({ { cx + 4 * s, cy + 7 * s }, { cx + 8 * s, cy + 7 * s }, { cx + 12 * s, cy + 9 * s } });
            path->AddLine({ cx + 12 * s, cy - 9 * s });
            path->AddBezier({ { cx + 8 * s, cy - 11 * s }, { cx + 4 * s, cy - 11 * s }, { cx, cy - 8 * s } });
            path->EndFigure(D2D1_FIGURE_END_CLOSED);
            check(path->Close());
            context->DrawGeometry(book.Get(), ink.Get(), stroke);
            context->DrawLine({ cx, cy - 8 * s }, { cx, cy + 10 * s }, ink.Get(), stroke);
        } else if (f.kind == Plus) {
            context->DrawLine({ cx - 9 * s, cy }, { cx + 9 * s, cy }, ink.Get(), stroke);
            context->DrawLine({ cx, cy - 9 * s }, { cx, cy + 9 * s }, ink.Get(), stroke);
        } else if (f.kind == Overview) {
            context->DrawRectangle(D2D1::RectF(cx - 9 * s, cy - 11 * s, cx + 5 * s, cy + 5 * s), ink.Get(), stroke);
            context->DrawRectangle(D2D1::RectF(cx - 5 * s, cy - 7 * s, cx + 9 * s, cy + 9 * s), ink.Get(), stroke);
        } else if (f.kind == Menu) {
            for (int index = -1; index <= 1; ++index)
                context->DrawLine({ cx - 9 * s, cy + index * 4 * s }, { cx + 9 * s, cy + index * 4 * s }, ink.Get(), stroke);
        } else if (f.kind == Reload) {
            ComPtr<ID2D1PathGeometry> ring;
            check(factory->CreatePathGeometry(&ring));
            ComPtr<ID2D1GeometrySink> path;
            check(ring->Open(&path));
            path->BeginFigure({ cx + 6 * s, cy - 6 * s }, D2D1_FIGURE_BEGIN_HOLLOW);
            path->AddArc(D2D1::ArcSegment({ cx + 8 * s, cy + 3 * s }, { 9 * s, 9 * s }, 0, D2D1_SWEEP_DIRECTION_COUNTER_CLOCKWISE, D2D1_ARC_SIZE_LARGE));
            path->EndFigure(D2D1_FIGURE_END_OPEN);
            check(path->Close());
            context->DrawGeometry(ring.Get(), ink.Get(), stroke);
            context->DrawLine({ cx + 6 * s, cy - 6 * s }, { cx + 1 * s, cy - 10 * s }, ink.Get(), stroke);
            context->DrawLine({ cx + 6 * s, cy - 6 * s }, { cx + 1 * s, cy - 2 * s }, ink.Get(), stroke);
        } else if (f.kind == Camera) {
            ComPtr<ID2D1SolidColorBrush> black;
            check(context->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &black));
            context->FillEllipse(D2D1::Ellipse({ cx, cy }, f.visualWidth / 2, f.visualHeight / 2), black.Get());
        } else if (f.kind == Wifi) {
            const auto statusRadius = std::min(f.visualWidth, f.visualHeight) / 2 - 1.5f * s;
            context->DrawEllipse(D2D1::Ellipse({ cx, cy }, statusRadius, statusRadius), ink.Get(), 1.f * s);
            ComPtr<ID2D1PathGeometry> waves;
            check(factory->CreatePathGeometry(&waves));
            ComPtr<ID2D1GeometrySink> path;
            check(waves->Open(&path));
            for (float radius : { 3.f, 5.f, 7.f }) {
                path->BeginFigure({ cx - radius * s, cy + (5 - radius * .75f) * s }, D2D1_FIGURE_BEGIN_HOLLOW);
                path->AddBezier({ { cx - radius * .5f * s, cy - radius * .95f * s },
                    { cx + radius * .5f * s, cy - radius * .95f * s }, { cx + radius * s, cy + (5 - radius * .75f) * s } });
                path->EndFigure(D2D1_FIGURE_END_OPEN);
            }
            check(path->Close());
            context->DrawGeometry(waves.Get(), ink.Get(), 1.25f * s);
            context->FillEllipse(D2D1::Ellipse({ cx, cy + 4 * s }, 1.f * s, 1.f * s), ink.Get());
        } else if (f.kind == Divider) {
            context->DrawLine({ cx, rect.top }, { cx, rect.bottom }, edge.Get(), .7f * s);
        } else if (!item.text.empty()) {
            auto format = textFormat(f.fontSize, f.kind == Clock);
            if ((f.kind == Address || f.kind == CompactAddress) && f.clipWidth > 0) {
                rect.left = cx - f.clipWidth / 2;
                rect.right = cx + f.clipWidth / 2;
            }
            if (f.kind == SelectedTab) {
                auto x = rect.left + 16 * s, y = (rect.top + rect.bottom) / 2;
                context->FillEllipse(D2D1::Ellipse({ x, y }, 7 * s, 7 * s), ink.Get());
                ComPtr<ID2D1SolidColorBrush> cross;
                check(context->CreateSolidColorBrush(f.foreground == 0xffffff ? D2D1::ColorF(0, 0, 0) : D2D1::ColorF(1, 1, 1), &cross));
                context->DrawLine({ x - 2.5f * s, y - 2.5f * s }, { x + 2.5f * s, y + 2.5f * s }, cross.Get(), 1.4f * s);
                context->DrawLine({ x - 2.5f * s, y + 2.5f * s }, { x + 2.5f * s, y - 2.5f * s }, cross.Get(), 1.4f * s);
                rect.left += 32 * s;
            }
            if (f.kind == SelectedTab || f.kind == Tab) {
                if (f.kind == Tab)
                    rect.right = rect.left + f.clipWidth;
                rect.left += 8 * s;
                rect.right -= 8 * s;
                if (rect.right - rect.left >= 24 * s) {
                    const auto titleWidth = std::min(rect.right - rect.left - 24 * s, measureText(item.text.c_str(), f.fontSize));
                    const auto iconX = rect.left + std::max(0.f, (rect.right - rect.left - 24 * s - titleWidth) / 2);
                    auto icon = D2D1::RectF(iconX, cy - 8 * s, iconX + 16 * s, cy + 8 * s);
                    ComPtr<ID2D1SolidColorBrush> badge;
                    check(context->CreateSolidColorBrush(color(f.foreground, .14f), &badge));
                    context->FillRoundedRectangle(D2D1::RoundedRect(icon, 4 * s, 4 * s), badge.Get());
                    auto numberFormat = textFormat(12 * s);
                    context->DrawTextW(L"1", 1, numberFormat.Get(), icon, white.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
                    rect.left = iconX + 24 * s;
                    check(format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING));
                } else
                    rect.right = rect.left;
            }
            if (f.kind == SelectedTab || f.kind == Tab) {
                // Shape the complete title at the existing native text width.
                // DirectWrite owns cluster boundaries and the tail ellipsis.
                check(format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP));
                DWRITE_TRIMMING trimming { DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0 };
                ComPtr<IDWriteInlineObject> ellipsis;
                check(textFactory->CreateEllipsisTrimmingSign(format.Get(), &ellipsis));
                check(format->SetTrimming(&trimming, ellipsis.Get()));
            }
            context->DrawTextW(item.text.c_str(), static_cast<UINT32>(item.text.size()), format.Get(), rect, ink.Get(),
                D2D1_DRAW_TEXT_OPTIONS_CLIP);
        }
        if (f.kind == SelectedTab || f.kind == Tab)
            context->PopAxisAlignedClip();
    }
};
}
unsigned DDCompositionVersion() { return 4; }
void* DDCompositionCreate(HWND parent, HWND browser)
{
    try {
        lastError.clear();
        if (!IsWindow(parent) || !IsWindow(browser))
            check(E_INVALIDARG);
        auto compositor = std::make_unique<Compositor>(parent, browser);
        compositor->initializeWithFallback();
        return compositor.release();
    } catch (...) {
        return nullptr;
    }
}
HRESULT DDCompositionSetPresentation(void* handle, HWND target, int degrees, int x, int y)
{
    try {
        if (!handle) check(E_INVALIDARG);
        static_cast<Compositor*>(handle)->setPresentation(target, degrees, x, y);
        return S_OK;
    } catch (const CompositionFailure& error) {
        return error.result;
    } catch (...) {
        return E_OUTOFMEMORY;
    }
}
unsigned DDCompositionPresentationVersion() { return 1; }
unsigned DDCompositionScrollIndicatorVersion() { return 1; }
HRESULT DDCompositionSetTouchPointer(void* handle, float x, float y, float diameter, float left, float top, float right, float bottom)
{
    try {
        if (!handle) check(E_INVALIDARG);
        static_cast<Compositor*>(handle)->setTouchPointer(x, y, diameter, left, top, right, bottom);
        return S_OK;
    } catch (const CompositionFailure& error) { return error.result; }
    catch (...) { return E_OUTOFMEMORY; }
}
HRESULT DDCompositionSetScrollIndicator(void* handle, float x, float y, float w, float h, float opacity)
{
    try {
        if (!handle) check(E_INVALIDARG);
        static_cast<Compositor*>(handle)->setScrollIndicator(x, y, w, h, opacity);
        return S_OK;
    } catch (const CompositionFailure& error) { return error.result; }
    catch (...) { return E_OUTOFMEMORY; }
}
HRESULT DDCompositionUpdate(void* handle, int w, int h, const DDChrome* frames, int count, const DDFill* fills,
    int fillCount, DDMaterial material)
{
    try {
        if (!handle)
            check(E_INVALIDARG);
        static_cast<Compositor*>(handle)->update(w, h, frames, count, fills, fillCount, material);
        return S_OK;
    } catch (const CompositionFailure& error) {
        return error.result;
    } catch (...) {
        lastError = L"Windows composition resource allocation failed";
        return E_OUTOFMEMORY;
    }
}
HRESULT DDCompositionCheck(void* handle)
{
    try {
        if (!handle)
            check(E_INVALIDARG);
        return static_cast<Compositor*>(handle)->refresh() ? S_FALSE : S_OK;
    } catch (const CompositionFailure& error) {
        return error.result;
    } catch (...) {
        return E_FAIL;
    }
}
DDCompositionState DDCompositionGetState(void* handle)
{
    if (!handle)
        return { };
    const auto& compositor = *static_cast<Compositor*>(handle);
    return { 4, compositor.softwareDevice, compositor.resourceGeneration, compositor.sceneGeneration, compositor.width,
        compositor.height };
}
unsigned DDCompositionHit(void* handle, int x, int y)
{
    if (!handle)
        return 0;
    for (auto item = static_cast<Compositor*>(handle)->items.rbegin(); item != static_cast<Compositor*>(handle)->items.rend(); ++item) {
        const auto& f = item->frame;
        if (f.action && x >= f.x && x < f.x + f.width && y >= f.y && y < f.y + f.height)
            return f.action;
    }
    return 0;
}
HRESULT DDCompositionMeasureText(void* handle, const wchar_t* text, float size, float* width)
{
    try {
        if (!handle || !width)
            check(E_INVALIDARG);
        *width = static_cast<Compositor*>(handle)->measureText(text, size);
        return S_OK;
    } catch (const CompositionFailure& failure) {
        return failure.result;
    } catch (...) {
        return E_FAIL;
    }
}
const wchar_t* DDCompositionError() { return lastError.c_str(); }
void DDCompositionDestroy(void* handle) { delete static_cast<Compositor*>(handle); }
