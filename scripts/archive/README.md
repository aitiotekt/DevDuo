# Historical calibration scripts

These programs preserve how the 2026-10-21 Device Hub cohort was derived and checked. They are historical reproduction tools, not maintained generic capture workflows or fresh-checkout acceptance tests. The active tools are the `devduo-tools` Python package and the Swift video inspector under `scripts/swift/`.

[`index.json`](index.json) records every original script, its original SHA-256, review decision and current destination. Raw captures, browser records, PDF extracts, and source snapshots stored with evidence remain private and unchanged. The original source hashes in the index describe the pre-organization scripts; archived files have only path/import, explicit-output and missing-input handling adaptations. Original evidence hashes in checked-in configuration continue to identify the independent captured files, not these reorganized script files.

Run from the repository root with the locked project environment:

```sh
mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_geometry.py --source LOCAL_ARTWORK_DIRECTORY --output temp/reproduced-device-profiles.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_safari_scenes.py --root LOCAL_EVIDENCE_ROOT --output temp/reproduced-safari-scenes.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/summarize_safari_session.py --captures LOCAL_CAPTURE_DIRECTORY --records LOCAL_RECORDS_JSONL --output temp/reproduced-session
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py LOCAL_DESKTOP_DIRECTORY --root LOCAL_EVIDENCE_ROOT --include-single --output temp/historical-desktop-verification.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_tab_strip.py LOCAL_SELECTED_TAB_DIRECTORY --root LOCAL_EVIDENCE_ROOT --output temp/historical-tab-verification.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/scene_contact_sheet.py LOCAL_DESKTOP_DIRECTORY --root LOCAL_EVIDENCE_ROOT --output temp/historical-contact-sheets
```

Use a new output path outside the source evidence. Review reproduced JSON before replacing any public configuration. Missing local inputs are an error; no placeholder data or passing result is generated for absent evidence.

| Script | Cohort assumptions and required local evidence |
| --- | --- |
| `extract_geometry.py` | `mask-paths.json` contains the original two PDF identifiers; `phone15-composite-path.txt` contains the exact outer-path marker. Canvas flips, screen offsets and 34-unit outline belong to that artwork. No physical units are inferred. |
| `extract_safari_scenes.py` | `measurements/session-2026-10-21/selected-browser-records.jsonl`; `captures/safari/2026-10-21/{closed,open}-ORIENTATION/` native components, selected-display receipts and images; optional `measurements/session-local/records.jsonl` plus `open-single-ORIENTATION/verified/`. Fixed 3x framebuffer scale, address width fallback and clockwise Closed 180° history are specific observations. |
| `summarize_safari_session.py` | Original `session.json`, `capture-milestones.jsonl`, stage PNGs/native components and browser POST JSONL. Verifies session identity, receipt hashes, nonblack pixels and pose/time association. Its fixed 12-pose matrix is the original capture plan; partial poses may remain unavailable. |
| `verify_desktop_scenes.py` | `config/safari-scenes.json`, every referenced original native/image file and desktop `SCENE-STATE.state.json`/`.jpg`. Rechecks independent source hashes and actual viewport/widget measurements for 16 or 24 states. This is geometry/provenance acceptance, not pixel/optical equivalence. |
| `verify_tab_strip.py` | Four `body-ANGLE.state.json` records plus their independently captured `TabBarItemTitleContainer` native source JSON. Its 32-point container filter and 3-point exposure gap are cohort-specific. |
| `scene_contact_sheet.py` | Same full canonical images and desktop screenshot matrix. Its automatic dark-pixel threshold and top-420-pixel exclusion are historic screenshot-crop heuristics. Resizing/cropping is presentation only; use the maintained explicit-pair `contact-sheet` for new material. |
| `viewport_insets_probe.swift` | macOS 27 SDK/runtime and a new output directory. Fixed 466 × 678 scene, 98/80/84 and 126/51 inset cases probe public WK behavior; they are not generic Safari or hardware measurements. |

The inset experiment can be rebuilt manually on macOS 27:

```sh
xcrun swift -module-cache-path temp/cache/swift scripts/archive/2026-10-21/viewport_insets_probe.swift temp/new-inset-experiment
```

It opens and closes its own diagnostic window. `HOLD_PROBE=1` retains the Closed bottom/right case for manual window inspection. WK snapshot PNGs do not establish the actual presented under-inset surface; the historical conclusion also required original native window screenshots. No new interactive validation is implied by retaining this source.
