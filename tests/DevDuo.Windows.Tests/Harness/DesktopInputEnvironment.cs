using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    internal static IDisposable? ActivatePresentedInputProfile()
    {
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_IME_PROFILE") != "microsoft-pinyin") return null;
        var profile = new DesktopPinyinProfile();
        try { profile.Activate(); return profile; }
        catch { profile.Dispose(); throw; }
    }
    [DllImport("imm32.dll", EntryPoint = "ImmGetContext")] private static extern nint DesktopImmGetContext(nint window);
    private static async Task WaitDesktopInputStart()
    {
        using var form = new System.Windows.Forms.Form { Text = "Native desktop acceptance", Width = 440, Height = 140,
            TopMost = true, StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen };
        using var start = new System.Windows.Forms.Button { Text = "Start native acceptance", Dock = System.Windows.Forms.DockStyle.Fill };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        start.Click += (_, _) => started.TrySetResult();
        form.FormClosed += (_, _) => started.TrySetCanceled();
        form.Controls.Add(start);
        form.AcceptButton = start;
        form.Show();
        await started.Task;
        form.Close();
    }
    private async Task CheckNativeImeBaseline(DesktopPinyinProfile? profile)
    {
        using var form = new System.Windows.Forms.Form { Text = "Native input baseline", Width = 500, Height = 180,
            TopMost = true, StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen };
        using var input = new DesktopImeTextBox { Dock = System.Windows.Forms.DockStyle.Top, Font = new System.Drawing.Font("Segoe UI", 18), ImeMode = System.Windows.Forms.ImeMode.On };
        using var start = new System.Windows.Forms.Button { Text = "Start input baseline", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 40 };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        start.Click += (_, _) => started.TrySetResult();
        form.FormClosed += (_, _) => started.TrySetCanceled();
        form.Controls.Add(input);
        form.Controls.Add(start);
        form.Show();
        await started.Task;
        RequireDesktopInputForeground();
        input.Focus();
        profile?.Activate();
        await Task.Delay(3000);
        PresentationSetForegroundWindow(form.Handle);
        input.Focus();
        var context = DesktopImmGetContext(input.Handle);
        var open = DesktopImmGetOpenStatus(context);
        DesktopImmGetConversionStatus(context, out var conversion, out var sentence);
        try
        {
            DesktopImmSetOpenStatus(context, true);
            DesktopImmSetConversionStatus(context, conversion | 1, sentence);
            if (DesktopGetFocus() != input.Handle) throw new InvalidOperationException("The native baseline edit must have keyboard focus.");
            SendDesktopImeLetter(0x4E);
            await Task.Delay(180);
            SendDesktopImeLetter(0x49);
            await Task.Delay(800);
            if (input.Text == "ni" && input.CompositionEvents.Count == 0)
            {
                // A literal Latin probe establishes English mode. Toggle the
                // installed Pinyin service with its actual Shift mode key.
                input.SelectAll();
                SendDesktopKeys(0x08);
                SendDesktopImeLetter(0x10);
                SendDesktopImeLetter(0x4E);
                await Task.Delay(180);
                SendDesktopImeLetter(0x49);
            }
            await Task.Delay(2000);
            Save("desktop-ime-native-baseline", JsonSerializer.SerializeToElement(new { text = input.Text, input.CompositionEvents, layout = DesktopGetKeyboardLayout(0).ToInt64(), activeProfile = profile?.ActiveProfile() }));
            SendDesktopKeys(0x20);
            await Task.Delay(2000);
            Save("desktop-ime-native-baseline-commit", JsonSerializer.SerializeToElement(new { text = input.Text, input.CompositionEvents }));
        }
        finally
        {
            DesktopGetWindowThreadProcessId(PresentationGetForegroundWindow(), out var process);
            if (process == Environment.ProcessId) SendDesktopKeys(0x1B);
            DesktopImmSetConversionStatus(context, conversion, sentence);
            DesktopImmSetOpenStatus(context, open);
            DesktopImmReleaseContext(input.Handle, context);
            form.Close();
        }
        Console.WriteLine("Native system edit baseline recorded; this is not web IME acceptance.");
    }
    private sealed class DesktopImeTextBox : System.Windows.Forms.TextBox
    {
        internal List<object> CompositionEvents { get; } = [];
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg is 0x10D or 0x10E or 0x10F)
            {
                var context = DesktopImmGetContext(Handle);
                var length = DesktopImmGetCompositionString(context, 8, null, 0);
                var text = length > 0 ? new byte[length] : [];
                if (length > 0) DesktopImmGetCompositionString(context, 8, text, (uint)text.Length);
                DesktopImmReleaseContext(Handle, context);
                CompositionEvents.Add(new { message = message.Msg, flags = message.LParam.ToInt64(), preedit = System.Text.Encoding.Unicode.GetString(text) });
            }
            base.WndProc(ref message);
        }
    }
    [DllImport("imm32.dll", EntryPoint = "ImmGetCompositionStringW")] private static extern int DesktopImmGetCompositionString(nint context, uint kind, [Out] byte[]? data, uint length);
    [StructLayout(LayoutKind.Sequential)] private struct DesktopGuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal DesktopRect CaretRect;
    }
    [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo")] private static extern bool DesktopGetGuiThreadInfo(uint thread, ref DesktopGuiThreadInfo info);
    private static void SendDesktopImeLetter(ushort key)
    {
        RequireDesktopInputForeground();
        var scan = (ushort)DesktopMapVirtualKey(key, 0);
        var inputs = new[] { new DesktopKey { Type = 1, Scan = scan, Flags = 8 }, new DesktopKey { Type = 1, Scan = scan, Flags = 10 } };
        if (DesktopSendInput(2, inputs, Marshal.SizeOf<DesktopKey>()) != 2) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")] private static extern uint DesktopMapVirtualKey(uint key, uint kind);
    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")] private static extern nint DesktopGetKeyboardLayout(uint thread);
    [DllImport("user32.dll", EntryPoint = "ActivateKeyboardLayout")] private static extern nint DesktopActivateKeyboardLayout(nint layout, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutList")] private static extern int DesktopGetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("imm32.dll", EntryPoint = "ImmReleaseContext")] private static extern bool DesktopImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll", EntryPoint = "ImmGetOpenStatus")] private static extern bool DesktopImmGetOpenStatus(nint context);
    [DllImport("imm32.dll", EntryPoint = "ImmSetOpenStatus")] private static extern bool DesktopImmSetOpenStatus(nint context, bool open);
    [DllImport("imm32.dll", EntryPoint = "ImmGetConversionStatus")] private static extern bool DesktopImmGetConversionStatus(nint context, out uint conversion, out uint sentence);
    [DllImport("imm32.dll", EntryPoint = "ImmSetConversionStatus")] private static extern bool DesktopImmSetConversionStatus(nint context, uint conversion, uint sentence);

    // IMM and TSF maintain separate open/conversion compartments. Configure the
    // installed TIP through its public thread service, then restore both values.
    private sealed class DesktopChineseInputMode : IDisposable
    {
        private readonly DesktopThreadManager manager;
        private readonly DesktopCompartmentManager compartments;
        private readonly uint client;
        private readonly List<(Guid Key, DesktopCompartment Compartment, object? Previous)> values = [];
        internal object Diagnostics => values.Select(item =>
        {
            item.Compartment.GetValue(out var current);
            return new { item.Key, item.Previous, current };
        }).ToArray();
        internal DesktopChineseInputMode()
        {
            Marshal.ThrowExceptionForHR(DesktopGetThreadManager(out manager));
            manager.Activate(out client);
            compartments = (DesktopCompartmentManager)manager;
            foreach (var id in new[] { new Guid("58273aad-01bb-4164-95c6-755ba0b5162d"), new Guid("ccf05dd8-4a87-11d7-a6e2-00065b84435c") })
            {
                var key = id;
                compartments.GetCompartment(ref key, out var compartment);
                compartment.GetValue(out var previous);
                values.Add((key, compartment, previous));
                object enabled = 1;
                compartment.SetValue(client, ref enabled);
            }
        }
        public void Dispose()
        {
            foreach (var (key, compartment, previous) in values)
            {
                if (previous is null)
                {
                    var removed = key;
                    compartments.ClearCompartment(client, ref removed);
                }
                else
                {
                    var restored = previous;
                    compartment.SetValue(client, ref restored);
                }
                Marshal.ReleaseComObject(compartment);
            }
            manager.Deactivate();
            Marshal.ReleaseComObject(manager);
        }
    }
    // Only the used leading methods of the published COM vtables are declared.
    [ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DesktopThreadManager { void Activate(out uint client); void Deactivate(); }
    [DllImport("msctf.dll", EntryPoint = "TF_GetThreadMgr")]
    private static extern int DesktopGetThreadManager([MarshalAs(UnmanagedType.Interface)] out DesktopThreadManager manager);
    [ComImport, Guid("7dcf57ac-18ad-438b-824d-979bffb74b7c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DesktopCompartmentManager
    {
        void GetCompartment(ref Guid key, out DesktopCompartment compartment);
        void ClearCompartment(uint client, ref Guid key);
    }
    [ComImport, Guid("bb08f7a9-607a-4384-8623-056892b64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DesktopCompartment
    {
        void SetValue(uint client, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
        void GetValue([MarshalAs(UnmanagedType.Struct)] out object value);
    }

    private sealed class DesktopPinyinProfile : IDisposable
    {
        private readonly DesktopProfileManager manager;
        private readonly DesktopInputProfile previous, pinyin;
        private readonly nint previousLayout = DesktopGetKeyboardLayout(0);
        internal object ActiveProfile()
        {
            var category = new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");
            manager.GetActiveProfile(ref category, out var active);
            return new { active.Service, active.Profile, active.Language, keyboard = active.Keyboard.ToInt64() };
        }
        internal void Activate()
        {
            var layouts = new nint[DesktopGetKeyboardLayoutList(0, null)];
            DesktopGetKeyboardLayoutList(layouts.Length, layouts);
            var chinese = layouts.FirstOrDefault(layout => (layout.ToInt64() & 0xffff) == 0x804);
            if (chinese == 0 || DesktopActivateKeyboardLayout(chinese, 0) == 0)
                throw new InvalidOperationException("The Chinese keyboard layout must be loaded and active for Pinyin acceptance.");
            var service = pinyin.Service;
            var profile = pinyin.Profile;
            manager.ActivateProfile(1, 0x804, ref service, ref profile, 0, 0x10000000);
        }
        internal DesktopPinyinProfile()
        {
            var type = Type.GetTypeFromCLSID(new Guid("33c53a50-f456-4884-b049-85fd643ecfed"), true)!;
            manager = (DesktopProfileManager)Activator.CreateInstance(type)!;
            var category = new Guid("34745c63-b2f0-4784-8b67-5e12c8701a31");
            manager.GetActiveProfile(ref category, out previous);
            var service = new Guid("81d4e9c9-1d3b-41bc-9e6c-4b40bf79e35e");
            var profile = new Guid("fa550b04-5ad7-411f-a5ac-ca038ec515d7");
            manager.GetProfile(1, 0x804, ref service, ref profile, 0, out pinyin);
            if ((pinyin.Flags & 2) == 0) throw new InvalidOperationException("Enable Microsoft Pinyin in Windows keyboard settings before running this acceptance.");
            manager.ActivateProfile(1, 0x804, ref service, ref profile, 0, 0x10000000);
            manager.GetActiveProfile(ref category, out var active);
            if (active.Profile != profile) throw new InvalidOperationException("Microsoft Pinyin did not become the active native TIP.");
        }
        public void Dispose()
        {
            var service = pinyin.Service;
            var profile = pinyin.Profile;
            manager.DeactivateProfile(pinyin.Type, pinyin.Language, ref service, ref profile, pinyin.Keyboard, 0x10000000);
            service = previous.Service;
            profile = previous.Profile;
            manager.ActivateProfile(previous.Type, previous.Language, ref service, ref profile, previous.Keyboard, 0x10000000);
            DesktopActivateKeyboardLayout(previousLayout, 0);
            Marshal.ReleaseComObject(manager);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DesktopInputProfile
    {
        internal uint Type;
        internal ushort Language;
        internal Guid Service, Profile, Category;
        internal nint Substitute;
        internal uint Capabilities;
        internal nint Keyboard;
        internal uint Flags;
    }
    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface DesktopProfileManager
    {
        void ActivateProfile(uint type, ushort language, ref Guid service, ref Guid profile, nint keyboard, uint flags);
        void DeactivateProfile(uint type, ushort language, ref Guid service, ref Guid profile, nint keyboard, uint flags);
        void GetProfile(uint type, ushort language, ref Guid service, ref Guid profile, nint keyboard, out DesktopInputProfile value);
        void EnumProfiles(ushort language, out nint profiles);
        void ReleaseInputProcessor(ref Guid service, uint flags);
        void RegisterProfile(ref Guid service, ushort language, ref Guid profile, nint description, uint descriptionLength,
            nint icon, uint iconLength, uint iconIndex, nint substitute, uint preferredLayout, int enabled, uint flags);
        void UnregisterProfile(ref Guid service, ushort language, ref Guid profile, uint flags);
        void GetActiveProfile(ref Guid category, out DesktopInputProfile profile);
    }
}
