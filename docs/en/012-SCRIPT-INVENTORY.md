# Script review and ownership

All **20 original `tools/` scripts** were reviewed individually: 9 retained/refactored Python scripts, 1 Python helper merged into the simulator module, 6 cohort Python scripts archived, 1 Swift utility retained as a package, 1 Swift experiment archived, and 2 redundant shell wrappers removed. The maintained Python package is `scripts/python/devduo_tools`; `tools/` is no longer an execution or organization boundary.

The [machine-readable review index](../../scripts/archive/index.json) records each original SHA-256, disposition, destination and reason. Original captures, measurements, local records and frozen source snapshots remain unchanged. Reorganized archives have path/import, explicit-output and missing-input adaptations; the original hashes identify the scripts before that organization.

## Individual decisions

Paths in the first column are relative to the former `tools/` directory.

| Original script | Decision and destination | Concrete reason |
| --- | --- | --- |
| `calibration_server.py` | Retain → `scripts/python/devduo_tools/calibration_server.py` | Fixed-resource loopback receiver and concurrent JSON persistence are reusable. Add explicit loopback binding, UTF-8 persistence and input/error checks. |
| `capture_macos.py` | Retain → `scripts/python/devduo_tools/capture_macos.py` | Explicit simulator display, unchanged PNG bytes, hashes and verified record association are valuable evidence collection behavior. Reject path-like state labels and black/transparent captures. |
| `check_repository.py` | Retain → `scripts/python/devduo_tools/check_repository.py` | Read-only public-file/private-data and link audit remains useful. Audit intended worktree removals even when the initial Git index still contains old paths; never alter that index. |
| `png_compare.py` | Refactor → `scripts/python/devduo_tools/png_compare.py` | Keep exact-size RGBA differences, explicit crops/exclusions and hashes. Replace incomplete handwritten decoding with locked Pillow integrity/palette handling; reject higher-depth conversion and implicit alignment. |
| `sample_appearance.py` | Retain → `scripts/python/devduo_tools/sample_appearance.py` | Explicit named pixel regions and encoded RGBA statistics are portable. Keep limits on color management, material identification and physical inference. |
| `validate_framebuffer_mask.py` | Retain → `scripts/python/devduo_tools/validate_framebuffer_mask.py` | Original local PDF versus framebuffer-alpha overlap is reusable with an independently verified scale. Poppler is optional; no automatic rotation/resizing or physical-unit conversion. |
| `validate_video_evidence.py` | Retain → `scripts/python/devduo_tools/validate_video_evidence.py` | Check original video hash, decoded frame dimensions/hashes, nonblack pixels and first/last change. Legacy metadata requires an explicit expected source hash. |
| `run_safari_probe.py` | Retain → `scripts/python/devduo_tools/run_safari_probe.py` | Active XCTest capture broker preserves stage order, session identity, explicit-display PNG receipts and original encoded video. New sessions require a fresh directory and retain source/lock hashes; active native probe sources stay in `scripts/DeviceHubUITests/`. |
| `self_test.py` | Refactor → `scripts/python/devduo_tools/self_test.py` and `tests/python/` | Replace opaque assertion-only checks with portable unittest coverage of HTTP persistence, malformed inputs, image integrity/diffs, configuration, managed CLI boundaries and evidence provenance. |
| `open_safari.py` | Merge → `capture_macos.py:open_safari` | Preserve labeled simulator navigation without maintaining a separate three-line wrapper. Keep argument boundaries, existing query values and URL fragments. |
| `extract_geometry.py` | Archive → `scripts/archive/2026-10-21/extract_geometry.py` | Original PDF identifiers, exact path marker, canvas transforms and shell offsets are specific to the original Device Hub artwork. Preserve the derivation rather than advertise a generic extractor. |
| `extract_safari_scenes.py` | Archive → `scripts/archive/2026-10-21/extract_safari_scenes.py` | Fixed capture date/layout, 3× framebuffer scale, address fallback and clockwise Closed 180° history describe the canonical cohort. Preserve native/image hashes and verified one-tab selection. |
| `summarize_safari_session.py` | Archive → `scripts/archive/2026-10-21/summarize_safari_session.py` | Session/receipt/hash association is useful provenance, but its fixed 12-pose matrix is the historical collection plan, not generic future acceptance criteria. |
| `verify_desktop_scenes.py` | Archive → `scripts/archive/2026-10-21/verify_desktop_scenes.py` | Independent native/image hash and actual viewport/widget checks remain strong evidence. Fixed 16/24 states and screenshot names bind it to this cohort. |
| `verify_tab_strip.py` | Archive → `scripts/archive/2026-10-21/verify_tab_strip.py` | Preserve whole-tab/close-button native geometry checks. Four fixed body angles, 32-point title-container filtering and 3-point exposure gap are cohort assumptions. |
| `scene_contact_sheet.py` | Archive → `scripts/archive/2026-10-21/scene_contact_sheet.py`; replace generic capability with `contact_sheet.py` in the maintained package | Historic scene/state layout and dark-pixel/top-420-pixel cropping are presentation heuristics. The maintained command accepts explicit image pairs/labels without guessing crop boundaries. |
| `inspect_video.swift` | Retain → `scripts/swift/Sources/InspectVideo/main.swift` | AVFoundation actual-time video decoding is reusable. Package as `devduo-inspect-video`, record source/frame hashes, require a new output directory and report concise failures. |
| `viewport_insets_probe.swift` | Archive → `scripts/archive/2026-10-21/viewport_insets_probe.swift` | Fixed 466 × 678 scene and calibrated inset cases form a one-off macOS 27 capability experiment. Preserve the manual rebuild recipe and snapshot/presented-surface limitations. |
| `build-macos.sh` | Remove | Four-line zsh forwarding wrapper duplicates the canonical `just build` recipe and adds no build behavior. |
| `package-macos.sh` | Remove | Four-line zsh forwarding wrapper duplicates `just package-macos` and Node artifact packaging; it adds no packaging behavior. |

