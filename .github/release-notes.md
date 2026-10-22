Experimental DevDuo desktop viewport workbench. See the bilingual [documentation](https://dev-duo.aitiotekt.com/) and [changelog](https://dev-duo.aitiotekt.com/changelog).

- **macOS arm64:** macOS 27 or later. Includes .NET; Developer ID signed, notarized and stapled.
- **Windows x64:** includes .NET and the metadata-pinned Windows WebKit runtime under `DevDuo/webkit/`; no separate engine installation is required. Keep the complete extracted directory together. The native libraries require the Microsoft Visual C++ x64 runtime. See the [Windows guide](https://dev-duo.aitiotekt.com/docs/014-WINDOWS-WEBKIT#avalonia-backend). No Authenticode signature. Broader native input, DPI and compatibility acceptance remains pending.
- Every download is listed in `release-manifest.json` and `SHA256SUMS`, with GitHub build provenance attestations. Provenance is not Windows Verified Publisher trust.

This workbench approximates simulator viewports using desktop browser engines. It is not an iOS engine or a physical-device certification. Simulator-derived geometry has separate provenance and redistribution considerations; see the bundled third-party notices before redistribution.
