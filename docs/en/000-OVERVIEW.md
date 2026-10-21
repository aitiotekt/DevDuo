# Overview

DevDuo lets web developers interact with a real desktop WebView inside simulator-derived iPhone Duo screen geometry. Safari mode reproduces measured widget placement and viewport changes; immersive mode removes browser chrome and uses the whole logical scene.

The current reference inventory is Closed/Open × four body angles, with initial/scrolled Safari states and Open one-tab/multiple-tab variants. These are observed simulator states, not guaranteed physical-device dimensions or a complete browser implementation. A partially open hinge/perspective model is unavailable.

Start with the [README](../../README.md), then follow [Development](010-DEVELOPMENT.md), [Architecture](001-ARCHITECTURE.md) and the [Measurement contract](002-MEASUREMENT-CONTRACT.md). Collect new observations using [Calibration workflow](003-CALIBRATION-WORKFLOW.md); distinguish current checks from pending work in [Validation](008-VALIDATION.md).

The public checkout runs without raw evidence. Reference regeneration and full screenshot/source-frame verification require separately supplied local evidence. See [Reference data](007-REFERENCE-DATA.md), [Windows handoff](009-WINDOWS-HANDOFF.md) and the [Host UI roadmap](100-ROADMAP.md).

[English](000-OVERVIEW.md) | [中文](../zh/000-OVERVIEW.md)
