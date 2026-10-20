import AppKit
import Foundation

@main
struct NativeLayoutTests {
    @MainActor
    static func main() {
        _ = NSApplication.shared
        let host = CalibrationContainer(
            url: "about:blank", svg: "M 0 0 L 466 0 L 466 678 L 0 678 Z",
            width: 466, height: 678,
            options: ["mode": "safari", "chromeLayout": "side", "logicalWidth": 466, "logicalHeight": 678],
            observer: ""
        )
        defer { host.invalidate() }
        host.clipView.contentSize = NSSize(width: 466, height: 678)
        host.clipView.contentOffset = NSPoint(x: 41, y: 73)
        // Mirror NativeControlHost resizing its clip after the explicit viewport was configured.
        for size in [NSSize(width: 466, height: 678), NSSize(width: 300, height: 260),
                     NSSize(width: 180, height: 500), NSSize(width: 466, height: 678)] {
            host.clipView.setFrameSize(size)
            host.clipView.layoutSubtreeIfNeeded()
            precondition(host.frame == NSRect(x: -41, y: -73, width: 466, height: 678), "Clip resize changed the full container frame")
            precondition(host.bounds == NSRect(x: 0, y: 0, width: 466, height: 678), "Clip resize changed logical coordinates")
            precondition(host.web.frame == host.bounds, "Clip resize changed WebKit's full drawing frame")
            precondition(host.web.obscuredContentInsets.bottom == 98, "Clip resize changed the viewport reservation")
        }
        host.clipView.contentSize = NSSize(width: 233, height: 339)
        host.clipView.contentOffset = NSPoint(x: 12, y: 20)
        host.clipView.setFrameSize(NSSize(width: 180, height: 260))
        host.clipView.layoutSubtreeIfNeeded()
        precondition(host.frame == NSRect(x: -12, y: -20, width: 233, height: 339))
        precondition(host.bounds == NSRect(x: 0, y: 0, width: 466, height: 678), "Fit changed logical bounds")
        precondition(host.web.frame == host.bounds, "Fit changed the full drawing coordinates")
        host.compact = true
        host.needsLayout = true
        host.layoutSubtreeIfNeeded()
        precondition(host.web.obscuredContentInsets.bottom == 80)
        host.latestObservation = ["fixedBottomColor": [121, 26, 194]]
        host.updateAppearance()
        precondition(!host.fixedBottomBackground.isHidden)
        precondition(host.fixedBottomBackground.frame == NSRect(x: 0, y: 598, width: 382, height: 80))
        precondition(host.fixedBottomBackground.hitTest(NSPoint(x: 10, y: 10)) == nil, "Color extension must not consume input")
        host.latestObservation = [:]
        host.updateAppearance()
        precondition(host.fixedBottomBackground.isHidden, "Old footer color survived its removal")
        print("Native layout checks passed: clip resize, logical bounds, insets, passive footer color and removal")
    }
}
