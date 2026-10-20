# Maintained Python utilities

The installable `devduo_tools` package owns calibration capture, image inspection, public-file auditing and portable self-checks. Run from a source checkout with mise's Python and the locked uv environment:

```sh
mise exec -- uv sync --locked --all-groups
mise exec -- uv run --locked devduo-tools --help
mise exec -- uv run --locked devduo-tools self-test
mise exec -- uv run --locked ruff check scripts/python tests/python
mise exec -- uv run --locked ruff format --check scripts/python tests/python
```

`pyproject.toml` supports Python 3.12 and newer; the mise configuration selects the development interpreter line. uv is configured never to download a second Python interpreter. Pillow is a locked runtime dependency for PNG decoding and presentation sheets; Ruff is a development dependency.

Use `COMMAND --help` for arguments. For example:

```sh
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output temp/comparison
mise exec -- uv run --locked devduo-tools sample-appearance captures/reference.png --region address:20,20,100,40 --output temp/appearance
mise exec -- uv run --locked devduo-tools contact-sheet --pair captures/reference.png captures/candidate.png --label 'Observed pair' --output temp/contact-sheet.png
```

Comparison uses equal-size decoded RGBA pixels and optional explicit crops/exclusions. No implicit registration, resizing, ICC color management, physical conversion or material inference occurs. Higher-depth PNGs require explicit conversion. Contact sheets resize only for presentation and accept explicit image pairs, without guessing crop boundaries. Source files are not rewritten.

`serve` binds only to loopback and serves the fixed calibration page, JavaScript and stylesheet. `capture-macos`, `open-safari` and `safari-probe` require macOS/Xcode and an explicitly supplied simulator. The XCUITest project is `scripts/DeviceHubUITests`. Probe output must be a new directory; new sessions record tool source/lock hashes and copy fresh source snapshots without changing old captures. `validate-mask` additionally requires Poppler's `pdftoppm`, an independently verified scale and the original local PDF. `validate-video` requires an original encoded video, decoded frames and the expected source SHA-256 from the [Swift inspector](../swift/README.md); legacy metadata requires `--source-sha256`.

The receiver also serves `web/document-end.html` at `/document-end.html`: a 2400-CSS-pixel striped fixture with full-width/narrow/absent fixed-footer controls, live DOM measurements and **Save observation**. Use it to compare presented windows at document end without modifying layout or scroll extents.

## Safari canvas reference library

`/safari-reference.html?tab=A&palette=auto` is the controlled gallery fixture. A second page uses `tab=B`. `palette=light|dark` changes the page background independently of simulator Appearance; optional `themeColor=%23101923` sets an opposing meta hint. Records retain the actual system color scheme, page background, meta hint, DOM/visual viewport and document position. No Safari chrome is drawn in the page.

Use Device Hub to select fold, body rotation, appearance and one/two tabs. Use a fresh local session, preserve original window captures and verify the actual canvas after every transition. Native swipes and component frames are available through the existing XCUITest target:

```sh
mise exec -- uv run --locked devduo-tools reference-session --udid SIMULATOR_UDID --output captures/safari/reference-NEW/native
```

Enter one `ACTION ID` per line, for example `up closed-b000-light-expanded`, `down closed-b000-light-compact`, `observe inspect-current` and finally `stop complete`. Actions are restricted to `up`, `down`, `observe`, `stop`; IDs are safe local names and cannot be reused. Up means scrolling toward the document top (a native finger swipe down). The broker is loopback-only with a random capability path. It activates the existing Safari document and saves each native receipt before acknowledging it; it does not select poses, navigate, close tabs or capture the desktop. Use native upward gestures until the actual browser record confirms scrollY zero, and record reverse scrolling at nonzero Y separately. `scrollTo` alone did not compact the observed Open Safari.

The initial reference cohort keeps original PNGs, per-case JSON and failed attempts locally. For export, `cases.json` contains 48 indexed observations with `id`, `fold`, `bodyAngleClockwise`, `appearance` (actual simulator Light/Dark, matching the observed color scheme), `tabCount`, `chrome`, `sourceImage`, `sourceSha256`, `fixtureSourceSha256`, `capturedAt`, `browser`, `native`, `nativeSession`, `nativeId` and `orientationSource`. Native session/ID point to the preserved receipt; orientationSource is the local devicectl JSON filename. Record observed content layout and scene-to-canvas rotation explicitly; the exporter must not infer them from a commanded body angle.

`publication-review.json` supplies `reviewedForPublication: true` only after visual/privacy review, plus `cohort`, `themeAxis`, `fixtureSources`, `limits`, `triggers`, a per-ID `crops` map (`[left, top, right, bottom]` in original window pixels) and `layouts` map (`contentLayout`, `nativeSceneToCanvasClockwise`). Optional `conditions` list reviewed color-comparison sources with explicit crops/hashes and palette/system/meta observations. All inputs remain local; only selected numeric geometry and controlled colors enter the public manifest. Export refuses incomplete/duplicate combinations, path traversal, mismatched native receipts and changed source bytes, and requires a new output directory:

```sh
mise exec -- uv run --locked devduo-tools reference-library export --session captures/safari/reference-NEW --output temp/reference-library-NEW
mise exec -- uv run --locked devduo-tools reference-library verify temp/reference-library-NEW
mise exec -- just references-check
```

The last command checks the checked-in library. Repository audit also validates the exact image allowlist, hashes, dimensions and metadata. It rejects other binaries; it does not globally allow screenshots. Canvas crops are WebP quality 88 without resizing; overview sheets are explicitly resized derivatives. EXIF/XMP/ICC metadata is omitted. Public-only checks do not claim the absent private sources or native gestures were revalidated. See the [scene gallery](../../docs/en/005-SAFARI-SCENES.md) and [third-party notices](../../THIRD_PARTY_NOTICES.md).

The [archive](../archive/README.md) preserves original cohort extraction and independent geometry verifiers. Those fixed-layout programs are not maintained generic commands and are not needed to run the application from public configuration.
