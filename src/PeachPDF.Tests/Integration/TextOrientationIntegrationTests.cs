using PeachPDF.Adapters;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for real per-character <c>text-orientation: mixed</c> - Unicode's
    /// <c>Vertical_Orientation</c> property (UAX #50) classifying each codepoint in a vertical-writing-mode
    /// box as upright (painted without rotation) or rotated (this repo's prior universal behavior),
    /// splitting a word at the boundary the same way <c>font-variant: small-caps</c> already splits one
    /// (<c>CssBox.AddWord</c>/<c>EmitPerCodepointFragments</c>). Per CLAUDE.md's testing conventions: a
    /// parser-only test isn't sufficient for a paint feature, so this covers both real layout output
    /// (<c>CssRect</c> properties after layout) and the actual paint call sequence.
    /// </summary>
    public class TextOrientationIntegrationTests
    {
        // U+30C6 U+30AD "テキ" (katakana TE+KI, both Vertical_Orientation=U) + "AB" (both R).
        // Deliberately katakana, not a CJK Unified Ideograph (U+4E00-FA2D): CssBox.AppendWordsFromText's
        // pre-existing per-Asian-character tokenization (CommonUtils.IsAsianCharacter) already splits an
        // ideograph like "你" off into its own single-character word before AddWord (and this diff's own
        // orientation-boundary check inside it) ever sees more than one codepoint at a time - which would
        // make a test built on ideographs prove nothing about EmitPerCodepointFragments's own new
        // orientation-boundary logic, only about that older, unrelated mechanism. Katakana (like
        // hiragana) is Vertical_Orientation=U without being an "Asian character" by that narrower check,
        // so "テキAB" reaches AddWord as one un-pre-split string, and splitting it into upright/rotated
        // runs is genuinely this diff's own work.
        private const string Upright = "テキ";
        private const string Latin = "AB";

        [Fact]
        public async Task Mixed_SplitsWordAtOrientationBoundary()
        {
            var box = await LayoutVerticalWord(Upright + Latin, TextOrientationValue.Mixed);

            Assert.Equal(2, box.Words.Count);
            Assert.Equal(Upright, box.Words[0].Text);
            Assert.Equal(Latin, box.Words[1].Text);
            Assert.True(box.Words[0].IsUprightOrientation);
            Assert.False(box.Words[1].IsUprightOrientation);
        }

        [Fact]
        public async Task Mixed_MultipleBoundaries_SplitsIntoMaximalRuns()
        {
            // R | U | R - two boundaries, three runs, not one fragment per codepoint.
            var box = await LayoutVerticalWord(Latin + Upright + Latin, TextOrientationValue.Mixed);

            Assert.Equal(3, box.Words.Count);
            Assert.Equal(Latin, box.Words[0].Text);
            Assert.Equal(Upright, box.Words[1].Text);
            Assert.Equal(Latin, box.Words[2].Text);
            Assert.False(box.Words[0].IsUprightOrientation);
            Assert.True(box.Words[1].IsUprightOrientation);
            Assert.False(box.Words[2].IsUprightOrientation);
        }

        [Fact]
        public async Task Mixed_UniformOrientationWord_IsNotSplit()
        {
            var upright = await LayoutVerticalWord(Upright, TextOrientationValue.Mixed);
            Assert.Single(upright.Words);
            Assert.True(upright.Words[0].IsUprightOrientation);

            var rotated = await LayoutVerticalWord(Latin, TextOrientationValue.Mixed);
            Assert.Single(rotated.Words);
            Assert.False(rotated.Words[0].IsUprightOrientation);
        }

        [Fact]
        public async Task Mixed_SplitFragments_StayGluedOnOneColumn_NoSpuriousWrap()
        {
            var box = await LayoutVerticalWord(Upright + Latin, TextOrientationValue.Mixed);

            Assert.False(box.Words[0].SuppressWrapBefore); // first fragment of a word is a normal break point
            Assert.True(box.Words[1].SuppressWrapBefore); // continuation fragment must never itself wrap
        }

        [Fact]
        public async Task HorizontalTb_NeverSplitsByOrientation_Regression()
        {
            var html = Wrap($"<p id=\"w\" style=\"width:400px\">{Upright}{Latin}</p>", null);
            var container = await LayoutHtml(html);
            var box = FindWordsBox(container.Root!, "w");

            Assert.Single(box.Words);
            Assert.Equal(Upright + Latin, box.Words[0].Text);
        }

        [Fact]
        public async Task Upright_ForcesEveryWordUpright_RegardlessOfCodepoint()
        {
            var box = await LayoutVerticalWord(Latin, TextOrientationValue.Upright);

            // upright applies one box-wide decision without needing a per-character split at all -
            // AddWord never even calls NeedsOrientationSplit for a non-mixed box.
            Assert.Single(box.Words);
        }

        [Fact]
        public async Task Sideways_NeverSplits_MatchesPriorEverythingRotatesBehavior()
        {
            var box = await LayoutVerticalWord(Upright + Latin, TextOrientationValue.Sideways);

            Assert.Single(box.Words);
            Assert.Equal(Upright + Latin, box.Words[0].Text);
        }

        [Fact]
        public async Task Upright_ReservesSameExtentAsMixedUprightRun_LayoutPaintConsistency()
        {
            // Regression for a layout/paint desync: FragmentPainter.Text.cs's PaintWords forces every
            // word upright for text-orientation:upright regardless of word.IsUprightOrientation (a
            // box-wide override), but CssLayoutEngine.NaturalWordSize used to branch on
            // word.IsUprightOrientation alone - a flag CssBox only ever sets true under
            // text-orientation:mixed (WholeTextOrientationIsUpright short-circuits to false whenever
            // IsVerticalMixedOrientation is false). So an upright-forced box's words reserved space
            // with the *rotated*, whole-string-measured formula while paint always painted them with
            // the per-character upright formula - an under-reservation that would overlap whatever
            // followed. Same text, same font, same character count under text-orientation:mixed
            // (which correctly classifies this uniform-orientation run as upright, exercising the
            // upright formula via the field) and text-orientation:upright (exercising it via the
            // box-wide override) must reserve the identical extent.
            var mixedBox = await LayoutVerticalWord(Upright, TextOrientationValue.Mixed);
            var uprightBox = await LayoutVerticalWord(Upright, TextOrientationValue.Upright);

            Assert.True(mixedBox.Words[0].IsUprightOrientation);
            Assert.Equal(mixedBox.Words[0].Height, uprightBox.Words[0].Height, precision: 6);
        }

        // ── Real vertical metrics (issue #770): vhea/vmtx/VORG, not the line-height approximation ──

        [Fact]
        public async Task Upright_ReservesRealVmtxAdvance_NotFlatLineHeight()
        {
            // BundledFonts.Cjk (embedded above as the "CJK" family) genuinely carries real vhea/vmtx
            // data (VerticalMetricsTablesTests.RealBundledCjkFont_HasVerticalMetrics_AdvanceDiffersFromFallback),
            // so the upright run's reserved down-the-column extent must now be the real per-character
            // vmtx advance sum, not the pre-#770 flat font.Height*runeCount approximation.
            var box = await LayoutVerticalWord(Upright, TextOrientationValue.Upright);
            var font = box.ActualFont;

            Assert.True(font.HasVerticalMetrics);

            double expectedRealAdvance = 0;
            double expectedFlatAdvance = 0;
            foreach (var rune in Upright.EnumerateRunes())
            {
                expectedRealAdvance += font.GetVerticalAdvance(rune) + box.ActualLetterSpacing;
                expectedFlatAdvance += font.Height + box.ActualLetterSpacing;
            }

            Assert.Equal(expectedRealAdvance, box.Words[0].Height, precision: 6);
            Assert.NotEqual(expectedFlatAdvance, box.Words[0].Height);
        }

        [Fact]
        public async Task Upright_FallsBackToLineHeight_WhenFontHasNoVerticalMetrics()
        {
            // A Latin-only bundled font (no vhea/vmtx table at all) forced upright via
            // text-orientation:upright must keep the pre-#770 flat font.Height step unchanged - the
            // real-metrics path in NaturalWordSize/PaintUprightVerticalRun is gated on
            // RFont.HasVerticalMetrics precisely so the overwhelming majority of fonts (which have no
            // vertical metrics) see zero behavior change.
            var latinFontBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'LatinOnly'; src: url('data:font/truetype;base64,{latinFontBase64}') format('truetype'); }}
