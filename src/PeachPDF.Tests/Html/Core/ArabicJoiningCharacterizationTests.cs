using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Arabic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Real-font characterization for Arabic-family cursive joining (issue #533) - unlike
    /// <see cref="GsubArabicJoiningSyntheticTests"/>'s synthetic byte-blob GSUB tables, this drives
    /// PeachPDF's actual OpenType reader/shaper against a real font (a "Noto Sans Arabic" subset - see
    /// <see cref="BundledFonts.Arabic"/>) and the real HTML layout pipeline
    /// (<see cref="CssBidiParagraphResolver"/> → <see cref="CssBox.AppendWordsFromText"/> →
    /// <see cref="DerivedStyle.ActualTextShapingFeatures"/> → <see cref="GsubShaper.Shape"/>), so a
    /// genuinely broken wiring anywhere in that chain (not just a bug in one isolated piece) would show
    /// up as a real substitution never happening - the same "prove it isn't a no-op" standard this
    /// repo's own paint/shaping-feature conventions ask for (see <c>ShapingCharacterizationTests</c>'s
    /// own doc comment). Manually verified further via actual PDF rasterization (PDFium + MuPDF) during
    /// development - see this fix's own <c>.claude/recent-fixes</c> entry for that evidence, not
    /// re-encoded as an automated test here since two-renderer rasterization is a Python-tooling
    /// verification step, not part of the C# test infrastructure.
    /// </summary>
    public class ArabicJoiningCharacterizationTests
    {
        // ARABIC LETTER BEH/YEH/TEH/ALEF/LAM - all present in the bundled subset (see
        // generate_arabic_subset.py's own KEEP_TEXT).
        private const string Beh = "ب";
        private const string Yeh = "ي";
        private const string Teh = "ت";
        private const string Alef = "ا";
        private const string Lam = "ل";

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Arabic)).Fontface;
            return new OpenTypeDescriptor("arabic-test", "arabic-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, string text, IReadOnlyList<ArabicJoiningForm> forms) =>
            descriptor.Shape(text, new TextShapingFeatures(ScriptTag: "arab", JoiningForms: forms))
                .Select(g => g.GlyphIndex).ToArray();

        [Fact]
        public void Shape_SingleMirrorableGlyphWithJoiningFormsRequested_StillMirrorsForDisplay()
        {
            // Regression: OpenTypeDescriptor.Shape's ReverseForDisplay handling used to gate its own
            // Bidi_Mirrored glyph remap (UAX #9 L4) behind the same `glyphs.Count > 1` check that
            // (correctly) skips the separately-unrelated list-reversal no-op for a single glyph - so a
            // one-glyph "joining" run whose sole character is itself mirrorable (e.g. a lone parenthesis
            // sharing a word/JoiningForms array with adjacent Arabic-script content) never got remapped.
            // A single JoiningForms entry of None keeps the positional-substitution/cursive-attachment
            // machinery a no-op here, isolating this test to the mirror-remap step alone.
            var descriptor = Descriptor();
            var shaped = descriptor.Shape("(", new TextShapingFeatures(
                JoiningForms: [ArabicJoiningForm.None], ReverseForDisplay: true));

            Assert.Single(shaped);
            var expectedMirroredGlyphIndex = descriptor.CharCodeToGlyphIndex(new System.Text.Rune(')'));
            Assert.NotEqual(0, expectedMirroredGlyphIndex);
            Assert.Equal(expectedMirroredGlyphIndex, shaped[0].GlyphIndex);
        }

        [Fact]
        public void IsolatedForm_DiffersFromInitialForm_RealFontSubstitutionIsNotANoOp()
        {
            // LAM (Dual-joining, and - confirmed directly via fontTools against the subset font - not
            // one of the letters this font's own `ccmp` feature decomposes, unlike BEH/YEH/TEH/FEH; see
            // ThreeLetterWord_EveryPositionGetsItsOwnDistinctForm for that case) - the simplest case to
            // prove positional substitution reaches a real font's actual glyph data, not a no-op.
            var descriptor = Descriptor();

            var isolated = ShapeGlyphIds(descriptor, Lam, [ArabicJoiningForm.Isol]);
            var initial = ShapeGlyphIds(descriptor, Lam, [ArabicJoiningForm.Init]);

            Assert.Single(isolated);
            Assert.Single(initial);
            Assert.NotEqual(isolated[0], initial[0]);
        }

        [Fact]
        public void ThreeLetterWord_EveryPositionGetsItsOwnDistinctForm()
        {
            // "بيت" (BEH YEH TEH) - ArabicJoiningShaper.Resolve already proved (unit-tested) this
            // resolves to [Init, Medi, Fina]. This font's own `ccmp` feature decomposes each of these
            // three dotted letters into a base glyph + a separate combining-mark glyph for the dot(s)
            // (confirmed directly via fontTools) BEFORE isol/init/medi/fina ever run - GsubShaper.Shape's
            // own ccmp/locl pre-stage (see its remarks) is what makes that decomposition actually happen
            // ahead of positional substitution, matching real font behavior; each pair's own FIRST glyph
            // (the decomposed base, which is what the positional forms actually apply to) is what
            // differs per position - the second (the dot mark) never changes with joining form.
            var descriptor = Descriptor();
            var word = Beh + Yeh + Teh;
            var forms = ArabicJoiningShaper.Resolve([Beh[0], Yeh[0], Teh[0]]);

            var shaped = descriptor.Shape(word, new TextShapingFeatures(ScriptTag: "arab", JoiningForms: forms));
            var isolatedForms = descriptor.Shape(word, new TextShapingFeatures(ScriptTag: "arab",
                JoiningForms: [ArabicJoiningForm.Isol, ArabicJoiningForm.Isol, ArabicJoiningForm.Isol]));

            // Each letter decomposes into exactly 2 glyphs (base + dot mark) under both requests.
            Assert.Equal(6, shaped.Count);
            Assert.Equal(6, isolatedForms.Count);

            // The base glyph at each letter's own first decomposed position (0, 2, 4) differs from its
            // own isolated-form base glyph, and the three base glyphs are themselves pairwise distinct
            // (Init != Medi != Fina) - real per-position substitution, not a single shared no-op.
            int[] baseIndices = [0, 2, 4];
            var joinedBases = baseIndices.Select(i => shaped[i].GlyphIndex).ToArray();
            var isolatedBases = baseIndices.Select(i => isolatedForms[i].GlyphIndex).ToArray();
            for (var i = 0; i < 3; i++)
                Assert.NotEqual(isolatedBases[i], joinedBases[i]);
            Assert.Equal(3, joinedBases.Distinct().Count());
        }

        [Fact]
        public void NoJoiningFormsRequested_SkipsTheCcmpPreStage()
        {
            // The pre-#533 behavior (no JoiningForms at all) must still work exactly as before: the
            // ccmp/locl pre-stage GsubShaper.Shape now runs is gated on JoiningForms being present (see
            // its own remarks) specifically so it never fires for a caller who never asked for Arabic-
            // family shaping - BEH stays a single, undecomposed, cmap-nominal glyph.
            var descriptor = Descriptor();

            var shaped = descriptor.Shape(Beh, TextShapingFeatures.Default);

            Assert.Single(shaped);
        }

        [Fact]
        public void LamAlef_RligStageProducesDifferentGlyphsThanPositionalSubstitutionAlone()
        {
            // LAM followed by ALEF: this real font (confirmed directly via fontTools) implements its
            // lam-alef "ligature" not as a single merged glyph, but as an rlig contextual substitution
            // (GSUB Format 3) that swaps BOTH already-positionally-substituted glyphs for a matched pair
            // of visually-connecting ".rlig"-suffixed glyph variants once they're adjacent in exactly the
            // right joining forms (uni0644.init + uni0627.fina) - still 2 glyphs, but not the same 2 as
            // plain positional substitution alone would produce. GsubShaper.Shape's own staged design
            // (positional forms first, rlig immediately after in the same pass, per ArabicJoiningShaper's
            // own remarks) is what lets this contextual rule ever match at all - the rlig lookup's own
            // coverage is keyed on the substituted glyph names, not the nominal ones.
            var descriptor = Descriptor();
            var forms = ArabicJoiningShaper.Resolve([Lam[0], Alef[0]]);

            var withRlig = descriptor.Shape(Lam + Alef, new TextShapingFeatures(
                Ligatures: LigatureFeatures.Default, ScriptTag: "arab", JoiningForms: forms))
                .Select(g => g.GlyphIndex).ToArray();
            var positionalOnly = descriptor.Shape(Lam + Alef, new TextShapingFeatures(
                Ligatures: LigatureFeatures.None, ScriptTag: "arab", JoiningForms: forms))
                .Select(g => g.GlyphIndex).ToArray();

            Assert.Equal(2, withRlig.Length);
            Assert.Equal(2, positionalOnly.Length);
            Assert.NotEqual(positionalOnly, withRlig);
        }

        [Fact]
        public async Task EndToEndLayout_LamAlef_RligFiresViaLogicalOrderShapingThenGlyphReversal()
        {
            // Regression test for the rlig/bidi-reversal fix: Arabic text is a strong-R bidi run and
            // gets L2-reversed for display regardless of the *paragraph's* own direction (even under
            // `direction: ltr`, per UAX#9 - this is not specific to an RTL paragraph). A font's own
            // `rlig` contextual ligature rule (lam-alef) is keyed on true-logical-order adjacency
            // (lam.init immediately followed by alef.fina), so it only ever matches if GSUB actually
            // sees the word in that order. CssRectWord therefore never mutates a joining-forms word's
            // own Text/EffectiveJoiningForms (they stay true logical order permanently -
            // DisplayOrderReversed just records that the word reads right-to-left) and shaping instead
            // reverses only the resulting glyph list, as its own final step, once GSUB/GPOS have both
            // already run in true logical order - see OpenTypeDescriptor.Shape's own remarks.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'ArabicTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'ArabicTest'; font-size: 14pt; }}
p {{ width: 400px; direction: ltr; }}
</style></head>
<body><p>{Lam}{Alef}</p></body>
</html>");

            // Text/joining forms stay true logical order (Lam, then Alef, matching the source markup)
            // permanently - only DisplayOrderReversed flips, recording that this word still reads
            // right-to-left on the page despite the paragraph's own `direction: ltr`.
            Assert.Equal(Lam + Alef, word.Text);
            Assert.True(word.DisplayOrderReversed);

            var descriptor = Descriptor();

            // Exactly what painting itself asks for - see CssBox.ResolveWordShapingFeatures: shape
            // word.Text (still true logical order) with ReverseForDisplay requested.
            var painted = descriptor.Shape(word.Text, new TextShapingFeatures(
                Ligatures: LigatureFeatures.Default, ScriptTag: word.ScriptTag,
                JoiningForms: word.EffectiveJoiningForms, ReverseForDisplay: word.DisplayOrderReversed));

            // The reference shape: true logical order, rlig applied, no reversal requested - what GSUB/
            // GPOS themselves produce before ReverseForDisplay's own final step runs.
            var trueLogicalOrderWithRlig = descriptor.Shape(Lam + Alef, new TextShapingFeatures(
                Ligatures: LigatureFeatures.Default, ScriptTag: "arab", JoiningForms: ArabicJoiningShaper.Resolve([Lam[0], Alef[0]])));

            Assert.Equal(2, painted.Count);
            Assert.Equal(2, trueLogicalOrderWithRlig.Count);

            // `painted` is exactly `trueLogicalOrderWithRlig` reversed - proving rlig actually fired
            // (both agree on which glyphs the lam-alef ligature resolves to) and the display order is
            // still correct (reversed, since this word reads right-to-left).
            Assert.Equal(trueLogicalOrderWithRlig[0].GlyphIndex, painted[1].GlyphIndex);
            Assert.Equal(trueLogicalOrderWithRlig[1].GlyphIndex, painted[0].GlyphIndex);
        }

        [Theory]
        [InlineData("تب")] // Teh (init) + Beh (fina) - Beh's own fina base has an unusually wide
                            // (~1093 design unit) advance, a long connecting swash, which is what made
                            // this specific ordering visually dramatic: pre-fix, Beh's own dot rendered
                            // roughly a full base-glyph-width away from the letter it belongs to.
        [InlineData("بت")] // Beh (init) + Teh (fina) - the same letters, opposite joining-form
                            // assignment, confirming the fix isn't order-specific.
        public async Task EndToEndLayout_MarkStaysAttachedToItsOwnBaseAfterDisplayReversal(string text)
        {
            // Regression test for a second, distinct bug the same rasterization pass that caught the
            // rlig/reversal issue (see the lam-alef test above) also turned up: GposPositioner.ApplyMarkAnchor's
            // XOffset formula bakes in the pen-distance from a mark glyph to its own base, under the walk
            // order GPOS actually ran in (true logical order) - reversing the glyph list for display
            // afterward, without adjusting for the new walk order, shifts every attached mark by roughly
            // its own base's advance width, since the base and mark are no longer separated by that same
            // pen distance in the new order. See OpenTypeDescriptor.ReverseGlyphsForDisplay's own remarks
            // for the fix: resolve each glyph's desired absolute position before reversing, using the new
            // ShapedGlyph.AttachedToIndex back-reference to keep an attached mark's position tied to
            // wherever its base ends up, rather than reusing an offset computed for the old walk order.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'ArabicTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'ArabicTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{text}</p></body>
</html>");

            Assert.True(word.DisplayOrderReversed);

            var descriptor = Descriptor();
            var shaped = descriptor.Shape(word.Text!, new TextShapingFeatures(
                Ligatures: LigatureFeatures.Default, ScriptTag: word.ScriptTag,
                JoiningForms: word.EffectiveJoiningForms, ReverseForDisplay: word.DisplayOrderReversed));

            // Walk the already-reversed (display-order) glyph list computing each glyph's absolute X -
            // exactly what painting itself does (cumulative advance, plus this glyph's own XOffset).
            var absoluteX = new double[shaped.Count];
            double pen = 0;
            for (var i = 0; i < shaped.Count; i++)
            {
                absoluteX[i] = pen + shaped[i].XOffset;
                pen += descriptor.GlyphIndexToWidth(shaped[i].GlyphIndex) + shaped[i].XAdvanceDelta;
            }

            // Every mark glyph (ClusterLength 0 - a ccmp-decomposed combining mark, e.g. this font's own
            // "two dots" glyphs) must land close to an immediately-adjacent glyph - its own base, paired
            // by the ccmp decomposition that produced it. A generous threshold (half an em) comfortably
            // separates "correctly attached" (single- to low-triple-digit design-unit deltas in this
            // font) from the pre-fix bug, which was off by roughly a whole base advance (up to ~1093
            // units for this font's widest "fina" swash - see the InlineData remarks above).
            var halfEm = descriptor.UnitsPerEm / 2.0;
            for (var i = 0; i < shaped.Count; i++)
            {
                if (shaped[i].ClusterLength != 0)
                    continue;

                var neighborDistances = new List<double>();
                if (i > 0) neighborDistances.Add(Math.Abs(absoluteX[i] - absoluteX[i - 1]));
                if (i < shaped.Count - 1) neighborDistances.Add(Math.Abs(absoluteX[i] - absoluteX[i + 1]));

                Assert.True(neighborDistances.Count > 0 && neighborDistances.Min() < halfEm,
                    $"Mark at index {i} (gid {shaped[i].GlyphIndex}) is not within a plausible distance " +
                    $"of a neighboring base glyph - nearest neighbor distance(s): {string.Join(",", neighborDistances)}");
            }
        }

        [Fact]
        public async Task EndToEndLayout_ArabicWord_ResolvesArabicScriptTagAndJoiningForms()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'ArabicTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'ArabicTest'; font-size: 14pt; }}
p {{ width: 400px; direction: rtl; }}
</style></head>
<body><p>{Beh}{Yeh}{Teh}</p></body>
</html>");

            Assert.Equal("arab", word.ScriptTag);
            Assert.NotNull(word.EffectiveJoiningForms);
            Assert.Equal(3, word.EffectiveJoiningForms!.Length);
            // Unlike a plain RTL word, an Arabic-family joining word's own Text/EffectiveJoiningForms
            // never reverse for display (see CssRectWord.DisplayOrderReversed's remarks) - GSUB needs
            // true logical adjacency to match real fonts' contextual rlig rules, so both stay in true
            // logical order (BEH, Init) permanently; only DisplayOrderReversed records that this word
            // still reads right-to-left on the page; shaping's own final glyph-list reversal (requested
            // via TextShapingFeatures.ReverseForDisplay) is what actually displays it correctly.
            Assert.Equal(Beh, word.Text[0].ToString());
            Assert.Equal(ArabicJoiningForm.Init, word.EffectiveJoiningForms[0]);
            Assert.True(word.DisplayOrderReversed);
        }

        [Fact]
        public async Task EndToEndLayout_VerticalWritingModeSidewaysRun_MatchesHorizontalNaturalWidth()
        {
            // Regression: CssLayoutEngine.NaturalWordSize's rotated ("sideways") run branch used to
            // measure via the box-level ActualTextShapingFeatures rather than this word's own per-word
            // ScriptTag/EffectiveJoiningForms (see DerivedStyle.ResolveWordShapingFeatures' own
            // remarks) - so an Arabic-joining word laid out under writing-mode: vertical-rl with
            // text-orientation: sideways (isUpright=false, per NaturalWordSize's own remarks) would
            // reserve space as if joining-form substitution had never been requested, while
            // FragmentPainter.Text.cs's PaintWords (which already called ResolveWordShapingFeatures)
            // painted the real, joining-form-substituted glyphs - a layout/paint desync (the same class
            // of bug issue #770's per-character advance fix addressed on the other axis). Proven by
            // comparing this word's own cached pre-rotation CssRect.NaturalSize (exactly what
            // NaturalWordSize computes and caches - not CssRect.Width/Height, which
            // CreateVerticalLineBoxes overwrites with the word's *physical*, rotated footprint) against
            // the same text's naturally-measured horizontal width: both must agree, since real
            // joining-form substitution changes total advance independent of writing-mode.
            var arabicWord = $"{Beh}{Yeh}{Teh}";

            var horizontalWord = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'ArabicTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'ArabicTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{arabicWord}</p></body>
</html>");

            var verticalWord = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'ArabicTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Arabic)}') format('truetype'); }}
