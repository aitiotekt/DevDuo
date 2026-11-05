# Windows WebKit backend

The measured Windows page exclusively uses a pinned WebKit Windows Port with production host extensions. Avalonia owns the workbench, WebKit owns the document/input, and DirectComposition presents the live page and Safari approximation. Runtime acceptance is maintained in the [Windows handoff](009-WINDOWS-HANDOFF.md); this page owns implementation and reproduction.

## Host API source build

`native/webkit/windows/` owns the source pin, production/experimental patches, portable build environment, cache management and runtime packaging. `tests/DevDuo.WebKitProbe/` owns standalone diagnostics and fixtures. See [runtime ownership](../../native/webkit/windows/README.md) and [runtime release policy](013-RELEASE.md#windows-webkit-runtime-releases).

The current upstream pin is `324423@main`, revision `6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d`. The diagnostic archive lock and release metadata must agree on upstream revision. Production uses the patched source build, not the official testing DLL.

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
mise exec -- just dev
```

mise manages portable CMake/Ninja/Ruby/Perl/gperf/pkgconf/vcpkg and the normal Python environment. VS C++/Clang and Windows SDK remain system dependencies. Custom vcpkg triplets preserve interpreter/generator ownership. Source/dependency caches live in `temp/cache/windows-webkit/`, output in `temp/build/windows-webkit-host/`, and toolchain-specific retained trees in `temp/build/windows-webkit-trees/`. Do not clean these between incremental builds. The build selects `ENABLE_BACK_FORWARD_LIST_SWIFT=OFF`; do not mix its files with the official Swift-enabled archive.

Patch synchronization preserves timestamps for byte-identical files, including patch-created headers, so replacing one patch does not invalidate unrelated objects. Changed bytes still invalidate normally; unrecorded source edits fail explicitly and remain intact.

| Patch | Responsibility |
| --- | --- |
| `0001-windows-host-viewport-api.patch` | Production insets and independent logical-view/backing-scale API |
| `0002-windows-content-painting.patch` | Painting/damage through insets, canvas boundaries, color/appearance/indicator integration and native transport support |
| `0003-windows-developer-api.patch` | User-script removal, native Inspector listener and proxy API |
| `0004-windows-desktop-integration.patch` | Physical desktop mapping, TSF/IMM, AX transport and current software-frame/glyph handling |
| `0005-windows-edge-color-v2.patch` | Current-container coverage and upstream predominant-color sampling |
| `0006-windows-frame-pipeline.patch` | Correlated input/frame timing and asynchronous AX snapshots |
| `0007-windows-atomic-continuation.patch` | Frame-bound continuation, source geometry and clock correlation |
| `0008-windows-frame-surfaces.patch` | Leased GPU textures/CPU buffers, presentation backpressure and frame acknowledgement |
| `0009-windows-frame-clock.patch` | Machine-wide QPC timestamps, including input across process and wheel coalescing |
| `0010-windows-scrolling-layers.patch` | Experimental WC scrolling-layer bridge and reuse of upstream threaded hit testing, scroll chains and fixed/sticky constraints |
| `0011-windows-direct-frame-relay.patch` | Experimental GPU-to-UI frame delivery and acknowledgement, bypassing the Web process main-thread return hop |
| `0012-windows-async-presentation.patch` | Experimental retained-layer scroll frames, revision ordering, independent clock and native scroll diagnostics |
| `0013-windows-native-touch.patch` | Experimental trusted single-contact input, native gesture authorization and upstream precise/kinetic scrolling; scoped native acceptance |
| `0014-windows-wc-raster-coverage.patch` | Shared pixel bounds for Windows WC tile painting, damage and texture mapping |
| `0015-windows-async-viewport-publication.patch` | Orders independent scrolling after the scene carrying the matching viewport geometry |
| `0016-windows-direct-pan-work.patch` | Uses the Windows scrolling tick directly and stops keyboard scrolling once per native pan |
| `0017-windows-precise-touch-coordinates.patch` | Retains fractional CSS coordinates through native contact and active scroll handling |

The working-tree host requires viewport v2, painting v2, content color v1, appearance v1, developer v1, fixed-container color v2, scroll-indicator v1, desktop v1, asynchronous accessibility v2, frame timing v2, frame presentation v1 and frame surface v1. The application-owned compositor separately requires composition v6, page frame v2 and presentation v1. Versions and required exports are checked at startup. Manifest hashes are provenance outputs, not runtime pass gates. Missing capabilities require rebuilding the matching runtime; application delivery requires the metadata-selected engine release.

## Avalonia backend

Successful source builds atomically select their output in ignored `temp/build/windows-webkit-runtime.json`. A checkout uses that selection on its next process start; without a selection it discovers `temp/build/windows-webkit-host/bin`. An invalid/stale selection fails explicitly rather than silently loading an older runtime. Installed apps use `webkit/bin` plus `webkit/runtime-manifest.json` beside the executable. An absolute `DEVDUO_WEBKIT_RUNTIME` override is authoritative: missing/incompatible capabilities fail explicitly rather than selecting another engine. Test-only IPC stays disabled.

`WindowsWebKitApi.cs` binds the C ABI; `WindowsWebKitView*.cs` owns navigation, storage, input, dialogs and accessibility; `WindowsWebViewPanel.cs` configures scene geometry. The persistent website store is `DevDuo/WebKit` under local application data. An absolute `DEVDUO_WEBKIT_PROFILE` isolates Windows test profiles. Native views share one store session; persistent cookies, localStorage, IndexedDB and caches survive restart. sessionStorage retains normal document semantics. Resetting host preferences does not clear website data or migrate/delete an older engine's profile.

Rotation, Fit, outline, language, appearance and shell settings update the existing native view. Fold/container-mode changes intentionally replace it. Navigation/document/geometry generations and disposal guards reject stale async results. Owned HWNDs, callbacks, COM resources and handlers are released predictably; source destruction invalidates pending work before managed detachment.

## Drawing and document boundaries

`WKViewSetObscuredContentInsets` sends ordinary production IPC. `WKViewSetUnobscuredSizeRange` supplies real minimum/maximum/default sizes for viewport units. `WKViewSetHostPresentationGeometry` separates logical scene size from physical backing scale; Fit preserves the layout viewport with page zoom fixed at 1. It does not modify CSS or JavaScript geometry accessors.

The drawing frame includes genuine top/left/right/bottom insets. Engine paint clips, origins, scroll-copy extent and damage cover that full domain while fixed/sticky positions and scroll range use the unobscured viewport. Inset/geometry changes request event-driven complete damage even when logical size is unchanged. Fractional device-pixel scroll deltas use normal damage painting instead of truncating a backing-store copy; integral deltas retain the optimization.

Accelerated WC frames render into an ANGLE pbuffer backed by an owned keyed D3D11 texture. Frame surface API v1 leases that resource to the host, which copies it on the GPU into retained raw/final page buffers. Non-composited frames upload the engine's native CPU bitmap to the same buffers. Neither application presentation path uses GDI or page readback; explicit diagnostic captures still read back the presented framebuffer. This is GPU-to-GPU transport, not zero-copy rendering: the producer currently completes GL work before releasing the mutex, and Direct2D copies the final page into a composition surface. Asymmetric page pixels check the shared pbuffer's Y-axis convention.

One frame remains in flight until the host acknowledges its transfer. Obsolete geometry drains the producer key without presenting the frame; resource replay distinguishes an already consumed key from a new frame. Raw content remains separate from continuation so a fill expiry does not accumulate previous fills. Failures report an explicit capability/presentation error. Multi-adapter sharing and device-loss recovery remain unverified.

The default Windows runtime enables asynchronous scrolling and native touch. Patches `0010`–`0013` connect the upstream scroll tree and trusted touch events to retained GPU layers with one leased frame and coalesced pending positions. Frame mode is selected before WebPage creation. A scrolling-thread clock wakes on handled input and stops when recent activity and native animations end. Scoped native acceptance covers blocked page JavaScript, touch cancellation, touch-action, overflow locks and nested scrolling. Complex-page fluidity remains a scoped acceptance concern, not a reason to compile out the validated path. `build-windows-webkit-runtime -RuntimeRoot <absolute-build-directory> -KeepRuntimeSelection` builds an isolated runtime without changing checkout selection. Submitted geometry generations reject stale frames. Cached Windows glyph display lists use backend-neutral operations across local Skia and remote GPU-recording contexts.

The experimental host scroll-frame API v1 reports source paint identity, document/geometry generations, scroll revision, real root position and input/commit/GPU/host timestamps. Main rendering stages remain absent on independently composed scroll frames. Its read-only native scroll-tree request uses production IPC and returns no page image. The existing frame timing API v2 ABI is unchanged. Neither API proves DWM scanout.

Experimental host touch API v1 exposes mode selection and contact-generation diagnostics. The managed host negotiates this optional capability; its native document-start script only reports mode/capability and does not replay events or write scroll positions. The engine dispatches contacts through its event regions and ordinary WebCore touch handling before granting native pan. Non-passive cancellation and `touch-action` remain page policy, and a pan does not synthesize a DOM wheel event. Canceling pointerdown suppresses compatibility mouse down/up without canceling native pan or the trusted touch click. Focus/capture loss, navigation and mode changes cancel the contact; creation parameters preserve mode across process replacement. The original down timestamp anchors pan velocity, and stationary window-generated mouse messages do not create new contact moves. This supports one contact, not pinch or multiple contacts. Scoped native results and remaining acceptance boundaries are recorded in the [Windows handoff](009-WINDOWS-HANDOFF.md).

Listener and touch-action changes schedule the normal event-region rendering step, including changes without pixel damage. WC publishes changed regions to its scrolling-layer mirror; enabling the region setting only in the WebProcess is insufficient because later preference updates can overwrite it. A missing committed region requires an actual main-thread touch reply. Documents without an asynchronous root retain native synchronous default/overflow scrolling, without dispatching the internal wheel payload to page listeners. These paths preserve page cancellation, overflow locks and nested scroll chaining.

Windows touch emulation forwards both permitted axes for `touch-action: auto` and `manipulation`. Gesture-based directional locking is missing in both the native pan and DOM fallback. The pinned engine also leaves the experimental CSS `scroll-axis-lock` preference disabled. That property controls whether the user agent may lock a particular scroller, so enabling its parser alone would leave gesture behavior unchanged.

WC mirror hit bounds use zero-origin local extents, matching `CoordinatedPlatformLayer::bounds()`; traversal applies `boundsOrigin` separately. Including the origin again misroutes input after nested scrolling. A zero-delta pan start may not latch a node, so cancellation resolves the preceding pan's original hit chain when needed and stops its native animation, including nested targets. A new contact, mode/focus change or navigation retires the old gesture. Read-only scrolling diagnostics expose the mirror's node IDs and local geometry separately from the main-thread graphics tree.

The Windows host keeps button ownership from the press through release. A page drag released over a Safari control sends the release to WebKit. A chrome press activates its original control when release still hits that control. Native touch cancels an orphaned contact when the next mouse move reports no held left button.

The source raster extent comes from the logical geometry and the engine's uniform float backing scale. Independently rounded HWND dimensions can differ at fractional scaling. The host validates source dimensions against that geometry, drains stale producer leases, and copies valid pixels at their original size into a separately sized output buffer. Excess is clipped; physical rounding margins use the frame background. The page is never stretched to compensate, and DOM viewport, fixed geometry and scroll range are unchanged.

The engine preserves valid two-dimensional document flow and fills only beyond its extent with the opaque html/body background fallback, otherwise white. Flow bounds, submitted insets, root background, fixed-edge state and page pixels travel together. The UI process paints continuation into the same native page buffer; host widget/material updates never overlay an independently sampled edge fill. Toolbar tint remains independent of the document-boundary rule. No screenshot, repeated row, DOM padding or fake viewport supplies continuation.

## Mobile request identity

`BrowserIdentity.MobileUserAgent` supplies the observed Safari compatibility identity to both platforms before the first native request:

```text
Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1
```

Both containers use the same observation; no separate immersive identity was measured. This changes request headers and native `navigator.userAgent`, not platform/DPR/viewport properties or the actual desktop engine. The frozen `OS 18_7` token is not evidence of the observed runtime version, and the string does not authenticate physical Duo hardware. Keep the original observed value and its private collector provenance rather than inferring a replacement from the simulator OS.

## Live surface composition

`HostCompositor.cpp` implements composition v6 with D3D11, Direct2D/DirectWrite and DirectComposition. Page frame v2 publishes the leased WebKit frame and its continuation through one retained GPU framebuffer and one composition submission. WebKit offscreen mode is enabled before navigation; the HWND remains the native input/popup owner and is not the source of page pixels. The measured page has no WebView2 fallback; only the separate developer-tools frontend uses system WebView2.

Rotated scenes retain a full-sized, cloaked layered source host outside the smaller physical clip. Presentation v1 rotates/translates the complete tree; `WindowsScreenPresenter` inversely maps input and focus. Body, content-in-body and scene-to-canvas coordinates follow the [scene contract](005-SAFARI-SCENES.md#body-and-content-coordinate-systems).

Composition order is page/valid continuation, document-boundary fills, local blur, rounded tint/rim/shadow, then clear text/icons. Blur reads the undecorated page, clamps sampling edges and precedes rounded clipping. Premultiplied alpha and separate glyph layers preserve readable foregrounds. Hit frames, optical frames and text clips remain distinct; selected tabs use complete visual containers.

The passive touch pointer uses an independent DirectComposition device attached above the scene. Pointer movement commits only that device. Movement requests share a 16 ms fallback timer that reads the latest OS position. An accepted scroll frame can submit a pending pointer update once its previous submission is at least 16 ms old. Entry, exit, focus loss and mode changes update immediately, and disposal cancels pending work. Pointer and page submissions use the same QPC clock. Native touch keeps fractional CSS coordinates through the held drag. Mouse hit testing and tap compatibility use their existing integer coordinates. See the [cross-device visual contract](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositionvisual-addvisual).

Physical HWND synchronization and resource generations handle resize, ancestor clips and device recreation without navigation. Hardware setup may retry the same D3D11 route with WARP; unsupported composition reports a capability error rather than certifying an old GDI material fallback. Device-loss/WARP recovery still lacks fault-injection acceptance. Scene/resource counters are not WebKit frame IDs, atomic DOM/frame timestamps or final DWM FPS.

## Fractional boundary rasterization

Content clips use half-up nearest-pixel edges to agree with Skia hard clipping; damage rectangles conservatively enclose changed pixels. Confusing these rounding rules leaves a one-pixel gap at fractional Fit. Backing dimensions enclose the drawing domain, and origins align to physical pixels.

WC tile painting and TextureMapper use the same rounded backing extent and device scale as CSS background snapping. Adjacent tiles round their shared edges once and clip damage to those bounds. Root fixed layer backings snap in viewport coordinates so cancellation of document scroll leaves their texture origin on the same physical grid as the continuation. The DOM geometry remains in CSS coordinates.

An inset change can update the scrolling tree's fixed positions while the matching main scene still waits for paint. The Web process suspends independent publication before applying the inset. The first scene uses the new layout snapshot for layer positions and continuation edges, clearing pending positions from the old viewport. Main-scene capture holds the scrolling tree lock while taking layer positions so it cannot split a staged batch. The GPU queue discards pending deltas already covered by a full scene snapshot. Independent publication resumes when the matching scene is queued. The previous complete frame stays available. Scroll input and the layout viewport remain engine-owned.

The final GPU page buffer composites premultiplied page pixels over the submitted under-page background, then applies frame-bound continuation at rounded physical edges. The engine's legacy GDI diagnostic path still flattens alpha because `SetDIBitsToDevice` does not honor it; application presentation does not use that path. No content stretching or edge-pixel copying repairs the boundary.

Marker-free static boundary fixtures exercise all edges. Their source-paint evidence is distinct from final DWM output; [incremental acceptance](009-WINDOWS-HANDOFF.md#windows-milestone-and-incremental-regression) selects Fit/DPI cases only when affected.

## Shell material and dynamic state

`config/safari-shell.json` owns tunable Windows optical parameters and measured scene references. Material roles separate top background, floating surfaces, compact addresses and tab tracks. Saturation/brightness/contrast use a ColorMatrix; foreground glyphs follow shell appearance independently of backdrop colors. Dark/light backdrop recipes interpolate smoothly. These are artistic desktop approximations, not physical refraction or Apple's Liquid Glass kernel.

The current native rendered edge determines backdrop color. `theme-color` remains diagnostic. Content color API v1 uses ordinary snapshot IPC and returns only request/status/RGB. The Closed address region samples the bottom unobscured edge and the Open shell samples the top. Closed side controls use the last page-frame fill when it covers the full height of the side inset and all side glass surfaces. The compositor updates their decoration on the same commit as those page pixels. If the frame has no such uniform fill, the controls use the sampled strip within their own left or right inset, with the current document's opaque root background as the initial fallback. Every strip is at most 18 logical rows, painted at its own size and resampled to width 64 before averaging in unpremultiplied sRGB and excluding low-alpha pixels. Sampling runs in one throttled 180 ms cycle with at most one request per region, bounded follow-up and stale-generation rejection. An unavailable result retains the current document's last sample. Navigation resets ownership. Snapshot images never enter page presentation.

Direct2D filters the completed page framebuffer into one reusable backdrop for the Closed side buttons. Each existing button visual clips the shared result to its own shape and keeps its foreground and decorations separate. The filter samples adjacent page pixels even when the inset has a uniform fill. The page, backdrop and updated decoration are submitted together. Open glass still uses the existing DirectComposition effects.


Each sampled region has an independent temporal filter. Per-channel changes up to 3 are ignored, and two samples within 6 establish a target with a 400 ms maximum confirmation window. A 120 ms interpolation changes tint and derived optics while the live backdrop and foreground glyphs remain separate. Settled pages stop animation. Composition v6 interpolates the expanded/compact material recipes and keeps surviving glass nodes attached by role across changes to the address and controls. The base page, glass, decorations and foreground use separate visuals. Same-size surfaces publish damage through `EndDraw` and retain their content bindings. `just test-windows-render-stability` samples consecutive composed-window frames and correlates them with native edge and material observations. Update counters describe work and capture timestamps describe WGC observations.

Appearance API v1 notifies WebKit and page media queries without navigation. Native input intent correlated with document movement changes chrome state: cumulative down >24 CSS px compacts, up >8 expands, and intent expires after 350 ms. Zero/opposite motion, viewport clamping and script-only scrolling do not change state. Concurrent script/native movement remains a qualification.

Windows chrome uses a presentation-only 220 ms cubic transition (`cubic-bezier(1/3,0,2/3,1)`, a desktop approximation, not measured Safari timing). `windowsMaterial.chromeTransitionMilliseconds` controls duration; zero and the Windows client-area animation preference disable it. Reversal starts from displayed frames; navigation, mode/scene and backing-scale changes cancel old motion. Measured expanded/compact endpoints are unchanged. WebKit receives the target inset once per state change; intermediate widget frames update retained composition clips/surfaces without repeated viewport changes, DOM evaluations or telemetry publication. Current hit/accessibility frames follow the interpolated native items. `chromeAnimation` reports target progress separately from `chromeState`.

Address/menu/bookmarks/overview expand the shell; only reload reloads; plus is a return-to-top illustration. DirectWrite measures and draws the same font. Passive camera/clock/Wi-Fi follow scene rules: Closed portrait shows all; Closed landscape shows camera only; Open expanded shows clock/Wi-Fi; Open compact and immersive show none. The local `HH:mm` clock updates on static pages. These localized placeholders do not intercept page input.

On runtimes without host touch API v1, legacy touch emulation coalesces native mouse moves to one synthetic move/pan per animation frame, flushes the pending tail before release and cancels pending work on blur, page exit or mode changes. Scroll candidates are resolved once per contact; candidate/root overflow and containment are rechecked before each frame's writes, preserving live modal locks. Counters distinguish received/processed moves and scroll writes. This branch remains DOM emulation, without native kinetic scrolling. The shared observer reuses a bounded color-conversion cache; Windows bypasses the legacy DOM fixed-footer scan because native coverage is available. Host-to-page diagnostic publication is coalesced at 100 ms; native input/presentation and control API reads remain live. Unchanged pointer positions do not create extra composition commits; device recreation invalidates that cache explicitly.

## Safari scroll indicators

Windows Safari suppresses the root vertical scrollbar's native paint/hit region while retaining layout and scrolling. A passive screen-edge DirectComposition thumb follows the scene mask/transform and fades independently of the glass tree. Nested/horizontal scrollbars and immersive mode keep their native behavior; no page CSS is injected to hide scrollbars.

Scroll-indicator API v1 reports document epoch, root range/position, CSS suppression and native motion. Recent wheel/keyboard intent plus actual movement is required; script scroll alone, zero range, CSS `none` or hidden/zero-width custom bars suppress the thumb. Navigation and layout changes invalidate intent. Timing and thickness are engineering approximations, not Safari measurements. Other scroller classes and touch deceleration need separate acceptance; macOS has no matching host policy.

## Translucent fixed overlays

Viewport alpha and off-viewport extension are separate: fixed elements still end at the real layout viewport. Fixed-container color API v2 reports current-document coverage separately from color validity. The host checks five edge positions and positioning containers, then reuses upstream background-color and `PageColorSampler::predominantColor` rules without the previous-container cache. Native background colors take priority; the fallback paints a two-pixel strip of fixed/sticky layers, excluding text and replaced content other than iframes, then selects a predominant color rather than averaging unrelated colors. The upstream alpha/dimming policy controls blending against the page's extended background. Ordinary document images do not contaminate a transparent fixed wrapper's descendant background color. Mixed or partial coverage remains uncertain. Flattened Windows snapshots retain ancestor transparency across live compositing boundaries. Snapshot pixels produce scalars only.

Windows WC clips viewport-constrained composited subtrees to the real layout viewport when the host reserves insets. For a viewport-fixed layer under a positioned stacking context, the same clip is mapped into its actual graphics parent's coordinates. Ordinary content in that parent keeps its continuation. Fixed descendants whose containing block is an ancestor retain that positioning and paint behavior. WC scrolling stores a layer's top-left position directly. TextureMapper applies the anchor only as the transform origin, so a sized clip cannot shift a fixed or sticky layer during asynchronous scrolling.

A running transform on a sampled fixed or sticky paint path makes the continuation color uncertain while the page continues painting inside the viewport. Geometry also detects translations across the clip when a script changes the transform directly. A translation that leaves the viewport edge cutting through the layer's interior remains uncertain at that edge. A predominant color from painted pixels must match samples near both ends of a line in the same strip, parallel to the edge. This check also covers transparent viewport-sized hit targets. They can cover every hit-test point while unrelated foreground paint dominates the middle of the strip. A uniform background owned by the edge container or its native `::backdrop` can supply the color directly. Settled transforms that meet the edge, including finished animations with forwards fill, follow the same coverage and color rules.

The Windows host can retain a settled dimming color when an overlaid card appears. Each side first establishes a solid translucent sheet at all five ordinary edge hits within the same transparent fixed container. Later samples require the same document, container and sheet, at least one exposed point and continuous hit coverage behind the foreground. The sheet's live color and opacity remain authoritative. A replacement, missing or inconsistent coverage, a filter or a fully covering card releases the confirmation. A first paint that already includes the card follows upstream predominant-color sampling when the same continuous dimming sheet is already verifiable behind it. Incomplete or conflicting sheets return to document continuation after the existing bounded uncertainty interval when the sampled strip cannot supply a continuous color. This scoped exception follows the observed Safari opening order while continuing to sample other same-container color changes.

An author can also supply a separate fixed piece of the same dimming background outside the layout viewport. When foreground hits leave the inner edge color uncertain, the Windows host checks the current nonzero inset for a fixed piece owned by the same container. The exterior piece must match a unique solid, translucent sibling covering the viewport, cover the entire inset, remain the top hit at representative depths and positions, and have a paint path clear of author clipping and effects. If a viewport-wide `pointer-events: none` layer wins the initial hits, the host can honor pointer events to locate the lower container and verify its exterior. It then checks the fixed subtrees in front of that container across the inner strip. When repaint bounds overlap the strip, a bounded subtree snapshot checks the alpha of the actual paint, including text and replaced content. A visible canvas pixel or an unavailable snapshot prevents the recovery. The host blends the exterior piece over a uniform opaque background from the modal's ordinary-flow ancestors when they cover the inset, with the page's extended background as the fallback. This uses the actual backing when it hides the document root. A flattened off-viewport snapshot could reintroduce the inner fixed sheet that WC clips at the viewport. The host paints only the verified color scalar and preserves the page layout and scroll range.

The macOS bridge checks for the private `_setContentInsetBackgroundFillEnabled:` preference and `_updateFixedColorExtensionViews` before selecting system WKWebView inset fill. Availability can differ by system WebKit revision. The observed macOS 27.0.1 runtime exposes `_updateFixedColorExtensionViews` and the view-level `_setUsesAutomaticContentInsetBackgroundFill:` but lacks the preference setter. The independent probe uses the view-level selector for layer observations. Compatibility in the application bridge requires separate verification.

`just test-windows-upstream-fixed-colors` compares fresh-document v2 results with the preserved upstream-rule export and actual inset pixels using generic patterned-underlay fixtures. It also checks the continuous dimming sheet during card reveal, restyling, replacement and dismissal. A separate validation runtime can be linked with `just build-windows-webkit-runtime -RuntimeRoot <absolute-temp-build-directory>`; the successful build selects it for subsequent checkout launches, while already running instances keep their loaded DLLs. `DEVDUO_WEBKIT_RUNTIME` can explicitly override the selection.

Frame presentation API v1 binds edge state to the produced page frame and its submitted geometry. Absence in a rendered frame removes that side immediately. Reliable colors replace prior values; uncertain colors retain a value for at most 600 ms without renewing the deadline. A one-shot native invalidation expires static uncertain colors using the retained page, without forcing a WebCore rendering loop. Navigation/document and backing-store generations reject obsolete results. Side continuation owns intersections. Fixed-color snapshots still produce scalars only.

Frame timing API v2 carries native input sequences through render/commit, GPU work, UI receipt and host frame transfer/commit. Host records separately bracket `WM_PAINT` validation and DirectComposition submission. All timing stages use machine-wide Windows QPC seconds, including the original native input retained through coalescing. An explicit anchor checks agreement with host Stopwatch; neither timestamps nor frame IDs establish scanout. See [Microsoft QPC guidance](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps). Diagnostic history is bounded. `DEVDUO_FRAME_TIMING_HISTORY=1` exposes up to 128 events instead of the default eight and includes the physical fill rectangles and RGB values for successful page-frame submissions. Frame-surface diagnostics distinguish actual GPU/CPU transport, GDI uploads and explicit captures. CPU stacks are discrete samples, not a statistical CPU profile. Correct asynchronous scrolling remains work in progress.

## Desktop input, dialogs and accessibility

Desktop API v1 maps physical popup/caret coordinates through the borrowed presenter HWND, quarter-turn and offsets. Select anchors, monitor bounds, ownership and activation follow the visible root owner; the original WebKit HWND remains the real input source.

TSF `ITextStoreACP` plus the IMM path drive actual WebCore composition. The store holds only the pending transaction, not a substitute document. A focused message hook feeds the text service once before Avalonia translates keys. UTF-16 preedit, selection replacement, commit/cancel and candidate positioning use real editor state. Frame/document/element ownership rejects stale work; focus changes retire the old composition without reselecting its range. WebCore draws the visible caret; an invisible OS caret supports IME queries. Blur/navigation/disposal release hooks, contexts and COM ownership. Full surrounding text/reconversion is unsupported.

Ordinary `WKPageUIClientV6` listeners provide localized owned alert/confirm/prompt dialogs with requesting-frame origin, serialized completion and navigation/disposal cancellation.

`WKViewRequestHostAccessibilityTree` and `WKViewPerformHostAccessibilityAction` expose document-scoped WebCore AX semantics through production IPC; the legacy synchronous snapshot export remains available. UIA/MSAA reads use a published cache, with one asynchronous snapshot in flight and coalesced refreshes. Protected values stay private; grouping nodes do not serialize descendant text as editable values. Navigation/disposal reject late snapshots. The visible presenter owns one UIA fragment with source/intermediate HWND bridges. Snapshot traversal still runs on the Web process main thread and may be expensive on large documents. UIA TextPattern/IAccessible2 remain unsupported. See the [Windows handoff](009-WINDOWS-HANDOFF.md) for acceptance.

Notification-driven refresh requires a listening UIA client or an installed WinEvent hook for the pending event; explicit reads/actions still request fresh state. A past accessibility read alone does not enable permanent background tree rebuilding. Subscriber detection is conservative and can remain active for global listeners, so this gating is not a claim of measured CPU reduction.

## Inspector, navigation and first paint

Local Inspector is detached so it never reserves measured viewport space. Native remote inspection is a separate unauthenticated WebKit TCP protocol, mutually exclusive with local Inspector; page CDP/chii uses the independent token-protected bridge. Stop/disposal releases listeners and frontends. Native proxy settings support system/direct/custom modes. See [developer architecture](001-ARCHITECTURE.md#developer-tools-and-page-proxy).

Before a validated document-extent observation, only unverified inset continuation receives fallback fill; the layout viewport remains live during loading. One observation-verification worker drains newer messages without dropping them behind an older reply. Native navigation, commit, finish, observation and source-pixel timings are different milestones.

The external Avalonia pump services WTF deadlines through one nearest-deadline timer and the existing fire/cancellation queue, not polling or a nested message drain. Auxiliary-process handle transfer uses inherited/registered process capabilities, an explicit inheritance allowlist, peer validation and processed/accepted acknowledgments. Transfers adopt or release target-owned handles and reject stale connection attempts. Registry lookup retains a strong reference without duplicating the process handle; failed registered operations do not silently retry `OpenProcess`. This is IPC optimization, not a new sandbox boundary.

Network bootstrap preserves request/attempt ordering and samples preferences after acknowledgment. Remote Canvas completes GPU bootstrap on its owning dispatcher before entering an isolated sync stream; one deadline covers both handshake phases, and reentrant close invalidates the captured stream. Native validation, stream identity and resource ownership must not be weakened to hide startup waits.

## Validation and diagnostics

Choose affected recipes from [Windows acceptance](009-WINDOWS-HANDOFF.md). Ordinary-page probes compare real baseline and inset viewports, including standards/quirks, transparent/gradient, height changes and animation at fractional Fit. Geometry equality does not certify paint. Native source pixels, final-window observations and input/AX results remain separate evidence.

The presented harness supports original color HTML, supplied `DEVDUO_URL`, left-inset scenes, observations and real window-size/Fit changes. Serve reviewed fixtures with:

```powershell
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

Ordinary tests isolate website/host preferences. Cross-process storage tests require the same absolute test profile and a unique origin/token; never clear the user's real website store. `just package-local-windows` uses the existing source runtime for an unsigned local preview; formal release packaging uses the published metadata-selected dependency.

The separate official prebuilt probe may enable the private testing inset API for comparison. Its ephemeral download can expire; preparation reports unavailable artifacts explicitly. Probe flags and artifacts are diagnostic, not production API compatibility or native acceptance.

## Decisions and failure lessons

- **Engine and layout:** preserve WebKit ownership and genuine obscured insets. Page zoom, modified DOM sizes, injected fixed markers and stitched screenshots cannot establish correct continuation.
- **Transparency:** WinForms transparent backgrounds repaint parents, not underlying sibling WebKit windows. Use live composition with separate material and foreground layers.
- **Temporal capture:** GDI layered-window readback can disagree with composed output around a transition. Compare consecutive Windows Graphics Capture frames, including the first, before attributing a readback change to visible material flicker. Neither capture counts nor composition counters alone prove temporal stability.
- **Rendering:** a populated DOM/AX tree or successful submission can coexist with blank pixels. Validate static native paint and final windows separately. The old remote white-frame stall was not conclusively isolated; do not invent a single root cause.
- **Backend transitions:** cached local Skia `DrawTextBlob` operations crashed when replayed on a remote recorder. Keep glyph operations backend-neutral across layer promotion and retain stale-frame generation checks.
- **Readiness and reentrancy:** early synchronous AX discovery stalled startup; clearing invalidation after a synchronous reply lost newer events. Wait for a committed document and preserve events received while pumping messages.
- **Coordinates and references:** retain complete tab containers, independent body/content transforms and Closed 180° history. Native popup/caret mapping must follow the visible presenter, not an offscreen source's `ClientToScreen` alone.

Original diagnostic captures, failed attempts and matching build inputs remain private and unchanged. Current limits and metrics live only in the milestone; this guide does not accumulate per-run totals.

[English](014-WINDOWS-WEBKIT.md) | [中文](../zh/014-WINDOWS-WEBKIT.md)
