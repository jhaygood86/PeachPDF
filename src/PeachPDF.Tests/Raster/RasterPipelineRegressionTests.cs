using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Paint;
using PeachPDF.CSS;
using PeachDrawing;
using PeachPDF.Tests.TestSupport;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>Regressions for defects a review of the raster backend found: each pins the behaviour that was wrong.</summary>
    public class RasterPipelineRegressionTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        [Theory]
        [InlineData(false, new byte[] { 0, 0, 0, 255 })]
        [InlineData(true, new byte[] { 153, 102, 102, 255 })]
        public void ACmykBitmap_FollowsTheDocumentsBlackGenerationForNeutralPixels(bool richBlack, byte[] expectedBlack)
        {
            using var surface = new RasterSurface(2, 1, 0, 0, 1, 1);
            var p = surface.Pixels;
            // Black, then a chromatic red.
            p[3] = 255;
            p[4] = 255;
            p[7] = 255;

            var image = RasterEmbedding.ToXImage(surface, asCmyk: true, richBlack: richBlack);
            var cmyk = image.CmykRaster!.Value.Data;

            Assert.Equal(expectedBlack, cmyk[..4]);
            Assert.Equal(new byte[] { 0, 255, 255, 0 }, cmyk[4..8]);
        }

        [Fact]
        public void ColorFunctionsAreClampedBetweenStages_NotComposedIntoOneMatrix()
        {
            // brightness(2) sepia(1) on mid grey: brightness clips 0.6 * 2 to 1 first, so sepia sees white. Composing
            // the two matrices without the clip would see 1.2 and give a brighter red than white can produce.
            var surface = new RasterSurface(1, 1, 0, 0, 1, 1);
            surface.Pixels[0] = surface.Pixels[1] = surface.Pixels[2] = 153;
            surface.Pixels[3] = 255;

            var functions = new[]
            {
                FilterEffectResolver.BrightnessMatrix(2),
                FilterEffectResolver.TryGetMatrix(new FilterGrammar.FilterFunction { Name = "sepia", Arguments = ["1"] })!.Value,
            };
            ColorMatrixFilter.ApplyInPlace(surface, functions);

            var white = new byte[4];
            ColorMatrixFilter.Apply(new byte[] { 255, 255, 255, 255 }, white, functions[1]);
            Assert.Equal(white, surface.Pixels.Slice(0, 4).ToArray());
        }


        [Fact]
        public void EmptyColorFunctionList_LeavesThePixelsAlone()
        {
            var surface = new RasterSurface(1, 1, 0, 0, 1, 1);
            surface.Pixels[0] = 10;
            surface.Pixels[3] = 255;

            ColorMatrixFilter.ApplyInPlace(surface, Array.Empty<ColorMatrix>());

            Assert.Equal(10, surface.Pixels[0]);
        }


        [Theory]
        [InlineData("-0")]
        [InlineData("+0")]
        [InlineData("-0px")]
        [InlineData("0.0px")]
        public void SignedZeroBlurRadius_IsZeroLength(string radius)
        {
            var function = new FilterGrammar.FilterFunction { Name = "blur", Arguments = [radius] };

            Assert.True(FilterEffectResolver.IsZeroLength(function));
        }


        // ---- through the whole pipeline ---------------------------------------------------------------------

        private static PdfGenerateConfig Config() => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        };

        private static List<(int Width, int Height)> ImageSizes(string pdf)
        {
            var sizes = new List<(int, int)>();
            foreach (Match obj in Regex.Matches(pdf, @"(?:^|[\r\n])\d+ 0 obj(.*?)endobj", RegexOptions.Singleline))
            {
                var text = obj.Groups[1].Value;
                var streamStart = text.IndexOf("stream", StringComparison.Ordinal);
                var dictionary = streamStart >= 0 ? text[..streamStart] : text;
                if (!Regex.IsMatch(dictionary, @"/Subtype\s*/Image"))
                    continue;

                var width = Regex.Match(dictionary, @"/Width\s+(\d+)");
                var height = Regex.Match(dictionary, @"/Height\s+(\d+)");
                if (width.Success && height.Success)
                    sizes.Add((int.Parse(width.Groups[1].Value), int.Parse(height.Groups[1].Value)));
            }

            return sizes;
        }

        private static int Placements(string pdf) =>
            Regex.Matches(pdf, @"q\s+[-\d.]+\s+0\s+0\s+[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q").Count;

        [Theory]
        [InlineData("box-shadow: 0 0 30px 10px red;")]
        [InlineData("outline: 12px solid red;")]
        [InlineData("text-shadow: 0 0 20px red; font-size: 20px;")]
        public async Task DescendantEffects_ThatSpillPastTheFilteredElement_AreNotCropped(string childStyle)
        {
            string Html(string child) =>
                $"<html><body style=\"margin:0\"><div style=\"width:100px;height:60px;filter:blur(1px)\"><div style=\"width:40px;height:20px;margin:20px;{child}\">Hi</div></div></body></html>";

            var plain = ImageSizes(await PdfObjectReader.GeneratePdf(Html(""), Config()))[0];
            var spilled = ImageSizes(await PdfObjectReader.GeneratePdf(Html(childStyle), Config()))[0];

            Assert.True(spilled.Width > plain.Width || spilled.Height > plain.Height,
                $"expected a larger bitmap than {plain} for '{childStyle}', got {spilled}");
        }

        [Fact]
        public async Task OpacityInsideAFilteredElement_IsCompositedInTheBitmap_NotAsAnotherImage()
        {
            const string html = """
                <html><body style="margin:0"><div style="width:100px;height:60px;filter:blur(2px)">
                <div style="width:50px;height:30px;background:red;opacity:.5"></div></div></body></html>
                """;

            var pdf = await PdfObjectReader.GeneratePdf(html, Config());

            Assert.Equal(1, Placements(pdf));
        }

        [Fact]
        public async Task TextInAFilteredElement_StaysInThePdfAsInvisibleSelectableText()
        {
            const string html = """
                <html><body style="margin:0"><div style="font-size:20px;filter:blur(2px)">Findable words</div></body></html>
                """;

            var pdf = await PdfObjectReader.GeneratePdf(html, Config());

            Assert.Equal(1, Placements(pdf));
            Assert.Contains("3 Tr", pdf);
            Assert.Matches(@"T[jJ]", pdf);
            // The visible text is in the bitmap only: no fill-mode text object outside the invisible one.
            Assert.DoesNotContain("0 Tr", pdf);
        }

        [Fact]
        public async Task TextInAFilteredElement_IsNotDuplicatedInTheTaggedStructure()
        {
            var config = Config();
            config.EnableTaggedPdf = true;
            const string html = """
                <html lang="en"><body style="margin:0"><p style="font-size:20px;filter:blur(2px)">One paragraph</p></body></html>
                """;

            var pdf = await PdfObjectReader.GeneratePdf(html, config);

            Assert.Single(Regex.Matches(pdf, @"/S\s*/P\b"));
        }
    }
}
