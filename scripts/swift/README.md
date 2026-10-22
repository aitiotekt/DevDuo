# macOS video inspector

This dependency-free Swift package decodes actual video frames with AVFoundation, records requested and actual frame times, original source SHA-256, per-frame hashes, track size and encoded nominal frame rate. It requires macOS 13 or newer and the Xcode Swift toolchain; it does not use simulator UI or infer compositor/physical refresh rate.

```sh
swift build --package-path scripts/swift --scratch-path temp/build/swift-diagnostics
swift run --package-path scripts/swift --scratch-path temp/build/swift-diagnostics devduo-inspect-video captures/transition.mov temp/new-video-frames
mise exec -- uv run --locked devduo-tools validate-video temp/new-video-frames
```

The output directory must be new. Encoded source bytes remain untouched. The Python validator requires the original video, records source and frame hashes, checks frame dimensions and rejects black/transparent or unchanged first/last frames. Repeated actual decode times are retained honestly; successful inspection does not prove compositor timing, optical constants or hardware dimensions.

The one-off WKWebView inset capability experiment is retained in the [historical archive](../archive/README.md), with its original cohort assumptions and manual rebuild recipe.
