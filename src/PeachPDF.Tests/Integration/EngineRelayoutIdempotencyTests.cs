using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Whether laying the same subtree out again reproduces the first result — the property a resumed
    /// fragmentainer pass depends on, since it re-runs an engine's measurement phases to rebuild the
    /// geometry it then resumes into.
    /// </summary>
    /// <remarks>
    /// Not theoretical. A repeating table <c>&lt;thead&gt;</c> is detached from the tree and replaced by
    /// one proxy per page, and nothing removes the proxies, so a second run over the same table finds no
    /// header group and measures taller. These pin the engines that a resumed pass may re-enter, so a
    /// regression shows up here rather than as content landing in the wrong fragmentainer.
    /// </remarks>
    public class EngineRelayoutIdempotencyTests
    {
        // Position and size of every box that carries an id, which is what a resumed pass re-derives.
        private static string GeometryOf(CssBox root, HtmlContainerInt container)
        {
            var parts = LayoutHarness.Descendants(root)
                .Where(b => !string.IsNullOrEmpty(b.HtmlTag?.TryGetAttribute("id")))
                .Select(b => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}@({1:F3},{2:F3})-({3:F3},{4:F3})",
                    b.HtmlTag!.TryGetAttribute("id"),
                    b.Location.X, b.Location.Y, b.ActualRight, b.ActualBottom));

            return string.Join("|", parts)
                   + string.Format(CultureInfo.InvariantCulture, "||size={0:F3}", container.ActualSize.Height);
        }

        private static async Task AssertStableAcrossLayouts(string body, double pageHeight = 842)
        {
            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap(body), passes: 3, GeometryOf, pageHeight: pageHeight);

            Assert.Equal(snapshots[0], snapshots[1]);
            Assert.Equal(snapshots[1], snapshots[2]);
        }

        private static string Items(int count, string extra = "") =>
            string.Concat(Enumerable.Range(1, count).Select(i =>
                $"<div id='i{i}' style='{extra}'>Item {i} with enough words in it to wrap onto more than "
                + "a single line when the column it sits in is narrow.</div>"));

        [Theory]
        [InlineData("display:flex; flex-wrap:wrap; gap:6pt")]
        [InlineData("display:flex; flex-direction:column")]
        [InlineData("display:grid; grid-template-columns:1fr 1fr; gap:6pt")]
        [InlineData("display:grid; grid-template-columns:repeat(3, 1fr)")]
        public async Task EngineContainer_LaidOutAgain_ReproducesItsGeometry(string containerStyle)
        {
            await AssertStableAcrossLayouts($"<div id='c' style='{containerStyle}'>{Items(6)}</div>");
        }

        // The shape that matters most for a resumed pass: content tall enough to cross a boundary, so the
        // second run starts from a tree the first one already paginated.
        [Theory]
        [InlineData("display:flex; flex-direction:column")]
        [InlineData("display:grid; grid-template-columns:1fr")]
        [InlineData("column-count:2")]
        public async Task EngineContainer_TallerThanAPage_LaidOutAgain_ReproducesItsGeometry(string containerStyle)
        {
            await AssertStableAcrossLayouts(
                $"<div id='c' style='{containerStyle}'>{Items(24)}</div>", pageHeight: 300);
        }

        // A plain block container, as the control: whatever the engines do, ordinary flow is stable, so a
        // failure above is the engine's and not the harness's.
        [Fact]
        public async Task PlainBlockFlow_LaidOutAgain_ReproducesItsGeometry()
        {
            await AssertStableAcrossLayouts($"<div id='c'>{Items(24)}</div>", pageHeight: 300);
        }
    }
}
