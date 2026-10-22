using System.Net;
using System.Text.Json;

namespace DevDuo;

public static class WelcomePage
{
    public static string Html(AppLocalization language) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Pages", "welcome.html"))
            .Replace("__DEVDUO_LANGUAGE__", language.Language)
            .Replace("__DEVDUO_STRINGS__", JsonSerializer.Serialize(language.WelcomeStrings()))
            .Replace("__DEVDUO_REPOSITORY__", WebUtility.HtmlEncode(ProjectLinks.Repository))
            .Replace("__DEVDUO_DOCUMENTATION__", WebUtility.HtmlEncode(ProjectLinks.Documentation));

    // The welcome document owns these text nodes. Changing language does not
    // recreate its native view, navigate, or re-run its initialization.
    public static string LocalizationScript(AppLocalization language) =>
        "if(document.documentElement.dataset.devduoPage==='welcome-v1' && " +
        "typeof window.devduoLocalizeWelcome==='function')window.devduoLocalizeWelcome(" +
        JsonSerializer.Serialize(language.Language) + "," + JsonSerializer.Serialize(language.WelcomeStrings()) + ");";
}
