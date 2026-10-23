import AppKit
import WebKit
import QuartzCore
import Darwin
import Network

private struct NativeProxySettings {
    var mode="system"
    var endpoint:String?

    @available(macOS 14.0,*) func configuration()->(ProxyConfiguration?,String?) {
        if mode=="system" {return (nil,nil)}
        if mode=="direct" {return (nil,"Direct mode is unavailable through the system WKWebView proxy API. Use system or a custom proxy.")}
        guard mode=="custom",let endpoint,let url=URLComponents(string:endpoint),
              let scheme=url.scheme?.lowercased(),["http","https","socks5"].contains(scheme),
              let host=url.host,!host.isEmpty,
              url.user==nil,url.password==nil,url.query==nil,url.fragment==nil,
              url.path.isEmpty || url.path=="/"
        else {return (nil,"Proxy requires an unauthenticated http, https or socks5 endpoint with a valid port.")}
        let port=url.port ?? (scheme=="http" ? 80:(scheme=="https" ? 443:0))
        guard (1...65535).contains(port),let nativePort=NWEndpoint.Port(rawValue:UInt16(port))
        else{return (nil,"Proxy requires a port between 1 and 65535.")}
        let nativeHost=host.hasPrefix("[") && host.hasSuffix("]") ? String(host.dropFirst().dropLast()):host
        let address=NWEndpoint.hostPort(host:NWEndpoint.Host(nativeHost),port:nativePort)
        let config=scheme=="socks5" ? ProxyConfiguration(socksv5Proxy:address):
            ProxyConfiguration(httpCONNECTProxy:address,tlsOptions:scheme=="https" ? NWProtocolTLS.Options():nil)
        return (config,nil)
    }

    func apply(to store:WKWebsiteDataStore)->String? {
        guard #available(macOS 14.0,*) else{return "Proxy configuration requires macOS 14 or newer."}
        let (config,error)=configuration()
        if let error {return error}
        store.proxyConfigurations=config.map{[$0]} ?? []
        return nil
    }
}
private var initialProxySettings=NativeProxySettings()

func setInitialDeveloperProxy(mode:String,endpoint:String?)->String? {
    guard #available(macOS 14.0,*) else{return "Proxy configuration requires macOS 14 or newer."}
    let settings=NativeProxySettings(mode:mode,endpoint:endpoint)
    let (_,error)=settings.configuration()
    if error==nil {initialProxySettings=settings}
    return error
}

