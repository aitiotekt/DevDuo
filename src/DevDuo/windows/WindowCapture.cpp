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
#include <mutex>
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
    HANDLE ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    std::mutex mutex;
    Direct3D11CaptureFrame frame { nullptr };
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
void captureWindow(HWND window, int x, int y, int width, int height, unsigned char** pixels)
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
    auto arrival = pool.FrameArrived(auto_revoke, [state](const auto& sender, const auto&) {
        try {
            auto frame = sender.TryGetNextFrame();
            if (!frame) return;
            std::lock_guard lock(state->mutex);
            if (!state->frame) { state->frame = frame; SetEvent(state->ready); }
        } catch (...) { SetEvent(state->ready); }
    });
    auto session = resources.session = pool.CreateCaptureSession(item);
    session.IsCursorCaptureEnabled(false);
    session.StartCapture();
    auto wait = WaitForSingleObject(state->ready, 5000);
    arrival.revoke();
    Direct3D11CaptureFrame frame { nullptr };
    { std::lock_guard lock(state->mutex); frame = state->frame; }
    if (wait != WAIT_OBJECT_0 || !frame) throw hresult_error(E_FAIL, L"No composed window frame arrived within five seconds.");
    if (frame.ContentSize().Width != size.Width || frame.ContentSize().Height != size.Height)
        throw hresult_error(E_FAIL, L"The window resized during capture; retry after layout settles.");
    auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    com_ptr<ID3D11Texture2D> texture;
    check_hresult(access->GetInterface(guid_of<ID3D11Texture2D>(), texture.put_void()));
    D3D11_TEXTURE2D_DESC description;
    texture->GetDesc(&description);
    description.Usage = D3D11_USAGE_STAGING;
    description.BindFlags = 0;
    description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    description.MiscFlags = 0;
    com_ptr<ID3D11Texture2D> staging;
    check_hresult(device->CreateTexture2D(&description, nullptr, staging.put()));
    context->CopyResource(staging.get(), texture.get());
    D3D11_MAPPED_SUBRESOURCE mapped;
    check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
    auto data = static_cast<unsigned char*>(CoTaskMemAlloc(static_cast<size_t>(width) * height * 4));
    if (!data) { context->Unmap(staging.get(), 0); throw std::bad_alloc(); }
    std::unique_ptr<unsigned char, decltype(&CoTaskMemFree)> allocation(data, CoTaskMemFree);
    for (int row = 0; row < height; ++row)
        std::memcpy(data + static_cast<size_t>(row) * width * 4,
            static_cast<unsigned char*>(mapped.pData) + static_cast<size_t>(top + row) * mapped.RowPitch + static_cast<size_t>(left) * 4,
            static_cast<size_t>(width) * 4);
    context->Unmap(staging.get(), 0);
    frame.Close();
    *pixels = allocation.release();
}
}

extern "C" __declspec(dllexport) HRESULT DDWindowCapture(HWND window, int x, int y, int width, int height, unsigned char** pixels)
{
    if (!pixels) return E_POINTER;
    *pixels = nullptr;
    try {
        Apartment apartment;
        captureWindow(window, x, y, width, height, pixels);
        captureError.clear();
        return S_OK;
    } catch (const hresult_error& error) {
        captureError = error.message().c_str();
        return error.code();
    } catch (const std::bad_alloc&) { captureError = L"Screenshot allocation failed."; return E_OUTOFMEMORY; }
    catch (...) { captureError = L"Windows Graphics Capture failed."; return E_FAIL; }
}
extern "C" __declspec(dllexport) const wchar_t* DDWindowCaptureError() { return captureError.c_str(); }
