using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.MathML;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-engine coverage for MathML (mirrors <c>FlexboxIntegrationTests</c>/
    /// <c>MulticolLayoutIntegrationTests</c>'s pattern): build a real <see cref="HtmlContainerInt"/>,
    /// run layout, then assert on the resulting <see cref="MathBox"/> tree's geometry - not just that
    /// layout completes without throwing.
    /// </summary>
    public class MathLayoutIntegrationTests
    {
        static string FontFace() => BundledFonts.FontFaceRule(BundledFonts.Math, "TestMath", "font/truetype");

        static string Wrap(string mathHtml) =>
            $"<html><head><style>{FontFace()} math {{ font-family: TestMath; font-size: 20pt; }}</style></head><body>{mathHtml}</body></html>";

        static async Task<MathBox> LayoutMath(string mathHtml)
        {
            var html = Wrap(mathHtml);
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            var mathBox = FindByTag(container.Root!, "math") as CssBoxMath;
            Assert.NotNull(mathBox);
            Assert.NotNull(mathBox!.Layout);
            return mathBox.Layout!;
        }

        static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, System.StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        [Fact]
        public async Task MathWord_IsImageNotSpaces_ToStringIsMath()
        {
            var html = Wrap("<math><mi>x</mi></math>");
            var (root, _) = await LayoutMathBox(html);
            var mathBox = (CssBoxMath)FindByTag(root, "math")!;

            Assert.True(mathBox.MathWord.IsImage);
            Assert.False(mathBox.MathWord.IsSpaces);
            Assert.Equal("Math", mathBox.MathWord.ToString());
        }

        static async Task<(CssBox Root, HtmlContainerInt Container)> LayoutMathBox(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return (container.Root!, container);
        }

        [Fact]
        public async Task SimpleIdentifier_HasPositiveSizeAndTextPaint()
        {
            var box = await LayoutMath("<math><mi>x</mi></math>");

            Assert.True(box.InlineSize > 0);
            Assert.True(box.Ascent > 0);
            Assert.Equal(MathPaintKind.Text, box.PaintKind);
            // MathML Core's mi { text-transform: math-auto } default (Appendix C.1) substitutes a
            // single-character mi's content with its Unicode "math italic" codepoint - "x" (U+0078)
            // becomes MATHEMATICAL ITALIC SMALL X (U+1D465), not the plain ASCII letter.
            Assert.Equal(char.ConvertFromUtf32(0x1D465), box.Text);
        }

        [Fact]
        public async Task Identifier_MultiCharacter_IsNotItalicized()
        {
            var box = await LayoutMath("<math><mi>exp</mi></math>");
            Assert.Equal("exp", box.Text);
        }

        [Fact]
        public async Task Identifier_MathVariantNormal_CancelsAutomaticItalic()
        {
            var box = await LayoutMath("<math><mi mathvariant=\"normal\">x</mi></math>");
            Assert.Equal("x", box.Text);
        }

        [Fact]
        public async Task NumberAndText_AreNeverItalicized()
        {
            var mn = await LayoutMath("<math><mn>1</mn></math>");
            Assert.Equal("1", mn.Text);

            var mtext = await LayoutMath("<math><mtext>a</mtext></math>");
            Assert.Equal("a", mtext.Text);
        }

        [Fact]
        public async Task Row_ConcatenatesChildrenLeftToRight()
        {
            var box = await LayoutMath("<math><mi>x</mi><mo>+</mo><mi>y</mi></math>");

            Assert.Equal(3, box.Children.Count);
            // Each child's X strictly increases - left to right layout.
            Assert.True(box.Children[0].X < box.Children[1].X);
            Assert.True(box.Children[1].X < box.Children[2].X);
            // All children share the row's own baseline (Y=0).
            Assert.All(box.Children, c => Assert.Equal(0, c.Y));
        }

        [Fact]
        public async Task Fraction_NumeratorAboveDenominator_BothCenteredOverWidestOperand()
        {
            var box = await LayoutMath("<math><mfrac><mi>a</mi><mi>bb</mi></mfrac></math>");

            Assert.Equal(MathPaintKind.Rule, box.PaintKind);
            Assert.True(box.RuleHeight > 0);
            Assert.Equal(2, box.Children.Count);

            var numerator = box.Children[0];
            var denominator = box.Children[1];

            // Numerator sits above the row's own baseline (negative Y), denominator below (positive Y).
            Assert.True(numerator.Y < 0);
            Assert.True(denominator.Y > 0);

            // The denominator ("bb") is the wider operand, so the narrower numerator ("a") is
            // horizontally centered relative to it - its X offset should be positive (indented).
            Assert.True(denominator.Box.InlineSize > numerator.Box.InlineSize);
            Assert.True(numerator.X > 0);
        }

        [Fact]
        public async Task Superscript_ShiftsUp_SubscriptShiftsDown()
        {
            var supBox = await LayoutMath("<math><msup><mi>x</mi><mn>2</mn></msup></math>");
            Assert.Equal(2, supBox.Children.Count);
            var supBase = supBox.Children[0];
            var sup = supBox.Children[1];
            Assert.Equal(0, supBase.Y);
            Assert.True(sup.Y < 0); // superscript raised above the baseline
            Assert.True(sup.X >= supBase.Box.InlineSize); // placed after the base

            var subBox = await LayoutMath("<math><msub><mi>x</mi><mn>1</mn></msub></math>");
            Assert.Equal(2, subBox.Children.Count);
            var sub = subBox.Children[1];
            Assert.True(sub.Y > 0); // subscript dropped below the baseline
        }

        [Fact]
        public async Task Subsup_ScriptsAreSmallerThanBase()
        {
            var box = await LayoutMath("<math><msubsup><mi>x</mi><mn>1</mn><mn>2</mn></msubsup></math>");
            Assert.Equal(3, box.Children.Count);

            var baseBox = box.Children[0].Box;
            var subBox = box.Children[1].Box;
            var supBox = box.Children[2].Box;

            // scriptlevel+1 scales the font down (ScriptPercentScaleDown), so both scripts should
            // measure smaller than the base token.
            Assert.True(subBox.Ascent + subBox.Descent < baseBox.Ascent + baseBox.Descent);
            Assert.True(supBox.Ascent + supBox.Descent < baseBox.Ascent + baseBox.Descent);
        }

        [Fact]
        public async Task Radical_ReservesSpaceForSignAndDrawsRule()
        {
            var noSqrt = await LayoutMath("<math><mi>x</mi></math>");
            var sqrt = await LayoutMath("<math><msqrt><mi>x</mi></msqrt></math>");

            Assert.Equal(MathPaintKind.Rule, sqrt.PaintKind);
            Assert.True(sqrt.RuleHeight > 0);
            // The radicand, offset right to make room for the sign, plus (when the font has one, as
            // STIX Two Math does) the radical sign glyph itself.
            Assert.True(sqrt.Children.Count is 1 or 2);
            var radicandChild = sqrt.Children.Single(c => c.X > 0);
            Assert.True(radicandChild.X > 0);
            // A radical is taller and wider than its bare radicand (sign + vertical gap reserved).
            Assert.True(sqrt.InlineSize > noSqrt.InlineSize);
            Assert.True(sqrt.Ascent > noSqrt.Ascent);
        }

        [Fact]
        public async Task Radical_SignWidth_MatchesChosenVariantsRealAdvanceWidth_NotFlatApproximation()
        {
            // At this test's 20pt font-size, a single "x" radicand's height selects STIX Two Math's
            // uni221A.s1 (glyph 1658, real hmtx advance 1041 design units - independently confirmed with
            // fontTools) for the radical sign, not the base uni221A glyph the old sizePt*0.75 flat
            // approximation had no relation to at all: 1041/1000*20 = 20.82pt, not 20*0.75 = 15pt.
            var sqrt = await LayoutMath("<math><msqrt><mi>x</mi></msqrt></math>");

            var signChild = sqrt.Children.Single(c => c.X == 0);
            Assert.Equal(MathPaintKind.Glyphs, signChild.Box.PaintKind);
            Assert.Equal([1658], signChild.Box.Glyphs!.Select(g => g.GlyphIndex));
            Assert.Equal(20.82, signChild.Box.InlineSize, 2);
        }

        [Fact]
        public async Task Mroot_HasIndexPositionedAboveAndLeftOfRadicand()
        {
            var box = await LayoutMath("<math><mroot><mi>x</mi><mn>3</mn></mroot></math>");

            // Index + radicand (+ optional sign glyph box when the font has one - STIX Two Math does).
            Assert.True(box.Children.Count is 2 or 3);
            var index = box.Children[0];
            Assert.True(index.Y < 0); // raised above the baseline
        }

        [Fact]
        public async Task Table_ProducesGridOfPositionedCells()
        {
            var box = await LayoutMath(
                "<math><mtable><mtr><mtd><mn>1</mn></mtd><mtd><mn>0</mn></mtd></mtr>" +
                "<mtr><mtd><mn>0</mn></mtd><mtd><mn>1</mn></mtd></mtr></mtable></math>");

            Assert.Equal(4, box.Children.Count);

            // Two distinct rows (different Y) and two distinct columns (different X).
            var ys = box.Children.Select(c => c.Y).Distinct().ToList();
            var xs = box.Children.Select(c => c.X).Distinct().ToList();
            Assert.Equal(2, ys.Count);
            Assert.Equal(2, xs.Count);
        }

        [Fact]
        public async Task StretchyFence_GrowsToMatchTallerSibling()
        {
            // A single-character fence next to a fraction should stretch taller than its own natural
            // (unstretched) size to visually enclose the fraction.
            var plain = await LayoutMath("<math><mo>(</mo></math>");
            var stretched = await LayoutMath(
                "<math><mrow><mo stretchy=\"true\">(</mo><mfrac><mi>x</mi><mi>y</mi></mfrac><mo stretchy=\"true\">)</mo></mrow></math>");

            Assert.True(stretched.Children.Count >= 3);
            var openParen = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, openParen.PaintKind);
            Assert.True(openParen.Ascent + openParen.Descent > plain.Ascent + plain.Descent);
        }

        [Fact]
        public async Task StretchyFence_TallerThanLargestPresizedVariant_AssemblesFromParts()
        {
            // STIX Two Math's largest pre-sized parenleft variant tops out at 3821 design units
            // (MathTableTests.Variants_VerticalConstruction_ForParenLeft...) - 3.821em, ~76pt at this
            // test's 20pt font-size. A 30-row table comfortably exceeds that, forcing the fence past
            // every pre-sized variant and into MathML Core §5.3.2's glyph-assembly construction.
            var rows = string.Concat(Enumerable.Range(0, 30).Select(_ => "<mtr><mtd><mn>1</mn></mtd></mtr>"));
            var stretched = await LayoutMath(
                $"<math><mrow><mo stretchy=\"true\">(</mo><mtable>{rows}</mtable><mo stretchy=\"true\">)</mo></mrow></math>");

            Assert.True(stretched.Children.Count >= 3);
            var openParen = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, openParen.PaintKind);
            // An assembled shape is drawn from more than one glyph (top/extender/bottom parts, with the
            // extender repeated as needed) - a single pre-sized variant is always exactly one glyph.
            Assert.True(openParen.Glyphs!.Count > 1);
            // Every part's Y strictly decreases (PeachPDF's Y-down convention: moving further up the
            // page) as parts are shaped bottom-to-top, so consecutive parts never land on top of each
            // other.
            var ys = openParen.Glyphs!.Select(g => g.Y).ToList();
            for (int i = 1; i < ys.Count; i++)
                Assert.True(ys[i] < ys[i - 1]);
        }

        [Fact]
        public async Task StretchyFence_InlineSize_MatchesChosenVariantsRealAdvanceWidth_NotBaseGlyphs()
        {
            // The showcase's 2x2 identity matrix: at this test's 20pt font-size, SelectVerticalVariant
            // picks STIX Two Math's parenleft.s8 (glyph 1308, real hmtx advance 542 design units) to
            // cover the table's height - not the base "(" glyph's own 357 design units. Independently
            // confirmed with fontTools (357/1000*20 = 7.14pt base vs. 542/1000*20 = 10.84pt real - the
            // reserved InlineSize before this fix always matched the smaller, wrong figure).
            var stretched = await LayoutMath(
                "<math><mrow><mo stretchy=\"true\">(</mo><mtable>" +
                "<mtr><mtd><mn>1</mn></mtd><mtd><mn>0</mn></mtd></mtr>" +
                "<mtr><mtd><mn>0</mn></mtd><mtd><mn>1</mn></mtd></mtr>" +
                "</mtable><mo stretchy=\"true\">)</mo></mrow></math>");

            Assert.True(stretched.Children.Count >= 3);
            var openParen = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, openParen.PaintKind);
            Assert.Equal([1308], openParen.Glyphs!.Select(g => g.GlyphIndex));
            Assert.Equal(10.84, openParen.InlineSize, 2);
        }

        [Fact]
        public async Task StretchyFence_Assembled_InlineSize_UsesAssemblyPartsRealAdvanceWidth()
        {
            // Same 30-row setup as StretchyFence_TallerThanLargestPresizedVariant_AssemblesFromParts,
            // forcing the glyph-assembly path (not a single pre-sized variant). STIX Two Math's three
            // parenleft assembly parts (gids 4862/4861/4860 - top cap, extender, bottom cap) each carry
            // the same real hmtx advance, 484 design units (9.68pt @ 20pt) - independently confirmed
            // with fontTools - which is what SelectVerticalVariant's Max(...) should resolve to, still
            // wider than the base "(" glyph's own 357 units (7.14pt).
            var rows = string.Concat(Enumerable.Range(0, 30).Select(_ => "<mtr><mtd><mn>1</mn></mtd></mtr>"));
            var stretched = await LayoutMath(
                $"<math><mrow><mo stretchy=\"true\">(</mo><mtable>{rows}</mtable><mo stretchy=\"true\">)</mo></mrow></math>");

            Assert.True(stretched.Children.Count >= 3);
            var openParen = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, openParen.PaintKind);
            Assert.True(openParen.Glyphs!.Count > 1); // assembled, not a single pre-sized variant
            Assert.Equal(9.68, openParen.InlineSize, 2);
        }

        [Fact]
        public async Task StretchyFence_NoAssemblyData_ClampsToLargestVariantsRealAdvanceWidth()
        {
            // "/" (slash) is a real STIX Two Math glyph with pre-sized vertical variants but no
            // GlyphAssembly data at all - SelectVerticalVariant's final "nothing covers the target, use
            // the largest available" fallback (matching MathML Core §5.3.2's own last-resort clamp),
            // distinct from both the pre-sized-fit and assembled-shape branches the other StretchyFence_*
            // tests exercise. Its largest variant (slash.s4, glyph 1384) has a real hmtx advance of 1340
            // design units (26.8pt @ 20pt) - independently confirmed with fontTools.
            var rows = string.Concat(Enumerable.Range(0, 10).Select(_ => "<mtr><mtd><mn>1</mn></mtd></mtr>"));
            var stretched = await LayoutMath($"<math><mrow><mo stretchy=\"true\">/</mo><mtable>{rows}</mtable></mrow></math>");

            var slash = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, slash.PaintKind);
            Assert.Equal([1384], slash.Glyphs!.Select(g => g.GlyphIndex)); // clamped, not assembled
            Assert.Equal(26.8, slash.InlineSize, 2);
        }

        [Fact]
        public async Task Mpadded_WidthOverridesInlineSizeWithoutRescalingContent()
        {
            var natural = await LayoutMath("<math><mi>x</mi></math>");
            var padded = await LayoutMath("<math><mpadded width=\"5em\"><mi>x</mi></mpadded></math>");

            // 5em at this test's 20pt font-size is 100pt - the box's own reported size, independent of
            // the content's own (unrescaled, still natural-sized) width.
            Assert.Equal(100, padded.InlineSize, 3);
            Assert.Single(padded.Children);
            Assert.Equal(natural.InlineSize, padded.Children[0].Box.InlineSize, 3);
        }

        [Fact]
        public async Task Mpadded_HeightAndDepth_OverrideAscentAndDescent()
        {
            var padded = await LayoutMath("<math><mpadded height=\"1em\" depth=\"3em\"><mi>x</mi></mpadded></math>");
            Assert.Equal(20, padded.Ascent, 3);  // 1em @ 20pt
            Assert.Equal(60, padded.Descent, 3); // 3em @ 20pt
        }

        [Fact]
        public async Task Mpadded_DepthAbsent_DefaultsToInnerLineAscent_NotDescent()
        {
            // MathML Core §3.3.6.1: depth's default (absent/invalid/percentage) is the inner box's
            // line-ascent, not its line-descent - the same default height uses.
            var natural = await LayoutMath("<math><mi>x</mi></math>");
            var padded = await LayoutMath("<math><mpadded height=\"1em\"><mi>x</mi></mpadded></math>");
            Assert.Equal(natural.Ascent, padded.Descent, 3);
        }

        [Fact]
        public async Task Mpadded_LSpaceAndVOffset_PositionContentWithinTheBox()
        {
            var padded = await LayoutMath("<math><mpadded lspace=\"2em\" voffset=\"-1em\"><mi>x</mi></mpadded></math>");
            Assert.Single(padded.Children);
            var content = padded.Children[0];
            Assert.Equal(40, content.X, 3); // 2em @ 20pt
            Assert.Equal(20, content.Y, 3); // voffset shifts towards line-over (up); Y-down negates it
        }

        [Fact]
        public async Task Mpadded_PercentageAttribute_FallsBackToDefault_NotResolvedAsPercentOfContent()
        {
            // MathML Core §3.3.6.1: a percentage value is explicitly excluded from being "resolved" for
            // every one of width/height/depth/lspace/voffset - it falls back to that attribute's own
            // default exactly as if it were absent.
            var natural = await LayoutMath("<math><mi>x</mi></math>");
            var padded = await LayoutMath("<math><mpadded width=\"150%\"><mi>x</mi></mpadded></math>");
            Assert.Equal(natural.InlineSize, padded.InlineSize, 3);
        }

        [Fact]
        public async Task PlusSign_UsesMediumMathSpace_NotFlatThickMathSpace()
        {
            // "+" is operator-dictionary category B (mediummathspace, 4/18em) - the flat thickmathspace
            // (5/18em) approximation this replaced would have produced 100pt of gap at this test's 20pt
            // font-size instead of 80pt.
            var box = await LayoutMath("<math><mi>x</mi><mo>+</mo><mi>y</mi></math>");
            var x = box.Children[0];
            var plus = box.Children[1];
            var gap = plus.X - (x.X + x.Box.InlineSize);
            Assert.Equal(4.0 / 18 * 20, gap, 2); // mediummathspace (4/18em) at 20pt
        }

        [Fact]
        public async Task BareFenceCharacter_StretchesByDefault_WithoutExplicitStretchyAttribute()
        {
            // "(" is operator-dictionary category F: stretchy by default, per MathML Core's own dictionary
            // - an author shouldn't need to write stretchy="true"/fence="true" explicitly for it to work.
            var plain = await LayoutMath("<math><mo>(</mo></math>");
            var stretched = await LayoutMath(
                "<math><mrow><mo>(</mo><mfrac><mi>x</mi><mi>y</mi></mfrac><mo>)</mo></mrow></math>");

            var openParen = stretched.Children[0].Box;
            Assert.Equal(MathPaintKind.Glyphs, openParen.PaintKind);
            Assert.True(openParen.Ascent + openParen.Descent > plain.Ascent + plain.Descent);
        }

        [Fact]
        public async Task NoMathTable_FallsBackToApproximateConstants_WithoutThrowing()
        {
            // No font-family override - resolves to whatever default font the test environment has,
            // almost certainly with no MATH table, exercising MathMetrics' fallback path.
            var html = "<html><body><math><mfrac><mi>x</mi><mi>y</mi></mfrac></math></body></html>";
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var mathBox = FindByTag(container.Root!, "math") as CssBoxMath;
            Assert.NotNull(mathBox?.Layout);
            Assert.True(mathBox!.Layout!.InlineSize > 0);
        }

        [Fact]
        public async Task CssWidth_SmallerThanNaturalContent_SizesTheBoxWithoutRescalingContent()
        {
            var html = Wrap("<math style=\"width: 1pt\"><mfrac><mi>x</mi><mi>y</mi></mfrac></math>");
            var (root, _) = await LayoutMathBox(html);
            var mathBox = (CssBoxMath)FindByTag(root, "math")!;

            // The box itself takes the explicit (smaller-than-natural) width...
            Assert.Equal(1, mathBox.MathWord.Width, 3);
            // ...but the content laid out inside it is untouched - still its own full natural size, not
            // rescaled/shrunk to fit (per MathML Core: math is not a replaced element, so width/height
            // resize only the outer box, and content may overflow it).
            Assert.True(mathBox.Layout!.InlineSize > 1);
        }

        [Fact]
        public async Task CssWidthAndHeight_LargerThanNaturalContent_SizeTheBoxWithExtraSpace()
        {
            var html = Wrap("<math style=\"width: 500pt; height: 300pt\"><mi>x</mi></math>");
            var (root, _) = await LayoutMathBox(html);
            var mathBox = (CssBoxMath)FindByTag(root, "math")!;

            Assert.Equal(500, mathBox.MathWord.Width, 3);
            Assert.Equal(300, mathBox.MathWord.Height, 3);
            Assert.True(mathBox.Layout!.InlineSize < 500);
        }

        [Fact]
        public async Task CssHeight_Alone_OverridesOnlyThatAxis()
        {
            var natural = await LayoutMath("<math><mi>x</mi></math>");
            var html = Wrap("<math style=\"height: 200pt\"><mi>x</mi></math>");
            var (root, _) = await LayoutMathBox(html);
            var mathBox = (CssBoxMath)FindByTag(root, "math")!;

            Assert.Equal(200, mathBox.MathWord.Height, 3);
            Assert.Equal(natural.InlineSize, mathBox.MathWord.Width, 3);
        }

        [Fact]
        public async Task CssWidth_Percentage_ResolvesAgainstContainingBlock()
        {
            var html = Wrap("<div style=\"width: 400pt\"><math style=\"width: 50%\"><mi>x</mi></math></div>");
            var (root, _) = await LayoutMathBox(html);
            var mathBox = (CssBoxMath)FindByTag(root, "math")!;

            Assert.Equal(200, mathBox.MathWord.Width, 3);
        }

    }
}
