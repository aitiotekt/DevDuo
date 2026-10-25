# Windows WebKit backend

The measured Windows page exclusively uses a pinned WebKit Windows Port with production host extensions. Avalonia owns the workbench, WebKit owns the document/input, and DirectComposition presents the live page and Safari approximation. Runtime acceptance is maintained in the [Windows handoff](009-WINDOWS-HANDOFF.md); this page owns implementation and reproduction.

## Host API source build

`native/webkit/windows/` owns the source pin, four patches, portable build environment, cache management and runtime packaging. `tests/DevDuo.WebKitProbe/` owns standalone diagnostics and fixtures. See [runtime ownership](../../native/webkit/windows/README.md) and [runtime release policy](013-RELEASE.md#windows-webkit-runtime-releases).

The current upstream pin is `324423@main`, revision `6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d`. The diagnostic archive lock and release metadata must agree on upstream revision. Production uses the patched source build, not the official testing DLL.

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
mise exec -- just dev
```

mise manages portable CMake/Ninja/Ruby/Perl/gperf/pkgconf/vcpkg and the normal Python environment. VS C++/Clang and Windows SDK remain system dependencies. Custom vcpkg triplets preserve interpreter/generator ownership. Source/dependency caches live in `temp/cache/windows-webkit/`, output in `temp/build/windows-webkit-host/`, and toolchain-specific retained trees in `temp/build/windows-webkit-trees/`. Do not clean these between incremental builds. The build selects `ENABLE_BACK_FORWARD_LIST_SWIFT=OFF`; do not mix its files with the official Swift-enabled archive.

| Patch | Responsibility |
| --- | --- |
| `0001-windows-host-viewport-api.patch` | Production insets and independent logical-view/backing-scale API |
| `0002-windows-content-painting.patch` | Painting/damage through insets, canvas boundaries, color/appearance/indicator integration and native transport support |
| `0003-windows-developer-api.patch` | User-script removal, native Inspector listener and proxy API |
| `0004-windows-desktop-integration.patch` | Physical desktop mapping, TSF/IMM, AX transport and current software-frame/glyph handling |

The working-tree host requires viewport v2, painting v2, content color v1, appearance v1, developer v1, fixed-container color v1, scroll-indicator v1 and desktop v1. The application-owned compositor separately requires composition v4 and presentation v1. Versions and required exports are checked at startup. Manifest hashes are provenance outputs, not runtime pass gates. Runtimes without desktop v1 are incompatible; application delivery requires the matching metadata-selected engine release.

## Avalonia backend

A checkout discovers `temp/build/windows-webkit-host/bin`. Installed apps use `webkit/bin` plus `webkit/runtime-manifest.json` beside the executable. An absolute `DEVDUO_WEBKIT_RUNTIME` override is authoritative: missing/incompatible capabilities fail explicitly rather than selecting another engine. Test-only IPC stays disabled.

`WindowsWebKitApi.cs` binds the C ABI; `WindowsWebKitView*.cs` owns navigation, storage, input, dialogs and accessibility; `WindowsWebViewPanel.cs` configures scene geometry. The persistent website store is `DevDuo/WebKit` under local application data. An absolute `DEVDUO_WEBKIT_PROFILE` isolates Windows test profiles. Native views share one store session; persistent cookies, localStorage, IndexedDB and caches survive restart. sessionStorage retains normal document semantics. Resetting host preferences does not clear website data or migrate/delete an older engine's profile.

Rotation, Fit, outline, language, appearance and shell settings update the existing native view. Fold/container-mode changes intentionally replace it. Navigation/document/geometry generations and disposal guards reject stale async results. Owned HWNDs, callbacks, COM resources and handlers are released predictably; source destruction invalidates pending work before managed detachment.

## Drawing and document boundaries

`WKViewSetObscuredContentInsets` sends ordinary production IPC. `WKViewSetUnobscuredSizeRange` supplies real minimum/maximum/default sizes for viewport units. `WKViewSetHostPresentationGeometry` separates logical scene size from physical backing scale; Fit preserves the layout viewport with page zoom fixed at 1. It does not modify CSS or JavaScript geometry accessors.

The drawing frame includes genuine top/left/right/bottom insets. Engine paint clips, origins, scroll-copy extent and damage cover that full domain while fixed/sticky positions and scroll range use the unobscured viewport. Inset/geometry changes request event-driven complete damage even when logical size is unchanged. Fractional device-pixel scroll deltas use normal damage painting instead of truncating a backing-store copy; integral deltas retain the optimization.

Windows non-composited frames paint into an engine-owned shared CPU bitmap, then follow WC commit, retained native painting and DirectComposition. Unaccelerated snapshots use local shared bitmaps as well. Accelerated WC frames retain GPU transport/fences and readback into the existing shared-bitmap path; this is not zero-copy rendering. Submitted geometry generations reject stale frames. Cached Windows glyph display lists use backend-neutral operations across local/remote contexts.

The host preserves valid two-dimensional document flow and fills only beyond its extent with the explicit opaque html/body background fallback, otherwise white. For a top-zero bottom inset, valid continuation is `clamp(documentHeight - scrollY - viewportHeight, 0, bottomInset)`. Toolbar theme tint never replaces that canvas rule. Fixed-container colors are a separate extension policy, described below. No screenshot, repeated row, DOM padding or fake viewport supplies continuation.

## Mobile request identity

`BrowserIdentity.MobileUserAgent` supplies the observed Safari compatibility identity to both platforms before the first native request:

```text
Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1
```

Both containers use the same observation; no separate immersive identity was measured. This changes request headers and native `navigator.userAgent`, not platform/DPR/viewport properties or the actual desktop engine. The frozen `OS 18_7` token is not evidence of the observed runtime version, and the string does not authenticate physical Duo hardware. Keep the original observed value and its private collector provenance rather than inferring a replacement from the simulator OS.

## Live surface composition

`HostCompositor.cpp` implements composition v4 with D3D11, Direct2D/DirectWrite and DirectComposition. `CreateSurfaceFromHwnd` imports the WebKit child's live DWM surface. WebKit offscreen mode is enabled before navigation so software and accelerated frames reach its retained painter and layered HWND. The measured page has no WebView2 fallback; only the separate developer-tools frontend uses system WebView2.

Rotated scenes retain a full-sized, cloaked layered source host outside the smaller physical clip. Presentation v1 rotates/translates the complete tree; `WindowsScreenPresenter` inversely maps input and focus. Body, content-in-body and scene-to-canvas coordinates follow the [scene contract](005-SAFARI-SCENES.md#body-and-content-coordinate-systems).

Composition order is page/valid continuation, document-boundary fills, local blur, rounded tint/rim/shadow, then clear text/icons. Blur reads the undecorated page, clamps sampling edges and precedes rounded clipping. Premultiplied alpha and separate glyph layers preserve readable foregrounds. Hit frames, optical frames and text clips remain distinct; selected tabs use complete visual containers.

Physical HWND synchronization and resource generations handle resize, ancestor clips and device recreation without navigation. Hardware setup may retry the same D3D11 route with WARP; unsupported composition reports a capability error rather than certifying an old GDI material fallback. Device-loss/WARP recovery still lacks fault-injection acceptance. Scene/resource counters are not WebKit frame IDs, atomic DOM/frame timestamps or final DWM FPS.

## Fractional boundary rasterization

Content clips use half-up nearest-pixel edges to agree with Skia hard clipping; damage rectangles conservatively enclose changed pixels. Confusing these rounding rules leaves a one-pixel gap at fractional Fit. Backing dimensions enclose the drawing domain, and origins align to physical pixels.

Before GDI upload, transparent premultiplied software pixels are composited over the explicit under-page background, then white: `SetDIBitsToDevice` does not honor alpha. `WM_PAINT` and diagnostic `WM_PRINTCLIENT` convert physical damage to logical coordinates consistently. No content stretching or edge-pixel copying repairs the boundary.

Marker-free static boundary fixtures exercise all edges. Their source-paint evidence is distinct from final DWM output; [incremental acceptance](009-WINDOWS-HANDOFF.md#windows-milestone-and-incremental-regression) selects Fit/DPI cases only when affected.

## Shell material and dynamic state

`config/safari-shell.json` owns tunable Windows optical parameters and measured scene references. Material roles separate top background, floating surfaces, compact addresses and tab tracks. Saturation/brightness/contrast use a ColorMatrix; foreground glyphs follow shell appearance independently of backdrop colors. Dark/light backdrop recipes interpolate smoothly. These are artistic desktop approximations, not physical refraction or Apple's Liquid Glass kernel.

The current native rendered edge determines backdrop color; `theme-color` remains diagnostic. Content color API v1 uses ordinary snapshot IPC, returning only request/status/RGB. Closed samples the bottom unobscured edge; Open samples the top. A strip of at most 18 logical rows is painted at its own size, then resampled to width 64 and averaged in unpremultiplied sRGB, ignoring low-alpha pixels. Snapshot images never enter page presentation. Sampling debounces 180 ms, allows one request in flight and rejects stale generations. A failed sample retains the current document's last native color or uses a labeled DOM fallback until one succeeds; navigation resets ownership.

Appearance API v1 notifies WebKit and page media queries without navigation. Native input intent correlated with document movement changes chrome state: cumulative down >24 CSS px compacts, up >8 expands, and intent expires after 350 ms. Zero/opposite motion, viewport clamping and script-only scrolling do not change state. Concurrent script/native movement remains a qualification.

Address/menu/bookmarks/overview expand the shell; only reload reloads; plus is a return-to-top illustration. DirectWrite measures and draws the same font. Passive camera/clock/Wi-Fi follow scene rules: Closed portrait shows all; Closed landscape shows camera only; Open expanded shows clock/Wi-Fi; Open compact and immersive show none. The local `HH:mm` clock updates on static pages. These localized placeholders do not intercept page input.

## Safari scroll indicators

Windows Safari suppresses the root vertical scrollbar's native paint/hit region while retaining layout and scrolling. A passive screen-edge DirectComposition thumb follows the scene mask/transform and fades independently of the glass tree. Nested/horizontal scrollbars and immersive mode keep their native behavior; no page CSS is injected to hide scrollbars.

Scroll-indicator API v1 reports document epoch, root range/position, CSS suppression and native motion. Recent wheel/keyboard intent plus actual movement is required; script scroll alone, zero range, CSS `none` or hidden/zero-width custom bars suppress the thumb. Navigation and layout changes invalidate intent. Timing and thickness are engineering approximations, not Safari measurements. Other scroller classes and touch deceleration need separate acceptance; macOS has no matching host policy.

## Translucent fixed overlays

Viewport alpha and off-viewport color extension are separate. A fixed mask still ends at the real layout viewport. Fixed-container color API v1 reads WebCore's four-edge recognition/blending through ordinary IPC, including dialog backdrops, and returns edge states/opaque RGB. The host fills only the obscured strips; empty/unsupported results clear stale colors. The legacy DOM-footer field cannot override a native result.

Windows includes the renderer's own background opacity in edge blending; this does not generalize ancestor group opacity, filters, gradients or arbitrary stacking contexts. At side/horizontal intersections, side continuation owns the corner. Sampling is bounded, single-in-flight and generation-checked, with a small retry budget after first observation/load or later rendering changes. Static pages do not require a paint loop. System macOS support and its limited fallback are documented in [platform parity](015-PLATFORM-PARITY.md).

## Desktop input, dialogs and accessibility

Desktop API v1 maps physical popup/caret coordinates through the borrowed presenter HWND, quarter-turn and offsets. Select anchors, monitor bounds, ownership and activation follow the visible root owner; the original WebKit HWND remains the real input source.

TSF `ITextStoreACP` plus the IMM path drive actual WebCore composition. The store holds only the pending transaction, not a substitute document. A focused message hook feeds the text service once before Avalonia translates keys. UTF-16 preedit, selection replacement, commit/cancel and candidate positioning use real editor state. Frame/document/element ownership rejects stale work; focus changes retire the old composition without reselecting its range. WebCore draws the visible caret; an invisible OS caret supports IME queries. Blur/navigation/disposal release hooks, contexts and COM ownership. Full surrounding text/reconversion is unsupported.

Ordinary `WKPageUIClientV6` listeners provide localized owned alert/confirm/prompt dialogs with requesting-frame origin, serialized completion and navigation/disposal cancellation.

`WKViewCopyHostAccessibilityTree` / `WKViewPerformHostAccessibilityAction` expose document-scoped WebCore AX semantics over bounded production IPC. Protected fields do not export values. One UIA fragment belongs to the visible presenter; thin source/intermediate HWND bridges and a Windows-specific Avalonia peer avoid duplicate trees. MSAA, Value/Invoke/Focus, names and physical hit bounds use that model. Queries wait for a committed document; native invalidation received during synchronous IPC survives its reply. Native notifications drive updates rather than periodic full-tree polling. UIA TextPattern/IAccessible2 remain unsupported. Test options and native acceptance belong in the [Windows handoff](009-WINDOWS-HANDOFF.md).

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
- **Rendering:** a populated DOM/AX tree or successful submission can coexist with blank pixels. Validate static native paint and final windows separately. The old remote white-frame stall was not conclusively isolated; do not invent a single root cause.
- **Backend transitions:** cached local Skia `DrawTextBlob` operations crashed when replayed on a remote recorder. Keep glyph operations backend-neutral across layer promotion and retain stale-frame generation checks.
- **Readiness and reentrancy:** early synchronous AX discovery stalled startup; clearing invalidation after a synchronous reply lost newer events. Wait for a committed document and preserve events received while pumping messages.
- **Coordinates and references:** retain complete tab containers, independent body/content transforms and Closed 180° history. Native popup/caret mapping must follow the visible presenter, not an offscreen source's `ClientToScreen` alone.

Original diagnostic captures, failed attempts and matching build inputs remain private and unchanged. Current limits and metrics live only in the milestone; this guide does not accumulate per-run totals.

[English](014-WINDOWS-WEBKIT.md) | [中文](../zh/014-WINDOWS-WEBKIT.md)
