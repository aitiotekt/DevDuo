using DevDuo;

internal static class PageTitleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var title = new PageTitleState();
        check(title.DisplayTitle("Page 2") == "Page 2", "A new container has a generic active-tab label.");
        title.UpdateNativeTitle("Previous document");
        check(title.DisplayTitle("Page 2") == "Page 2", "Unconfirmed native metadata cannot expose the previous title.");
        title.ConfirmDocument("Current document");
        check(title.DisplayTitle("Page 2") == "Current document", "The confirmed document supplies its actual title.");
        title.UpdateNativeTitle("Dynamic title");
        check(title.DisplayTitle("Page 2") == "Dynamic title", "Native title changes update the current document.");
        foreach (var blank in new string?[] { null, "", " \t\r\n", "\u3000" })
        {
            title.UpdateNativeTitle(blank);
            check(title.DisplayTitle("页面 2") == "页面 2", "Missing and whitespace titles use the current localized fallback.");
        }
        foreach (var actual in new[]
        {
            "A long document title that must be measured by the native text renderer rather than shortened by character count",
            "很长的真实中文文档标题保持完整文本，由原生字体测量决定省略位置",
            "👩🏽‍💻 Family 👨‍👩‍👧‍👦 中文 e\u0301 <script>alert('title')</script>",
            "  Authored spacing  "
        })
        {
            title.UpdateNativeTitle(actual);
            check(title.DisplayTitle("Page 2") == actual, "Nonempty page titles retain complete authored Unicode text.");
            check(title.DisplayTitle("页面 2") == actual, "A host language change never translates the document title.");
        }
        title.BeginNavigation();
        title.UpdateNativeTitle("Metadata retained during provisional navigation");
        check(title.Title is null && title.DisplayTitle("Page 2") == "Page 2", "Every navigation clears the preceding document title.");
        title.ConfirmDocument(null);
        check(title.DisplayTitle("Page 2") == "Page 2", "A newly confirmed untitled document keeps the generic label.");
        title.UpdateNativeTitle("Title added after parsing");
        check(title.DisplayTitle("Page 2") == "Title added after parsing", "An initially untitled page can later publish its title.");
    }
}
