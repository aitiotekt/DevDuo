# Native painting under closed Safari chrome (2026-10-21 follow-up)

The previous host physically reduced WKWebView to the page viewport. That prevented ordinary flow content from painting behind and below the floating address bar. Closed Safari mode now keeps the native WKWebView at the complete requested screen and uses the public `obscuredContentInsets` API to shrink the page layout/visual viewport independently.

At 1:1, the native view remains466x678 DIP. Insets right84/bottom98 produce an actual page382x580; compact bottom80 produces382x598. Ordinary flow paints below that viewport while fixed-bottom elements remain within it. `setMinimumViewportInset` / `maximumViewportInset` describe bottom80..98 for small/large viewport units. The independent probe measured100svh580 /100lvh598 and100dvh580 or598. The current macOS Fit preview scales the entire native coordinate space, preserving these CSS dimensions; its rendered DIP frame is recorded separately. The270° scene moves the84-point side inset to the left. See the current [scene matrix](005-SAFARI-SCENES.md).

The actual app was verified in expanded and scrolled states; switching to App WebView cleared all insets and produced466x678 with no chrome. State now distinguishes native drawing frame, actual page viewport, obscured insets, rendering strategy and whether continuation is enabled. The status label also shows native and page dimensions separately. Edge-color sampling stays within the unobscured content area, because `takeSnapshot` on this runtime excludes under-inset continuation even when it is visibly presented by the actual window. A snapshot alone is therefore insufficient to verify this effect.

Evidence is under `captures/desktop-validation/under-chrome/`: original CUA JPEGs, initial/scrolled/app-reset state JSON, source hashes and both build logs. The independent numbered-row probe really displayed rows600..680 beneath a page reporting innerHeight580, while a fixed marker ended at580. The probe is `scripts/archive/2026-10-21/viewport_insets_probe.swift`:

```sh
mkdir -p temp/cache/swift
xcrun swift -module-cache-path temp/cache/swift scripts/archive/2026-10-21/viewport_insets_probe.swift temp/inset-capability
```

The probe opens and automatically closes its own small window. `HOLD_PROBE=1` keeps the closed-inset case open for native app-window inspection; close its window afterwards. Its `takeSnapshot` PNGs are deliberately retained as diagnostics and do not prove the presented under-inset surface. Top-inset cases are diagnostic only and are not calibrated to the Safari top chrome.

This implementation is scoped to macOS Closed mode on the tested macOS27 runtime. Open mode and Windows retain the previous reserved-frame approximation. Windows has no claimed equivalent native under-chrome viewport API here; its shared status-label change cross compiled successfully. Both platform builds succeeded. No iOS optical equality or hardware dimensions are inferred.

The previous `container-modes/manifest.json` and earlier deliverable preview refer to the prior build. Its two changed sources are preserved in `under-chrome/before/`; the new manifest records the follow-up version. The actual Device Hub Safari matrix and its original frozen source snapshots remain unchanged. Current web assets use the DevDuo telemetry names.

References: [Apple obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets), [WebKit introduction](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/).

[English](006-UNDER-CHROME-RENDERING.md) | [中文](../zh/006-UNDER-CHROME-RENDERING.md)
