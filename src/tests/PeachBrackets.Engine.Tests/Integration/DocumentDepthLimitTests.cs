using PeachBrackets.Html.Parse;
using PeachBrackets.Utilities;
using PeachPDF;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A document nested deeper than the machine stack can walk used to end the process with a stack overflow, which a
    /// host cannot catch (about 200 nested divs; fragment emission and layout recurse once per level). The recursive
    /// walks check the stack and fail as an <see cref="HtmlRenderException"/> at whatever depth the host's stack
    /// allows; the parser refuses a document past <see cref="HtmlParser.MaxElementDepth"/> outright.
    /// </summary>
    public class DocumentDepthLimitTests
    {
        private static string NestedDivs(int levels) =>
            "<!DOCTYPE html><html><head><style>body{margin:0;font:10pt Arial}</style></head><body>"
            + string.Concat(Enumerable.Repeat("<div>", levels)) + "x"
            + string.Concat(Enumerable.Repeat("</div>", levels)) + "</body></html>";

        /// <summary>Runs <paramref name="work"/> on a thread with a stack small enough to be exhausted by a modest document.</summary>
        private static Exception? RunOnASmallStack(Func<Task> work)
        {
            Exception? caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    work().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            }, maxStackSize: 512 * 1024);

            thread.Start();
            thread.Join();
            return caught;
        }

        [Fact]
        public async Task ANestedDocument_WithinTheStack_LaysOutAndPlacesItsText()
        {
            // Shallow enough for the 512 KB stack of a macOS test thread, which cannot hold 60.
            var (root, _) = await LayoutHarness.LayoutAsync(NestedDivs(20));

            Assert.True(root.ActualBottom > 0);
        }

        [Fact]
        public async Task ADocumentPastTheParserLimit_ThrowsAnHtmlRenderException()
        {
            var levels = HtmlParser.MaxElementDepth + 100;

            var ex = await Assert.ThrowsAsync<HtmlRenderException>(() => LayoutHarness.LayoutAsync(NestedDivs(levels)));

            Assert.Equal(HtmlRenderErrorType.HtmlParsing, ex.RenderErrorType);
            Assert.Contains(HtmlParser.MaxElementDepth.ToString(), ex.Message);
        }

        [Fact]
        public void ADocumentTooDeepForTheStack_FailsAsAnHtmlRenderException_NotAStackOverflow()
        {
            // 300 levels is under the parser's limit but far past what 512 KB of stack can lay out.
            var caught = RunOnASmallStack(() => LayoutHarness.LayoutAsync(NestedDivs(300)));

            Assert.IsType<HtmlRenderException>(caught);
        }

        [Fact]
        public void ANestedSvgTooDeepForTheStack_FailsAsAnHtmlRenderException_NotAStackOverflow()
        {
            var html = "<!DOCTYPE html><html><body><svg width='100' height='100'>"
                       + string.Concat(Enumerable.Repeat("<g>", 300)) + "<rect width='10' height='10' fill='red'/>"
                       + string.Concat(Enumerable.Repeat("</g>", 300)) + "</svg></body></html>";

            var caught = RunOnASmallStack(() => LayoutHarness.LayoutAsync(html));

            Assert.IsType<HtmlRenderException>(caught);
        }

        [Fact]
        public void ColorMixNestedPastTheLimit_IsAnInvalidColour_NotAStackOverflow()
        {
            // Each level of color-mix() nesting is five frames of the stack; 500 of them ended the process.
            var nested = string.Concat(Enumerable.Repeat("color-mix(in srgb, red, ", 500)) + "blue" + new string(')', 500);
            var html = $"<!DOCTYPE html><html><body><div style='color:{nested}'>x</div></body></html>";

            var caught = RunOnASmallStack(() => LayoutHarness.LayoutAsync(html));

            // Past the colour's own limit it is invalid; a stack this small may run out in the lexer first. Either
            // way the process survives.
            Assert.True(caught is null or HtmlRenderException, caught?.ToString());
        }

        [Fact]
        public async Task AWideDocument_IsNotCountedAsDeep()
        {
            var html = "<!DOCTYPE html><html><body>" + string.Concat(Enumerable.Repeat("<div><p>x</p></div>", 2000)) + "</body></html>";

            var (root, _) = await LayoutHarness.LayoutAsync(html);

            Assert.True(root.ActualBottom > 0);
        }

        [Fact]
        public void TheStackGuard_FailsAsAnHtmlRenderException_BeforeTheStackRunsOut()
        {
            var caught = RunOnASmallStack(() =>
            {
                Recurse();
                return Task.CompletedTask;
            });

            var ex = Assert.IsType<HtmlRenderException>(caught);
            Assert.Equal(HtmlRenderErrorType.Layout, ex.RenderErrorType);

            static int Recurse()
            {
                StackGuard.Ensure();
                return Recurse() + 1;
            }
        }

        [Fact]
        public void TheStackGuard_LetsAShallowCallThrough()
        {
            StackGuard.Ensure();
        }
    }
}
