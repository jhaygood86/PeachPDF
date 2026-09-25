using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Images 3 §3.4.3 step 2: a color stop positioned before an earlier stop takes that stop's position.
    /// The checkerboard idiom <c>#ddd 0% 25%, #fff 0% 50%</c> depends on it - its second stop's 0% is below the
    /// first's 25% - and without it the shading function's /Bounds decrease, which strict readers (MuPDF:
    /// "subfunction 1 boundary out of range") reject and lenient ones smear into a soft blend.
    /// </summary>
    public class GradientStopFixupTests
    {
        private static async Task<string> GetPdfText(string css)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200px; height: 100px; {css} }}</style></head><body><div></div></body></html>";
            var doc = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static double[][] AllBounds(string pdfText) =>
            Regex.Matches(pdfText, @"/Bounds\s*\[([^\]]*)\]")
                .Select(m => m.Groups[1].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray())
                .ToArray();

        private static void AssertNonDecreasing(double[][] boundsLists)
        {
            Assert.NotEmpty(boundsLists);
            foreach (var bounds in boundsLists)
                for (var i = 1; i < bounds.Length; i++)
                    Assert.True(bounds[i] >= bounds[i - 1], $"/Bounds decrease at index {i}: [{string.Join(' ', bounds)}]");
        }

        [Theory]
        [InlineData(new double[] { 0.25, 0.0 }, new double[] { 0.25, 0.25 })]
        [InlineData(new double[] { 0.0, 0.25, 0.0, 0.5 }, new double[] { 0.0, 0.25, 0.25, 0.5 })]
        [InlineData(new double[] { 0.5, 0.2, 0.1, 0.9 }, new double[] { 0.5, 0.5, 0.5, 0.9 })]
        [InlineData(new double[] { 0.0, 0.3, 0.6, 1.0 }, new double[] { 0.0, 0.3, 0.6, 1.0 })]
        public void ExplicitPositions_NeverDecrease(double[] input, double[] expected)
        {
            var positions = input.Select(v => (double?)v).ToArray();

            CssImagePainter.ClampStopPositionsToRunningMaximum(positions, 1.0);

            Assert.Equal(expected, positions.Select(p => p!.Value).ToArray());
        }

        [Fact]
        public void UnpositionedStops_AreLeftForSpacing()
        {
            var positions = new double?[] { 0.4, null, 0.1, null };

            CssImagePainter.ClampStopPositionsToRunningMaximum(positions, 1.0);

            Assert.Equal(new double?[] { 0.4, null, 0.4, null }, positions);
        }

        [Fact]
        public void UnpositionedLastStop_IsRaisedAbovePreviousStopsPastItsDefault()
        {
            var positions = new double?[] { 0.0, 1.5, null };

            CssImagePainter.ClampStopPositionsToRunningMaximum(positions, 1.0);

            Assert.Equal(1.5, positions[2]);
        }

        [Fact]
        public void SingleStop_IsUntouched()
        {
            var positions = new double?[] { null };

            CssImagePainter.ClampStopPositionsToRunningMaximum(positions, 1.0);

            Assert.Null(positions[0]);
        }

        [Fact]
        public async Task LinearHardStopPairWithLowerSecondPosition_HasNonDecreasingBounds()
        {
            var pdfText = await GetPdfText("background-image: linear-gradient(90deg, #888 0% 25%, #fff 0% 50%, #00f 50%);");

            AssertNonDecreasing(AllBounds(pdfText));
        }

        [Fact]
        public async Task RepeatingLinearCheckerStops_HaveNonDecreasingBoundsAndAHardEdge()
        {
            var pdfText = await GetPdfText("background-image: repeating-linear-gradient(90deg, #888 0% 25%, #fff 0% 50%);");

            var bounds = AllBounds(pdfText);
            AssertNonDecreasing(bounds);
            // The clamp turns the 0% into 25%, so the two colors meet at a coincident pair rather than a ramp.
            Assert.Contains(bounds, list => list.Zip(list.Skip(1)).Any(p => p.First == p.Second));
        }

        [Fact]
        public async Task RadialHardStopPairWithLowerSecondPosition_HasNonDecreasingBounds()
        {
            var pdfText = await GetPdfText("background-image: radial-gradient(#888 0% 25%, #fff 0% 50%, #00f 50%);");

            AssertNonDecreasing(AllBounds(pdfText));
        }

        [Fact]
        public void ConicCheckerStops_AreOrderedAndMeetAtAHardEdge()
        {
            var conic = new ParsedConicGradient
            {
                Stops =
                [
                    (RColor.FromArgb(0xDD, 0xDD, 0xDD), 0.0, false),
                    (RColor.FromArgb(0xDD, 0xDD, 0xDD), System.Math.PI / 2, false),
                    (RColor.FromArgb(0xFF, 0xFF, 0xFF), 0.0, false),
                    (RColor.FromArgb(0xFF, 0xFF, 0xFF), System.Math.PI, false),
                ],
                IsRepeating = false,
            };

            var (_, angles) = CssImagePainter.NormalizeConicStops(conic);

            for (var i = 1; i < angles.Length; i++)
                Assert.True(angles[i] >= angles[i - 1], $"conic angle decreases at index {i}");
            Assert.Equal(System.Math.PI / 2, angles[2], 9);
        }

        [Fact]
        public async Task RepeatingConicCheckerboard_RendersAShading()
        {
            var pdfText = await GetPdfText("background: repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%) 0 / 24px 24px;");

            Assert.Contains("/ShadingType", pdfText);
        }
    }
}
