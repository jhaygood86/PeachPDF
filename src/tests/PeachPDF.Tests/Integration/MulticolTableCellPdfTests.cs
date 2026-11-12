using System.Globalization;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell that is itself a multi-column container paints its content in columns. Resolved against the row it sits in, the cell's width was
    /// nothing, every column was zero wide and the cell painted none of its children. Goes through <see cref="PdfGenerator"/>, which is what the
    /// coverage gate measures the engine through; the layout is asserted box by box in <c>PeachBrackets.Engine.Tests</c> (<c>MulticolTableCellTests</c>).
    /// </summary>
    public class MulticolTableCellPdfTests
    {
        [Fact]
        public async Task ABlockChildrenCell_PaintsItsBlocksSideBySide()
        {
            var html = "<!DOCTYPE html><html><body style='margin:0'><table style='border-spacing:0;width:300pt'><tr>" +
                       "<td style='padding:0;columns:2;column-gap:10pt'>" +
                       "<div style='width:100pt;height:10pt;background:#f00'></div><div style='width:100pt;height:10pt;background:#00f'></div>" +
                       "</td></tr></table></body></html>";

            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);
            var file = result.Inspect();
            var content = file.ContentText(file.Pages[0]);

            double X(string rgb)
            {
                var match = Regex.Match(content, rgb + @" rg\s+(?:/\S+ gs\s+)?(\S+) \S+ (\S+) \S+ re\s+f", RegexOptions.CultureInvariant);
                Assert.True(match.Success, $"no fill for {rgb}");
                return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            Assert.True(X("0 0 1") - X("1 0 0") > 100);
        }
    }
}
