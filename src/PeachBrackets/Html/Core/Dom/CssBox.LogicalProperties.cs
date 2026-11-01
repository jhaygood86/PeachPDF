using System;
using System.Collections.Generic;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS Logical Properties and Values Level 1's margin/padding/inset/border longhands
    /// (<c>margin-block-start</c>, <c>border-inline-end-color</c>, etc.) - each cascades into its own
    /// field here (set by <c>Utils.CssUtils</c>'s string setters, see <c>PeachBrackets.Css.StyleProperties.Logical</c>
    /// for the Layer A property classes), independently of its physical sibling. These are resolution
    /// scratch space, not part of <c>ComputedStyleAreas</c>'s copy-on-write model: unlike
    /// margin-top/padding-left/etc. (read repeatedly throughout layout), a logical value is read exactly
    /// once, by <see cref="ResolveLogicalProperties"/> (called from <c>DomParser.CascadeApplyStyles</c>
    /// once this box's own <see cref="CssBox.Direction"/>/<see cref="CssBox.WritingMode"/> are resolved),
    /// which writes it into the corresponding physical field via <see cref="LogicalPropertyResolver"/>'s
    /// abstract-to-physical mapping and is never consulted again - matching the plain-field treatment
    /// <see cref="CssBox.BidiLevels"/> and <c>CollapsedBlockStartMargin</c> already use for other per-box,
    /// write-once/read-once cascade facts.
    /// </summary>
    internal partial class CssBox
    {
        internal string? MarginBlockStart { get => _rare is null ? default(string?) : _rare.MarginBlockStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.MarginBlockStart = value; } }
        internal string? MarginBlockEnd { get => _rare is null ? default(string?) : _rare.MarginBlockEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.MarginBlockEnd = value; } }
        internal string? MarginInlineStart { get => _rare is null ? default(string?) : _rare.MarginInlineStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.MarginInlineStart = value; } }
        internal string? MarginInlineEnd { get => _rare is null ? default(string?) : _rare.MarginInlineEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.MarginInlineEnd = value; } }

        internal string? PaddingBlockStart { get => _rare is null ? default(string?) : _rare.PaddingBlockStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.PaddingBlockStart = value; } }
        internal string? PaddingBlockEnd { get => _rare is null ? default(string?) : _rare.PaddingBlockEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.PaddingBlockEnd = value; } }
        internal string? PaddingInlineStart { get => _rare is null ? default(string?) : _rare.PaddingInlineStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.PaddingInlineStart = value; } }
        internal string? PaddingInlineEnd { get => _rare is null ? default(string?) : _rare.PaddingInlineEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.PaddingInlineEnd = value; } }

        internal string? InsetBlockStart { get => _rare is null ? default(string?) : _rare.InsetBlockStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.InsetBlockStart = value; } }
        internal string? InsetBlockEnd { get => _rare is null ? default(string?) : _rare.InsetBlockEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.InsetBlockEnd = value; } }
        internal string? InsetInlineStart { get => _rare is null ? default(string?) : _rare.InsetInlineStart; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.InsetInlineStart = value; } }
        internal string? InsetInlineEnd { get => _rare is null ? default(string?) : _rare.InsetInlineEnd; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.InsetInlineEnd = value; } }

        internal string? BorderBlockStartWidth { get => _rare is null ? default(string?) : _rare.BorderBlockStartWidth; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockStartWidth = value; } }
        internal string? BorderBlockStartStyle { get => _rare is null ? default(string?) : _rare.BorderBlockStartStyle; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockStartStyle = value; } }
        internal string? BorderBlockStartColor { get => _rare is null ? default(string?) : _rare.BorderBlockStartColor; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockStartColor = value; } }
        internal string? BorderBlockEndWidth { get => _rare is null ? default(string?) : _rare.BorderBlockEndWidth; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockEndWidth = value; } }
        internal string? BorderBlockEndStyle { get => _rare is null ? default(string?) : _rare.BorderBlockEndStyle; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockEndStyle = value; } }
        internal string? BorderBlockEndColor { get => _rare is null ? default(string?) : _rare.BorderBlockEndColor; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderBlockEndColor = value; } }
        internal string? BorderInlineStartWidth { get => _rare is null ? default(string?) : _rare.BorderInlineStartWidth; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineStartWidth = value; } }
        internal string? BorderInlineStartStyle { get => _rare is null ? default(string?) : _rare.BorderInlineStartStyle; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineStartStyle = value; } }
        internal string? BorderInlineStartColor { get => _rare is null ? default(string?) : _rare.BorderInlineStartColor; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineStartColor = value; } }
        internal string? BorderInlineEndWidth { get => _rare is null ? default(string?) : _rare.BorderInlineEndWidth; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineEndWidth = value; } }
        internal string? BorderInlineEndStyle { get => _rare is null ? default(string?) : _rare.BorderInlineEndStyle; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineEndStyle = value; } }
        internal string? BorderInlineEndColor { get => _rare is null ? default(string?) : _rare.BorderInlineEndColor; set { if (_rare is null) { if (EqualityComparer<string?>.Default.Equals(value, default(string?))) return; _rare = new BoxRareState(); } _rare.BorderInlineEndColor = value; } }

        /// <summary>
        /// Resolves every logical margin/padding/inset/border value cascaded onto this box (any of the
        /// 24 properties above that are non-null) into its physical edge, via
        /// <see cref="LogicalPropertyResolver"/> against this box's own resolved <see cref="Direction"/>/
        /// <see cref="WritingMode"/>. A logical value overwrites the physical longhand on the same edge
        /// unless that physical longhand was applied later in the cascade (CSS Logical Properties 1 §1.1:
        /// the last one written, physical or logical, wins) - see <see cref="LogicalDeclarationOrder"/>.
        /// </summary>
        /// <remarks>
        /// A logical longhand with no recorded order (one written by a path other than
        /// <c>CssUtils.SetPropertyValue</c>) keeps winning, which is the behaviour before order was tracked.
        /// </remarks>
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

            if (MarginBlockStart is { } mbs) ApplyMargin(blockStart, mbs, "margin-block-start");
            if (MarginBlockEnd is { } mbe) ApplyMargin(blockEnd, mbe, "margin-block-end");
            if (MarginInlineStart is { } mis) ApplyMargin(inlineStart, mis, "margin-inline-start");
            if (MarginInlineEnd is { } mie) ApplyMargin(inlineEnd, mie, "margin-inline-end");

            if (PaddingBlockStart is { } pbs) ApplyPadding(blockStart, pbs, "padding-block-start");
            if (PaddingBlockEnd is { } pbe) ApplyPadding(blockEnd, pbe, "padding-block-end");
            if (PaddingInlineStart is { } pis) ApplyPadding(inlineStart, pis, "padding-inline-start");
            if (PaddingInlineEnd is { } pie) ApplyPadding(inlineEnd, pie, "padding-inline-end");

            if (InsetBlockStart is { } ibs) ApplyInset(blockStart, ibs, "inset-block-start");
            if (InsetBlockEnd is { } ibe) ApplyInset(blockEnd, ibe, "inset-block-end");
            if (InsetInlineStart is { } iis) ApplyInset(inlineStart, iis, "inset-inline-start");
            if (InsetInlineEnd is { } iie) ApplyInset(inlineEnd, iie, "inset-inline-end");

            if (BorderBlockStartWidth is { } bbsw) ApplyBorderWidth(blockStart, bbsw, "border-block-start-width");
            if (BorderBlockStartStyle is { } bbss) ApplyBorderStyle(blockStart, bbss, "border-block-start-style");
            if (BorderBlockStartColor is { } bbsc) ApplyBorderColor(blockStart, bbsc, "border-block-start-color");
            if (BorderBlockEndWidth is { } bbew) ApplyBorderWidth(blockEnd, bbew, "border-block-end-width");
            if (BorderBlockEndStyle is { } bbes) ApplyBorderStyle(blockEnd, bbes, "border-block-end-style");
            if (BorderBlockEndColor is { } bbec) ApplyBorderColor(blockEnd, bbec, "border-block-end-color");
            if (BorderInlineStartWidth is { } bisw) ApplyBorderWidth(inlineStart, bisw, "border-inline-start-width");
            if (BorderInlineStartStyle is { } biss) ApplyBorderStyle(inlineStart, biss, "border-inline-start-style");
            if (BorderInlineStartColor is { } bisc) ApplyBorderColor(inlineStart, bisc, "border-inline-start-color");
            if (BorderInlineEndWidth is { } biew) ApplyBorderWidth(inlineEnd, biew, "border-inline-end-width");
            if (BorderInlineEndStyle is { } bies) ApplyBorderStyle(inlineEnd, bies, "border-inline-end-style");
            if (BorderInlineEndColor is { } biec) ApplyBorderColor(inlineEnd, biec, "border-inline-end-color");
        }

        /// <summary>
        /// Records that <paramref name="propName"/> was just applied to this box, so
        /// <see cref="ResolveLogicalProperties"/> can tell which of a logical longhand and the physical
        /// longhand on the same edge was declared later. Called by <c>CssUtils.SetPropertyValue</c> for every
        /// declaration in cascade order. A logical longhand is always stamped; a physical one only once the
        /// box has a logical declaration, because one applied before any logical declaration can never
        /// outrank it (an unstamped physical longhand counts as earliest) and almost no box has one.
        /// </summary>
        internal void NoteDeclarationOrder(string propName)
        {
            if (IsLogicalBoxModelName(propName))
            {
                _rare ??= new BoxRareState();
                (_rare.LogicalOrder ??= new LogicalDeclarationOrder()).Stamp(propName);
            }
            else if (_rare?.LogicalOrder is { } order && LogicalDeclarationOrder.IsPhysicalEdgeName(propName))
            {
                order.Stamp(propName);
            }
        }

        private static bool IsLogicalBoxModelName(string propName) =>
            propName.Contains("-block-", StringComparison.Ordinal) || propName.Contains("-inline-", StringComparison.Ordinal);

        /// <summary>
        /// Whether the logical longhand <paramref name="logicalName"/> may write the physical edge
        /// <paramref name="side"/>: false only when the physical longhand on that edge was declared after it.
        /// </summary>
        private bool LogicalOutranks(string logicalName, PhysicalSide side)
        {
            if (_rare?.LogicalOrder is not { } order) return true;

            var sideName = side switch
            {
                PhysicalSide.Top => "top",
                PhysicalSide.Right => "right",
                PhysicalSide.Bottom => "bottom",
                _ => "left",
            };

            var physicalName = logicalName switch
            {
                _ when logicalName.StartsWith("margin-", StringComparison.Ordinal) => "margin-" + sideName,
                _ when logicalName.StartsWith("padding-", StringComparison.Ordinal) => "padding-" + sideName,
                _ when logicalName.StartsWith("inset-", StringComparison.Ordinal) => sideName,
                // border-{block|inline}-{start|end}-{width|style|color} -> border-{side}-{width|style|color}
                _ => "border-" + sideName + logicalName.Substring(logicalName.LastIndexOf('-')),
            };

            return order.Of(physicalName) <= order.Of(logicalName);
        }

        private void ApplyMargin(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

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

        private void ApplyPadding(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

            switch (side)
            {
                case PhysicalSide.Top: PaddingTop = value; break;
                case PhysicalSide.Right: PaddingRight = value; break;
                case PhysicalSide.Bottom: PaddingBottom = value; break;
                default: PaddingLeft = value; break;
            }
        }

        private void ApplyInset(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

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

        private void ApplyBorderWidth(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

            switch (side)
            {
                case PhysicalSide.Top: BorderTopWidth = value; break;
                case PhysicalSide.Right: BorderRightWidth = value; break;
                case PhysicalSide.Bottom: BorderBottomWidth = value; break;
                default: BorderLeftWidth = value; break;
            }
        }

        private void ApplyBorderStyle(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

            var parsed = CssProperty<LineStyle>.FromCssText(value, Map.LineStyles, LineStyle.None);
            switch (side)
            {
                case PhysicalSide.Top: BorderTopStyle = parsed; break;
                case PhysicalSide.Right: BorderRightStyle = parsed; break;
                case PhysicalSide.Bottom: BorderBottomStyle = parsed; break;
                default: BorderLeftStyle = parsed; break;
            }
        }

        private void ApplyBorderColor(PhysicalSide side, string value, string logicalName)
        {
            if (!LogicalOutranks(logicalName, side)) return;

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
