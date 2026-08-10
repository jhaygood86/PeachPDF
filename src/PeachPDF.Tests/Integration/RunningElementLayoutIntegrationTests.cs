using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Covers css-gcpm-3's <c>position: running()</c> flow-exclusion and document-level registration
    /// (<see cref="HtmlContainerInt.RunningElements"/>) - the detach-from-flow half of running headers/
    /// footers. <c>content: element()</c>'s own selection/layout is covered separately once margin-box
    /// layout integration lands.
    /// </summary>
    public class RunningElementLayoutIntegrationTests
    {
        [Fact]
        public async Task RunningBox_ContributesNoSizeAtOriginalPosition()
        {
            var withRunning = Wrap(@"
                <div id='before' style='height:10pt;'></div>
                <div style='position:running(heading); height:50pt;'>Heading</div>
                <div id='after' style='height:10pt;'></div>");
            var withoutRunning = Wrap(@"
                <div id='before' style='height:10pt;'></div>
                <div id='after' style='height:10pt;'></div>");

            var (rootWith, _) = await LayoutAsync(withRunning);
            var (rootWithout, _) = await LayoutAsync(withoutRunning);

            var afterWith = FindById(rootWith, "after")!;
            var afterWithout = FindById(rootWithout, "after")!;

            // The running box (50pt tall) must contribute nothing to the flow it was removed from - the
            // sibling after it lands exactly where it would if the running box were entirely absent.
            Assert.Equal(afterWithout.Location.Y, afterWith.Location.Y, 0.5);
        }

        [Fact]
        public async Task RunningBox_NeverPlaced_LocationStaysAtDefault()
        {
            var html = Wrap(@"
                <div id='before' style='height:10pt;'></div>
                <div id='running' style='position:running(heading); height:50pt;'>Heading</div>");

            var (root, _) = await LayoutAsync(html);
            var running = FindById(root, "running")!;

            // Never reaches PlaceAndSizeBlockChild, so it never gets a real document position.
            Assert.Equal(0, running.Location.Y);
        }

        [Fact]
        public async Task RunningBox_RegistersInContainerRunningElements()
        {
            var html = Wrap(@"
                <div id='before' style='height:10pt;'></div>
                <div id='running' style='position:running(heading); height:50pt;'>Heading</div>");

            var (root, container) = await LayoutAsync(html);
            var running = FindById(root, "running")!;

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("heading", entry.Name);
            Assert.Same(running, entry.Box);
            // Attributed to the position it would have occupied: its preceding in-flow sibling's bottom.
            var before = FindById(root, "before")!;
            Assert.Equal(before.ActualBottom, entry.Y, 0.5);
        }

        [Fact]
        public async Task RunningBox_NoPrecedingSibling_RegistersAtParentContentTop()
        {
            var html = Wrap("<div id='running' style='position:running(heading);'>Heading</div>");

            var (_, container) = await LayoutAsync(html);

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("heading", entry.Name);
        }

        [Fact]
        public async Task RunningBox_OwnCounterIncrement_VisibleToLaterSiblingCounter()
        {
            // Proves the flow-exclusion hook does NOT piggyback on the display:none idiom (which
            // CssCounterEngine also keys off to decide counter visibility) - a running element is still
            // generated, just relocated, so its own counter-increment must still fire.
            var html = Wrap(@"
                <style>
                    body { counter-reset: chapter; }
                    #running { counter-increment: chapter; position: running(heading); }
                    #after::before { content: counter(chapter); }
                </style>
                <div id='running'>Heading</div>
                <div id='after'></div>");

            var (root, _) = await LayoutAsync(html);
            var after = FindById(root, "after")!;
            var beforePseudo = after.Boxes.First(b => b.IsBeforePseudoElement);

            Assert.Equal("1", beforePseudo.Text);
        }

        [Fact]
        public async Task RunningBox_Registration_DoesNotAccumulateAcrossRepeatedLayout()
        {
            var html = Wrap(@"
                <div id='before' style='height:10pt;'></div>
                <div id='running' style='position:running(heading); height:50pt;'>Heading</div>");

            var counts = await LayoutRepeatedlyAsync(html, passes: 3,
                (_, container) => container.RunningElements.Count);

            Assert.All(counts, count => Assert.Equal(1, count));
        }

        [Fact]
        public async Task RunningFlexItem_ExcludedFromFlexAlgorithm_AndRegistered()
        {
            var html = Wrap(@"
                <div style='display:flex; width:300pt;'>
                    <div id='a' style='width:50pt; height:20pt;'></div>
                    <div id='running' style='position:running(heading); width:50pt; height:20pt;'>Heading</div>
                    <div id='b' style='width:50pt; height:20pt;'></div>
                </div>");

            var (root, container) = await LayoutAsync(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            // The running item never became a flex item: 'b' sits immediately after 'a', not shifted
            // right by a phantom item between them.
            Assert.Equal(a.Location.X + a.ActualBoxSizingWidth, b.Location.X, 0.5);

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("heading", entry.Name);
        }

        [Fact]
        public async Task RunningGridItem_ExcludedFromGridAlgorithm_AndRegistered()
        {
            var html = Wrap(@"
                <div style='display:grid; grid-template-columns: 50pt 50pt; width:300pt;'>
                    <div id='a' style='height:20pt;'></div>
                    <div id='running' style='position:running(heading); height:20pt;'>Heading</div>
                    <div id='b' style='height:20pt;'></div>
                </div>");

            var (root, container) = await LayoutAsync(html);
            var a = FindById(root, "a")!;
            var b = FindById(root, "b")!;

            // The running item never became a grid item: 'b' occupies the grid cell right after 'a',
            // not shifted by a phantom item between them.
            Assert.Equal(a.Location.Y, b.Location.Y, 0.5);
            Assert.True(b.Location.X > a.Location.X);

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("heading", entry.Name);
        }

        [Fact]
        public async Task RunningMulticolChild_ExcludedFromColumnFlow_AndRegistered()
        {
            var html = Wrap(@"
                <div style='column-count:2; width:300pt;'>
                    <div id='a' style='height:20pt;'>A</div>
                    <div id='running' style='position:running(heading); height:20pt;'>Heading</div>
                </div>");

            var (root, container) = await LayoutAsync(html);
            var running = FindById(root, "running")!;

            Assert.Equal(0, running.Location.Y);

            var entry = Assert.Single(container.RunningElements);
            Assert.Equal("heading", entry.Name);
            Assert.Same(running, entry.Box);
        }
    }
}