body {{ font-family: 'LatinOnly'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Latin}</div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindWordsBox(container.Root!, "w");
            var font = box.ActualFont;

            Assert.False(font.HasVerticalMetrics);
            Assert.Equal(Latin.Length * (font.Height + box.ActualLetterSpacing), box.Words[0].Height, precision: 6);
        }

        [Fact]
        public async Task Mixed_PaintsUprightRun_StepsByRealVmtxAdvance_MatchingLayoutReservation()
        {
            // The paint-side counterpart of Upright_ReservesRealVmtxAdvance_NotFlatLineHeight: paint's
            // actual down-the-column step between consecutive upright characters must equal layout's
            // real-vmtx reservation, not just "some positive step" (already covered structurally by
            // Mixed_PaintsUprightRunPerCharacter_RotatedRunAsOneTransformedCall's Y-ordering check).
            var html = Wrap($"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{Upright}</div>", "mixed");
            var container = await LayoutHtml(html);
            var elementBox = FindById(container.Root!, "w")!;
            var wordsBox = FindWordsBox(container.Root!, "w");
            var font = wordsBox.ActualFont;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, elementBox, recorder);

            Assert.True(font.HasVerticalMetrics);
            Assert.Equal(2, recorder.DrawStringCalls.Count);

            var firstRune = System.Text.Rune.GetRuneAt(Upright, 0);
            var expectedStep = font.GetVerticalAdvance(firstRune) + wordsBox.ActualLetterSpacing;
            var actualStep = recorder.DrawStringCalls[1].Point.Y - recorder.DrawStringCalls[0].Point.Y;

            Assert.Equal(expectedStep, actualStep, precision: 6);
        }

        [Fact]
        public async Task Upright_ClipsEachCharacterToItsOwnCell_WhenFontHasVerticalMetrics()
        {
            // A real vmtx advance is routinely narrower than the font's own line height (see
            // PaintUprightVerticalRun's remarks), so RGraphics.DrawString would still paint each
            // character across its full line-height span unless confined to its own reserved cell -
            // verifies the push/pop clip actually brackets the draw call (order, not just count, per
            // this repo's own painting-test convention), and that the no-real-metrics fallback path
            // needs no clip at all (its advance already equals its own painted span).
            var realHtml = Wrap($"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{Upright}</div>", "mixed");
            var realContainer = await LayoutHtml(realHtml);
            var realBox = FindById(realContainer.Root!, "w")!;
            var realRecorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(realContainer, realBox, realRecorder);

            Assert.Equal(2, realRecorder.DrawStringCalls.Count); // Upright = "テキ", one call per character
            foreach (var draw in realRecorder.DrawStringCalls)
            {
                Assert.True(draw.Font.HasVerticalMetrics);

                var drawIndex = realRecorder.Log.IndexOf(draw);
                Assert.IsType<RecordingGraphics.PushClipMarker>(realRecorder.Log[drawIndex - 1]);
                Assert.IsType<RecordingGraphics.PopClipMarker>(realRecorder.Log[drawIndex + 1]);
            }

            var fallbackFontBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var fallbackHtml = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'LatinOnly'; src: url('data:font/truetype;base64,{fallbackFontBase64}') format('truetype'); }}
