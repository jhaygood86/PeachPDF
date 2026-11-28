using PeachBrackets.Html;
using System;
using System.Collections.Generic;

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
    /// Whether a <see cref="MediaType.Screen"/> document skips the contents of a <c>content-visibility: auto</c> element that is not relevant to the user
    /// (css-contain-2 §4): one whose box lies well away from the viewport and holds neither focus. Off by default, as a document laid out once shows every
    /// page and a host has to say where it is looking: the host reports the viewport with <see cref="HtmlDocument.SetViewport"/>, and when
    /// <see cref="HtmlDocument.ContentVisibilityNeedsUpdate"/> is true calls <see cref="HtmlDocument.UpdateContentVisibilityAsync"/> to skip or show contents
    /// as the viewport moves. A skipped element is as large as an empty box, or its <c>contain-intrinsic-size</c> (<c>auto</c> being the size it last had
    /// when it was shown). A paged document never skips: all of it is shown.
    /// </summary>
    public bool SkipOffscreenContent { get; set; }

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
    /// How much larger than the sheet the document lays itself out, for a host that then draws every page at <c>1 / PageScale</c> of its size:
    /// 1 (the default) lays out on the sheet as it is, 2 lays out on a sheet twice as wide and as tall (with margins twice as wide) so twice as much fits
    /// across and down, which is how a web browser's print "scale" shrinks content that is too wide. The page size, margins and an <c>@page</c> rule's
    /// size and margins are all multiplied, so the pages <see cref="HtmlDocument.Pages"/> reports are <see cref="PageScale"/> times the configured
    /// sheet; <c>vw</c>/<c>vh</c> and media queries see the larger sheet. A per-page <c>@page</c> rule (<c>:first</c>, a named page) is not scaled. Ignored
    /// for a <see cref="MediaType.Screen"/> document, which has no sheet. A value that is not a positive finite number is treated as 1.
    /// </summary>
    public double PageScale { get; set; } = 1;

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
    /// The mode to lay the document out in, or <see langword="null"/> (the default) for the one its DOCTYPE gives it, as a browser would: a document
    /// with no DOCTYPE, or a legacy one, is in <see cref="PeachBrackets.DocumentMode.Quirks"/> mode. HTML handed over without a DOCTYPE is
    /// laid out the way a browser lays it out; set <see cref="PeachBrackets.DocumentMode.NoQuirks"/> to have the standards apply to it anyway.
    /// </summary>
    public DocumentMode? DocumentMode { get; set; }

    /// <summary>
    /// How the source is parsed: <see cref="DocumentSyntax.Html"/> (the default) as HTML, or <see cref="DocumentSyntax.Xml"/> as XHTML with an XML
    /// parser, which is what a browser does for a document served as <c>application/xhtml+xml</c>. The syntax is never guessed from the text.
    /// </summary>
    public DocumentSyntax Syntax { get; set; }

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

    /// <summary>
    /// The host that runs the document's <c>&lt;script&gt;</c> elements, or null (the default) to leave scripting off: scripts are then ignored,
    /// <c>&lt;noscript&gt;</c> content is shown and the <c>(scripting)</c> media feature reports <c>none</c>. The <c>PeachBrackets.Scripting</c>
    /// package supplies a JavaScript host. Scripts run once while the document loads; what they leave is what is laid out.
    /// </summary>
    public PeachBrackets.ScriptHosting.IScriptHost? ScriptHost { get; set; }

    /// <summary>
    /// What plays the document's <c>audio</c> and <c>video</c> elements, or null (the default): PeachBrackets decodes no media itself. With none, a
    /// <c>video</c> shows its <c>poster</c> and a script's media elements cannot play. With one, a <c>video</c> shows a picture of its media (the
    /// first, or the one at a <c>#t=</c> fragment, for a paged document; the playing one for a document that <see cref="AnimationsEnabled"/> steps).
    /// </summary>
    public PeachBrackets.Media.IMediaProvider? MediaProvider { get; set; }

    /// <summary>
    /// Makes a canvas to measure text on, for the layout a script asks for while it runs (<c>getBoundingClientRect</c>, <c>offsetWidth</c>,
    /// <c>getComputedStyle</c>). Called each time the script needs the document laid out again after it changed. Null (the default) means scripts
    /// get no layout: sizes read as zero and computed styles as what the element's own <c>style</c> attribute says. Only read when
    /// <see cref="ScriptHost"/> is set.
    /// </summary>
    public Func<PeachDrawing.Core.Canvas>? MeasuringCanvasFactory { get; set; }

    /// <summary>
    /// Whether each <c>canvas</c> a script draws on keeps a bitmap of the backend <see cref="MeasuringCanvasFactory"/> makes canvases for (an
    /// <see cref="PeachDrawing.Core.OffscreenSurface"/>: on the graphics device where the backend has one), draws into it as the script draws, and is shown
    /// as that bitmap, instead of playing the whole drawing again each time the page is painted. For a host that shows the page on a screen and may repaint
    /// a canvas many times a second. The default, <see langword="false"/>, keeps the drawing as a recording that any canvas can play: a PDF or SVG gets
    /// the shapes and text as vector content. Only read when <see cref="ScriptHost"/> is set and the factory makes canvases that can give a surface; with
    /// any other (a PDF writer's, say) the recording is kept whatever this says.
    /// </summary>
    public bool CanvasSurfaces { get; set; }

    /// <summary>
    /// How many times a script host may change a laid-out document and have it laid out again (<see cref="PeachBrackets.ScriptHosting.IScriptHost.LayoutCompletedAsync"/>).
    /// When the limit is reached the last change is not laid out. The default is 16; 0 means no limit, which lets a script that changes the document
    /// on every pass run forever.
    /// </summary>
    public int MaxLayoutPasses { get; set; } = 16;

    /// <summary>
    /// How the document's scripts reach the network with <c>XMLHttpRequest</c> and <c>fetch</c>, or null (the default) to let them read what the document's
    /// resource loader can: a GET that either answers or fails. A host that wants every method, status and header sets one. Only read when
    /// <see cref="ScriptHost"/> is set. No cross-origin check is made: whatever the network answers a script can read.
    /// </summary>
    public PeachBrackets.ScriptHosting.IScriptNetwork? ScriptNetwork { get; set; }

    /// <summary>
    /// Global objects the host adds to the document's scripts (see <see cref="PeachBrackets.ScriptHosting.ScriptApi"/>), installed by the
    /// <see cref="ScriptHost"/>. Each object serves one document, so give each document its own. Only read when <see cref="ScriptHost"/> is set.
    /// </summary>
    public IList<PeachBrackets.ScriptHosting.ScriptApi> ScriptApis { get; private set; } = [];

    /// <summary>
    /// Whether a <c>canvas</c> that makes its children drawable (<c>content="drawable"</c>) is laid out as a box that holds them, instead of as a replaced element showing what a script drew.
    /// The script probe answers where the children are and draws them from such a document; the document a page is shown from does not.
    /// </summary>
    internal bool LayoutCanvasSubtrees { get; set; }

    /// <summary>Whether images from another origin than the document's are left out (a picture of an element for a script).</summary>
    internal bool HideCrossOriginImages { get; set; }

    internal HtmlDocumentOptions CloneWithoutScripts()
    {
        var copy = (HtmlDocumentOptions)MemberwiseClone();
        // Not null: a document parses differently with scripting on (<noscript> is text), and the probe's elements must be numbered as the scripted tree's are.
        copy.ScriptHost = PeachBrackets.ScriptHosting.IdleScriptHost.Instance;
        copy.MeasuringCanvasFactory = null;
        copy.ScriptApis = [];
        copy.MediaProvider = null;

        // The probe answers where text is (the rectangles of a range, the character at a point), which the text runs have.
        copy.RecordTextRuns = true;

        // ... and where the children of a canvas are, which the canvas itself does not lay out.
        copy.LayoutCanvasSubtrees = true;
        copy.UserStyleSheet += "\ncanvas[content=drawable] { display: inline-block; background: transparent !important; border-color: transparent !important; box-shadow: none !important; outline: none !important; }\n";
        return copy;
    }

    /// <summary>The background of a selection where no <c>::selection</c> rule applies; light blue by default.</summary>
    public PeachDrawing.Core.PaintColor SelectionBackground { get; set; } = PeachDrawing.Core.PaintColor.FromArgb(255, 173, 214, 255);

    /// <summary>The colour selected text is drawn in where no <c>::selection</c> rule applies, or null (the default) to keep the text's own.</summary>
    public PeachDrawing.Core.PaintColor? SelectionForeground { get; set; }
}
