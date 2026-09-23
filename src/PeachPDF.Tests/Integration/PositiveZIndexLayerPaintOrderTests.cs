using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A box's positive <c>z-index</c> layers paint after everything else the box paints itself - its
    /// collapsed table borders and its list marker included - per CSS 2.1 Appendix E step 9. Asserted
    /// without any outline present, since the outline paint order is what moved these layers last and
    /// its own tests always have one.
    /// </summary>
    public class PositiveZIndexLayerPaintOrderTests
    {
        private static readonly RColor Blue = RColor.FromArgb(10, 20, 200);
        private static readonly RColor Green = RColor.FromArgb(10, 200, 20);

        [Fact]
        public async Task RaisedChild_PaintsOverItsTablesCollapsedBorders()
        {
            var g = await PaintAsync(
                "<table style='border-collapse: collapse; position: relative; z-index: 0; margin: 30pt'>" +
                "<tr><td style='border: 3pt solid rgb(10,200,20)'>a</td>" +
                "<td style='border: 3pt solid rgb(10,200,20)'>" +
                "<div style='position: relative; z-index: 1; margin: -6pt; background: rgb(10,20,200)'>raised</div></td></tr>" +
                "</table>");

            var lastBorder = g.Log.FindLastIndex(e => e is TestRecordingGraphics.DrawRectCall r && r.Color == Green
                                                      || e is TestRecordingGraphics.DrawPolygonCall p && p.Color == Green);
            var raised = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawRectCall r && r.Color == Blue);

            Assert.True(lastBorder >= 0, "no collapsed border was painted");
            Assert.True(raised > lastBorder, "a collapsed border was painted over a positive z-index child");
        }

        [Fact]
        public async Task RaisedChild_PaintsOverItsListItemsMarker()
        {
            var g = await PaintAsync(
                "<ol style='margin-left: 40pt'><li style='position: relative; z-index: 0'>" +
                "<div style='position: absolute; left: -30pt; top: 0; width: 30pt; z-index: 1; background: rgb(10,20,200)'>raised</div>" +
                "item</li></ol>");

            var marker = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawStringCall { Text: "1." });
            var raised = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawRectCall r && r.Color == Blue);

            Assert.True(marker >= 0, "no marker was painted");
            Assert.True(raised > marker, "the list marker was painted over a positive z-index child");
        }

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }
    }
}
