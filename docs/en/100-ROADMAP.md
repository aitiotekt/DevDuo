# Roadmap

The desktop host is a calibration workbench around the measured device preview: a real address bar, a grouped settings sidebar and a status bar, themed with SukiUI on Avalonia 12. Current behavior is described in the [usage guide](004-USAGE.md) and [architecture](001-ARCHITECTURE.md); platform gaps are tracked in [Platform capabilities](015-PLATFORM-PARITY.md) and the [Windows handoff](009-WINDOWS-HANDOFF.md).

## Host UI

- Further host control and visual polish, with keyboard/accessibility and presented-window checks at each affected platform.
- Real tab management is separate future work; the current two-tab strip illustrates measured shell variants.
- Optional: keep the page across fold/mode changes by reconfiguring the mask in place. Body rotation already preserves the native view; broader preservation needs new native evidence that viewport, insets and Closed 180° behavior match the accepted scene.

## Windows input and accessibility

- Resolve Windows touch direction locking after native scroll-target selection. The contact bridge has `touch-action` and pointer deltas before it knows which axes the selected nested/root scroll chain can consume. Apply direction intent to that chain, including one-axis availability, deliberate diagonal gestures and handoff to an ancestor, and carry the same decision through release velocity. Verify cancellation and mode switches and preserve the legacy DOM emulation path until it has equivalent checks.
- Implement accessibility text navigation (UIA TextPattern/IAccessible2), surrounding-text/reconversion and broader assistive-technology compatibility. Current Value/Invoke and Narrator label/value checks do not cover them.

## CDP and agent capabilities

A source audit of the pinned [chobitsu 1.8.6 registry](https://github.com/liriliri/chobitsu/blob/v1.8.6/src/index.ts) distinguishes actual implementations from compatibility no-ops.

| Priority | Current limitation | Follow-up while retaining WebKit |
| --- | --- | --- |
| Next | Runtime by-value serialization/context semantics are incomplete; object-release registrations include no-ops. | Correct serialization/error boundaries and release tracked objects/groups for long agent sessions. |
| Next | `DOM.getBoxModel` is a no-op; frame/context routing is incomplete. | Implement measured DOM geometry and selectors with explicit transform/scroll/frame coordinates. |
| Next | Input is synthetic JS; native focus/default actions and IME are not equivalent. | Bridge verified native click/type/wheel/touch paths and preserve scene/Fit mapping. |
| Next | Navigation/history/resources are page approximations; no capability manifest. | Reuse native navigation, expose real layout metrics/lifecycle and method-level implemented/approximate/unsupported capabilities. |
| Later | Network hooks miss earlier requests, main navigation and complete resource/cookie data; cache/quota responses include placeholders. | Add explicit native network/storage integrations; page JS cannot observe HttpOnly cookies or complete traffic. |
| Separate project | Breakpoints/Target auto-attach/Emulation include no-ops; CPU/heap profiling, the CDP Accessibility domain and real worker/browser sessions are absent. | Evaluate WebKit Inspector Protocol/JSC adapters and their platform/version limits; JS injection cannot supply these engine capabilities. |

Prioritize the agent loop of state → DOM/geometry → native input/navigation → screenshot. A DevDuo-specific MCP adapter can wrap verified REST/CDP operations. [Playwright's CDP connection](https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp) is Chromium-only, and its [WebKit backend](https://playwright.dev/docs/browsers#webkit) relies on patches that cannot attach to system WKWebView.

## Acceptance boundaries

The host address bar and the simulated Safari address surface have different responsibilities. The latter belongs to the measured device scene; it keeps its calibrated frame, floating behavior and page-derived appearance. The multi-tab switch represents Safari shell variants.

Host UI refinements must preserve both container modes, the eight body/fold mappings, Open single/multiple-tab variants, scroll transitions, independent inner/outer paths, and actual CSS viewport dimensions. A changed host panel may affect available preview space; it must not silently change logical device dimensions. Check before/after state records at the same settings. Do not place ordinary Avalonia controls over native browser children.

iOS Safari widgets, native glass material, hardware simulation and device geometry are separate evidence-dependent work. A general visual redesign cannot replace measured Safari frame references.

[English](100-ROADMAP.md) | [中文](../zh/100-ROADMAP.md)
