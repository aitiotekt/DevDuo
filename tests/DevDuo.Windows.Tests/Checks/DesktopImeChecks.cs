using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckDesktopIme()
    {
        using var profile = Environment.GetEnvironmentVariable("DEVDUO_TEST_IME_PROFILE") == "microsoft-pinyin" ? new DesktopPinyinProfile() : null;
        if (Environment.GetEnvironmentVariable("DEVDUO_IME_BASELINE") == "1")
        {
            await CheckNativeImeBaseline(profile);
            return;
        }
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_FOREGROUND_GATE") == "1")
            await WaitDesktopInputStart();
        using var server = new BrowserTestServer();
        await LoadDesktopInput();
        var layouts = new nint[DesktopGetKeyboardLayoutList(0, null)];
        DesktopGetKeyboardLayoutList(layouts.Length, layouts);
        Save("desktop-ime-environment", JsonSerializer.SerializeToElement(new { current = DesktopGetKeyboardLayout(0).ToInt64(), installed = layouts.Select(layout => layout.ToInt64()).ToArray(), host = State() }));
        for (var pose = 0; pose < 2; pose++)
        {
            if (pose == 1)
            {
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
            }
            var browser = Web.WindowsPanel!.Browser;
            foreach (var target in new[] { "text", "multiline", "editable" })
            {
                var previousEditor = State().GetProperty("webKit").GetProperty("textInput").GetProperty("editorId").GetUInt64();
                await Web.EvaluateResultAsync($"document.querySelector('#{target}').focus();window.resetInputAcceptance();'ready'");
                ActivateDesktopInput(browser.BrowserWindow);
                await Wait(state => state.GetProperty("webKit").GetProperty("textInput").GetProperty("editable").GetBoolean() && state.GetProperty("webKit").GetProperty("textInput").GetProperty("editorId").GetUInt64() != previousEditor, "Native editor/caret input readiness");
                profile?.Activate();
                await Task.Delay(100);
                var context = DesktopImmGetContext(browser.BrowserWindow);
                if (context == 0) throw new InvalidOperationException("The native browser needs an available Windows IME context.");
                var open = DesktopImmGetOpenStatus(context);
                DesktopImmGetConversionStatus(context, out var conversion, out var sentence);
                using var tsf = new DesktopChineseInputMode();
                try
                {
                    DesktopImmSetOpenStatus(context, true);
                    DesktopImmSetConversionStatus(context, conversion | 1, sentence);
                    var gui = new DesktopGuiThreadInfo { Size = (uint)Marshal.SizeOf<DesktopGuiThreadInfo>() };
                    DesktopGetGuiThreadInfo(0, ref gui);
                    Save($"desktop-ime-native-context-{pose}-{target}", JsonSerializer.SerializeToElement(new { activeProfile = profile?.ActiveProfile(), mode = tsf.Diagnostics, nativeFocus = gui.Focus.ToInt64(), nativeCaret = gui.Caret.ToInt64(), browser = browser.BrowserWindow.ToInt64(), gui.CaretRect, host = State() }));
                    var baseline = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])");
                    SendDesktopImeLetter(0x4E); // Actual scan-code keys, not injected composition events.
                    await Task.Delay(180);
                    SendDesktopImeLetter(0x49);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'))", value => value == "true", "Installed Chinese IME preedit");
                    using var preeditJson = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"))!);
                    var preedit = preeditJson.RootElement.Clone();
                    Check(preedit.GetProperty("events").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "compositionstart" && item.GetProperty("target").GetString() == target),
                        "Actual Windows IME input starts native WebCore composition in the focused authored field.");
                    Check(preedit.GetProperty("events").EnumerateArray().Any(item => item.GetProperty("type").GetString() == "input" && item.GetProperty("composing").ValueKind == JsonValueKind.True),
                        "Preedit produces native composing input events before candidate confirmation.");
                    SendDesktopKeys(0x20);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionend'))", value => value == "true", "Native IME candidate commit");
                    using var committedJson = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"))!);
                    var committed = committedJson.RootElement.Clone();
                    var text = committed.GetProperty(target).GetString()!;
                    Check(text.Any(character => character >= 0x3400 && character <= 0x9fff),
                        "A real selected Chinese candidate is committed to the web field.");
                    Check(committed.GetProperty("events").EnumerateArray().Count(item => item.GetProperty("type").GetString() == "compositionend") == 1,
                        "Candidate commit ends composition once, without duplicate compatibility character insertion.");
                    await Web.EvaluateResultAsync("window.resetInputAcceptance();'reset'");
                    SendDesktopImeLetter(0x4E);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'))", value => value == "true", "Second native preedit");
                    SendDesktopKeys(0x1B);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionend'))", value => value == "true", "Escape cancels native preedit");
                    Check(await Web.EvaluateResultAsync($"document.querySelector('#{target}').{(target == "editable" ? "textContent" : "value")}") == text,
                        "Escape cancels preedit while preserving previously committed content.");
                    await Web.EvaluateResultAsync("window.resetInputAcceptance();'reset'");
                    SendDesktopKeys(0x11, 0x41);
                    var selection = target == "editable" ? "getSelection().toString()" : $"((e)=>e.value.slice(e.selectionStart,e.selectionEnd))(document.querySelector('#{target}'))";
                    await WaitForScript(selection, value => value == text, "Actual editor selection before IME replacement");
                    SendDesktopImeLetter(0x4E);
                    await Task.Delay(180);
                    SendDesktopImeLetter(0x49);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'))", value => value == "true", "Selected text replacement preedit");
                    SendDesktopKeys(0x20);
                    await WaitForScript("JSON.stringify(inputAcceptanceState().events.some(e=>e.type==='compositionend'))", value => value == "true", "Selected text replacement commit");
                    using var replacementJson = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"))!);
                    var replacement = replacementJson.RootElement;
                    var ends = replacement.GetProperty("events").EnumerateArray().Where(item => item.GetProperty("type").GetString() == "compositionend").ToArray();
                    Check(ends.Length == 1 && ends[0].GetProperty("data").GetString() == replacement.GetProperty(target).GetString(),
                        "Native candidate confirmation replaces the selected range exactly once.");
                    Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight,scrollX,scrollY])") == baseline,
                        "IME preedit, commit and cancellation preserve the document and real viewport geometry.");
                    Save($"desktop-ime-{pose}-{target}", JsonSerializer.SerializeToElement(new { preedit, committed, final = await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"), host = State() }));
                }
                finally
                {
                    Save($"desktop-ime-observed-{pose}-{target}", JsonSerializer.SerializeToElement(new { interaction = await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"), layout = DesktopGetKeyboardLayout(0).ToInt64(), host = State() }));
                    DesktopImmSetConversionStatus(context, conversion, sentence);
                    DesktopImmSetOpenStatus(context, open);
                    DesktopImmReleaseContext(browser.BrowserWindow, context);
                }
            }
        }
        try { await CheckDesktopImeLifecycle(profile); }
        finally { Save("desktop-ime-lifecycle-observed", JsonSerializer.SerializeToElement(new { interaction = await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"), host = State() })); }
        Console.WriteLine($"PASS desktop IME: {assertions} actual preedit/commit/cancel and lifecycle checks in unrotated and Closed 180-degree presentation.");
    }

    private async Task CheckDesktopImeLifecycle(DesktopPinyinProfile? profile)
    {
        await LoadDesktopInput();
        var browser = Web.WindowsPanel!.Browser;
        await Web.EvaluateResultAsync("document.querySelector('#text').focus();resetInputAcceptance();'focused'");
        ActivateDesktopInput(browser.BrowserWindow);
        await Wait(state => state.GetProperty("webKit").GetProperty("textInput").GetProperty("tsfActive").GetBoolean(), "Lifecycle editor readiness");
        profile?.Activate();
        using var mode = new DesktopChineseInputMode();
        SendDesktopImeLetter(0x4E);
        await WaitForScript("String(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'))", value => value == "true", "Preedit before field focus changes");
        var previousEditor = State().GetProperty("webKit").GetProperty("textInput").GetProperty("editorId").GetUInt64();
        await Web.EvaluateResultAsync("document.querySelector('#multiline').focus();resetInputAcceptance();'focused'");
        await Wait(state => state.GetProperty("webKit").GetProperty("textInput").GetProperty("editorId").GetUInt64() != previousEditor &&
            !state.GetProperty("webKit").GetProperty("textInput").GetProperty("composing").GetBoolean(), "Focus change retires the previous composition context");
        await WaitForScript("String(inputAcceptanceState().events.some(e=>e.type==='compositionend'&&e.target==='text'))", value => value == "true", "The old editor receives its own compositionend");
        Check(await Web.EvaluateResultAsync("document.activeElement?.id") == "multiline",
            "Retiring the old composition preserves the newly focused editor.");
        Check(await Web.EvaluateResultAsync("document.querySelector('#multiline').value") == "",
            "Changing focused fields during preedit does not copy pending text into the new editor.");
        SendDesktopImeLetter(0x4E);
        await Task.Delay(180);
        SendDesktopImeLetter(0x49);
        await WaitForScript("String(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'&&e.target==='multiline'))", value => value == "true", "Fresh composition in the new field");
        SendDesktopKeys(0x20);
        await WaitForScript("String(inputAcceptanceState().events.some(e=>e.type==='compositionend'&&e.target==='multiline'))", value => value == "true", "Fresh candidate commit after focus change");
        Check((await Web.EvaluateResultAsync("document.querySelector('#multiline').value"))!.Any(character => character is >= '\u3400' and <= '\u9fff'),
            "The newly focused editor accepts its own Chinese candidate after retiring the old context.");
        await Web.EvaluateResultAsync("resetInputAcceptance();'reset'");
        SendDesktopImeLetter(0x4E);
        await WaitForScript("String(inputAcceptanceState().events.some(e=>e.type==='compositionupdate'))", value => value == "true", "Preedit before navigation");
        var document = await Web.EvaluateResultAsync("String(performance.timeOrigin)");
        await LoadDesktopInput();
        await Wait(state => !state.GetProperty("webKit").GetProperty("textInput").GetProperty("composing").GetBoolean() &&
            !state.GetProperty("webKit").GetProperty("textInput").GetProperty("editable").GetBoolean(), "Navigation retires editable input state");
        Check(await Web.EvaluateResultAsync("String(performance.timeOrigin)") != document,
            "The IME navigation case observes a real replacement document.");
        Check(await Web.EvaluateResultAsync("JSON.stringify([inputAcceptanceState().text,inputAcceptanceState().multiline,inputAcceptanceState().editable])") == "[\"\",\"\",\"\"]",
            "A navigation during preedit cannot commit the old transaction into the replacement document.");
        Save("desktop-ime-lifecycle", JsonSerializer.SerializeToElement(new { interaction = await Web.EvaluateResultAsync("JSON.stringify(inputAcceptanceState())"), host = State() }));
    }
}