body {{ font-family: 'LatinOnly'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Latin}</div></body></html>";
            var fallbackContainer = await LayoutHtml(fallbackHtml);
            var fallbackBox = FindById(fallbackContainer.Root!, "w")!;
            var fallbackRecorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(fallbackContainer, fallbackBox, fallbackRecorder);

            Assert.False(fallbackRecorder.DrawStringCalls[0].Font.HasVerticalMetrics);
            Assert.DoesNotContain(fallbackRecorder.Log, entry => entry is RecordingGraphics.PushClipMarker);
        }

        [Fact]
        public async Task Upright_AppliesRealVorgOrigin_WhenFontHasRealVorgTable()
        {
            // BundledFonts.Otf (Source Code Pro) is confirmed CFF-flavored (no glyf), so a real VORG
            // table spliced onto it is actually trusted (issue #775) - unlike the TrueType case in the
            // companion test below. defaultVertOriginY=700 is deliberately different from this font's
            // own Ascender (984, confirmed directly via OpenTypeDescriptor) so the resulting shift is
            // real and provably non-zero, not just coincidentally near-zero.
            var fontBase64 = Convert.ToBase64String(BuildFontWithSyntheticVerticalTables(BundledFonts.Otf, vertOriginY: 700, baseFontNeedsVheaVmtx: true));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VorgTest'; src: url('data:font/truetype;base64,{fontBase64}') format('truetype'); }}
body {{ font-family: 'VorgTest'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Latin}</div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindById(container.Root!, "w")!;
            var wordsBox = FindWordsBox(container.Root!, "w");
            var font = wordsBox.ActualFont;
            Assert.True(font.HasVerticalOrigin);

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box, recorder);

            var rune = System.Text.Rune.GetRuneAt(Latin, 0);
            var expectedShift = font.GetVerticalOriginY(rune) - font.Ascent;
            Assert.NotEqual(0, expectedShift, precision: 3); // the shift is actually nonzero for this fixture

            // word.Top is the unshifted top-of-cell anchor CreateVerticalLineBoxes already placed this
            // word at, before FragmentPainter (and its VORG shift) ever runs - the same "+= (originY -
            // Ascent)" derivation PaintUprightVerticalRun's own remarks work through.
            var unshiftedY = wordsBox.Words[0].Top;
            var actualY = recorder.DrawStringCalls[0].Point.Y;
            Assert.Equal(unshiftedY + expectedShift, actualY, precision: 6);
        }

        [Fact]
        public async Task Upright_IgnoresVorg_OnTrueTypeFont_RendersIdenticallyToNoVorg()
        {
            // The same synthetic VORG bytes as the CFF test above, spliced onto a TrueType font instead
            // (BundledFonts.Cjk) - per the OpenType spec, VORG "must be ignored" on TrueType-outline
            // fonts, so this must render byte-for-byte identically to the plain no-VORG case (issue
            // #770's already-shipped behavior), not merely "no crash."
            var withVorgBase64 = Convert.ToBase64String(BuildFontWithSyntheticVerticalTables(BundledFonts.Cjk, vertOriginY: 700, baseFontNeedsVheaVmtx: false));
            var withVorgHtml = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VorgOnTtf'; src: url('data:font/truetype;base64,{withVorgBase64}') format('truetype'); }}
body {{ font-family: 'VorgOnTtf'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Upright}</div></body></html>";
            var withVorgContainer = await LayoutHtml(withVorgHtml);
            var withVorgBox = FindById(withVorgContainer.Root!, "w")!;
            var withVorgWordsBox = FindWordsBox(withVorgContainer.Root!, "w");
            Assert.False(withVorgWordsBox.ActualFont.HasVerticalOrigin);
            var withVorgRecorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(withVorgContainer, withVorgBox, withVorgRecorder);

            var plainBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Cjk));
            var plainHtml = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'PlainCjk'; src: url('data:font/truetype;base64,{plainBase64}') format('truetype'); }}
