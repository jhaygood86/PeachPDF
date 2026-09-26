using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Whether a float moved whole onto the next page (<c>CssBox.MoveWholeOntoTheNextPageIfItFits</c>)
    /// lands in the same place when the same tree is laid out again, as the measure-then-final passes of
    /// <c>PdfGenerator</c> and a <c>target-counter</c> document's repeated layouts do.
    /// </summary>
    public class MovedFloatRelayoutIdempotencyTests
    {
        private const string Style =
            "<style>@page{size:300pt 200pt;margin:20pt} body{margin:0;font:10pt/12pt Arial} p{margin:0}</style>";

        private static string Lines(string prefix, int count, string separator = "<br>") =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"{prefix}{i}{separator}"));

        private static string Paragraphs(string prefix, int count) =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"<p>{prefix}{i}</p>"));

        // Every box - anonymous ones included, which is where the drift showed - and every word, keyed by
        // the box's position in the tree.
        private static string GeometryOf(CssBox root, HtmlContainerInt container)
        {
            var text = new StringBuilder();
            var index = 0;

            foreach (var box in LayoutHarness.Descendants(root))
            {
                text.AppendFormat(CultureInfo.InvariantCulture, "#{0}@({1:F2},{2:F2})-({3:F2},{4:F2})",
                    index++, box.Location.X, box.Location.Y, box.ActualRight, box.ActualBottom);

                foreach (var word in box.Words)
                {
                    text.AppendFormat(CultureInfo.InvariantCulture, " {0}({1:F2},{2:F2})", word.Text, word.Left, word.Top);
                }

                text.Append('\n');
            }

            text.AppendFormat(CultureInfo.InvariantCulture, "size={0:F2}", container.ActualSize.Height);
            return text.ToString();
        }

        [Theory]
        // A plain float straddling the foot of page one, moved whole onto page two.
        [InlineData("MovedFloat")]
        // A wrapper beside a floated sibling that is moved whole.
        [InlineData("WrapperBesideMovedFloat")]
        public async Task AFloatMovedWhole_LaidOutAgain_ReproducesAFreshLayout(string shape)
        {
            var body = shape switch
            {
                "MovedFloat" => Paragraphs("P", 9)
                                + $"<div style='float:left;width:100pt'>{Lines("F", 6)}</div>"
                                + Paragraphs("Q", 20),
                _ => "<p>x</p>"
                     + $"<div style='overflow:hidden'>{Lines("A", 9)}</div>"
                     + $"<div style='float:right;width:80pt'>{Lines("F", 5)}</div>"
                     + $"<div style='overflow:hidden'>{Lines("B", 4)}</div>",
            };

            var html = $"<!DOCTYPE html><html><head>{Style}</head><body>{body}</body></html>";

            var fresh = (await LayoutHarness.LayoutRepeatedlyAsync(html, 1, GeometryOf, 300, 200))[0];
            var repeated = await LayoutHarness.LayoutRepeatedlyAsync(html, 4, GeometryOf, 300, 200);

            Assert.All(repeated, pass => Assert.Equal(fresh, pass));
        }
    }
}
