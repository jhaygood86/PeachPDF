using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Which <c>@keyframes</c> rule a name finds: the cascade layers order picks among rules of one name (CSS Animations 1 §3.1, CSS Cascade 5 §6.4.2),
    /// and a <c>&lt;string&gt;</c> names the same animation as the identifier spelled the same way. The width at the end of the run is the
    /// 100% keyframe of the rule that won.
    /// </summary>
    public class AnimationKeyframesLookupTests
    {
        private const double PointsPerPx = 0.75;

        private static async Task<double> WidthPx(string css, string animation = "k 1s linear")
        {
            var html = $"<!DOCTYPE html><html><head><style>{css} p {{ margin: 0; animation: {animation} }}</style></head><body><p>Ab</p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = 1.0 };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return DomUtils.GetBoxByTagName(container.Root!, "p")!.Size.Width / PointsPerPx;
        }

        [Theory]
        // An unlayered rule outranks a layered one, whichever comes later.
        [InlineData("@keyframes k { to { width: 100px } } @layer a { @keyframes k { to { width: 200px } } }", 100)]
        [InlineData("@layer a { @keyframes k { to { width: 200px } } } @keyframes k { to { width: 100px } }", 100)]
        // A later layer outranks an earlier one, whichever comes later in the source.
        [InlineData("@layer a, b; @layer b { @keyframes k { to { width: 200px } } } @layer a { @keyframes k { to { width: 100px } } }", 200)]
        [InlineData("@layer a { @keyframes k { to { width: 100px } } } @layer b { @keyframes k { to { width: 200px } } }", 200)]
        // A nested layer ranks above its parent's own rules, and an anonymous layer is a layer.
        [InlineData("@layer a { @keyframes k { to { width: 100px } } @layer n { @keyframes k { to { width: 200px } } } }", 200)]
        [InlineData("@layer { @keyframes k { to { width: 100px } } } @keyframes k { to { width: 300px } }", 300)]
        // The same layer, or none: the last one wins.
        [InlineData("@keyframes k { to { width: 100px } } @keyframes k { to { width: 200px } }", 200)]
        [InlineData("@layer a { @keyframes k { to { width: 100px } } } @layer a { @keyframes k { to { width: 200px } } }", 200)]
        // Grouping rules around the keyframes do not change the layer.
        [InlineData("@layer a { @media all { @keyframes k { to { width: 200px } } } } @keyframes k { to { width: 100px } }", 100)]
        [InlineData("@layer a { @supports (display: grid) { @keyframes k { to { width: 200px } } } } @keyframes k { to { width: 100px } }", 100)]
        public async Task TheCascadeLayersOrderPicksTheKeyframes(string css, double expected)
        {
            Assert.Equal(expected, await WidthPx(css), 2);
        }

        [Theory]
        [InlineData("@keyframes \"k\" { to { width: 120px } }", "k 1s linear")]
        [InlineData("@keyframes \"k\" { to { width: 120px } }", "\"k\" 1s linear")]
        [InlineData("@keyframes k { to { width: 120px } }", "\"k\" 1s linear")]
        [InlineData("@keyframes 'k' { to { width: 120px } }", "k 1s linear")]
        public async Task AStringNamesTheSameAnimationAsTheIdentifier(string css, string animation)
        {
            Assert.Equal(120, await WidthPx(css, animation), 2);
        }

        [Fact]
        public async Task AStringWithASpace_IsFoundByItsOwnString()
        {
            Assert.Equal(120, await WidthPx("@keyframes \"two words\" { to { width: 120px } }", "\"two words\" 1s linear"), 2);
        }
    }
}