body {{ font-family: 'PlainCjk'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Upright}</div></body></html>";
            var plainContainer = await LayoutHtml(plainHtml);
            var plainBox = FindById(plainContainer.Root!, "w")!;
            var plainRecorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(plainContainer, plainBox, plainRecorder);

            Assert.Equal(plainRecorder.DrawStringCalls.Count, withVorgRecorder.DrawStringCalls.Count);
            for (var i = 0; i < plainRecorder.DrawStringCalls.Count; i++)
            {
                Assert.Equal(plainRecorder.DrawStringCalls[i].Point.Y, withVorgRecorder.DrawStringCalls[i].Point.Y, precision: 6);
                Assert.Equal(plainRecorder.DrawStringCalls[i].Point.X, withVorgRecorder.DrawStringCalls[i].Point.X, precision: 6);
            }
        }

        [Fact]
        public async Task Upright_AppliesVorgOrigin_AndClipsCharacter_OnFontWithVorgButNoVerticalMetrics()
        {
            // Regression test for a bug the post-change review pass caught: PaintUprightVerticalRun's
            // clip gate originally only fired on HasVerticalMetrics, so a font with a real VORG table but
            // no vhea/vmtx got its origin shifted with no clip protecting the shifted glyph from bleeding
            // into its neighbor's cell. BundledFonts.Otf (CFF) has neither vhea nor vmtx natively, so
            // baseFontNeedsVheaVmtx: false here (unlike the companion VORG test above, which needs both
            // synthesized to also exercise the clip-per-cell path) exercises exactly the VORG-only
            // combination: HasVerticalOrigin true, HasVerticalMetrics false.
            var fontBase64 = Convert.ToBase64String(BuildFontWithSyntheticVerticalTables(BundledFonts.Otf, vertOriginY: 700, baseFontNeedsVheaVmtx: false));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'VorgOnlyTest'; src: url('data:font/truetype;base64,{fontBase64}') format('truetype'); }}
