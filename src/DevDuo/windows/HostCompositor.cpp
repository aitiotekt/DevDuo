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
#include <tuple>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;
namespace {
thread_local std::wstring lastError;
double monotonicSeconds()
{
    LARGE_INTEGER tick, frequency;
    QueryPerformanceCounter(&tick);
    QueryPerformanceFrequency(&frequency);
    return static_cast<double>(tick.QuadPart) / frequency.QuadPart;
}
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
class SharedTextureReadLock {
    ComPtr<IDXGIKeyedMutex> mutex;
    bool held = false;
public:
    explicit SharedTextureReadLock(ID3D11Texture2D* texture, unsigned long long key)
    {
        check(texture->QueryInterface(IID_PPV_ARGS(&mutex)));
        const auto result = mutex->AcquireSync(key, 0);
        check(result == S_OK ? S_OK : result == WAIT_TIMEOUT ? E_PENDING : HRESULT_FROM_WIN32(ERROR_INVALID_STATE));
        held = true;
    }
    ~SharedTextureReadLock() { if (held) mutex->ReleaseSync(0); }
    void release()
    {
        check(mutex->ReleaseSync(0));
        held = false;
    }
};
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
DDMaterial materialForRole(int kind, bool dark, DDMaterial material)
{
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
    if (kind == Top) {
        material.tint = dark ? 0x2b3c4f : 0xffffff;
        material.alpha = std::min(.95f, material.alpha * (dark ? 2.4f : 2.7f));
        material.strength = std::min(1.f, material.strength * 2);
        material.blur *= 1.2f;
        material.topHighlight = material.edgeHighlight = material.sheen = 0;
        material.shadow = 0;
    } else if (kind == CompactAddress) {
        material.tint = dark ? blendTint(0x74808f, 0x657382) : 0xffffff;
        material.alpha *= dark ? blend(.65f, 2.5f) : blend(2.f, .2f);
        material.topHighlight *= .08f;
        material.edgeHighlight *= .1f;
        material.sheen = 0;
        material.shadow *= .15f;
        material.blur *= .7f;
    } else if (kind <= TabGroup) {
        material.tint = dark ? blendTint(0x8b97a5, 0x657382) : 0xffffff;
        material.alpha *= dark ? blend(1.1f, 2.5f) : blend(2.f, .55f);
        material.topHighlight *= dark ? .35f : .65f;
        material.edgeHighlight *= dark ? .55f : .75f;
        material.sheen *= .5f;
    }
    return material;
}

DDMaterial materialFor(const DDChrome& frame, DDMaterial material)
{
    const bool dark = frame.foreground == 0xffffff;
    if (frame.kind != Address && frame.kind != CompactAddress)
        return materialForRole(frame.kind, dark, material);
    auto expanded = materialForRole(Address, dark, material);
    const auto compact = materialForRole(CompactAddress, dark, material);
    const auto fraction = std::clamp(frame.compactMaterial, 0.f, 1.f);
    unsigned tint = 0;
    for (int shift : { 0, 8, 16 })
        tint |= static_cast<unsigned>(std::round(std::lerp(static_cast<float>((expanded.tint >> shift) & 255),
            static_cast<float>((compact.tint >> shift) & 255), fraction))) << shift;
    expanded.tint = tint;
    expanded.alpha = std::lerp(expanded.alpha, compact.alpha, fraction);
    expanded.strength = std::lerp(expanded.strength, compact.strength, fraction);
    expanded.shadow = std::lerp(expanded.shadow, compact.shadow, fraction);
    expanded.scale = std::lerp(expanded.scale, compact.scale, fraction);
    expanded.blur = std::lerp(expanded.blur, compact.blur, fraction);
    expanded.saturation = std::lerp(expanded.saturation, compact.saturation, fraction);
    expanded.brightness = std::lerp(expanded.brightness, compact.brightness, fraction);
    expanded.contrast = std::lerp(expanded.contrast, compact.contrast, fraction);
    expanded.topHighlight = std::lerp(expanded.topHighlight, compact.topHighlight, fraction);
    expanded.edgeHighlight = std::lerp(expanded.edgeHighlight, compact.edgeHighlight, fraction);
    expanded.sheen = std::lerp(expanded.sheen, compact.sheen, fraction);
    expanded.shadowBlur = std::lerp(expanded.shadowBlur, compact.shadowBlur, fraction);
    expanded.shadowOffset = std::lerp(expanded.shadowOffset, compact.shadowOffset, fraction);
    return expanded;
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
    ComPtr<IDCompositionSurface> pageSurface;
    int pageSurfaceWidth = 0, pageSurfaceHeight = 0;
    std::vector<ComPtr<IDCompositionVisual2>> pageVisuals;
    ComPtr<ID3D11Texture2D> sharedPageTexture;
    ComPtr<ID3D11Texture2D> rawPageTexture, finalPageTexture;
    ComPtr<ID3D11Texture2D> presentedPageTexture;
    ComPtr<ID2D1Bitmap1> rawPageBitmap, finalPageBitmap;
    ComPtr<ID2D1DeviceContext> pageContext;
    HANDLE sharedPageHandle = nullptr;
    unsigned long long pageFrameID = 0, pageDocumentEpoch = 0;
    unsigned long long rawFrameID = 0, rawDocumentEpoch = 0;
    unsigned long long pagePresentations = 0, gpuTransfers = 0, cpuTransfers = 0, pageCaptures = 0;
    int rawPageWidth = 0, rawPageHeight = 0, finalPageWidth = 0, finalPageHeight = 0;
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
    struct GlassNode {
        size_t index;
        ComPtr<IDCompositionGaussianBlurEffect> blur;
        ComPtr<IDCompositionColorMatrixEffect> correction;
        ComPtr<IDCompositionVisual3> opacity;
        ComPtr<IDCompositionRectangleClip> clip;
    };
    std::vector<GlassNode> glassNodes;
    std::vector<ComPtr<IDCompositionVisual2>> boundaryVisuals;
    ComPtr<IDCompositionVisual2> shadowVisual, decorationVisual, foregroundVisual;
    ComPtr<IDCompositionGaussianBlurEffect> shadowEffect;
    ComPtr<IDCompositionSurface> foregroundSurface, shadowSurface;
    std::vector<DDFill> previousFills;
    DDMaterial previousMaterial { };
    bool sceneReady = false;
    bool sceneUpdateIncomplete = false;
    unsigned long long structureUpdates = 0, materialUpdates = 0, boundaryUpdates = 0, foregroundUpdates = 0;
    std::vector<Item> items;
    int width = 0, height = 0;
    bool softwareDevice = false;
    unsigned long long resourceGeneration = 0, sceneGeneration = 0;
    double updateStartSeconds = 0, commitStartSeconds = 0, commitEndSeconds = 0;
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
        // The HWND remains the input/native-popup owner. Page pixels arrive
        // through the frame lease API, independent of window redirection.
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
        ComPtr<IDCompositionSurface> empty;
        check(composition->CreateSurface(1, 1, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED, &empty));
        check(empty.As(&page));
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
        if (sceneUpdateIncomplete) check(E_UNEXPECTED);
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
        if (SUCCEEDED(device->GetDeviceRemovedReason())) {
            if (sceneUpdateIncomplete) check(E_UNEXPECTED);
            return false;
        }
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
        sceneReady = false;
        sceneUpdateIncomplete = false;
        glassNodes.clear();
        boundaryVisuals.clear();
        shadowVisual.Reset(); decorationVisual.Reset(); foregroundVisual.Reset();
        shadowEffect.Reset(); foregroundSurface.Reset(); shadowSurface.Reset();
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
        pageSurface.Reset();
        pageSurfaceWidth = pageSurfaceHeight = 0;
        pageVisuals.clear();
        sharedPageTexture.Reset();
        rawPageTexture.Reset(); finalPageTexture.Reset();
        presentedPageTexture.Reset();
        rawPageBitmap.Reset(); finalPageBitmap.Reset(); pageContext.Reset();
        rawPageWidth = rawPageHeight = finalPageWidth = finalPageHeight = 0;
        sharedPageHandle = nullptr;
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
        if (!surface) check(composition->CreateSurface(
            width, height, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_ALPHA_MODE_PREMULTIPLIED, &surface));
        ComPtr<ID2D1DeviceContext> context;
        POINT offset;
        check(surface->BeginDraw(nullptr, IID_PPV_ARGS(&context), &offset));
        context->SetDpi(96, 96);
        context->SetTransform(
            D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
        // BeginDraw may return a pooled texture larger than this logical surface.
        // Clear is not transformed, so constrain it and all drawing to our region.
        context->PushAxisAlignedClip(D2D1::RectF(0, 0, static_cast<float>(width), static_cast<float>(height)), D2D1_ANTIALIAS_MODE_ALIASED);
        context->Clear(D2D1::ColorF(0, 0));
        try {
            paint(context.Get());
        } catch (...) {
            context->PopAxisAlignedClip();
            surface->EndDraw();
            throw;
        }
        context->PopAxisAlignedClip();
        check(surface->EndDraw());
    }
    void addBase(IDCompositionVisual2* group)
    {
        auto source = visual(page.Get());
        pageVisuals.push_back(source);
        check(group->AddVisual(source.Get(), FALSE, nullptr));
        auto fill = visual(boundary.Get());
        boundaryVisuals.push_back(fill);
        check(group->AddVisual(fill.Get(), FALSE, nullptr));
    }
    void createPageBuffers(int w, int h)
    {
        if (rawPageTexture && rawPageWidth == w && rawPageHeight == h && finalPageWidth == width && finalPageHeight == height) return;
        rawPageBitmap.Reset(); finalPageBitmap.Reset(); pageContext.Reset();
        rawPageTexture.Reset(); finalPageTexture.Reset();
        D3D11_TEXTURE2D_DESC description { };
        description.Width = w; description.Height = h;
        description.MipLevels = description.ArraySize = description.SampleDesc.Count = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.Usage = D3D11_USAGE_DEFAULT;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
        check(device->CreateTexture2D(&description, nullptr, &rawPageTexture));
        description.Width = std::max(1, width); description.Height = std::max(1, height);
        check(device->CreateTexture2D(&description, nullptr, &finalPageTexture));
        check(drawing->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &pageContext));
        ComPtr<IDXGISurface> raw, final;
        check(rawPageTexture.As(&raw)); check(finalPageTexture.As(&final));
        auto properties = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
        check(pageContext->CreateBitmapFromDxgiSurface(raw.Get(), &properties, &rawPageBitmap));
        properties.bitmapOptions = D2D1_BITMAP_OPTIONS_TARGET;
        check(pageContext->CreateBitmapFromDxgiSurface(final.Get(), &properties, &finalPageBitmap));
        pageContext->SetTarget(finalPageBitmap.Get());
        pageContext->SetDpi(96, 96);
        pageContext->SetUnitMode(D2D1_UNIT_MODE_PIXELS);
        rawPageWidth = w; rawPageHeight = h;
        finalPageWidth = std::max(1, width); finalPageHeight = std::max(1, height);
    }
    HRESULT presentPageFrame(DDPageFrame frame, const DDFill* fills, int fillCount)
    {
        if (sceneUpdateIncomplete) check(E_UNEXPECTED);
        const bool contentChanged = frame.flags & 1;
        if (frame.width < 1 || frame.height < 1 || frame.width > 32767 || frame.height > 32767
            || fillCount < 0 || fillCount > 16 || (fillCount && !fills)
            || (contentChanged && !frame.sharedTexture && (!frame.pixels || frame.rowBytes < static_cast<unsigned>(frame.width) * 4)))
            check(E_INVALIDARG);
        const auto begin = monotonicSeconds();
        if (contentChanged) {
            createPageBuffers(frame.width, frame.height);
            ComPtr<ID3D11DeviceContext> queue;
            device->GetImmediateContext(&queue);
            if (frame.sharedTexture) {
                // Legacy DXGI sharing tokens are not NT handles; never close
                // them with CloseHandle. COM references retain the resource.
                if (sharedPageHandle != frame.sharedTexture) {
                    sharedPageTexture.Reset();
                    check(device->OpenSharedResource(frame.sharedTexture, IID_PPV_ARGS(&sharedPageTexture)));
                    sharedPageHandle = frame.sharedTexture;
                }
                SharedTextureReadLock readLock(sharedPageTexture.Get(), frame.flags & 2 ? 0 : 1);
                D3D11_TEXTURE2D_DESC description;
                sharedPageTexture->GetDesc(&description);
                if (description.Width != static_cast<unsigned>(frame.width) || description.Height != static_cast<unsigned>(frame.height)
                    || description.Format != DXGI_FORMAT_B8G8R8A8_UNORM) check(E_INVALIDARG);
                queue->CopyResource(rawPageTexture.Get(), sharedPageTexture.Get());
                queue->Flush();
                readLock.release();
                ++gpuTransfers;
            } else {
                queue->UpdateSubresource(rawPageTexture.Get(), 0, nullptr, frame.pixels, frame.rowBytes, 0);
                ++cpuTransfers;
            }
            rawFrameID = frame.frameID; rawDocumentEpoch = frame.documentEpoch;
        } else if (!rawPageTexture || rawFrameID != frame.frameID || rawDocumentEpoch != frame.documentEpoch)
            return S_FALSE;
        // Retain raw content even if scene layout overtakes this frame. A later
        // geometry/resource replay must not reacquire a consumed producer key.
        // The host validates the source against the current logical geometry
        // and backing scale, marking stale frames with bit 4. HWND rounding is
        // independent of that raster extent; do not discard a valid fractional
        // scale frame merely because one physical edge differs.
        if ((frame.flags & 4) || finalPageWidth != width || finalPageHeight != height) return S_FALSE;
        sceneUpdateIncomplete = true;
        pageContext->SetTarget(finalPageBitmap.Get());
        pageContext->BeginDraw();
        pageContext->Clear(color(frame.background));
        // Copy at native pixel size. Clip excess pixels and clear any physical
        // rounding margin with the frame's background, never stretch the page.
        pageContext->DrawBitmap(rawPageBitmap.Get(), D2D1::RectF(0, 0, static_cast<float>(frame.width), static_cast<float>(frame.height)),
            1, D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
        pageContext->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
        ComPtr<ID2D1SolidColorBrush> brush;
        check(pageContext->CreateSolidColorBrush(D2D1::ColorF(0, 0), &brush));
        for (int index = 0; index < fillCount; ++index) {
            const auto& fill = fills[index];
            if (!std::isfinite(fill.x) || !std::isfinite(fill.y) || !std::isfinite(fill.width) || !std::isfinite(fill.height)
                || fill.width < 0 || fill.height < 0) check(E_INVALIDARG);
            brush->SetColor(color(fill.color));
            pageContext->FillRectangle(D2D1::RectF(fill.x, fill.y, fill.x + fill.width, fill.y + fill.height), brush.Get());
        }
        check(pageContext->EndDraw());
        pageContext->SetTarget(nullptr);
        auto next = pageSurface;
        if (!next || pageSurfaceWidth != width || pageSurfaceHeight != height) next.Reset();
        paintSurface(next, [&](ID2D1DeviceContext* context) {
            context->DrawBitmap(finalPageBitmap.Get(), D2D1::RectF(0, 0, static_cast<float>(width), static_cast<float>(height)),
                1, D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
        });
        for (auto& visual : pageVisuals) check(visual->SetContent(next.Get()));
        check(next.As(&page));
        const auto commit = monotonicSeconds();
        check(composition->Commit());
        pageSurface = next;
        presentedPageTexture = finalPageTexture;
        pageSurfaceWidth = width; pageSurfaceHeight = height;
        pageFrameID = frame.frameID; pageDocumentEpoch = frame.documentEpoch;
        ++pagePresentations;
        updateStartSeconds = begin; commitStartSeconds = commit; commitEndSeconds = monotonicSeconds();
        sceneUpdateIncomplete = false;
        return S_OK;
    }
    void copyPagePixels(void* pixels, unsigned rowBytes, int w, int h)
    {
        if (sceneUpdateIncomplete || !presentedPageTexture || !pixels || w != pageSurfaceWidth || h != pageSurfaceHeight
            || rowBytes < static_cast<unsigned>(w) * 4) check(E_INVALIDARG);
        D3D11_TEXTURE2D_DESC description;
        presentedPageTexture->GetDesc(&description);
        description.Usage = D3D11_USAGE_STAGING;
        description.BindFlags = description.MiscFlags = 0;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;
        check(device->CreateTexture2D(&description, nullptr, &staging));
        ComPtr<ID3D11DeviceContext> queue;
        device->GetImmediateContext(&queue);
        queue->CopyResource(staging.Get(), presentedPageTexture.Get());
        D3D11_MAPPED_SUBRESOURCE mapped;
        check(queue->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped));
        for (int y = 0; y < h; ++y)
            memcpy(static_cast<unsigned char*>(pixels) + static_cast<size_t>(y) * rowBytes,
                static_cast<const unsigned char*>(mapped.pData) + static_cast<size_t>(y) * mapped.RowPitch, static_cast<size_t>(w) * 4);
        queue->Unmap(staging.Get(), 0);
        ++pageCaptures;
    }
    void update(
        int w, int h, const DDChrome* frames, int count, const DDFill* fills, int fillCount, DDMaterial material)
    {
        updateStartSeconds = monotonicSeconds();
        if (w < 1 || h < 1 || w > 32767 || h > 32767 || count < 0 || count > 100 || fillCount < 0 || fillCount > 16
            || (count && !frames) || (fillCount && !fills) || !std::isfinite(material.scale) || material.scale <= 0
            || !std::isfinite(material.alpha) || !std::isfinite(material.strength) || !std::isfinite(material.shadow))
            check(E_INVALIDARG);
        for (int i = 0; i < count; ++i) {
            const auto& f = frames[i];
            const float values[] = { f.x, f.y, f.width, f.height, f.visualX, f.visualY, f.visualWidth, f.visualHeight,
                f.radius, f.fontSize, f.clipWidth, f.compactMaterial };
            if (f.kind < Top || f.kind > CompactAddress || f.action < 0 || f.action > 7
                || std::any_of(std::begin(values), std::end(values), [](float value) { return !std::isfinite(value); })
                || f.width < 0 || f.height < 0 || f.visualWidth < 0 || f.visualHeight < 0 || f.radius < 0
                || f.fontSize <= 0 || f.compactMaterial < 0 || f.compactMaterial > 1)
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
        if (sceneUpdateIncomplete) check(E_UNEXPECTED);
        auto geometryKey = [](const DDChrome& f) {
            return std::tuple { f.kind, f.x, f.y, f.width, f.height, f.visualX, f.visualY, f.visualWidth, f.visualHeight, f.radius };
        };
        auto materialKey = [](const DDMaterial& m) {
            return std::tuple { m.tint, m.alpha, m.strength, m.shadow, m.scale, m.blur, m.saturation, m.brightness,
                m.contrast, m.topHighlight, m.edgeHighlight, m.sheen, m.shadowBlur, m.shadowOffset };
        };
        auto fillKey = [](const DDFill& f) { return std::tuple { f.x, f.y, f.width, f.height, f.color }; };
        const bool surfaceSizeChanged = !sceneReady || w != width || h != height;
        bool structureChanged = surfaceSizeChanged || items.size() != static_cast<size_t>(count);
        bool geometryChanged = structureChanged;
        bool textChanged = geometryChanged;
        bool materialChanged = !sceneReady || materialKey(material) != materialKey(previousMaterial);
        if (!geometryChanged) {
            for (int i = 0; i < count; ++i) {
                structureChanged |= frames[i].kind != items[i].frame.kind;
                geometryChanged |= geometryKey(frames[i]) != geometryKey(items[i].frame);
                textChanged |= items[i].text != (frames[i].text ? frames[i].text : L"")
                    || frames[i].foreground != items[i].frame.foreground || frames[i].fontSize != items[i].frame.fontSize
                    || frames[i].clipWidth != items[i].frame.clipWidth;
                materialChanged |= frames[i].foreground != items[i].frame.foreground
                    || frames[i].compactMaterial != items[i].frame.compactMaterial
                    || frames[i].clipWidth != items[i].frame.clipWidth;
            }
        }
        textChanged |= geometryChanged || material.scale != previousMaterial.scale;
        bool fillsChanged = !sceneReady || w != width || h != height || previousFills.size() != static_cast<size_t>(fillCount);
        if (!fillsChanged)
            for (int i = 0; i < fillCount; ++i) fillsChanged |= fillKey(fills[i]) != fillKey(previousFills[i]);
        width = w;
        height = h;
        std::vector<Item> nextItems;
        for (int i = 0; i < count; ++i) nextItems.push_back({ frames[i], frames[i].text ? frames[i].text : L"" });

        // EndDraw records surface damage for the next Commit. Retain same-size
        // surfaces so effect caches receive that damage, including boundary fills.
        // Block every other commit if any part of this transaction fails.
        sceneUpdateIncomplete = true;
        ComPtr<IDCompositionSurface> nextBoundary, nextShadow, nextDecoration, nextForeground;
        if (!surfaceSizeChanged) {
            if (fillsChanged) nextBoundary = boundary;
            if (geometryChanged || materialChanged) { nextShadow = shadowSurface; nextDecoration = decorations; }
            if (textChanged) nextForeground = foregroundSurface;
        }
        if (fillsChanged) paintSurface(nextBoundary, [&](ID2D1DeviceContext* context) {
            for (int i = 0; i < fillCount; ++i) {
                const auto& f = fills[i];
                ComPtr<ID2D1SolidColorBrush> brush;
                check(context->CreateSolidColorBrush(color(f.color), &brush));
                context->FillRectangle(D2D1::RectF(f.x, f.y, f.x + f.width, f.y + f.height), brush.Get());
            }
        });
        if (geometryChanged || materialChanged) {
            paintSurface(nextShadow, [&](ID2D1DeviceContext* context) {
                ComPtr<ID2D1SolidColorBrush> brush;
                check(context->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &brush));
                for (const auto& item : nextItems) {
                    const auto& f = item.frame;
                    if (f.kind == Top || !hasGlass(f.kind)) continue;
                    const auto optics = materialFor(f, material);
                    brush->SetOpacity(std::clamp(optics.shadow, 0.f, 1.f));
                    const auto rect = D2D1::RectF(f.x + f.visualX, f.y + f.visualY + material.shadowOffset * material.scale,
                        f.x + f.visualX + f.visualWidth, f.y + f.visualY + f.visualHeight + material.shadowOffset * material.scale);
                    context->FillRoundedRectangle(D2D1::RoundedRect(rect, f.radius, f.radius), brush.Get());
                }
            });
            paintSurface(nextDecoration, [&](ID2D1DeviceContext* context) {
                for (const auto& item : nextItems) paintItem(context, item, material, true);
            });
        }
        if (textChanged) paintSurface(nextForeground, [&](ID2D1DeviceContext* context) {
            for (const auto& item : nextItems) paintItem(context, item, material, false);
        });
        if (nextBoundary) boundary = nextBoundary;
        if (nextShadow) shadowSurface = nextShadow;
        if (nextDecoration) decorations = nextDecoration;
        if (nextForeground) foregroundSurface = nextForeground;
        items = std::move(nextItems);
        if (structureChanged) {
            applyPresentation();
            check(root->RemoveAllVisuals());
            boundaryVisuals.clear(); glassNodes.clear();
            pageVisuals.clear();
            addBase(root.Get());
            shadowVisual = visual(nextShadow.Get());
            check(composition->CreateGaussianBlurEffect(&shadowEffect));
            check(shadowVisual->SetEffect(shadowEffect.Get()));
            check(root->AddVisual(shadowVisual.Get(), FALSE, nullptr));
            for (size_t i = 0; i < items.size(); ++i) {
                const auto& f = items[i].frame;
                if (!hasGlass(f.kind)) continue;
                ComPtr<IDCompositionVisual2> clipped, blur;
                check(composition->CreateVisual(&clipped));
                check(composition->CreateVisual(&blur));
                addBase(blur.Get());
                GlassNode node { i };
                check(composition->CreateGaussianBlurEffect(&node.blur));
                check(node.blur->SetBorderMode(D2D1_BORDER_MODE_HARD));
                check(composition->CreateColorMatrixEffect(&node.correction));
                check(node.correction->SetAlphaMode(D2D1_COLORMATRIX_ALPHA_MODE_PREMULTIPLIED));
                check(node.correction->SetClampOutput(TRUE));
                check(node.correction->SetInput(0, node.blur.Get(), 0));
                check(blur->SetEffect(node.correction.Get()));
                check(blur.As(&node.opacity));
                check(clipped->AddVisual(blur.Get(), FALSE, nullptr));
                check(composition->CreateRectangleClip(&node.clip));
                check(clipped->SetClip(node.clip.Get()));
                check(root->AddVisual(clipped.Get(), FALSE, nullptr));
                glassNodes.push_back(std::move(node));
            }
            decorationVisual = visual(decorations.Get());
            foregroundVisual = visual(foregroundSurface.Get());
            check(root->AddVisual(decorationVisual.Get(), FALSE, nullptr));
            check(root->AddVisual(foregroundVisual.Get(), FALSE, nullptr));
            if (indicatorVisual) check(root->AddVisual(indicatorVisual.Get(), FALSE, nullptr));
            if (pointerGroup) check(root->AddVisual(pointerGroup.Get(), FALSE, nullptr));
        } else {
            if (fillsChanged)
                for (auto& visual : boundaryVisuals) check(visual->SetContent(boundary.Get()));
            if (nextShadow) check(shadowVisual->SetContent(nextShadow.Get()));
            if (nextDecoration) check(decorationVisual->SetContent(decorations.Get()));
            if (nextForeground) check(foregroundVisual->SetContent(foregroundSurface.Get()));
        }
        if (geometryChanged || materialChanged) {
            check(shadowEffect->SetStandardDeviation(material.shadowBlur * material.scale));
            for (auto& node : glassNodes) {
                const auto& frame = items[node.index].frame;
                if (geometryChanged) {
                    const auto left = frame.x + frame.visualX, top = frame.y + frame.visualY;
                    check(node.clip->SetLeft(left)); check(node.clip->SetTop(top));
                    check(node.clip->SetRight(left + frame.visualWidth)); check(node.clip->SetBottom(top + frame.visualHeight));
                    check(node.clip->SetTopLeftRadiusX(frame.radius)); check(node.clip->SetTopLeftRadiusY(frame.radius));
                    check(node.clip->SetTopRightRadiusX(frame.radius)); check(node.clip->SetTopRightRadiusY(frame.radius));
                    check(node.clip->SetBottomLeftRadiusX(frame.radius)); check(node.clip->SetBottomLeftRadiusY(frame.radius));
                    check(node.clip->SetBottomRightRadiusX(frame.radius)); check(node.clip->SetBottomRightRadiusY(frame.radius));
                }
                const auto optics = materialFor(frame, material);
                check(node.blur->SetStandardDeviation(optics.blur * material.scale));
                check(node.opacity->SetOpacity(std::clamp(optics.strength, 0.f, 1.f)));
                const float s = material.saturation, gain = material.brightness * material.contrast;
                const float r = .2126f * (1 - s), g = .7152f * (1 - s), b = .0722f * (1 - s);
                const float offset = material.brightness * .5f * (1 - material.contrast);
                D2D1_MATRIX_5X4_F matrix = {
                    (r + s) * gain, r * gain, r * gain, 0, g * gain, (g + s) * gain, g * gain, 0,
                    b * gain, b * gain, (b + s) * gain, 0, 0, 0, 0, 1, offset, offset, offset, 0
                };
                check(node.correction->SetMatrix(matrix));
            }
        }
        commitStartSeconds = monotonicSeconds();
        check(composition->Commit());
        commitEndSeconds = monotonicSeconds();
        sceneUpdateIncomplete = false;
        sceneReady = true;
        previousMaterial = material;
        previousFills.clear();
        for (int i = 0; i < fillCount; ++i) previousFills.push_back(fills[i]);
        structureUpdates += structureChanged;
        materialUpdates += geometryChanged || materialChanged;
        boundaryUpdates += fillsChanged;
        foregroundUpdates += textChanged;
        ++sceneGeneration;
        lastError.clear();
    }
    void setTouchPointer(float x, float y, float diameter, float left, float top, float right, float bottom)
    {
        if (sceneUpdateIncomplete) check(E_UNEXPECTED);
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
            context->PushAxisAlignedClip(D2D1::RectF(0, 0, static_cast<float>(extent), static_cast<float>(extent)), D2D1_ANTIALIAS_MODE_ALIASED);
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
            context->PopAxisAlignedClip();
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
        if (sceneUpdateIncomplete) check(E_UNEXPECTED);
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
            context->PushAxisAlignedClip(D2D1::RectF(0, 0, std::ceil(w), std::ceil(h)), D2D1_ANTIALIAS_MODE_ALIASED);
            context->Clear(D2D1::ColorF(0, 0));
            ComPtr<ID2D1SolidColorBrush> brush;
            auto result = context->CreateSolidColorBrush(D2D1::ColorF(0, 0, 0), &brush);
            if (SUCCEEDED(result)) context->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(0, 0, w, h), w / 2, w / 2), brush.Get());
            context->PopAxisAlignedClip();
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
    void paintItem(ID2D1DeviceContext* context, const Item& item, DDMaterial material, bool backdrop)
    {
        const auto& f = item.frame;
        if (f.width <= 0 || f.height <= 0 || f.visualWidth <= 0 || f.visualHeight <= 0) return;
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
        if (backdrop) {
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
            if (f.kind == SelectedTab || f.kind == Tab) context->PopAxisAlignedClip();
            return;
        }
        auto cx = (rect.left + rect.right) / 2, cy = (rect.top + rect.bottom) / 2;
        float s = material.scale, stroke = 1.8f * s;
        if (f.kind == Back || f.kind == Bookmarks || f.kind == Menu || f.kind == Reload || f.kind == Plus || f.kind == Overview
            || f.kind == Wifi || f.kind == SelectedTab || f.kind == Tab) {
            // The host interpolates the existing font metric while these roles
            // enter/leave. Keep icons with their shrinking optical containers.
            const float nominalSize = f.kind == SelectedTab || f.kind == Tab ? 14.f : 17.f;
            const float visibility = std::clamp(f.fontSize / (nominalSize * material.scale), 0.f, 1.f);
            s *= visibility;
            stroke *= visibility;
            ink->SetOpacity(visibility);
        }
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
unsigned DDCompositionVersion() { return 5; }
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
unsigned DDCompositionPageFrameVersion() { return 2; }
DDPageFrameState DDCompositionGetPageFrameState(void* handle)
{
    if (!handle) return { };
    const auto& value = *static_cast<Compositor*>(handle);
    return { value.pageFrameID, value.pageDocumentEpoch, value.pagePresentations, value.gpuTransfers,
        value.cpuTransfers, value.pageCaptures, value.pageSurfaceWidth, value.pageSurfaceHeight };
}
HRESULT DDCompositionCopyPagePixels(void* handle, void* pixels, unsigned rowBytes, int width, int height)
{
    try {
        if (!handle) check(E_INVALIDARG);
        static_cast<Compositor*>(handle)->copyPagePixels(pixels, rowBytes, width, height);
        return S_OK;
    } catch (const CompositionFailure& error) { return error.result; }
    catch (...) { return E_OUTOFMEMORY; }
}
HRESULT DDCompositionPresentPageFrame(void* handle, DDPageFrame frame, const DDFill* fills, int fillCount)
{
    try {
        if (!handle) check(E_INVALIDARG);
        return static_cast<Compositor*>(handle)->presentPageFrame(frame, fills, fillCount);
    } catch (const CompositionFailure& error) {
        return error.result;
    } catch (...) {
        lastError = L"Windows page-frame presentation failed";
        return E_OUTOFMEMORY;
    }
}
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
    return { 5, compositor.softwareDevice, compositor.resourceGeneration, compositor.sceneGeneration, compositor.width,
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

HRESULT DDCompositionGetUpdateCounters(void* handle, unsigned long long* structure, unsigned long long* material,
    unsigned long long* boundary, unsigned long long* foreground)
{
    if (!handle || !structure || !material || !boundary || !foreground) return E_INVALIDARG;
    const auto* compositor = static_cast<Compositor*>(handle);
    *structure = compositor->structureUpdates; *material = compositor->materialUpdates;
    *boundary = compositor->boundaryUpdates; *foreground = compositor->foregroundUpdates;
    return S_OK;
}

HRESULT DDCompositionGetUpdateTiming(void* handle, double* startSeconds, double* commitSeconds, double* endSeconds)
{
    if (!handle || !startSeconds || !commitSeconds || !endSeconds) return E_INVALIDARG;
    const auto* compositor = static_cast<Compositor*>(handle);
    *startSeconds = compositor->updateStartSeconds;
    *commitSeconds = compositor->commitStartSeconds;
    *endSeconds = compositor->commitEndSeconds;
    return S_OK;
}
