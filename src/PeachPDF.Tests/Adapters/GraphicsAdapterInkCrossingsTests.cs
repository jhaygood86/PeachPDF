using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// <see cref="GraphicsAdapter.GetInkCrossings"/> — the real-font half of
    /// <c>text-decoration-skip-ink</c>: where a run's glyphs actually cross a horizontal band.
    /// </summary>
    /// <remarks>
    /// Coordinates are the same user space <c>DrawString</c> paints in, with the baseline at the origin's
    /// y, so a band below the baseline means "under the text". Both bundled fonts are exercised: the
    /// TrueType one has <c>glyf</c> outlines to decode, and the OpenType one has CFF outlines this
    /// engine's decoder cannot read, which is the documented limitation this reports as "no ink known"
    /// rather than as "no ink".
    /// </remarks>
    public class GraphicsAdapterInkCrossingsTests
    {
        [Fact]
        public async Task ADescender_CrossesABandBelowTheBaseline()
        {
            using var fixture = await Fixture.CreateAsync();

            var crossings = fixture.Crossings("g", below: 2, height: 1);

            Assert.NotNull(crossings);

            // One crossing, not two: a band through the open loop of a descender cuts both its walls,
            // but each glyph is reported as a single hulled range so the decoration breaks once around
            // the letter instead of leaving a wisp of underline stranded inside the loop. See
            // GraphicsAdapter.MeasureInkCrossings and RGraphics.GetInkCrossings.
            var only = Assert.Single(crossings);
            Assert.True(only.End > only.Start, "the crossing should be a real interval");
            Assert.True(only.Start >= -0.01 && only.End <= fixture.Advance("g") + 0.01,
                $"the crossing ({only.Start}..{only.End}) should lie within the glyph's own advance");
        }

        [Fact]
        public async Task AGlyphWhoseInkIsInSeveralPieces_IsReportedAsOneRangeCoveringAllOfThem()
        {
            // CSS Text Decoration 4 §2.10.5 leaves the skip shape to the UA, and both Chrome and Firefox
            // break the line once per glyph rather than once per ink run - so an 'o', whose counter puts
            // its two sides in the band as separate runs, must come back as a single range spanning the
            // whole letter. Reporting the two runs separately is what left a stub of underline drawn
            // across the inside of the letter.
            using var fixture = await Fixture.CreateAsync();

            var crossings = fixture.Crossings("o", below: -4, height: 2);

            Assert.NotNull(crossings);

            var only = Assert.Single(crossings);
            Assert.True(only.End - only.Start > fixture.Advance("o") * 0.5,
                $"the range ({only.Start}..{only.End}) should span the letter, not one of its two sides");
        }

        [Fact]
        public async Task ALetterWithNoDescender_CrossesNothingBelowTheBaseline()
        {
            using var fixture = await Fixture.CreateAsync();

            var crossings = fixture.Crossings("n", below: 2, height: 1);

            Assert.NotNull(crossings);
            Assert.Empty(crossings);
        }

        [Fact]
        public async Task EachDescenderInARun_ReportsInkAtItsOwnPenPosition()
        {
            // The crossings have to advance with the pen rather than all landing on the first glyph, and
            // the letter between the two descenders has to leave a real gap.
            using var fixture = await Fixture.CreateAsync();

            var crossings = fixture.Crossings("gng", below: 2, height: 1);

            Assert.NotNull(crossings);
            Assert.True(crossings.Count >= 2, $"both descenders should be found, got {crossings.Count}");
            Assert.True(crossings[0].Start < fixture.Advance("g"),
                "the first crossing should belong to the leading 'g'");
            Assert.True(crossings[^1].Start >= fixture.Advance("gn"),
                $"the last crossing ({crossings[^1].Start}) should belong to the trailing 'g', past 'gn' ({fixture.Advance("gn")})");
        }

        [Fact]
        public async Task ABandThroughTheXHeight_CrossesEveryLetter()
        {
            using var fixture = await Fixture.CreateAsync();

            var crossings = fixture.Crossings("nnn", below: -4, height: 1);

            Assert.NotNull(crossings);
            Assert.NotEmpty(crossings);
            Assert.True(crossings[^1].End > fixture.Advance("nn"),
                "the run's crossings should reach the last letter");
        }

        [Fact]
        public async Task TheBaselineOriginMovesTheCrossingsWithIt()
        {
            using var fixture = await Fixture.CreateAsync();

            var atOrigin = fixture.Crossings("g", below: 2, height: 1, originX: 0)!;
            var shifted = fixture.Crossings("g", below: 2, height: 1, originX: 50)!;

            Assert.Equal(atOrigin.Count, shifted.Count);
            Assert.Equal(
                atOrigin.Select(c => Math.Round(c.Start + 50, 3)),
                shifted.Select(c => Math.Round(c.Start, 3)));
        }

        [Fact]
        public async Task LetterSpacing_SpreadsTheCrossingsApart()
        {
            using var fixture = await Fixture.CreateAsync();

            var tight = fixture.Crossings("gng", below: 2, height: 1)!;
            var loose = fixture.Crossings("gng", below: 2, height: 1, letterSpacing: 10)!;

            // The trailing 'g' sits two advances along, so two letter-spacings of extra room precede it.
            Assert.Equal(tight.Count, loose.Count);
            Assert.Equal(tight[^1].Start + 20, loose[^1].Start, 3);
            Assert.Equal(tight[0].Start, loose[0].Start, 3);
        }

        [Fact]
        public async Task TheSameWordOnALaterLine_ReportsInkAtItsOwnPosition()
        {
            // The measurement is cached with the run's absolute position factored out - keyed by the band
            // RELATIVE to the baseline - so the same word one line down is a cache hit. What must not
            // happen is the hit returning the first line's absolute coordinates: the crossings have to be
            // re-offset to this run's own origin every time.
            using var fixture = await Fixture.CreateAsync();

            var firstLine = fixture.Crossings("g", below: 2, height: 1, originX: 10, originY: 0)!;
            var secondLine = fixture.Crossings("g", below: 2, height: 1, originX: 70, originY: 48)!;

            Assert.NotEmpty(firstLine);
            Assert.Equal(firstLine.Count, secondLine.Count);
            Assert.Equal(
                firstLine.Select(c => Math.Round(c.Start + 60, 3)),
                secondLine.Select(c => Math.Round(c.Start, 3)));
        }

        [Fact]
        public async Task ADifferentBandOverTheSameWord_IsMeasuredAgainRatherThanServedFromTheCache()
        {
            // Underline and overline cross different parts of the same glyphs, so the band is part of the
            // cache key. A key that ignored it would answer the second query with the first's spans.
            using var fixture = await Fixture.CreateAsync();

            var belowBaseline = fixture.Crossings("n", below: 2, height: 1)!;
            var throughTheLetter = fixture.Crossings("n", below: -4, height: 1)!;

            Assert.Empty(belowBaseline);
            Assert.NotEmpty(throughTheLetter);
        }

        [Fact]
        public async Task AnInvertedBand_ReportsNoInkKnown()
        {
            using var fixture = await Fixture.CreateAsync();

            Assert.Null(fixture.Crossings("g", below: 2, height: -1));
        }

        [Fact]
        public async Task ARunOfSpaces_ReportsNoInkKnown()
        {
            // No glyph in the run has a decodable outline, which is indistinguishable from a font whose
            // outlines cannot be read at all - so null, not an empty list. The caller skips nothing
            // either way.
            using var fixture = await Fixture.CreateAsync();

            Assert.Null(fixture.Crossings("   ", below: 2, height: 1));
        }

        [Fact]
        public async Task ACffOutlineFont_ReportsNoInkKnown()
        {
            // GlyphOutlineDecoder reads glyf/loca only, so an OpenType/CFF font yields no decodable ink
            // and nothing is skipped under it. Conformant for `auto` (UA discretion) and a tracked gap
            // for `all` - see docs/html-css-support.md.
            using var fixture = await Fixture.CreateAsync(BundledFonts.Otf, "InkTestCff");

            Assert.Null(fixture.Crossings("g", below: 2, height: 1));
        }

        // ─── Fixture ─────────────────────────────────────────────────────────────

        private sealed class Fixture : IDisposable
        {
            private readonly XGraphics _measure;
            private readonly GraphicsAdapter _graphics;
            private readonly RFont _font;

            private Fixture(XGraphics measure, GraphicsAdapter graphics, RFont font)
            {
                _measure = measure;
                _graphics = graphics;
                _font = font;
            }

            internal static async Task<Fixture> CreateAsync(string? fontPath = null, string family = "InkTestFont")
            {
                var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
                await BundledFonts.RegisterFont(adapter, fontPath ?? BundledFonts.Ttf, family);

                var measure = XGraphics.CreateMeasureContext(new XSize(595, 842), XGraphicsUnit.Point, XPageDirection.Downwards);
                var graphics = new GraphicsAdapter(adapter, measure, 1.0);

                var font = adapter.GetFont(family, 40, RFontStyle.Regular);
                Assert.NotNull(font);

                return new Fixture(measure, graphics, font);
            }

            /// <summary>
            /// The ink of <paramref name="text"/> across a band <paramref name="height"/> tall whose top
            /// sits <paramref name="below"/> points under the baseline. A negative
            /// <paramref name="below"/> puts the band above the baseline, through the letters.
            /// </summary>
            /// <remarks>
            /// The run's origin is the point <c>DrawString</c> paints from, not its baseline — that is
            /// what <see cref="RGraphics.GetInkCrossings"/> takes, so that the adapter can place the
            /// baseline from the font's own unrounded metrics. The band is still expressed relative to the
            /// baseline here, which is what makes "2 points under the baseline" readable; the rounded
            /// <see cref="RFont.Ascent"/> used to locate it is within half a unit of the adapter's own
            /// figure, far finer than the 40pt fixtures below care about.
            /// <para>
            /// <paramref name="originY"/> moves the whole run down the page — a later line — and the band
            /// moves with it, so the band stays in the same place relative to this run's own baseline.
            /// </para>
            /// </remarks>
            internal System.Collections.Generic.IReadOnlyList<RInkSpan>? Crossings(
                string text, double below, double height, double originX = 0, double letterSpacing = 0,
                double originY = 0)
            {
                var top = originY + _font.Ascent + below;
                return _graphics.GetInkCrossings(text, _font, new RPoint(originX, originY), top, top + height, letterSpacing);
            }

            /// <summary>How far the pen advances over <paramref name="text"/>, for bounding a crossing.</summary>
            internal double Advance(string text) => _graphics.MeasureString(text, _font).Width;

            public void Dispose()
            {
                _graphics.Dispose();
                _measure.Dispose();
            }
        }
    }
}
