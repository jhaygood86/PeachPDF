using PeachPDF;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// The <c>background</c> shorthand with several layers expands into one value per layer for each layered longhand. A layer whose text leaves a longhand
    /// out takes that longhand's initial value (CSS Backgrounds 3 section 2.10). It used to leave a hole in the comma list instead, which no longhand accepts,
    /// so <c>background: linear-gradient(...), rgba(...)</c> (the last layer is only a colour: it names no image) lost its gradient entirely, and
    /// <c>url(a) no-repeat, url(b)</c> lost its <c>no-repeat</c>.
    /// </summary>
    public class BackgroundLayerShorthandTests
    {
        private static string[] Expand(string declaration, params string[] longhands)
        {
            var sheet = StylesheetParser.Default.Parse("#a { " + declaration + "; }");
            var style = sheet.Rules.OfType<IStyleRule>().Single().Style;

            return longhands.Select(name => style.GetPropertyValue(name)).ToArray();
        }

        [Theory]
        [InlineData("url(a.png), red", "url(\"a.png\"), none", "rgb(255, 0, 0)")]
        [InlineData("url(a.png) no-repeat, red", "url(\"a.png\"), none", "rgb(255, 0, 0)")]
        [InlineData("linear-gradient(red, blue), rgba(255, 0, 0, .5)", "linear-gradient(rgb(255, 0, 0), rgb(0, 0, 255)), none", "rgba(255, 0, 0, 0.5)")]
        [InlineData("url(a.png), linear-gradient(red, blue), #fff", "url(\"a.png\"), linear-gradient(rgb(255, 0, 0), rgb(0, 0, 255)), none", "rgb(255, 255, 255)")]
        [InlineData("none, red", "none, none", "rgb(255, 0, 0)")]
        public void AColourOnlyLastLayer_KeepsTheImagesOfTheLayersBeforeIt(string declaration, string image, string color) =>
            Assert.Equal([image, color], Expand("background: " + declaration, "background-image", "background-color"));

        [Theory]
        [InlineData("url(a.png) no-repeat, url(b.png)", "no-repeat, repeat")]
        [InlineData("url(a.png), url(b.png) no-repeat", "repeat, no-repeat")]
        [InlineData("url(a.png) no-repeat, url(b.png) repeat-x, url(c.png)", "no-repeat, repeat-x, repeat")]
        [InlineData("url(a.png) no-repeat, red", "no-repeat, repeat")]
        public void ALayerThatOmitsALonghand_TakesItsInitialValue(string declaration, string repeat) =>
            Assert.Equal([repeat], Expand("background: " + declaration, "background-repeat"));

        [Theory]
        [InlineData("url(a.png) center / cover, url(b.png)", "background-size", "cover, auto")]
        [InlineData("url(a.png) fixed, url(b.png)", "background-attachment", "fixed, scroll")]
        [InlineData("url(a.png), url(b.png) local", "background-attachment", "scroll, local")]
        [InlineData("url(a.png) content-box padding-box, url(b.png)", "background-origin", "content-box, padding-box")]
        [InlineData("url(a.png) content-box padding-box, url(b.png)", "background-clip", "padding-box, border-box")]
        [InlineData("url(a.png), url(b.png) content-box padding-box", "background-origin", "padding-box, content-box")]
        [InlineData("url(a.png), url(b.png) content-box padding-box", "background-clip", "border-box, padding-box")]
        public void EveryLayeredLonghand_FallsBackToItsOwnInitialValueInAnOmittingLayer(string declaration, string longhand, string expected) =>
            Assert.Equal([expected], Expand("background: " + declaration, longhand));

        [Fact]
        public void APositionOmittedInOneLayer_IsTheInitialPositionThere() =>
            Assert.Equal(["center, 0 0"], Expand("background: url(a.png) center, url(b.png)", "background-position"));

        [Theory]
        [InlineData("url(a.png), url(b.png)")]
        [InlineData("red")]
        [InlineData("url(a.png) red")]
        public void ALonghandNoLayerNames_StaysInitial(string declaration) =>
            Assert.Equal(["initial", "initial", "initial"], Expand("background: " + declaration, "background-size", "background-clip", "background-attachment"));

        [Theory]
        [InlineData("url(a.png),")]
        [InlineData(", url(a.png)")]
        [InlineData("url(a.png), , url(b.png)")]
        public void AnEmptyLayer_MakesTheDeclarationInvalid(string declaration) =>
            Assert.Equal(["", ""], Expand("background: " + declaration, "background-image", "background-color"));

        [Fact]
        public void TheColour_IsOnlyTakenFromTheLastLayer() =>
            Assert.Equal(["rgb(0, 128, 0)"], Expand("background: url(a.png) no-repeat, url(b.png) green", "background-color"));

        [Theory]
        [InlineData("background: url(a.png) no-repeat, url(b.png) fixed", "scroll, fixed")]
        [InlineData("background: url(a.png) fixed, red", "fixed, scroll")]
        [InlineData("background: url(a.png), url(b.png) local, #eee", "scroll, local, scroll")]
        [InlineData("background-attachment: fixed, scroll", "fixed, scroll")]
        public async Task APerLayerAttachmentList_ReachesTheBox(string declaration, string attachment)
        {
            // The layered shorthand exports background-attachment as one entry per layer; the property registry used to take a single keyword only,
            // so `url(a) no-repeat, url(b) fixed` (which exported the lone `fixed` before layers that omit it took their initial value) lost its `fixed`.
            var html = $"<!DOCTYPE html><html><head><style>div {{ {declaration} }}</style></head><body><div>x</div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var box = DomUtils.GetBoxByTagName(container.Root!, "div");
            Assert.NotNull(box);
            Assert.Equal(attachment, box.BackgroundAttachment);
        }

        [Fact]
        public async Task AGradientLayerAboveAColour_IsPainted()
        {
            // The whole point of the expansion: the gradient is a real shading in the PDF, not dropped. With only the colour there is none.
            static async Task<string> Pdf(string background, string extraStyle = "")
            {
                var property = extraStyle.Length == 0 ? "background" : "background-image";
                var html = $"<!DOCTYPE html><html><body><div style=\"width:200px;height:100px;{property}:{background};{extraStyle}\"></div></body></html>";
                var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
                var ms = new System.IO.MemoryStream();
                doc.Save(ms);
                return System.Text.Encoding.Latin1.GetString(ms.ToArray());
            }

            var shorthand = await Pdf("linear-gradient(to right, #00f, #0f0), rgba(255, 0, 0, .5)");
            Assert.Contains("/ShadingType", shorthand);
            Assert.DoesNotContain("/ShadingType", await Pdf("rgba(255, 0, 0, .5)"));

            // And it is the same page as the longhands it stands for.
            static string[] Streams(string pdf) => System.Text.RegularExpressions.Regex.Matches(pdf, @"stream(.*?)endstream", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(match => match.Groups[1].Value).ToArray();

            var longhands = await Pdf("linear-gradient(to right, #00f, #0f0)", "background-color: rgba(255, 0, 0, .5)");
            Assert.Equal(Streams(longhands), Streams(shorthand));
        }

        [Theory]
        [InlineData("top 10px left")]
        [InlineData("bottom left 20px")]
        [InlineData("top 10px left 20px")]
        [InlineData("left 10px top")]
        [InlineData("right bottom 20px")]
        public void AThreeOrFourValuePosition_IsAcceptedInEitherGroupOrder(string position)
        {
            Assert.Equal([position], Expand("background-position: " + position, "background-position"));
            Assert.Equal([position], Expand("background: url(a.png) " + position, "background-position"));
        }

        [Theory]
        [InlineData("top left left")]
        [InlineData("top top 10px")]
        [InlineData("center 10px left")]
        [InlineData("top 10px center 20px")]
        [InlineData("left 10px left")]
        public void AnInvalidThreeOrFourValuePosition_IsStillDropped(string position) =>
            Assert.Equal([""], Expand("background-position: " + position, "background-position"));
    }
}
