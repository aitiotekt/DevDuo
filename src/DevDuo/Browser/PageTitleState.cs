namespace DevDuo;

// Native title metadata may still describe the previous document while a new
// provisional load is running. Accept it only after that document is observed.
internal sealed class PageTitleState
{
    private bool documentConfirmed;
    internal string? Title { get; private set; }

    internal void BeginNavigation()
    {
        documentConfirmed = false;
        Title = null;
    }

    internal void ConfirmDocument(string? title)
    {
        documentConfirmed = true;
        Title = title;
    }

    internal void UpdateNativeTitle(string? title)
    {
        if (documentConfirmed) Title = title;
    }

    internal string DisplayTitle(string fallback) => string.IsNullOrWhiteSpace(Title) ? fallback : Title;
}
