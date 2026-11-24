using System.Collections.Generic;

namespace PeachBrackets.ScriptHosting;

/// <summary>What a <see cref="BoxReport"/> is a box of.</summary>
public enum BoxReportKind
{
    /// <summary>The content area of a page: the root of one page's boxes.</summary>
    Page,

    /// <summary>The note area at the bottom of a page that holds footnotes.</summary>
    Footnotes,

    /// <summary>A block-level box, or an inline-block, a table part, a flex or grid item: any box that is not one of the kinds below.</summary>
    Box,

    /// <summary>An inline box: the part of an inline element that sits on one line or is split by a line or page break.</summary>
    Span,

    /// <summary>A run of text of one box on one line.</summary>
    Text,

    /// <summary>A replaced image.</summary>
    Image,

    /// <summary>An inline SVG.</summary>
    Svg,
}

/// <summary>The four edges of a box (margin, padding or border width), in points.</summary>
/// <param name="Top">the top edge</param>
/// <param name="Right">the right edge</param>
/// <param name="Bottom">the bottom edge</param>
/// <param name="Left">the left edge</param>
public readonly record struct BoxEdges(double Top, double Right, double Bottom, double Left);

/// <summary>
/// One box of a laid-out page: where it is, how big it is, and what made it. A document's boxes form a tree for each page
/// (<see cref="ScriptLayout.Pages"/>); an element that breaks across pages has one box on each page it reaches
/// (<see cref="ScriptLayout.GetBoxes"/>).
/// </summary>
/// <remarks>
/// Positions are in points from the top left corner of the page's sheet, y growing downwards (a consumer that wants another origin converts).
/// The values are the used ones: what the layout settled on, not what the style sheet asked for.
/// </remarks>
public sealed class BoxReport
{
    private readonly List<BoxReport> _children = [];

    internal BoxReport(BoxReportKind kind, int pageNumber, double x, double y, double width, double height, double pageWidth, double pageHeight)
    {
        Kind = kind;
        PageNumber = pageNumber;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    /// <summary>What the box is a box of.</summary>
    public BoxReportKind Kind { get; }

    /// <summary>The page the box is on, counting from 1.</summary>
    public int PageNumber { get; }

    /// <summary>The distance from the left of the page's sheet to the left of the box, in points.</summary>
    public double X { get; }

    /// <summary>The distance from the top of the page's sheet to the top of the box, in points.</summary>
    public double Y { get; }

    /// <summary>The width of the box (its border box), in points.</summary>
    public double Width { get; }

    /// <summary>The height of the box (its border box), in points.</summary>
    public double Height { get; }

    /// <summary>The width of the page's sheet, in points.</summary>
    public double PageWidth { get; }

    /// <summary>The height of the page's sheet, in points.</summary>
    public double PageHeight { get; }

    /// <summary>The used margins, in points.</summary>
    public BoxEdges Margin { get; internal set; }

    /// <summary>The used padding, in points.</summary>
    public BoxEdges Padding { get; internal set; }

    /// <summary>The used border widths, in points.</summary>
    public BoxEdges Border { get; internal set; }

    /// <summary>The element the box was made for, or null for a box with no element of its own (an anonymous box, the page area).</summary>
    public ScriptNode? Element { get; internal set; }

    /// <summary>The generated content the box is, <c>before</c> or <c>after</c> or <c>marker</c> and so on, or null for a box of an element or of no element.</summary>
    public string? PseudoElement { get; internal set; }

    /// <summary>The text of a <see cref="BoxReportKind.Text"/> box, or null.</summary>
    public string? Text { get; internal set; }

    /// <summary>The source of a <see cref="BoxReportKind.Image"/> box, or null.</summary>
    public string? ImageSource { get; internal set; }

    /// <summary>The box this one is inside, or null for a page's root.</summary>
    public BoxReport? Parent { get; internal set; }

    /// <summary>The boxes inside this one, in order.</summary>
    public IReadOnlyList<BoxReport> Children => _children;

    internal void Add(BoxReport child)
    {
        child.Parent = this;
        _children.Add(child);
    }
}
