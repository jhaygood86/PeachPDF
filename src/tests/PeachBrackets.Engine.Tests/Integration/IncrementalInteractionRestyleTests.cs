using PeachBrackets;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The restyle in place of a change of the interaction state, at the level of the box tree: which boxes are marked, what a comparison
    /// of the matches under two states does to the tree, and that the styles it leaves are the ones loading the document again makes.
    /// </summary>
    public class IncrementalInteractionRestyleTests(Xunit.ITestOutputHelper output)
    {
        private const string Corpus = """
            <!DOCTYPE html><html><head><style>
            body { margin: 0 }
            .card { padding: 8px; color: rgb(10,10,10); height: 30px; }
            .a:hover { color: rgb(255,0,0); border-color: rgb(1,2,3) }
            .b:hover { background-color: rgb(0,0,255); opacity: .5 }
            .c { border: 2px solid currentColor }
            .c:hover { color: rgb(200,0,0); text-decoration: underline }
            .group:hover .g1 { filter: saturate(1.5); color: rgb(0,128,0) }
            .group:hover > .g2 { box-shadow: 0 2px 8px rgba(0,0,0,.4) }
            .group:hover + .sib { color: rgb(9,9,9) }
            .holder:has(.kid:hover) { background-color: rgb(7,7,7) }
            .n { &:hover { color: rgb(0,0,255) } .inner { color: rgb(5,5,5) } &:hover .inner { color: rgb(0,255,0) } }
            .i:is(:hover, :focus) { outline: 2px solid rgb(0,128,0) }
            .nt:not(:hover) { color: rgb(30,30,30) }
            .f:focus { outline: 1px solid blue }
            .s:hover { transform: scale(1.1) }
            .hid:hover { display: none }
            .pad:hover { padding: 30px }
            .bf::before { content: "b" }
            .bf:hover::before { color: rgb(255,0,0) }
            .gen:hover::after { content: "!" }
            </style></head><body>
            <div id=a class="card a">a</div>
            <div id=b class="card b">b</div>
            <div id=c class="card c">c</div>
            <div id=g class="group card"><span id=g1 class=g1>g1</span><div id=g2 class=g2>g2</div></div>
            <div id=sib class=sib>sib</div>
            <div id=h class=holder><span id=k class="kid card">kid</span></div>
            <div id=n class="n card">n<span class=inner>inner</span></div>
            <div id=i class="i card" tabindex=0>i</div>
            <div id=nt class="nt card">nt</div>
            <input id=f class=f>
            <div id=s class="s card">s</div>
            <div id=hid class="hid card">hid</div>
            <div id=pad class="pad card">pad</div>
            <div id=bf class="bf card">bf</div>
            <div id=gen class="gen card">gen</div>
            <ul><li class=a>1</li><li class=a>2</li><li class=a>3</li></ul>
            </body></html>
            """;

        private static async Task<(HtmlDocument Document, Canvas Canvas)> LoadAsync(string html, bool verify = false)
        {
            var context = new HtmlRenderContext();
            var size = new Size(800, 1200);
            var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = size.Width, PageHeightPoints = size.Height });
            var canvas = PdfCanvas.CreateMeasuring(context, size);
            await document.LayoutAsync(canvas);
            document.VerifyIncrementalRestyle = verify;
            return (document, canvas);
        }

        private static IEnumerable<CssBox> Walk(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes.ToList())
            {
                foreach (var inner in Walk(child))
                    yield return inner;
            }
        }

        [Fact]
        public async Task TheBoxesAStateRuleCouldReach_AreMarkedWhenTheyAreCascaded_SharedStyleSiblingsIncluded()
        {
            var (document, _) = await LoadAsync(Corpus);
            var marked = Walk(document.Container.Root!).Where(b => b.HasStatefulCandidates).Select(b => b.HtmlTag?.TryGetAttribute("id") ?? b.HtmlTag?.Name).ToList();

            foreach (var id in new[] { "a", "b", "c", "g1", "g2", "sib", "h", "k", "n", "i", "nt", "s", "hid", "pad", "bf" })
                Assert.Contains(id, marked);

            // The list items share one style among them (the same tag and class) yet each can be hovered.
            Assert.Equal(3, Walk(document.Container.Root!).Count(b => b.HtmlTag is { Name: "li" } && b.HasStatefulCandidates));

            // A rule on a box's own state (the user agent's :focus reaches every box) is not a reason to look at it when another's changes.
            var (plain, _) = await LoadAsync("<!DOCTYPE html><html><head><style>.x:hover + .y { color: red } .z:hover { color: blue }</style></head><body><ul><li>1</li></ul><b class=x>x</b><i class=y>y</i><u class=z>z</u></body></html>");
            var flagged = Walk(plain.Container.Root!).Where(b => b.HasRelationalStatefulCandidates).Select(b => b.HtmlTag?.Name).ToList();
            Assert.Equal(["i"], flagged);
            Assert.Contains(Walk(plain.Container.Root!), b => b.HtmlTag is { Name: "u" } && b.HasStatefulCandidates && !b.HasRelationalStatefulCandidates);
        }

        [Fact]
        public async Task ComparingTheMatchesUnderTwoStates_AddsNoBoxToTheTree()
        {
            var (document, _) = await LoadAsync(Corpus);
            var container = document.Container;
            var before = Walk(container.Root!).Count();
            var media = PeachBrackets.Html.MediaQueryContext.FromContainer(container, "screen");
            var gen = DomUtils.GetBoxById(container.Root, "gen")!;
            var from = new InteractionState();
            var to = new InteractionState { Hover = [gen.HtmlTag!.SourceOrdinal] };

            var verdict = container.CssData!.CompareStatefulMatches(media, gen, container.PreviousPassContainerSizes, container, from, to, ownStateChanged: true);

            // :hover::after would add a box for the element: said, not done.
            Assert.Equal(2, verdict);
            Assert.Equal(before, Walk(container.Root!).Count());
            Assert.DoesNotContain(gen.Boxes, b => b.IsAfterPseudoElement);
        }

        [Theory]
        [InlineData("a", true)]
        [InlineData("b", true)]
        [InlineData("c", true)]
        [InlineData("g1", true)]
        [InlineData("g2", true)]
        [InlineData("k", true)]
        [InlineData("n", true)]
        [InlineData("i", true)]
        [InlineData("nt", true)]
        [InlineData("s", true)]
        [InlineData("bf", true)]
        [InlineData("hid", false)]
        [InlineData("pad", false)]
        [InlineData("gen", false)]
        public async Task EveryElementOfTheCorpus_RestylesTheSameAsLoadingAgain(string id, bool inPlace)
        {
            var (document, canvas) = await LoadAsync(Corpus, verify: true);
            var bounds = document.GetElementBounds(id)!.Value;

            // The verification throws when the styles or the geometry differ from a reload's; it runs when the restyle was applied in place.
            var entered = await document.SetPointerStateAsync(0, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2), false, canvas);
            Assert.True(entered.Applied);
            Assert.True(inPlace == entered.Incremental, $"in place: {entered.Incremental}; {document.LastRestyleFallbackReason}");

            var left = await document.ClearPointerStateAsync(canvas);
            Assert.True(left.Applied);
        }

        // Boxes the load changes after the cascade: an inline that holds a block is made a block, an absolutely positioned inline is wrapped (and left static in
        // the wrapper), an item of a flex container is blockified even when it comes up through a display: contents box. Cascading one of them again on its own
        // takes each change back, which used to make every hover over one a full load.
        private const string CorrectedBoxes = """
            <!DOCTYPE html><html><head><style>
            body { margin: 0 }
            .row { display: flex }
            .shell { display: contents }
            .link { color: rgb(10,10,10); text-decoration: none; line-height: 1.5 }
            .link:hover { color: rgb(255,0,0); text-decoration: underline }
            .card { position: relative; display: block; width: 120px; height: 60px; color: rgb(0,0,100) }
            .card:hover { color: rgb(200,0,0); background-color: rgb(240,240,255) }
            .pic { position: absolute; left: 0; top: 0; width: 30px; height: 30px; background: rgb(0,128,0) }
            .card:hover .pic { opacity: .5 }
            </style></head><body>
            <a id=wrap class=link href=#><div style="width: 100px; height: 20px">block in an inline</div></a>
            <div class=row><span class=shell><a id=item class=link href=#>flex item</a></span></div>
            <a id=card class=card href=#><span id=pic class=pic></span>caption <b>bold</b></a>
            </body></html>
            """;

        [Theory]
        [InlineData("wrap")]
        [InlineData("item")]
        [InlineData("card")]
        public async Task ABoxTheLoadCorrectedAfterTheCascade_IsRestyledInPlaceTheSameAsLoadingAgain(string id)
        {
            var (document, canvas) = await LoadAsync(CorrectedBoxes, verify: true);
            var bounds = document.GetElementBounds(id)!.Value;

            // The verification throws when the styles or the geometry differ from a reload's.
            var entered = await document.SetPointerStateAsync(0, bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2), false, canvas);
            Assert.True(entered.Applied);
            Assert.True(entered.Incremental, document.LastRestyleFallbackReason);

            var left = await document.ClearPointerStateAsync(canvas);
            Assert.True(left.Applied);
            Assert.True(left.Incremental, document.LastRestyleFallbackReason);
        }

        [Fact]
        public async Task FocusMoves_AreRestyledTheSameAsLoadingAgain()
        {
            var (document, canvas) = await LoadAsync(Corpus, verify: true);

            var focused = await document.SetFocusAsync(document.GetElement("f"), true, canvas);
            Assert.True(focused.Applied);
            var moved = await document.SetFocusAsync(document.GetElement("i"), true, canvas);
            Assert.True(moved.Applied);
            var left = await document.SetFocusAsync(null, false, canvas);
            Assert.True(left.Applied);
        }

        [Fact]
        public async Task APageWithNoStateRule_IsNotRestyled()
        {
            var (document, canvas) = await LoadAsync("<!DOCTYPE html><html><body><div id=a>x</div></body></html>");
            var bounds = document.GetElementBounds("a")!.Value;

            var result = await document.SetPointerStateAsync(0, bounds.X + 2, bounds.Y + 2, false, canvas);

            Assert.True(result.Applied);
            Assert.Empty(result.DirtyRegions);
        }

        [Fact]
        public async Task OnALargeTailwindShapedPage_HoverIsMuchCheaperInPlaceThanLoadingAgain()
        {
            var html = new System.Text.StringBuilder(@"<!DOCTYPE html><html><head><style>"
                + @".item { display: block; padding: 4px; color: rgb(20,20,20) } .hover\:text:hover { color: rgb(255,0,0) } "
                + @".hover\:bg:hover { background-color: rgb(230,230,255) } .group:hover .group-hover\:sat { filter: saturate(1.5) } "
                + @".p-4 { padding: 16px } .hover\:underline:hover { text-decoration: underline }"
                + @"</style></head><body>");
            for (var i = 0; i < 1500; i++)
                html.Append($"<div id=i{i} class=\"item group hover:text hover:bg\"><span class=\"group-hover:sat p-4\">item {i}</span><a class=\"hover:underline\" href=#>link</a></div>");
            html.Append("</body></html>");

            async Task<(double Milliseconds, bool Incremental)> HoverAsync(bool incremental)
            {
                var context = new HtmlRenderContext();
                var size = new Size(800, 1200);
                using var document = await HtmlDocument.LoadAsync(html.ToString(), context,
                    new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = size.Width, PageHeightPoints = size.Height, IncrementalRestyle = incremental });
                var canvas = PdfCanvas.CreateMeasuring(context, size);
                await document.LayoutAsync(canvas);
                var bounds = document.GetElementBounds("i3")!.Value;

                // One warm-up change, then the timed one.
                await document.SetPointerStateAsync(0, bounds.X + 2, bounds.Y + 2, false, canvas);
                var next = document.GetElementBounds("i5")!.Value;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var result = await document.SetPointerStateAsync(0, next.X + 2, next.Y + 2, false, canvas);
                return (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, result.Incremental);
            }

            var full = await HoverAsync(incremental: false);
            var inPlace = await HoverAsync(incremental: true);
            output.WriteLine($"hover on 1500 items: loading again {full.Milliseconds:F1} ms, in place {inPlace.Milliseconds:F1} ms");

            Assert.False(full.Incremental);
            Assert.True(inPlace.Incremental);
            Assert.True(inPlace.Milliseconds < full.Milliseconds / 2, $"{inPlace.Milliseconds:F1} ms in place against {full.Milliseconds:F1} ms loading again");
        }
    }
}
