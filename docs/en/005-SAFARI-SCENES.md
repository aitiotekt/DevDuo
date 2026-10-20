# Safari scene matrix

`config/safari-scenes.json` records actual Device Hub correspondence. **Body +90°** selects a measured body pose, rather than mechanically rotating the previous screen. Body angle, scene angle, actual DOM viewport, rendered DIP and unscaled component frames are recorded separately.

## Body-to-scene mapping

| Body angle | Closed scene | Sidebar | Open scene |
| --- | --- | --- | --- |
| 0° | 466 × 678 | right | 951 × 669 |
| 90° | 678 × 466 | right; camera below tabs | 669 × 951 |
| 180° | 678 × 466, retained preceding landscape | right | 951 × 669 |
| 270° | 678 × 466 | left; camera above navigation | 669 × 951 |

Closed 180° is the observed clockwise-sequence result, not proof of upside-down portrait support or every rotation history. Open mask rotation signs represent a symmetric outline, not extra hardware measurements. Partial-fold geometry remains unavailable.

## Viewport reference

All values are logical/CSS coordinates, not physical dimensions. The tab-strip switch applies only to Open Safari; immersive uses the complete scene and Closed has its own side widgets.

| Screen-coordinate scene | Tab variant | Initial viewport | Scrolled viewport |
| --- | --- | --- | --- |
| Closed 466 × 678 | side widgets | 382 × 580 | 382 × 598 |
| Closed 678 × 466 | side widgets | 594 × 368 | 594 × 386 |
| Open 951 × 669 | multiple | 951 × 543 | 951 × 618 |
| Open 669 × 951 | multiple | 669 × 825 | 669 × 900 |
| Open 951 × 669 | single | 951 × 589 | 951 × 618 |
| Open 669 × 951 | single | 669 × 871 | 669 × 900 |

Closed reserves side 84 and bottom 98/80. Open top reservation is 126 with multiple tabs, 80 with one tab and 51 when compact; the extra tab row consumes 46. Single-tab source Safari reports safe-area top 2 initially / 31 compact and bottom 34. These stay reference observations and are not injected into desktop CSS.

## Tab geometry and provenance

All titles use one 14-point medium scene font with vertically centered lines and truncation; Windows uses consistent pixel-unit font/alignment. Generic titles/favicons avoid copying private browsing content. Slot count/width reflects each capture's tab count, not a universal device-angle rule; this shell is not a tab manager.

The selected `TabBarTab` hit rectangle excludes its 32-point close button. Preserve that hit frame, but paint the whole 32-point-high `TabBarItemTitleContainer`, validating its union with the close frame. At body 0°, selected width is 226.67 rather than the hit width 194.67; the other selected widths are 124.33, 150 and 100. One gray track, separators and the complete selected surface share the same geometry; paint/text clips at the next whole-tab origin while original overlapping AX frames remain source data. Colors, stroke softness and platform glass remain approximate.

The original single-tab sources live under `open-single-ORIENTATION/verified/`; failed idle-wait attempts remain separate. The canonical Open cohort used explicit display 3, because the default XCTest image could target another display. Source hashes and original native frames support the 24-state and four-angle whole-tab checks. Reproduction inputs and historical verifiers are documented once in the [archive guide](../../scripts/archive/README.md).

Follow [Calibration and validation](003-CALIBRATION-WORKFLOW.md) for new capture/acceptance steps. [Architecture](001-ARCHITECTURE.md) explains actual Closed under-chrome flow versus Open/Windows reserved-frame approximations; the [measurement contract](002-MEASUREMENT-CONTRACT.md) defines units and unknown hardware values.

[English](005-SAFARI-SCENES.md) | [中文](../zh/005-SAFARI-SCENES.md)
