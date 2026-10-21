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

The [archive](../archive/README.md) preserves original cohort extraction and independent geometry verifiers. Those fixed-layout programs are not maintained generic commands and are not needed to run the application from public configuration.
