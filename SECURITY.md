# Security

DevDuo is an experimental local development tool. No security support period or production hardening guarantee is established for the current development version.

The measurement receiver is an unauthenticated HTTP development service. Keep its default loopback binding. Explicitly binding a reachable interface exposes its record endpoint to that network; do not use it as a public service or send secrets to it. It accepts measurement JSON, not remote shell commands. Native desktop browsers navigate user-selected content and retain their actual engine behavior.

Keep SDKs and browsers updated through their normal channels. Windows WebKit is source-pinned: maintainers must deliberately update the revision, patch and hashes, rebuild, and repeat native acceptance for security updates. Dependencies are resolved through committed lock files. Build checks and the public-file audit do not establish that an arbitrary page or third-party runtime is safe.

For a sensitive report, use the repository's private vulnerability reporting facility if enabled. If it is unavailable, open an issue asking for a private contact without including exploit details, credentials or private browsing records. Public non-sensitive bugs should include generic platform/runtime metadata and reproducible steps, with simulator identifiers and personal paths removed.

See [Privacy](PRIVACY.md), [Contributing](CONTRIBUTING.md) and [Third-party notices](THIRD_PARTY_NOTICES.md).

Remote inspection is explicit opt-in and starts disabled. Page CDP permits JavaScript evaluation and website data access. Its fixed loopback endpoint allows unauthenticated local clients when the optional bearer token is empty. Configure a token to require an Authorization bearer header for discovery and page sockets; LAN CDP requires a token. Static bundled frontend assets are public and contain no page data. The built-in tools window uses a separate per-start same-origin socket capability, which cannot authorize discovery. Host and browser WebSocket-origin checks remain active even without a token. There is no TLS or multi-user authorization layer; LAN access makes the service reachable to that network. Windows native remote Inspector is a separate unauthenticated WebKit TCP protocol; the CDP token does not protect it. macOS native inspection follows Safari's system discovery rules. Stop listeners when finished and keep these development endpoints off public networks. A proxy endpoint is accepted without credentials; DevDuo does not implement proxy authentication or disable the engine's TLS checks.

The REST control API is independently opt-in and loopback-only, even when debugging LAN access is selected. Its optional bearer token protects host state and mutations; public Swagger UI/schema/assets contain only the API contract. Host/origin checks and JSON-only mutation requests prevent cross-origin browser control. This service can navigate pages, change host settings and expose page diagnostics and native screenshots to authorized local clients. It has no remote shell or filesystem endpoint. See the [usage guide](docs/en/004-USAGE.md#rest-control-api) for configuration and scope.

[English](SECURITY.md) | [中文](docs/zh/SECURITY.md)
