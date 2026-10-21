import AppKit
import WebKit

@MainActor final class Probe:NSObject,NSApplicationDelegate,WKNavigationDelegate {
 let output=URL(fileURLWithPath:CommandLine.arguments[1],isDirectory:true)
 var window:NSWindow!
 var web:WKWebView!
 var results:[[String:Any]]=[]
 let configurations:[(String,NSEdgeInsets,NSEdgeInsets,NSEdgeInsets)]=[
  ("baseline",NSEdgeInsets(),NSEdgeInsets(),NSEdgeInsets()),
  ("bottom98-right84",NSEdgeInsets(top:0,left:0,bottom:98,right:84),NSEdgeInsets(top:0,left:0,bottom:80,right:84),NSEdgeInsets(top:0,left:0,bottom:98,right:84)),
  ("bottom80-right84",NSEdgeInsets(top:0,left:0,bottom:80,right:84),NSEdgeInsets(top:0,left:0,bottom:80,right:84),NSEdgeInsets(top:0,left:0,bottom:98,right:84)),
  ("top126",NSEdgeInsets(top:126,left:0,bottom:0,right:0),NSEdgeInsets(top:51,left:0,bottom:0,right:0),NSEdgeInsets(top:126,left:0,bottom:0,right:0)),
  ("bottom98-only",NSEdgeInsets(top:0,left:0,bottom:98,right:0),NSEdgeInsets(top:0,left:0,bottom:80,right:0),NSEdgeInsets(top:0,left:0,bottom:98,right:0))
 ]
 func applicationShouldTerminateAfterLastWindowClosed(_ sender:NSApplication)->Bool{true}
 func applicationDidFinishLaunching(_ notification:Notification){
  try! FileManager.default.createDirectory(at:output,withIntermediateDirectories:true)
  window=NSWindow(contentRect:NSRect(x:80,y:80,width:466,height:678),styleMask:[.titled,.closable],backing:.buffered,defer:false)
  window.title="DevDuo viewport inset capability probe"
  web=WKWebView(frame:NSRect(x:0,y:0,width:466,height:678));window.contentView=web;web.navigationDelegate=self;window.makeKeyAndOrderFront(nil)
  let html="""
  <!doctype html><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>
  *{box-sizing:border-box}html,body{margin:0;font:18px system-ui} .row{height:40px;border-bottom:1px solid #8296aa;background:#deecfa;padding:5px}.row:nth-child(even){background:#fae9d7}#fixed{position:fixed;bottom:0;right:0;background:#0a3177;color:white;padding:6px;font-size:12px}.metric{position:absolute;left:-200px;width:1px}#d{height:100dvh}#s{height:100svh}#l{height:100lvh}</style>
  <div id="rows"></div><div id="fixed">FIXED BOTTOM</div><i id="d" class="metric"></i><i id="s" class="metric"></i><i id="l" class="metric"></i><script>rows.innerHTML=Array.from({length:40},(_,i)=>`<div class="row">Flow row ${i}: y=${i*40}..${(i+1)*40}</div>`).join('')</script>
  """
  web.loadHTMLString(html,baseURL:nil)
 }
 func webView(_ webView:WKWebView,didFinish navigation:WKNavigation!){runCase(0)}
 func runCase(_ index:Int){
  if index==configurations.count {finish();return}
  let (name,current,minimum,maximum)=configurations[index]
  web.setMinimumViewportInset(minimum,maximumViewportInset:maximum)
  web.obscuredContentInsets=current
  DispatchQueue.main.asyncAfter(deadline:.now()+0.6){self.measure(index,name,current)}
 }
 func measure(_ index:Int,_ name:String,_ inset:NSEdgeInsets){
  let script="""
  (()=>{const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}};return {innerWidth,innerHeight,clientWidth:document.documentElement.clientWidth,clientHeight:document.documentElement.clientHeight,scrollY,visualViewport:{width:visualViewport.width,height:visualViewport.height,offsetTop:visualViewport.offsetTop,offsetLeft:visualViewport.offsetLeft},fixedRect:rect(document.getElementById('fixed')),row14:rect(document.querySelectorAll('.row')[14]),viewportUnits:{dvh:document.getElementById('d').getBoundingClientRect().height,svh:document.getElementById('s').getBoundingClientRect().height,lvh:document.getElementById('l').getBoundingClientRect().height}}})()
  """
  web.evaluateJavaScript(script){value,error in
   var record:[String:Any]=["case":name,"webViewFrameDip":["width":self.web.frame.width,"height":self.web.frame.height],"requestedObscuredInsetsDip":["top":inset.top,"left":inset.left,"bottom":inset.bottom,"right":inset.right],"actualObscuredInsetsDip":["top":self.web.obscuredContentInsets.top,"left":self.web.obscuredContentInsets.left,"bottom":self.web.obscuredContentInsets.bottom,"right":self.web.obscuredContentInsets.right],"measuredAt":ISO8601DateFormatter().string(from:Date())]
   record["page"]=value ?? NSNull();if let error {record["error"]=error.localizedDescription}
   let config=WKSnapshotConfiguration();config.rect=self.web.bounds
   self.web.takeSnapshot(with:config){image,error in
    if let image,let tiff=image.tiffRepresentation,let rep=NSBitmapImageRep(data:tiff),let data=rep.representation(using:.png,properties:[:]){let filename=name+".png";try! data.write(to:self.output.appendingPathComponent(filename));record["image"]=filename;record["imagePixels"]=["width":rep.pixelsWide,"height":rep.pixelsHigh]}
    if let error{record["snapshotError"]=error.localizedDescription}
    self.results.append(record);self.runCase(index+1)
   }
  }
 }
 func finish(){
  let result:[String:Any]=["schemaVersion":1,"platform":"macOS 27, system WKWebView, public APIs only","meaning":"Capability experiment, not Safari emulation or physical-device measurement","cases":results]
  let data=try! JSONSerialization.data(withJSONObject:result,options:[.prettyPrinted,.sortedKeys]);try! data.write(to:output.appendingPathComponent("results.json"));print(String(data:data,encoding:.utf8)!);if ProcessInfo.processInfo.environment["HOLD_PROBE"]=="1" { let c=configurations[1];web.setMinimumViewportInset(c.2,maximumViewportInset:c.3);web.obscuredContentInsets=c.1;print("HOLDING_BOTTOM_RIGHT_PROBE");fflush(stdout) } else {NSApp.terminate(nil)}
 }
}
if CommandLine.arguments.count != 2 { fputs("usage: xcrun swift scripts/archive/2026-10-21/viewport_insets_probe.swift OUTPUT_DIRECTORY\n",stderr);exit(2) }
MainActor.assumeIsolated {
 let app=NSApplication.shared
 let delegate=Probe();app.delegate=delegate;app.setActivationPolicy(.regular);app.run()
}
