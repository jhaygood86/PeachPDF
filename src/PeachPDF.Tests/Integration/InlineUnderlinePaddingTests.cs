using PeachPDF.Html.Core;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-text-decor-3 §2.5: an underline is placed from the text's own baseline and font metrics, not from
    /// the decorating box's border or padding edge, so padding and border around an underlined inline box
    /// change where its background ends but not where the underline sits.
    /// </summary>
    public class InlineUnderlinePaddingTests
    {
        private static string Document(string spanStyle, string line = "underline") =>
            "<!DOCTYPE html><html><body style='font:20pt Arial;margin:20pt'><p><span style='text-decoration:" + line + ";background:#fdd;"
            + spanStyle + "'>text</span></p></body></html>";

        private static async Task<double> UnderlineYAsync(string spanStyle, string line = "underline")
        {
            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Document(spanStyle, line), new PdfGenerateConfig { PageSize = PageSize.Letter });

            var recording = new RecordingGraphics(new PeachPDF.Adapters.PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recording, 0);

            // The decoration is the only horizontal hairline on the page: the background is a fill.
            var lines = recording.Log.Where(op => op.Kind == PaintOpKind.Line && op.Bounds.Width > 0 && op.Bounds.Height < 0.01).ToList();
            return Assert.Single(lines).Bounds.Top;
        }

        [Theory]
        [InlineData("padding-bottom:12pt")]
        [InlineData("padding-top:12pt")]
        [InlineData("padding:10pt 0")]
        [InlineData("padding:10pt 4pt")]
        [InlineData("border-top:3pt solid;border-bottom:3pt solid")]
        [InlineData("padding:6pt;border:2pt solid")]
        public async Task AnUnderlinedInlineBox_KeepsItsUnderlineWhereTheUnpaddedOneIs(string spanStyle)
        {
            var plain = await UnderlineYAsync("");
            var padded = await UnderlineYAsync(spanStyle);

            Assert.Equal(plain, padded, 0.05);
        }

        [Theory]
        [InlineData("overline", "padding-bottom:12pt")]
        [InlineData("overline", "padding:10pt 0")]
        [InlineData("line-through", "padding-bottom:12pt")]
        [InlineData("line-through", "padding:10pt 0")]
        public async Task OtherDecorationLines_AreNotMovedByPaddingEither(string line, string spanStyle)
        {
            var plain = await UnderlineYAsync("", line);
            var padded = await UnderlineYAsync(spanStyle, line);

            Assert.Equal(plain, padded, 0.05);
        }

        [Fact]
        public async Task TheUnderlinePositionUnder_StillHangsFromTheBoxsOwnBottomEdge()
        {
            // `under` is defined by the line's descender edge, which a box's own padding does not move.
            var plain = await UnderlineYAsync("text-underline-position:under");
            var padded = await UnderlineYAsync("text-underline-position:under;padding-bottom:12pt");

            Assert.Equal(plain, padded, 0.05);
        }
    }
}
