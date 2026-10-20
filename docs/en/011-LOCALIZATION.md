# Application localization

DevDuo provides English and Simplified Chinese for the desktop application. The **Language** control offers **System default**, **English**, and **简体中文**. A language change updates the host controls, status messages, native shell labels and the app's color laboratory in place. It does not reload or replace the loaded WebView.

## Selection and persistence

The initial choice is System default. At application launch, DevDuo resolves the operating system's UI culture:

| System UI culture | Application language |
| --- | --- |
| Chinese, including `zh`, `zh-CN`, `zh-SG`, and `zh-Hans` cultures | Simplified Chinese |
| A culture containing `Hant`, or `zh-TW`, `zh-HK`, or `zh-MO` | English |
| Other cultures, including an invariant culture | English |

An explicit English or Simplified Chinese choice overrides the system culture. System default is resolved from the UI culture captured at launch; restart the application after changing the operating system's UI language.

Preferences are stored in the current user's .NET `LocalApplicationData` directory under `DevDuo/preferences.json`. This is application data, separate from checkout configuration and browser profiles. The JSON contains `schemaVersion: 1` and a `language` value of `system`, `en`, or `zh-Hans`. Writes use a temporary file and replacement. The reader accepts only a JSON object with integer `schemaVersion: 1` and a string `language`; extra fields are allowed. Non-object roots, missing or unsupported schema versions, and missing or non-string language fields fall back to System default with a localized explanation. An unknown language string also resolves to System default. Missing preferences use System default; unreadable or corrupt preferences also fall back and report an explanation. If saving fails, the selected language still applies for the current session. Preference failures appear in the material settings message area.

## Resources and changes

The embedded flat JSON catalogs are `src/DevDuo/Localization/en.json` and `src/DevDuo/Localization/zh-Hans.json`. `AppLocalization.cs` resolves language, formats display text and manages preference storage. English is the fallback catalog; an unknown catalog uses English, and a key absent from both selected and English catalogs displays the key itself.

When changing application text:

1. Add or edit the same key in both catalogs. Keep every translation nonempty.
2. Preserve numbered format placeholders and their format specifiers, for example `{0}` and `{3:F0}`. Format user-facing messages through `AppLocalization.Format` so the selected culture applies.
3. For host controls, register the update with `MainWindow`'s localization refresh. Preserve selected values while refreshing labels.
4. Native shell labels use the `Shell.*` keys, passed separately as `uiLanguage` and `uiStrings` presentation options. Keep the Swift and Windows key references synchronized. Actual developer page titles and hostnames retain their original text.
5. The app-owned color laboratory uses `Fixture.*` keys and `data-i18n` attributes in `Fixtures/color-scroll.html`. Its initial payload and subsequent updates also set the HTML language. In-place translation is gated by the fixture marker. It preserves the chosen theme hint.

Application-owned validation, loading, saving and initialization explanations are translated. Exception details supplied by the operating system or browser remain in their original language inside the translated context. The Windows WebView2 initialization explanation supports language changes even when browser initialization failed.

## Measurement boundaries

Application UI localization is separate from measured scene data. The language control changes presentation strings and reconfigures the existing native container; it does not change profile IDs, body rotations, scene mapping, masks, logical dimensions, viewport reservations, tab frames, clipping or scene font size. The eight body/fold mappings and 24 Safari initial/scrolled reference states remain the same.

Known telemetry values are translated for the host status display only. Saved JSON and page telemetry retain their canonical keys, identifiers, engine names and source values. The additional `uiLanguage` and `uiStrings` fields are presentation metadata within the serialized container options, also visible under `materialParameters`. Arbitrary developer pages and the calibration page are not translated by the application's language selector. No iPhone browser APIs or device properties are fabricated.

Translated simulated tab titles use the existing measured slots and truncation. A language change can alter text length inside those slots, but must not alter their measured geometry. The app-owned color laboratory can reflow its translated prose; it is separate from the canonical calibration page and reference scenes.

## Verification and limits

Run `mise exec -- just test` for the portable tests, including the dependency-free .NET localization harness. Its direct invocation is:

```sh
mise exec -- dotnet run --project tests/DevDuo.Localization.Tests -p:RestoreLockedMode=true
```

The harness links the actual localization, profile, scene and options sources. It checks catalog key and placeholder parity, nonempty text, formatting, fallback, explicit/system language resolution, preference restart/corruption/write-failure behavior, fixture key coverage, all eight mappings and 24 states, and unchanged measured option values across locale payloads. It uses the repository-managed .NET SDK and has no external test-runner dependency.

Compilation and portable tests cannot establish native runtime correctness. On macOS, separately verify language changes while a page is loaded and scrolled, native accessibility labels, selected mode/profile preservation, the color laboratory, save/error messages and preference restoration after restart. Inspect viewport and native-frame telemetry before and after switching languages. On Windows, perform the equivalent runtime checks, including the WebView2 error path, input and DPI behavior. A Windows cross-build alone does not establish those results. See [Windows handoff](009-WINDOWS-HANDOFF.md) and [validation](003-CALIBRATION-WORKFLOW.md).

[English](011-LOCALIZATION.md) | [中文](../zh/011-LOCALIZATION.md)
