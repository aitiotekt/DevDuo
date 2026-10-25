# Application localization

DevDuo provides English and Simplified Chinese for the desktop application. The **Language** control offers **System default**, **English**, and **简体中文**. A language change updates the host controls, status messages, native shell labels, offline welcome page and color laboratory in place. It does not reload or replace the loaded WebView.

## Selection and persistence

The initial choice is System default. At application launch, DevDuo resolves the operating system's UI culture:

| System UI culture | Application language |
| --- | --- |
| Chinese, including `zh`, `zh-CN`, `zh-SG`, and `zh-Hans` cultures | Simplified Chinese |
| A culture containing `Hant`, or `zh-TW`, `zh-HK`, or `zh-MO` | English |
| Other cultures, including an invariant culture | English |

An explicit English or Simplified Chinese choice overrides the system culture. System default is resolved from the UI culture captured at launch; restart the application after changing the operating system's UI language.

Preferences are stored in the current user's .NET `LocalApplicationData` directory under `DevDuo/preferences.json`. This is application data, separate from checkout configuration and browser profiles. `Host/PreferenceStore.cs` owns the file. The JSON contains `schemaVersion: 1`, a `language` value of `system`, `en`, or `zh-Hans`, and an optional `host` object for host settings described in [Architecture](001-ARCHITECTURE.md). Writes use a temporary file and replacement; a language write keeps the host settings and a host write keeps the language. The reader accepts only a JSON object with integer `schemaVersion: 1` and a string `language`; extra fields are allowed, so earlier language-only files remain valid. Non-object roots, missing or unsupported schema versions, and missing or non-string language fields fall back to System default with a localized explanation. An unknown language string also resolves to System default. Missing preferences use System default; unreadable or corrupt preferences also fall back and report an explanation. Invalid individual `host` fields fall back to their defaults and are named in a message without affecting the language. If saving fails, the selected language still applies for the current session. Preference messages appear below **Reset settings** in the settings sidebar.

Developer tools starts collapsed at the top of the settings sidebar for each new window. Its expansion is session state: changing language updates its labels in place without closing it. Old `host.developerExpanded` values are ignored without invalidating the preference file; new writes omit the field. Material and diagnostics expansion remain persisted host settings.

## Resources and changes

The embedded flat JSON catalogs are `src/DevDuo/Localization/en.json` and `src/DevDuo/Localization/zh-Hans.json`. `Host/AppLocalization.cs` resolves language, formats display text and manages preference storage. English is the fallback catalog; an unknown catalog uses English, and a key absent from both selected and English catalogs displays the key itself.

When changing application text:

1. Add or edit the same key in both catalogs. Keep every translation nonempty.
2. Preserve numbered format placeholders and their format specifiers, for example `{0}` and `{3:F0}`. Format user-facing messages through `AppLocalization.Format` so the selected culture applies.
3. Host components refresh their text in `Localize()` (`HostAddressBar`, `HostSettingsPanel`), called from `MainWindow.RefreshLanguage`. Preserve selected values while refreshing labels, and give each new control a localized automation name; shortcut tooltips use `Shortcut.Format`.
4. Native shell labels use the `Shell.*` keys, passed separately as `uiLanguage` and `uiStrings` presentation options. Keep the Swift and Windows key references synchronized. Actual developer page titles and hostnames retain their original text.
5. The app-owned color laboratory uses `Fixture.*` keys and `data-i18n` attributes in `WebAssets/Pages/color-scroll.html`. Its initial payload and subsequent updates also set the HTML language. In-place translation is gated by the fixture marker. It preserves the chosen theme hint.

Application-owned validation, loading, saving and initialization explanations are translated. Exception details supplied by the operating system or browser remain in their original language inside the translated context. Windows WebKit initialization and composition-capability errors support language changes even after initialization failed. Native composed widgets retain localized names and the `Accessibility.Press` action.

## Measurement boundaries

Application UI localization is separate from measured scene data. The language control changes presentation strings and reconfigures the existing native container; it does not change profile IDs, body rotations, scene mapping, masks, logical dimensions, viewport reservations, tab frames, clipping or scene font size. The eight body/fold mappings and 24 Safari initial/scrolled reference states remain the same.

Known telemetry values are translated for the host status display only. Saved JSON and page telemetry retain their canonical keys, identifiers, engine names and source values. The additional `uiLanguage` and `uiStrings` fields are presentation metadata within the serialized container options, also visible under `materialParameters`. Arbitrary developer pages and the calibration page are not translated by the application's language selector. No iPhone browser APIs or device properties are fabricated.

The active simulated tab shows the current document's actual title, without translation; only the generic fallback and inactive illustrative label are localized. Native tail truncation uses the existing measured text slot and font. A language change can alter fallback text length but must not alter measured geometry. The app-owned welcome page and color laboratory can reflow their translated prose; they are separate from the canonical calibration page and reference scenes.

## Verification and limits

Select the affected shared-state and localization checks:

```sh
mise exec -- just test-core
mise exec -- just test-localization
```

`tests/DevDuo.Localization.Tests` links the actual localization, profile, scene and options sources. It checks catalog key and placeholder parity, nonempty text, formatting, fallback, explicit/system language resolution, language preference restart/corruption/write-failure behavior, page resource keys, welcome-page translation and unchanged measured option values across locale payloads. `tests/DevDuo.Core.Tests` separately covers host-preference defaults/round trips/field validation, calibration labels, URL normalization, navigation payloads, all eight mappings and 24 states, coordinate transforms, title state and scroll policies. Both use the repository-managed .NET SDK, committed locks and no external test runner. `just test-debugging` checks real loopback CDP service boundaries; `just test-windows` runs the separate native app scenarios and opens Windows UI.

Compilation and portable tests cannot establish native runtime correctness. On macOS, separately verify language changes while a page is loaded and scrolled, native accessibility labels, selected mode/profile preservation, the color laboratory, save/error messages and preference restoration after restart. Inspect viewport and native-frame telemetry before and after switching languages. On Windows, select the equivalent affected runtime checks on one current actual DPI; add input/DPI coverage only when those paths change. A Windows cross-build alone does not establish those results. See [Platform parity and macOS acceptance](015-PLATFORM-PARITY.md), [Windows handoff](009-WINDOWS-HANDOFF.md) and [validation](003-CALIBRATION-WORKFLOW.md).

For isolated native acceptance runs, set `DEVDUO_PREFERENCES_PATH` before process startup to a dedicated preference file. The Windows harness uses an OS temporary path so a browser/native crash cannot overwrite the user's real settings. This does not change the preference schema or runtime language behavior.

[English](011-LOCALIZATION.md) | [中文](../zh/011-LOCALIZATION.md)
