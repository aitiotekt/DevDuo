# Privacy

DevDuo's calibration receiver and diagnostics write local files. No separate analytics or hosted telemetry collector is implemented. Browsing a developer-selected remote URL still connects to that website using the system browser engine; the tool does not make remote browsing anonymous.

The measurement page records viewport/screen metrics, safe areas, selected labels, environment information and its URL. The receiver adds timestamps, an opaque record ID and the connection source address. Desktop observation includes scroll/viewport information, page title, hostname and page-derived colors. Native capture workflows can also record simulator identifiers, browsing titles/URLs, screenshots, videos, accessibility trees, tool output paths and source hashes.

Raw records belong in ignored `captures/`, `measurements/` or `.local/`. Generated diagnostic output under `temp/` can also contain private metadata. The Windows WebView2 profile and installed-app diagnostics use the current user's local application-data directories. `DEVDUO_OUTPUT_DIR` can redirect desktop state output. Deleting evidence or a browser profile is a deliberate local user operation; DevDuo does not implement an automated retention policy.

Public configuration retains selected simulator measurements, source-relative file references and evidence hashes. It omits the original raw sessions. Before sharing a report, review both metadata and images, and publish a sanitized derivative without changing original evidence bytes. Never publish secrets, personal host names, simulator identifiers, private page contents or user-directory paths.

See [Reference data](docs/en/007-REFERENCE-DATA.md) and [Security](SECURITY.md).

[English](PRIVACY.md) | [中文](docs/zh/PRIVACY.md)
