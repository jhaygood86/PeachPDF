using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Fragments
{
    /// <summary>
    /// Direct unit coverage for <see cref="BoxGeometrySnapshot.Translate"/>'s multi-root fallback path -
    /// a snapshot captured via the <c>Capture(IEnumerable&lt;CssBox&gt;, IReadOnlySet&lt;CssBox&gt;?)</c>
    /// overload has no single <c>_translationRoot</c>, so <c>Translate</c> falls back to its pre-#787
    /// unconditional per-box shift. Not reachable through any end-to-end layout today (that overload is
    /// only ever combined with <c>ReflectSubtree</c>/single-root <c>Translate</c> callers in
    /// practice - see <c>CssLayoutEngineColumns.FillColumns</c>), but a real, defensively-kept code path
    /// worth pinning directly rather than leaving uncovered.
    /// </summary>
    public class BoxGeometrySnapshotTests
    {
        [Fact]
        public async Task Translate_MultiRootSnapshot_ShiftsEveryCapturedBoxUnconditionally()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='outer' style='width:10pt; height:10pt;'>" +
                "<div id='inner' style='width:8pt; height:8pt;'></div>" +
                "</div>"));

            var outer = LayoutHarness.FindById(root, "outer")!;
            var inner = LayoutHarness.FindById(root, "inner")!;
            var outerLocationBefore = outer.Location;
            var innerLocationBefore = inner.Location;

            var snapshot = BoxGeometrySnapshot.Capture([outer]);

            snapshot.Translate(5, 7);

            Assert.True(snapshot.TryGetGeometry(outer, out var outerGeometry));
            Assert.Equal(outerLocationBefore.X + 5, outerGeometry.Location.X);
            Assert.Equal(outerLocationBefore.Y + 7, outerGeometry.Location.Y);

            Assert.True(snapshot.TryGetGeometry(inner, out var innerGeometry));
            Assert.Equal(innerLocationBefore.X + 5, innerGeometry.Location.X);
            Assert.Equal(innerLocationBefore.Y + 7, innerGeometry.Location.Y);

            // The live boxes themselves are untouched - Translate only mutates the snapshot's own copy.
            Assert.Equal(outerLocationBefore, outer.Location);
            Assert.Equal(innerLocationBefore, inner.Location);
        }
    }
}
