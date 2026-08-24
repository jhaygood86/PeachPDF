using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class RenderUtilsTests
    {
        [Fact]
        public void GetRoundRect_ClosesTheReturnedPath()
        {
            var g = new RecordingGraphics(new PdfSharpAdapter());

            using var path = (RecordingGraphicsPath)RenderUtils.GetRoundRect(g, new RRect(0, 0, 100, 40),
                10, 10, 10, 10, 10, 10, 10, 10);

            Assert.True(path.Closed);
        }
    }
}
