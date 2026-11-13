using PeachDrawing.PDF.Engine;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A flex column or a grid taller than its content only because of <c>min-height</c> gives the free space to its items, so the
    /// last one sits at the foot of the container rather than under the first.
    /// </summary>
    public class MinHeightFreeSpacePdfTests
    {
        [Theory]
        [InlineData("display:flex; flex-direction:column; min-height:200mm;", "margin-top:auto")]
        [InlineData("display:flex; flex-direction:column; min-height:200mm; justify-content:space-between;", "")]
        [InlineData("display:grid; grid-template-rows: auto 1fr auto; min-height:200mm;", "")]
        [InlineData("display:grid; grid-template-rows: auto minmax(0, 1fr) auto; min-height:200mm;", "")]
        public async Task TheLastItemSitsFarBelowTheFirst(string container, string last)
        {
            var html = "<!DOCTYPE html><html><head><style>@page { size: A4; margin: 10mm } body { margin:0; font: 10pt sans-serif }</style></head><body>" +
                       $"<div style='{container}'><div>TOP</div><div></div><div style='{last}'>BOTTOM</div></div><div>AFTER</div></body></html>";

            var doc = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            using var ms = new MemoryStream();
            doc.Save(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());

            // Every Td moves relative to the last, so accumulate them into absolute baselines and keep each distinct line once.
            double y = 0;
            List<double> ys = [];
            foreach (Match m in Regex.Matches(text, @"(-?[\d.]+) (-?[\d.]+) Td"))
            {
                y += double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (ys.Count == 0 || Math.Abs(ys[^1] - y) > 0.5) ys.Add(y);
            }

            Assert.Equal(3, ys.Count);
            // Baselines are bottom-up points: TOP, BOTTOM and AFTER. 200mm is about 567pt, so BOTTOM is more than 500pt under TOP.
            Assert.True(ys[0] - ys[1] > 500, $"TOP {ys[0]}, BOTTOM {ys[1]}");
            Assert.True(ys[1] - ys[2] < 30, $"BOTTOM {ys[1]}, AFTER {ys[2]}");
        }
    }
}
