using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using SukiUI;
using SukiUI.Controls;
using SukiUI.Enums;

namespace DevDuo;

public sealed class MainWindow : SukiWindow
{
    private const double PreviewMargin = 24;
    // Keeps scroll bars reachable: the native view is never placed over them.
    private const double ScrollBarReserve = 14;
    private readonly AppLocalization language = AppLocalization.Current;
    private readonly PreferenceStore preferences = PreferenceStore.Default;
    private readonly DeviceProfile[] profiles = DeviceProfile.Load();
    private readonly SafariScene[] scenes = SafariScene.Load();
    // Material defaults from config/safari-shell.json; host preferences override only the four tunables.
    private readonly SafariContainerOptions settings = SafariContainerOptions.Load();
    private HostSettings host;
    private readonly HostAddressBar addressBar;
    private readonly HostSettingsPanel panel;
    private readonly HostStatusBar statusBar = new();
    private readonly Canvas shell = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(PreviewMargin) };
    private readonly ScrollViewer preview;
    private readonly Border previewSurface;
    // Scrolls with the device outline and marks where the screen is. The native view itself lives in
    // nativeLayer, above the scroll viewer, sized to the visible part of this slot.
    private readonly Border screenSlot = new();
    private readonly Canvas nativeLayer = new() { ClipToBounds = true };
    private readonly DispatcherTimer telemetryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<(NativeMenuItem Item, string Key)> menuItems = new();
    private NativeWebView? web;
    private ShellDrawing? drawing;
    // The developer-selected page without host-added calibration labels.
    private Uri page;
    private bool showingFixture;
    private int rotation;
    private bool ready, applyingSettings;
    private bool nativePlacementQueued;
    private double previewScale = 1;
    private string? savedFile, saveError, noticeKey;
    private Exception? previewError;
    private bool? saved;

    public MainWindow()
    {
        host = preferences.Host.Copy();
        var start = Environment.GetEnvironmentVariable("DEVDUO_URL") ?? host.LastUrl ?? CalibrationUrl.DefaultAddress;
        if (!CalibrationUrl.TryNormalize(start, out page))
        {
            page = new Uri(CalibrationUrl.DefaultAddress);
            noticeKey = "Error.Url";
        }

        Width = 1240;
        Height = 960;
        MinWidth = 900;
        MinHeight = 600;
        BackgroundStyle = SukiBackgroundStyle.Flat;
        // The animated shader background is unnecessary behind an opaque host and native browser child.
        BackgroundAnimationEnabled = false;
        BackgroundTransitionsEnabled = false;
        CanPin = false;

        addressBar = new HostAddressBar(language);
        panel = new HostSettingsPanel(language) { Width = 300, IsVisible = host.SettingsPanelExpanded };
        preview = new ScrollViewer
        {
            Content = shell,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        previewSurface = new Border { Classes = { "preview-surface" }, Child = preview };
        // Avalonia moves native children but does not clip them to scrolling ancestors, so a native view
        // inside the scroll viewer would draw over the toolbar and sidebar when scrolled.
        var previewArea = new Grid { Children = { previewSurface, nativeLayer } };
        Grid.SetColumn(previewArea, 1);
        // The settings column never overlaps the native browser child; both are separate grid cells.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { panel, previewArea } };
        Grid.SetRow(body, 1);
        Grid.SetRow(statusBar, 2);
        Content = new Grid { Classes = { "host-root" }, RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { addressBar, body, statusBar } };

        ApplyHostSettingsToControls();
        WireEvents();
        BuildNativeMenu();
        ApplyTheme();
        RefreshLanguage();
    }

    private void WireEvents()
    {
        addressBar.NavigateRequested += Navigate;
        addressBar.ReloadRequested += () => RunCommand(HostCommand.Reload);
        addressBar.StopRequested += () => web?.Stop();
        addressBar.Back.Click += (_, _) => RunCommand(HostCommand.Back);
        addressBar.Forward.Click += (_, _) => RunCommand(HostCommand.Forward);
        addressBar.SidebarToggle.Click += (_, _) => RunCommand(HostCommand.ToggleSettings);
        addressBar.ColorDemo.Click += (_, _) => ShowFixture();
        addressBar.Calibration.Click += (_, _) => Navigate(new Uri(CalibrationUrl.DefaultAddress));
        statusBar.Retry.Click += (_, _) => Navigate(page);

        // Native scene changes: a new native view is created at the current page.
        panel.Fold.SelectionChanged += _ => SettingChanged(() => Rebuild("Status.Rebuilt"));
        panel.Mode.SelectionChanged += _ => SettingChanged(() => Rebuild("Status.Rebuilt"));
        panel.Rotate.Click += (_, _) =>
        {
            rotation = (rotation + 1) % 4;
            Rebuild("Status.Rebuilt");
        };
        // Presentation changes keep the loaded page.
        panel.Scale.SelectionChanged += _ => SettingChanged(() =>
        {
            ApplyPreviewSize();
            RelabelCalibrationPage();
        });
        panel.Outline.IsCheckedChanged += (_, _) => SettingChanged(ApplyPreviewSize);
        panel.TabStrip.IsCheckedChanged += (_, _) => SettingChanged(Configure);
        foreach (var slider in new[] { panel.Tint, panel.Glass, panel.Shadow, panel.Radius })
        {
            slider.PropertyChanged += (_, args) =>
            {
                if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                    SettingChanged(Configure);
            };
        }
        panel.ResetMaterial.Click += (_, _) =>
        {
            host.TintAlpha = host.GlassStrength = host.ShadowOpacity = host.CornerRadius = null;
            ApplyHostSettingsToControls();
            Configure();
            QueueSave();
        };
        panel.ThemeChoice.SelectionChanged += _ => SettingChanged(ApplyTheme);
        foreach (var expander in new[] { panel.Material, panel.Diagnostics })
        {
            expander.PropertyChanged += (_, args) =>
            {
                if (args.Property == Expander.IsExpandedProperty && !applyingSettings)
                    QueueSave();
            };
        }
        panel.Language.SelectionChanged += (_, _) =>
        {
            if (applyingSettings)
                return;
            language.Select(panel.Language.SelectedIndex switch
            {
                1 => "en",
                2 => "zh-Hans",
                _ => "system"
            });
            RefreshLanguage();
            // Reconfigure labels in place: no navigation, new native view, scene changes or scroll reset.
            Configure();
        };
        panel.Save.Click += (_, _) => SaveState();
        panel.ResetSettings.Click += (_, _) => ResetSettings();

        preview.ScrollChanged += (_, _) => PositionNativeView();
        LayoutUpdated += (_, _) => PositionNativeView();
        preview.SizeChanged += (_, _) =>
        {
            if (panel.Scale.SelectedIndex == 1)
                resizeTimer.Start();
        };
        resizeTimer.Tick += (_, _) =>
        {
            resizeTimer.Stop();
            ApplyPreviewSize();
        };
        saveTimer.Tick += (_, _) => SavePreferences();
        telemetryTimer.Tick += (_, _) => UpdateTelemetry();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            ready = true;
            Rebuild(null);
            telemetryTimer.Start();
        };
        Closing += (_, _) =>
        {
            if (saveTimer.IsEnabled)
                SavePreferences();
        };
        Closed += (_, _) =>
        {
            telemetryTimer.Stop();
            resizeTimer.Stop();
        };
    }

    // Applies a control change unless it came from restoring settings, then persists the host settings.
    private void SettingChanged(Action apply)
    {
        if (applyingSettings || !ready)
            return;
        apply();
        QueueSave();
    }

    private void ApplyHostSettingsToControls()
    {
        applyingSettings = true;
        try
        {
            var fold = Array.FindIndex(profiles, profile => profile.Id == host.Fold);
            panel.Fold.Select(Math.Max(0, fold));
            panel.Mode.Select(host.ContainerMode == "app" ? 1 : 0);
            panel.Scale.Select(host.PreviewScale == "fit" ? 1 : 0);
            panel.ThemeChoice.Select(Array.IndexOf(HostSettings.Themes, host.Theme));
            panel.Outline.IsChecked = host.DeviceOutline;
            panel.TabStrip.IsChecked = host.ShowTabStrip;
            panel.Tint.Value = host.TintAlpha ?? settings.TintAlpha;
            panel.Glass.Value = host.GlassStrength ?? settings.GlassStrength;
            panel.Shadow.Value = host.ShadowOpacity ?? settings.ShadowOpacity;
            panel.Radius.Value = host.CornerRadius ?? settings.CornerRadius;
            panel.Material.IsExpanded = host.MaterialExpanded;
            panel.Diagnostics.IsExpanded = host.DiagnosticsExpanded;
            panel.IsVisible = host.SettingsPanelExpanded;
        }
        finally
        {
            applyingSettings = false;
        }
    }

    private void CollectHostSettings()
    {
        // Values equal to the configured defaults stay null so config changes still apply.
        double? Tuned(double value, double configured) => Math.Abs(value - configured) < 1e-9 ? null : value;
        host.Fold = profiles[Math.Max(0, panel.Fold.SelectedIndex)].Id;
        host.ContainerMode = HostSettings.ContainerModes[panel.Mode.SelectedIndex];
        host.PreviewScale = HostSettings.PreviewScales[panel.Scale.SelectedIndex];
        host.Theme = HostSettings.Themes[Math.Max(0, panel.ThemeChoice.SelectedIndex)];
        host.DeviceOutline = panel.Outline.IsChecked == true;
        host.ShowTabStrip = panel.TabStrip.IsChecked == true;
        host.TintAlpha = Tuned(panel.Tint.Value, settings.TintAlpha);
        host.GlassStrength = Tuned(panel.Glass.Value, settings.GlassStrength);
        host.ShadowOpacity = Tuned(panel.Shadow.Value, settings.ShadowOpacity);
        host.CornerRadius = Tuned(panel.Radius.Value, settings.CornerRadius);
        host.MaterialExpanded = panel.Material.IsExpanded;
        host.DiagnosticsExpanded = panel.Diagnostics.IsExpanded;
        host.SettingsPanelExpanded = panel.IsVisible;
    }

    private void QueueSave()
    {
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SavePreferences()
    {
        saveTimer.Stop();
        CollectHostSettings();
        preferences.SaveHost(host);
        RefreshPreferenceStatus();
    }

    // Restores the documented initial host state (Safari, Closed, body 0°, 1:1). Language and the current page stay.
    private void ResetSettings()
    {
        host = new HostSettings { LastUrl = host.LastUrl };
        rotation = 0;
        ApplyHostSettingsToControls();
        ApplyTheme();
        Rebuild("Status.Rebuilt");
        SavePreferences();
    }

    private void ApplyTheme()
    {
        var variant = panel.ThemeChoice.SelectedIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        if (variant == ThemeVariant.Default)
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        else
            SukiTheme.GetInstance().ChangeBaseTheme(variant);
    }

    private void BuildNativeMenu()
    {
        // macOS key equivalents reach the menu even while WKWebView is first responder.
        if (!OperatingSystem.IsMacOS())
            return;
        var navigate = new NativeMenu();
        void Add(string key, HostCommand command, Key gestureKey)
        {
            var item = new NativeMenuItem { Gesture = new KeyGesture(gestureKey, KeyModifiers.Meta) };
            item.Click += (_, _) => RunCommand(command);
            navigate.Items.Add(item);
            menuItems.Add((item, key));
        }
        Add("Menu.FocusAddress", HostCommand.FocusAddress, Key.L);
        Add("Nav.Reload", HostCommand.Reload, Key.R);
        Add("Nav.Back", HostCommand.Back, Key.OemOpenBrackets);
        Add("Nav.Forward", HostCommand.Forward, Key.OemCloseBrackets);
        Add("Control.Save", HostCommand.SaveState, Key.S);
        Add("Nav.ToggleSettings", HostCommand.ToggleSettings, Key.OemPipe);
        var root = new NativeMenuItem { Menu = navigate };
        menuItems.Add((root, "Menu.Navigate"));
        NativeMenu.SetMenu(this, new NativeMenu { Items = { root } });
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        var commandKey = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        HostCommand? command = (args.Key, args.KeyModifiers) switch
        {
            (Key.L, var modifiers) when modifiers == commandKey => HostCommand.FocusAddress,
            (Key.R, var modifiers) when modifiers == commandKey => HostCommand.Reload,
            (Key.F5, KeyModifiers.None) => HostCommand.Reload,
            (Key.S, var modifiers) when modifiers == commandKey => HostCommand.SaveState,
            (Key.OemPipe, var modifiers) when modifiers == commandKey => HostCommand.ToggleSettings,
            (Key.OemOpenBrackets, KeyModifiers.Meta) when OperatingSystem.IsMacOS() => HostCommand.Back,
            (Key.OemCloseBrackets, KeyModifiers.Meta) when OperatingSystem.IsMacOS() => HostCommand.Forward,
            (Key.Left, KeyModifiers.Alt) when !OperatingSystem.IsMacOS() => HostCommand.Back,
            (Key.Right, KeyModifiers.Alt) when !OperatingSystem.IsMacOS() => HostCommand.Forward,
            _ => null
        };
        if (command is { } value)
        {
            args.Handled = true;
            RunCommand(value);
        }
        else if (args.Key == Key.Escape && web?.Navigation.IsLoading == true && args.Source is not TextBox)
        {
            args.Handled = true;
            web.Stop();
        }
    }

    private void RunCommand(HostCommand command)
    {
        switch (command)
        {
            case HostCommand.FocusAddress:
                if (!IsActive)
                    Activate();
                addressBar.FocusAddress();
                break;
            case HostCommand.Reload:
                noticeKey = null;
                if (showingFixture)
                    web?.ShowColorFixture();
                else if (web?.Navigation.Error is not null)
                    Navigate(page); // A failed provisional load has no committed page to reload.
                else
                    web?.Reload();
                break;
            case HostCommand.Back:
                noticeKey = null;
                web?.GoBack();
                break;
            case HostCommand.Forward:
                noticeKey = null;
                web?.GoForward();
                break;
            case HostCommand.SaveState:
                SaveState();
                break;
            case HostCommand.ToggleSettings:
                panel.IsVisible = !panel.IsVisible;
                QueueSave();
                break;
        }
    }

    private void RefreshLanguage()
    {
        applyingSettings = true;
        try
        {
            Title = language.Text("Window.Title");
            panel.Fold.SetItems(profiles.Select(profile =>
                AppLocalization.Catalog("en").ContainsKey("Device." + profile.Id)
                    ? language.Text("Device." + profile.Id)
                    : profile.Label).ToArray());
            panel.Mode.SetItems([language.Text("Mode.Safari"), language.Text("Mode.App")]);
            panel.Scale.SetItems([language.Text("Sizing.Native"), language.Text("Sizing.Fit")]);
            panel.ThemeChoice.SetItems([language.Text("Theme.System"), language.Text("Theme.Light"), language.Text("Theme.Dark")]);
            panel.Language.ItemsSource = new[] { language.Text("Language.System"), "English", "简体中文" };
            panel.Language.SelectedIndex = language.Preference switch
            {
                "en" => 1,
                "zh-Hans" => 2,
                _ => 0
            };
            AutomationProperties.SetName(previewSurface, language.Text("Preview.Name"));
            AutomationProperties.SetName(panel.Fold, language.Text("Control.Fold"));
            AutomationProperties.SetName(panel.Mode, language.Text("Control.Mode"));
            AutomationProperties.SetName(panel.Scale, language.Text("Control.Sizing"));
            AutomationProperties.SetName(panel.ThemeChoice, language.Text("Control.Theme"));
            AutomationProperties.SetName(panel.Language, language.Text("Language.Label"));
            panel.Rotate.Content = language.Text("Control.Rotate");
            AutomationProperties.SetName(panel.Rotate, language.Text("Control.Rotate"));
            panel.Save.Content = language.Text("Control.Save");
            ToolTip.SetTip(panel.Save, language.Format("Shortcut.Format", language.Text("Control.Save"), HostAddressBar.Shortcut("⌘S", "Ctrl+S")));
            panel.Localize();
            addressBar.Localize();
            foreach (var (item, key) in menuItems)
                item.Header = language.Text(key);
            RefreshSceneDescription();
            RefreshSaveStatus();
            RefreshPreferenceStatus();
            UpdateNavigationUi();
            UpdateTelemetry();
        }
        finally
        {
            applyingSettings = false;
        }
    }

    private SafariScene CurrentScene() => scenes.Single(scene =>
        scene.Fold == profiles[Math.Max(0, panel.Fold.SelectedIndex)].Id && scene.BodyRotationDegrees == rotation * 90);

    private DeviceProfile CurrentProfile()
    {
        var profile = profiles[Math.Max(0, panel.Fold.SelectedIndex)];
        for (var count = 0; count < CurrentScene().SceneRotationDegrees / 90; count++)
            profile = profile.RotateClockwise();
        return profile;
    }

    private bool SafariMode => panel.Mode.SelectedIndex == 0;

    private SafariContainerOptions CurrentOptions()
    {
        var profile = CurrentProfile();
        var options = settings.Copy();
        options.Mode = SafariMode ? "safari" : "app";
        options.ChromeLayout = profile.Id == "closed" ? "side" : "top";
        options.RotationClockwiseDegrees = profile.RotationClockwiseDegrees;
        options.ReferenceScene = CurrentScene();
        options.ShowTabStrip = panel.TabStrip.IsChecked == true;
        options.LogicalWidth = profile.ScreenWidthCssPx;
        options.LogicalHeight = profile.ScreenHeightCssPx;
        options.TintAlpha = panel.Tint.Value;
        options.GlassStrength = panel.Glass.Value;
        options.ShadowOpacity = panel.Shadow.Value;
        options.CornerRadius = panel.Radius.Value;
        options.UiLanguage = language.Language;
        options.UiStrings = language.NativeStrings();
        return options;
    }

    private CalibrationLabels Labels()
    {
        var profile = CurrentProfile();
        return new CalibrationLabels(
            profile.Id,
            OperatingSystem.IsMacOS() ? "macOS-WKWebView" : "Windows-WebView2",
            SafariMode ? "safari-shell-approx" : "app-webview",
            "body-clockwise-" + rotation * 90 + "-scene-" + profile.RotationClockwiseDegrees + "-" +
                (panel.Scale.SelectedIndex == 0 ? "1to1" : "scaled-preview"));
    }

    private string TargetUrl() => CalibrationUrl.ApplyLabels(page, Labels()).AbsoluteUri;

    private void Navigate(Uri target)
    {
        page = target;
        showingFixture = false;
        noticeKey = null;
        if (web is null)
            Rebuild(null);
        else
            web.Navigate(TargetUrl());
        UpdateNavigationUi();
    }

    private void ShowFixture()
    {
        showingFixture = true;
        noticeKey = null;
        if (web is null)
            Rebuild(null);
        else
            web.ShowColorFixture();
    }

    // The calibration page records its query labels; keep them consistent with the preview scale.
    private void RelabelCalibrationPage()
    {
        if (web is null || showingFixture || !CalibrationUrl.IsCalibrationPage(page))
            return;
        web.Navigate(TargetUrl());
        noticeKey = "Status.CalibrationRelabelled";
        UpdateNavigationUi();
    }

    private void Configure()
    {
        if (!ready)
            return;
        RefreshSceneDescription();
        web?.Configure(CurrentOptions());
    }

    // Recreates the native container for a different device scene or container mode. The new view
    // opens the current page (or color laboratory); page scroll and history start over.
    private void Rebuild(string? notice)
    {
        if (!ready)
            return;
        try
        {
            var profile = CurrentProfile();
            if (web is not null)
            {
                web.NavigationChanged -= OnNavigationChanged;
                web.CommandRequested -= RunCommand;
            }
            shell.Children.Clear();
            shell.Children.Add(screenSlot);
            drawing = null;
            nativeLayer.Children.Clear();
            web = new NativeWebView(profile, TargetUrl(), CurrentOptions()) { ShowFixtureOnCreate = showingFixture };
            web.NavigationChanged += OnNavigationChanged;
            web.CommandRequested += RunCommand;
            nativeLayer.Children.Add(web);
            ApplyPreviewSize();
            RefreshSceneDescription();
            previewError = null;
            noticeKey = notice;
        }
        catch (Exception error)
        {
            previewError = error;
        }
        UpdateNavigationUi();
    }

    // Presentation-only sizing. Moves and resizes the existing native view; it is never detached,
    // so the loaded page, its history and scroll position are kept. Logical CSS dimensions do not change.
    private void ApplyPreviewSize()
    {
        if (web is null)
            return;
        var profile = CurrentProfile();
        var outline = panel.Outline.IsChecked == true;
        var xToCss = profile.ScreenWidthCssPx / profile.ScreenCanvasWidth;
        var yToCss = profile.ScreenHeightCssPx / profile.ScreenCanvasHeight;
        var naturalWidth = outline ? profile.ShellCanvasWidth * xToCss : profile.ScreenWidthCssPx;
        var naturalHeight = outline ? profile.ShellCanvasHeight * yToCss : profile.ScreenHeightCssPx;
        previewScale = panel.Scale.SelectedIndex == 0 ? 1 : FitScale(naturalWidth, naturalHeight);
        var scaleX = xToCss * previewScale;
        var scaleY = yToCss * previewScale;
        shell.Width = naturalWidth * previewScale;
        shell.Height = naturalHeight * previewScale;
        if (drawing is not null)
            shell.Children.Remove(drawing);
        drawing = outline ? new ShellDrawing(profile, scaleX, scaleY) { Width = shell.Width, Height = shell.Height } : null;
        if (drawing is not null)
            shell.Children.Insert(0, drawing);
        screenSlot.Width = profile.ScreenWidthCssPx * previewScale;
        screenSlot.Height = profile.ScreenHeightCssPx * previewScale;
        Canvas.SetLeft(screenSlot, outline ? profile.ScreenX * scaleX : 0);
        Canvas.SetTop(screenSlot, outline ? profile.ScreenY * scaleY : 0);
        if (!web.HasViewport)
        {
            // A new native container starts at full size so the page never lays out at a placeholder size;
            // PositionNativeView then clips it to the visible area after layout.
            web.Width = screenSlot.Width;
            web.Height = screenSlot.Height;
            web.SetViewport(screenSlot.Width, screenSlot.Height, 0, 0, RenderScaling);
        }
        UpdateTelemetry();
    }

    // Shows only the part of the screen slot inside the scroll viewport. The native container keeps its
    // full size and scene scale; the clip view offsets it, so CSS viewport and telemetry are unchanged.
    private void PositionNativeView()
    {
        if (web is null || double.IsNaN(screenSlot.Width) || screenSlot.TranslatePoint(default, nativeLayer) is not { } origin)
            return;
        var full = new Rect(origin, new Size(screenSlot.Width, screenSlot.Height));
        var visibleWidth = preview.Viewport.Width - (preview.Extent.Height > preview.Viewport.Height ? ScrollBarReserve : 0);
        var visibleHeight = preview.Viewport.Height - (preview.Extent.Width > preview.Viewport.Width ? ScrollBarReserve : 0);
        var visible = full.Intersect(new Rect(0, 0, Math.Max(0, visibleWidth), Math.Max(0, visibleHeight)));
        web.IsVisible = visible.Width >= 1 && visible.Height >= 1;
        if (!web.IsVisible)
            return;
        Canvas.SetLeft(web, visible.X);
        Canvas.SetTop(web, visible.Y);
        web.Width = visible.Width;
        web.Height = visible.Height;
        web.SetViewport(full.Width, full.Height, visible.X - full.X, visible.Y - full.Y, RenderScaling);
        if (!nativePlacementQueued)
        {
            nativePlacementQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                nativePlacementQueued = false;
                web?.SynchronizePlacement();
            }, DispatcherPriority.Loaded);
        }
    }

    // Fit scales presentation to the available preview area, never above 1:1.
    private double FitScale(double width, double height)
    {
        var available = preview.Bounds.Size;
        if (available.Width <= 0 || available.Height <= 0)
            return Math.Min(1, 600 / height);
        var scale = Math.Min((available.Width - 2 * PreviewMargin) / width, (available.Height - 2 * PreviewMargin) / height);
        return Math.Clamp(scale, .2, 1);
    }

    private void RefreshSceneDescription()
    {
        var scene = CurrentScene();
        panel.Angle.Text = language.Format("Device.Angle", rotation * 90, scene.Id);
        // Closed 180° is the observed clockwise-sequence result, not a mechanical rotation.
        panel.RotationNote.Text = scene.Fold == "closed" && scene.BodyRotationDegrees == 180 ? language.Text("Device.HistoryNote") : "";
        panel.RotationNote.IsVisible = panel.RotationNote.Text.Length > 0;
        var tabStripApplies = CurrentProfile().Id == "open" && SafariMode;
        panel.TabStrip.IsEnabled = tabStripApplies;
        panel.TabStripHint.Text = language.Text("Control.TabStripHint");
        panel.TabStripHint.IsVisible = !tabStripApplies;
        AutomationProperties.SetHelpText(panel.TabStrip, tabStripApplies ? null : language.Text("Control.TabStripHint"));
    }

    private void OnNavigationChanged(NavigationState state)
    {
        if (state.IsLocalFixture)
        {
            showingFixture = true;
        }
        else if (!state.IsLoading && state.Error is null && CommittedWebAddress(state) is { } committed)
        {
            // Redirects and in-page navigation update the page that rebuilds and Retry reopen.
            page = CalibrationUrl.RemoveLabels(committed);
            showingFixture = false;
            if (host.LastUrl != page.AbsoluteUri)
            {
                host.LastUrl = page.AbsoluteUri;
                QueueSave();
            }
        }
        UpdateNavigationUi();
    }

    private string DisplayAddress(NavigationState state)
    {
        if (showingFixture)
            return language.Text("Address.Fixture");
        // A failed load keeps the requested address visible for correction or Retry.
        if (state.Error is not null)
            return page.AbsoluteUri;
        return CommittedWebAddress(state) is { } committed ? CalibrationUrl.RemoveLabels(committed).AbsoluteUri : page.AbsoluteUri;
    }

    private static Uri? CommittedWebAddress(NavigationState state) =>
        Uri.TryCreate(state.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    private void UpdateNavigationUi()
    {
        var state = web?.Navigation ?? NavigationState.Empty;
        addressBar.Update(state, DisplayAddress(state));
        Title = string.IsNullOrWhiteSpace(state.Title) || showingFixture
            ? language.Text("Window.Title")
            : state.Title + " · DevDuo";
        var retry = language.Text("Status.Retry");
        if (previewError is not null)
            statusBar.Show(language.Format("Status.Error", previewError.Message), StatusTone.Error, false, retry);
        else if (state.Error is { } error)
            statusBar.Show(language.Format("Status.LoadFailed", error.Message.Length > 0 ? error.Message : error.Code), StatusTone.Error, !showingFixture, retry);
        else if (state.IsLoading)
            statusBar.Show(language.Format("Status.LoadingPage", showingFixture ? language.Text("Control.Fixture") : page.Host), StatusTone.Normal, false, retry);
        else if (noticeKey is not null)
            statusBar.Show(language.Text(noticeKey), noticeKey.StartsWith("Error.") ? StatusTone.Error : StatusTone.Normal, false, retry);
        else if (state.Url is null)
            statusBar.Show(language.Text("Status.Loading"), StatusTone.Normal, false, retry);
        else
            statusBar.Show(language.Format("Status.PageLoaded", string.IsNullOrWhiteSpace(state.Title) ? DisplayAddress(state) : state.Title),
                StatusTone.Success, false, retry);
    }

    private string DisplayValue(string? value)
    {
        if (value is null)
            return "";
        var key = "Telemetry." + value;
        return AppLocalization.Catalog("en").ContainsKey(key) ? language.Text(key) : value;
    }

    // Reads native telemetry for the Diagnostics panel and the status-bar viewport summary.
    private void UpdateTelemetry()
    {
        var scale = panel.Scale.SelectedIndex == 0
            ? language.Text("Status.ScaleNative")
            : language.Format("Status.ScaleFit", previewScale);
        try
        {
            using var document = JsonDocument.Parse(web?.ReadState() ?? "{}");
            var root = document.RootElement;
            if (!root.TryGetProperty("containerMode", out var selected))
            {
                panel.Measurements.Text = language.Text("Status.Loading");
                statusBar.SetViewport(language.Format("Status.ViewportPending", CurrentScene().Id, scale));
                return;
            }
            var frame = root.GetProperty("nativeWebViewFrameDip");
            var viewport = root.GetProperty("actualPageViewport");
            var reported = viewport.TryGetProperty("width", out var width) && viewport.TryGetProperty("height", out var height)
                ? language.Format("Status.Size", width.GetDouble(), height.GetDouble())
                : null;
            var pageSize = reported ?? language.Text("Status.LoadingSize");
            statusBar.SetViewport(reported is not null
                ? language.Format("Status.Viewport", reported, CurrentScene().Id, scale)
                : language.Format("Status.ViewportPending", CurrentScene().Id, scale));
            panel.Measurements.Text = language.Format("Status.Measurements",
                DisplayValue(selected.GetString()),
                DisplayValue(root.GetProperty("chromeState").GetString()),
                pageSize,
                frame.GetProperty("width").GetDouble(),
                frame.GetProperty("height").GetDouble(),
                rotation * 90,
                CurrentProfile().RotationClockwiseDegrees,
                DisplayValue(root.GetProperty("tintSource").GetString()),
                DisplayValue(root.GetProperty("materialBackend").GetString()));
        }
        catch (Exception error)
        {
            panel.Measurements.Text = language.Format("Status.ReadError", error.Message);
        }
    }

    private void RefreshSaveStatus()
    {
        panel.Save.Content = language.Text(saved switch
        {
            true => "Control.Saved",
            false => "Control.SaveFailed",
            _ => "Control.Save"
        });
        panel.SaveStatus.Classes.Set("error", saveError is not null);
        panel.SaveStatus.Text = saveError is not null
            ? language.Format("Status.SaveError", saveError)
            : savedFile is not null ? language.Format("Status.Saved", savedFile) : "";
        panel.SaveStatus.IsVisible = panel.SaveStatus.Text.Length > 0;
    }

    private void RefreshPreferenceStatus()
    {
        string? message = null;
        if (language.PreferenceError is not null)
            message = language.Format(language.PreferenceErrorOnRead ? "Status.PreferenceReadError" : "Status.PreferenceError", language.PreferenceError);
        else if (preferences.WriteError is not null)
            message = language.Format("Status.HostPreferenceError", preferences.WriteError);
        else if (preferences.InvalidHostFields.Count > 0)
            message = language.Format("Status.HostPreferenceInvalid", string.Join(", ", preferences.InvalidHostFields));
        panel.PreferenceStatus.Text = message ?? "";
        panel.PreferenceStatus.Classes.Set("error", message is not null);
        panel.PreferenceStatus.IsVisible = message is not null;
    }

    private static string DiagnosticOutputDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("DEVDUO_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "DevDuo.sln")))
                return Path.Combine(folder.FullName, "captures", "desktop-validation", "container-modes");
            folder = folder.Parent;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevDuo", "captures");
    }

    private void SaveState()
    {
        try
        {
            var output = DiagnosticOutputDirectory();
            Directory.CreateDirectory(output);
            var file = Path.Combine(output,
                "state-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture) + ".json");
            using var native = JsonDocument.Parse(web?.ReadState() ?? "{}");
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                savedAt = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                requestedProfile = CurrentProfile().Id,
                requestedRotation = rotation * 90,
                referenceScene = CurrentScene().Id,
                sceneRotation = CurrentProfile().RotationClockwiseDegrees,
                showTabStrip = panel.TabStrip.IsChecked == true,
                deviceOutline = panel.Outline.IsChecked == true,
                scaleMode = panel.Scale.SelectedIndex == 0 ? "native-1to1" : "scaled-preview",
                // Actual host presentation scale; Fit follows the available preview area.
                previewScale,
                nativeContainer = native.RootElement,
                unknownIosOptics = new
                {
                    blurKernel = (string?)null,
                    refractionIndex = (double?)null
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
            savedFile = Path.GetFileName(file);
            saveError = null;
            saved = true;
        }
        catch (Exception error)
        {
            saveError = error.Message;
            saved = false;
        }
        RefreshSaveStatus();
    }
}
