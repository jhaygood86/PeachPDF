using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Fragments
{
    /// <summary>
    /// Direct unit coverage for <see cref="BoxGeometrySnapshot.Translate"/>'s multi-root fallback path -
    /// a snapshot captured via the <c>Capture(IEnumerable&lt;CssBox&gt;, IBoxExclusion?)</c>
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

        private const string Nested =
            "<div id='a'><div id='a1'><span id='a1x'>t</span></div><div id='a2'>t</div></div>" +
            "<div id='b'><div id='b1'>t</div></div>" +
            "<div id='c'>t</div>";

        private static async Task<(CssBox Body, BoxGeometrySnapshot Snapshot, BoxGeometrySnapshot Twin)> Captured()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Nested));
            var body = LayoutHarness.FindById(root, "a")!.ParentBox!;

            // Two captures of the one tree, so what one forgets can be compared with what the other does.
            return (body, BoxGeometrySnapshot.Capture([body]), BoxGeometrySnapshot.Capture([body]));
        }

        private static List<string> HeldIds(CssBox body, BoxGeometrySnapshot snapshot)
        {
            var ids = new List<string>();

            void Walk(CssBox box)
            {
                if (snapshot.Holds(box) && box.HtmlTag?.TryGetAttribute("id") is { } id) ids.Add(id);
                foreach (var child in box.Boxes) Walk(child);
            }

            Walk(body);
            return ids;
        }

        [Fact]
        public async Task ForgetMatching_DropsTheNamedBoxesAndEverythingUnderThem_LikeForgettingEachOne()
        {
            var (body, matching, forgetting) = await Captured();

            var moved = new BoxesBeyondColumn();
            moved.Add(LayoutHarness.FindById(body, "a1")!);
            moved.Add(LayoutHarness.FindById(body, "b")!);

            matching.ForgetMatching(moved);
            foreach (var box in moved.Enumerate()) forgetting.Forget(box);

            Assert.Equal(HeldIds(body, forgetting), HeldIds(body, matching));
            Assert.Equal(["a", "a2", "c"], HeldIds(body, matching));
        }

        [Fact]
        public async Task ForgetMatching_DropsTheChildrenOfARange()
        {
            var (body, snapshot, _) = await Captured();
            var a = LayoutHarness.FindById(body, "a")!;
            var moved = new BoxesBeyondColumn();
            moved.AddFrom(a.Boxes, 1);

            snapshot.ForgetMatching(moved);

            Assert.Equal(["a", "a1", "a1x", "b", "b1", "c"], HeldIds(body, snapshot));
        }

        [Fact]
        public async Task ForgetMatching_WithNothingBeyond_ForgetsNothing()
        {
            var (body, snapshot, _) = await Captured();
            var before = HeldIds(body, snapshot);

            snapshot.ForgetMatching(BoxesBeyondColumn.None);

            Assert.Equal(before, HeldIds(body, snapshot));
        }
    }
}