body {{ font-family: 'VorgOnlyTest'; margin: 0 }}
#w {{ text-orientation: upright }}
</style></head><body><div id=""w"" style=""writing-mode:vertical-rl;height:200px"">{Latin}</div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindById(container.Root!, "w")!;
            var wordsBox = FindWordsBox(container.Root!, "w");
            var font = wordsBox.ActualFont;
            Assert.True(font.HasVerticalOrigin);
            Assert.False(font.HasVerticalMetrics);

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, box, recorder);

            var draw = recorder.DrawStringCalls[0];
            var drawIndex = recorder.Log.IndexOf(draw);
            Assert.IsType<RecordingGraphics.PushClipMarker>(recorder.Log[drawIndex - 1]);
            Assert.IsType<RecordingGraphics.PopClipMarker>(recorder.Log[drawIndex + 1]);

            var rune = System.Text.Rune.GetRuneAt(Latin, 0);
            var expectedShift = font.GetVerticalOriginY(rune) - font.Ascent;
            Assert.NotEqual(0, expectedShift, precision: 3);

            var unshiftedY = wordsBox.Words[0].Top;
            Assert.Equal(unshiftedY + expectedShift, draw.Point.Y, precision: 6);
        }

        /// <summary>Splices a synthetic <c>VORG</c> onto <paramref name="basePath"/>'s own bytes (see
        /// <see cref="SyntheticFontTables"/>), adding real <c>vhea</c>/<c>vmtx</c> too when
        /// <paramref name="baseFontNeedsVheaVmtx"/> is true - <see cref="BundledFonts.Otf"/> has neither
        /// and needs both to make <see cref="RFont.HasVerticalMetrics"/> true (so the clip-per-cell path
        /// is exercised alongside the origin shift), while <see cref="BundledFonts.Cjk"/> already has
        /// real ones (that's what makes #770's own tests work) and would collide with a second, synthetic
        /// pair. <paramref name="vertOriginY"/> is this suite's only varying input.</summary>
        private static byte[] BuildFontWithSyntheticVerticalTables(string basePath, short vertOriginY, bool baseFontNeedsVheaVmtx)
        {
            var fontBytes = File.ReadAllBytes(basePath);

            if (baseFontNeedsVheaVmtx)
            {
                var numGlyphs = XFontSource.GetOrCreateFrom(fontBytes).Fontface.maxp.numGlyphs;
                fontBytes = SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, TableTagNames.VHea, SyntheticFontTables.BuildVhea(ascent: 900, descent: -200, numOfLongVerMetrics: numGlyphs));
                fontBytes = SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, TableTagNames.VMtx, SyntheticFontTables.BuildVmtxUniform(1000, numGlyphs));
            }

            fontBytes = SyntheticFontTables.InsertTableDirectoryEntry(fontBytes, TableTagNames.VOrg, SyntheticFontTables.BuildVorg(vertOriginY));
            return fontBytes;
        }

        // ── Painting: actual RGraphics call sequence ────────────────────────────

        [Fact]
        public async Task Mixed_PaintsUprightRunPerCharacter_RotatedRunAsOneTransformedCall()
        {
            var html = Wrap(
                $"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{Upright}{Latin}</div>",
                "mixed");
            var container = await LayoutHtml(html);
            var elementBox = FindById(container.Root!, "w")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, elementBox, recorder);

            // The upright run ("テキ") paints one DrawString call per character - PaintUprightVerticalRun
            // has no notion of "whole run" the way a rotated fragment's single call does. The rotated run
            // ("AB") paints as one call for its whole fragment, wrapped in a push/pop transform pair.
            Assert.Equal(3, recorder.DrawStringCalls.Count);
            Assert.Equal("テ", recorder.DrawStringCalls[0].Text);
            Assert.Equal("キ", recorder.DrawStringCalls[1].Text);
            Assert.Equal(Latin, recorder.DrawStringCalls[2].Text);

            Assert.Equal(1, recorder.PushTransformCount);
            Assert.Equal(1, recorder.PopTransformCount);

            // The two upright characters stack top-to-bottom (increasing Y), not side-by-side.
            Assert.True(recorder.DrawStringCalls[1].Point.Y > recorder.DrawStringCalls[0].Point.Y);
        }

        [Fact]
        public async Task Upright_PaintsEveryCharacterUpright_NoTransformAtAll()
        {
            var html = Wrap(
                $"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{Latin}</div>",
                "upright");
            var container = await LayoutHtml(html);
            var elementBox = FindById(container.Root!, "w")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, elementBox, recorder);

            Assert.Equal(2, recorder.DrawStringCalls.Count); // one call per character: "A", "B"
            Assert.Equal("A", recorder.DrawStringCalls[0].Text);
            Assert.Equal("B", recorder.DrawStringCalls[1].Text);
            Assert.Equal(0, recorder.PushTransformCount);
        }

        [Fact]
        public async Task Sideways_PaintsEveryFragmentRotated_NoUprightCallsAtAll()
        {
            var html = Wrap(
                $"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{Upright}</div>",
                "sideways");
            var container = await LayoutHtml(html);
            var elementBox = FindById(container.Root!, "w")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, elementBox, recorder);

            Assert.Single(recorder.DrawStringCalls); // one call for the whole (unsplit) fragment
            Assert.Equal(Upright, recorder.DrawStringCalls[0].Text);
            Assert.Equal(1, recorder.PushTransformCount);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private enum TextOrientationValue { Mixed, Upright, Sideways }

        private static async Task<CssBox> LayoutVerticalWord(string text, TextOrientationValue orientation)
        {
            var cssValue = orientation switch
            {
                TextOrientationValue.Upright => "upright",
                TextOrientationValue.Sideways => "sideways",
                _ => "mixed"
            };
            var html = Wrap($"<div id=\"w\" style=\"writing-mode:vertical-rl;height:200px\">{text}</div>", cssValue);
            var container = await LayoutHtml(html);
            return FindWordsBox(container.Root!, "w");
        }

        // Embeds a font covering both the CJK and Latin characters every test here uses, so every
        // character resolves to one face - isolating the Vertical_Orientation split under test from
        // CssBox.NeedsPerCodepointFont's own unrelated missing-glyph-fallback split (the default,
        // system/generic sans-serif font used elsewhere in this suite has no CJK coverage, which would
        // otherwise split "你好" for the wrong reason before orientation is ever consulted).
        private static readonly string FontFaceBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Cjk));

        private static string Wrap(string body, string? textOrientation) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'CJK'; src: url('data:font/truetype;base64,{FontFaceBase64}') format('truetype'); }}
