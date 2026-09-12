#region PeachPDF - A .NET library for rendering HTML to PDF
//
// The MathML Core layout algorithm: turns a MathNode tree into a positioned MathBox tree. Every
// structural measurement (fraction bars, radical rules, script shifts, stack gaps) comes from the
// resolved math font's OpenType MATH table (MathTable.cs) when it has one, scaled by
// sizePt / unitsPerEm * g.PixelsPerPoint - the exact formula FontAdapter itself already uses for
// vertical metrics (see FontAdapter.ScaleDesignUnits), so MathBox geometry lands in the same working
// unit space as everything else RGraphics measures/draws in. A font with no MATH table falls back to
// fixed, TeX-book-derived approximate ratios (MathML Core's own documented fallback strategy for
// exactly this case) rather than refusing to lay the formula out at all.
//
// Deliberately out of scope for v1 (see the implementation plan's accepted-gap list): full
// operator-dictionary-driven inter-element spacing (this engine distinguishes only prefix/infix/
// postfix form, not per-character spacing rules), MathKernInfo-driven sub/superscript corner kerning,
// mpadded's relative size adjustments (rendered as a plain pass-through of its content), and
// menclose's notation decorations (content is laid out plainly, without the enclosing mark).
//
// Full MathML Core §5.3.2 glyph-assembly stretch construction IS implemented (see
// MathGlyphAssemblyShaper/SelectVerticalVariant): when no pre-sized MathVariants entry is tall enough,
// a taller shape is built from the font's GlyphAssembly parts by repeating its extender part(s) and
// distributing overlap, exactly like a real MathML implementation would - this was formerly a known
// v1 gap (only the largest pre-sized variant was ever used, clamping tall content), closed after
// checking that Chromium/WebKit/Firefox all implement the full algorithm.
//
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    internal static class MathLayoutEngine
    {
        public static MathBox Layout(MathDocument document, RGraphics g)
        {
            var referenceFont = document.ResolveFont(Math.Max(document.Root.FontSizePt, 1));
            var metrics = new MathMetrics(
                referenceFont.HasMathTable ? referenceFont.MathTable : null,
                referenceFont.FontUnitsPerEm > 0 ? referenceFont.FontUnitsPerEm : 1000,
                g.PixelsPerPoint,
                document.ResolveFont,
                g);

            return LayoutNode(document.Root, metrics);
        }

        /// <summary>The effective point size for <paramref name="node"/>, applying MathML Core's
        /// scriptlevel-driven scaling on top of its own (mathsize-chain-resolved) base size. Level 1
        /// uses <c>ScriptPercentScaleDown</c>; level 2 and beyond additionally applies
        /// <c>ScriptScriptPercentScaleDown</c> once and stops shrinking further (matching TeX's
        /// scriptstyle/scriptscriptstyle - there is no level-3-and-beyond further reduction in either
        /// TeX or any mainstream MathML implementation).</summary>
        static double EffectiveSizePt(MathNode node, MathMetrics metrics)
        {
            var ratio = node.ScriptLevel switch
            {
                <= 0 => 1.0,
                1 => metrics.ScriptPercentScaleDown,
                _ => metrics.ScriptPercentScaleDown * metrics.ScriptScriptPercentScaleDown,
            };
            return Math.Max(node.FontSizePt * ratio, 1);
        }

        static MathBox LayoutNode(MathNode node, MathMetrics metrics) => node switch
        {
            MathRowNode row => LayoutRow(row, metrics),
            MathTokenNode token => LayoutToken(token, metrics),
            MathFractionNode frac => LayoutFraction(frac, metrics),
            MathRadicalNode rad => LayoutRadical(rad, metrics),
            MathScriptNode script => LayoutScript(script, metrics),
            MathUnderOverNode uo => LayoutUnderOver(uo, metrics),
            MathMultiscriptsNode ms => LayoutMultiscripts(ms, metrics),
            MathTableNode table => LayoutTable(table, metrics),
            MathSpaceNode space => LayoutSpace(space, metrics),
            MathPhantomNode phantom => LayoutPhantom(phantom, metrics),
            MathPaddedNode padded => LayoutPadded(padded, metrics),
            MathEncloseNode enclose => LayoutNode(enclose.Content, metrics),
            MathErrorNode error => LayoutNode(error.Content, metrics),
            _ => throw new InvalidOperationException($"Unhandled MathNode type '{node.GetType().Name}'."),
        };

        // ---- mrow, and inter-element spacing ------------------------------------------------------

        static MathBox LayoutRow(MathRowNode row, MathMetrics metrics)
        {
            var built = row.Children
                .Select((c, i) => LayoutChildPossiblyStretched(c, i == 0, i == row.Children.Count - 1, metrics, row.Children))
                .ToArray();
            return AssembleRow(built, row, metrics);
        }

        /// <summary>Lays out one row child at its natural size, then - if it's a stretchy fence/operator
        /// - re-lays it out stretched to match the row's own non-stretchy ascent/descent (a simplified,
        /// single-pass version of MathML Core's stretch algorithm: this engine doesn't iterate to a
        /// fixed point, it just measures every non-stretchy sibling once).</summary>
        static (MathNode Source, MathBox Box) LayoutChildPossiblyStretched(
            MathNode child, bool isFirst, bool isLast, MathMetrics metrics, IReadOnlyList<MathNode> siblings)
        {
            if (child is not MathTokenNode { Kind: MathTokenKind.Operator } token || !IsStretchy(token, isFirst, isLast))
                return (child, LayoutNode(child, metrics));

            var (targetAscent, targetDescent) = NonStretchyExtent(siblings, metrics);
            return (child, StretchToken(token, targetAscent, targetDescent, metrics));
        }

        /// <summary>MathML Core §3.2.4's default <c>form</c> inference for an operator with no explicit
        /// <c>form</c> attribute: the first of several siblings is <c>prefix</c>, the last is
        /// <c>postfix</c>, anything else (including a lone child) is <c>infix</c>.</summary>
        static string EffectiveForm(MathTokenNode op, bool isFirst, bool isLast)
        {
            if (op.Form is { } explicitForm)
                return explicitForm;
            if (isFirst && !isLast)
                return "prefix";
            if (isLast && !isFirst)
                return "postfix";
            return "infix";
        }

        /// <summary>Whether an operator token stretches: an explicit <c>stretchy</c> attribute wins,
        /// then the legacy <c>fence="true"</c> shorthand, then the operator dictionary's own default for
        /// this character/form (MathML Core Appendix B.1) - e.g. a bare <c>&lt;mo&gt;(&lt;/mo&gt;</c>
        /// with no explicit attribute at all is still stretchy by default, since <c>(</c> is a dictionary
        /// fence character.</summary>
        static bool IsStretchy(MathTokenNode token, bool isFirst, bool isLast) =>
            token.Stretchy ?? token.Fence ?? MathOperatorDictionary.Classify(token.Text, EffectiveForm(token, isFirst, isLast)).Stretchy;

        static (double Ascent, double Descent) NonStretchyExtent(IReadOnlyList<MathNode> siblings, MathMetrics metrics)
        {
            double ascent = 0, descent = 0;
            for (int i = 0; i < siblings.Count; i++)
            {
                var sibling = siblings[i];
                if (sibling is MathTokenNode { Kind: MathTokenKind.Operator } t && IsStretchy(t, i == 0, i == siblings.Count - 1))
                    continue;

                var box = LayoutNode(sibling, metrics);
                ascent = Math.Max(ascent, box.Ascent);
                descent = Math.Max(descent, box.Descent);
            }
            return (ascent, descent);
        }

        static MathBox AssembleRow(IReadOnlyList<(MathNode Source, MathBox Box)> built, MathNode owner, MathMetrics metrics)
        {
            double x = 0, ascent = 0, descent = 0;
            var children = new List<MathPositionedBox>(built.Count);

            for (int i = 0; i < built.Count; i++)
            {
                var (source, box) = built[i];
                bool isFirst = i == 0, isLast = i == built.Count - 1;
                x += LeadingSpace(source, isFirst, isLast, metrics);
                children.Add(new MathPositionedBox(box, x, 0));
                x += box.InlineSize;
                ascent = Math.Max(ascent, box.Ascent);
                descent = Math.Max(descent, box.Descent);
                x += TrailingSpace(source, isFirst, isLast, metrics);
            }

            return new MathBox
            {
                InlineSize = x,
                Ascent = ascent,
                Descent = descent,
                Children = children,
                Color = owner.Color,
            };
        }

        /// <summary>The space before one row child - an explicit <c>lspace</c> if the source authored
        /// one, else the MathML Core operator dictionary's own default for this character/form
        /// (<see cref="MathOperatorDictionary"/>).</summary>
        static double LeadingSpace(MathNode source, bool isFirst, bool isLast, MathMetrics metrics)
        {
            if (source is not MathTokenNode { Kind: MathTokenKind.Operator } op || isFirst)
                return 0;

            if (op.LSpace is { } explicitSpace)
                return metrics.Length(explicitSpace, op.FontSizePt);

            var entry = MathOperatorDictionary.Classify(op.Text, EffectiveForm(op, isFirst, isLast));
            return metrics.Length(entry.LSpace, op.FontSizePt);
        }

        static double TrailingSpace(MathNode source, bool isFirst, bool isLast, MathMetrics metrics)
        {
            if (source is not MathTokenNode { Kind: MathTokenKind.Operator } op || isLast)
                return 0;

            if (op.RSpace is { } explicitSpace)
                return metrics.Length(explicitSpace, op.FontSizePt);

            var entry = MathOperatorDictionary.Classify(op.Text, EffectiveForm(op, isFirst, isLast));
            return metrics.Length(entry.RSpace, op.FontSizePt);
        }

        // ---- token elements ------------------------------------------------------------------------

        static MathBox LayoutToken(MathTokenNode token, MathMetrics metrics)
        {
            var sizePt = EffectiveSizePt(token, metrics);
            var font = metrics.ResolveFont(sizePt);
            var text = token.Text;
            var size = metrics.Graphics.MeasureString(text, font);

            return new MathBox
            {
                InlineSize = size.Width,
                Ascent = font.Ascent,
                Descent = Math.Max(0, font.Height - font.Ascent),
                Children = [],
                PaintKind = MathPaintKind.Text,
                Text = text,
                Font = font,
                Color = token.Color,
            };
        }

        /// <summary>Stretches a single-character operator token to cover at least
        /// <paramref name="targetAscent"/>/<paramref name="targetDescent"/>, using the resolved math
        /// font's <c>MathVariants</c> pre-sized variants or, if none is tall enough, an assembled shape
        /// (<see cref="MathGlyphAssemblyShaper"/>). Falls back to plain (unstretched) token layout when
        /// the font has no MATH table, the token isn't exactly one character, or no vertical variant
        /// data covers it at all.</summary>
        static MathBox StretchToken(MathTokenNode token, double targetAscent, double targetDescent, MathMetrics metrics)
        {
            var sizePt = EffectiveSizePt(token, metrics);
            var font = metrics.ResolveFont(sizePt);
            var mathTable = font.MathTable;

            if (mathTable is null || token.Text.Length == 0 || !System.Text.Rune.TryGetRuneAt(token.Text, 0, out var rune))
                return LayoutToken(token, metrics);

            var glyphId = (ushort)font.GetGlyphIndex(rune);
            var chosenOrNull = SelectVerticalVariant(mathTable, glyphId, targetAscent + targetDescent, sizePt, metrics);
            if (chosenOrNull is not { } chosen)
                return LayoutToken(token, metrics);

            var scale = sizePt / font.FontUnitsPerEm * metrics.PixelsPerPoint;
            var half = chosen.SizeDesignUnits * scale / 2;
            // Vertically centered on the math axis, matching how a symmetric fence is conventionally
            // placed relative to the content it encloses.
            var axis = metrics.AxisHeight(sizePt);

            // The chosen glyph's own real advance width - not the base/unstretched glyph's, since what
            // gets drawn is whichever size variant (or assembled shape) SelectVerticalVariant picked to
            // match the target height, and that glyph is a different, generally wider glyph.
            var inlineSize = chosen.AdvanceWidthDesignUnits > 0
                ? chosen.AdvanceWidthDesignUnits * scale
                : metrics.Graphics.MeasureString(token.Text, font).Width; // no descriptor - unchanged fallback

            return new MathBox
            {
                InlineSize = inlineSize,
                Ascent = axis + half,
                Descent = half - axis,
                Children = [],
                PaintKind = MathPaintKind.Glyphs,
                Font = font,
                Glyphs = PositionStretchedGlyphs(chosen, half - axis, scale),
                Color = token.Color,
            };
        }

        /// <summary>Converts a <see cref="StretchedGlyphResult"/>'s parts into working-unit-space
        /// <see cref="MathPositionedGlyph"/>s within a box whose own bottom edge is at
        /// <paramref name="descent"/> (PeachPDF's Y-down convention). A single pre-sized variant is
        /// drawn like any ordinary glyph, at the box's own baseline (Y=0) - it's simply a bigger version
        /// of the same design, meant to be positioned the same way the unstretched glyph would be. An
        /// assembled shape has no such "natural" placement (its pieces are independent glyphs with no
        /// individually meaningful baseline), so its parts - shaped bottom-to-top, each carrying its own
        /// offset along the growth axis from the assembly's own bottom (OpenType MATH's "GlyphAssembly"
        /// part order, MathML Core §5.3.2) - are placed explicitly from the box's bottom upward.</summary>
        static IReadOnlyList<MathPositionedGlyph> PositionStretchedGlyphs(StretchedGlyphResult chosen, double descent, double scale) =>
            chosen.IsAssembly
                ? chosen.Parts.Select(p => new MathPositionedGlyph(p.GlyphId, 0, descent - p.OffsetDesignUnits * scale)).ToArray()
                : [new MathPositionedGlyph(chosen.Parts[0].GlyphId, 0, 0)];

        /// <summary>The result of MathML Core's §5.3.2 "shape a stretchy glyph" algorithm for one glyph:
        /// either a single pre-sized <c>MathVariants</c> glyph (<see cref="IsAssembly"/> false, exactly
        /// one part at offset 0) or an assembled sequence of <c>GlyphAssembly</c> parts
        /// (<see cref="IsAssembly"/> true) - see <see cref="MathGlyphAssemblyShaper"/>.
        /// <see cref="AdvanceWidthDesignUnits"/> is these parts' own real <c>hmtx</c> horizontal advance
        /// (the largest, if more than one part) - the actual space the chosen/assembled glyph needs when
        /// drawn, as opposed to <see cref="SizeDesignUnits"/> (the vertical growth-direction extent
        /// <c>MathVariants</c> itself measures). Resolved once here, at selection time, so every caller
        /// of this method gets a structurally correct width instead of each re-deriving it.</summary>
        readonly record struct StretchedGlyphResult(
            IReadOnlyList<MathGlyphAssemblyShaper.ShapedPart> Parts, double SizeDesignUnits, bool IsAssembly,
            int AdvanceWidthDesignUnits);

        /// <summary>Finds or builds a vertical variant of <paramref name="glyphId"/> covering
        /// <paramref name="targetExtent"/> (working-unit-space height to match), per MathML Core
        /// §5.3.2's "shape a stretchy glyph" algorithm: the smallest pre-sized <c>MathVariants</c> entry
        /// that's already tall enough, else an assembled shape built from <c>GlyphAssembly</c> parts,
        /// else (nothing covers the target) the largest available pre-sized variant - matching the
        /// spec's own "if none of the stretch options above allowed to cover the target size, choose
        /// the last one that was tried" final fallback. Null when the font has no vertical construction
        /// data for this glyph at all.</summary>
        static StretchedGlyphResult? SelectVerticalVariant(
            MathTable mathTable, ushort glyphId, double targetExtent, double sizePt, MathMetrics metrics)
        {
            var construction = mathTable.Variants.GetVerticalConstruction(glyphId);
            if (construction is null)
                return null;

            var referenceFont = metrics.ResolveFont(sizePt);
            var targetDesignUnits = targetExtent / sizePt * referenceFont.FontUnitsPerEm / metrics.PixelsPerPoint;

            int AdvanceWidthOf(IReadOnlyList<MathGlyphAssemblyShaper.ShapedPart> parts) =>
                parts.Max(p => referenceFont.GetGlyphAdvanceWidthDesignUnits(p.GlyphId));

            var fit = construction.Variants.FirstOrDefault(v => v.AdvanceMeasurement >= targetDesignUnits);
            if (fit != default)
            {
                MathGlyphAssemblyShaper.ShapedPart[] fitParts = [new(fit.GlyphId, 0)];
                return new StretchedGlyphResult(fitParts, fit.AdvanceMeasurement, IsAssembly: false, AdvanceWidthOf(fitParts));
            }

            if (construction.Assembly is { } assembly &&
                MathGlyphAssemblyShaper.Shape(assembly, mathTable.Variants.MinConnectorOverlap, targetDesignUnits) is { } shaped)
            {
                return new StretchedGlyphResult(shaped.Parts, shaped.SizeDesignUnits, IsAssembly: true, AdvanceWidthOf(shaped.Parts));
            }

            if (construction.Variants.Count > 0)
            {
                var largest = construction.Variants[^1];
                MathGlyphAssemblyShaper.ShapedPart[] largestParts = [new(largest.GlyphId, 0)];
                return new StretchedGlyphResult(largestParts, largest.AdvanceMeasurement, IsAssembly: false, AdvanceWidthOf(largestParts));
            }

            return null;
        }

        // ---- mfrac -----------------------------------------------------------------------------------

        static MathBox LayoutFraction(MathFractionNode frac, MathMetrics metrics)
        {
            var numerator = LayoutNode(frac.Numerator, metrics);
            var denominator = LayoutNode(frac.Denominator, metrics);
            var sizePt = EffectiveSizePt(frac, metrics);

            var ruleThickness = frac.LineThickness is { } lt
                ? metrics.Length(lt, sizePt)
                : metrics.FractionRuleThickness(sizePt);
            var numGap = metrics.FractionNumeratorGapMin(sizePt);
            var denGap = metrics.FractionDenominatorGapMin(sizePt);
            var axis = metrics.AxisHeight(sizePt);

            var width = Math.Max(numerator.InlineSize, denominator.InlineSize);
            var numX = (width - numerator.InlineSize) / 2;
            var denX = (width - denominator.InlineSize) / 2;

            // Numerator's baseline sits ruleThickness/2 + numGap + numerator.Descent above the axis;
            // denominator's baseline sits ruleThickness/2 + denGap + denominator.Ascent below it.
            var numBaselineAboveAxis = ruleThickness / 2 + numGap + numerator.Descent;
            var denBaselineBelowAxis = ruleThickness / 2 + denGap + denominator.Ascent;

            var ascent = axis + numBaselineAboveAxis + numerator.Ascent;
            var descent = -axis + denBaselineBelowAxis + denominator.Descent;

            var children = new List<MathPositionedBox>
            {
                new(numerator, numX, -(ascent - numerator.Ascent)),
                new(denominator, denX, descent - denominator.Descent),
            };

            return new MathBox
            {
                InlineSize = width,
                Ascent = ascent,
                Descent = descent,
                Children = children,
                PaintKind = ruleThickness > 0 ? MathPaintKind.Rule : MathPaintKind.None,
                RuleX = 0,
                RuleY = -axis - ruleThickness / 2,
                RuleWidth = width,
                RuleHeight = ruleThickness,
                Color = frac.Color,
            };
        }

        // ---- msqrt / mroot -----------------------------------------------------------------------------

        static MathBox LayoutRadical(MathRadicalNode rad, MathMetrics metrics)
        {
            var radicand = LayoutNode(rad.Radicand, metrics);
            var sizePt = EffectiveSizePt(rad, metrics);
            var ruleThickness = metrics.RadicalRuleThickness(sizePt);
            var gap = metrics.RadicalVerticalGap(sizePt);
            var extraAscender = metrics.RadicalExtraAscender(sizePt);

            var ascent = radicand.Ascent + gap + ruleThickness + extraAscender;
            var descent = radicand.Descent;

            // The radical sign itself (U+221A), stretched via the same MathVariants mechanism
            // StretchToken uses for fences, placed at the radicand's own baseline. Silently omitted
            // (blank reserved space only, sized by a flat fraction of the current size) when the font
            // has no MATH table or no vertical construction for U+221A - matches this box's own
            // PaintKind.Rule vinculum-only fallback.
            MathBox? signBox = null;
            var signWidth = sizePt * 0.75;
            var signFont = metrics.ResolveFont(sizePt);
            if (signFont.MathTable is { } signMathTable && System.Text.Rune.TryCreate(0x221A, out var radicalRune))
            {
                var signGlyphId = (ushort)signFont.GetGlyphIndex(radicalRune);
                if (SelectVerticalVariant(signMathTable, signGlyphId, ascent + descent, sizePt, metrics) is { } signVariant)
                {
                    var signScale = sizePt / signFont.FontUnitsPerEm * metrics.PixelsPerPoint;
                    if (signVariant.AdvanceWidthDesignUnits > 0)
                        signWidth = signVariant.AdvanceWidthDesignUnits * signScale;

                    signBox = new MathBox
                    {
                        InlineSize = signWidth,
                        Ascent = ascent,
                        Descent = descent,
                        Children = [],
                        PaintKind = MathPaintKind.Glyphs,
                        Font = signFont,
                        Glyphs = PositionStretchedGlyphs(signVariant, descent, signScale),
                        Color = rad.Color,
                    };
                }
            }

            var width = signWidth + radicand.InlineSize;

            // Everything from here on (radicand + sign) shifts right by groupX to make room for the
            // index, when present (MathML 3 §3.3.4's kernBeforeDegree/kernAfterDegree).
            var indexWidth = 0.0;
            var groupX = signWidth;
            var children = new List<MathPositionedBox>();

            if (rad.Index is { } indexNode)
            {
                var index = LayoutNode(indexNode, metrics);
                var raise = ascent * metrics.RadicalDegreeBottomRaisePercent(sizePt) / 100.0;
                indexWidth = index.InlineSize + metrics.RadicalKernBeforeDegree(sizePt);
                groupX = indexWidth + signWidth + metrics.RadicalKernAfterDegree(sizePt);
                width += indexWidth + metrics.RadicalKernAfterDegree(sizePt);
                children.Add(new MathPositionedBox(index, metrics.RadicalKernBeforeDegree(sizePt), -raise));
            }

            children.Add(new MathPositionedBox(radicand, groupX, 0));
            if (signBox is not null)
                children.Add(new MathPositionedBox(signBox, groupX - signWidth, 0));

            return new MathBox
            {
                InlineSize = width,
                Ascent = ascent,
                Descent = descent,
                Children = children,
                PaintKind = MathPaintKind.Rule,
                RuleX = groupX,
                RuleY = -ascent + extraAscender,
                RuleWidth = radicand.InlineSize,
                RuleHeight = ruleThickness,
                Color = rad.Color,
            };
        }

        // ---- msub / msup / msubsup ----------------------------------------------------------------

        static MathBox LayoutScript(MathScriptNode script, MathMetrics metrics)
        {
            var baseBox = LayoutNode(script.Base, metrics);
            var sizePt = EffectiveSizePt(script, metrics);

            var subBox = script.Sub is { } s ? LayoutNode(s, metrics) : null;
            var supBox = script.Sup is { } u ? LayoutNode(u, metrics) : null;

            var subShift = subBox is null ? 0 : Math.Max(
                metrics.SubscriptShiftDown(sizePt),
                subBox.Ascent - metrics.SubscriptTopMax(sizePt));
            var supShift = supBox is null ? 0 : Math.Max(
                metrics.SuperscriptShiftUp(sizePt),
                subBox is null ? 0 : 0);

            var scriptGap = metrics.SubSuperscriptGapMin(sizePt);
            if (subBox is not null && supBox is not null)
            {
                // Keep at least scriptGap of clearance between the two scripts (MathML Core's own
                // subSuperscriptGapMin rule) by pushing the superscript up further if needed.
                var clearance = (supShift - supBox.Descent) - (-subShift + subBox.Ascent);
                if (clearance < scriptGap)
                    supShift += scriptGap - clearance;
            }

            var children = new List<MathPositionedBox> { new(baseBox, 0, 0) };
            var x = baseBox.InlineSize;
            var maxScriptWidth = 0.0;

            if (supBox is not null)
            {
                children.Add(new MathPositionedBox(supBox, x, -supShift));
                maxScriptWidth = Math.Max(maxScriptWidth, supBox.InlineSize);
            }
            if (subBox is not null)
            {
                children.Add(new MathPositionedBox(subBox, x, subShift));
                maxScriptWidth = Math.Max(maxScriptWidth, subBox.InlineSize);
            }

            var ascent = Math.Max(baseBox.Ascent, supBox is null ? 0 : supBox.Ascent + supShift);
            var descent = Math.Max(baseBox.Descent, subBox is null ? 0 : subBox.Descent + subShift);

            return new MathBox
            {
                InlineSize = x + maxScriptWidth,
                Ascent = ascent,
                Descent = descent,
                Children = children,
                Color = script.Color,
            };
        }

        // ---- munder / mover / munderover -----------------------------------------------------------

        static MathBox LayoutUnderOver(MathUnderOverNode uo, MathMetrics metrics)
        {
            var baseBox = LayoutNode(uo.Base, metrics);
            var sizePt = EffectiveSizePt(uo, metrics);
            var gap = metrics.StretchStackGapAboveMin(sizePt);

            var children = new List<MathPositionedBox> { new(baseBox, 0, 0) };
            var width = baseBox.InlineSize;
            var ascent = baseBox.Ascent;
            var descent = baseBox.Descent;

            if (uo.Over is { } overNode)
            {
                var over = LayoutNode(overNode, metrics);
                var overX = (Math.Max(width, over.InlineSize) - over.InlineSize) / 2;
                var overY = -(ascent + gap + over.Descent);
                children.Add(new MathPositionedBox(over, overX, overY));
                width = Math.Max(width, over.InlineSize);
                ascent = ascent + gap + over.Ascent + over.Descent;
            }

            if (uo.Under is { } underNode)
            {
                var under = LayoutNode(underNode, metrics);
                var underX = (Math.Max(width, under.InlineSize) - under.InlineSize) / 2;
                var underY = descent + gap + under.Ascent;
                children.Add(new MathPositionedBox(under, underX, underY));
                width = Math.Max(width, under.InlineSize);
                descent = descent + gap + under.Ascent + under.Descent;
            }

            // Re-center the base horizontally now that width may have grown.
            children[0] = new MathPositionedBox(baseBox, (width - baseBox.InlineSize) / 2, 0);
            for (int i = 1; i < children.Count; i++)
            {
                var c = children[i];
                var centeredX = (width - c.Box.InlineSize) / 2;
                children[i] = c with { X = centeredX };
            }

            return new MathBox { InlineSize = width, Ascent = ascent, Descent = descent, Children = children, Color = uo.Color };
        }

        // ---- mmultiscripts -----------------------------------------------------------------------------

        static MathBox LayoutMultiscripts(MathMultiscriptsNode ms, MathMetrics metrics)
        {
            var baseBox = LayoutNode(ms.Base, metrics);
            var sizePt = EffectiveSizePt(ms, metrics);
            var subShift = metrics.SubscriptShiftDown(sizePt);
            var supShift = metrics.SuperscriptShiftUp(sizePt);

            var children = new List<MathPositionedBox>();
            double x = 0;
            double ascent = baseBox.Ascent, descent = baseBox.Descent;

            void PlacePair(MathScriptPair pair, ref double cursorX)
            {
                var pairWidth = 0.0;
                if (pair.Sub is { } subNode)
                {
                    var sub = LayoutNode(subNode, metrics);
                    children.Add(new MathPositionedBox(sub, cursorX, subShift));
                    pairWidth = Math.Max(pairWidth, sub.InlineSize);
                    descent = Math.Max(descent, sub.Descent + subShift);
                }
                if (pair.Sup is { } supNode)
                {
                    var sup = LayoutNode(supNode, metrics);
                    children.Add(new MathPositionedBox(sup, cursorX, -supShift));
                    pairWidth = Math.Max(pairWidth, sup.InlineSize);
                    ascent = Math.Max(ascent, sup.Ascent + supShift);
                }
                cursorX += pairWidth;
            }

            foreach (var pair in ms.PreScripts)
                PlacePair(pair, ref x);

            children.Add(new MathPositionedBox(baseBox, x, 0));
            x += baseBox.InlineSize;

            foreach (var pair in ms.PostScripts)
                PlacePair(pair, ref x);

            return new MathBox { InlineSize = x, Ascent = ascent, Descent = descent, Children = children, Color = ms.Color };
        }

        // ---- mtable ------------------------------------------------------------------------------------

        static MathBox LayoutTable(MathTableNode table, MathMetrics metrics)
        {
            var sizePt = EffectiveSizePt(table, metrics);
            var rowSpacing = metrics.StackGapMin(sizePt);
            var colSpacing = metrics.ThickMathSpace(sizePt);

            var rowBoxes = table.Rows
                .Select(row => row.Cells.Select(cell => LayoutNode(cell.Content, metrics)).ToArray())
                .ToArray();

            var columnCount = rowBoxes.Length == 0 ? 0 : rowBoxes.Max(r => r.Length);
            var columnWidths = new double[columnCount];
            for (int c = 0; c < columnCount; c++)
                columnWidths[c] = rowBoxes.Where(r => c < r.Length).Select(r => r[c].InlineSize).DefaultIfEmpty(0).Max();

            var rowHeights = rowBoxes.Select(r => (
                Ascent: r.Select(b => b.Ascent).DefaultIfEmpty(0).Max(),
                Descent: r.Select(b => b.Descent).DefaultIfEmpty(0).Max())).ToArray();

            var children = new List<MathPositionedBox>();
            double totalWidth = columnWidths.Sum() + Math.Max(0, columnCount - 1) * colSpacing;
            double y = -(rowHeights.Sum(r => r.Ascent + r.Descent) + Math.Max(0, rowBoxes.Length - 1) * rowSpacing) / 2;

            for (int r = 0; r < rowBoxes.Length; r++)
            {
                y += rowHeights[r].Ascent;
                double x = 0;
                for (int c = 0; c < rowBoxes[r].Length; c++)
                {
                    var cellBox = rowBoxes[r][c];
                    var cellX = x + (columnWidths[c] - cellBox.InlineSize) / 2; // centered - see accepted gap for columnalign
                    children.Add(new MathPositionedBox(cellBox, cellX, y));
                    x += columnWidths[c] + colSpacing;
                }
                y += rowHeights[r].Descent + rowSpacing;
            }

            var axis = metrics.AxisHeight(sizePt);
            var totalAscent = rowHeights.Sum(r => r.Ascent + r.Descent) / 2 + Math.Max(0, rowBoxes.Length - 1) * rowSpacing / 2 + axis;
            var totalDescent = rowHeights.Sum(r => r.Ascent + r.Descent) / 2 + Math.Max(0, rowBoxes.Length - 1) * rowSpacing / 2 - axis;

            return new MathBox
            {
                InlineSize = totalWidth,
                Ascent = Math.Max(0, totalAscent),
                Descent = Math.Max(0, totalDescent),
                Children = children,
                Color = table.Color,
            };
        }

        // ---- mspace / mphantom -----------------------------------------------------------------------

        static MathBox LayoutSpace(MathSpaceNode space, MathMetrics metrics)
        {
            var sizePt = EffectiveSizePt(space, metrics);
            return new MathBox
            {
                InlineSize = metrics.Length(space.Width, sizePt),
                Ascent = metrics.Length(space.Height, sizePt),
                Descent = metrics.Length(space.Depth, sizePt),
                Children = [],
                Color = space.Color,
            };
        }

        static MathBox LayoutPhantom(MathPhantomNode phantom, MathMetrics metrics)
        {
            var content = LayoutNode(phantom.Content, metrics);
            // Same size as the content, but with no paint data of its own and no visible children -
            // MathRenderer draws nothing for it, exactly matching mphantom's "reserve the space, draw
            // nothing" semantics.
            return new MathBox
            {
                InlineSize = content.InlineSize,
                Ascent = content.Ascent,
                Descent = content.Descent,
                Children = [],
                Color = phantom.Color,
            };
        }

        // ---- mpadded -----------------------------------------------------------------------------------

        /// <summary>MathML Core §3.3.6's "requested parameters" - a <c>width</c>/<c>height</c>/<c>depth</c>/
        /// <c>lspace</c>/<c>voffset</c> attribute resolves to <paramref name="fallback"/> when it's absent,
        /// unparseable, or (per spec) a percentage - <c>mpadded</c>'s attributes are plain CSS
        /// <c>&lt;length-percentage&gt;</c>s, not MathML's own relative "+2pt"/"150%" adjustment grammar,
        /// and a percentage value is explicitly excluded from every one of the five "resolved value"
        /// branches (falling through to that attribute's own default instead of being resolved against
        /// anything).</summary>
        static double ResolvedPaddedLength(string? raw, double fallback, double sizePt, MathMetrics metrics)
        {
            if (MathAttributeParser.TryParseLength(raw) is not { Unit: not MathLengthUnit.Percent } length)
                return fallback;
            return metrics.Length(length, sizePt);
        }

        static MathBox LayoutPadded(MathPaddedNode padded, MathMetrics metrics)
        {
            var inner = LayoutNode(padded.Content, metrics);
            var sizePt = EffectiveSizePt(padded, metrics);

            var width = ResolvedPaddedLength(padded.Width, inner.InlineSize, sizePt, metrics);
            // Per spec, height AND depth both default to the inner box's line-ascent (not line-descent).
            var height = Math.Max(0, ResolvedPaddedLength(padded.Height, inner.Ascent, sizePt, metrics));
            var depth = Math.Max(0, ResolvedPaddedLength(padded.Depth, inner.Ascent, sizePt, metrics));
            var lspace = Math.Max(0, ResolvedPaddedLength(padded.LSpace, 0, sizePt, metrics));
            // voffset is deliberately not clamped - a negative value is valid and shifts content down.
            var voffset = ResolvedPaddedLength(padded.VOffset, 0, sizePt, metrics);

            return new MathBox
            {
                InlineSize = width,
                Ascent = height,
                Descent = depth,
                // The inner box's own alphabetic baseline shifts away from this box's baseline, towards
                // line-over (up), by voffset - PeachPDF's Y-down convention negates that.
                Children = [new MathPositionedBox(inner, lspace, -voffset)],
                Color = padded.Color,
            };
        }
    }
}
