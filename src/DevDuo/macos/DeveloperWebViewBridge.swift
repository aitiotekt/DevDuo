import AppKit
import WebKit

// Give Avalonia a plain flipped host, and arrange WebKit after the native host
// has received its real size. Loading a zero-sized WKWebView directly as the
// platform control can leave the frontend's composited widgets unpainted.
final class DeveloperWebViewHost: NSView {
    let web: WKWebView
    override var isFlipped: Bool { true }

    init(address: URL) {
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        // chii uses this token to load its customized-built-in-element polyfill.
        configuration.applicationNameForUserAgent = "DevDuoTools Safari"
        web = WKWebView(frame: .zero, configuration: configuration)
        super.init(frame: .zero)
        wantsLayer = true
        autoresizesSubviews = false
        web.wantsLayer = true
        addSubview(web)
        web.load(URLRequest(url: address))
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) not supported") }

    override func setFrameSize(_ size: NSSize) {
        super.setFrameSize(size)
        needsLayout = true
    }

    override func layout() {
        super.layout()
        if web.frame != bounds { web.frame = bounds }
    }
}

// The tools browser has its own ephemeral session, separate from the measured WKWebView.
@_cdecl("devduo_tools_webview_create")
public func developerWebViewCreate(_ address: UnsafePointer<CChar>) -> UnsafeMutableRawPointer? {
    guard let url = URL(string: String(cString: address)) else { return nil }
    let view = DeveloperWebViewHost(address: url)
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("devduo_tools_webview_destroy")
public func developerWebViewDestroy(_ pointer: UnsafeMutableRawPointer) {
    let view = Unmanaged<DeveloperWebViewHost>.fromOpaque(pointer).takeRetainedValue()
    view.web.stopLoading()
    view.web.navigationDelegate = nil
    view.web.uiDelegate = nil
    view.removeFromSuperview()
}

@_cdecl("devduo_tools_webview_reload")
public func developerWebViewReload(_ pointer: UnsafeMutableRawPointer) {
    Unmanaged<DeveloperWebViewHost>.fromOpaque(pointer).takeUnretainedValue().web.reload()
}
