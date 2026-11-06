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
    /// The medium the document is laid out for: <see cref="PeachBrackets.MediaType.Print"/> (the default) breaks it into pages and applies
    /// <c>@media print</c>; <see cref="PeachBrackets.MediaType.Screen"/> lays it out as one continuous surface, with no pages, and applies
    /// <c>@media screen</c>.
    /// </summary>
    public MediaType MediaType { get; set; } = MediaType.Print;

    /// <summary>
    /// The height of the viewport the document is shown in, in points, or <see langword="null"/> (the default) when it is not known. Only
    /// <see cref="MediaType.Screen"/> uses it: it is what <c>vh</c> and the other viewport-height units, <c>height: 100%</c> on the root and the
    /// <c>height</c>/<c>aspect-ratio</c>/<c>orientation</c> media features resolve against, and the least height of the surface, so a short
    /// document still fills a viewport. The host pins it - a value that follows the content would never settle for <c>100vh</c> content. When
    /// it is <see langword="null"/>, viewport-height units and a percentage root height behave as <c>auto</c>.
    /// </summary>
    public double? ViewportHeightPoints { get; set; }

    /// <summary>
    /// The pointing devices the <c>hover</c>, <c>any-hover</c>, <c>pointer</c> and <c>any-pointer</c> media features report. <see langword="null"/>
    /// (the default) is <see cref="InputCapabilities.None"/>: a document that is not pointed at, so those queries match their "none" state.
    /// </summary>
    public InputCapabilities? Input { get; set; }

    /// <summary>
    /// The settings of the reader that <c>prefers-reduced-motion</c>, <c>prefers-contrast</c>, <c>forced-colors</c>, <c>inverted-colors</c>,
    /// <c>prefers-reduced-transparency</c> and <c>prefers-reduced-data</c> report. <see langword="null"/> (the default) is
    /// <see cref="UserPreferences.Default"/>, the settings of a static document.
    /// </summary>
    public UserPreferences? Preferences { get; set; }

    /// <summary>
    /// Spacing to apply over the document's own for a reader who needs more room around text, or <see langword="null"/> (the default) to leave
    /// the document's spacing alone. <see cref="TextSpacingOverrides.Wcag"/> is the spacing WCAG 1.4.12 asks to be supported.
    /// </summary>
    public TextSpacingOverrides? TextSpacing { get; set; }

    /// <summary>
    /// How much larger (or smaller) than the document asks text is drawn, for a reader who sets a text size on their system: 1 (the default) leaves it
    /// alone, 1.5 makes the root font size 150% of the default. Text the document sizes in <c>em</c>, <c>rem</c>, a percentage or a keyword follows;
    /// text sized in <c>px</c> or <c>pt</c> does not, as in a browser's font size setting. It is added to <see cref="UserStyleSheet"/>, so it is
    /// ignored when <see cref="StyleSheet"/> is set.
    /// </summary>
    public double FontScale { get; set; } = 1;

    /// <summary>
    /// Whether form controls are drawn with their look: the check mark of a checked check box, the dot of a radio button, the value or placeholder
    /// of a text field, the label of a button, the chosen option and arrow of a drop-down, the track and thumb of a range. All of it is drawn from the
    /// control's own CSS (border, background, padding, font, colour, <c>accent-color</c>). Off by default, which leaves a control the plain box it
    /// has always been in a PDF; a host that shows the page turns it on. It takes precedence over <see cref="CollectFormFields"/> for how a control is
    /// drawn, so a host can collect the fields and still show the controls as they look.
    /// </summary>
    public bool PaintFormControls { get; set; }

    /// <summary>
    /// How many device pixels the host paints for one CSS pixel (1 by default): 2 on a display with twice the density, 1.5 at 150% display scaling.
    /// The <c>resolution</c>, <c>device-pixel-ratio</c> and <c>-webkit-device-pixel-ratio</c> media features report it (a <c>&lt;source media&gt;</c>
    /// condition and the <c>srcset</c> choice see it too), and a <see cref="MediaType.Screen"/> document uses it to pick the <c>srcset</c> image
    /// that is just sharp enough (see <see cref="ImageSelection"/>). A value that is not a positive finite number is treated as 1.
    /// </summary>
    public double DevicePixelRatio { get; set; } = 1;

    /// <summary>
    /// How the image of a <c>srcset</c> or <c>&lt;picture&gt;</c> is chosen, or <see langword="null"/> (the default) for the policy of the medium:
    /// <see cref="ImageSelectionPolicy.HighestDensity"/> for <see cref="MediaType.Print"/>, <see cref="ImageSelectionPolicy.BestForTarget"/> for
    /// <see cref="MediaType.Screen"/>.
    /// </summary>
    public ImageSelectionOptions? ImageSelection { get; set; }

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

    /// <summary>
    /// Whether the document plays its CSS animations and transitions and its animated images (GIF, WebP) over time. Off by default: a PDF is a
    /// single moment. When on, nothing moves by itself: the host calls <see cref="HtmlDocument.AdvanceAnimations(TimeSpan)"/> once per frame with the time
    /// and repaints the regions it returns. <c>prefers-reduced-motion</c> is the document's to honour through <see cref="Preferences"/>; the
    /// host decides whether to turn this on at all. Ignored when <see cref="AnimationProgress"/> is set.
    /// </summary>
    /// <remarks>
    /// Properties that cannot change layout (<c>transform</c>, <c>opacity</c>, <c>filter</c>, colours, shadows) animate in place with no layout
    /// pass; an animation of a property that can move or resize something does not play.
    /// </remarks>
    public bool AnimationsEnabled { get; set; }

    /// <summary>
    /// How many pages precede this document in the file it is part of. Added to the number <c>counter(page)</c> shows in a page's margin
    /// boxes and running elements, so a document that is one part of a larger one numbers its pages on from the part before. Pages
    /// are still selected by <c>@page</c> rules (<c>:first</c>, <c>:left</c>) by their place in this document. Zero by default.
    /// </summary>
    public int PageNumberOffset { get; set; }

    /// <summary>
    /// The number <c>counter(pages)</c> shows in place of this document's own page count, for a document that is one part of a larger one;
    /// <see langword="null"/> (the default) shows its own page count.
    /// </summary>
    public int? TotalPagesOverride { get; set; }

    /// <summary>Whether box backgrounds and borders snap to whole CSS pixels, as a browser's do. Off by default.</summary>
    public bool SnapBoxDecorationsToCssPixels { get; set; }

    /// <summary>Whether border widths snap to whole CSS pixels, as a browser's do. Off by default.</summary>
    public bool SnapBorderWidthsToCssPixels { get; set; }

    /// <summary>
    /// Whether form controls are collected into <see cref="Annotations.DocumentAnnotations.FormFields"/> so a consumer can make them
    /// interactive. Off by default; the controls are painted either way.
    /// </summary>
    public bool CollectFormFields { get; set; }

    /// <summary>
    /// Whether the document can report where its text is (<see cref="HtmlDocument.TextRuns"/> and the hit-testing, selection and search that
    /// build on it), so a consumer can offer text selection, copying and find. Off by default; nothing is worked out unless it is read, and the
    /// layout and painting are the same either way.
    /// </summary>
    public bool RecordTextRuns { get; set; }

    /// <summary>
    /// Whether <see cref="HtmlDocument.SetSelectedRanges"/> paints the selection, in the document's <c>::selection</c> style. Off by default, and
    /// then <c>::selection</c> rules are ignored as they always were. Needs <see cref="RecordTextRuns"/>.
    /// </summary>
    public bool EnableSelectionStyling { get; set; }

    /// <summary>
    /// Says that loading and laying out this document needs nothing asynchronous: its images, style sheets and fonts are inline, already registered
    /// on the render context, or answered without waiting. The document can then be loaded, laid out and painted with the synchronous
    /// <see cref="HtmlDocument.Load"/>, <see cref="HtmlDocument.Layout"/> and <see cref="HtmlDocument.PaintPage"/>, which a host that cannot
    /// wait (a control measuring itself during layout) needs. If the document then tries to do something that would really suspend - fetch an image or
    /// style sheet over the network or from disk, load a web font - it throws <see cref="InvalidOperationException"/> naming the resource instead of
    /// blocking. Off by default.
    /// </summary>
    public bool SynchronousOnly { get; set; }

    /// <summary>
    /// Whether <see cref="HtmlDocument.SetStyle"/> can restyle elements of the laid-out document in place, reporting the regions to repaint
    /// instead of the document being loaded and laid out again. Off by default.
    /// </summary>
    public bool EnableIncrementalUpdates { get; set; }

    /// <summary>
    /// Whether a change of the pointer or focus state (<c>:hover</c>, <c>:active</c>, <c>:focus</c>) that only changes how something is painted
    /// (a colour, an opacity, a shadow, a transform...) is applied to the styles in place, instead of the document being loaded and laid out
    /// again. A change to anything else still loads it again. On by default; the environment variable <c>PEACHPDF_FULL_RESTYLE=1</c> turns it
    /// off for every document.
    /// </summary>
    public bool IncrementalRestyle { get; set; } = true;

    /// <summary>The background of a selection where no <c>::selection</c> rule applies; light blue by default.</summary>
    public PeachDrawing.Core.PaintColor SelectionBackground { get; set; } = PeachDrawing.Core.PaintColor.FromArgb(255, 173, 214, 255);

    /// <summary>The colour selected text is drawn in where no <c>::selection</c> rule applies, or null (the default) to keep the text's own.</summary>
    public PeachDrawing.Core.PaintColor? SelectionForeground { get; set; }
}
