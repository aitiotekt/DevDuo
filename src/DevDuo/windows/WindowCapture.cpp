#include <windows.h>
#include <d3d11.h>
#include <dwmapi.h>
#include <roapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <cstring>
#include <memory>
#include <string>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

namespace {
thread_local std::wstring captureError;
struct Apartment {
    Apartment() { check_hresult(RoInitialize(RO_INIT_MULTITHREADED)); }
    ~Apartment() { RoUninitialize(); }
};
struct FrameState {
    HANDLE ready = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    ~FrameState() { if (ready) CloseHandle(ready); }
};
struct CaptureScope {
    Direct3D11CaptureFramePool pool { nullptr };
    GraphicsCaptureSession session { nullptr };
    ~CaptureScope() {
        try { if (session) session.Close(); } catch (...) { }
        try { if (pool) pool.Close(); } catch (...) { }
    }
};

// Capture only our own HWND. WGC reads final DWM composition, including native
// child windows and glass. The requested crop uses physical desktop pixels.
using CaptureFrame = void(__cdecl*)(const unsigned char*, long long);
void captureWindow(HWND window, int x, int y, int width, int height, unsigned char** pixels,
    unsigned duration = 0, CaptureFrame observe = nullptr)
{
    DWORD process = 0;
    GetWindowThreadProcessId(window, &process);
    if (process != GetCurrentProcessId() || !IsWindowVisible(window) || IsIconic(window)
        || width < 1 || height < 1 || static_cast<long long>(width) * height > 32000000)
        throw hresult_invalid_argument(L"The current process's visible device window is required.");
    if (!GraphicsCaptureSession::IsSupported()) throw hresult_not_implemented(L"Windows Graphics Capture is unavailable.");
    com_ptr<ID3D11Device> device;
    com_ptr<ID3D11DeviceContext> context;
    check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
    auto dxgi = device.as<IDXGIDevice>();
    com_ptr<::IInspectable> inspectable;
    check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
    auto directDevice = inspectable.as<IDirect3DDevice>();
    auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
    GraphicsCaptureItem item { nullptr };
    check_hresult(factory->CreateForWindow(window, guid_of<GraphicsCaptureItem>(), put_abi(item)));
    auto size = item.Size();
    RECT bounds;
    check_hresult(DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, &bounds, sizeof(bounds)));
    if (bounds.right - bounds.left != size.Width || bounds.bottom - bounds.top != size.Height) {
        if (!GetWindowRect(window, &bounds) || bounds.right - bounds.left != size.Width || bounds.bottom - bounds.top != size.Height)
            throw hresult_error(E_FAIL, L"The native window capture frame changed; retry after layout settles.");
    }
    const int left = x - bounds.left, top = y - bounds.top;
    if (left < 0 || top < 0 || left > size.Width - width || top > size.Height - height)
        throw hresult_invalid_argument(L"The complete device must fit inside the window.");
    auto state = std::make_shared<FrameState>();
    if (!state->ready) throw hresult_error(HRESULT_FROM_WIN32(GetLastError()));
    CaptureScope resources;
    auto pool = resources.pool = Direct3D11CaptureFramePool::CreateFreeThreaded(directDevice, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
    auto arrival = pool.FrameArrived(auto_revoke, [state](const auto&, const auto&) { SetEvent(state->ready); });
    auto session = resources.session = pool.CreateCaptureSession(item);
    session.IsCursorCaptureEnabled(false);
    session.StartCapture();
    D3D11_TEXTURE2D_DESC description { };
    description.Width = width;
    description.Height = height;
    description.MipLevels = description.ArraySize = description.SampleDesc.Count = 1;
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.Usage = D3D11_USAGE_STAGING;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    com_ptr<ID3D11Texture2D> staging;
    check_hresult(device->CreateTexture2D(&description, nullptr, staging.put()));
    auto data = static_cast<unsigned char*>(CoTaskMemAlloc(static_cast<size_t>(width) * height * 4));
    if (!data) throw std::bad_alloc();
    std::unique_ptr<unsigned char, decltype(&CoTaskMemFree)> allocation(data, CoTaskMemFree);
    const D3D11_BOX crop { static_cast<UINT>(left), static_cast<UINT>(top), 0,
        static_cast<UINT>(left + width), static_cast<UINT>(top + height), 1 };
    auto deadline = GetTickCount64() + 5000;
    bool captured = false;
    while (GetTickCount64() < deadline) {
        auto frame = pool.TryGetNextFrame();
        if (!frame) {
            const auto now = GetTickCount64();
            if (now >= deadline || WaitForSingleObject(state->ready, static_cast<DWORD>(deadline - now)) != WAIT_OBJECT_0)
                break;
            continue;
        }
        if (frame.ContentSize().Width != size.Width || frame.ContentSize().Height != size.Height)
            throw hresult_error(E_FAIL, L"The window resized during capture; retry after layout settles.");
        auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
        com_ptr<ID3D11Texture2D> texture;
        check_hresult(access->GetInterface(guid_of<ID3D11Texture2D>(), texture.put_void()));
        context->CopySubresourceRegion(staging.get(), 0, 0, 0, 0, texture.get(), 0, &crop);
        D3D11_MAPPED_SUBRESOURCE mapped;
        check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
        for (int row = 0; row < height; ++row)
            std::memcpy(data + static_cast<size_t>(row) * width * 4,
                static_cast<unsigned char*>(mapped.pData) + static_cast<size_t>(row) * mapped.RowPitch,
                static_cast<size_t>(width) * 4);
        context->Unmap(staging.get(), 0);
        const auto timestamp = frame.SystemRelativeTime().count();
        frame.Close();
        if (!observe) {
            *pixels = allocation.release();
            return;
        }
        if (!captured) deadline = GetTickCount64() + duration;
        captured = true;
        observe(data, timestamp);
    }
    if (!captured) throw hresult_error(E_FAIL, L"No composed window frame arrived within five seconds.");
}
}

static HRESULT runCapture(HWND window, int x, int y, int width, int height, unsigned char** pixels,
    unsigned duration, CaptureFrame observe)
{
    try {
        Apartment apartment;
        captureWindow(window, x, y, width, height, pixels, duration, observe);
        captureError.clear();
        return S_OK;
    } catch (const hresult_error& error) {
        captureError = error.message().c_str();
        return error.code();
    } catch (const std::bad_alloc&) { captureError = L"Screenshot allocation failed."; return E_OUTOFMEMORY; }
    catch (...) { captureError = L"Windows Graphics Capture failed."; return E_FAIL; }
}
extern "C" __declspec(dllexport) HRESULT DDWindowCapture(HWND window, int x, int y, int width, int height, unsigned char** pixels)
{
    if (!pixels) return E_POINTER;
    *pixels = nullptr;
    return runCapture(window, x, y, width, height, pixels, 0, nullptr);
}
extern "C" __declspec(dllexport) HRESULT DDWindowCaptureFrames(HWND window, int x, int y, int width, int height,
    unsigned duration, CaptureFrame observe)
{
    if (!observe || !duration || duration > 15000) return E_INVALIDARG;
    return runCapture(window, x, y, width, height, nullptr, duration, observe);
}
extern "C" __declspec(dllexport) const wchar_t* DDWindowCaptureError() { return captureError.c_str(); }
