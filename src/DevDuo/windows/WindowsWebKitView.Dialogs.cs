#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using static DevDuo.WindowsWebKitApi;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private enum PageDialogKind { Alert, Confirm, Prompt }
    private static readonly DialogCallback alertCallback = (page, message, _, origin, listener, context) =>
        QueuePageDialog(page, context, PageDialogKind.Alert, message, 0, origin, listener);
    private static readonly DialogCallback confirmCallback = (page, message, _, origin, listener, context) =>
        QueuePageDialog(page, context, PageDialogKind.Confirm, message, 0, origin, listener);
    private static readonly PromptCallback promptCallback = (page, message, value, _, origin, listener, context) =>
        QueuePageDialog(page, context, PageDialogKind.Prompt, message, value, origin, listener);
    private readonly List<PageDialogRequest> pageDialogs = [];
    private bool showingPageDialog;

    private void ConfigurePageDialogs()
    {
        var client = new UiClient
        {
            Version = 6, Info = token, UnusedCallbacks = new nint[58],
            Alert = Marshal.GetFunctionPointerForDelegate(alertCallback),
            Confirm = Marshal.GetFunctionPointerForDelegate(confirmCallback),
            Prompt = Marshal.GetFunctionPointerForDelegate(promptCallback)
        };
        WKPageSetPageUIClient(page, ref client);
    }

    private static void QueuePageDialog(nint page, nint context, PageDialogKind kind, nint message, nint value, nint origin, nint listener)
    {
        var owner = Owner(page, context);
        var request = new PageDialogRequest(owner, kind, WKRetain(listener));
        if (owner is null) { request.Complete(false, null); return; }
        owner.pageDialogs.Add(request);
        try
        {
            request.Message = String(message);
            request.Initial = String(value);
            request.Origin = CopyString(WKSecurityOriginCopyToString(origin));
            // Return to WebKit before opening a modal loop. The retained listener
            // completes once, even if navigation/disposal cancels the queued UI.
            owner.BeginInvoke(owner.ShowNextPageDialog);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine(error);
            request.Complete(false, null);
        }
    }

    private void ShowNextPageDialog()
    {
        if (showingPageDialog || pageDialogs.Count == 0) return;
        var request = pageDialogs[0];
        if (!request.IsCurrent) { request.Complete(false, null); return; }
        showingPageDialog = true;
        try
        {
            var ui = AppLocalization.Current;
            var origin = request.Origin is not ("" or "null") ? request.Origin : ui.Text("PageDialog.LocalPage");
            using var dialog = new Forms.Form
            {
                Name = "DevDuoPageDialog", Text = ui.Format("PageDialog.Title", origin),
                ClientSize = new(420, request.Kind == PageDialogKind.Prompt ? 260 : 210),
                AutoScaleMode = Forms.AutoScaleMode.Dpi, FormBorderStyle = Forms.FormBorderStyle.FixedDialog,
                StartPosition = Forms.FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
                ShowInTaskbar = false, Padding = new(12)
            };
            dialog.AccessibleName = dialog.Text;
            request.Dialog = dialog;
            var layout = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new(Forms.SizeType.Percent, 100));
            layout.RowStyles.Add(new(Forms.SizeType.AutoSize));
            layout.RowStyles.Add(new(Forms.SizeType.AutoSize));
            var messagePanel = new Forms.Panel { Dock = Forms.DockStyle.Fill, AutoScroll = true };
            messagePanel.Controls.Add(new Forms.Label { Text = request.Message, UseMnemonic = false, AutoSize = true, MaximumSize = new(370, 0) });
            layout.Controls.Add(messagePanel, 0, 0);
            using var input = new Forms.TextBox { Name = "Response", Text = request.Initial, Dock = Forms.DockStyle.Top, AccessibleName = ui.Text("PageDialog.Response") };
            if (request.Kind == PageDialogKind.Prompt) layout.Controls.Add(input, 0, 1);
            var buttons = new Forms.FlowLayoutPanel { AutoSize = true, Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.RightToLeft, Padding = new(0, 12, 0, 0) };
            var accept = new Forms.Button { Name = "Accept", Text = ui.Text("PageDialog.Accept"), AutoSize = true, DialogResult = Forms.DialogResult.OK };
            accept.AccessibleName = accept.Text;
            buttons.Controls.Add(accept);
            dialog.AcceptButton = accept;
            if (request.Kind != PageDialogKind.Alert)
            {
                var cancel = new Forms.Button { Name = "Cancel", Text = ui.Text("PageDialog.Cancel"), AutoSize = true, DialogResult = Forms.DialogResult.Cancel };
                cancel.AccessibleName = cancel.Text;
                buttons.Controls.Add(cancel);
                dialog.CancelButton = cancel;
            }
            layout.Controls.Add(buttons, 0, 2);
            dialog.Controls.Add(layout);
            dialog.Shown += (_, _) => { if (request.Kind == PageDialogKind.Prompt) { input.Focus(); input.SelectAll(); } };
            var result = dialog.ShowDialog(new PageDialogOwner(GetAncestor(Handle, 2)));
            request.Complete(result == Forms.DialogResult.OK, input.Text);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine(error);
            request.Complete(false, null);
        }
        finally
        {
            request.Dialog = null;
            showingPageDialog = false;
            if (!disposed && IsHandleCreated && pageDialogs.Count > 0) BeginInvoke(ShowNextPageDialog);
        }
    }

    private void ClosePageDialogs()
    {
        foreach (var request in pageDialogs.ToArray())
        {
            if (request.Dialog is { IsDisposed: false } dialog)
            {
                // Close can defer removal until the nested modal loop unwinds.
                // Remove its visible surface before destroying the source view.
                dialog.DialogResult = Forms.DialogResult.Cancel;
                dialog.Hide();
                dialog.Close();
            }
            request.Complete(false, null);
        }
    }

    private sealed class PageDialogOwner(nint handle) : Forms.IWin32Window
    {
        public nint Handle => handle;
    }

    private sealed class PageDialogRequest(WindowsWebKitView? owner, PageDialogKind kind, nint listener)
    {
        private readonly long navigation = owner?.navigationGeneration ?? -1;
        private bool completed;
        internal PageDialogKind Kind => kind;
        internal string Message = "", Initial = "", Origin = "";
        internal Forms.Form? Dialog;
        internal bool IsCurrent => !completed && owner is { disposed: false } && owner.navigationGeneration == navigation;
        internal void Complete(bool accepted, string? value)
        {
            if (completed) return;
            accepted &= IsCurrent;
            completed = true;
            owner?.pageDialogs.Remove(this);
            nint result = 0;
            try
            {
                if (kind == PageDialogKind.Alert) WKPageRunJavaScriptAlertResultListenerCall(listener);
                else if (kind == PageDialogKind.Confirm) WKPageRunJavaScriptConfirmResultListenerCall(listener, accepted);
                else
                {
                    if (accepted) result = WKStringCreateWithUTF8CString(value ?? "");
                    WKPageRunJavaScriptPromptResultListenerCall(listener, result);
                }
            }
            finally { if (result != 0) WKRelease(result); WKRelease(listener); }
        }
    }
}
#endif
