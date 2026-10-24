import AppKit
import ScreenCaptureKit
import WebKit

public typealias ScreenshotCallback = @convention(c) (UnsafeRawPointer?, Int32, Int32, Int32, Double, UnsafePointer<CChar>?, Int64) -> Void

private func screenshotError(_ message: String) -> NSError {
    NSError(domain: "DevDuoScreenshot", code: 409, userInfo: [NSLocalizedDescriptionKey: message])
}

extension CalibrationContainer {
    @MainActor
    private func screenshotState() async throws -> String {
        guard !invalidated else { throw screenshotError("The native page was disposed.") }
        return try await withCheckedThrowingContinuation { continuation in
            web.evaluateJavaScript("""
                JSON.stringify({document:performance.timeOrigin,url:location.href,
                  width:innerWidth,height:innerHeight,x:scrollX,y:scrollY,
                  visual:window.visualViewport ? [visualViewport.width,visualViewport.height,
                    visualViewport.pageLeft,visualViewport.pageTop,visualViewport.scale] : null})
                """) { value, error in
                if let error { continuation.resume(throwing: error) }
                else if let state = value as? String { continuation.resume(returning: state) }
                else { continuation.resume(throwing: screenshotError("Page geometry is unavailable.")) }
            }
        }
    }

    @MainActor
    private func viewportScreenshot() async throws -> (CGImage, Int, Int, Double) {
        let insets = web.obscuredContentInsets
        // WK snapshots start at the unobscured content origin on this system
        // backend. Reapplying a left inset clips real page pixels a second time.
        let rect = NSRect(x: 0, y: 0,
                          width: web.bounds.width - insets.left - insets.right,
                          height: web.bounds.height - insets.top - insets.bottom)
        guard rect.width > 0, rect.height > 0 else { throw screenshotError("The viewport is empty." ) }
        let configuration = WKSnapshotConfiguration()
        configuration.rect = rect
        configuration.snapshotWidth = NSNumber(value: Double(rect.width))
        configuration.afterScreenUpdates = true
        let image: NSImage = try await withCheckedThrowingContinuation { continuation in
            web.takeSnapshot(with: configuration) { image, error in
                if let error { continuation.resume(throwing: error) }
                else if let image { continuation.resume(returning: image) }
                else { continuation.resume(throwing: screenshotError("WebKit returned no snapshot.")) }
            }
        }
        guard let pixels = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
            throw screenshotError("WebKit snapshot pixels are unavailable.")
        }
        return (pixels, Int(rect.width.rounded()), Int(rect.height.rounded()), 1)
    }

    @MainActor
    private func deviceScreenshot(_ crop: CGRect) async throws -> (CGImage, Int, Int, Double) {
        guard var owner = window else { throw screenshotError("The device window is unavailable.") }
        while let parent = owner.parent { owner = parent }
        guard owner.isVisible, !owner.isMiniaturized else { throw screenshotError("The device window is not visible.") }
        // This API enumerates only windows available to our own process without screen-recording consent.
        // Never enumerate the desktop or fall back to capturing another application.
        let content = try await SCShareableContent.currentProcess
        guard let target = content.windows.first(where: { $0.windowID == CGWindowID(owner.windowNumber) }) else {
            throw screenshotError("The current process's device window is unavailable for capture.")
        }
        let scale = owner.backingScaleFactor
        let topInset = owner.frame.height - (owner.contentView?.frame.height ?? owner.frame.height)
        let source = crop.offsetBy(dx: 0, dy: topInset)
        guard source.width > 0, source.height > 0,
              CGRect(origin: .zero, size: target.frame.size).contains(source) else {
            throw screenshotError("The complete device must be inside the window; capture does not resize it.")
        }
        let configuration = SCStreamConfiguration()
        configuration.sourceRect = source
        configuration.width = Int((crop.width * scale).rounded())
        configuration.height = Int((crop.height * scale).rounded())
        configuration.showsCursor = false
        configuration.ignoreShadowsSingleWindow = true
        configuration.includeChildWindows = true
        configuration.captureResolution = .best
        let image = try await SCScreenshotManager.captureImage(
            contentFilter: SCContentFilter(desktopIndependentWindow: target), configuration: configuration)
        return (image, image.width, image.height, scale)
    }

    @MainActor
    func captureScreenshot(device: Bool, crop: CGRect, callback: ScreenshotCallback, requestId: Int64) async {
        do {
            let before = try await screenshotState()
            let image = try await (device ? deviceScreenshot(crop) : viewportScreenshot())
            guard !invalidated, before == (try await screenshotState()) else {
                throw screenshotError("The page viewport or scroll changed during capture; retry after layout settles.")
            }
            let bitmap = NSBitmapImageRep(cgImage: image.0)
            guard let data = bitmap.representation(using: .png, properties: [:]) else {
                throw screenshotError("PNG encoding failed.")
            }
            data.withUnsafeBytes { bytes in
                callback(bytes.baseAddress, Int32(data.count), Int32(image.1), Int32(image.2), image.3, nil, requestId)
            }
        } catch {
            error.localizedDescription.withCString { callback(nil, 0, 0, 0, 0, $0, requestId) }
        }
    }
}

@_cdecl("devduo_capture_screenshot")
public func captureScreenshot(_ pointer: UnsafeMutableRawPointer, _ device: Int32,
                              _ x: Double, _ y: Double, _ width: Double, _ height: Double,
                              _ callback: @escaping ScreenshotCallback, _ requestId: Int64) {
    let host = Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    Task { @MainActor [weak host] in
        guard let host else {
            "The native page was disposed.".withCString { callback(nil, 0, 0, 0, 0, $0, requestId) }
            return
        }
        await host.captureScreenshot(device: device != 0, crop: CGRect(x: x, y: y, width: width, height: height),
                                     callback: callback, requestId: requestId)
    }
}
