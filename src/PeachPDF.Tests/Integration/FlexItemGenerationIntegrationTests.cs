using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Which children of a flex/grid container become items, and what shape they arrive in:
    /// <list type="bullet">
    /// <item>generated content (<c>::before</c>/<c>::after</c>) generates an item even with no text
    /// (css-flexbox-1 §4, css-grid-2 §6);</item>
    /// <item>a layout-internal display (<c>table-row</c> and friends) is blockified rather than handed to
    /// a table engine that never runs (css-display-3 §2.7);</item>
    /// <item>CSS 2.1 §9.2.1.1's anonymous block box — a block-container rule — is not created inside a
    /// flex/grid container, while each contiguous text sequence gets one anonymous item;</item>
    /// <item>a floated child remains an item because <c>float</c> is ignored for flex/grid children.</item>
    /// </list>
    /// Charts.css's area and line charts depend on several of these at once: an empty <c>td::after</c>
    /// spacer, sized only by its own <c>height</c>, sitting beside a <c>display: flex</c> label.
    /// </summary>
    public class FlexItemGenerationIntegrationTests
    {
        [Fact]
        public async Task EmptyAfterPseudoElement_IsAFlexItem_AndTakesItsOwnHeight()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  #c { display: flex; flex-direction: column; justify-content: flex-end;" +
                "       width: 200pt; height: 100pt; }" +
                "  #c::after { content: ''; width: 100%; height: 60pt; }" +
                "</style>" +
                "<div id='c'><span id='label'>L</span></div>"), margin: 20);

            var c = LayoutHarness.FindById(root, "c")!;
            var spacer = Assert.Single(c.Boxes, b => b.IsAfterPseudoElement);

            // The spacer is a real item: 60pt tall, flushed to the container's bottom, with the label
            // stacked directly above it rather than sharing its position.
            Assert.Equal(60, spacer.ActualBottom - spacer.Location.Y, 0.5);
            Assert.Equal(c.ClientBottom, spacer.ActualBottom, 0.5);

            var label = LayoutHarness.FindById(root, "label")!;
            Assert.Equal(spacer.Location.Y, label.ActualBottom, 0.5);
        }

        [Fact]
        public async Task InlineChild_BesideBlockSibling_IsNotWrappedInAnAnonymousBlock()
        {
            // CSS 2.1 §9.2.1.1's wrapper belongs to block containers. Created here, the wrapper became the
            // item and the inline child's own height sized nothing.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  #c { display: flex; flex-direction: column; width: 200pt; height: 100pt; }" +
                "  #block { display: flex; }" +
                "  #inline { height: 40pt; }" +
                "</style>" +
                "<div id='c'><div id='block'>B</div><span id='inline'>I</span></div>"), margin: 20);

            var c = LayoutHarness.FindById(root, "c")!;
            var inline = LayoutHarness.FindById(root, "inline")!;

            Assert.Equal(2, c.Boxes.Count);
            Assert.Same(inline, c.Boxes[1]);
            Assert.Equal(40, inline.ActualBottom - inline.Location.Y, 0.5);
        }

        [Fact]
        public async Task TableRowChildOfFlexContainer_IsBlockifiedAndKeepsItsText()
        {
            // A `display: table-row` flex item stayed layout-internal, so nothing laid it out: the row's
            // cells kept the origin they were born at and their words were never placed at all.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table><tbody style='display:flex'>" +
                "<tr id='r'><th>LABEL</th><td>VALUE</td></tr>" +
                "</tbody></table>"), margin: 20);

            var row = LayoutHarness.FindById(root, "r")!;
            Assert.Equal(DisplayMode.Block, row.Display.Value);

            var words = LayoutHarness.Descendants(row)
                .SelectMany(b => b.Words)
                .Select(w => w.Text)
                .ToList();
            Assert.Contains("LABEL", words);
            Assert.Contains("VALUE", words);

            // Laid out, not merely present: the cells sit side by side inside the row's own band.
            foreach (var cell in row.Boxes)
            {
                Assert.True(cell.ActualBottom > cell.Location.Y, $"'{cell}' has no height");
            }

            // And painted: the fragment tree is what paint reads, so a word missing there is invisible
            // however good the box tree looks.
            var painted = FragmentPaintHarness.FragmentOf(container, row)
                .Children.SelectMany(CollectWords)
                .ToList();
            Assert.Contains("LABEL", painted);
            Assert.Contains("VALUE", painted);
        }

        [Fact]
        public async Task IndefinitePercentageHeight_LeavesTheColumnMainAxisIndefinite_SoItemsStillStack()
        {
            // `height: 100%` against a containing block that is not itself height-calculated behaves as
            // automatic (CSS Box Sizing 4 §5). Reading it as "definite, and it came out 0" collapsed the
            // container's main size, and every item was placed at the content origin - drawn on top of
            // each other rather than stacked.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                "  #outer { width: 200pt; }" +
                "  #c { display: flex; flex-direction: column; height: 100%; }" +
                "  #a, #b { height: 30pt; }" +
                "</style>" +
                "<div id='outer'><div id='c'><div id='a'>A</div><div id='b'>B</div></div></div>"), margin: 20);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            Assert.Equal(30, a.ActualBottom - a.Location.Y, 0.5);
            Assert.Equal(a.ActualBottom, b.Location.Y, 0.5);
        }

        [Theory]
        [InlineData("flex")]
        [InlineData("grid")]
        public async Task TextRunsSeparatedByAComment_ShareOneAnonymousItem(string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c' style='display:{display}; width:200pt;'>" +
                "alpha<!-- comment separates the parser's data tokens -->beta" +
                "<div id='block'>block</div></div>"), margin: 20);

            var c = LayoutHarness.FindById(root, "c")!;
            var block = LayoutHarness.FindById(root, "block")!;

            Assert.Equal(2, c.Boxes.Count);
            Assert.Same(block, c.Boxes[1]);

            var textItem = c.Boxes[0];
            Assert.Null(textItem.HtmlTag);
            Assert.Equal(2, textItem.Boxes.Count);
            Assert.Equal(["alpha", "beta"], textItem.Boxes.SelectMany(b => b.Words).Select(w => w.Text));
        }

        [Fact]
        public async Task NestedFlexTextItem_KeepsItsMaxContentWidth()
        {
            // The structural half of the guard below: a text run inside a nested flex container is the item
            // itself. Wrap it again and the auto-sized wrapper is measured instead, which is what lets
            // `overflow-wrap: anywhere` split the text.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:flex; width:100pt; overflow-wrap:anywhere;'>" +
                "<span id='c' style='display:flex;'>$91M</span></div>"), margin: 20);

            var c = LayoutHarness.FindById(root, "c")!;
            var textItem = Assert.Single(c.Boxes);

            var line = Assert.Single(textItem.LineBoxes);
            Assert.Equal("$91M", string.Concat(line.Words.Select(w => w.Text)));
        }

        [Fact]
        public async Task AbsoluteColumnLabelInAStretchedFlexItem_DoesNotBreakMidWord()
        {
            // Charts.css's column axis label, reduced to the parts that matter: a `tbody` flex container
            // sized by aspect-ratio, rows that are stretched flex items carrying `overflow-wrap: anywhere`,
            // and an absolutely-positioned label that is itself a column flex container holding bare text.
            //
            // This is the shape that caught a wrapper around a *lone* text run: the extra anonymous box
            // became the item, its measured max-content width was a hair under the text's own, and
            // `overflow-wrap: anywhere` then split every label onto two lines - "Q"/"1", "Ma"/"r". The
            // charts rendered with bars intact and labels in pieces, which is easy to miss in a box-tree
            // assertion and impossible to miss on the page, so this asserts the words share one line.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<style>" +
                // The break is decided a fraction of a point either side of the text's own width, so the
                // measured font is part of the fixture, not incidental styling.
                "  body { font-family: sans-serif; margin: 20px; }" +
                "  .wrap { width: 345pt; }" +
                "  table, tbody, tr, th, td { display: block; margin: 0; padding: 0; border: 0; }" +
                "  table, table * { box-sizing: border-box; }" +
                "  tbody { position: relative; display: flex; justify-content: space-between;" +
                "          align-items: stretch; width: 100%; aspect-ratio: 21/9; }" +
                "  tr { position: relative; display: flex; justify-content: flex-start;" +
                "       flex: 1 1 0; overflow-wrap: anywhere; margin-block-end: 18pt; }" +
                "  th { position: absolute; inset: 0; height: 18pt; display: flex; flex-direction: column;" +
                "       align-items: center; justify-content: flex-end;" +
                "       margin-block-start: auto; margin-block-end: -19pt; }" +
                "  td { display: flex; justify-content: center; width: 100%; height: 60%;" +
                "       position: relative; }" +
                "</style>" +
                "<div class='wrap'><table><tbody>" +
                "<tr><th id='label'>Q1</th><td></td></tr>" +
                "<tr><th>Q2</th><td></td></tr>" +
                "<tr><th>Q3</th><td></td></tr>" +
                "<tr><th>Q4</th><td></td></tr>" +
                "</tbody></table></div>"), margin: 20);

            var label = LayoutHarness.FindById(root, "label")!;
            var words = LayoutHarness.Descendants(label).SelectMany(b => b.Words).ToList();

            Assert.Equal("Q1", string.Concat(words.Select(w => w.Text)));
            Assert.All(words, word => Assert.Equal(words[0].Top, word.Top, 0.5));
        }

        [Theory]
        [InlineData("flex")]
        [InlineData("grid")]
        public async Task FloatedChild_RemainsAnItem(string display)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='c' style='display:{display}; width:200pt;'>" +
                "<div id='floated' style='float:left; width:40pt; height:20pt;'>float</div>" +
                "<div>other</div></div>"), margin: 20);

            var floated = LayoutHarness.FindById(root, "floated")!;

            Assert.Equal(20, floated.ActualHeight, 0.5);
            var fragment = FragmentPaintHarness.FragmentOf(container, floated);
            var painted = fragment.Words.Select(w => w.Word.Text ?? string.Empty)
                .Concat(fragment.Children.SelectMany(CollectWords));
            Assert.Contains("float", painted);
        }

        [Fact]
        public async Task FloatedItem_IsPlacedByFlexLayoutAlone_NotAlsoDisplacedAsAFloat()
        {
            // Collecting a floated child as an item is only half the rule: while the box still answered
            // IsFloated, the float machinery displaced it *as well*, so a float between two ordinary items
            // dropped onto its own row and left a hole where flex had already placed it. `float` has no
            // effect on an item (css-flexbox-1 §4), so all three sit on one row at the same Y.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='c' style='display:flex; width:300pt;'>" +
                "<div id='a'>A</div>" +
                "<div id='b' style='float:left;'>B</div>" +
                "<div id='d'>C</div></div>"), margin: 20);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;
            var d = LayoutHarness.FindById(root, "d")!;

            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.Equal(a.Location.Y, d.Location.Y, 0.5);
            Assert.True(b.Location.X >= a.ActualRight - 0.5, "the float follows its preceding item");
            Assert.True(d.Location.X >= b.ActualRight - 0.5, "and leaves no hole before the next one");
        }

        private static System.Collections.Generic.IEnumerable<string> CollectWords(
            PeachPDF.Html.Core.Fragments.BoxFragment fragment)
        {
            foreach (var word in fragment.Words)
                yield return word.Word.Text ?? string.Empty;

            foreach (var child in fragment.Children)
                foreach (var text in CollectWords(child))
                    yield return text;
        }
    }
}
