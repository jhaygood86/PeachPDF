using System.Collections.Generic;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    /// <summary>One glyph placed at an explicit offset within a <see cref="MathBox"/>'s own coordinate
    /// space - used for a stretchy operator's assembled/variant glyphs (drawn via
    /// <c>RGraphics.DrawGlyphs</c>), never for ordinary token text (drawn via <c>RGraphics.DrawString</c>
    /// from <see cref="MathBox.Text"/> instead, which lets ordinary Unicode shaping run normally).</summary>
    internal readonly record struct MathPositionedGlyph(int GlyphIndex, double X, double Y);

    /// <summary>One child box positioned within its parent's own coordinate space - <see cref="X"/>/
    /// <see cref="Y"/> are relative to the parent's origin (its own left edge, on its own baseline;
    /// positive <see cref="Y"/> is downward, matching PeachPDF's usual top-down convention).</summary>
    internal readonly record struct MathPositionedBox(MathBox Box, double X, double Y);

    /// <summary>How a leaf <see cref="MathBox"/> paints its own content - <see cref="MathRenderer"/>
    /// dispatches on this; a box with children and <see cref="None"/> paints nothing of its own (a pure
    /// grouping box, e.g. a row).</summary>
    internal enum MathPaintKind { None, Text, Rule, Glyphs }

    /// <summary>
    /// The computed layout of one <see cref="MathNode"/> - MathML Core's own box model (inline size,
    /// ascent/descent from the alphabetic baseline, positioned children), and the paint-time data a
    /// leaf box needs. Produced by <see cref="MathLayoutEngine.Layout"/>, consumed by
    /// <see cref="MathRenderer"/>. Does not participate in <c>FragmentTree</c>/<c>BoxFragment</c> -
    /// like SVG's internal scene graph, this is an entirely separate model, opaque to the fragment
    /// tree, referenced only from <c>CssBoxMath</c>/<c>MathFragmentPainter</c>.
    /// </summary>
    internal sealed class MathBox
    {
        /// <summary>This box's total width, in points.</summary>
        public required double InlineSize { get; init; }

        /// <summary>Distance from this box's own baseline up to its top, in points (always >= 0).</summary>
        public required double Ascent { get; init; }

        /// <summary>Distance from this box's own baseline down to its bottom, in points (always >= 0).</summary>
        public required double Descent { get; init; }

        /// <summary>This box's italic correction, in points - the extra horizontal space a following
        /// straight (non-slanted) sibling should add before it, per MathML Core's italic-correction
        /// rules. 0 for anything but a single slanted-glyph token.</summary>
        public double ItalicCorrection { get; init; }

        public required IReadOnlyList<MathPositionedBox> Children { get; init; }

        public MathPaintKind PaintKind { get; init; } = MathPaintKind.None;

        /// <summary><see cref="MathPaintKind.Text"/> only: the run to draw via <c>RGraphics.DrawString</c>,
        /// at this box's own origin baseline.</summary>
        public string? Text { get; init; }

        /// <summary><see cref="MathPaintKind.Text"/>/<see cref="MathPaintKind.Glyphs"/> only: the font
        /// to draw with.</summary>
        public RFont? Font { get; init; }

        /// <summary><see cref="MathPaintKind.Rule"/> only: a filled rectangle (a fraction bar, radical
        /// vinculum, or similar) - <see cref="RuleX"/>/<see cref="RuleY"/> relative to this box's own
        /// origin, <see cref="RuleWidth"/>/<see cref="RuleHeight"/> its size.</summary>
        public double RuleX { get; init; }
        public double RuleY { get; init; }
        public double RuleWidth { get; init; }
        public double RuleHeight { get; init; }

        /// <summary><see cref="MathPaintKind.Glyphs"/> only: explicitly positioned glyphs (a stretchy
        /// operator's assembled parts, or a single substituted size-variant glyph).</summary>
        public IReadOnlyList<MathPositionedGlyph>? Glyphs { get; init; }

        public required RColor Color { get; init; }
    }
}
