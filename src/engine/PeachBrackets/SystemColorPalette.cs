using PeachDrawing.Core;

namespace PeachBrackets;

/// <summary>
/// The colours a system offers a page by name - the CSS system colour keywords <c>Canvas</c>, <c>CanvasText</c>, <c>LinkText</c> and the rest - and
/// that the document is drawn in when <see cref="UserPreferences.ForcedColors"/> is on. A host fills it from its platform's palette (the Windows
/// high contrast theme, say); <see cref="Light"/> and <see cref="Dark"/> are ready-made ones.
/// </summary>
/// <remarks>
/// A style sheet that writes a system colour keyword gets the colour of <see cref="Light"/> whatever palette is chosen here: the keywords are parsed
/// without knowing the host. The palette chosen here is what forced colors draws with.
/// </remarks>
public sealed record SystemColorPalette
{
    /// <summary>The background of the page.</summary>
    public PaintColor Canvas { get; init; } = PaintColor.FromArgb(255, 255, 255);

    /// <summary>The text on the page.</summary>
    public PaintColor CanvasText { get; init; } = PaintColor.FromArgb(0, 0, 0);

    /// <summary>The text of a link.</summary>
    public PaintColor LinkText { get; init; } = PaintColor.FromArgb(0, 0, 238);

    /// <summary>The text of a visited link.</summary>
    public PaintColor VisitedText { get; init; } = PaintColor.FromArgb(85, 26, 139);

    /// <summary>The text of a link being pressed.</summary>
    public PaintColor ActiveText { get; init; } = PaintColor.FromArgb(255, 0, 0);

    /// <summary>The face of a button.</summary>
    public PaintColor ButtonFace { get; init; } = PaintColor.FromArgb(239, 239, 239);

    /// <summary>The text on a button.</summary>
    public PaintColor ButtonText { get; init; } = PaintColor.FromArgb(0, 0, 0);

    /// <summary>The border of a button or a field.</summary>
    public PaintColor ButtonBorder { get; init; } = PaintColor.FromArgb(118, 118, 118);

    /// <summary>The background of a text field.</summary>
    public PaintColor Field { get; init; } = PaintColor.FromArgb(255, 255, 255);

    /// <summary>The text in a text field.</summary>
    public PaintColor FieldText { get; init; } = PaintColor.FromArgb(0, 0, 0);

    /// <summary>The background of selected text or a selected item.</summary>
    public PaintColor Highlight { get; init; } = PaintColor.FromArgb(0, 120, 215);

    /// <summary>Selected text.</summary>
    public PaintColor HighlightText { get; init; } = PaintColor.FromArgb(255, 255, 255);

    /// <summary>Text that is disabled.</summary>
    public PaintColor GrayText { get; init; } = PaintColor.FromArgb(109, 109, 109);

    /// <summary>The background of marked text (<c>&lt;mark&gt;</c>).</summary>
    public PaintColor Mark { get; init; } = PaintColor.FromArgb(255, 255, 0);

    /// <summary>Marked text.</summary>
    public PaintColor MarkText { get; init; } = PaintColor.FromArgb(0, 0, 0);

    /// <summary>The colour of a light system: black on white, the palette style sheets get from the keywords.</summary>
    public static SystemColorPalette Light { get; } = new();

    /// <summary>The colour of a dark system: white on black, with yellow links.</summary>
    public static SystemColorPalette Dark { get; } = new()
    {
        Canvas = PaintColor.FromArgb(0, 0, 0),
        CanvasText = PaintColor.FromArgb(255, 255, 255),
        LinkText = PaintColor.FromArgb(255, 255, 0),
        VisitedText = PaintColor.FromArgb(255, 128, 255),
        ActiveText = PaintColor.FromArgb(0, 255, 255),
        ButtonFace = PaintColor.FromArgb(0, 0, 0),
        ButtonText = PaintColor.FromArgb(255, 255, 255),
        ButtonBorder = PaintColor.FromArgb(255, 255, 255),
        Field = PaintColor.FromArgb(0, 0, 0),
        FieldText = PaintColor.FromArgb(255, 255, 255),
        Highlight = PaintColor.FromArgb(26, 235, 255),
        HighlightText = PaintColor.FromArgb(0, 0, 0),
        GrayText = PaintColor.FromArgb(63, 242, 63),
        Mark = PaintColor.FromArgb(255, 255, 0),
        MarkText = PaintColor.FromArgb(0, 0, 0),
    };
}