body {{ font-family: 'CJK'; margin: 0 }}
{(textOrientation is null ? "" : $"#w {{ text-orientation: {textOrientation} }}")}
</style></head><body>{body}</body></html>";

        private static async Task<HtmlContainerInt> LayoutHtml(string body)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(body, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id)
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }

            return null;
        }

        /// <summary>
        /// Text nodes get their own anonymous child <see cref="CssBox"/> (see
        /// <c>DomParser.CorrectTextBoxes</c>), so an element like <c>&lt;div id="w"&gt;...&lt;/div&gt;</c>'s
        /// own box has an empty <see cref="CssBox.Words"/> - the words live on its single anonymous text
        /// child instead.
        /// </summary>
        private static CssBox FindWordsBox(CssBox root, string id)
        {
            var element = FindById(root, id)!;
            if (element.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
        }

        private sealed class RecordingGraphics : RGraphics
        {
            public List<(string Text, RFont Font, RPoint Point)> DrawStringCalls { get; } = [];
            public int PushTransformCount { get; private set; }
            public int PopTransformCount { get; private set; }

            /// <summary>Ordered log of draw/clip calls only - a minimal parallel to <see cref="DrawStringCalls"/>,
            /// added so a test can assert a clip push/pop actually brackets a specific draw call (order,
            /// not just count) without disturbing every existing assertion built against the untyped
            /// tuple list above.</summary>
            public List<object> Log { get; } = [];
            public sealed record PushClipMarker;
            public sealed record PopClipMarker;

            public RecordingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, TextShapingFeatures? features = null)
            {
                var call = (str, font, point);
                DrawStringCalls.Add(call);
                Log.Add(call);
            }

            public override void PushTransform(RMatrix matrix) => PushTransformCount++;
            public override void PopTransform() => PopTransformCount++;
            public override void PushBlendMode(RBlendMode mode) { }
            public override void PopBlendMode() { }
            public override void PushClip(RRect rect) { _clipStack.Push(rect); Log.Add(new PushClipMarker()); }
            public override void PushClip(RGraphicsPath path) { _clipStack.Push(_clipStack.Peek()); Log.Add(new PushClipMarker()); }
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); Log.Add(new PopClipMarker()); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, TextShapingFeatures? features = null) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }

            // Deterministic, non-zero, length-proportional - PaintUprightVerticalRun uses this to
            // position/advance each character, so a constant-zero stub (fine for tests that only care
            // about call count/text/font) would silently collapse every stacked character onto the same
            // Y, making the "characters stack top-to-bottom" assertion vacuous.
            public override RSize MeasureString(string str, RFont font, TextShapingFeatures? features = null) => new((str?.Length ?? 0) * 10, 12);
            public override int CountShapedGlyphs(string str, RFont font, TextShapingFeatures? features = null) => str?.Length ?? 0;
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }
    }
}
