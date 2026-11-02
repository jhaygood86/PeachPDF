namespace PeachBrackets;

/// <summary>What the user wants more or less of, as the <c>prefers-contrast</c> media feature reports it.</summary>
public enum ContrastPreference
{
    /// <summary>No preference (the default): <c>prefers-contrast: no-preference</c>.</summary>
    NoPreference,

    /// <summary>The user asks for more contrast: <c>prefers-contrast: more</c>.</summary>
    More,

    /// <summary>The user asks for less contrast: <c>prefers-contrast: less</c>.</summary>
    Less,

    /// <summary>The user has chosen a custom palette: <c>prefers-contrast: custom</c>.</summary>
    Custom,
}

/// <summary>
/// The settings of the person or system reading the document that style sheets can ask about with media queries:
/// <c>prefers-reduced-motion</c>, <c>prefers-contrast</c>, <c>forced-colors</c>, <c>inverted-colors</c>,
/// <c>prefers-reduced-transparency</c> and <c>prefers-reduced-data</c>. A host such as a window or a phone reads them from the system and
/// passes them in <see cref="HtmlDocumentOptions.Preferences"/>.
/// </summary>
/// <remarks>
/// The defaults are those of a static, printed document, which is what a PDF is, so a document rendered without preferences reports exactly
/// what it always did. A document is a still image, so <see cref="ReducedMotion"/> is on by default. With <see cref="ForcedColors"/> on, the
/// document is also drawn in the colours of <see cref="ForcedPalette"/> (<c>forced-color-adjust</c> opts an element out).
/// </remarks>
public sealed record UserPreferences
{
    /// <summary>The preferences of a static document, used when none are given.</summary>
    public static UserPreferences Default { get; } = new();

    /// <summary>Whether <c>prefers-reduced-motion</c> reports <c>reduce</c>. On by default: a paginated document does not move.</summary>
    public bool ReducedMotion { get; init; } = true;

    /// <summary>What <c>prefers-contrast</c> reports. <see cref="ContrastPreference.NoPreference"/> by default.</summary>
    public ContrastPreference Contrast { get; init; }

    /// <summary>
    /// Whether <c>forced-colors</c> reports <c>active</c>: the system overrides the page's colours with its own palette, and the document is drawn in
    /// the colours of <see cref="ForcedPalette"/> instead of its own (text, backgrounds, borders and outlines; shadows and gradient backgrounds are
    /// dropped), except where an element sets <c>forced-color-adjust: none</c>. Off by default.
    /// </summary>
    public bool ForcedColors { get; init; }

    /// <summary>The colours a document is drawn in when <see cref="ForcedColors"/> is on. <see cref="SystemColorPalette.Light"/> by default.</summary>
    public SystemColorPalette ForcedPalette { get; init; } = SystemColorPalette.Light;

    /// <summary>Whether <c>inverted-colors</c> reports <c>inverted</c>: the system inverts the colours it shows. Off by default.</summary>
    public bool InvertedColors { get; init; }

    /// <summary>Whether <c>prefers-reduced-transparency</c> reports <c>reduce</c>. Off by default.</summary>
    public bool ReducedTransparency { get; init; }

    /// <summary>Whether <c>prefers-reduced-data</c> reports <c>reduce</c>. Off by default.</summary>
    public bool ReducedData { get; init; }
}
