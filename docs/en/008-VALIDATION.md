# Validation

## Current preparation checks

During the DevDuo repository preparation on 2026-10-21:

- The public-file audit passed with raw captures, measurements, private notes and build products excluded. It checks targeted personal-path/identifier patterns, legacy branding, Python syntax, JSON and local documentation links.
- Portable self-checks passed: concurrent receiver persistence, invalid record rejection, PNG round trip, changed-pixel detection, explicit exclusions, size mismatch rejection and shell/screen containment.
- The macOS Avalonia/Swift build and Windows cross compilation both succeeded with zero warnings and zero errors, using SDK 10.0.401 and locked NuGet dependencies. Native bridge compilation now writes to ignored project intermediates rather than the source directory or a fixed temporary path.
- Regenerating the public device profiles and eight Safari scenes/24 states from the original local evidence produced semantically identical configuration. Renaming did not change measured screen/mask/widget geometry.

A separate public-only checkout containing 69 eligible files, with no captures or measurements, passed the repository audit, portable checks and macOS native build with zero warnings/errors. The missing-private-source extraction path returned a clear actionable error rather than silently inventing references.

The renamed `DevDuo.app` was launched and inspected through accessibility and an actual window screenshot. Save state succeeded in Closed Safari (382 × 580 CSS px), Closed immersive (466 × 678), Open Fit with one tab (951 × 589), and Open Fit with multiple tabs (951 × 543). The menu and window identify the application as DevDuo. These checks exercise the renamed Swift bridge, JavaScript message channel, bundled configuration and source-checkout diagnostic output; the installed-app fallback output location was inspected in code but was not separately exercised. GitHub Actions is configured for portable checks and Windows compilation, but no remote workflow execution is claimed before the initial push.

## Existing interactive baseline

The original calibration implementation was interactively inspected on macOS 27 with system WKWebView, using native app accessibility, actual app-window screenshots, saved state and separate Device Hub Safari references. This historical evidence is retained locally and excluded from the public repository.

- Closed and Open immersive views reported the requested full logical viewports. Independent exterior and screen paths were inspected, including asymmetric rotated corners.
- An extra Y flip in the WKWebView layer mask was found and removed. A 90° asymmetric scene exposed the issue; a symmetric portrait outline alone would not have done so.
- The body selector was validated against the eight actual Closed/Open body-to-scene mappings. Closed 180° is the landscape retained during the recorded clockwise rotation sequence, not a universal upside-down behavior.
- Safari initial/scrolled states and Open single/multiple-tab variants were checked across the 24 reference states. Fit preview on macOS preserved CSS dimensions while scaling native coordinate space.
- The selected Open tab now uses the full title-container frame including its close-button region. All four Open body angles were checked against original native component frames; selected and unselected title text uses one centered scene font.
- Closed mode actually painted ordinary document flow below the floating address bar while retaining the measured smaller viewport through obscured insets. Switching to immersive mode cleared the insets. A WK snapshot alone omitted some presented continuation, so actual window inspection was required.

Original app-window captures are JPEG observations. Any PNG conversions are explicitly labeled derivatives. Use original lossless simulator PNGs for framebuffer comparison. Difference-image statistics describe a particular capture pair; they are not an iPhone hardware fidelity score.

## Platform limits

Windows has a compiled WebView2 implementation with shared scene geometry, clipping, scroll/theme observation and GDI tint/alpha fallback. Native Windows Runtime initialization, input, display scaling, monitor transitions, zoom-based Fit behavior and material appearance remain pending. Follow [Windows handoff](009-WINDOWS-HANDOFF.md) to establish a runtime baseline. Cross compilation is not runtime validation.

The current macOS package targets macOS 27. Its older-system material fallback has not been validated by lowering the deployment target and running it. Open mode retains a reserved native content frame; exact continuation beneath top Safari chrome is not established. The iOS Liquid Glass kernel, real hardware dimensions/radii and partially open perspective model remain unknown. See the [measurement contract](002-MEASUREMENT-CONTRACT.md).

## Earlier tooling and documentation baseline

Before the command-ownership refactor, the managed environment, dependency locks, relative documentation symlinks, bilingual source checks, portable tests, VitePress build, macOS build, Windows cross compilation and unsigned macOS package were checked. A public-only source snapshot also passed setup/checks/docs build without raw measurement evidence. These observations describe that earlier implementation; current command ownership is documented in [Development](010-DEVELOPMENT.md) and individual script decisions in the [inventory](012-SCRIPT-INVENTORY.md).

The documentation site was inspected for English/Chinese navigation, numbered routes and local search. Convention-page technical aliases are excluded to avoid duplicate search entries. Restart static preview after rebuilding; use the development server while editing. No remote CI execution or Windows native runtime result is implied.

## Command ownership, reviewed scripts and app localization

The refactored checkout passes the public audit, 22 real relative documentation links / 20 bilingual pairs, Ruff, 3 JavaScript tests, 15 Python tests and the localization harness (881 assertions, 88 keys per language, all eight mappings / 24 scene states). JS lint/typecheck and the strict VitePress build pass. macOS builds, the unsigned development bundle and Windows cross compilation succeed with zero reported warnings/errors. A separate public-only snapshot with fresh project dependency directories completes locked setup and full verification without private captures. An additional dependency-free scratch check confirms doctor does not install dependencies and pnpm rejects missing dependencies without changing lock hashes.

The maintained Swift video inspector was built and run against an original local transition video; source/per-frame hashes, five decoded frames and changed first/last pixels passed validation. Archived extractors reproduce identical public configuration; the independent historical verifiers pass 24 desktop states and all four selected-tab angles. These are historical-evidence reproductions, not new simulator captures or optical equivalence claims.

The new macOS app was checked interactively in system-default, English and Simplified Chinese. Switching English to Chinese while scrolled preserves the existing page URL, scrollY 580, 382 × 598 CSS viewport, native frame, obscured insets and widget geometry. Native accessibility labels update, the app-owned color fixture relabels in place while retaining the Ocean selection, and existing URL validation errors update with language. English preference survives an actual app restart. Open Fit remains 951 × 543 with multiple tabs and 951 × 589 with one tab; immersive Open remains 951 × 669. The final inspection uses Chinese Open Safari Fit.

The documentation browser check covers new localization/inventory routes, both language directions and local search. Windows runtime language switching, accessibility, preference storage and native appearance remain pending alongside the existing platform acceptance items. Raw sources and the original repository index were not rewritten.


[English](008-VALIDATION.md) | [中文](../zh/008-VALIDATION.md)
