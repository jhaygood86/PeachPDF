using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Borders 4 <c>corner-shape</c> rendered end to end: the page's content stream is read back and a bevel, which is only
    /// straight cuts, must contain no curve operators where the same box with round corners has them.
    /// </summary>
    public partial class CornerShapeRenderingTests
    {
        private static async Task<string> ContentOf(string boxStyle, string bodyExtra = "")
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}</style></head><body>" +
                       $"<div style='width:120pt;height:80pt;{boxStyle}'>{bodyExtra}</div></body></html>";
            var config = new PdfGenerateConfig { ManualPageWidth = 300, ManualPageHeight = 200 };
            config.SetMargins(0);
            var document = await new PdfGenerator().GeneratePdf(html, config);
            return document.Page(0).ContentText;
        }

        [GeneratedRegex(@"\sc\s")]
        private static partial Regex CurveOperator();

        private static int Curves(string content) => CurveOperator().Matches(content).Count;

        [Fact]
        public async Task ABevelFillHasNoCurvesWhereARoundOneHasFour()
        {
            var round = await ContentOf("border-radius:20pt;background:#369;");
            var bevel = await ContentOf("border-radius:20pt;corner-shape:bevel;background:#369;");

            Assert.True(Curves(round) >= 4);
            Assert.Equal(0, Curves(bevel));
        }

        [Theory]
        [InlineData("corner-shape:bevel")]
        [InlineData("corner-shape:squircle scoop")]
        [InlineData("corner-shape:bevel scoop squircle notch")]
        [InlineData("corner-top-left-shape:square;corner-bottom-right-shape:superellipse(-2)")]
        [InlineData("corner-top-shape:notch;corner-bottom-shape:superellipse(0.5) scoop")]
        [InlineData("corner-left-shape:bevel;corner-right-shape:squircle")]
        [InlineData("corner-block-start-shape:bevel;corner-inline-end-shape:scoop")]
        [InlineData("corner-start-start-shape:notch;corner-end-end-shape:bevel;corner-start-end-shape:scoop;corner-end-start-shape:squircle")]
        [InlineData("corner:bevel 18pt")]
        [InlineData("border-radius:0;corner-top-left:bevel 20pt;corner-bottom-right:scoop 20pt")]
        [InlineData("border-radius:0;corner-top:scoop 14pt;corner-bottom:bevel 14pt")]
        [InlineData("border-radius:0;corner-block-start:bevel 14pt;corner-inline-end:notch 14pt")]
        [InlineData("border-radius:0;corner-start-start:bevel 20pt")]
        [InlineData("border-radius:0;border-top-radius:12pt 20pt;border-left-radius:9pt;corner-shape:bevel")]
        [InlineData("border-radius:0;border-block-end-radius:12pt 20pt / 8pt;border-inline-start-radius:9pt;corner-shape:scoop")]
        [InlineData("border-radius:0;border-start-end-radius:20pt;border-end-start-radius:10pt 20pt;corner-shape:squircle")]
        [InlineData("direction:rtl;border-radius:0;border-start-end-radius:20pt;corner-start-end-shape:bevel")]
        [InlineData("writing-mode:vertical-rl;border-radius:0;border-end-start-radius:20pt;corner-end-start-shape:scoop")]
        public async Task EveryShapeAndShorthandFormChangesWhatIsPainted(string shapeStyle)
        {
            var plain = await ContentOf("border-radius:20pt;background:#369;");
            var shaped = await ContentOf("border-radius:20pt;background:#369;" + shapeStyle);

            Assert.NotEmpty(shaped);
            Assert.NotEqual(plain, shaped);
        }

        [Theory]
        [InlineData("solid")]
        [InlineData("dashed")]
        [InlineData("dotted")]
        [InlineData("double")]
        [InlineData("groove")]
        [InlineData("ridge")]
        [InlineData("inset")]
        [InlineData("outset")]
        public async Task EveryBorderStyleIsDrawnAlongAShapedBox(string style)
        {
            var content = await ContentOf(
                $"border:7pt {style} #336699;border-top-color:#993333;border-radius:22pt;corner-shape:bevel scoop notch squircle;");

            Assert.NotEmpty(content);
        }

        [Fact]
        public async Task ABeveledSolidBorderIsStraightCutsOnly()
        {
            var content = await ContentOf("border:6pt solid #c0392b;border-radius:22pt;corner-shape:bevel;");

            Assert.Equal(0, Curves(content));
        }

        [Fact]
        public async Task BordersWithMissingSidesAndUnevenWidthsAreDrawn()
        {
            var content = await ContentOf("border:solid #369;border-width:4pt 0 10pt 6pt;border-radius:20pt;corner-shape:bevel scoop;");

            Assert.NotEmpty(content);
        }

        [Fact]
        public async Task ASquareBoxWithOneShapedCornerPaintsItsBorder()
        {
            var content = await ContentOf("border:5pt solid black;border-top-left-radius:20pt;corner-top-left-shape:notch;");

            Assert.NotEmpty(content);
        }

        [Theory]
        [InlineData("outline:4pt solid orange;outline-offset:2pt;")]
        [InlineData("outline:4pt dashed orange;outline-offset:2pt;")]
        [InlineData("box-shadow:5pt 5pt 6pt #000a;background:white;")]
        [InlineData("box-shadow:inset 3pt 3pt 6pt #000a;background:white;")]
        [InlineData("backdrop-filter:blur(2pt);background:#fff8;")]
        [InlineData("clip-path:border-box;background:red;")]
        [InlineData("clip-path:inset(4pt round 10pt);background:red;")]
        [InlineData("background:linear-gradient(red,blue);background-clip:padding-box;border:6pt solid green;")]
        public async Task EveryPaintedFeatureFollowsTheShape(string feature)
        {
            var plain = await ContentOf("border-radius:20pt;" + feature);
            var shaped = await ContentOf("border-radius:20pt;corner-shape:bevel;" + feature);

            Assert.NotEmpty(shaped);
            Assert.NotEqual(plain, shaped);
        }

        [Fact]
        public async Task OverflowHiddenClipsDescendantsToTheShape()
        {
            var plain = await ContentOf("border-radius:20pt;overflow:hidden;", "<div style='height:200pt;background:red'></div>");
            var shaped = await ContentOf("border-radius:20pt;corner-shape:bevel;overflow:hidden;", "<div style='height:200pt;background:red'></div>");

            Assert.NotEqual(plain, shaped);
            Assert.Equal(0, Curves(shaped));
        }

        [Fact]
        public async Task ABorderImageIsClippedToTheShapedCorner()
        {
            const string pixel = "data:image/gif;base64,R0lGODlhAQABAIAAAAUEBAAAACwAAAAAAQABAAACAkQBADs=";
            var content = await ContentOf(
                $"border:8pt solid transparent;border-image:url({pixel}) 1;border-radius:20pt;corner-shape:bevel;");

            Assert.NotEmpty(content);
        }

        [Fact]
        public async Task AFlowRelativeCornerLosesToALaterPhysicalOne()
        {
            var logicalFirst = await ContentOf("border-start-start-radius:20pt;border-top-left-radius:0;corner-start-start-shape:bevel;background:red;");
            var physicalFirst = await ContentOf("border-top-left-radius:0;border-start-start-radius:20pt;corner-start-start-shape:bevel;background:red;");

            Assert.NotEqual(logicalFirst, physicalFirst);
        }

        [Fact]
        public async Task InvalidShapesAreIgnored()
        {
            var plain = await ContentOf("border-radius:20pt;background:red;");
            var invalid = await ContentOf("border-radius:20pt;background:red;corner-shape:wobbly;corner-top-left-shape:superellipse();corner:bevel bevel bevel bevel bevel;");

            Assert.Equal(plain, invalid);
        }
    }
}
