import AppKit
import Foundation

@main
struct NativeLayoutTests {
    @MainActor
    static func main() {
        _ = NSApplication.shared
        let toolsPointer="about:blank".withCString { developerWebViewCreate($0)! }
        let tools=Unmanaged<DeveloperWebViewHost>.fromOpaque(toolsPointer).takeUnretainedValue()
        for size in [NSSize(width:1100,height:780),NSSize(width:600,height:400)] {
            tools.setFrameSize(size)
            tools.layoutSubtreeIfNeeded()
            precondition(tools.web.frame==tools.bounds,"Tools browser must follow the arranged native host")
        }
        precondition(!tools.web.configuration.websiteDataStore.isPersistent,"Tools must not share persistent page storage")
        developerWebViewDestroy(toolsPointer)
        let fitHost=CalibrationContainer(url:"about:blank",svg:"",width:951,height:669,
            options:["mode":"app-webview","logicalWidth":951,"logicalHeight":669],observer:"")
        defer { fitHost.invalidate() }
        for scale in [1.0,0.90344636085961,0.94,0.75,0.5] {
            let size=NSSize(width:951*scale,height:669*scale)
            fitHost.clipView.contentSize=size
            fitHost.clipView.setFrameSize(size)
            fitHost.clipView.needsLayout=true
            fitHost.clipView.layoutSubtreeIfNeeded()
            precondition(fitHost.web.frame.size==NSSize(width:951,height:669),
                         "Fractional Fit changed the logical WebKit frame: \(scale), \(fitHost.web.frame)")
        }
        let host = CalibrationContainer(
            url: "about:blank", svg: "M 0 0 L 466 0 L 466 678 L 0 678 Z",
            width: 466, height: 678,
            options: ["mode": "safari", "chromeLayout": "side", "logicalWidth": 466, "logicalHeight": 678],
            observer: ""
        )
        defer { host.invalidate() }
        if host.detachedInspectorAvailable {
            let attachment=host.web.perform(NSSelectorFromString("_inspectorAttachmentView"))?.takeUnretainedValue() as? NSView
            precondition(attachment === host.inspectorAttachment && attachment?.isHidden == true,
                         "Inspector docking must target a hidden view, never the measured browser")
        }
        var inspectorShortcuts=0
        host.web.onInspectorShortcut={inspectorShortcuts+=1}
        for (code,characters,modifiers) in [(UInt16(111),"",NSEvent.ModifierFlags()),
                                             (UInt16(34),"I",[.control,.shift])] {
            let event=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:modifiers,timestamp:0,
                                      windowNumber:0,context:nil,characters:characters,
                                      charactersIgnoringModifiers:characters,isARepeat:false,keyCode:code)!
            precondition(host.web.performKeyEquivalent(with:event),"Inspector shortcut did not reach the native web host")
        }
        precondition(inspectorShortcuts==2)
        host.web.onInspectorShortcut=nil
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
        let nativeFixedContainerColorsAvailable = host.nativeFixedContainerColorsAvailable
        host.latestObservation = ["fixedBottomColor": [121, 26, 194]]
        host.updateAppearance()
        precondition(host.fixedBottomBackground.isHidden == nativeFixedContainerColorsAvailable,
                     "The DOM footer surface must defer to the system fixed-container extension")
        // Exercise the fallback surface independently of the installed system capability.
        // Its geometry is testable here; actual system extension pixels require native acceptance.
        host.nativeFixedContainerColorsAvailable = false
        host.updateAppearance()
        precondition(!host.fixedBottomBackground.isHidden)
        precondition(host.fixedBottomBackground.frame == NSRect(x: 0, y: 598, width: 382, height: 80))
        precondition(host.fixedBottomBackground.hitTest(NSPoint(x: 10, y: 10)) == nil, "Color extension must not consume input")
        host.latestObservation = [:]
        host.updateAppearance()
        precondition(host.fixedBottomBackground.isHidden, "Old footer color survived its removal")
        host.nativeFixedContainerColorsAvailable = nativeFixedContainerColorsAvailable
        for degrees in [0,90,180,270] {
            let swapped=degrees % 180 != 0
            host.clipView.clockwiseDegrees=degrees
            host.clipView.contentSize=swapped ? NSSize(width:339,height:233):NSSize(width:233,height:339)
            host.clipView.needsLayout=true
            host.clipView.layoutSubtreeIfNeeded()
            host.layoutSubtreeIfNeeded()
            let point=host.convert(NSPoint(x:20,y:40),to:host.clipView)
            let expected:NSPoint
            switch degrees {
            case 90:expected=NSPoint(x:339-20-12,y:10-20)
            case 180:expected=NSPoint(x:233-10-12,y:339-20-20)
            case 270:expected=NSPoint(x:20-12,y:233-10-20)
            default:expected=NSPoint(x:10-12,y:20-20)
            }
            precondition(abs(point.x-expected.x)<0.001 && abs(point.y-expected.y)<0.001,"Scene/body coordinate transform: \(degrees), \(point), expected \(expected)")
            let roundTrip=host.convert(point,from:host.clipView)
            precondition(abs(roundTrip.x-20)<0.001 && abs(roundTrip.y-40)<0.001,"Native input inverse changed")
            precondition(host.bounds.size==NSSize(width:466,height:678),"Presentation rotation changed logical bounds")
        }
        var scrollPolicy=NativeScrollState()
        precondition(!scrollPolicy.observe(compact:false,y:509,delta:509,viewportChanged:false,now:1,threshold:24),"Script scrolling is not native intent")
        scrollPolicy.record(1,now:1,source:"native wheel")
        precondition(scrollPolicy.observe(compact:false,y:539,delta:30,viewportChanged:false,now:1.01,threshold:24))
        precondition(scrollPolicy.observe(compact:true,y:521,delta:-18,viewportChanged:true,now:1.02,threshold:24),"Viewport clamp must not expand")
        scrollPolicy.record(-1,now:1.03,source:"native wheel")
        precondition(!scrollPolicy.observe(compact:true,y:509,delta:-12,viewportChanged:false,now:1.04,threshold:24),"Reverse gesture expands above zero")
        precondition(scrollPolicy.observe(compact:true,y:0,delta:-509,viewportChanged:false,now:2,threshold:24),"Expired intent rejects a later script")
        let oldFrame=host.web.frame,oldBounds=host.bounds
        var darkOptions=host.options;darkOptions["shellAppearance"]="dark"
        host.configure(darkOptions)
        precondition(host.appearance?.bestMatch(from:[.darkAqua,.aqua]) == .darkAqua)
        precondition(host.web.appearance?.bestMatch(from:[.darkAqua,.aqua]) == .darkAqua)
        precondition(host.web.frame==oldFrame && host.bounds==oldBounds,"Appearance must preserve native geometry")
        host.clock.stringValue="stale-clock"
        host.clockTimer?.fire()
        precondition(host.clock.stringValue.range(of:"^[0-9]{2}:[0-9]{2}$",options:.regularExpression) != nil,"A static page clock refreshes without scrolling")
        print("Native layout checks passed: tools host resize/ephemeral store, Inspector attachment/shortcuts, fractional Fit, clip resize, logical bounds, insets, passive footer color, four presentation rotations, coordinate round trips, native scroll intent and independent appearance")
    }
}
