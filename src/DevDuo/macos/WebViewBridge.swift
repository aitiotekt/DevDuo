import AppKit
import WebKit
import QuartzCore
import Darwin

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
class FlippedView:NSView {override var isFlipped:Bool{true}}
class PassiveView:FlippedView {override func hitTest(_ point:NSPoint)->NSView?{nil}}
@available(macOS 26.0,*) final class PassiveGlass:NSGlassEffectView {override func hitTest(_ point:NSPoint)->NSView?{nil}}
final class TabStripView:PassiveView {
    var slots:[[String:Any]]=[]
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
                (light ? NSColor.white.withAlphaComponent(0.92):baseColor.blended(withFraction:0.14,of:.white) ?? baseColor).setFill();path.fill()
                color.withAlphaComponent(0.18).setStroke();path.lineWidth=0.65;path.stroke()
                let center=NSPoint(x:box.minX+16,y:box.midY)
                color.setFill();NSBezierPath(ovalIn:NSRect(x:center.x-7,y:center.y-7,width:14,height:14)).fill()
                let cross=NSBezierPath();cross.move(to:NSPoint(x:center.x-2.5,y:center.y-2.5));cross.line(to:NSPoint(x:center.x+2.5,y:center.y+2.5));cross.move(to:NSPoint(x:center.x-2.5,y:center.y+2.5));cross.line(to:NSPoint(x:center.x+2.5,y:center.y-2.5));cross.lineWidth=1.4;NSColor.white.setStroke();cross.stroke()
            }
            let title=active ? (uiStrings["Shell.CalibrationTab"] ?? "Device Hub calibration"):(uiStrings["Shell.PageTab"] ?? "Page {0}").replacingOccurrences(of:"{0}",with:String(index+1))
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
    func style(color:NSColor,alpha:CGFloat,strength:CGFloat,shadow:Float,radius:CGFloat){
        layer?.shadowColor=NSColor.black.cgColor;layer?.shadowOpacity=shadow;layer?.shadowRadius=10;layer?.shadowOffset=CGSize(width:0,height:-3)
        surface.alphaValue=strength;surface.layer?.cornerRadius=radius;surface.layer?.masksToBounds=true
        if #available(macOS 26.0,*),let glass=surface as? NSGlassEffectView {glass.cornerRadius=radius;glass.tintColor=color.withAlphaComponent(alpha)}
        tint.layer?.backgroundColor=color.withAlphaComponent(alpha).cgColor;tint.layer?.cornerRadius=radius;tint.layer?.masksToBounds=true
        tint.layer?.borderColor=NSColor.white.withAlphaComponent(0.3).cgColor;tint.layer?.borderWidth=0.5
        layer?.shadowPath=CGPath(roundedRect:visualRect,cornerWidth:radius,cornerHeight:radius,transform:nil)
    }
    override func layout(){super.layout();surface.frame=visualRect;tint.frame=visualRect;content.frame=visualRect}
    override func mouseDown(with event:NSEvent){onClick?()}
}
final class CalibrationContainer:FlippedView,WKScriptMessageHandler,WKNavigationDelegate {
    var options:[String:Any]
    let web:WKWebView
    let canvas:CGSize
    let originalMask:CGPath?
    let address=GlassPanel(),back=GlassPanel(),bookmarks=GlassPanel(),tabGroup=GlassPanel(),topBackground=GlassPanel()
    let addressLabel=NSTextField(labelWithString:"127.0.0.1")
    let titleLabel=NSTextField(labelWithString:"New tab")
    let clock=NSTextField(labelWithString:"")
    let camera=PassiveView(),wifi=NSImageView(),sideBackground=PassiveView()
    let tabStrip=TabStripView()
    var buttons:[NSButton]=[]
    var compact=false,scrollPosition:Double=0,lastHost="127.0.0.1",tintColor=NSColor(srgbRed:0.94,green:0.95,blue:0.97,alpha:1)
    var tintSource="initial fallback",snapshotWork:DispatchWorkItem?,latestObservation:[String:Any]=[:]
    var invalidated=false
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
        let configuration=WKWebViewConfiguration();web=WKWebView(frame:.zero,configuration:configuration)
        super.init(frame:.zero);wantsLayer=true
        configuration.userContentController.add(self,name:"devduoShell")
        configuration.userContentController.addUserScript(WKUserScript(source:observer,injectionTime:.atDocumentEnd,forMainFrameOnly:true))
        web.navigationDelegate=self;if #available(macOS 13.3,*){web.isInspectable=true};web.setValue(false,forKey:"drawsBackground")
        addSubview(web);sideBackground.wantsLayer=true;addSubview(sideBackground)
        for p in [topBackground,address,back,bookmarks,tabGroup]{addSubview(p)}
        address.onClick = { [weak self] in self?.compact=false;self?.needsLayout=true }
        addressLabel.alignment = .center;addressLabel.font = .systemFont(ofSize:17,weight:.medium);addressLabel.setAccessibilityLabel(ui("Shell.Address","Safari approximation address"))
        address.content.addSubview(addressLabel)
        titleLabel.font = .systemFont(ofSize:12,weight:.medium);titleLabel.alignment = .center;topBackground.content.addSubview(titleLabel)
        clock.font = .systemFont(ofSize:21,weight:.semibold);clock.alignment = .center;addSubview(clock)
        camera.wantsLayer=true;camera.layer?.backgroundColor=NSColor.black.cgColor;addSubview(camera)
        addSubview(tabStrip);tabStrip.setAccessibilityLabel(ui("Shell.TabStrip","Safari tab strip illustration"))
        wifi.image=NSImage(systemSymbolName:"wifi",accessibilityDescription:ui("Shell.Wifi","Wi-Fi illustration"));addSubview(wifi)
        makeButton("chevron.backward",ui("Shell.Back","Back"),#selector(goBack),in:back.content)
        makeButton("book",ui("Shell.Bookmarks","Bookmarks approximation"),#selector(expand),in:bookmarks.content)
        makeButton("plus",ui("Shell.NewTab","New tab"),#selector(newTab),in:tabGroup.content)
        makeButton("square.on.square",ui("Shell.Tabs","Tabs approximation"),#selector(expand),in:tabGroup.content)
        makeButton("line.3.horizontal",ui("Shell.Menu","Page menu approximation"),#selector(expand),in:address.content)
        makeButton("arrow.clockwise",ui("Shell.Reload","Reload"),#selector(reload),in:address.content)
        updateLocalizedLabels()
        if let target=URL(string:url){web.load(URLRequest(url:target))}
    }
    required init?(coder:NSCoder){fatalError("init(coder:) not supported")}
    func makeButton(_ symbol:String,_ label:String,_ action:Selector,in parent:NSView){
        let b=NSButton(image:NSImage(systemSymbolName:symbol,accessibilityDescription:label) ?? NSImage(),target:self,action:action)
        b.isBordered=false;b.imagePosition = .imageOnly;b.focusRingType = .none;b.imageScaling = .scaleProportionallyDown;b.setAccessibilityLabel(label);b.toolTip=label;parent.addSubview(b);buttons.append(b)
    }
    @objc func goBack(){if web.canGoBack{web.goBack()}}
    @objc func reload(){web.reload()}
    @objc func expand(){compact=false;needsLayout=true}
    @objc func newTab(){web.evaluateJavaScript("window.scrollTo({top:0,behavior:'smooth'})")}
    func configure(_ updated:[String:Any]){options=updated;updateLocalizedLabels();needsLayout=true;layoutSubtreeIfNeeded();updateAppearance()}
    func updateLocalizedLabels(){
        addressLabel.setAccessibilityLabel(ui("Shell.Address","Safari approximation address"))
        tabStrip.setAccessibilityLabel(ui("Shell.TabStrip","Safari tab strip illustration"))
        wifi.setAccessibilityLabel(ui("Shell.Wifi","Wi-Fi illustration"))
        tabStrip.uiStrings=options["uiStrings"] as? [String:String] ?? [:]
        let keys=["Shell.Back","Shell.Bookmarks","Shell.NewTab","Shell.Tabs","Shell.Menu","Shell.Reload"]
        let fallbacks=["Back","Bookmarks approximation","New tab","Tabs approximation","Page menu approximation","Reload"]
        for (index,button) in buttons.enumerated(){let text=ui(keys[index],fallbacks[index]);button.setAccessibilityLabel(text);button.toolTip=text}
    }
    func navigate(_ url:String){compact=false;needsLayout=true;if let target=URL(string:url){web.load(URLRequest(url:target))}}
    func showHtml(_ html:String){compact=false;needsLayout=true;web.loadHTMLString(html,baseURL:URL(string:"http://devduo.local/"))}
    func webView(_ webView:WKWebView,didStartProvisionalNavigation navigation:WKNavigation!){compact=false;needsLayout=true}
    func invalidate(){invalidated=true;snapshotWork?.cancel();web.stopLoading();web.configuration.userContentController.removeScriptMessageHandler(forName:"devduoShell");web.navigationDelegate=nil}
    override func layout(){
        super.layout()
        // Scale the native coordinate space, keeping CSS layout at the calibrated scene size.
        let sceneSize=CGSize(width:d("logicalWidth",466),height:d("logicalHeight",678))
        if bounds.size != sceneSize {setBoundsSize(sceneSize)}
        let w=bounds.width,h=bounds.height
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
        let nextWebFrame=nativeUnderChrome ? bounds:frame(viewport.minX,viewport.minY,viewport.width,viewport.height)
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
        sideBackground.isHidden = !safari || !side
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
            tabStrip.slots=(referenceState["tabs"] as? [[String:Any]]) ?? []
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
    func updateAppearance(){
        let c=tintColor.usingColorSpace(.sRGB) ?? tintColor
        let alpha=max(0,min(1,d("tintAlpha",0.32))),strength=max(0,min(1,d("glassStrength",0.92))),shadow=Float(max(0,min(1,d("shadowOpacity",0.16))))
        for p in [address,back,bookmarks,tabGroup,topBackground]{p.style(color:c,alpha:alpha,strength:strength,shadow:shadow,radius:p === topBackground ? 0:min(d("cornerRadius",24),p.visualRect.height/2));p.needsLayout=true}
        if let rgb=latestObservation["rootBackgroundColor"] as? [NSNumber],rgb.count==3 {sideBackground.layer?.backgroundColor=NSColor(srgbRed:rgb[0].doubleValue/255,green:rgb[1].doubleValue/255,blue:rgb[2].doubleValue/255,alpha:1).cgColor}
        let neutral=c.blended(withFraction:0.65,of:NSColor.windowBackgroundColor) ?? c
        layer?.backgroundColor=neutral.cgColor
        let text:NSColor=(c.redComponent*0.2126+c.greenComponent*0.7152+c.blueComponent*0.0722)<0.36 ? .white:.black
        tabStrip.color=text;tabStrip.baseColor=c;tabStrip.needsDisplay=true;for b in buttons{b.contentTintColor=text};addressLabel.textColor=text;titleLabel.textColor=text;clock.textColor=text;wifi.contentTintColor=text
        addressLabel.stringValue=lastHost;titleLabel.stringValue=(latestObservation["title"] as? String) ?? ui("Shell.Title","Safari approximation")
        let formatter=DateFormatter();formatter.locale=Locale(identifier:options["uiLanguage"] as? String ?? "en");formatter.dateFormat="HH:mm";clock.stringValue=formatter.string(from:Date())
    }
    func userContentController(_ userContentController:WKUserContentController,didReceive message:WKScriptMessage){
        guard !invalidated,let body=message.body as? [String:Any],body["type"] as? String == "devduo-shell-observation" else{return}
        latestObservation=body
        if let rgb=body["rootBackgroundColor"] as? [NSNumber],rgb.count==3 {web.underPageBackgroundColor=NSColor(srgbRed:rgb[0].doubleValue/255,green:rgb[1].doubleValue/255,blue:rgb[2].doubleValue/255,alpha:1)}
        lastHost=(body["host"] as? String) ?? lastHost
        let y=(body["scrollY"] as? NSNumber)?.doubleValue ?? 0,delta=(body["deltaY"] as? NSNumber)?.doubleValue ?? 0;scrollPosition=y
        let next:Bool
        if y<=2 || delta < -8 {next=false}else if y>Double(d("collapseThresholdCssPx",24)) && delta>1{next=true}else{next=compact}
        if next != compact {compact=next;needsLayout=true}
        if let values=body["themeColor"] as? [NSNumber],values.count==3 {setTint(values.map{$0.doubleValue},source:"document theme-color");snapshotWork?.cancel()}
        else {
            if tintSource != "WKWebView native edge snapshot",let values=body["domBackgroundColor"] as? [NSNumber],values.count==3 {setTint(values.map{$0.doubleValue},source:"computed content background fallback")}
            scheduleSnapshot()
        }
        updateAppearance();publishState()
    }
    func setTint(_ rgb:[Double],source:String){tintColor=NSColor(srgbRed:rgb[0]/255,green:rgb[1]/255,blue:rgb[2]/255,alpha:1);tintSource=source}
    func scheduleSnapshot(){
        snapshotWork?.cancel()
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
                guard let self,!self.invalidated,self.latestObservation["themeColor"] is NSNull,let image,error==nil,let data=image.tiffRepresentation,let bitmap=NSBitmapImageRep(data:data) else{return}
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
        return ["schemaVersion":2,"widgetFramesScenePoints":widgetFrames,"nativeWebViewFrameScenePoints":rect(web.frame),"addressHitFrameScenePoints":rect(address.frame),"rotationClockwiseDegrees":d("rotationClockwiseDegrees",0),"sidebarSide":safari && side ? (sidebarLeft ? "left":"right"):"absent","widgetFramesDip":widgetDip,"landscapeStatusWidgetsHidden":landscape && clock.isHidden && wifi.isHidden,"tabStripVisible":safari && !side && !compact && showTabStrip,"tabFontSizeScenePoints":d("tabFontSize",14),"tabStripGeometryScenePoints":tabStrip.isHidden ? []:tabStrip.geometry(),"tabStripRendering":"shared track; native whole-tab container including close control; original hit frames retained","referenceScene":referenceScene["id"] ?? "unmeasured","bodyRotationDegrees":referenceScene["bodyRotationDegrees"] ?? 0,"horizontalInsetFill":"document root color approximation; no duplicated page pixels","layoutCalibration":referenceScene["mappingStatus"] ?? "unmeasured","engine":"macOS system WKWebView","containerMode":safari ? "safari-approximation":"app-webview","chromeState":safari ? (compact ? "compact":"expanded"):"absent","chromeVisible":safari,"addressGeometryActive":safari,"chromeLayout":side ? "closed-side-widgets":"open-top-widgets","scrollY":scrollPosition,"nativeContainerFrameDip":["width":frame.width,"height":frame.height],"renderScale":["x":frame.width/max(1,bounds.width),"y":frame.height/max(1,bounds.height)],"coordinateSpace":"ScenePoints are unscaled screen-coordinate reference units; Dip fields are rendered host points; no physical mm","requestedSceneCssPx":["width":d("logicalWidth",466),"height":d("logicalHeight",678)],"contentRenderingStrategy":safari && side ? "full-native-view-with-obscured-insets":safari ? "reserved-frame-approximation":"full-native-view","underChromeContinuation":safari && side,"obscuredContentInsetsDip":["top":web.obscuredContentInsets.top,"left":web.obscuredContentInsets.left,"bottom":web.obscuredContentInsets.bottom,"right":web.obscuredContentInsets.right],"nativeWebViewFrameDip":dipRect(web.frame),"addressHitFrameDip":dipRect(address.frame),"addressVisualFrameDip":dipRect(NSRect(x:address.frame.minX+address.visualRect.minX,y:address.frame.minY+address.visualRect.minY,width:address.visualRect.width,height:address.visualRect.height)),"actualPageViewport":latestObservation["viewportWidth"].map{["width":$0,"height":latestObservation["viewportHeight"] ?? 0]} ?? [:],"tintSource":tintSource,"tintRgb":[Int(c.redComponent*255),Int(c.greenComponent*255),Int(c.blueComponent*255)],"materialBackend":safari ? materialBackend:"none (App WebView)","inactiveMaterialBackend":safari ? NSNull() as Any:materialBackend,"materialParameters":options,"opticalStatus":"macOS native material and measured-layout approximation; iOS optical kernel/refraction unknown","sampling":"DOM observation and native snapshot are asynchronous; not an atomic Safari sample"]
    }
    func publishState(){guard !invalidated,let data=try? JSONSerialization.data(withJSONObject:state(),options:[.sortedKeys]),let json=String(data:data,encoding:.utf8) else{return};web.evaluateJavaScript("window.devduoContainerTelemetry="+json+";window.dispatchEvent(new CustomEvent('devduo:container',{detail:window.devduoContainerTelemetry}));")}
}
func optionsFrom(_ pointer:UnsafePointer<CChar>)->[String:Any]{(try? JSONSerialization.jsonObject(with:Data(String(cString:pointer).utf8))) as? [String:Any] ?? [:]}
@_cdecl("devduo_create") public func create(_ url:UnsafePointer<CChar>,_ svg:UnsafePointer<CChar>,_ width:Double,_ height:Double,_ options:UnsafePointer<CChar>,_ observer:UnsafePointer<CChar>)->UnsafeMutableRawPointer? {
    let host=CalibrationContainer(url:String(cString:url),svg:String(cString:svg),width:width,height:height,options:optionsFrom(options),observer:String(cString:observer));return Unmanaged.passRetained(host).toOpaque()
}
@_cdecl("devduo_navigate") public func navigate(_ pointer:UnsafeMutableRawPointer,_ url:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().navigate(String(cString:url))}
@_cdecl("devduo_configure") public func configure(_ pointer:UnsafeMutableRawPointer,_ options:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().configure(optionsFrom(options))}
@_cdecl("devduo_html") public func html(_ pointer:UnsafeMutableRawPointer,_ html:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().showHtml(String(cString:html))}
@_cdecl("devduo_state_json") public func stateJson(_ pointer:UnsafeMutableRawPointer)->UnsafeMutablePointer<CChar>?{let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue();guard let data=try? JSONSerialization.data(withJSONObject:host.state(),options:[.sortedKeys]),let json=String(data:data,encoding:.utf8) else{return nil};return strdup(json)}
@_cdecl("devduo_free") public func freeString(_ pointer:UnsafeMutableRawPointer){free(pointer)}
@_cdecl("devduo_destroy") public func destroy(_ pointer:UnsafeMutableRawPointer){let host=Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeRetainedValue();host.invalidate();host.removeFromSuperview()}

@_cdecl("devduo_evaluate") public func evaluate(_ pointer:UnsafeMutableRawPointer,_ script:UnsafePointer<CChar>){Unmanaged<CalibrationContainer>.fromOpaque(pointer).takeUnretainedValue().web.evaluateJavaScript(String(cString:script))}
