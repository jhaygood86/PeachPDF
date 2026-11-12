using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachBrackets.Engine.Tests.TestSupport;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A multi-column container under a vertical writing mode (css-multicol-1 with css-writing-modes-4):
    /// the columns run along the container's inline axis - physical Y - so they are horizontal strips
    /// stacked top to bottom, and the content of each flows along the block axis (right to left for
    /// <c>vertical-rl</c>, left to right for <c>vertical-lr</c>), continuing in the next strip.
    /// </summary>
    /// <remarks>
    /// The harness page has a 20pt margin, so a container laid out first in the body starts at (20, 20).
    /// Fixtures use <c>font-size: 10pt; line-height: 12pt</c> and <c>margin: 0</c> paragraphs; a vertical line
    /// ("column of text") is then a little over 12pt thick (the glyph box is 13.3pt) and 12pt apart.
    /// </remarks>
    public class MulticolWritingModeIntegrationTests
    {
        // Enough text to fill three 66pt strips in the bundled font.
        private static readonly string Words150 = string.Join(" ", Enumerable.Range(0, 80).Select(i => "w" + i.ToString("D3") + "aaa"));

        private static readonly string Words60 = string.Join(" ", Enumerable.Range(0, 60).Select(i => "w" + i.ToString("D2") + "aaa"));

        // Bundled so the fixtures measure the same text on every platform (the default fonts differ on Linux and macOS).
        private static Task Register(PeachPDF.Rendering.HtmlRenderContext renderContext) =>
            BundledFonts.RegisterFont(renderContext, BundledFonts.LiberationSans, "MulticolWritingModeSans");

        private const string Base = "font-family: 'MulticolWritingModeSans'; font-size: 10pt; line-height: 12pt;";

        private static CssBox P(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

        /// <summary>The top of every line of <paramref name="box"/>, in line order.</summary>
        private static List<double> LineTops(CssBox box) =>
            [.. box.LineBoxes.Select(l => l.Words.Min(w => w.Top))];

        private static List<double> LineLefts(CssBox box) =>
            [.. box.LineBoxes.Select(l => l.Words.Min(w => w.Left))];

        [Fact]
        public async Task VerticalRl_TwoColumns_AreStripsStackedDownTheInlineAxis_AndContentContinuesInTheNext()
        {
            // 200pt of inline size, 20pt gap -> two 90pt... (220 - 20) / 2 = 100pt strips at y 20 and y 140.
            // column-fill: auto fills the first strip up to the container's 300pt block size before using the next.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var p0 = P(root, "p0");
            var tops = LineTops(p0);
            var lefts = LineLefts(p0);

            Assert.True(tops.Distinct().Count() == 2, $"lines sit in exactly two strips, got tops {string.Join(",", tops.Distinct())}");
            Assert.Equal(20, tops.Min(), 1);
            Assert.Equal(140, tops.Max(), 1);

            // Strip 0 is full to the container's 300pt block size: floor(300 / 12) = 25 lines (the 25th ends at 24 * 12 + 13.3).
            var inFirst = tops.Count(t => t < 100);
            Assert.True(inFirst is >= 24 and <= 25, $"first strip holds ~25 lines of a 300pt block size, got {inFirst}");
            Assert.True(inFirst < tops.Count, "the rest of the paragraph continues in the second strip");

            // vertical-rl: lines start at the right edge and run leftwards, and the next strip starts at the right edge again.
            var firstOfSecond = lefts[inFirst];
            Assert.Equal(lefts[0], firstOfSecond, 1);
            for (var i = 1; i < inFirst; i++) Assert.True(lefts[i] < lefts[i - 1], "lines run right to left within a strip");

            // The strips lie inside the container: nothing is displaced past its right edge or below its box.
            var mc = P(root, "mc");
            Assert.All(p0.LineBoxes.SelectMany(l => l.Words), w =>
            {
                Assert.True(w.Right <= mc.ActualRight + 0.5);
                Assert.True(w.Bottom <= mc.ActualBottom + 0.5);
            });
        }

        [Fact]
        public async Task VerticalLr_Columns_RunLeftToRight_AndEachStripStartsAtTheLeftEdgeAgain()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-lr; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var p0 = P(root, "p0");
            var tops = LineTops(p0);
            var lefts = LineLefts(p0);
            var inFirst = tops.Count(t => t < 100);

            Assert.Equal(2, tops.Distinct().Count());
            Assert.Equal(lefts[0], lefts[inFirst], 1);
            Assert.Equal(20, lefts[0], 1);
            for (var i = 1; i < inFirst; i++) Assert.True(lefts[i] > lefts[i - 1], "lines run left to right within a strip");
        }

        [Fact]
        public async Task ColumnFillBalance_SplitsTheFlowEvenlyBetweenTheStrips()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var tops = LineTops(P(root, "p0"));
            var first = tops.Count(t => t < 100);
            var second = tops.Count - first;

            Assert.True(System.Math.Abs(first - second) <= 1, $"balanced strips hold {first} and {second} lines");
            Assert.True(first > 0 && second > 0);
        }

        [Fact]
        public async Task AutoWidth_ShrinksTheContainerToTheBalancedBlockSize()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var mc = P(root, "mc");
            var p0 = P(root, "p0");
            var lines = p0.LineBoxes.Count;
            var perStrip = (lines + 1) / 2;

            // Half the lines in each strip: the container is about perStrip lines wide, not the page wide.
            Assert.InRange(mc.ActualRight - mc.Location.X, perStrip * 12 - 1, perStrip * 12 + 2);
            Assert.Equal(220, mc.ActualBottom - mc.Location.Y, 1);
        }

        [Fact]
        public async Task ThreeColumns_StripPitchIsTheColumnWidthPlusTheGap()
        {
            // (220 - 2 * 10) / 3 = 66.67pt strips, 76.67pt apart.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 360pt; height: 220pt; column-count: 3; column-gap: 10pt; column-fill: auto; {{Base}}">
                  <p id="p0" style="margin:0">{{Words150}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var tops = LineTops(P(root, "p0")).Distinct().OrderBy(t => t).ToList();

            Assert.Equal(3, tops.Count);
            Assert.Equal(20, tops[0], 1);
            Assert.Equal(20 + 200d / 3 + 10, tops[1], 1);
            Assert.Equal(20 + 2 * (200d / 3 + 10), tops[2], 1);
        }

        [Fact]
        public async Task ColumnWidth_ResolvesTheColumnCountAgainstTheInlineSize()
        {
            // 220pt of inline size with 80pt columns and a 10pt gap: floor((220 + 10) / 90) = 2 columns.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 200pt; height: 220pt; column-width: 80pt; column-gap: 10pt; column-fill: auto; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var tops = LineTops(P(root, "p0")).Distinct().OrderBy(t => t).ToList();

            Assert.Equal(2, tops.Count);
            Assert.Equal(20, tops[0], 1);
            Assert.Equal(20 + (220 - 10) / 2d + 10, tops[1], 1);
        }

        [Fact]
        public async Task BlockChildren_StackAlongTheBlockAxis_AndAChildThatDoesNotFitStartsTheNextStrip()
        {
            // Each paragraph is 10 lines (120pt) in a 100pt-wide strip; a 250pt block size holds two whole ones
            // and 10 lines of the third (the break falls between lines).
            var para = string.Join(" ", Enumerable.Range(0, 10).Select(i => "line" + i + "aaaaaaaaaaaaaaaaaa"));
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 250pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="a" style="margin:0; break-inside: avoid">{{para}}</p>
                  <p id="b" style="margin:0; break-inside: avoid">{{para}}</p>
                  <p id="c" style="margin:0; break-inside: avoid">{{para}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var a = P(root, "a");
            var b = P(root, "b");
            var c = P(root, "c");

            // a then b share the first strip, b to the left of a (vertical-rl); c does not fit it and moves whole.
            Assert.Equal(20, a.Location.Y, 1);
            Assert.Equal(20, b.Location.Y, 1);
            Assert.True(b.Location.X < a.Location.X, "b follows a along the block axis, leftwards");
            Assert.Equal(140, c.Location.Y, 1);
            Assert.Equal(a.ActualRight, c.ActualRight, 1);
        }

        [Fact]
        public async Task ForcedColumnBreak_StartsANewStrip()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="a" style="margin:0">Short first paragraph.</p>
                  <p id="b" style="margin:0; break-before: column">Second paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var a = P(root, "a");
            var b = P(root, "b");

            Assert.Equal(20, a.Location.Y, 1);
            Assert.Equal(140, b.Location.Y, 1);
            Assert.Equal(a.ActualRight, b.ActualRight, 1);
        }

        [Fact]
        public async Task BreakInsideAvoid_MovesAFittingBlockToTheNextStrip_ButLetsAnOversizedOneBreak()
        {
            var tall = string.Join(" ", Enumerable.Range(0, 40).Select(i => "t" + i.ToString("D2") + "bbbbbbbbbbbbbb"));
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 120pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="a" style="margin:0">{{string.Join(" ", Enumerable.Range(0, 6).Select(i => "a" + i + "cccccccccccccccccc"))}}</p>
                  <p id="b" style="margin:0; break-inside: avoid">{{string.Join(" ", Enumerable.Range(0, 6).Select(i => "b" + i + "cccccccccccccccccc"))}}</p>
                  <p id="c" style="margin:0; break-inside: avoid">{{tall}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var b = P(root, "b");
            var c = P(root, "c");

            // b (6 lines = 72pt) would straddle the 120pt block size after a's 6 lines, so it moves whole.
            Assert.Equal(140, b.Location.Y, 1);

            // c is taller than any strip, so avoiding the break is impossible and it is split anyway.
            var cTops = LineTops(c).Distinct().Count();
            Assert.True(cTops >= 2, "an oversized break-inside: avoid block still fragments");
        }

        [Fact]
        public async Task OrphansAndWidows_KeepTheMinimumLinesOnEachSideOfTheBreak()
        {
            // 3 + 3 lines would split a 6-line block with room for 5 lines at 2 widows: widows moves lines across.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 62pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}} orphans: 2; widows: 2">
                  <p id="a" style="margin:0">{{string.Join(" ", Enumerable.Range(0, 6).Select(i => "a" + i + "cccccccccccccccccc"))}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var tops = LineTops(P(root, "a"));
            var first = tops.Count(t => t < 100);

            Assert.Equal(6, tops.Count);
            Assert.True(first >= 2 && tops.Count - first >= 2, $"orphans/widows of 2 leave {first} + {tops.Count - first} lines");
        }

        [Fact]
        public async Task DirectionRtl_StripsAreStackedBottomUp()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; direction: rtl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="a" style="margin:0">Short first paragraph.</p>
                  <p id="b" style="margin:0; break-before: column">Second paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var a = P(root, "a");
            var b = P(root, "b");

            Assert.True(a.Location.Y > b.Location.Y, "the first strip is the bottom one when the inline direction is right to left");
            Assert.Equal(140, a.Location.Y, 1);
            Assert.Equal(20, b.Location.Y, 1);
        }

        [Fact]
        public async Task TextDirectlyInTheContainer_IsColumnized()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">{{Words60}}</div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var mc = P(root, "mc");
            var tops = LineTops(mc).Distinct().OrderBy(t => t).ToList();

            Assert.Equal(2, tops.Count);
            Assert.Equal(20, tops[0], 1);
            Assert.Equal(140, tops[1], 1);
        }

        [Fact]
        public async Task ColumnSpanAll_CoversEveryColumn_AndSplitsTheFlowIntoRunsThatFillOnTheirOwn()
        {
            // The spanning heading covers the whole 220pt inline extent; the paragraph before it fills columns to its
            // right and the one after it columns to its left (vertical-rl runs right to left), each run balanced
            // independently of the other.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 600pt; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p id="before" style="margin:0">{{string.Join(" ", Enumerable.Range(0, 24).Select(i => "b" + i.ToString("D2") + "aaaaaaaaaaaaaa"))}}</p>
                  <h2 id="span" style="margin:0; column-span: all; font-size: 10pt; line-height: 12pt">Spanning heading</h2>
                  <p id="after" style="margin:0">{{string.Join(" ", Enumerable.Range(0, 24).Select(i => "a" + i.ToString("D2") + "aaaaaaaaaaaaaa"))}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var before = P(root, "before");
            var span = P(root, "span");
            var after = P(root, "after");

            Assert.Equal(20, span.Location.Y, 1);
            Assert.Equal(220, span.ActualBottom - span.Location.Y, 1);

            // Along the block axis: the run before, then the heading, then the run after (right to left).
            Assert.All(before.LineBoxes.SelectMany(l => l.Words), w => Assert.True(w.Left >= span.ActualRight - 0.5));
            Assert.All(after.LineBoxes.SelectMany(l => l.Words), w => Assert.True(w.Right <= span.Location.X + 0.5));

            // Each run is balanced across both columns on its own.
            Assert.Equal(2, LineTops(before).Distinct().Count());
            Assert.Equal(2, LineTops(after).Distinct().Count());
        }

        [Fact]
        public async Task NestedBlocks_AreCutBetweenTheirChildren_AndBreakAfterColumnEndsTheStrip()
        {
            // A wrapper's own children are break points too: the wrapper straddles the strips, its first paragraph
            // ends the first strip (break-after: column) and the next one starts the second.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <div id="wrap">
                    <p id="a" style="margin:0; break-after: column">First paragraph.</p>
                    <p id="b" style="margin:0">Second paragraph.</p>
                  </div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var a = P(root, "a");
            var b = P(root, "b");
            var wrap = P(root, "wrap");

            Assert.Equal(20, a.Location.Y, 1);
            Assert.Equal(140, b.Location.Y, 1);
            Assert.Equal(a.ActualRight, b.ActualRight, 1);

            // The wrapper reports its last piece, which holds b: it lies in the second strip.
            Assert.Equal(140, wrap.Location.Y, 1);
            Assert.Equal(b.ActualRight, wrap.ActualRight, 1);
        }

        [Fact]
        public async Task AnAtomicInlineBox_MovesWithTheLineItIsOn()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}} <span id="ib" style="display: inline-block; width: 10pt; height: 10pt; background: red"></span> tail</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var mc = P(root, "mc");
            var ib = P(root, "ib");

            // The flow ends in the second strip and nothing was displaced out of the container: every word and
            // every rectangle of the inline-block lies inside it, and the paragraph still has its two strips.
            var tops = LineTops(P(root, "p0")).Distinct().ToList();
            Assert.Equal(2, tops.Count);
            Assert.All(P(root, "p0").LineBoxes.SelectMany(l => l.Words), w => Assert.True(w.Bottom <= mc.ActualBottom + 0.5));
            Assert.All(ib.RectanglesView.Values, r => Assert.True(r.Bottom <= mc.ActualBottom + 0.5 && r.Right <= mc.ActualRight + 0.5));
        }

        [Fact]
        public async Task SingleColumn_VerticalContainerStacksItsBlockChildrenAlongTheBlockAxis()
        {
            // column-count: 1 is an ordinary vertical box: paragraphs stack right to left, not top to bottom.
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 1; {{Base}}">
                  <p id="a" style="margin:0">First paragraph.</p>
                  <p id="b" style="margin:0">Second paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var a = P(root, "a");
            var b = P(root, "b");

            Assert.Equal(a.Location.Y, b.Location.Y, 1);
            Assert.True(b.Location.X < a.Location.X);
        }

        [Fact]
        public async Task ColumnRule_IsAHorizontalLineInTheMiddleOfEachGap()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 360pt; height: 220pt; column-count: 3; column-gap: 10pt; column-fill: auto; column-rule: 1pt solid black; {{Base}}">
                  <p id="p0" style="margin:0">{{Words150}}</p>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var mc = P(root, "mc");

            var rules = mc.BlockAxisColumnRuleSegments;
            Assert.NotNull(rules);
            Assert.Equal(2, rules!.Count);
            Assert.Equal(20 + 200d / 3 + 5, rules[0].Y, 1);
            Assert.Equal(20 + 2 * 200d / 3 + 15, rules[1].Y, 1);
            Assert.All(rules, r => Assert.True(r.Right > r.Left));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, mc, g);
            var horizontal = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().Where(l => System.Math.Abs(l.Y1 - l.Y2) < 0.01 && l.X1 != l.X2).ToList();
            Assert.Equal(2, horizontal.Count);
        }

        [Fact]
        public async Task EveryWordIsEmittedExactlyOnce_AndLandsInTheStripItWasPlacedIn()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p id="a" style="margin:0">{{Words60}}</p>
                  <p id="b" style="margin:0">Second paragraph of the flow.</p>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var mc = P(root, "mc");
            var authored = LayoutHarness.Descendants(mc).SelectMany(b => b.Words).Count();

            var claims = new Dictionary<CssRect, int>();
            void Count(BoxFragment f)
            {
                foreach (var w in f.Words) claims[w.Word] = claims.GetValueOrDefault(w.Word) + 1;
                foreach (var c in f.Children) Count(c);
            }

            foreach (var page in container.FragmentTree!.Fragmentainers) Count(page.Root);

            Assert.Equal(authored, claims.Count);
            Assert.All(claims.Values, n => Assert.Equal(1, n));
        }

        [Fact]
        public async Task ABlockCutAcrossStrips_HasOneFragmentPerStrip_WithItsOwnDecorations()
        {
            // A bordered, shaded wrapper around the flow: its fragments cover only the part of each strip it holds,
            // with the block-start border on the first piece and the block-end border on the last (box-decoration-break: slice).
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; column-fill: auto; {{Base}}">
                  <div id="wrap" style="background: #ddd; border-right: 3pt solid red; border-left: 3pt solid blue"><p id="a" style="margin:0">{{Words60}}</p></div>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            var wrap = P(root, "wrap");

            var pieces = new List<BoxFragment>();
            void Find(BoxFragment f)
            {
                if (ReferenceEquals(f.Box, wrap)) pieces.Add(f);
                foreach (var c in f.Children) Find(c);
            }

            foreach (var page in container.FragmentTree!.Fragmentainers) Find(page.Root);

            Assert.Equal(2, pieces.Count);
            var ordered = pieces.OrderBy(p => p.Rect.Top).ToList();
            Assert.Equal(100, ordered[0].Rect.Height, 1);
            Assert.Equal(100, ordered[1].Rect.Height, 1);
            Assert.True(ordered[0].Rect.Top < ordered[1].Rect.Top);

            var first = ordered[0].Lines.Single().Slice;
            var last = ordered[1].Lines.Single().Slice;
            Assert.True(first.HasRightEdge, "vertical-rl: the block-start edge is the right one, on the first piece");
            Assert.False(first.HasLeftEdge, "the first piece is cut at its block-end side");
            Assert.False(last.HasRightEdge);
            Assert.True(last.HasLeftEdge);
        }

        [Fact]
        public async Task AContainerThatDoesNotFitTheRestOfThePage_MovesWholeToTheNextOne_StripsIncluded()
        {
            // 100pt of content leaves 160pt of a 260pt page band, short of the container's 220pt: it moves whole.
            var html = LayoutHarness.Wrap($$"""
                <div style="height: 100pt">first page content</div>
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p id="p0" style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 300, configureRenderContext: Register);
            var mc = P(root, "mc");
            var p0 = P(root, "p0");

            Assert.True(mc.Location.Y >= 280 - 0.5, $"the container starts on the second page (y = {mc.Location.Y})");
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);

            var claims = new Dictionary<CssRect, int>();
            void Count(BoxFragment f)
            {
                foreach (var w in f.Words) claims[w.Word] = claims.GetValueOrDefault(w.Word) + 1;
                foreach (var c in f.Children) Count(c);
            }

            Count(container.FragmentTree!.Fragmentainers[1].Root);
            Assert.All(p0.LineBoxes.SelectMany(l => l.Words), w => Assert.Equal(1, claims.GetValueOrDefault(w)));

            // Both strips are on that page: the strips are told apart by Y within it.
            Assert.True(LineTops(p0).Distinct().Count() == 2);
        }

        [Fact]
        public async Task VerticalMulticolContainer_IsMonolithicWithRespectToThePage()
        {
            var html = LayoutHarness.Wrap($$"""
                <div id="mc" style="writing-mode: vertical-rl; width: 300pt; height: 220pt; column-count: 2; column-gap: 20pt; {{Base}}">
                  <p style="margin:0">{{Words60}}</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            Assert.True(MonolithicContent.IsUnresumableOrthogonalFlow(P(root, "mc")));
        }

        [Fact]
        public async Task HorizontalTb_MulticolContainer_IsUnaffected()
        {
            var html = LayoutHarness.Wrap("""
                <div id="mc" style="column-count: 3; column-gap: 10pt; width: 300pt">
                  <p id="p0">First paragraph.</p>
                  <p id="p1" style="break-before: column">Second paragraph.</p>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, configureRenderContext: Register);
            Assert.True(P(root, "p1").Location.X > P(root, "p0").Location.X);
        }
    }
}
