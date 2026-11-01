using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Entities;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="BoxRareState"/> holds what only an unusual <see cref="CssBox"/> has. These pin that an ordinary box never
    /// allocates it, that writing a default value does not, and that a value written is read back.
    /// </summary>
    public class BoxRareStateTests
    {
        [Fact]
        public async Task OrdinaryBoxes_AfterLayout_HoldNoRareState()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='a'><p id='b'>some <b>bold</b> and <i>italic</i> text</p><ul><li>one</li><li>two</li></ul></div>"));

            var all = new List<CssBox>();
            void Collect(CssBox box) { all.Add(box); foreach (var child in box.Boxes) Collect(child); }
            Collect(root);

            // The root and the html/body shells carry IsRoot and similar; everything below holds nothing unusual.
            Assert.All(all.Where(b => b.HtmlTag?.Name is "div" or "p" or "b" or "i" or "ul" or "li"), box => Assert.False(box.HasRareState, box.HtmlTag?.Name));
        }

        [Fact]
        public void WritingADefaultValue_DoesNotAllocate()
        {
            var box = new CssBox(null, null);

            box.IsMarkerPseudoElement = false;
            box.FootnoteSourceBox = null;
            box.HasClearance = false;
            box.SectionBeginId = null;

            Assert.False(box.HasRareState);
        }

        [Fact]
        public void AValueWritten_IsReadBack_AndStaysAfterTheDefaultIsWrittenAgain()
        {
            var box = new CssBox(null, null);
            var other = new CssBox(null, null);

            box.FootnoteSourceBox = other;
            box.SectionBeginId = "sec";
            box.RelativeOffsetX = 4.5;

            Assert.True(box.HasRareState);
            Assert.Same(other, box.FootnoteSourceBox);
            Assert.Equal("sec", box.SectionBeginId);
            Assert.Equal(4.5, box.RelativeOffsetX);

            box.SectionBeginId = null;
            box.RelativeOffsetX = 0;

            Assert.Null(box.SectionBeginId);
            Assert.Equal(0, box.RelativeOffsetX);
            Assert.Same(other, box.FootnoteSourceBox);
        }

        [Fact]
        public void ReadingTheCollections_OfABoxThatHasNone_AllocatesNothing()
        {
            var box = new CssBox(null, null);

            Assert.False(box.TryGetCounter("c", out _));
            Assert.Null(box.GetCounterOrNull("c"));
            Assert.False(box.HasNamedStrings);
            Assert.Empty(box.NamedStringValues);
            Assert.False(box.TryGetNamedString("s", out _));
        }

        [Fact]
        public void WritingACounter_MakesItReadable()
        {
            var box = new CssBox(null, null);

            box.Counters["c"] = new CssCounter("c", 3, false, true, null);

            Assert.True(box.TryGetCounter("c", out var counter));
            Assert.Equal(3, counter.Value);
            Assert.Equal(3, box.GetCounterOrNull("c")!.Value);
        }

        [Fact]
        public async Task BoxesWithoutInlineGeometry_NeverAllocateARectanglesDictionary()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='block'><p id='p'>text <span id='s'>inline run</span> more</p></div>"));

            var block = LayoutHarness.FindById(root, "block")!;
            var span = LayoutHarness.FindById(root, "s")!;

            Assert.False(block.HasRectanglesDictionary);
            Assert.Equal(0, block.RectangleCount);
            Assert.Empty(block.RectangleKeys);
            Assert.Empty(block.RectangleValues);
            Assert.True(span.HasRectanglesDictionary);
            Assert.True(span.RectangleCount > 0);
        }

        [Fact]
        public void WritingARectangle_GivesTheBoxItsOwnDictionary_AndResetClearsIt()
        {
            var plain = new CssBox(null, null);
            var other = new CssBox(null, null);
            var line = new CssLineBox(plain);

            other.Rectangles.Add(line, new PeachDrawing.Core.Rect(1, 2, 3, 4));

            Assert.Equal(1, other.RectangleCount);
            Assert.Equal(0, plain.RectangleCount);
            Assert.False(plain.HasRectanglesDictionary);

            other.RemoveRectangle(line);
            Assert.Equal(0, other.RectangleCount);

            other.Rectangles.Add(line, new PeachDrawing.Core.Rect(1, 2, 3, 4));
            other.ClearRectangles();
            plain.ClearRectangles();
            plain.RemoveRectangle(line);

            Assert.Equal(0, other.RectangleCount);
            Assert.False(plain.HasRectanglesDictionary);
        }
    }
}
