using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckWelcomePageRuntime()
    {
        // BrowserTools and title checks have already committed a real developer
        // URL. Home must preserve that remembered URL without reopening it.
        Check(CalibrationUrl.TryNormalize(Web.Navigation.Url, out var currentPage) &&
            BuiltinPages.FromAddress(currentPage.AbsoluteUri) == BuiltinPageKind.None, "Start Home checks from a real developer document.");
        var expectedRememberedUrl = CalibrationUrl.RemoveLabels(currentPage).AbsoluteUri;
        await Wait(_ => PreferenceStore.Default.Host.LastUrl == expectedRememberedUrl, "The current developer URL is saved before Home");
        var rememberedUrl = PreferenceStore.Default.Host.LastUrl;
        var address = window.GetVisualDescendants().OfType<HostAddressBar>().Single();
        address.Home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => !Web.Navigation.IsLoading && Web.Navigation.BuiltinPage == BuiltinPageKind.Welcome &&
            state.TryGetProperty("pageDocumentId", out var id) && id.ValueKind == JsonValueKind.String, "Offline Home document");
        Check(!Web.Navigation.IsLocalFixture, "Home is not a calibration or color fixture.");
        var originalWeb = Web;
        var document = State().GetProperty("pageDocumentId").GetString();
        var identity = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href])");
        await Web.EvaluateResultAsync("window.welcomeSentinel='preserved';document.body.style.minHeight='2000px';scrollTo(0,240);'ready'");
        await WaitForScript("String(scrollY)", value => value == "240", "Welcome scroll position");
        var previousLanguage = AppLocalization.Current.Preference;
        foreach (var language in new[] { "en", "zh-Hans" })
        {
            Settings.Language.SelectedIndex = language == "en" ? 1 : 2;
            await WaitForScript("document.documentElement.lang + ':' + document.title",
                value => value == language + ":" + AppLocalization.Catalog(language)["Welcome.PageTitle"], "Welcome in-place language");
            Check(ReferenceEquals(originalWeb, Web) && State().GetProperty("pageDocumentId").GetString() == document,
                "Welcome language changes retain the native view and document.");
            Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href])") == identity &&
                await Web.EvaluateResultAsync("window.welcomeSentinel + ':' + scrollY") == "preserved:240",
                "Welcome language changes preserve URL, JavaScript state and scroll.");
            Save("welcome-" + language, State());
        }
        Settings.Language.SelectedIndex = previousLanguage switch { "en" => 1, "zh-Hans" => 2, _ => 0 };
        var originalMode = Settings.Mode.SelectedIndex;
        Select(Settings.Mode, originalMode == 0 ? 1 : 0);
        await Wait(state => !ReferenceEquals(originalWeb, Web) && !Web.Navigation.IsLoading &&
            Web.Navigation.BuiltinPage == BuiltinPageKind.Welcome && state.GetProperty("pageDocumentId").ValueKind == JsonValueKind.String,
            "Container replacement opens Home directly");
        Save("welcome-rebuilt", State());
        Settings.ResetSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => !Web.Navigation.IsLoading && Web.Navigation.BuiltinPage == BuiltinPageKind.Welcome &&
            state.GetProperty("pageDocumentId").ValueKind == JsonValueKind.String, "Reset retains Home");
        Check(PreferenceStore.Default.Host.LastUrl == rememberedUrl, "Home, language, mode and reset preserve the remembered developer URL.");
        Check(address.GetVisualDescendants().OfType<TextBox>().Single().Text == AppLocalization.Current.Text("Address.Welcome"),
            "Home has a localized address label, separate from the fixture label.");
        var links = await Web.EvaluateResultAsync("JSON.stringify([...document.querySelectorAll('a')].map(a=>a.href))");
        using var actualLinks = JsonDocument.Parse(links!);
        Check(actualLinks.RootElement.EnumerateArray().Select(link => link.GetString()).Order().SequenceEqual(
            new[] { ProjectLinks.Documentation, ProjectLinks.Repository }.Order()), "The real Home document links to the metadata project URLs.");
        Save("welcome-reset", State());
        Console.WriteLine("PASS offline Home: localized document and scroll, container replacement, reset and remembered developer URL.");
    }
}
