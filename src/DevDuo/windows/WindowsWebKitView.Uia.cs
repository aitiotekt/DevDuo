#if WINDOWS
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using Forms = System.Windows.Forms;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private nint GetWebUiaProvider(nint parameter, nint objectId) => GetPresentedUiaProvider(browserWindow, parameter, objectId);

    internal nint AccessibilityHostWindow;

    internal nint GetPresentedUiaProvider(nint window, nint parameter, nint objectId)
    {
        if (disposed || !IsReady) return 0;
        webAccessibility ??= new(this);
        webAccessibility.Refresh();
        if (AccessibilityHostWindow != 0 && window != AccessibilityHostWindow)
            return UiaReturnRawElementProvider(window, parameter, objectId, webAccessibility.HostBridge(window, AccessibilityHostWindow));
        return UiaReturnRawElementProvider(window, parameter, objectId, webAccessibility.Provider(webAccessibility.Root, window));
    }

    [DllImport("UIAutomationCore.dll")]
    private static extern nint UiaReturnRawElementProvider(nint window, nint parameter, nint objectId, [MarshalAs(UnmanagedType.Interface)] IRawElementProviderSimple provider);

    private void NotifyWebUia(uint nativeEvent, ulong objectId)
    {
        if (webAccessibility is not { } tree || !AutomationInteropProvider.ClientsAreListening) return;
        tree.Refresh();
        var root = tree.Provider(tree.Root);
        var target = tree.Provider(tree.Object(objectId) ?? tree.Root);
        if (nativeEvent == 0x8005)
            AutomationInteropProvider.RaiseAutomationEvent(AutomationElementIdentifiers.AutomationFocusChangedEvent, target,
                new AutomationEventArgs(AutomationElementIdentifiers.AutomationFocusChangedEvent));
        else if (nativeEvent == 0x800E)
            AutomationInteropProvider.RaiseAutomationPropertyChangedEvent(target,
                new AutomationPropertyChangedEventArgs(ValuePatternIdentifiers.ValueProperty, null, target.Value));
        else
            AutomationInteropProvider.RaiseStructureChangedEvent(root, new StructureChangedEventArgs(StructureChangeType.ChildrenInvalidated, []));
    }

    // The visible presenter owns the semantic fragment; source/intermediate
    // HWNDs only bridge focus and hit lookup. WebCore owns properties/actions.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private class WebUiaProvider(WebAccessibility tree, WebAccessibleObject model, nint window) : StandardOleMarshalObject,
        IRawElementProviderSimple, IRawElementProviderFragment, IValueProvider, IInvokeProvider
    {
        protected WebAccessibility Tree => tree;
        protected WebAccessibleObject Model => model;
        protected nint Window => window;
        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;
        public IRawElementProviderSimple? HostRawElementProvider => ReferenceEquals(model, tree.Root) && tree.Window != 0
            ? AutomationInteropProvider.HostProviderFromHandle(window) : null;
        public IRawElementProviderFragmentRoot FragmentRoot => (IRawElementProviderFragmentRoot)tree.Provider(tree.Root, window);
        public System.Windows.Rect BoundingRectangle => model.Bounds is var bounds && !bounds.IsEmpty
            ? new(bounds.X, bounds.Y, bounds.Width, bounds.Height) : System.Windows.Rect.Empty;

        public object? GetPatternProvider(int patternId)
        {
            model.EnsureAvailable();
            if (patternId == ValuePatternIdentifiers.Pattern.Id && model.Role == Forms.AccessibleRole.Text) return this;
            if (patternId == InvokePatternIdentifiers.Pattern.Id && model.DefaultAction is not null) return this;
            return null;
        }
        public object? GetPropertyValue(int propertyId)
        {
            model.EnsureAvailable();
            var state = model.State;
            if (propertyId == AutomationElementIdentifiers.NameProperty.Id) return model.Name ?? "";
            if (propertyId == AutomationElementIdentifiers.HelpTextProperty.Id) return model.Description ?? "";
            if (propertyId == AutomationElementIdentifiers.FrameworkIdProperty.Id) return "WebKit";
            if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id) return ControlTypeId(model.Role);
            if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id) return (state & Forms.AccessibleStates.Focusable) != 0;
            if (propertyId == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id) return (state & Forms.AccessibleStates.Focused) != 0;
            if (propertyId == AutomationElementIdentifiers.IsEnabledProperty.Id) return (state & Forms.AccessibleStates.Unavailable) == 0;
            if (propertyId == AutomationElementIdentifiers.IsOffscreenProperty.Id) return (state & (Forms.AccessibleStates.Offscreen | Forms.AccessibleStates.Invisible)) != 0;
            if (propertyId == AutomationElementIdentifiers.IsPasswordProperty.Id) return (state & Forms.AccessibleStates.Protected) != 0;
            if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id || propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id) return true;
            return null;
        }
        private static int ControlTypeId(Forms.AccessibleRole role) => role switch
        {
            Forms.AccessibleRole.Document => ControlType.Document.Id,
            Forms.AccessibleRole.Text => ControlType.Edit.Id,
            Forms.AccessibleRole.StaticText => ControlType.Text.Id,
            Forms.AccessibleRole.PushButton => ControlType.Button.Id,
            Forms.AccessibleRole.CheckButton => ControlType.CheckBox.Id,
            Forms.AccessibleRole.RadioButton => ControlType.RadioButton.Id,
            Forms.AccessibleRole.ComboBox => ControlType.ComboBox.Id,
            Forms.AccessibleRole.Link => ControlType.Hyperlink.Id,
            Forms.AccessibleRole.Graphic => ControlType.Image.Id,
            Forms.AccessibleRole.List => ControlType.List.Id,
            Forms.AccessibleRole.ListItem => ControlType.ListItem.Id,
            Forms.AccessibleRole.Slider => ControlType.Slider.Id,
            Forms.AccessibleRole.ProgressBar => ControlType.ProgressBar.Id,
            _ => ControlType.Group.Id
        };
        public IRawElementProviderFragment? Navigate(NavigateDirection direction)
        {
            var next = direction switch
            {
                NavigateDirection.Parent => model.Parent,
                NavigateDirection.FirstChild => model.GetChild(0),
                NavigateDirection.LastChild => model.GetChild(model.GetChildCount() - 1),
                NavigateDirection.NextSibling => model.Navigate(Forms.AccessibleNavigation.Next),
                NavigateDirection.PreviousSibling => model.Navigate(Forms.AccessibleNavigation.Previous),
                _ => null
            };
            return next is WebAccessibleObject web ? tree.Provider(web, window) : null;
        }
        public int[]? GetRuntimeId() => model.Id == 0 ? null : [AutomationInteropProvider.AppendRuntimeId,
            model.Document.GetHashCode(StringComparison.Ordinal), unchecked((int)model.Id), unchecked((int)(model.Id >> 32))];
        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
        public void SetFocus() => model.Select(Forms.AccessibleSelection.TakeFocus);
        public bool IsReadOnly => (model.State & Forms.AccessibleStates.ReadOnly) != 0;
        public string Value => model.Value ?? "";
        public void SetValue(string value) => model.Value = value;
        public void Invoke() => model.DoDefaultAction();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class WebUiaRootProvider(WebAccessibility tree, WebAccessibleObject model, nint window) : WebUiaProvider(tree, model, window),
        IRawElementProviderFragmentRoot, IRawElementProviderHwndOverride
    {
        public IRawElementProviderSimple? GetOverrideProviderForHwnd(nint child) =>
            child != Window && Tree.IsPresentationChild(child) ? Tree.HostBridge(child, Window) : null;
        public IRawElementProviderFragment? GetFocus() => Model.GetFocused() is WebAccessibleObject focused ? Tree.Provider(focused, Window) : null;
        public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y) => Model.HitTest((int)x, (int)y) is WebAccessibleObject hit ? Tree.Provider(hit, Window) : null;
    }

    // Native layout HWNDs are implementation hosts, not additional copies of
    // the document. Their hit tests belong to the visible semantic fragment.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class WebUiaHostBridge(WebAccessibility tree, nint child, nint root) : StandardOleMarshalObject, IRawElementProviderFragmentRoot
    {
        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;
        public IRawElementProviderSimple? HostRawElementProvider => tree.Window != 0 ? AutomationInteropProvider.HostProviderFromHandle(child) : null;
        public IRawElementProviderFragmentRoot FragmentRoot => (IRawElementProviderFragmentRoot)tree.Provider(tree.Root, root);
        public System.Windows.Rect BoundingRectangle => System.Windows.Rect.Empty;
        public object? GetPatternProvider(int patternId) => null;
        public object? GetPropertyValue(int propertyId)
        {
            if (propertyId == AutomationElementIdentifiers.IsControlElementProperty.Id || propertyId == AutomationElementIdentifiers.IsContentElementProperty.Id ||
                propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id) return false;
            if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id) return ControlType.Pane.Id;
            if (propertyId == AutomationElementIdentifiers.NameProperty.Id) return "";
            return null;
        }
        public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction == NavigateDirection.Parent ? FragmentRoot : null;
        public int[]? GetRuntimeId() => null;
        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;
        public void SetFocus() => FragmentRoot.SetFocus();
        public IRawElementProviderFragment? GetFocus() => FragmentRoot.GetFocus();
        public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y) => FragmentRoot.ElementProviderFromPoint(x, y);
    }
}

// The native presenter owns the Windows semantic fragment. Embedding its HWND
// again through Avalonia's interop peer creates a second route to the same page.
internal sealed class WindowsWebViewHostPeer(NativeWebView owner) : Avalonia.Automation.Peers.ControlAutomationPeer(owner)
{
    protected override IReadOnlyList<Avalonia.Automation.Peers.AutomationPeer> GetOrCreateChildrenCore() => [];
    protected override bool IsControlElementCore() => false;
    protected override bool IsContentElementCore() => false;
}
#endif
