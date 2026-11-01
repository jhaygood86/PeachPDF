using PeachBrackets.Html.Dom;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A break between two atomic inlines (an inline-table, inline-grid or inline-flex) resumed at the position of the first
    /// of a run, because only words took a place in the walk's order: the next page placed every one of them again from the
    /// top, over and over, and layout never finished (about 1000 inline-tables on an A4 page).
    /// </summary>
    public class AtomicInlineRunPaginationTests
    {
        private static string Run(string display, string content, string separator, int count) =>
            "<!DOCTYPE html><html><head><style>body{margin:0;font:14px/1.3 Arial}</style></head><body>"
            + string.Concat(Enumerable.Range(0, count).Select(i => display == "inline-table"
                ? $"<table class='a' style='display:inline-table'><tr><td>{content}{i}</td></tr></table>{separator}"
                : $"<div class='a' style='display:{display}'>{content}{i}</div>{separator}"))
            + "</body></html>";

        private static void Collect(CssBox box, List<CssBox> into)
        {
            if (box.GetAttribute("class") == "a") into.Add(box);
            foreach (var child in box.Boxes) Collect(child, into);
        }

        [Theory]
        [InlineData("inline-table", "", "")]
        [InlineData("inline-table", "", " ")]
        [InlineData("inline-table", "x", "")]
        [InlineData("inline-grid", "x", "")]
        [InlineData("inline-grid", "x", " ")]
        [InlineData("inline-flex", "x", "")]
        [InlineData("inline-flex", "x", " ")]
        [InlineData("inline-table", "x", " <span> </span>")]
        public async Task ARunOfAtomicInlines_PastThePageFoot_ContinuesOnTheNextPageAndPlacesEachOnce(string display, string content, string separator)
        {
            const int count = 400;
            var layout = Task.Run(() => LayoutHarness.LayoutAsync(Run(display, content, separator, count), pageWidth: 300, pageHeight: 200, margin: 10));
            var finished = await Task.WhenAny(layout, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            Assert.Same(layout, finished);

            var (root, container) = await layout;
            var boxes = new List<CssBox>();
            Collect(root, boxes);

            Assert.Equal(count, boxes.Count);

            // Every box has a place of its own: placed again from the top on each page, they would pile up in a few spots.
            Assert.Equal(count, boxes.Select(b => (Math.Round(b.Location.X, 2), Math.Round(b.Location.Y, 2))).Distinct().Count());

            // In document order, down the page and on to the next.
            for (var i = 1; i < boxes.Count; i++)
            {
                var before = boxes[i - 1].Location;
                var after = boxes[i].Location;
                Assert.True(after.Y > before.Y - 0.01 || after.X > before.X, $"box {i} at {after} is not after box {i - 1} at {before}");
            }

            // More than one page of them, and no more passes than pages.
            var lastSlot = container.SlotStartingAt(boxes[^1].Location.Y);
            Assert.True(lastSlot >= 1, "the run should reach a second page");
            Assert.True(container.FragmentainerPasses <= lastSlot + 2, $"{container.FragmentainerPasses} passes for {lastSlot + 1} pages");

            // A stale page is rebuilt now and then; one per page per pass was the quadratic blow-up of the endless layout.
            Assert.True(container.CatchUpSlotEmits <= container.FragmentainerPasses, $"{container.CatchUpSlotEmits} catch-ups");
        }

        [Fact]
        public async Task AThousandInlineTables_OnAnA4Page_Finish()
        {
            var layout = Task.Run(() => LayoutHarness.LayoutAsync(
                Run("inline-table", "x", " ", 1000), pageWidth: 595, pageHeight: 842, margin: 15));
            var finished = await Task.WhenAny(layout, Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            Assert.Same(layout, finished);

            var (_, container) = await layout;

            Assert.True(container.FragmentainerPasses <= 5, $"{container.FragmentainerPasses} passes");
        }
    }
}
