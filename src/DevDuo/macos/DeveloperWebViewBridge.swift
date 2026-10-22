import AppKit
import WebKit

// The tools browser has its own ephemeral session, separate from the measured WKWebView.
@_cdecl("devduo_tools_webview_create")
public func developerWebViewCreate(_ address: UnsafePointer<CChar>) -> UnsafeMutableRawPointer? {
    guard let url = URL(string: String(cString: address)) else { return nil }
    let configuration = WKWebViewConfiguration()
    configuration.websiteDataStore = .nonPersistent()
    // chii uses this token to load its Safari customized-built-in-element polyfill.
    configuration.applicationNameForUserAgent = "DevDuoTools Safari"
    let view = WKWebView(frame: .zero, configuration: configuration)
    view.autoresizingMask = [.width, .height]
    view.load(URLRequest(url: url))
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("devduo_tools_webview_destroy")
public func developerWebViewDestroy(_ pointer: UnsafeMutableRawPointer) {
    let view = Unmanaged<WKWebView>.fromOpaque(pointer).takeRetainedValue()
    view.stopLoading()
    view.navigationDelegate = nil
    view.uiDelegate = nil
    view.removeFromSuperview()
}

@_cdecl("devduo_tools_webview_reload")
public func developerWebViewReload(_ pointer: UnsafeMutableRawPointer) {
    Unmanaged<WKWebView>.fromOpaque(pointer).takeUnretainedValue().reload()
}
