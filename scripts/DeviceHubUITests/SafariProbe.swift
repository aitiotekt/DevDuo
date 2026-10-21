import XCTest
import Foundation

final class SafariProbe: XCTestCase {
    override func setUpWithError() throws { continueAfterFailure = false }
    func testCurrentPose() throws {
        let pose=ProcessInfo.processInfo.environment["DEVDUO_POSE"] ?? "unlabeled-current-pose"
        print("DEVDUO_REQUESTED_POSE " + pose)
        try probe(pose:pose)
    }

    private func rect(_ r: CGRect) -> [String: Double] {
        ["x":r.origin.x,"y":r.origin.y,"width":r.width,"height":r.height]
    }
    private final class CaptureResponse: @unchecked Sendable {
        var data: Data?
        var response: HTTPURLResponse?
        var error: Error?
    }
    private func captureSelectedDisplay(pose: String, state: String) throws {
        let environment=ProcessInfo.processInfo.environment
        guard let raw=environment["DEVDUO_CAPTURE_URL"] ?? environment["TEST_RUNNER_DEVDUO_CAPTURE_URL"],
              let url=URL(string:raw),url.host == "127.0.0.1",url.scheme == "http" else {
            throw NSError(domain:"DevDuoCapture",code:1,userInfo:[NSLocalizedDescriptionKey:"Missing valid loopback capture broker URL"])
        }
        guard let captureSessionId=environment["DEVDUO_SESSION_ID"] ?? environment["TEST_RUNNER_DEVDUO_SESSION_ID"], !captureSessionId.isEmpty else {
            throw NSError(domain:"DevDuoCapture",code:4,userInfo:[NSLocalizedDescriptionKey:"Missing browser capture session ID"])
        }
        var request=URLRequest(url:url)
        request.httpMethod="POST";request.timeoutInterval=75
        request.setValue("application/json",forHTTPHeaderField:"Content-Type")
        request.httpBody=try JSONSerialization.data(withJSONObject:["pose":pose,"state":state])
        let completed=XCTestExpectation(description:"Explicit active display screenshot saved and validated")
        let result=CaptureResponse()
        let task=URLSession.shared.dataTask(with:request) { data,response,error in
            result.data=data;result.response=response as? HTTPURLResponse;result.error=error;completed.fulfill()
        }
        task.resume()
        guard XCTWaiter.wait(for:[completed],timeout:80) == .completed else {
            task.cancel()
            throw NSError(domain:"DevDuoCapture",code:2,userInfo:[NSLocalizedDescriptionKey:"Capture broker timed out"])
        }
        if let error=result.error { throw error }
        guard let data=result.data,let response=result.response,response.statusCode == 200,
              let body=try JSONSerialization.jsonObject(with:data) as? [String:Any],
              body["ok"] as? Bool == true,body["pose"] as? String == pose,body["state"] as? String == state,
              body["outcome"] as? String == "valid",body["browserCaptureSessionId"] as? String == captureSessionId else {
            let detail=result.data.flatMap { String(data:$0,encoding:.utf8) } ?? "empty response"
            throw NSError(domain:"DevDuoCapture",code:3,userInfo:[NSLocalizedDescriptionKey:"Capture broker rejected stage: "+detail])
        }
        let receipt=XCTAttachment(data:data,uniformTypeIdentifier:"public.json")
        receipt.name=pose+"-"+state+"-selected-display-capture";receipt.lifetime = .keepAlways;add(receipt)
    }
    private func evidence(_ app: XCUIApplication, pose: String, state: String) throws {
        var elements: [[String:Any]] = []
        for type in [XCUIElement.ElementType.button, .textField, .toolbar, .statusBar, .webView] {
            for element in app.descendants(matching: type).allElementsBoundByIndex where element.exists {
                elements.append(["elementType":String(describing:type),"identifier":element.identifier,"label":element.label,"frame":rect(element.frame),"hittable":element.isHittable])
            }
        }
        let data: [String:Any] = ["schemaVersion":1,"recordType":"native-xcuitest","captureSessionId":ProcessInfo.processInfo.environment["DEVDUO_SESSION_ID"] ?? "missing","clientTimestamp":ISO8601DateFormatter().string(from: Date()),"labels":["device":"iPhone Duo","browser":"Safari","pose":pose,"state":state],"applicationFramePoints":rect(app.frame),"deviceOrientationRaw":XCUIDevice.shared.orientation.rawValue,"nativeComponents":elements,"samplingOrder":"native component JSON, then synchronous explicit-display broker capture, then default XCTest diagnostic screenshot; sequential, not atomic", "snapshotSource":"simctl-explicit-display-at-xctest-milestone", "units":"XCTest screen coordinate points; outside web DOM. Not physical mm.","unknown":["hardwareCornerRadiusMm":NSNull(),"liquidGlassMaterialParameters":NSNull()]]
        let json=try JSONSerialization.data(withJSONObject:data,options:[.prettyPrinted,.sortedKeys])
        let attachment=XCTAttachment(data:json,uniformTypeIdentifier:"public.json");attachment.name=pose+"-"+state+"-components";attachment.lifetime = .keepAlways; add(attachment)
        try captureSelectedDisplay(pose:pose,state:state)
        let screenshot=XCTAttachment(screenshot: app.screenshot());screenshot.name=pose+"-"+state+"-xcuitest-default-screen";screenshot.lifetime = .keepAlways;add(screenshot)
        print("DEVDUO_CAPTURE \(pose) \(state)")
    }
    private func probe(pose: String) throws {
        let app=XCUIApplication(bundleIdentifier:"com.apple.mobilesafari"); app.activate()
        XCTAssertTrue(app.wait(for:.runningForeground,timeout:15))
        XCTAssertTrue(app.webViews.staticTexts["Measure the viewport."].waitForExistence(timeout:15))
        let web=app.webViews.firstMatch
        web.swipeDown(velocity:.slow)
        XCTAssertTrue(app.buttons.matching(identifier:"MoreMenuButton").firstMatch.waitForExistence(timeout:8), "Safari expanded page menu unavailable at initial state")
        try evidence(app,pose:pose,state:"initial")
        web.swipeUp(velocity:.slow)
        try evidence(app,pose:pose,state:"scrolled")
    }
}
