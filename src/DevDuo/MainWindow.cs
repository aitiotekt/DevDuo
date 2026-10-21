using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DevDuo;

public sealed class MainWindow : Window
{
    private readonly AppLocalization language = AppLocalization.Current;
    private readonly List<Action> localize = new();
    private readonly ComboBox devices, sizing, mode, languages;
    private readonly CheckBox deviceOutline, tabStrip;
    private readonly TextBox url;
    private readonly TextBlock status, saveStatus;
    private readonly Button save;
    private readonly Canvas shell;
    private readonly Slider tint, glass, shadow, radius;
    private readonly DeviceProfile[] profiles = DeviceProfile.Load();
    private readonly SafariScene[] scenes = SafariScene.Load();
    private readonly SafariContainerOptions settings = SafariContainerOptions.Load();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private NativeWebView? web;
    private int rotation;
    private bool ready, refreshingLanguage;
    private string? savedFile, saveError;
    private Exception? previewError;
    private bool? saved;

    public MainWindow()
    {
        Width = 1180;
        Height = 970;
        MinWidth = 880;
        MinHeight = 650;
        Background = Brush.Parse("#E8EDF4");
        devices = new ComboBox { SelectedIndex = 0, Width = 230 };
        sizing = new ComboBox { SelectedIndex = 0, Width = 145 };
        mode = new ComboBox { SelectedIndex = 0, Width = 235 };
        deviceOutline = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        localize.Add(() => deviceOutline.Content = language.Text("Control.Outline"));
        var rotate = LocalizedButton("Control.Rotate", () =>
        {
            rotation = (rotation + 1) % 4;
            Rebuild();
        });
        var selectors = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 14, 18, 8)
        };
        foreach (var control in new Control[] { mode, devices, sizing, rotate, deviceOutline })
        {
            control.Margin = new Thickness(0, 0, 10, 6);
            selectors.Children.Add(control);
        }

        url = new TextBox
        {
            Text = Environment.GetEnvironmentVariable("DEVDUO_URL") ?? "http://127.0.0.1:8765/",
            MinWidth = 200
        };
        localize.Add(() =>
        {
            url.Watermark = language.Text("Control.Url");
            AutomationProperties.SetName(url, language.Text("Control.Url"));
        });
        var navigate = LocalizedButton("Control.Open", Rebuild);
        var reload = LocalizedButton("Control.Reload", () => RunNavigation(() => web?.Navigate(BuildUrl())));
        var fixture = LocalizedButton("Control.Fixture", () => RunNavigation(() => web?.ShowColorFixture()));
        tabStrip = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        localize.Add(() => tabStrip.Content = language.Text("Control.TabStrip"));
        save = LocalizedButton("Control.Save", SaveState);
        var navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(18, 0, 18, 8)
        };
        AddColumns(navigation, [url, navigate, reload, fixture, tabStrip, save]);

        languages = new ComboBox { Width = 160 };
        var languageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        localize.Add(() =>
        {
            languageLabel.Text = language.Text("Language.Label");
            AutomationProperties.SetName(languages, languageLabel.Text);
        });
        var languageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(18, 0, 18, 8),
            Children = { languageLabel, languages }
        };
        languages.SelectionChanged += (_, _) =>
        {
            if (refreshingLanguage)
                return;
            language.Select(languages.SelectedIndex switch
            {
                1 => "en",
                2 => "zh-Hans",
                _ => "system"
            });
            RefreshLanguage();
            // Reconfigure labels in place: no navigation, new native view, scene changes or scroll reset.
            if (ready)
                web?.Configure(CurrentOptions());
        };

        tint = MakeSlider(0, 1, settings.TintAlpha);
        glass = MakeSlider(0, 1, settings.GlassStrength);
        shadow = MakeSlider(0, .5, settings.ShadowOpacity);
        radius = MakeSlider(0, 40, settings.CornerRadius);
        var tuning = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8) };
        foreach (var control in new[]
        {
            Tuning("Material.Tint", tint),
            Tuning("Material.Glass", glass),
            Tuning("Material.Shadow", shadow),
            Tuning("Material.Radius", radius)
        })
        {
            control.Margin = new Thickness(0, 0, 18, 8);
            tuning.Children.Add(control);
        }
        saveStatus = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, TextWrapping = TextWrapping.Wrap };
        tuning.Children.Add(saveStatus);
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 0, 18, 8), FontSize = 12, MinHeight = 44 };
        shell = new Canvas { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var body = new ScrollViewer
        {
            Content = shell,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var materialSettings = new Expander { Content = tuning, IsExpanded = false, Margin = new Thickness(18, 0, 18, 4) };
        localize.Add(() => materialSettings.Header = language.Text("Material.Header"));
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*") };
        var rows = new Control[] { selectors, navigation, languageRow, materialSettings, status, body };
        for (var index = 0; index < rows.Length; index++)
        {
            Grid.SetRow(rows[index], index);
            grid.Children.Add(rows[index]);
        }
        Content = grid;

        devices.SelectionChanged += (_, _) =>
        {
            if (ready && !refreshingLanguage)
                Rebuild();
        };
        sizing.SelectionChanged += (_, _) =>
        {
            if (ready && !refreshingLanguage)
                Rebuild();
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (ready && !refreshingLanguage)
                Rebuild();
        };
        tabStrip.IsCheckedChanged += (_, _) =>
        {
            if (ready)
                web?.Configure(CurrentOptions());
        };
        deviceOutline.IsCheckedChanged += (_, _) =>
        {
            if (ready)
                Rebuild();
        };
        foreach (var slider in new[] { tint, glass, shadow, radius })
        {
            slider.PropertyChanged += (_, args) =>
            {
                if (ready && args.Property == Slider.ValueProperty)
                    web?.Configure(CurrentOptions());
            };
        }
        timer.Tick += (_, _) => UpdateStatus();
        Opened += (_, _) =>
        {
            ready = true;
            Rebuild();
            timer.Start();
        };
        Closed += (_, _) => timer.Stop();
        RefreshLanguage();
    }

    private Button LocalizedButton(string key, Action action)
    {
        var button = new Button();
        localize.Add(() => button.Content = language.Text(key));
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddColumns(Grid grid, Control[] controls)
    {
        for (var index = 0; index < controls.Length; index++)
        {
            Grid.SetColumn(controls[index], index);
            grid.Children.Add(controls[index]);
        }
    }

    private static Slider MakeSlider(double min, double max, double value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = 140
    };

    private StackPanel Tuning(string key, Slider slider)
    {
        var label = new TextBlock { FontSize = 11 };
        localize.Add(() =>
        {
            label.Text = language.Text(key);
            AutomationProperties.SetName(slider, label.Text);
        });
        return new StackPanel { Spacing = 1, Children = { label, slider } };
    }

    private void RefreshLanguage()
    {
        refreshingLanguage = true;
        try
        {
            Title = language.Text("Window.Title");
            void Items(ComboBox control, string[] values)
            {
                var selected = Math.Max(0, control.SelectedIndex);
                control.ItemsSource = values;
                control.SelectedIndex = selected;
            }
            Items(devices, profiles.Select(profile =>
                AppLocalization.Catalog("en").ContainsKey("Device." + profile.Id)
                    ? language.Text("Device." + profile.Id)
                    : profile.Label).ToArray());
            Items(sizing, [language.Text("Sizing.Native"), language.Text("Sizing.Fit")]);
            Items(mode, [language.Text("Mode.Safari"), language.Text("Mode.App")]);
            languages.ItemsSource = new[] { language.Text("Language.System"), "English", "简体中文" };
            languages.SelectedIndex = language.Preference switch
            {
                "en" => 1,
                "zh-Hans" => 2,
                _ => 0
            };
            foreach (var update in localize)
                update();
            AutomationProperties.SetName(devices, language.Text("Control.Device"));
            AutomationProperties.SetName(sizing, language.Text("Control.Sizing"));
            AutomationProperties.SetName(mode, language.Text("Control.Mode"));
            RefreshSaveStatus();
            UpdateStatus();
        }
        finally
        {
            refreshingLanguage = false;
        }
    }

    private SafariScene CurrentScene() => scenes.Single(scene =>
        scene.Fold == profiles[Math.Max(0, devices.SelectedIndex)].Id && scene.BodyRotationDegrees == rotation * 90);

    private DeviceProfile CurrentProfile()
    {
        var profile = profiles[Math.Max(0, devices.SelectedIndex)];
        for (var count = 0; count < CurrentScene().SceneRotationDegrees / 90; count++)
            profile = profile.RotateClockwise();
        return profile;
    }

    private SafariContainerOptions CurrentOptions()
    {
        var profile = CurrentProfile();
        var options = settings.Copy();
        options.Mode = mode.SelectedIndex == 0 ? "safari" : "app";
        options.ChromeLayout = profile.Id == "closed" ? "side" : "top";
        options.RotationClockwiseDegrees = profile.RotationClockwiseDegrees;
        options.ReferenceScene = CurrentScene();
        options.ShowTabStrip = tabStrip.IsChecked == true;
        options.LogicalWidth = profile.ScreenWidthCssPx;
        options.LogicalHeight = profile.ScreenHeightCssPx;
        options.TintAlpha = tint.Value;
        options.GlassStrength = glass.Value;
        options.ShadowOpacity = shadow.Value;
        options.CornerRadius = radius.Value;
        options.UiLanguage = language.Language;
        options.UiStrings = language.NativeStrings();
        return options;
    }

    private string BuildUrl()
    {
        var value = url.Text?.Trim() ?? "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new AppMessageException("Error.Url");
        var profile = CurrentProfile();
        var join = value.Contains('?') ? '&' : '?';
        return value + join + "device=iPhone%20Duo&pose=" + Uri.EscapeDataString(profile.Id) + "&browser=" +
            (OperatingSystem.IsMacOS() ? "macOS-WKWebView" : "Windows-WebView2") + "&state=initial&displayMode=" +
            (mode.SelectedIndex == 0 ? "safari-shell-approx" : "app-webview") + "&notes=body-clockwise-" + rotation * 90 +
            "-scene-" + profile.RotationClockwiseDegrees + "-" + (sizing.SelectedIndex == 0 ? "1to1" : "scaled-preview");
    }

    private void RunNavigation(Action navigate)
    {
        try
        {
            navigate();
            previewError = null;
            UpdateStatus();
        }
        catch (Exception error)
        {
            previewError = error;
            UpdateStatus();
        }
    }

    private void Rebuild()
    {
        if (!ready)
            return;
        try
        {
            var target = BuildUrl(); // Validate before replacing an already loaded native view.
            var profile = CurrentProfile();
            tabStrip.IsEnabled = profile.Id == "open" && mode.SelectedIndex == 0;
            var outline = deviceOutline.IsChecked == true;
            var xToCss = profile.ScreenWidthCssPx / profile.ScreenCanvasWidth;
            var yToCss = profile.ScreenHeightCssPx / profile.ScreenCanvasHeight;
            var scale = sizing.SelectedIndex == 0 ? 1 : Math.Min(1, 600 / (profile.ShellCanvasHeight * yToCss));
            var scaleX = xToCss * scale;
            var scaleY = yToCss * scale;
            var width = profile.ScreenWidthCssPx * scale;
            var height = profile.ScreenHeightCssPx * scale;
            shell.Children.Clear();
            web = null;
            shell.Width = outline ? profile.ShellCanvasWidth * scaleX : width;
            shell.Height = outline ? profile.ShellCanvasHeight * scaleY : height;
            if (outline)
                shell.Children.Add(new ShellDrawing(profile, scaleX, scaleY) { Width = shell.Width, Height = shell.Height });
            web = new NativeWebView(profile, target, CurrentOptions()) { Width = width, Height = height };
            Canvas.SetLeft(web, outline ? profile.ScreenX * scaleX : 0);
            Canvas.SetTop(web, outline ? profile.ScreenY * scaleY : 0);
            shell.Children.Add(web);
            previewError = null;
            UpdateStatus();
        }
        catch (Exception error)
        {
            previewError = error;
            UpdateStatus();
        }
    }

    private string DisplayValue(string? value)
    {
        if (value is null)
            return "";
        var key = "Telemetry." + value;
        return AppLocalization.Catalog("en").ContainsKey(key) ? language.Text(key) : value;
    }

    private void UpdateStatus()
    {
        if (previewError is not null)
        {
            status.Text = language.Format("Status.Error", previewError.Message);
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(web?.ReadState() ?? "{}");
            var root = document.RootElement;
            if (!root.TryGetProperty("containerMode", out var selected))
            {
                status.Text = language.Text("Status.Loading");
                return;
            }
            var frame = root.GetProperty("nativeWebViewFrameDip");
            var page = root.GetProperty("actualPageViewport");
            var pageSize = page.TryGetProperty("width", out var width) && page.TryGetProperty("height", out var height)
                ? language.Format("Status.Size", width.GetDouble(), height.GetDouble())
                : language.Text("Status.LoadingSize");
            status.Text = language.Format("Status.Measurements",
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
            status.Text = language.Format("Status.ReadError", error.Message);
        }
    }

    private void RefreshSaveStatus()
    {
        save.Content = language.Text(saved switch
        {
            true => "Control.Saved",
            false => "Control.SaveFailed",
            _ => "Control.Save"
        });
        if (language.PreferenceError is not null)
        {
            saveStatus.Text = language.Format(
                language.PreferenceErrorOnRead ? "Status.PreferenceReadError" : "Status.PreferenceError",
                language.PreferenceError);
        }
        else if (saveError is not null)
        {
            saveStatus.Text = language.Format("Status.SaveError", saveError);
        }
        else if (savedFile is not null)
        {
            saveStatus.Text = language.Format("Status.Saved", savedFile);
        }
        else
        {
            saveStatus.Text = language.Text("Material.Note");
        }
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
                showTabStrip = tabStrip.IsChecked == true,
                deviceOutline = deviceOutline.IsChecked == true,
                scaleMode = sizing.SelectedIndex == 0 ? "native-1to1" : "scaled-preview",
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
