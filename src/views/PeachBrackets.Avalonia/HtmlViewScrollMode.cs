namespace PeachBrackets.Avalonia;

/// <summary>Whether an <see cref="HtmlView"/> scrolls its own content or takes the height of it.</summary>
public enum HtmlViewScrollMode
{
    /// <summary>The view scrolls and zooms the document inside its own bounds.</summary>
    Auto,

    /// <summary>The view is as tall as the document and does not scroll, so a parent (a list or a scroll viewer) does.</summary>
    Disabled,
}
