using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// <c>HtmlContainerInt.Clear()</c> disposes <c>Root</c> and resets several per-document tracking
    /// fields so a container can be reused across multiple <c>SetHtml</c> calls (the documented reuse
    /// pattern <c>PerformLayout</c>'s own container-query refinement loop relies on) without retaining
    /// the previous document's box tree. <c>FragmentTree</c>, the lazily-built id index
    /// (<c>_idIndex</c>/<c>_idIndexRoot</c>), and <c>CanvasBackgroundBox</c> were all left out of that
    /// reset even though each references the same <c>CssBox</c> tree <c>Root</c> does, keeping the whole
    /// disposed tree reachable until the next document happened to overwrite each of them independently.
    /// </summary>
    public class HtmlContainerIntClearTests
    {
        [Fact]
        public async Task Clear_ReleasesTheFragmentTreeSoTheOldBoxTreeCanBeCollected()
        {
            var weakRoot = await LayoutThenClearAsync("<html><body><p>Hello</p></body></html>");

            for (var i = 0; i < 5 && weakRoot.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.False(weakRoot.IsAlive);
        }

        [Fact]
        public async Task Clear_SetsFragmentTreeToNull()
        {
            var (_, container) = await LayoutHarness.LayoutAsync("<html><body><p>Hello</p></body></html>");
            Assert.NotNull(container.FragmentTree);

            container.Clear();

            Assert.Null(container.FragmentTree);
        }

        [Fact]
        public async Task Clear_ReleasesTheIdIndexSoTheOldBoxTreeCanBeCollected()
        {
            var weakRoot = await LayoutPopulateIdIndexThenClearAsync(
                "<html><body><p id='target'>Hello</p></body></html>");

            for (var i = 0; i < 5 && weakRoot.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.False(weakRoot.IsAlive);
        }

        [Fact]
        public async Task Clear_ThenNewDocument_GetBoxByIdResolvesAgainstTheNewTreeNotTheOld()
        {
            var (root1, container) = await LayoutHarness.LayoutAsync("<html><body><p id='target'>First</p></body></html>");
            var first = container.GetBoxById(root1, "target");
            Assert.NotNull(first);

            container.Clear();
            await container.SetHtml("<html><body><p id='target'>Second</p></body></html>", null);
            var root2 = container.Root!;

            var second = container.GetBoxById(root2, "target");

            Assert.NotNull(second);
            Assert.NotSame(first, second);
        }

        [Fact]
        public async Task Clear_SetsCanvasBackgroundBoxToNull()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<html><body style='background: red;'><p>Hello</p></body></html>");
            Assert.NotNull(container.CanvasBackgroundBox);

            container.Clear();

            Assert.Null(container.CanvasBackgroundBox);
        }

        // Isolated into its own method (not inlined into the test), matching FontFactoryCrossInstanceIsolationTests'
        // own established pattern for this - otherwise, in a Debug build, the JIT keeps a local like
        // `root` rooted in the test method's own live stack frame for the rest of that frame's scope
        // (reassigning it to null does not help), regardless of whether Clear() itself leaks anything.
        private static async Task<WeakReference> LayoutThenClearAsync(string html)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            container.Clear();
            return new WeakReference(root);
        }

        private static async Task<WeakReference> LayoutPopulateIdIndexThenClearAsync(string html)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            Assert.NotNull(container.GetBoxById(root, "target"));
            container.Clear();
            return new WeakReference(root);
        }
    }
}