body {{ font-family: 'ArabicTest'; font-size: 14pt; }}
p {{ writing-mode: vertical-rl; text-orientation: sideways; height: 400px; }}
</style></head>
<body><p>{arabicWord}</p></body>
</html>");

            Assert.NotNull(horizontalWord.EffectiveJoiningForms);
            Assert.NotNull(verticalWord.EffectiveJoiningForms);
            Assert.NotNull(verticalWord.NaturalSize);
            // horizontalWord.Width is CssRect.Width from a plain (non-vertical) layout - never
            // overwritten with a rotated physical footprint the way a vertical word's is, so it already
            // is this text's own natural, joining-form-aware measured width; NaturalSize itself is only
            // ever populated by CreateVerticalLineBoxes' own NaturalWordSize calls (see that field's own
            // remarks), so a horizontal word's NaturalSize stays null and isn't the right comparison.
            Assert.Equal(horizontalWord.Width, verticalWord.NaturalSize!.Value.Width, precision: 2);
        }

        [Fact]
        public async Task EndToEndLayout_LatinWord_NoScriptTagOrJoiningForms()
        {
            // Regression: this whole feature must be a complete no-op for ordinary (non-Arabic-family)
            // text - Latin content must never pick up a "arab" tag or non-null joining forms.
            var word = await LayoutWord(@"<!DOCTYPE html>
<html><body><p style=""width:400px; font-size:14pt"">Hello</p></body></html>");

            Assert.NotEqual("arab", word.ScriptTag);
            Assert.Null(word.EffectiveJoiningForms);
        }

        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        // Routed through the shared LayoutHarness (see its own remarks: "prefer this over hand-rolling
        // another per-file BuildAndLayout copy") rather than this file's own HtmlContainerInt/
        // PdfSharpAdapter/GraphicsAdapter wiring - margin: 0 matches this file's own prior no-margin setup,
        // since these tests assert word-level shaping properties, not page-margin-relative coordinates.
        private static async Task<CssRectWord> LayoutWord(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 842, margin: 0);
            var p = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true);
            return WordsOf(p).First(w => w.Text != "\n");
        }

        private static List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new List<CssRectWord>();
            Collect(p, words);
            return words;
        }

        private static void Collect(CssBox box, List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>());
            foreach (var child in box.Boxes)
                Collect(child, words);
        }
    }
}