## Maintained entry points

Use the locked Python ecosystem directly:

```sh
mise exec -- uv run --locked devduo-tools --help
mise exec -- uv run --locked devduo-tools COMMAND --help
```

| Command | Purpose / prerequisites |
| --- | --- |
| `serve` | Loopback calibration page and JSON receiver. |
| `capture-macos`, `open-safari`, `safari-probe` | macOS/Xcode simulator collection with explicit device identifiers; pose labels do not prove physical state. |
| `png-compare`, `sample-appearance` | Explicit image-pixel comparison or region statistics with locked Pillow. |
| `contact-sheet` | Repeated `--pair REFERENCE CANDIDATE`, optional matching `--label`, and `--output NEW.png`; presentation only. |
| `validate-mask` | Original PDF, framebuffer, explicit scale and optional external Poppler. |
| `validate-video` | Original video plus inspector metadata/frames; expected source hash is mandatory. |
| `check-repository`, `self-test` | Read-only worktree publication audit and portable Python tests. |

Python supports a compatible range (`>=3.12`); mise selects the development line and uv never downloads a second interpreter. Pillow is a locked runtime dependency and Ruff a development dependency. See the [Python package guide](../../scripts/python/README.md) for arguments and formatter checks.

The independent Swift package requires macOS 13+ and the Xcode Swift toolchain:

```sh
swift run --package-path scripts/swift --scratch-path temp/build/swift-diagnostics devduo-inspect-video captures/transition.mov temp/new-video-frames
mise exec -- uv run --locked devduo-tools validate-video temp/new-video-frames
```

See the [Swift package guide](../../scripts/swift/README.md). Encoded nominal frame rate does not prove compositor timing or physical refresh rate.

## Historical reproduction boundary

Archives are not routine maintained commands or fresh-checkout tests. They require the original separately retained private evidence and fail when required inputs are unavailable. The [archive guide](../../scripts/archive/README.md) specifies every cohort path, assumption, argument and inset-experiment rebuild recipe. For example:

```sh
mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_safari_scenes.py --root LOCAL_EVIDENCE_ROOT --output temp/reproduced-scenes.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py LOCAL_DESKTOP_DIRECTORY --root LOCAL_EVIDENCE_ROOT --include-single --output temp/historical-verification.json
```

Use fresh outputs outside the source evidence and compare regenerated configuration before replacement. During organization, local reproduction produced semantically identical device/Safari configuration; the preserved geometry verifiers passed all 24 desktop states and four selected-tab angles. These results reuse historical captures and do not establish new platform/runtime or optical validation.

The maintained release entry points are `scripts/release.mts` (version/ref policy, manifests and publication verification) and `scripts/package-release.mts` (native self-contained bundles, signing and archives). Workflow composition stays in just and GitHub Actions; see [Releases](013-RELEASE.md).

[English](012-SCRIPT-INVENTORY.md) | [中文](../zh/012-SCRIPT-INVENTORY.md)
