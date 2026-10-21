# Reference data and publication boundary

DevDuo runs from its checked-in configuration. A fresh checkout does not need Xcode, Device Hub, raw Safari sessions or original Apple PDFs unless a contributor is collecting or regenerating references.

| File | Meaning |
| --- | --- |
| `config/device-profiles.json` | Independent inner screen paths, exterior proportions and canvas-to-screen mapping derived from local simulator artwork |
| `config/safari-scenes.json` | Eight measured Closed/Open body mappings; initial/scrolled multi-tab references and eight additional Open single-tab states |
| `config/safari-shell.json` | Editable widget/material defaults, with estimates and interaction bounds distinguished |
| `config/physical-parameters.json` | Unknown physical dimensions and hardware radii, deliberately null |
| `config/measurement-matrix.json` | Intended collection plan; presence in this plan is not proof of completed coverage |

The current scene cohort was collected using Xcode 27.1 and the iOS 27.1 iPhone Duo simulator. It is an observed simulator baseline and may differ across runtimes, Safari versions, tab counts and rotation histories. The measured screen is 466 × 678 in the Closed portrait scene and 669 × 951 in the Open portrait scene; these are logical coordinates rather than hardware dimensions. The full body correspondence is in [Safari scenes](005-SAFARI-SCENES.md).

Public scene records retain source-relative evidence names and SHA-256 hashes to identify their original provenance. The corresponding raw files are intentionally absent from Git. A hash identifies a file and does not make it publicly available or prove a physical hardware specification. Some opaque browser record identifiers remain as provenance references; no simulator UDID, workstation name or personal absolute path is required by these configs.

Local `captures/`, `measurements/`, `temp/` and `.local/` are ignored in full. They may contain original screenshots, videos, native accessibility trees with browsing titles, URLs, simulator identifiers, tool output paths, xcresult bundles and private session notes. Keep original bytes and hashes intact. Produce separately reviewed, sanitized derivatives for any public issue or future evidence transfer; never rewrite originals in place merely to make them publishable.

`mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_geometry.py --source SOURCE_DIRECTORY --output temp/device-profiles.json` regenerates the device config from a local artwork extraction. `mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_safari_scenes.py --root EVIDENCE_ROOT --output OUTPUT_JSON` regenerates the current scene cohort from the original local directory layout. These archived, cohort-specific tools require explicit outputs and explain missing private inputs. See the [archive reproduction notes](../../scripts/archive/README.md). Run extraction into `temp/` first and compare the result before replacing public configuration. The capture runner is reusable for new sessions; the current scene extractor describes this particular cohort, not an automatic classifier for arbitrary future captures.

The full desktop/selected-tab comparison verifiers require original native source records in addition to desktop state files. Their absence in a public clone is an evidence limitation, not a failed app dependency. Portable self-checks and native builds use only public files.

See [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md) for simulator-artwork provenance and distribution status.

[English](007-REFERENCE-DATA.md) | [中文](../zh/007-REFERENCE-DATA.md)
