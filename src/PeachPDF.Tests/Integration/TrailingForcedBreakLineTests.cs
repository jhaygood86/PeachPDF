using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A forced break ends the line it falls on, so one at the very end of a block's content leaves
    /// no line behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A forced line break (<see href="https://www.w3.org/TR/css-text-3/#forced-line-break">css-text-3
    /// §5.5</see>) ends the line it falls on. This engine closes that line and starts the next one with
    /// the break's own word, so a break was always <i>followed</i> by a line — including where the break
    /// is the last thing in the block and there is nothing for that line to hold. CSS 2.1
    /// <see href="https://www.w3.org/TR/CSS21/visuren.html#inline-formatting">§9.4.2</see> requires such
    /// a line to "be treated as not existing for any other purpose"; every such block measured one line
    /// taller than a browser makes it.
    /// </para>
    /// <para>
    /// The heights here are all stated as multiples of the block's own line, taken from a
    /// single-line control in the same document, so the test says nothing about what a line
    /// happens to measure.
    /// </para>
    /// </remarks>
    public class TrailingForcedBreakLineTests
    {
        // line-height is pinned well clear of anything the font's own metrics could ask for, so a
        // line box is exactly that height whatever it holds. Both halves matter: under `normal` a
        // break-only line and a text line differ by a fraction of a point, and a line-height merely
        // close to the font's own extent leaves a text line taller than the declared value on some
        // platforms while an empty one still measures it exactly — 13.3pt against 12pt on Windows,
        // which is what a first version of this fixture failed on. Neither difference is this
        // test's subject; the number of lines is.
        private const string Sizing = "width:200pt;font-size:10pt;line-height:30pt";

        /// <param name="content">the probe block's inline content</param>
        /// <param name="lines">the number of line boxes a browser gives it</param>
        [Theory]
        // A break with nothing after it ends the line it is on and opens none.
        [InlineData("x<br>", 1)]
        [InlineData("<br>", 1)]
        // Only the last one: each earlier break still opens the line that follows it.
        [InlineData("x<br><br>", 2)]
        [InlineData("<br><br>", 2)]
        // A break that something does follow keeps its line, wherever the content comes from.
        [InlineData("x<br><br>c", 3)]
        [InlineData("x<br>c", 2)]
        // The control: no break at all.
        [InlineData("x", 1)]
        public async Task ABlockIsAsTallAsTheLinesABrowserGivesIt(string content, int lines)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='one' style='{Sizing}'>x</div>"
                + $"<div id='probe' style='{Sizing}'>{content}</div>"));

            var one = LayoutHarness.FindById(root, "one")!;
            var probe = LayoutHarness.FindById(root, "probe")!;
            var lineHeight = one.ActualBottom - one.Location.Y;

            Assert.Equal(lines * lineHeight, probe.ActualBottom - probe.Location.Y, 3);
        }

        /// <summary>
        /// A block-level sibling after the break starts its own line, so the break's own line is as
        /// absent as it is at the end of the block — the case a barcode or logo drawn as
        /// <c>label&lt;br&gt;&lt;img style="display:block"&gt;</c> hits on every row of a table.
        /// </summary>
        [Fact]
        public async Task ABreakBeforeABlockSiblingOpensNoLineEither()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='one' style='{Sizing}'>x</div>"
                + $"<div id='probe' style='{Sizing}'>x<br><span style='display:block'>y</span></div>"));

            var one = LayoutHarness.FindById(root, "one")!;
            var probe = LayoutHarness.FindById(root, "probe")!;
            var lineHeight = one.ActualBottom - one.Location.Y;

            // The "x" line and the block's own line, and nothing between them.
            Assert.Equal(2 * lineHeight, probe.ActualBottom - probe.Location.Y, 3);
        }

        /// <summary>
        /// A block with no inline content at all has no line to take back — its own empty line box is
        /// not a break's leftover, and removing it would collapse a block that is already zero-height
        /// into something the walk below it does not expect.
        /// </summary>
        [Fact]
        public async Task AnEmptyBlockIsUntouched()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='probe' style='{Sizing}'></div>"));

            var probe = LayoutHarness.FindById(root, "probe")!;

            Assert.Equal(0, probe.ActualBottom - probe.Location.Y, 3);
        }
    }
}
