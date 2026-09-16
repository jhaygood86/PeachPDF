using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>DomParser.CorrectReplacedElementBoxes</c> wraps a <c>display: block</c> <c>&lt;img&gt;</c>/inline
    /// <c>&lt;svg&gt;</c> in an anonymous block and forces the element itself back to <c>display: inline</c>,
    /// because this engine can only size a replaced element as a single atomic inline "word". That wrapper
    /// is never a real inline formatting context an author could see, so it must not reserve the CSS 2.1
    /// §10.8 strut a genuine line box would - doing so re-added the exact gap <c>display: block</c> exists
    /// to remove, and it compounded once per element (issue #1127).
    /// </summary>
    public class ReplacedBlockWrapperLayoutIntegrationTests
    {
        [Fact]
        public async Task BlockImage_InsideALargeFontContainer_IsNotInflatedByTheStrut()
        {
            // font-size/line-height are set far larger than the image so an included strut is
            // unmistakable - if the bug were present, "wrap" would measure closer to 120pt than 50pt.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='wrap' style='font-size:40pt;line-height:120pt'>"
                + "<img id='img' style='display:block;width:50pt;height:50pt' />"
                + "</div>"));

            var wrap = LayoutHarness.FindById(root, "wrap")!;
            var img = LayoutHarness.FindById(root, "img")!;

            // Non-vacuity: confirm the fixture actually produces the synthetic wrapper this test is
            // about, and that its strut - if it were still counted - would dwarf the image.
            var syntheticWrapper = Assert.Single(wrap.Boxes);
            Assert.True(syntheticWrapper.IsReplacedBlockWrapper);
            Assert.Same(img, Assert.Single(syntheticWrapper.Boxes));
            Assert.True(syntheticWrapper.ActualLineHeight > 100,
                $"fixture needs a strut far bigger than the image; line-height={syntheticWrapper.ActualLineHeight}");

            Assert.Equal(50, wrap.ActualBottom - wrap.Location.Y, 3);
            Assert.Equal(50, syntheticWrapper.ActualBottom - syntheticWrapper.Location.Y, 3);
        }

        [Fact]
        public async Task TableRows_WithBlockImages_DoNotDriftFromCompoundingStrutHeight()
        {
            // Reduced form of issue #1127's repro: a row's error, if present, compounds because every
            // row's cell gets its own anonymous wrapper and its own spurious strut.
            var html = LayoutHarness.Wrap(
                "<table id='t' style='border-collapse:collapse'>"
                + "<tr id='row1'><td style='padding:0'>r1</td><td style='padding:0'><img style='display:block;width:80pt;height:25pt' /></td></tr>"
                + "<tr id='row2'><td style='padding:0'>r2</td><td style='padding:0'><img style='display:block;width:80pt;height:25pt' /></td></tr>"
                + "<tr id='row3'><td style='padding:0'>r3</td><td style='padding:0'><img style='display:block;width:80pt;height:45pt' /></td></tr>"
                + "</table>"
                + "<p id='after' style='margin:0'>after</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            var table = LayoutHarness.FindById(root, "t")!;
            var row1 = LayoutHarness.FindById(root, "row1")!;
            var row2 = LayoutHarness.FindById(root, "row2")!;
            var row3 = LayoutHarness.FindById(root, "row3")!;
            var after = LayoutHarness.FindById(root, "after")!;

            Assert.Equal(25, row1.ActualBottom - row1.Location.Y, 2);
            Assert.Equal(row1.ActualBottom, row2.Location.Y, 2);
            Assert.Equal(25, row2.ActualBottom - row2.Location.Y, 2);
            Assert.Equal(row2.ActualBottom, row3.Location.Y, 2);
            Assert.Equal(45, row3.ActualBottom - row3.Location.Y, 2);

            // The error the issue describes accumulates: with the bug, each row - not just the first -
            // adds another whole strut on top of its image, so the table (and everything after it) ends
            // up far below where the sum of the three declared image heights says it should.
            Assert.Equal(95, table.ActualBottom - table.Location.Y, 2);
            Assert.Equal(table.ActualBottom, after.Location.Y, 2);
        }

        [Fact]
        public async Task OrdinaryInlineImage_StillReceivesTheLinesStrut()
        {
            // The guard is scoped to the synthetic display:block wrapper only - an author-declared
            // (default-display) inline image sitting beside text must still grow to the line's full
            // line-height when that exceeds the image, exactly as before this fix.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='font-size:10pt;line-height:40pt'>x<img id='img' style='width:5pt;height:5pt' /></div>"));

            var block = LayoutHarness.FindById(root, "d")!;
            var line = Assert.Single(block.LineBoxes);
            var img = LayoutHarness.FindById(root, "img")!;

            Assert.False(img.IsReplacedBlockWrapper);
            Assert.False(img.ParentBox!.IsReplacedBlockWrapper);

            Assert.Equal(40, line.BaselineExtent!.Value.Height, 2);
        }
    }
}
