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
    /// A custom property in <c>@keyframes</c>: discrete when unregistered, mixed by its syntax when registered (CSS Properties and Values API 1 §5).
    /// The numbers are what Chrome computes for <c>width: var(--w)</c> on a paused animation, in px; the box holds points.
    /// </summary>
    public class AnimationCustomPropertyTests
    {
        private const double PointsPerPx = 0.75;
        private const string Registration = "@property --w { syntax: '<length>'; inherits: true; initial-value: 0px }";

        private static async Task<CssBox> BoxAt(string css, double progress, string body = "<p id='p'>Ab</p>", string element = "p")
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return DomUtils.GetBoxByTagName(container.Root!, element)!;
        }

        private static string Rule(string animation = "k 1s linear") => $"p {{ margin: 0; width: var(--w); animation: {animation} }}";

        private const string Keyframes = "@keyframes k { from { --w: 100px } to { --w: 300px } }";

        [Theory]
        [InlineData(0.1, 100)]
        [InlineData(0.49, 100)]
        [InlineData(0.5, 300)]
        [InlineData(0.9, 300)]
        public async Task AnUnregisteredCustomProperty_FlipsAtTheMiddle(double progress, double pixels)
        {
            var box = await BoxAt($"{Keyframes} {Rule()}", progress);
            Assert.Equal(pixels * PointsPerPx, box.Size.Width, 2);
        }

        [Theory]
        [InlineData(0.1, 120)]
        [InlineData(0.5, 200)]
        [InlineData(0.9, 280)]
        public async Task ARegisteredLengthCustomProperty_IsMixed(double progress, double pixels)
        {
            var box = await BoxAt($"{Registration} {Keyframes} {Rule()}", progress);
            Assert.Equal(pixels * PointsPerPx, box.Size.Width, 2);
        }

        [Fact]
        public async Task ARegisteredCustomProperty_MissingFromAKeyframe_StartsFromItsInitialValue()
        {
            var box = await BoxAt($"{Registration} @keyframes k {{ to {{ --w: 300px }} }} {Rule()}", 0.5);
            Assert.Equal(150 * PointsPerPx, box.Size.Width, 2);
        }

        [Fact]
        public async Task ACustomPropertyMissingFromAKeyframe_StartsFromTheValueTheCascadeGaveIt()
        {
            var box = await BoxAt($"{Registration} @keyframes k {{ to {{ --w: 300px }} }} p {{ --w: 100px; margin: 0; width: var(--w); animation: k 1s linear }}", 0.5);
            Assert.Equal(200 * PointsPerPx, box.Size.Width, 2);
        }

        [Fact]
        public async Task AnAnimatedCustomProperty_IsInheritedByTheChildren()
        {
            var css = $"{Registration} {Keyframes} div {{ margin: 0; animation: k 1s linear }} p {{ margin: 0; width: var(--w) }}";
            var box = await BoxAt(css, 0.25, "<div><p>Ab</p></div>");
            Assert.Equal(150 * PointsPerPx, box.Size.Width, 2);
        }

        [Fact]
        public async Task AnImportantCustomProperty_OutranksTheAnimation()
        {
            var box = await BoxAt($"{Registration} {Keyframes} p {{ --w: 50px !important; margin: 0; width: var(--w); animation: k 1s linear }}", 0.5);
            Assert.Equal(50 * PointsPerPx, box.Size.Width, 2);
        }

        [Theory]
        [InlineData("@property --n { syntax: '<number>'; inherits: false; initial-value: 0 } @keyframes k { from { --n: 0 } to { --n: 10 } } p { margin: 0; width: calc(var(--n) * 1px); animation: k 1s linear }", 0.25, 2.5)]
        [InlineData("@property --n { syntax: '<integer>'; inherits: false; initial-value: 0 } @keyframes k { from { --n: 0 } to { --n: 10 } } p { margin: 0; width: calc(var(--n) * 1px); animation: k 1s linear }", 0.25, 3)]
        [InlineData("@property --n { syntax: '<length> | auto'; inherits: false; initial-value: auto } @keyframes k { from { --n: 100px } to { --n: auto } } p { margin: 0; width: 20px; animation: k 1s linear }", 0.25, 20)]
        [InlineData("@property --n { syntax: '*'; inherits: false } @keyframes k { from { --n: 10px } to { --n: 30px } } p { margin: 0; width: var(--n); animation: k 1s linear }", 0.25, 10)]
        [InlineData("@keyframes k { from { --n: inherit } to { --n: 30px } } div { --n: 10px; margin: 0 } p { margin: 0; width: var(--n); animation: k 1s linear }", 0.75, 30)]
        [InlineData("@keyframes k { from { --n: 5px } to { --n: 30px } } p { margin: 0; width: var(--n); --n: initial; animation: k 1s linear }", 0.75, 30)]
        public async Task TheSyntaxDecidesHowAValueIsMixed(string css, double progress, double pixels)
        {
            var box = await BoxAt(css, progress, "<div><p>Ab</p></div>");
            Assert.Equal(pixels * PointsPerPx, box.Size.Width, 2);
        }

        [Theory]
        [InlineData("initial", 0.25, 75)]
        [InlineData("unset", 0.25, 78.75)]
        [InlineData("revert", 0.25, 78.75)]
        [InlineData("revert-layer", 0.25, 78.75)]
        public async Task AGlobalKeywordInAKeyframe_ResolvesForACustomProperty(string keyword, double progress, double pixels)
        {
            var css = $"{Registration} @keyframes k {{ from {{ --w: {keyword} }} to {{ --w: 300px }} }} p {{ --w: 5px; margin: 0; width: var(--w); animation: k 1s linear }}";
            var box = await BoxAt(css, progress);
            Assert.Equal(pixels * PointsPerPx, box.Size.Width, 2);
        }
    }
}
