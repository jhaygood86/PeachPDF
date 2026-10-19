using PeachPDF.Html.Core.Fragmentation;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// One note area on one pagination slot, and the footnote calls routed into it: the page's own area
    /// (<see cref="Column"/> null), or one column's, for calls declaring
    /// <c>float-reference: column</c>.
    /// </summary>
    /// <remarks>
    /// Mutable and short-lived by design - the footnote convergence loop rebuilds these from scratch on
    /// every pass, and the resolved geometry is filled in as each area is laid out. It is the last thing
    /// the loop writes before <c>AttachFootnoteAreas</c> reads it to build the paint-facing fragments.
    /// </remarks>
    /// <param name="Column">
    /// The column this area belongs to, or null for the page's own area.
    /// </param>
    /// <param name="AreaLeft">The area's own left edge, in document space.</param>
    /// <param name="AreaWidth">The width its bodies are laid out against.</param>
    /// <param name="BandBottom">The edge the area's own bottom is placed flush with.</param>
    /// <param name="BandHeight">
    /// The height of the band the area sits in - the page's content band, or the column's - which
    /// <c>footnote-policy</c> compares the area's total height against.
    /// </param>
    internal sealed record FootnoteAreaGroup(
        ColumnAreaKey? Column,
        double AreaLeft,
        double AreaWidth,
        double BandBottom,
        double BandHeight)
    {
        /// <summary>The calls routed into this area, in document order.</summary>
        internal List<CssBoxFootnoteCall> Calls { get; } = [];

        /// <summary>The area's whole reserved height, chrome included, once it has been laid out.</summary>
        internal double TotalHeight { get; set; }

        /// <summary>The area's own top edge in document space, once it has been laid out.</summary>
        internal double AreaTop { get; set; }

        /// <summary>Where the divider rule sits in document space, once it has been laid out.</summary>
        internal double DividerTop { get; set; }

        /// <summary>The divider's thickness, resolved from this area's own <c>@footnote</c> rule.</summary>
        internal double DividerThickness { get; set; }

        /// <summary>The declared divider colour, or null to paint the UA default.</summary>
        internal string? DividerColor { get; set; }
    }
}
