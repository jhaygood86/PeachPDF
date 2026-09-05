using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Arabic;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s Arabic-family joining-form resolution/shaping-run
    /// wiring (issue #533) - <c>SvgRenderer.ResolveComplexScriptRuns</c>/<c>GlyphInfo.ShapingRunFirst</c>,
    /// mirroring <see cref="SvgTextLanguageTests"/>'s own pattern of asserting the resolved
    /// <see cref="TextShapingFeatures"/> actually reaches <see cref="RGraphics.DrawString"/> via the
    /// <see cref="TestRecordingGraphics"/> mock, not just that some internal state parses correctly.
    /// Real-font glyph-substitution proof (not just wiring) lives in
    /// <see cref="SvgTextArabicJoiningCharacterizationTests"/>.
    /// </summary>
    public class SvgTextArabicJoiningTests
    {
        // Same letters PeachPDF.Tests.Html.Core.ArabicJoiningCharacterizationTests uses, for
        // consistency across the Arabic-family test fixtures.
        private const string Beh = "ب";
        private const string Yeh = "ي";
        private const string Teh = "ت";
        private const string Alef = "ا";
        private const string Lam = "ل";

        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void ThreeLetterWord_ResolvesArabicScriptTagAndPerCharacterJoiningForms()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">{Beh}{Yeh}{Teh}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Beh + Yeh + Teh, draw.Text);
            Assert.Equal("arab", draw.Features!.Value.ScriptTag);
            Assert.Equal(
                new[] { ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina },
                draw.Features.Value.JoiningForms);
            Assert.Null(draw.Features.Value.UseCategories);
        }

        [Fact]
        public void SingleLetter_StillFormsATrivialRun_RequestsIsolatedForm()
        {
            // Even a lone Arabic-family letter needs an explicit isol request - most real fonts don't
            // reliably default to the isolated presentation form for a bare nominal glyph (see
            // ResolveComplexScriptRuns' own remarks).
            var g = Render($"""<text x="10" y="50" font-size="20">{Lam}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(new[] { ArabicJoiningForm.Isol }, draw.Features!.Value.JoiningForms);
        }

        [Fact]
        public void RtlDirection_TextStaysLogicalOrder_ReverseForDisplayRequestedInstead()
        {
            // The core architectural point of this feature: SVG must never mirror/reorder a joining
            // run's own characters before shaping - that would break a font's contextual rlig rules,
            // which need true logical adjacency. Only the resulting shaped glyph list should reverse
            // for display, via TextShapingFeatures.ReverseForDisplay (see
            // SvgRenderer.ApplyBidiReordering's own remarks, mirroring
            // CssLayoutEngine.MirrorWordTextIfNeeded's HTML precedent).
            var g = Render($"""<text x="190" y="50" font-size="20" direction="rtl">{Beh}{Yeh}{Teh}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Beh + Yeh + Teh, draw.Text);
            Assert.True(draw.Features!.Value.ReverseForDisplay);
        }

        [Fact]
        public void LamAlef_BothLettersShareOneRun_KeepsDefaultLigatures()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">{Lam}{Alef}</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal(Lam + Alef, draw.Text);
            Assert.Equal(new[] { ArabicJoiningForm.Init, ArabicJoiningForm.Fina }, draw.Features!.Value.JoiningForms);
            Assert.Equal(LigatureFeatures.Default, draw.Features.Value.Ligatures);
        }

        [Fact]
        public void MixedArabicAndLatin_LatinGetsNoJoiningForms()
        {
            var g = Render($"""<text x="10" y="50" font-size="20">Hi {Beh}{Yeh}</text>""");

            // "Hi " (no participants) and the Arabic pair each need their own TextShapingFeatures, so
            // they can never share one PaintGlyphs batch/DrawString call.
            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal("Hi ", g.DrawStringCalls[0].Text);
            Assert.Null(g.DrawStringCalls[0].Features!.Value.JoiningForms);
            Assert.Equal(Beh + Yeh, g.DrawStringCalls[1].Text);
            Assert.NotNull(g.DrawStringCalls[1].Features!.Value.JoiningForms);
        }

        [Fact]
        public void ExplicitDxMidWord_SplitsIntoSeparatePaintRunsButKeepsTheWordsTrueJoiningForms()
        {
            // An explicit per-character dx has no well-defined position inside a shaped multi-
            // character glyph run (SVG 2 §11.5) - ResolveComplexScriptRuns never forms one atomic
            // *paint* run across it, so Beh and Yeh+Teh become two separate DrawString batches. But
            // joining forms are still resolved once over the whole flattened stream and only sliced
            // per run afterward (mirroring CssBidiParagraphResolver's own paragraph-wide resolution) -
            // Yeh's true Joining_Type-driven form is still Medi (it has both a preceding and a
            // following joining neighbor in the real word), not re-resolved as if isolated from Beh
            // just because an incidental positioning tweak split how it paints. All three letters are
            // still one uniform-level Arabic (strong-R) bidi run overall, so real UAX#9 L2 reordering
            // swaps the two *blocks*' relative visual order (Yeh+Teh, then Beh) even with no explicit
            // direction="rtl" - each block's own internal order stays intact.
            var g = Render($"""<text x="10" y="50" font-size="20" dx="0 5">{Beh}{Yeh}{Teh}</text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal(Yeh + Teh, g.DrawStringCalls[0].Text);
            Assert.Equal(new[] { ArabicJoiningForm.Medi, ArabicJoiningForm.Fina }, g.DrawStringCalls[0].Features!.Value.JoiningForms);
            Assert.Equal(Beh, g.DrawStringCalls[1].Text);
            // Beh's own true form is Init (Yeh follows it in the real word), not Isol - the same
            // "sliced from the whole-stream resolution, not re-resolved as if isolated" rule as Yeh's.
            Assert.Equal(new[] { ArabicJoiningForm.Init }, g.DrawStringCalls[1].Features!.Value.JoiningForms);
        }

        [Fact]
        public void NonZeroLetterSpacing_NeverFormsARun()
        {
            // Non-zero letter-spacing inserts space between glyphs, which real shapers (and real
            // browsers) already treat as disabling optional ligature/cursive joining - simply never
            // forming a run here reproduces that rather than forming an incorrect one.
            var g = Render($"""<text x="10" y="50" font-size="20" style="letter-spacing:2px">{Beh}{Yeh}{Teh}</text>""");

            Assert.All(g.DrawStringCalls, d => Assert.Null(d.Features!.Value.JoiningForms));
        }

        [Fact]
        public void ExplicitNonZeroRotateOnFirstCharacter_NeverFormsARun()
        {
            // PaintRotatedGlyph paints only that one GlyphInfo's own Glyph string - forming a multi-
            // character run whose first (and only externally addressable) glyph is explicitly rotated
            // would silently drop the rest of the run's text.
            var g = Render($"""<text x="10" y="50" font-size="20" rotate="15">{Beh}{Yeh}{Teh}</text>""");

            Assert.All(g.DrawStringCalls, d => Assert.Null(d.Features!.Value.JoiningForms));
        }

        [Fact]
        public void ExplicitRotateOnFirstCharacterOnly_StillFormsARunForTheRemainingLetters()
        {
            // Regression: a character whose own explicit rotate disqualifies it from anchoring a run
            // must not cause ResolveComplexScriptRuns to give up on the whole contiguous participant
            // span - only Beh (rotate 15) is excluded; Yeh+Teh (rotate 0, the rotate list's last value
            // persisting per SVG 1.1 §10.4) still validly form their own 2-character run.
            var g = Render($"""<text x="10" y="50" font-size="20" rotate="15 0">{Beh}{Yeh}{Teh}</text>""");

            var runCall = Assert.Single(g.DrawStringCalls, d => d.Features!.Value.JoiningForms is not null);
            Assert.Equal(Yeh + Teh, runCall.Text);
            Assert.Equal(new[] { ArabicJoiningForm.Medi, ArabicJoiningForm.Fina }, runCall.Features!.Value.JoiningForms);
        }

        [Fact]
        public void PlainLatinText_NeverGetsAScriptTagOrJoiningForms()
        {
            var g = Render("""<text x="10" y="50" font-size="20">Hello</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.NotEqual("arab", draw.Features!.Value.ScriptTag);
            Assert.Null(draw.Features.Value.JoiningForms);
        }
    }
}
