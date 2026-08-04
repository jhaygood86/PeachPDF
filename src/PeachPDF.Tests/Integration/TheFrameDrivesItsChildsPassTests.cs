using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A block-level box's layout pass is entered at the <i>frame</i>, which places the box before handing
    /// it its own content — <c>CssBox.LayoutBlockChild</c>, and the three phases
    /// <c>CssBox.DriveBlockChildPass</c> runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BlockPlacementSeamTests</c> pins <i>what</i> the frame resolves a child's offset against. These
    /// pin <i>who asks, and when</i>: the frame the pass is driven from is handed down by the caller rather
    /// than looked up by the box mid-layout, and the position is written before any of the box's content
    /// is laid out inside it. Both are what stop a later change from re-deriving a box's own position from
    /// inside its own layout, which is the shape the whole of <c>#390</c> exists to remove.
    /// </para>
    /// <para>
    /// The subject is the call sequence, so it is observed rather than inferred: a test-only
    /// <see cref="CssBox"/> subclass records the arguments its pass was driven with and what its frame's
    /// geometry already said at that moment — the same subclassing trick the driver-level and
    /// error-reporting tests use to state a condition no markup produces.
    /// </para>
    /// </remarks>
    public class TheFrameDrivesItsChildsPassTests
    {
        /// <summary>What one entry into a box's layout pass was driven with.</summary>
        private readonly record struct PassEntry(CssBox Frame, bool FramePlacesChild, double FrameTopAtEntry);

        /// <summary>
        /// A block box that records how each of its layout passes was entered, then lays out normally.
        /// </summary>
        private sealed class RecordingBox : CssBox
        {
            internal RecordingBox(CssBox parent) : base(parent, null)
            {
                InheritStyle(parent, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.Block, DisplayMode.Block);
                Height = "30pt";

                // Not empty, so an engine that skips whitespace-only anonymous children (flex, grid and
                // multicol all share that filter) treats it as a real item.
                Text = "recorded";
                ParseToWords();
            }

            internal List<PassEntry> Entries { get; } = [];

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                Entries.Add(new PassEntry(frame, framePlacesChild, ParentBox?.Location.Y ?? double.NaN));
                return base.PerformLayoutImp(g, frame, framePlacesChild);
            }
        }

        /// <summary>A box whose layout throws, for the frame's own error handling.</summary>
        private sealed class ThrowingBox : CssBox
        {
            internal ThrowingBox(CssBox? parent) : base(parent, null)
            {
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.Block, DisplayMode.Block);
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild) =>
                throw new InvalidOperationException("layout blew up");
        }

        private static RecordingBox AddRecordingChildOf(CssBox root, string id)
        {
            var host = LayoutHarness.FindById(root, id);
            Assert.NotNull(host);
            return new RecordingBox(host!);
        }

        /// <summary>
        /// The frame a pass is driven from is the box's own parent, and it is handed <i>down</i> — the box
        /// never looks it up, which is what stops it asking a frame that is not the one placing it.
        /// </summary>
        [Fact]
        public async Task AnOrdinaryBlockChild_IsDrivenByItsParentAsTheFrame()
        {
            RecordingBox? recorded = null;

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='host' style='height:80pt'></div>"),
                prepare: r => recorded = AddRecordingChildOf(r, "host"));

            Assert.NotNull(recorded);
            Assert.NotEmpty(recorded!.Entries);

            foreach (var entry in recorded.Entries)
            {
                Assert.Same(recorded.ParentBox, entry.Frame);
            }
        }

        /// <summary>
        /// An ordinary block child is one the frame places, so every pass of it is driven with the frame
        /// doing the placing.
        /// </summary>
        [Fact]
        public async Task AnOrdinaryBlockChild_IsPlacedByItsFrameOnEveryPass()
        {
            RecordingBox? recorded = null;

            var (_, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='host' style='height:80pt'></div>"),
                prepare: r => recorded = AddRecordingChildOf(r, "host"));

            Assert.NotNull(recorded);
            Assert.All(recorded!.Entries, entry => Assert.True(entry.FramePlacesChild));
        }

        /// <summary>
        /// <b>The frame's own position is already settled when its child's pass begins.</b> That is the
        /// ordering this seam exists to guarantee: the frame is placed and sized by <i>its</i> frame, and
        /// only then does it lay out the content that resolves against its origin. A box that re-derived
        /// its own position from inside its own layout would let a child observe a top it later moves away
        /// from.
        /// </summary>
        [Fact]
        public async Task AChildsPass_BeginsWithItsFrameAlreadyPlaced()
        {
            RecordingBox? recorded = null;

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='above' style='height:120pt'></div>"
                    + "<div id='host' style='height:80pt'></div>"),
                prepare: r => recorded = AddRecordingChildOf(r, "host"));

            var host = LayoutHarness.FindById(root, "host")!;

            Assert.NotNull(recorded);
            Assert.NotEmpty(recorded!.Entries);

            // The host sits below a 120pt sibling, so a position derived after the fact — or not yet
            // derived at all — is a visibly different number from its final one.
            Assert.True(host.Location.Y > 120);
            Assert.All(recorded.Entries, entry => Assert.Equal(host.Location.Y, entry.FrameTopAtEntry, 3));
        }

        /// <summary>
        /// A child a layout engine positions is simply a child the frame is <i>not asked</i> to place — the
        /// engine's commit pass drives it with the placement phase off, rather than letting it be placed
        /// and then having to notice, from inside its own layout, that it should not have been.
        /// </summary>
        /// <remarks>
        /// Its earlier passes in the same engine <i>are</i> placed by the frame, and harmlessly so: every
        /// one of those is a measurement the engine moves into place by translation afterwards. Both halves
        /// are asserted, because it is the pair that says the distinction is being drawn at all.
        /// </remarks>
        [Fact]
        public async Task AFlexItemsCommitPass_IsDrivenWithTheFrameNotPlacingIt()
        {
            RecordingBox? recorded = null;

            var (_, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='host' style='display:flex'><div>a</div></div>"),
                prepare: r => recorded = AddRecordingChildOf(r, "host"));

            Assert.NotNull(recorded);

            Assert.Contains(recorded!.Entries, entry => !entry.FramePlacesChild);
            Assert.Contains(recorded.Entries, entry => entry.FramePlacesChild);
        }

        /// <summary>
        /// A layout that throws is reported as a <see cref="HtmlRenderErrorType.Layout"/> failure by the
        /// frame that drove the pass, exactly as it was by the box's own <c>PerformLayout</c> before the
        /// pass moved there.
        /// </summary>
        [Fact]
        public async Task ALayoutThatThrows_IsReportedByTheFrameThatDroveThePass()
        {
            var thrown = await Assert.ThrowsAsync<HtmlRenderException>(async () =>
                await LayoutHarness.LayoutAsync(
                    LayoutHarness.Wrap("<div id='host'></div>"),
                    prepare: r => _ = new ThrowingBox(LayoutHarness.FindById(r, "host"))));

            Assert.Equal(HtmlRenderErrorType.Layout, thrown.RenderErrorType);
        }

        /// <summary>
        /// A box with no container has nowhere to report a layout failure <i>to</i>, so the frame swallows
        /// it — the one arm of that handler no document reaches, pinned here because it is the reason the
        /// null check exists at all.
        /// </summary>
        /// <remarks>
        /// Long-standing behaviour, carried over unchanged from <c>CssBox.PerformLayout</c>'s own handler:
        /// <c>HtmlContainerInt.RenderError</c> is what builds the exception, and a detached box has no
        /// container to build one with.
        /// </remarks>
        [Fact]
        public async Task ALayoutThatThrowsWithNoContainer_IsSwallowedRatherThanRethrown()
        {
            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<p>alpha</p>"),
                after: async (_, _, g) =>
                {
                    var detachedFrame = new ThrowingBox(null);
                    var detachedChild = new ThrowingBox(detachedFrame);

                    Assert.Null(detachedChild.HtmlContainer);

                    await detachedFrame.LayoutBlockChild(g, detachedChild);
                });
        }
    }
}
