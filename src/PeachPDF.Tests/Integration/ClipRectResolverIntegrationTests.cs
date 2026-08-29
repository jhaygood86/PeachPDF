using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer-B tests for <see cref="CssClipRectResolver.TryBuildClipRect"/>: resolves a validated legacy
    /// <c>clip</c> value against a known reference box and asserts the produced <see cref="RRect"/>.
    /// Unlike <see cref="ClipPathResolverIntegrationTests"/>, there is no <c>PixelsPerPoint</c>-division
    /// category of test here - the resolved <see cref="RRect"/> is undivided raw layout-space, and
    /// <c>RGraphics.PushClip(RRect)</c> (unlike its <c>PushClip(RGraphicsPath)</c> overload) already
    /// divides by <c>PixelsPerPoint</c> itself, so this resolver has no division of its own to verify.
    /// </summary>
    public class ClipRectResolverIntegrationTests
    {
        private static async Task<CssBox> BuildBoxAsync()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml("<div></div>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return container.Root!;
        }

        [Fact]
        public async Task Rect_CommaSeparated_ResolvesOffsetsFromTopAndLeftEdges()
        {
            var box = await BuildBoxAsync();
            var reference = new RRect(100, 200, 300, 400);

            // top/bottom are offsets from the top edge (200); right/left are offsets from the left edge (100).
            var built = CssClipRectResolver.TryBuildClipRect("rect(10pt, 250pt, 300pt, 40pt)", reference, box, out var rect);

            Assert.True(built);
            Assert.Equal(210, rect.Y, 3);      // 200 + 10 (top)
            Assert.Equal(140, rect.X, 3);      // 100 + 40 (left)
            Assert.Equal(350, rect.Right, 3);  // 100 + 250 (right)
            Assert.Equal(500, rect.Bottom, 3); // 200 + 300 (bottom)
        }

        [Fact]
        public async Task Rect_SpaceSeparated_ResolvesTheSameAsCommaSeparated()
        {
            var box = await BuildBoxAsync();
            var reference = new RRect(100, 200, 300, 400);

            var built = CssClipRectResolver.TryBuildClipRect("rect(10pt 250pt 300pt 40pt)", reference, box, out var rect);

            Assert.True(built);
            Assert.Equal(210, rect.Y, 3);
            Assert.Equal(140, rect.X, 3);
            Assert.Equal(350, rect.Right, 3);
            Assert.Equal(500, rect.Bottom, 3);
        }

        [Fact]
        public async Task Rect_AutoOnSomeEdges_ResolvesToTheBoxsOwnEdgeThere()
        {
            var box = await BuildBoxAsync();
            var reference = new RRect(100, 200, 300, 400);

            var built = CssClipRectResolver.TryBuildClipRect("rect(auto, 250pt, auto, 40pt)", reference, box, out var rect);

            Assert.True(built);
            Assert.Equal(reference.Y, rect.Y, 3);           // auto top -> the box's own top edge
            Assert.Equal(140, rect.X, 3);                   // 100 + 40 (left)
            Assert.Equal(350, rect.Right, 3);                // 100 + 250 (right)
            Assert.Equal(reference.Bottom, rect.Bottom, 3); // auto bottom -> the box's own bottom edge
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("banana")]
        [InlineData("")]
        [InlineData("rect(1pt, 2pt, 3pt)")]
        public async Task InvalidOrAuto_ReturnsFalse(string value)
        {
            var box = await BuildBoxAsync();

            var built = CssClipRectResolver.TryBuildClipRect(value, new RRect(0, 0, 100, 100), box, out var rect);

            Assert.False(built);
            Assert.Equal(default(RRect), rect);
        }
    }
}
