using PeachBrackets.Html;
using System;

namespace PeachBrackets;

/// <summary>
/// How an <see cref="HtmlDocument"/> is loaded and paginated: the size of the page, its margins, and the environment the style sheets
/// are evaluated in.
/// </summary>
/// <remarks>
/// Lengths are in points (1/72 inch); a <c>@page</c> rule in the document overrides the page size and margins set here. The options are
/// read when the document is loaded, so a change afterwards does not reach a document that already exists.
/// </remarks>
public sealed class HtmlDocumentOptions
{
    /// <summary>The width of the page, in points. A4 (595) by default.</summary>
    public double PageWidthPoints { get; set; } = 595;

    /// <summary>The height of the page, in points. A4 (842) by default.</summary>
    public double PageHeightPoints { get; set; } = 842;

    /// <summary>The margin above the content, in points. 20 by default.</summary>
    public double MarginTopPoints { get; set; } = 20;

    /// <summary>The margin to the right of the content, in points. 20 by default.</summary>
    public double MarginRightPoints { get; set; } = 20;

    /// <summary>The margin below the content, in points. 20 by default.</summary>
    public double MarginBottomPoints { get; set; } = 20;

    /// <summary>The margin to the left of the content, in points. 20 by default.</summary>
    public double MarginLeftPoints { get; set; } = 20;

    /// <summary>Sets all four margins to <paramref name="points"/>.</summary>
    /// <param name="points">the margin on every side, in points</param>
    public void SetMargins(double points)
    {
        MarginTopPoints = MarginRightPoints = MarginBottomPoints = MarginLeftPoints = points;
    }

    /// <summary>
    /// The media type the style sheets are evaluated for, as <c>@media</c> sees it. <c>print</c> by default, since a paginated document is a
    /// print medium.
    /// </summary>
    public string Media { get; set; } = "print";

    /// <summary>The colour scheme <c>prefers-color-scheme</c> media queries report. Light by default.</summary>
    public ColorSchemePreference ColorScheme { get; set; } = ColorSchemePreference.Light;

    /// <summary>
    /// Whether the document's own <c>&lt;style&gt;</c> and <c>&lt;link rel=stylesheet&gt;</c> rules are ignored, leaving only the built-in
    /// defaults and <see cref="UserStyleSheet"/>. Off by default.
    /// </summary>
    public bool IgnoreAuthorStyleSheets { get; set; }

    /// <summary>CSS added to the built-in defaults and applied before the document's own style sheets, or null for none.</summary>
    public string? UserStyleSheet { get; set; }

    /// <summary>
    /// A style sheet parsed ahead of time, applied in place of <see cref="UserStyleSheet"/> (which is ignored when this is set), so the same
    /// parsed rules can serve many documents. It is not changed by loading.
    /// </summary>
    public Building.StyleSheet? StyleSheet { get; set; }

    /// <summary>
    /// The language to assume when the document declares none (<c>&lt;html lang&gt;</c>), as a BCP 47 tag, or null to assume none. It
    /// drives hyphenation and language-dependent line breaking.
    /// </summary>
    public string? DefaultLanguage { get; set; }

    /// <summary>
    /// How far through its animations the document is painted, from 0 (the start) to 1 (the end), or null for the document's static look.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">set outside 0 to 1</exception>
    public double? AnimationProgress
    {
        get => _animationProgress;
        set
        {
            if (value is { } progress && !(progress >= 0 && progress <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "AnimationProgress must be a number from 0 to 1.");

            _animationProgress = value;
        }
    }

    private double? _animationProgress;

    /// <summary>Whether box backgrounds and borders snap to whole CSS pixels, as a browser's do. Off by default.</summary>
    public bool SnapBoxDecorationsToCssPixels { get; set; }

    /// <summary>Whether border widths snap to whole CSS pixels, as a browser's do. Off by default.</summary>
    public bool SnapBorderWidthsToCssPixels { get; set; }

    /// <summary>
    /// Whether form controls are collected into <see cref="Annotations.DocumentAnnotations.FormFields"/> so a consumer can make them
    /// interactive. Off by default; the controls are painted either way.
    /// </summary>
    public bool CollectFormFields { get; set; }
}
