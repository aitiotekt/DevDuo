using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.Styling;
using SukiUI;
using DevDuo;

namespace DevDuo.Windows.Tests;

public sealed class RuntimeTestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new SukiTheme());
        Styles.Add(new StyleInclude(new Uri("avares://DevDuo/")) { Source = new Uri("avares://DevDuo/Styles/DevDuoStyles.axaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow();
        if (Program.NavigationLatencyInline)
        {
            window.GetLogicalDescendants().OfType<HostSettingsPanel>().Single().ColorDemo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // Rebuild before native attachment so the first load is the authored
            // inline fixture, with no canceled HTTP predecessor.
            var mode = window.GetLogicalDescendants().OfType<HostSettingsPanel>().Single().Mode;
            mode.Children.OfType<RadioButton>().ElementAt(1).IsChecked = true;
            mode.Children.OfType<RadioButton>().ElementAt(0).IsChecked = true;
        }
        if (Program.Presented)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(0, 0);
            var content = (Control)window.Content!;
            window.Content = null;
            var capture = new Button { Content = "Capture observation" };
            var finish = new Button { Content = "Finish native acceptance" };
            var nextPage = new Button { Content = "Next page" };
            var nextFit = new Button { Content = "Next Fit" };
            var glass = new Button { Content = "Glass 0 / 1" };
            var nextScene = new Button { Content = "Next scene" };
            var nextAppearance = new Button { Content = "Appearance Light / Dark" };
            nextAppearance.Click += (_, _) =>
            {
                var settings = window.GetVisualDescendants().OfType<HostSettingsPanel>().Single();
                settings.ThemeChoice.Children.OfType<RadioButton>()
                    .ElementAt(settings.ThemeChoice.SelectedIndex == 2 ? 1 : 2).IsChecked = true;
            };
            var rotateBody = new Button { Content = "Body +90°" };
            rotateBody.Click += (_, _) => window.GetVisualDescendants().OfType<HostSettingsPanel>().Single()
                .Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var pageIndex = 0;
            var fitIndex = 0;
            var sceneIndex = 0;
            var pages = new[] { "live-glass.html", "static-gradient.html", "transparent.html", "static-quirks.html", "dynamic-height.html" };
            nextPage.Click += (_, _) => window.GetVisualDescendants().OfType<NativeWebView>().Single().Navigate("http://127.0.0.1:8878/" + pages[pageIndex++ % pages.Length]);
            nextFit.Click += (_, _) =>
            {
                // Exercise the real host Fit calculation through window resizing.
                window.MinHeight = 400;
                window.Height = new[] { 874, 740, 564, 960 }[fitIndex++ % 4];
            };
            glass.Click += (_, _) =>
            {
                var settings = window.GetVisualDescendants().OfType<HostSettingsPanel>().Single();
                settings.Glass.Value = settings.Glass.Value < .5 ? 1 : 0;
            };
            nextScene.Click += async (_, _) =>
            {
                var settings = window.GetVisualDescendants().OfType<HostSettingsPanel>().Single();
                switch (sceneIndex++ % 4)
                {
                    case 0:
                        settings.Fold.Children.OfType<RadioButton>().ElementAt(1).IsChecked = true;
                        settings.TabStrip.IsChecked = true;
                        break;
                    case 1:
                        settings.TabStrip.IsChecked = false;
                        break;
                    case 2:
                        settings.Fold.Children.OfType<RadioButton>().ElementAt(0).IsChecked = true;
                        for (var index = 0; index < 3; index++) settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case 3:
                        settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                }
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (pageIndex > 0) window.GetVisualDescendants().OfType<NativeWebView>().Single().Navigate("http://127.0.0.1:8878/" + pages[(pageIndex - 1) % pages.Length]);
            };
            capture.Click += async (_, _) => await CapturePresented(window, true);
            finish.Click += (_, _) => window.Close();
            var toolbar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { capture, finish, nextPage, nextFit, glass, nextScene, rotateBody, nextAppearance } };
            Grid.SetRow(content, 1);
            window.Content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { toolbar, content } };
        }
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            if (Program.Presented)
            {
                try { await DesktopTestScreen.Select(window); }
                catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); return; }
                var inputProfile = WindowsChecks.ActivatePresentedInputProfile();
                window.Closed += (_, _) => inputProfile?.Dispose();
                if (Program.PresentedLeft)
                {
                    var settings = window.GetVisualDescendants().OfType<HostSettingsPanel>().Single();
                    for (var index = 0; index < 3; index++) settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                if (Program.PresentedColor) window.GetVisualDescendants().OfType<HostSettingsPanel>().Single().ColorDemo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var capturing = false;
                var remaining = 300;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += async (_, _) =>
                {
                    if (capturing || !window.IsVisible) return;
                    if (--remaining < 0) { timer.Stop(); return; }
                    capturing = true;
                    try { await CapturePresented(window); }
                    finally { capturing = false; }
                };
                window.Closed += (_, _) => timer.Stop();
                timer.Start();
                var web = window.GetVisualDescendants().OfType<NativeWebView>().Single();
                web.CommandRequested += async command =>
                {
                    if (command != HostCommand.SaveState) return;
                    await CapturePresented(window);
                };
                return;
            }
            var exitCode = 0;
            var originalLanguage = AppLocalization.Current.Preference;
            try
            {
                await new WindowsChecks(window).Run();
            }
            catch (Exception error)
            {
                exitCode = 1;
                Console.Error.WriteLine(error);
                File.WriteAllText(Path.Combine(Program.Output, "failure.txt"), error.ToString());
                var failedWeb = window.GetVisualDescendants().OfType<NativeWebView>().Single();
                var failedPeek = await failedWeb.EvaluateResultAsync("JSON.stringify({url:location.href,ready:document.readyState,observation:window.devduoShellObservation,canvas:window.canvasAcceptance,interaction:window.inputAcceptanceState?.()})").WaitAsync(TimeSpan.FromSeconds(3));
                File.WriteAllText(Path.Combine(Program.Output, "failed-document-peek.json"), JsonSerializer.Serialize(new { failedPeek,
                    host = JsonSerializer.Deserialize<JsonElement>(failedWeb.ReadState()) }));
            }
            finally
            {
                if (AppLocalization.Current.Preference != originalLanguage)
                    AppLocalization.Current.Select(originalLanguage);
                window.Content = null;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                desktop.Shutdown(exitCode);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CapturePresented(MainWindow window, bool paintEvidence = false)
    {
        var web = window.GetVisualDescendants().OfType<NativeWebView>().Single();
        var actual = await web.EvaluateResultAsync("JSON.stringify({url:location.href,visibility:document.visibilityState,focused:document.hasFocus(),documentId:window.devduoShellObservation?.documentId,inner:[innerWidth,innerHeight],document:[document.scrollingElement.scrollWidth,document.scrollingElement.scrollHeight],scroll:[scrollX,scrollY],range:[document.scrollingElement.scrollWidth-innerWidth,document.scrollingElement.scrollHeight-innerHeight],input:document.querySelector('input')?.value,active:document.activeElement?.tagName,animation:document.querySelector('#animation')?.textContent,fixed:[...document.querySelectorAll('*')].filter(e=>getComputedStyle(e).position==='fixed').map(e=>{const r=e.getBoundingClientRect();return [r.left,r.top,r.right,r.bottom]}),sticky:[...document.querySelectorAll('*')].filter(e=>getComputedStyle(e).position==='sticky').map(e=>{const r=e.getBoundingClientRect();return [r.left,r.top,r.right,r.bottom]}),units:[...document.querySelectorAll('[data-unit]')].map(e=>[e.dataset.unit,e.getBoundingClientRect().height])})");
        using var host = JsonDocument.Parse(web.ReadState());
        using var dom = actual is null ? null : JsonDocument.Parse(actual);
        var prefix = Path.Combine(Program.Output, "presented-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
        if (paintEvidence) NativePaintEvidence.Save(web.WindowsPanel!.Browser, host.RootElement, prefix);
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new {
            host = host.RootElement, dom = dom?.RootElement, windowScaling = window.RenderScaling,
            interaction = await web.EvaluateResultAsync("JSON.stringify(window.inputAcceptanceState?.())"),
            remoteDesktop = System.Windows.Forms.SystemInformation.TerminalServerSession }));
    }
}