func maskPath(_ svg: String) -> CGPath? {
    let regex=try! NSRegularExpression(pattern:"[MLCZ]|[-+]?(?:[0-9]*\\.)?[0-9]+(?:[eE][-+]?[0-9]+)?")
    let ns=svg as NSString,tokens=regex.matches(in:svg,range:NSRange(location:0,length:ns.length)).map {ns.substring(with:$0.range)}
    if tokens.isEmpty{return nil};let path=CGMutablePath();var i=0
    func n()->CGFloat{defer{i+=1};return CGFloat(Double(tokens[i]) ?? 0)}
    while i<tokens.count {let command=tokens[i];i+=1;switch command {
        case "M":guard i+1<tokens.count else{return nil};path.move(to:CGPoint(x:n(),y:n()))
        case "L":guard i+1<tokens.count else{return nil};path.addLine(to:CGPoint(x:n(),y:n()))
        case "C":guard i+5<tokens.count else{return nil};let a=CGPoint(x:n(),y:n()),b=CGPoint(x:n(),y:n()),c=CGPoint(x:n(),y:n());path.addCurve(to:c,control1:a,control2:b)
        case "Z":path.closeSubpath()
        default:return nil
    }};return path
}
struct NativeScrollState {
    var direction=0,sequence=0
    var lastInput:TimeInterval=0,distance:Double=0
    var source="none"
    mutating func reset(){direction=0;distance=0;source="none"}
    mutating func record(_ sign:Int,now:TimeInterval,source:String){
        guard sign != 0 else{return}
        if direction != sign || now-lastInput>0.35 {distance=0}
        direction=sign;lastInput=now;self.source=source;sequence+=1
    }
    mutating func observe(compact:Bool,y:Double,delta:Double,viewportChanged:Bool,now:TimeInterval,threshold:Double)->Bool {
        guard !viewportChanged,direction != 0,delta != 0 else{return compact}
        if now-lastInput>0.35 {reset();return compact}
        guard (delta>0 ? 1:-1)==direction else{return compact}
        distance+=abs(delta)
        if direction<0 && (y<=2 || distance>8){return false}
        if direction>0 && distance>threshold{return true}
        return compact
    }
}
final class ShellWebView:WKWebView {
    var onScrollInput:((Int,String)->Void)?
    var onInspectorShortcut:(()->Void)?
    override func performKeyEquivalent(with event:NSEvent)->Bool {
        let modifiers=event.modifierFlags.intersection([.command,.control,.option,.shift])
        let inspectorKey=(event.keyCode==111 && modifiers.isEmpty) ||
            (event.charactersIgnoringModifiers?.lowercased()=="i" && modifiers==[.control,.shift])
        if inspectorKey,let onInspectorShortcut {onInspectorShortcut();return true}
        return super.performKeyEquivalent(with:event)
    }
    override func scrollWheel(with event:NSEvent){
        if event.scrollingDeltaY != 0 {onScrollInput?(event.scrollingDeltaY<0 ? 1:-1,"native wheel/momentum")}
        super.scrollWheel(with:event)
    }
    override func keyDown(with event:NSEvent){
        if event.modifierFlags.intersection([.command,.control,.option]).isEmpty {
            let up=[UInt16(126),116,115].contains(event.keyCode) || (event.keyCode==49 && event.modifierFlags.contains(.shift))
            let down=[UInt16(125),121,119,49].contains(event.keyCode)
            if up || down {onScrollInput?(up ? -1:1,"native keyboard")}
        }
        super.keyDown(with:event)
    }
}
class FlippedView:NSView {override var isFlipped:Bool{true}}
class PassiveView:FlippedView {override func hitTest(_ point:NSPoint)->NSView?{nil}}
final class WifiStatusView:PassiveView {
    var contentTintColor:NSColor? {didSet{needsDisplay=true}}
    override func draw(_ dirtyRect:NSRect){
        let ink=contentTintColor ?? .black
        ink.setStroke();ink.setFill()
        let size=min(bounds.width,bounds.height),scale=size/32
        let center=NSPoint(x:bounds.midX,y:bounds.midY)
        let ring=NSBezierPath(ovalIn:NSRect(x:center.x-size/2+1.5*scale,y:center.y-size/2+1.5*scale,width:size-3*scale,height:size-3*scale))
        ring.lineWidth=scale;ring.stroke()
        for radius in [CGFloat(3),5,7]{
            let path=NSBezierPath()
            path.move(to:NSPoint(x:center.x-radius*scale,y:center.y+(5-radius*0.75)*scale))
            path.curve(to:NSPoint(x:center.x+radius*scale,y:center.y+(5-radius*0.75)*scale),
                controlPoint1:NSPoint(x:center.x-radius*0.5*scale,y:center.y-radius*0.95*scale),
                controlPoint2:NSPoint(x:center.x+radius*0.5*scale,y:center.y-radius*0.95*scale))
            path.lineWidth=1.25*scale;path.stroke()
        }
        NSBezierPath(ovalIn:NSRect(x:center.x-scale,y:center.y+3*scale,width:2*scale,height:2*scale)).fill()
    }
}
@available(macOS 26.0,*) final class PassiveGlass:NSGlassEffectView {override func hitTest(_ point:NSPoint)->NSView?{nil}}
final class TabStripView:PassiveView {
    var slots:[[String:Any]]=[]
    var activeTitle:String?
    var uiStrings:[String:String]=[:]
    var font=NSFont.systemFont(ofSize:14,weight:.medium)
    var color=NSColor.black
    var baseColor=NSColor.white
    func rect(_ value:Any?)->NSRect {
        guard let r=value as? [String:NSNumber] else{return .zero}
        return NSRect(x:r["x"]?.doubleValue ?? 0,y:r["y"]?.doubleValue ?? 0,width:r["width"]?.doubleValue ?? 0,height:r["height"]?.doubleValue ?? 0)
    }
    func exposed(_ index:Int)->NSRect {
        var box=rect(slots[index]["visualFrame"] ?? slots[index]["frame"])
        if index+1<slots.count {let next=rect(slots[index+1]["visualFrame"] ?? slots[index+1]["frame"]);box.size.width=min(box.width,max(0,next.minX-box.minX-3))}
        return box
    }
    func geometry()->[[String:Any]] {
        slots.enumerated().map { index,slot in
            let v=rect(slot["visualFrame"] ?? slot["frame"]),e=exposed(index)
            return ["active":slot["active"] ?? false,"hitFrame":slot["frame"] ?? [:],"visualFrame":["x":v.minX,"y":v.minY,"width":v.width,"height":v.height],"exposedFrame":["x":e.minX,"y":e.minY,"width":e.width,"height":e.height],"closeFrame":slot["closeFrame"] ?? NSNull()]
        }
    }
    override func draw(_ dirtyRect:NSRect){
        guard !slots.isEmpty else{return}
        let track=NSRect(x:16,y:0,width:max(1,bounds.width-32),height:36)
        color.withAlphaComponent(0.08).setFill();NSBezierPath(roundedRect:track,xRadius:18,yRadius:18).fill()
        let paragraph=NSMutableParagraphStyle();paragraph.lineBreakMode = .byTruncatingTail
        let attributes:[NSAttributedString.Key:Any]=[.font:font,.foregroundColor:color,.paragraphStyle:paragraph]
        let lineHeight=font.ascender-font.descender+font.leading
        for (index,slot) in slots.enumerated(){
            let full=rect(slot["visualFrame"] ?? slot["frame"]),visible=exposed(index),active=slot["active"] as? Bool == true
            guard visible.width>0 else{continue}
            let box=full.offsetBy(dx:0,dy:-80),clip=visible.offsetBy(dx:0,dy:-80)
            if !active && index+1<slots.count && slots[index+1]["active"] as? Bool != true {
                let divider=NSBezierPath();divider.move(to:NSPoint(x:clip.maxX+1.5,y:7));divider.line(to:NSPoint(x:clip.maxX+1.5,y:29));divider.lineWidth=0.7;color.withAlphaComponent(0.2).setStroke();divider.stroke()
            }
            NSGraphicsContext.saveGraphicsState();NSBezierPath(rect:clip).addClip()
            if active {
                let path=NSBezierPath(roundedRect:box,xRadius:16,yRadius:16)
                let light=(color.usingColorSpace(.sRGB)?.redComponent ?? 0) < 0.5
                (light ? NSColor.white.withAlphaComponent(0.92):NSColor(srgbRed:0.35,green:0.4,blue:0.46,alpha:0.82)).setFill();path.fill()
                color.withAlphaComponent(0.18).setStroke();path.lineWidth=0.65;path.stroke()
                let center=NSPoint(x:box.minX+16,y:box.midY)
                color.setFill();NSBezierPath(ovalIn:NSRect(x:center.x-7,y:center.y-7,width:14,height:14)).fill()
                let cross=NSBezierPath();cross.move(to:NSPoint(x:center.x-2.5,y:center.y-2.5));cross.line(to:NSPoint(x:center.x+2.5,y:center.y+2.5));cross.move(to:NSPoint(x:center.x-2.5,y:center.y+2.5));cross.line(to:NSPoint(x:center.x+2.5,y:center.y-2.5));cross.lineWidth=1.4;(light ? NSColor.white:NSColor.black).setStroke();cross.stroke()
            }
            let fallback=(uiStrings["Shell.PageTab"] ?? "Page {0}").replacingOccurrences(of:"{0}",with:String(index+1))
            let title:String
            if active,let activeTitle,!activeTitle.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty {title=activeTitle}
            else {title=fallback}
            let contentBox=active ? box:clip
            let content=NSRect(x:contentBox.minX+(active ? 32:0)+8,y:box.minY,width:max(0,contentBox.width-(active ? 32:0)-16),height:box.height)
            let titleWidth=min(max(0,content.width-24),(title as NSString).size(withAttributes:attributes).width)
            let groupWidth=16+8+titleWidth,iconX=content.minX+max(0,(content.width-groupWidth)/2)
            if content.width>=24 {
                let icon=NSRect(x:iconX,y:box.midY-8,width:16,height:16)
                color.withAlphaComponent(0.14).setFill();NSBezierPath(roundedRect:icon,xRadius:4,yRadius:4).fill()
                let numberAttributes:[NSAttributedString.Key:Any]=[.font:NSFont.systemFont(ofSize:12,weight:.medium),.foregroundColor:NSColor.white]
                let number="1" as NSString,size=number.size(withAttributes:numberAttributes);number.draw(at:NSPoint(x:icon.midX-size.width/2,y:icon.midY-size.height/2),withAttributes:numberAttributes)
                (title as NSString).draw(in:NSRect(x:iconX+24,y:box.midY-lineHeight/2,width:max(0,content.maxX-iconX-24),height:lineHeight),withAttributes:attributes)
            }
            NSGraphicsContext.restoreGraphicsState()
        }
    }
}
final class GlassPanel:FlippedView {
    let surface:NSView
    let tint=PassiveView()
    let content=FlippedView()
    var visualSize:CGSize?
    var onClick:(()->Void)?
    var visualRect:NSRect {let size=visualSize ?? bounds.size;return NSRect(x:(bounds.width-size.width)/2,y:(bounds.height-size.height)/2,width:size.width,height:size.height)}
    override init(frame:NSRect){
        if #available(macOS 26.0,*) {let g=PassiveGlass();g.style = .clear;surface=g}
        else {let e=NSVisualEffectView();e.material = .hudWindow;e.blendingMode = .withinWindow;e.state = .active;surface=e}
        super.init(frame:frame);wantsLayer=true;surface.wantsLayer=true;tint.wantsLayer=true
        addSubview(surface);addSubview(tint);addSubview(content)
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    func style(color:NSColor,alpha:CGFloat,strength:CGFloat,shadow:Float,radius:CGFloat,role:String="floating",dark:Bool=false){
        let rgb=color.usingColorSpace(.sRGB) ?? color
        let darkBackdrop=rgb.redComponent*0.2126+rgb.greenComponent*0.7152+rgb.blueComponent*0.0722<0.36
        let color=role=="top" ? (dark ? NSColor(srgbRed:0.169,green:0.235,blue:0.31,alpha:1):.white):(dark ? (darkBackdrop ? NSColor(srgbRed:0.55,green:0.59,blue:0.65,alpha:1):NSColor(srgbRed:0.396,green:0.451,blue:0.51,alpha:1)):.white)
        let alpha=min(0.95,alpha*(role=="top" ? (dark ? 2.4:2.7):(role=="compact" ? (dark ? (darkBackdrop ? 0.65:2.5):(darkBackdrop ? 2:0.2)):(dark ? (darkBackdrop ? 1.1:2.5):(darkBackdrop ? 2:0.55)))))
        let shadow=role=="top" ? 0:(role=="compact" ? shadow*0.15:shadow)
        layer?.shadowColor=NSColor.black.cgColor;layer?.shadowOpacity=shadow;layer?.shadowRadius=10;layer?.shadowOffset=CGSize(width:0,height:-3)
        surface.alphaValue=role=="top" ? min(1,strength*2):strength;surface.layer?.cornerRadius=radius;surface.layer?.masksToBounds=true
        if #available(macOS 26.0,*),let glass=surface as? NSGlassEffectView {glass.cornerRadius=radius;glass.tintColor=color.withAlphaComponent(alpha)}
        tint.layer?.backgroundColor=color.withAlphaComponent(alpha).cgColor;tint.layer?.cornerRadius=radius;tint.layer?.masksToBounds=true
        tint.layer?.borderColor=NSColor.white.withAlphaComponent(0.3).cgColor;tint.layer?.borderWidth=role=="top" ? 0:(role=="compact" ? 0.1:0.5)
        layer?.shadowPath=CGPath(roundedRect:visualRect,cornerWidth:radius,cornerHeight:radius,transform:nil)
    }
    override func layout(){super.layout();surface.frame=visualRect;tint.frame=visualRect;content.frame=visualRect}
    override func mouseDown(with event:NSEvent){onClick?()}
}
// The view handed to Avalonia. Avalonia moves a native child but does not clip it to scrolling
// ancestors, so the host sizes this view to the visible preview area and offsets the full-size
// container inside it. The container's own frame, scene scale and telemetry are unchanged.
final class ClipHostView:FlippedView {
    weak var content:NSView?
    var contentSize=CGSize.zero
    var contentOffset=CGPoint.zero
    var clockwiseDegrees=0
    override init(frame:NSRect){super.init(frame:frame);autoresizesSubviews=false;wantsLayer=true;layer?.masksToBounds=true}
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    override func setFrameSize(_ newSize:NSSize){
        let changed=frame.size != newSize
        super.setFrameSize(newSize)
        // NativeControlHost resizes this view after Avalonia layout. AppKit may otherwise
        // autoresize the content before our explicit full-size/offset layout runs.
        if changed {needsLayout=true;layoutSubtreeIfNeeded()}
    }
    override func layout(){
        super.layout()
        // Before the host reports a viewport, the container fills the clip view as an unclipped child would.
        let size=contentSize == .zero ? bounds.size:contentSize
        guard let content else{return}
        let swapped=clockwiseDegrees % 180 != 0
        let sceneSize=swapped ? CGSize(width:size.height,height:size.width):size
        // Use an NSView transform, rather than only a layer transform, so AppKit's
        // coordinate conversion and hit testing include the content/body rotation.
        content.frameRotation=0
        content.frame=NSRect(origin:.zero,size:sceneSize)
        content.frameRotation=CGFloat(clockwiseDegrees)
        let box=content.convert(content.bounds,to:self)
        content.setFrameOrigin(NSPoint(x:content.frame.origin.x-box.minX-contentOffset.x,
                                      y:content.frame.origin.y-box.minY-contentOffset.y))
    }
}
final class CalibrationContainer:FlippedView,WKScriptMessageHandler,WKNavigationDelegate {
    let clipView=ClipHostView()
    var options:[String:Any]
    let web:ShellWebView
    var canvas:CGSize
    var originalMask:CGPath?
    let address=GlassPanel(),back=GlassPanel(),bookmarks=GlassPanel(),tabGroup=GlassPanel(),topBackground=GlassPanel()
    let addressLabel=NSTextField(labelWithString:"127.0.0.1")
    let titleLabel=NSTextField(labelWithString:"New tab")
    let clock=NSTextField(labelWithString:"")
    let camera=PassiveView(),wifi=WifiStatusView(),sideBackground=PassiveView()
    let fixedBottomBackground=PassiveView()
    // WebKit restores docking asynchronously, after connect() already reports a
    // frontend. A hidden attachment target makes docking unavailable without ever
    // giving the inspector permission to resize the measured WKWebView.
    let inspectorAttachment=NSView(frame:.zero)
    var detachedInspectorAvailable=false
    var nativeFixedContainerColorsAvailable=false
    let tabStrip=TabStripView()
    var buttons:[NSButton]=[]
    var nativeScroll=NativeScrollState()
    var snapshotGeneration=0
    var clockTimer:Timer?
    var compact=false,scrollPosition:Double=0,lastHost="127.0.0.1",tintColor=NSColor(srgbRed:0.94,green:0.95,blue:0.97,alpha:1)
    var tintSource="initial fallback",snapshotWork:DispatchWorkItem?,latestObservation:[String:Any]=[:]
    var invalidated=false
    // Host navigation binding. The callback is cleared in invalidate() before the host frees its context.
    var navigationCallback:NavigationCallback?
    var navigationContext:UnsafeMutableRawPointer?
    var navigationObservations:[NSKeyValueObservation]=[]
    var navigationError:[String:Any]?
    var pageTitleCommitted=false
    var pageTitleDocumentId:String?
    var developerError=""
    var inspectorRequested=false
    var inspectorWork:DispatchWorkItem?
    var remoteInspectionEnabled=false
    var remoteDebuggingCallback:NavigationCallback?
    var remoteDebuggingContext:UnsafeMutableRawPointer?
    var remoteDebuggingScript:WKUserScript?
    var developerDocumentGeneration=0
    var developerScriptGeneration=0
    func d(_ key:String,_ fallback:Double)->CGFloat{CGFloat((options[key] as? NSNumber)?.doubleValue ?? fallback)}
    func ui(_ key:String,_ fallback:String)->String{(options["uiStrings"] as? [String:String])?[key] ?? fallback}
    var safari:Bool{options["mode"] as? String == "safari"}
    var landscape:Bool{d("logicalWidth",466)>d("logicalHeight",678)}
    var sidebarLeft:Bool{landscape && Int(d("rotationClockwiseDegrees",0)) == 270}
    var side:Bool{options["chromeLayout"] as? String == "side"}
    var showTabStrip:Bool{(options["showTabStrip"] as? Bool) ?? true}
    var openExpandedTop:CGFloat{showTabStrip ? d("openTopExpanded",126):d("openTopSingleExpanded",80)}
    var referenceScene:[String:Any]{options["referenceScene"] as? [String:Any] ?? [:]}
    var referenceState:[String:Any]{(referenceScene[!side && !showTabStrip ? "singleStates":"states"] as? [String:Any])?[compact ? "scrolled":"initial"] as? [String:Any] ?? [:]}
    var materialBackend:String {if #available(macOS 26.0,*){return "macOS NSGlassEffectView clear"};return "macOS NSVisualEffectView withinWindow"}
    init(url:String,svg:String,width:Double,height:Double,options:[String:Any],observer:String){
        self.options=options;canvas=CGSize(width:width,height:height);originalMask=maskPath(svg)
        let configuration=WKWebViewConfiguration()
        // Configure the shared persistent store before the first request is created.
        let initialProxyError=initialProxySettings.apply(to:configuration.websiteDataStore)
        let extrasSelector=NSSelectorFromString("_setDeveloperExtrasEnabled:")
        if configuration.preferences.responds(to:extrasSelector),let implementation=configuration.preferences.method(for:extrasSelector) {
            typealias BoolSetter=@convention(c)(AnyObject,Selector,Bool)->Void
            unsafeBitCast(implementation,to:BoolSetter.self)(configuration.preferences,extrasSelector,true)
        }
        let fixedColorSelector=NSSelectorFromString("_setContentInsetBackgroundFillEnabled:")
        var fixedColorPreferenceAvailable=false
        if configuration.preferences.responds(to:fixedColorSelector),let implementation=configuration.preferences.method(for:fixedColorSelector) {
            typealias BoolSetter=@convention(c)(AnyObject,Selector,Bool)->Void
            unsafeBitCast(implementation,to:BoolSetter.self)(configuration.preferences,fixedColorSelector,true)
            fixedColorPreferenceAvailable=true
        }
        web=ShellWebView(frame:.zero,configuration:configuration)
        // Use the observed mobile Safari compatibility identity before the first
        // request. App WebView has no separately measured identity in this cohort.
        web.customUserAgent=options["userAgent"] as? String
        super.init(frame:.zero);autoresizesSubviews=false;wantsLayer=true
        let attachmentSelector=NSSelectorFromString("_setInspectorAttachmentView:")
        if web.responds(to:attachmentSelector),let implementation=web.method(for:attachmentSelector) {
            inspectorAttachment.isHidden=true
            typealias AttachmentSetter=@convention(c)(AnyObject,Selector,NSView)->Void
            unsafeBitCast(implementation,to:AttachmentSetter.self)(web,attachmentSelector,inspectorAttachment)
            detachedInspectorAvailable=true
        }
        nativeFixedContainerColorsAvailable=fixedColorPreferenceAvailable && web.responds(to:NSSelectorFromString("_updateFixedColorExtensionViews"))
        developerError=initialProxyError ?? ""
        clipView.content=self;clipView.addSubview(self)
        configuration.userContentController.add(self,name:"devduoShell")
        configuration.userContentController.add(self,name:"devduoRemoteDebugging")
        configuration.userContentController.addUserScript(WKUserScript(source:observer,injectionTime:.atDocumentEnd,forMainFrameOnly:true))
        // WebKit owns the canvas and under-page fill, including obscured insets.
        // Making the view transparent exposes the host's neutral background at document end.
        web.navigationDelegate=self;if #available(macOS 13.3,*){web.isInspectable=false}
        addSubview(web)
        web.onScrollInput={ [weak self] direction,source in
            self?.nativeScroll.record(direction,now:ProcessInfo.processInfo.systemUptime,source:source)
        }
        web.onInspectorShortcut={ [weak self] in
            guard let self else{return}
            _=self.setInspectorVisible(!(self.inspectorRequested || self.inspectorFlag("isVisible")))
        }
        applyShellAppearance()
        fixedBottomBackground.wantsLayer=true;fixedBottomBackground.isHidden=true;addSubview(fixedBottomBackground)
        sideBackground.wantsLayer=true;addSubview(sideBackground)
        for p in [topBackground,address,back,bookmarks,tabGroup]{addSubview(p)}
        address.onClick = { [weak self] in self?.compact=false;self?.needsLayout=true }
        addressLabel.alignment = .center;addressLabel.font = .systemFont(ofSize:17,weight:.medium);addressLabel.setAccessibilityLabel(ui("Shell.Address","Safari approximation address"))
        address.content.addSubview(addressLabel)
        titleLabel.font = .systemFont(ofSize:12,weight:.medium);titleLabel.alignment = .center;topBackground.content.addSubview(titleLabel)
        clock.font = .systemFont(ofSize:21,weight:.semibold);clock.alignment = .center;addSubview(clock)
        camera.wantsLayer=true;camera.layer?.backgroundColor=NSColor.black.cgColor;addSubview(camera)
        addSubview(tabStrip);tabStrip.setAccessibilityLabel(ui("Shell.TabStrip","Safari tab strip illustration"))
        wifi.setAccessibilityElement(true);wifi.setAccessibilityRole(.image)
        wifi.setAccessibilityLabel(ui("Shell.Wifi","Wi-Fi illustration"));addSubview(wifi)
        makeButton("chevron.backward",ui("Shell.Back","Back"),#selector(goBack),in:back.content)
        makeButton("book",ui("Shell.Bookmarks","Bookmarks approximation"),#selector(expand),in:bookmarks.content)
        makeButton("plus",ui("Shell.NewTab","New tab"),#selector(newTab),in:tabGroup.content)
        makeButton("square.on.square",ui("Shell.Tabs","Tabs approximation"),#selector(expand),in:tabGroup.content)
        makeButton("line.3.horizontal",ui("Shell.Menu","Page menu approximation"),#selector(expand),in:address.content)
        makeButton("arrow.clockwise",ui("Shell.Reload","Reload"),#selector(reload),in:address.content)
        updateLocalizedLabels()
        observeNavigation()
        clockTimer=Timer.scheduledTimer(withTimeInterval:15,repeats:true){ [weak self] _ in
            guard let self,!self.invalidated else{return}
            let formatter=DateFormatter();formatter.dateFormat="HH:mm"
            if self.clock.stringValue != formatter.string(from:Date()){self.updateAppearance();self.publishState()}
        }
        if !url.isEmpty,let target=URL(string:url){web.load(URLRequest(url:target))}
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    override func setFrameSize(_ newSize:NSSize){
        let changed=frame.size != newSize
        super.setFrameSize(newSize)
        if changed {needsLayout=true}
    }
    func makeButton(_ symbol:String,_ label:String,_ action:Selector,in parent:NSView){
        let b=NSButton(image:NSImage(systemSymbolName:symbol,accessibilityDescription:label) ?? NSImage(),target:self,action:action)
        b.isBordered=false;b.imagePosition = .imageOnly;b.focusRingType = .none;b.imageScaling = .scaleProportionallyDown;b.setAccessibilityLabel(label);b.toolTip=label;parent.addSubview(b);buttons.append(b)
    }
    @objc func goBack(){if web.canGoBack{web.goBack()}}
    @objc func reload(){web.reload()}
    @objc func expand(){compact=false;needsLayout=true}
    @objc func newTab(){compact=false;nativeScroll.reset();needsLayout=true;web.evaluateJavaScript("window.scrollTo({top:0,behavior:'smooth'})")}
    func applyShellAppearance(){
        appearance=NSAppearance(named:options["shellAppearance"] as? String == "dark" ? .darkAqua:.aqua)
        web.appearance=appearance
    }
    func configure(_ updated:[String:Any]){snapshotGeneration+=1;options=updated;applyShellAppearance();updateLocalizedLabels();needsLayout=true;layoutSubtreeIfNeeded();updateAppearance()}
    func updateLocalizedLabels(){
        addressLabel.setAccessibilityLabel(ui("Shell.Address","Safari approximation address"))
        camera.setAccessibilityElement(true);camera.setAccessibilityRole(.image)
        camera.setAccessibilityLabel(ui("Shell.Camera","Camera illustration"))
        tabStrip.setAccessibilityLabel(ui("Shell.TabStrip","Safari tab strip illustration"))
        wifi.setAccessibilityLabel(ui("Shell.Wifi","Wi-Fi illustration"))
        tabStrip.uiStrings=options["uiStrings"] as? [String:String] ?? [:]
        let keys=["Shell.Back","Shell.Bookmarks","Shell.NewTab","Shell.Tabs","Shell.Menu","Shell.Reload"]
        let fallbacks=["Back","Bookmarks approximation","New tab","Tabs approximation","Page menu approximation","Reload"]
        for (index,button) in buttons.enumerated(){let text=ui(keys[index],fallbacks[index]);button.setAccessibilityLabel(text);button.toolTip=text}
    }
    func navigate(_ url:String){compact=false;needsLayout=true;if let target=URL(string:url){web.load(URLRequest(url:target))}}
    func showHtml(_ html:String,baseURL:String="http://devduo.local/"){compact=false;needsLayout=true;web.loadHTMLString(html,baseURL:URL(string:baseURL))}
    func nativeInspector()->NSObject? {
        let selector=NSSelectorFromString("_inspector")
        guard web.responds(to:selector) else{return nil}
        return web.perform(selector)?.takeUnretainedValue() as? NSObject
    }
    func inspectorFlag(_ name:String)->Bool {
        let selector=NSSelectorFromString(name)
        guard let inspector=nativeInspector(),inspector.responds(to:selector),let implementation=inspector.method(for:selector) else{return false}
        typealias BoolGetter=@convention(c)(AnyObject,Selector)->Bool
        return unsafeBitCast(implementation,to:BoolGetter.self)(inspector,selector)
    }
    func setInspectorVisible(_ visible:Bool)->Bool {
        developerError=""
        guard !invalidated else{developerError="The native browser has been disposed.";return false}
        if visible && remoteInspectionEnabled {developerError="Stop native remote inspection before opening the local Web Inspector.";return false}
        guard detachedInspectorAvailable,let inspector=nativeInspector(),["show","close","isConnected","isVisible"].allSatisfy({inspector.responds(to:NSSelectorFromString($0))})
        else{developerError="This system WebKit does not expose the required native Web Inspector interface.";return false}
        inspectorWork?.cancel();inspectorWork=nil;inspectorRequested=visible
        if !visible {_=inspector.perform(NSSelectorFromString("close"));return true}
        _=inspector.perform(NSSelectorFromString("show"))
        showDetachedInspector(attempt:0)
        return true
    }
    func showDetachedInspector(attempt:Int) {
        guard !invalidated,inspectorRequested,nativeInspector() != nil else{return}
        if inspectorFlag("isConnected") && inspectorFlag("isVisible") {
            inspectorRequested=false;inspectorWork=nil
        } else if attempt<100 {
            let work=DispatchWorkItem{[weak self] in self?.showDetachedInspector(attempt:attempt+1)}
            inspectorWork=work;DispatchQueue.main.asyncAfter(deadline:.now()+0.05,execute:work)
        } else {inspectorRequested=false;developerError="Native Web Inspector did not become visible before the timeout."}
    }
    func setRemoteInspection(_ enabled:Bool)->Bool {
        developerError=""
        guard !invalidated else{developerError="The native browser has been disposed.";return false}
        guard #available(macOS 13.3,*) else{developerError="Native remote inspection requires macOS 13.3 or newer.";return false}
        if enabled && (inspectorRequested || inspectorFlag("isConnected") || inspectorFlag("isVisible")) {
            developerError="Close the local Web Inspector before enabling native remote inspection.";return false
        }
        web.isInspectable=enabled
        remoteInspectionEnabled=web.isInspectable
        return remoteInspectionEnabled==enabled
    }
    func setProxy(mode:String,endpoint:String?)->Bool {
        developerError=""
        guard !invalidated else{developerError="The native browser has been disposed.";return false}
        let next=NativeProxySettings(mode:mode,endpoint:endpoint)
        if let error=next.apply(to:web.configuration.websiteDataStore){developerError=error;return false}
        initialProxySettings=next
        return true
    }
    func sendRemoteDebuggingMessage(_ json:String) {
        guard !invalidated,remoteDebuggingScript != nil,let callback=remoteDebuggingCallback else{return}
        json.withCString{callback(remoteDebuggingContext,$0)}
    }
    func evaluateRemoteDebuggingScript(_ source:String)->Bool {
        developerError=""
        guard !invalidated else{developerError="The native browser has been disposed.";return false}
        let generation=developerDocumentGeneration
        let scriptGeneration=developerScriptGeneration
        web.evaluateJavaScript(source){[weak self] _,error in
            guard let self,!self.invalidated,generation==self.developerDocumentGeneration,
                  scriptGeneration==self.developerScriptGeneration,let error else{return}
            let payload=["type":"error","message":error.localizedDescription]
            if let data=try? JSONSerialization.data(withJSONObject:payload),let json=String(data:data,encoding:.utf8){self.sendRemoteDebuggingMessage(json)}
        }
        return true
    }
    func setRemoteDebuggingScript(_ source:String?)->Bool {
        developerError=""
        guard !invalidated else{developerError="The native browser has been disposed.";return false}
        developerScriptGeneration+=1
        let controller=web.configuration.userContentController
        let hadScript=remoteDebuggingScript != nil
        if let previous=remoteDebuggingScript {
            let retained=controller.userScripts.filter{$0 !== previous}
            controller.removeAllUserScripts()
            retained.forEach{controller.addUserScript($0)}
            remoteDebuggingScript=nil
        }
        guard let source else{
            if hadScript {_=evaluateRemoteDebuggingScript("window.__devduoRemoteDebugging?.disconnect(); delete window.__devduoPostDeveloperMessage;")}
            return true
        }
        let wrapped="window.__devduoPostDeveloperMessage = value => window.webkit.messageHandlers.devduoRemoteDebugging.postMessage(JSON.stringify({generation:\(developerScriptGeneration),message:String(value)}));\n"+source
        let script=WKUserScript(source:wrapped,injectionTime:.atDocumentStart,forMainFrameOnly:true)
        remoteDebuggingScript=script;controller.addUserScript(script)
        return evaluateRemoteDebuggingScript(wrapped)
    }
    func webView(_ webView:WKWebView,didStartProvisionalNavigation navigation:WKNavigation!){
        developerDocumentGeneration+=1
        pageTitleCommitted=false;pageTitleDocumentId=nil;setPageTitle(nil)
        compact=false;nativeScroll.reset();snapshotGeneration+=1;needsLayout=true
        latestObservation=[:];fixedBottomBackground.isHidden=true
        navigationError=nil
        publishNavigation()
    }
    func webView(_ webView:WKWebView,didCommit navigation:WKNavigation!){
        // A provisional load can retain the previous native title. Confirm the
        // committed document's observer identity before accepting title updates.
        pageTitleCommitted=true;pageTitleDocumentId=nil;setPageTitle(nil)
    }
    func webView(_ webView:WKWebView,didFinish navigation:WKNavigation!){publishNavigation()}
    func webView(_ webView:WKWebView,didFail navigation:WKNavigation!,withError error:Error){recordNavigationFailure(error)}
    func webView(_ webView:WKWebView,didFailProvisionalNavigation navigation:WKNavigation!,withError error:Error){recordNavigationFailure(error)}

    func observeNavigation(){
        // WebKit delivers these KVO changes on the main thread.
        let changed:()->Void={ [weak self] in self?.publishNavigation() }
        navigationObservations=[
            web.observe(\.url){_,_ in changed()},
            web.observe(\.title){ [weak self] _,_ in
                if let self,self.pageTitleCommitted,self.pageTitleDocumentId != nil {self.setPageTitle(self.web.title)}
                changed()
            },
            web.observe(\.isLoading){_,_ in changed()},
            web.observe(\.canGoBack){_,_ in changed()},
            web.observe(\.canGoForward){_,_ in changed()},
            web.observe(\.estimatedProgress){_,_ in changed()}
        ]
    }
    func recordNavigationFailure(_ error:Error){
        let failure=error as NSError
        // A superseded or stopped navigation is not a page failure; loading state already reports it.
        if failure.domain==NSURLErrorDomain && failure.code==NSURLErrorCancelled {publishNavigation();return}
        navigationError=["code":"\(failure.domain):\(failure.code)","message":failure.localizedDescription]
        publishNavigation()
    }
    func publishNavigation(){
        guard !invalidated,let callback=navigationCallback else{return}
        let payload:[String:Any]=[
            "url":web.url?.absoluteString ?? NSNull(),
            "title":web.title ?? NSNull(),
            "isLoading":web.isLoading,
            "progress":web.estimatedProgress,
            "canGoBack":web.canGoBack,
            "canGoForward":web.canGoForward,
            "error":navigationError ?? NSNull()
        ]
        guard let data=try? JSONSerialization.data(withJSONObject:payload,options:[.sortedKeys]),let json=String(data:data,encoding:.utf8) else{return}
        json.withCString{callback(navigationContext,$0)}
    }
    func invalidate(){
        _=setInspectorVisible(false);if #available(macOS 13.3,*){web.isInspectable=false};remoteInspectionEnabled=false
        inspectorWork?.cancel();inspectorWork=nil
        remoteDebuggingCallback=nil;remoteDebuggingContext=nil;remoteDebuggingScript=nil
        web.configuration.userContentController.removeScriptMessageHandler(forName:"devduoRemoteDebugging")
        invalidated=true;navigationCallback=nil;navigationContext=nil
        clockTimer?.invalidate();clockTimer=nil
        navigationObservations.forEach{$0.invalidate()};navigationObservations=[]
        snapshotWork?.cancel();web.onScrollInput=nil;web.onInspectorShortcut=nil;web.stopLoading();web.configuration.userContentController.removeScriptMessageHandler(forName:"devduoShell");web.navigationDelegate=nil
    }
    override func layout(){
        super.layout()
        // Scale the native coordinate space, keeping CSS layout at the calibrated scene size.
        let sceneSize=CGSize(width:d("logicalWidth",466),height:d("logicalHeight",678))
        if bounds.size != sceneSize {setBoundsSize(sceneSize)}
        // AppKit can round a scaled bounds height to e.g. 668.9999999999999.
        // WebKit truncates that to 668 CSS px. Arrange children in the exact
        // requested scene units; the ancestor transform owns presentation scale.
        let w=sceneSize.width,h=sceneSize.height
        guard w>0,h>0 else{return}
        if let mask=originalMask {var t=CGAffineTransform(scaleX:w/canvas.width,y:h/canvas.height);let layerMask=CAShapeLayer();layerMask.frame=bounds;layerMask.path=mask.copy(using:&t);layer?.mask=layerMask}
        let logicalW=d("logicalWidth",466),logicalH=d("logicalHeight",678),sx=w/logicalW,sy=h/logicalH
        func frame(_ x:CGFloat,_ y:CGFloat,_ width:CGFloat,_ height:CGFloat)->NSRect{NSRect(x:x*sx,y:y*sy,width:width*sx,height:height*sy)}
        func referenceFrame(_ id:String,_ fallback:NSRect)->NSRect{
            guard let r=(referenceState["frames"] as? [String:Any])?[id] as? [String:NSNumber] else{return fallback}
            return frame(CGFloat(r["x"]?.doubleValue ?? 0),CGFloat(r["y"]?.doubleValue ?? 0),CGFloat(r["width"]?.doubleValue ?? 0),CGFloat(r["height"]?.doubleValue ?? 0))
        }
        var viewport=NSRect(x:0,y:0,width:logicalW,height:logicalH)
        if safari {
            if side {let bottom=compact ? (logicalW>logicalH ? d("closedBottomCompactLandscape",80):d("closedBottomCompactPortrait",80)):d("closedBottomExpanded",98);viewport=NSRect(x:sidebarLeft ? d("closedSideWidth",84):0,y:0,width:max(1,logicalW-d("closedSideWidth",84)),height:max(1,logicalH-bottom))}
            else {let top=compact ? d("openTopCompact",51):openExpandedTop;viewport=NSRect(x:0,y:top,width:logicalW,height:max(1,logicalH-top))}
        }
        // Keep native painting behind closed-mode floating chrome. The page's
        // unobscured viewport is defined by WebKit, not a smaller backing view.
        let nativeUnderChrome=safari && side
        let nextWebFrame=nativeUnderChrome ? NSRect(origin:.zero,size:sceneSize):frame(viewport.minX,viewport.minY,viewport.width,viewport.height)
        if web.frame != nextWebFrame {web.frame=nextWebFrame}
        if #available(macOS 26.0,*) {
            if nativeUnderChrome {
                let smallBottom=min(d("closedBottomCompactPortrait",80),d("closedBottomCompactLandscape",80))
                let sideInset=d("closedSideWidth",84)*sx
                let minimum=NSEdgeInsets(top:0,left:sidebarLeft ? sideInset:0,bottom:smallBottom*sy,right:sidebarLeft ? 0:sideInset)
                let maximum=NSEdgeInsets(top:0,left:sidebarLeft ? sideInset:0,bottom:d("closedBottomExpanded",98)*sy,right:sidebarLeft ? 0:sideInset)
                web.setMinimumViewportInset(minimum,maximumViewportInset:maximum)
                web.obscuredContentInsets=NSEdgeInsets(top:0,left:sidebarLeft ? sideInset:0,bottom:(logicalH-viewport.height)*sy,right:sidebarLeft ? 0:sideInset)
            } else {
                web.obscuredContentInsets=NSEdgeInsets()
                web.setMinimumViewportInset(NSEdgeInsets(),maximumViewportInset:NSEdgeInsets())
            }
        }
        for p in [address,back,bookmarks,tabGroup,topBackground]{p.isHidden = !safari}
        sideBackground.isHidden = !safari || !side || nativeFixedContainerColorsAvailable
        sideBackground.frame=frame(sidebarLeft ? 0:logicalW-d("closedSideWidth",84),0,d("closedSideWidth",84),logicalH)
        camera.isHidden = !safari || !side
        for v in [clock,wifi]{v.isHidden = !safari || (side ? landscape:compact)}
        tabStrip.isHidden = compact || !safari || !showTabStrip || side
        guard safari else{publishState();return}
        if side {
            topBackground.isHidden=true
            let contentWidth=logicalW-d("closedSideWidth",84),fullWidth=min(d("closedAddressMaxWidth",334),contentWidth-48)
            let aw=compact ? d("closedCompactWidth",84):fullWidth,ah=compact ? d("closedCompactHeight",30):d("closedAddressHeight",48)
            let center=logicalW>logicalH ? logicalW/2:contentWidth/2
            let bottom=compact ? d("closedCompactBottom",34):d("closedAddressBottom",34)
            let opticalHeight=compact ? d("closedCompactVisualHeight",34.67):ah
            address.frame=frame(center-aw/2,logicalH-bottom-opticalHeight/2-ah/2,aw,ah)
            // These are screen-coordinate frames from the two distinct native Safari scenes.
            let sideX=sidebarLeft ? 24:logicalW-72
            let navTop=landscape ? (sidebarLeft ? d("closedLandscapeLeftNavTop",82):d("closedLandscapeRightNavTop",24)):174
            let tabsBottom=landscape ? (sidebarLeft ? d("closedLandscapeLeftTabsBottom",34):d("closedLandscapeRightTabsBottom",82)):34
            back.frame=frame(sideX,navTop,48,48);bookmarks.frame=frame(sideX,navTop+60,48,48)
            tabGroup.frame=frame(sideX,logicalH-tabsBottom-102,48,102)
            clock.frame=frame(logicalW-82,80,72,30);wifi.frame=frame(logicalW-65,112,38,32)
            let cameraCenter=landscape ? (sidebarLeft ? CGPoint(x:48,y:48):CGPoint(x:logicalW-48,y:logicalH-48)):CGPoint(x:logicalW-48,y:48)
            camera.frame=frame(cameraCenter.x-18,cameraCenter.y-18,36,36);camera.layer?.cornerRadius=18*min(sx,sy)
        } else {
            topBackground.frame=frame(0,0,logicalW,compact ? d("openTopCompact",51):openExpandedTop)
            let aw=min(logicalW-120,compact ? d("openCompactWidth",252):d("openAddressWidth",360)),ah=compact ? d("openCompactHeight",30.8):d("openAddressHeight",44)
            address.frame=frame((logicalW-aw)/2,compact ? d("openCompactTop",18.2667):d("openAddressTop",26),aw,ah)
            back.frame=frame(16,26,44,44);bookmarks.frame=frame(72,26,44,44);tabGroup.frame=frame(logicalW-232,26,88,44)
            back.isHidden=compact;bookmarks.isHidden=compact;tabGroup.isHidden=compact
            titleLabel.isHidden=true
            clock.frame=frame(logicalW-126,37,50,24);clock.font = .systemFont(ofSize:15*min(sx,sy),weight:.semibold)
            wifi.frame=frame(logicalW-65,37,34,24)
            tabStrip.frame=frame(0,80,logicalW,36)
            tabStrip.slots=(options["tabStripItems"] as? [[String:Any]]) ?? []
            tabStrip.font = .systemFont(ofSize:d("tabFontSize",14),weight:.medium);tabStrip.needsDisplay=true

        }
        back.frame=referenceFrame("BackButton",back.frame);bookmarks.frame=referenceFrame("SidebarButton",bookmarks.frame)
        if let first=(referenceState["frames"] as? [String:Any])?["NewTabButton"] as? [String:NSNumber] {
            let group=referenceFrame("NewTabButton",tabGroup.frame)
            tabGroup.frame=NSRect(x:group.minX,y:group.minY,width:(side ? 48:88)*sx,height:(side ? 102:44)*sy)
            _=first
        }
        address.frame=referenceFrame("address",address.frame)
        addressLabel.font = .systemFont(ofSize:compact ? d("compactFontSize",12):17,weight:.medium)
        if compact {let minimum=side ? d("closedCompactVisualMinWidth",86.33):d("openCompactVisualMinWidth",70);let opticalHeight=side ? d("closedCompactVisualHeight",34.67):d("openCompactVisualHeight",20);let textWidth=addressLabel.intrinsicContentSize.width/sx;address.visualSize=CGSize(width:max(minimum,textWidth+18)*sx,height:opticalHeight*sy)}else{address.visualSize=nil}
        let a=NSRect(origin:.zero,size:address.visualRect.size)
        addressLabel.frame=NSRect(x:compact ? 2:42*sx,y:max(0,(a.height-23*sy)/2),width:max(1,a.width-(compact ? 4:84*sx)),height:23*sy)
        buttons[4].isHidden=compact;buttons[5].isHidden=compact
        buttons[4].frame=NSRect(x:8*sx,y:0,width:30*sx,height:a.height);buttons[5].frame=NSRect(x:a.width-38*sx,y:0,width:30*sx,height:a.height)
        for (index,b) in buttons.enumerated(){
            b.symbolConfiguration=NSImage.SymbolConfiguration(pointSize:(index<4 ? 22:20)*min(sx,sy),weight:.regular)
        }
        buttons[0].frame=back.bounds.insetBy(dx:12*sx,dy:12*sy);buttons[1].frame=bookmarks.bounds.insetBy(dx:12*sx,dy:12*sy)
        if side {buttons[2].frame=NSRect(x:10*sx,y:9*sy,width:28*sx,height:28*sy);buttons[3].frame=NSRect(x:10*sx,y:63*sy,width:28*sx,height:28*sy)}
        else {buttons[2].frame=NSRect(x:8*sx,y:9*sy,width:28*sx,height:26*sy);buttons[3].frame=NSRect(x:52*sx,y:9*sy,width:28*sx,height:26*sy)}
        updateAppearance();publishState()
    }
    func updateFixedBottomBackground(){
        fixedBottomBackground.isHidden=true
        // The system engine's fixed-container extension owns all inset edges.
        // Keep the legacy DOM footer surface only where that capability is absent.
        guard !nativeFixedContainerColorsAvailable else{return}
        guard #available(macOS 26.0,*),safari,side,
              let rgb=latestObservation["fixedBottomColor"] as? [NSNumber],rgb.count==3 else{return}
        let insets=web.obscuredContentInsets
        guard insets.bottom>0 else{return}
        // A passive color surface outside the layout viewport; it never changes WebKit geometry.
        fixedBottomBackground.frame=NSRect(x:insets.left,y:bounds.height-insets.bottom,
            width:max(0,bounds.width-insets.left-insets.right),height:insets.bottom)
        fixedBottomBackground.layer?.backgroundColor=NSColor(srgbRed:rgb[0].doubleValue/255,
            green:rgb[1].doubleValue/255,blue:rgb[2].doubleValue/255,alpha:1).cgColor
        fixedBottomBackground.isHidden=false
    }
    func updateAppearance(){
        let c=tintColor.usingColorSpace(.sRGB) ?? tintColor
        let alpha=max(0,min(1,d("tintAlpha",0.32))),strength=max(0,min(1,d("glassStrength",0.92))),shadow=Float(max(0,min(1,d("shadowOpacity",0.16))))
        for p in [address,back,bookmarks,tabGroup,topBackground]{
            let role=p === topBackground ? "top":(p === address && compact ? "compact":"floating")
            p.style(color:c,alpha:alpha,strength:strength,shadow:shadow,radius:p === topBackground ? 0:min(d("cornerRadius",24),p.visualRect.height/2),role:role,dark:options["shellAppearance"] as? String == "dark");p.needsLayout=true
        }
        if let rgb=latestObservation["rootBackgroundColor"] as? [NSNumber],rgb.count==3 {sideBackground.layer?.backgroundColor=NSColor(srgbRed:rgb[0].doubleValue/255,green:rgb[1].doubleValue/255,blue:rgb[2].doubleValue/255,alpha:1).cgColor}
        let neutral=c.blended(withFraction:0.65,of:NSColor.windowBackgroundColor) ?? c
        layer?.backgroundColor=neutral.cgColor
        updateFixedBottomBackground()
        let dark=options["shellAppearance"] as? String == "dark"
        let text:NSColor=dark ? .white:.black
        tabStrip.color=text;tabStrip.baseColor=c;tabStrip.needsDisplay=true;for b in buttons{b.contentTintColor=text};addressLabel.textColor=text;titleLabel.textColor=text
        let root=(latestObservation["rootBackgroundColor"] as? [NSNumber]) ?? [239,243,248]
        let statusDark=root.count==3 && (root[0].doubleValue*0.2126+root[1].doubleValue*0.7152+root[2].doubleValue*0.0722)<0.36*255
        let statusColor=side ? (statusDark ? NSColor.white:NSColor.black):text
        clock.textColor=statusColor;wifi.contentTintColor=statusColor
        addressLabel.stringValue=lastHost
        let title=tabStrip.activeTitle ?? ""
        titleLabel.stringValue=title.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty ? ui("Shell.Title","Safari approximation"):title
        let formatter=DateFormatter();formatter.locale=Locale(identifier:options["uiLanguage"] as? String ?? "en");formatter.dateFormat="HH:mm";clock.stringValue=formatter.string(from:Date())
    }
    func setPageTitle(_ title:String?){
        tabStrip.activeTitle=title;tabStrip.needsDisplay=true
    }
    func synchronizePageTitle(_ body:[String:Any]){
        guard pageTitleCommitted,let id=body["documentId"] as? String else{return}
        if pageTitleDocumentId==id {setPageTitle(web.title ?? (body["title"] as? String));return}
        let generation=developerDocumentGeneration
        web.evaluateJavaScript("window.devduoShellObservation?.documentId"){ [weak self] actual,error in
            guard let self,!self.invalidated,self.pageTitleCommitted,
                  generation==self.developerDocumentGeneration,error==nil,(actual as? String)==id else{return}
            self.pageTitleDocumentId=id
            self.setPageTitle(self.web.title ?? (body["title"] as? String))
        }
    }
    func userContentController(_ userContentController:WKUserContentController,didReceive message:WKScriptMessage){
        if message.name=="devduoRemoteDebugging" {
            guard !invalidated,message.frameInfo.isMainFrame,message.webView===web,let text=message.body as? String,
                  let data=text.data(using:.utf8),let payload=(try? JSONSerialization.jsonObject(with:data)) as? [String:Any],
                  (payload["generation"] as? NSNumber)?.intValue==developerScriptGeneration,let json=payload["message"] as? String else{return}
            sendRemoteDebuggingMessage(json);return
        }
        guard !invalidated,let body=message.body as? [String:Any],body["type"] as? String == "devduo-shell-observation" else{return}
        if message.frameInfo.isMainFrame,message.webView===web {synchronizePageTitle(body)}
        let previousObservation=latestObservation
        latestObservation=body
        lastHost=(body["host"] as? String) ?? lastHost
        let y=(body["scrollY"] as? NSNumber)?.doubleValue ?? 0,delta=(body["deltaY"] as? NSNumber)?.doubleValue ?? 0;scrollPosition=y
        let oldHeight=(previousObservation["viewportHeight"] as? NSNumber)?.doubleValue
        let newHeight=(body["viewportHeight"] as? NSNumber)?.doubleValue
        let next=nativeScroll.observe(compact:compact,y:y,delta:delta,viewportChanged:oldHeight != newHeight,
            now:ProcessInfo.processInfo.systemUptime,threshold:Double(d("collapseThresholdCssPx",24)))
        if next != compact {compact=next;needsLayout=true}
        // Meta hints remain diagnostic inputs; original Safari controls follow the page backdrop.
        if tintSource != "WKWebView native edge snapshot",let values=body["domBackgroundColor"] as? [NSNumber],values.count==3 {setTint(values.map{$0.doubleValue},source:"computed content background fallback")}
        scheduleSnapshot()
        updateAppearance();publishState()
    }
    func setTint(_ rgb:[Double],source:String){tintColor=NSColor(srgbRed:rgb[0]/255,green:rgb[1]/255,blue:rgb[2]/255,alpha:1);tintSource=source}
    func scheduleSnapshot(){
        snapshotWork?.cancel();snapshotGeneration+=1
        let generation=snapshotGeneration
        let task=DispatchWorkItem { [weak self] in
            guard let self,!self.invalidated,self.web.bounds.width>1,self.web.bounds.height>1 else{return}
            let config=WKSnapshotConfiguration();let height=min(18,self.web.bounds.height)
            // takeSnapshot omits the under-inset continuation on this runtime.
            // Sample the actual unobscured content edge rather than its blank snapshot area.
            let insets=self.web.obscuredContentInsets
            let sampleWidth=max(1,self.web.bounds.width-insets.left-insets.right)
            let sampleY=self.side ? max(insets.top,self.web.bounds.height-insets.bottom-height):insets.top
            config.rect=NSRect(x:insets.left,y:sampleY,width:sampleWidth,height:height);config.snapshotWidth=64
            self.web.takeSnapshot(with:config){ [weak self] image,error in
                guard let self,!self.invalidated,self.snapshotGeneration==generation,let image,error==nil,let data=image.tiffRepresentation,let bitmap=NSBitmapImageRep(data:data) else{return}
                var r:CGFloat=0,g:CGFloat=0,b:CGFloat=0,total:CGFloat=0
                for y in 0..<bitmap.pixelsHigh {for x in 0..<bitmap.pixelsWide {if let c=bitmap.colorAt(x:x,y:y)?.usingColorSpace(.sRGB),c.alphaComponent>0.1 {r+=c.redComponent;g+=c.greenComponent;b+=c.blueComponent;total+=1}}}
                if total>0 {self.setTint([Double(r/total*255),Double(g/total*255),Double(b/total*255)],source:"WKWebView native edge snapshot");self.updateAppearance();self.publishState()}
            }
        };snapshotWork=task;DispatchQueue.main.asyncAfter(deadline:.now()+0.18,execute:task)
    }
    func state()->[String:Any]{
        let c=tintColor.usingColorSpace(.sRGB) ?? tintColor
        func rect(_ r:NSRect)->[String:CGFloat]{["x":r.minX,"y":r.minY,"width":r.width,"height":r.height]}
        var widgetFrames:[String:Any]=[:]
        if safari && (!compact || side) {
            widgetFrames["BackButton"]=rect(back.frame);widgetFrames["SidebarButton"]=rect(bookmarks.frame)
            widgetFrames["NewTabButton"]=rect(NSRect(x:tabGroup.frame.minX,y:tabGroup.frame.minY,width:(side ? 48:44)*bounds.width/d("logicalWidth",466),height:(side ? 48:44)*bounds.height/d("logicalHeight",678)))
            widgetFrames["TabOverviewButton"]=rect(NSRect(x:tabGroup.frame.minX+(side ? 0:44)*bounds.width/d("logicalWidth",466),y:tabGroup.frame.minY+(side ? 54:0)*bounds.height/d("logicalHeight",678),width:(side ? 48:44)*bounds.width/d("logicalWidth",466),height:(side ? 48:44)*bounds.height/d("logicalHeight",678)))
            if !camera.isHidden {widgetFrames["camera"]=rect(camera.frame)}
            if !clock.isHidden {widgetFrames["clock"]=rect(clock.frame);widgetFrames["wifi"]=rect(wifi.frame)}
        }
        let scaleX=frame.width/max(1,bounds.width),scaleY=frame.height/max(1,bounds.height)
        func dipRect(_ r:NSRect)->[String:CGFloat]{rect(NSRect(x:r.minX*scaleX,y:r.minY*scaleY,width:r.width*scaleX,height:r.height*scaleY))}
        let widgetDip=widgetFrames.mapValues { value -> Any in
            guard let v=value as? [String:CGFloat] else{return value}
            return ["x":v["x",default:0]*scaleX,"y":v["y",default:0]*scaleY,"width":v["width",default:0]*scaleX,"height":v["height",default:0]*scaleY]
        }
        return ["schemaVersion":2,"widgetFramesScenePoints":widgetFrames,"nativeWebViewFrameScenePoints":rect(web.frame),"addressHitFrameScenePoints":rect(address.frame),"rotationClockwiseDegrees":d("rotationClockwiseDegrees",0),"sidebarSide":safari && side ? (sidebarLeft ? "left":"right"):"absent","widgetFramesDip":widgetDip,"landscapeStatusWidgetsHidden":landscape && clock.isHidden && wifi.isHidden,"tabStripVisible":safari && !side && !compact && showTabStrip,"tabFontSizeScenePoints":d("tabFontSize",14),"tabStripGeometryScenePoints":tabStrip.isHidden ? []:tabStrip.geometry(),"tabStripRendering":"shared track; two illustrative whole-tab containers including close control; derived widths, original frames retained in referenceScene","referenceScene":referenceScene["id"] ?? "unmeasured","bodyRotationDegrees":referenceScene["bodyRotationDegrees"] ?? 0,"horizontalInsetFill":"document root color approximation; no duplicated page pixels","layoutCalibration":referenceScene["mappingStatus"] ?? "unmeasured","engine":"macOS system WKWebView","scrollIndicator":["enabled":false,"status":"host-scene-policy-unavailable; native-acceptance-pending","source":"system WKWebView native scrollbars; system preferences unchanged"],"containerMode":safari ? "safari-approximation":"app-webview","chromeState":safari ? (compact ? "compact":"expanded"):"absent","chromeVisible":safari,"addressGeometryActive":safari,"chromeLayout":side ? "closed-side-widgets":"open-top-widgets","scrollY":scrollPosition,"nativeContainerFrameDip":["width":frame.width,"height":frame.height],"nativeClipGeometry":["clipFrameDip":rect(clipView.frame),"clipBoundsDip":rect(clipView.bounds),"containerFrameDip":rect(frame),"containerBoundsScenePoints":rect(bounds),"contentOffsetDip":["x":clipView.contentOffset.x,"y":clipView.contentOffset.y]],"renderScale":["x":frame.width/max(1,bounds.width),"y":frame.height/max(1,bounds.height)],"coordinateSpace":"ScenePoints are unscaled screen-coordinate reference units; Dip fields are rendered host points; no physical mm","requestedSceneCssPx":["width":d("logicalWidth",466),"height":d("logicalHeight",678)],"contentRenderingStrategy":safari && side ? "full-native-view-with-obscured-insets":safari ? "reserved-frame-approximation":"full-native-view","underChromeContinuation":safari && side,"fixedContainerColorExtension":["available":nativeFixedContainerColorsAvailable,"source":nativeFixedContainerColorsAvailable ? "system WKWebView native fixedContainerEdges":"legacy DOM opaque-footer fallback","status":nativeFixedContainerColorsAvailable ? "system-extension-enabled-native-acceptance-pending":"unsupported-system-webkit; fixed-overlay support pending"],"fixedBottomColorExtension":["visible": !fixedBottomBackground.isHidden,"frameScenePoints":rect(fixedBottomBackground.frame),"source":"read-only DOM solid full-width fixed footer approximation"],"obscuredContentInsetsDip":["top":web.obscuredContentInsets.top,"left":web.obscuredContentInsets.left,"bottom":web.obscuredContentInsets.bottom,"right":web.obscuredContentInsets.right],"nativeWebViewFrameDip":dipRect(web.frame),"addressHitFrameDip":dipRect(address.frame),"addressVisualFrameDip":dipRect(NSRect(x:address.frame.minX+address.visualRect.minX,y:address.frame.minY+address.visualRect.minY,width:address.visualRect.width,height:address.visualRect.height)),"actualPageViewport":latestObservation["viewportWidth"].map{["width":$0,"height":latestObservation["viewportHeight"] ?? 0]} ?? [:],"shellAppearance":options["shellAppearance"] ?? "light","themeColorHint":latestObservation["themeColor"] ?? NSNull(),"themeColorPolicy":"observed hint only; page backdrop sampled independently","chromeScrollInput":["source":nativeScroll.source,"sequence":nativeScroll.sequence],"tintSource":tintSource,"tintRgb":[Int(c.redComponent*255),Int(c.greenComponent*255),Int(c.blueComponent*255)],"materialBackend":safari ? materialBackend:"none (App WebView)","inactiveMaterialBackend":safari ? NSNull() as Any:materialBackend,"materialParameters":options,"opticalStatus":"macOS native material and measured-layout approximation; iOS optical kernel/refraction unknown","sampling":"DOM observation and native snapshot are asynchronous; not an atomic Safari sample"]
    }
    func publishState(){guard !invalidated,let data=try? JSONSerialization.data(withJSONObject:state(),options:[.sortedKeys]),let json=String(data:data,encoding:.utf8) else{return};web.evaluateJavaScript("window.devduoContainerTelemetry="+json+";window.dispatchEvent(new CustomEvent('devduo:container',{detail:window.devduoContainerTelemetry}));")}
}
public typealias NavigationCallback=@convention(c)(UnsafeMutableRawPointer?,UnsafePointer<CChar>)->Void
func optionsFrom(_ pointer:UnsafePointer<CChar>)->[String:Any]{(try? JSONSerialization.jsonObject(with:Data(String(cString:pointer).utf8))) as? [String:Any] ?? [:]}
@_cdecl("devduo_create") public func create(_ url:UnsafePointer<CChar>,_ svg:UnsafePointer<CChar>,_ width:Double,_ height:Double,_ options:UnsafePointer<CChar>,_ observer:UnsafePointer<CChar>)->UnsafeMutableRawPointer? {
    let host=CalibrationContainer(url:String(cString:url),svg:String(cString:svg),width:width,height:height,options:optionsFrom(options),observer:String(cString:observer));return Unmanaged.passRetained(host).toOpaque()
}
@_cdecl("devduo_navigate") public func navigate(_ pointer:UnsafeMutableRawPointer,_ url:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().navigate(String(cString:url))}
@_cdecl("devduo_configure") public func configure(_ pointer:UnsafeMutableRawPointer,_ options:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().configure(optionsFrom(options))}
@_cdecl("devduo_update_scene") public func updateScene(_ pointer:UnsafeMutableRawPointer,_ svg:UnsafePointer<CChar>,_ width:Double,_ height:Double,_ options:UnsafePointer<CChar>){
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    host.canvas=CGSize(width:width,height:height)
    host.originalMask=maskPath(String(cString:svg))
    host.compact=false;host.nativeScroll=NativeScrollState()
    host.configure(optionsFrom(options))
}
@_cdecl("devduo_html") public func html(_ pointer:UnsafeMutableRawPointer,_ html:UnsafePointer<CChar>,_ baseURL:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().showHtml(String(cString:html),baseURL:String(cString:baseURL))}
@_cdecl("devduo_state_json") public func stateJson(_ pointer:UnsafeMutableRawPointer)->UnsafeMutablePointer<CChar>?{let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue();guard let data=try? JSONSerialization.data(withJSONObject:host.state(),options:[.sortedKeys]),let json=String(data:data,encoding:.utf8) else{return nil};return strdup(json)}
@_cdecl("devduo_free") public func freeString(_ pointer:UnsafeMutableRawPointer){free(pointer)}
@_cdecl("devduo_destroy") public func destroy(_ pointer:UnsafeMutableRawPointer){
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeRetainedValue()
    host.invalidate();host.clipView.removeFromSuperview();host.removeFromSuperview()
}
// The clip view is owned by its container; Avalonia only parents it.
@_cdecl("devduo_clip_view") public func clipView(_ pointer:UnsafeMutableRawPointer)->UnsafeMutableRawPointer{
    Unmanaged.passUnretained(Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().clipView).toOpaque()
}
@_cdecl("devduo_set_viewport") public func setViewport(_ pointer:UnsafeMutableRawPointer,_ width:Double,_ height:Double,_ offsetX:Double,_ offsetY:Double,_ rotation:Int32){
    let clip=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().clipView
    clip.contentSize=CGSize(width:width,height:height);clip.contentOffset=CGPoint(x:offsetX,y:offsetY)
    clip.clockwiseDegrees=Int(rotation)
    clip.needsLayout=true;clip.layoutSubtreeIfNeeded()
}

@_cdecl("devduo_set_navigation_callback") public func setNavigationCallback(_ pointer:UnsafeMutableRawPointer,_ callback:NavigationCallback?,_ context:UnsafeMutableRawPointer?){
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    host.navigationCallback=callback;host.navigationContext=context
    host.publishNavigation()
}
@_cdecl("devduo_go_back") public func goBack(_ pointer:UnsafeMutableRawPointer){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().goBack()}
@_cdecl("devduo_go_forward") public func goForward(_ pointer:UnsafeMutableRawPointer){
    let web=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().web
    if web.canGoForward{web.goForward()}
}
@_cdecl("devduo_reload") public func reload(_ pointer:UnsafeMutableRawPointer){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().web.reload()}
@_cdecl("devduo_stop") public func stop(_ pointer:UnsafeMutableRawPointer){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().web.stopLoading()}
@_cdecl("devduo_evaluate") public func evaluate(_ pointer:UnsafeMutableRawPointer,_ script:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().web.evaluateJavaScript(String(cString:script))}


@_cdecl("devduo_set_initial_proxy") public func setInitialProxy(_ mode:UnsafePointer<CChar>,_ endpoint:UnsafePointer<CChar>?)->UnsafeMutablePointer<CChar>? {
    guard let error=setInitialDeveloperProxy(mode:String(cString:mode),endpoint:endpoint.map{String(cString:$0)}) else{return nil}
    return strdup(error)
}
@_cdecl("devduo_set_proxy") public func setProxy(_ pointer:UnsafeMutableRawPointer,_ mode:UnsafePointer<CChar>,_ endpoint:UnsafePointer<CChar>?)->Int32 {
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    return host.setProxy(mode:String(cString:mode),endpoint:endpoint.map{String(cString:$0)}) ? 1:0
}
@_cdecl("devduo_set_inspector_visible") public func setInspectorVisible(_ pointer:UnsafeMutableRawPointer,_ visible:Int32)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().setInspectorVisible(visible != 0) ? 1:0
}
@_cdecl("devduo_inspector_visible") public func inspectorVisible(_ pointer:UnsafeMutableRawPointer)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().inspectorFlag("isVisible") ? 1:0
}
@_cdecl("devduo_set_remote_inspection") public func setRemoteInspection(_ pointer:UnsafeMutableRawPointer,_ enabled:Int32)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().setRemoteInspection(enabled != 0) ? 1:0
}
@_cdecl("devduo_remote_inspection_enabled") public func remoteInspectionEnabled(_ pointer:UnsafeMutableRawPointer)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().remoteInspectionEnabled ? 1:0
}
@_cdecl("devduo_developer_error") public func developerError(_ pointer:UnsafeMutableRawPointer)->UnsafeMutablePointer<CChar>? {
    strdup(Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().developerError)
}
@_cdecl("devduo_set_remote_debugging_callback") public func setRemoteDebuggingCallback(_ pointer:UnsafeMutableRawPointer,_ callback:NavigationCallback?,_ context:UnsafeMutableRawPointer?) {
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    guard !host.invalidated else{return}
    host.remoteDebuggingCallback=callback;host.remoteDebuggingContext=context
}
@_cdecl("devduo_set_remote_debugging_script") public func setRemoteDebuggingScript(_ pointer:UnsafeMutableRawPointer,_ source:UnsafePointer<CChar>?)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().setRemoteDebuggingScript(source.map{String(cString:$0)}) ? 1:0
}
@_cdecl("devduo_evaluate_remote_debugging_script") public func evaluateRemoteDebuggingScript(_ pointer:UnsafeMutableRawPointer,_ source:UnsafePointer<CChar>)->Int32 {
    Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().evaluateRemoteDebuggingScript(String(cString:source)) ? 1:0
}
@_cdecl("devduo_refresh_remote_debugging_script") public func refreshRemoteDebuggingScript(_ pointer:UnsafeMutableRawPointer)->Int32 {
    let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue()
    guard !host.invalidated else{host.developerError="The native browser has been disposed.";return 0}
    guard let script=host.remoteDebuggingScript else{return 1}
    return host.evaluateRemoteDebuggingScript(script.source) ? 1:0
}
