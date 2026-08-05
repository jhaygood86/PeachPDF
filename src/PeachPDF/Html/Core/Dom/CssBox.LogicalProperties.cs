using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS Logical Properties and Values Level 1's margin/padding/inset/border longhands
    /// (<c>margin-block-start</c>, <c>border-inline-end-color</c>, etc.) - each cascades into its own
    /// field here (set by <c>Utils.CssUtils</c>'s string setters, see <c>PeachPDF.CSS.StyleProperties.Logical</c>
    /// for the Layer A property classes), independently of its physical sibling. These are resolution
    /// scratch space, not part of <c>ComputedStyleAreas</c>'s copy-on-write model: unlike
    /// margin-top/padding-left/etc. (read repeatedly throughout layout), a logical value is read exactly
    /// once, by <see cref="ResolveLogicalProperties"/> (called from <c>DomParser.CascadeApplyStyles</c>
    /// once this box's own <see cref="CssBox.Direction"/>/<see cref="CssBox.WritingMode"/> are resolved),
    /// which writes it into the corresponding physical field via <see cref="LogicalPropertyResolver"/>'s
    /// abstract-to-physical mapping and is never consulted again - matching the plain-field treatment
    /// <see cref="CssBox.BidiLevels"/> and <c>CollapsedMarginTop</c> already use for other per-box,
    /// write-once/read-once cascade facts.
    /// </summary>
    internal partial class CssBox
    {
        internal string? MarginBlockStart { get; set; }
        internal string? MarginBlockEnd { get; set; }
        internal string? MarginInlineStart { get; set; }
        internal string? MarginInlineEnd { get; set; }

        internal string? PaddingBlockStart { get; set; }
        internal string? PaddingBlockEnd { get; set; }
        internal string? PaddingInlineStart { get; set; }
        internal string? PaddingInlineEnd { get; set; }

        internal string? InsetBlockStart { get; set; }
        internal string? InsetBlockEnd { get; set; }
        internal string? InsetInlineStart { get; set; }
        internal string? InsetInlineEnd { get; set; }

        internal string? BorderBlockStartWidth { get; set; }
        internal string? BorderBlockStartStyle { get; set; }
        internal string? BorderBlockStartColor { get; set; }
        internal string? BorderBlockEndWidth { get; set; }
        internal string? BorderBlockEndStyle { get; set; }
        internal string? BorderBlockEndColor { get; set; }
        internal string? BorderInlineStartWidth { get; set; }
        internal string? BorderInlineStartStyle { get; set; }
        internal string? BorderInlineStartColor { get; set; }
        internal string? BorderInlineEndWidth { get; set; }
        internal string? BorderInlineEndStyle { get; set; }
        internal string? BorderInlineEndColor { get; set; }

        /// <summary>
        /// Resolves every logical margin/padding/inset/border value cascaded onto this box (any of the
        /// 24 properties above that are non-null) into its physical edge, via
        /// <see cref="LogicalPropertyResolver"/> against this box's own resolved <see cref="Direction"/>/
        /// <see cref="WritingMode"/>. Deliberately unconditional - a logical value always overwrites
        /// whatever the corresponding physical longhand's own cascade already wrote, rather than tracking
        /// true declaration order between the two (CSS Logical Properties 1 §1.1 says the last one
        /// written, physical or logical, should win) - a known, narrow simplification, see
        /// .claude/accepted-gaps/logical-and-physical-property-conflicts-favor-logical.md.
        /// </summary>
        internal void ResolveLogicalProperties()
        {
            if (MarginBlockStart is null && MarginBlockEnd is null && MarginInlineStart is null && MarginInlineEnd is null
                && PaddingBlockStart is null && PaddingBlockEnd is null && PaddingInlineStart is null && PaddingInlineEnd is null
                && InsetBlockStart is null && InsetBlockEnd is null && InsetInlineStart is null && InsetInlineEnd is null
                && BorderBlockStartWidth is null && BorderBlockStartStyle is null && BorderBlockStartColor is null
                && BorderBlockEndWidth is null && BorderBlockEndStyle is null && BorderBlockEndColor is null
                && BorderInlineStartWidth is null && BorderInlineStartStyle is null && BorderInlineStartColor is null
                && BorderInlineEndWidth is null && BorderInlineEndStyle is null && BorderInlineEndColor is null)
            {
                return;
            }

            var writingMode = WritingMode.Value;
            var direction = Direction.Value;

            var blockStart = LogicalPropertyResolver.BlockStart(writingMode);
            var blockEnd = LogicalPropertyResolver.BlockEnd(writingMode);
            var inlineStart = LogicalPropertyResolver.InlineStart(writingMode, direction);
            var inlineEnd = LogicalPropertyResolver.InlineEnd(writingMode, direction);

            if (MarginBlockStart is { } mbs) ApplyMargin(blockStart, mbs);
            if (MarginBlockEnd is { } mbe) ApplyMargin(blockEnd, mbe);
            if (MarginInlineStart is { } mis) ApplyMargin(inlineStart, mis);
            if (MarginInlineEnd is { } mie) ApplyMargin(inlineEnd, mie);

            if (PaddingBlockStart is { } pbs) ApplyPadding(blockStart, pbs);
            if (PaddingBlockEnd is { } pbe) ApplyPadding(blockEnd, pbe);
            if (PaddingInlineStart is { } pis) ApplyPadding(inlineStart, pis);
            if (PaddingInlineEnd is { } pie) ApplyPadding(inlineEnd, pie);

            if (InsetBlockStart is { } ibs) ApplyInset(blockStart, ibs);
            if (InsetBlockEnd is { } ibe) ApplyInset(blockEnd, ibe);
            if (InsetInlineStart is { } iis) ApplyInset(inlineStart, iis);
            if (InsetInlineEnd is { } iie) ApplyInset(inlineEnd, iie);

            if (BorderBlockStartWidth is { } bbsw) ApplyBorderWidth(blockStart, bbsw);
            if (BorderBlockStartStyle is { } bbss) ApplyBorderStyle(blockStart, bbss);
            if (BorderBlockStartColor is { } bbsc) ApplyBorderColor(blockStart, bbsc);
            if (BorderBlockEndWidth is { } bbew) ApplyBorderWidth(blockEnd, bbew);
            if (BorderBlockEndStyle is { } bbes) ApplyBorderStyle(blockEnd, bbes);
            if (BorderBlockEndColor is { } bbec) ApplyBorderColor(blockEnd, bbec);
            if (BorderInlineStartWidth is { } bisw) ApplyBorderWidth(inlineStart, bisw);
            if (BorderInlineStartStyle is { } biss) ApplyBorderStyle(inlineStart, biss);
            if (BorderInlineStartColor is { } bisc) ApplyBorderColor(inlineStart, bisc);
            if (BorderInlineEndWidth is { } biew) ApplyBorderWidth(inlineEnd, biew);
            if (BorderInlineEndStyle is { } bies) ApplyBorderStyle(inlineEnd, bies);
            if (BorderInlineEndColor is { } biec) ApplyBorderColor(inlineEnd, biec);
        }

        private void ApplyMargin(PhysicalSide side, string value)
        {
            var parsed = CssKeywordOrValueParser.FromCssText<AutoKeyword, LengthOrCalc>(
                value, Map.AutoKeywords, CssValueParser.TryParseLengthOrCalc, AutoKeyword.Auto);
            switch (side)
            {
                case PhysicalSide.Top: MarginTop = parsed; break;
                case PhysicalSide.Right: MarginRight = parsed; break;
                case PhysicalSide.Bottom: MarginBottom = parsed; break;
                default: MarginLeft = parsed; break;
            }
        }

        private void ApplyPadding(PhysicalSide side, string value)
        {
            switch (side)
            {
                case PhysicalSide.Top: PaddingTop = value; break;
                case PhysicalSide.Right: PaddingRight = value; break;
                case PhysicalSide.Bottom: PaddingBottom = value; break;
                default: PaddingLeft = value; break;
            }
        }

        private void ApplyInset(PhysicalSide side, string value)
        {
            var parsed = CssKeywordOrValueParser.FromCssText<AutoKeyword, LengthOrCalc>(
                value, Map.AutoKeywords, CssValueParser.TryParseLengthOrCalc, AutoKeyword.Auto);
            switch (side)
            {
                case PhysicalSide.Top: Top = parsed; break;
                case PhysicalSide.Right: Right = parsed; break;
                case PhysicalSide.Bottom: Bottom = parsed; break;
                default: Left = parsed; break;
            }
        }

        private void ApplyBorderWidth(PhysicalSide side, string value)
        {
            switch (side)
            {
                case PhysicalSide.Top: BorderTopWidth = value; break;
                case PhysicalSide.Right: BorderRightWidth = value; break;
                case PhysicalSide.Bottom: BorderBottomWidth = value; break;
                default: BorderLeftWidth = value; break;
            }
        }

        private void ApplyBorderStyle(PhysicalSide side, string value)
        {
            var parsed = CssProperty<LineStyle>.FromCssText(value, Map.LineStyles, LineStyle.None);
            switch (side)
            {
                case PhysicalSide.Top: BorderTopStyle = parsed; break;
                case PhysicalSide.Right: BorderRightStyle = parsed; break;
                case PhysicalSide.Bottom: BorderBottomStyle = parsed; break;
                default: BorderLeftStyle = parsed; break;
            }
        }

        private void ApplyBorderColor(PhysicalSide side, string value)
        {
            switch (side)
            {
                case PhysicalSide.Top: BorderTopColor = value; break;
                case PhysicalSide.Right: BorderRightColor = value; break;
                case PhysicalSide.Bottom: BorderBottomColor = value; break;
                default: BorderLeftColor = value; break;
            }
        }
    }
}
