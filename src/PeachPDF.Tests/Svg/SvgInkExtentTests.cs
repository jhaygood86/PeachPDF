using PeachPDF.Adapters;
using PeachDrawing.Abstractions;
using PeachPDF.Svg;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>The conservative box of what a document draws: shapes, their strokes, transforms, <c>use</c>, nested viewports and images.</summary>
    public class SvgInkExtentTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private const string Svg = "xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

        private static Rect? Extent(string body)
        {
            var root = XDocument.Parse($"<svg {Svg} viewBox=\"0 0 100 100\">{body}</svg>").Root!;
            return SvgInkExtent.Of(SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), Adapter));
        }

        private static void AssertBox(Rect? actual, double x, double y, double width, double height)
        {
            var box = Assert.NotNull(actual);
            Assert.Equal(x, box.X, 4);
            Assert.Equal(y, box.Y, 4);
            Assert.Equal(width, box.Width, 4);
            Assert.Equal(height, box.Height, 4);
        }

        [Fact]
        public void ADocumentThatDrawsNothing_HasNoExtent() => Assert.Null(Extent(""));

        [Fact]
        public void AShape_IsItsGeometry()
        {
            AssertBox(Extent("<rect x=\"10\" y=\"20\" width=\"30\" height=\"40\" fill=\"red\"/>"), 10, 20, 30, 40);
            AssertBox(Extent("<circle cx=\"50\" cy=\"50\" r=\"10\"/>"), 40, 40, 20, 20);
        }

        [Fact]
        public void TwoShapes_AreTheirUnion()
        {
            AssertBox(Extent("<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/><rect x=\"90\" y=\"-40\" width=\"10\" height=\"10\"/>"), 0, -40, 100, 50);
        }

        [Fact]
        public void AStroke_GrowsTheBoxByItsReach()
        {
            // a round join reaches half the width; a mitered one the half width times the miter limit (4 by default)
            AssertBox(Extent("<rect x=\"10\" y=\"10\" width=\"10\" height=\"10\" stroke=\"#000\" stroke-width=\"4\" stroke-linejoin=\"round\"/>"), 8, 8, 14, 14);
            AssertBox(Extent("<rect x=\"10\" y=\"10\" width=\"10\" height=\"10\" stroke=\"#000\" stroke-width=\"4\"/>"), 2, 2, 26, 26);
            AssertBox(Extent("<rect x=\"10\" y=\"10\" width=\"10\" height=\"10\" stroke=\"none\" stroke-width=\"40\"/>"), 10, 10, 10, 10);
        }

        [Fact]
        public void ASquareCap_ReachesItsDiagonal()
        {
            var box = Extent("<line x1=\"0\" y1=\"0\" x2=\"10\" y2=\"0\" stroke=\"#000\" stroke-width=\"10\" stroke-linecap=\"square\" stroke-linejoin=\"round\"/>");

            Assert.Equal(-5 * System.Math.Sqrt(2), box!.Value.X, 6);
        }

        [Fact]
        public void ATransform_MapsTheBox()
        {
            AssertBox(Extent("<g transform=\"translate(100 -50)\"><rect width=\"10\" height=\"10\"/></g>"), 100, -50, 10, 10);
            AssertBox(Extent("<rect width=\"10\" height=\"20\" transform=\"scale(2 3)\"/>"), 0, 0, 20, 60);
            // a quarter turn about the origin: (x, y) -> (-y, x)
            AssertBox(Extent("<rect x=\"10\" y=\"0\" width=\"10\" height=\"5\" transform=\"rotate(90)\"/>"), -5, 10, 5, 10);
        }

        [Fact]
        public void AUse_IsItsTargetMovedByItsOffset()
        {
            AssertBox(Extent("<defs><rect id=\"r\" width=\"10\" height=\"10\"/></defs><use xlink:href=\"#r\" x=\"500\" y=\"-300\"/>"), 500, -300, 10, 10);
        }

        [Fact]
        public void AUseWithATransform_IsMappedThroughIt()
        {
            AssertBox(Extent("<defs><rect id=\"r\" width=\"10\" height=\"10\"/></defs><use xlink:href=\"#r\" x=\"5\" transform=\"translate(0 50)\"/>"), 5, 50, 10, 10);
        }

        [Fact]
        public void ASymbolThroughAUse_IsTheUsesViewportWhenItHasOne_AndNothingWhenItDoesNot()
        {
            const string symbol = "<defs><symbol id=\"s\"><rect width=\"1000\" height=\"1000\"/></symbol></defs>";

            AssertBox(Extent(symbol + "<use xlink:href=\"#s\" x=\"10\" y=\"20\" width=\"30\" height=\"40\"/>"), 10, 20, 30, 40);
            Assert.Null(Extent(symbol + "<use xlink:href=\"#s\" x=\"10\" y=\"20\"/>"));
        }

        [Fact]
        public void ANestedSvg_IsClippedToItsViewport()
        {
            AssertBox(Extent("<svg x=\"10\" y=\"10\" width=\"20\" height=\"20\"><rect width=\"5000\" height=\"5000\"/></svg>"), 10, 10, 20, 20);
        }

        [Fact]
        public void AnImage_IsItsRectangle()
        {
            AssertBox(Extent("<image x=\"-30\" y=\"5\" width=\"60\" height=\"10\" href=\"data:image/png;base64,iVBORw0KGgo=\"/>"), -30, 5, 60, 10);
        }

        [Fact]
        public void Text_ContributesNothing()
        {
            Assert.Null(Extent("<text x=\"0\" y=\"0\">Hello</text>"));
        }

        [Fact]
        public void AnArc_IsHeldByTheEllipseItBulgesInto()
        {
            // a half circle of radius 5000 from (0, 0) to (100, 0): its endpoints alone are a 100 x 0 line, but it bulges thousands of units
            var box = Extent("<path d=\"M0 0 A5000 5000 0 0 1 100 0\" fill=\"none\"/>")!.Value;

            Assert.True(box.Y <= -5000, $"top {box.Y}");
            Assert.True(box.Y + box.Height >= 5000 - 1, $"bottom {box.Y + box.Height}");
        }

        [Fact]
        public void AnArcTooSmallToSpanItsChord_IsHeldByTheRadiiItGrowsTo()
        {
            // radii of 1 cannot span 1000 units: they grow to 500, so the arc reaches about 500 to a side
            var box = Extent("<path d=\"M0 0 A1 1 0 0 1 1000 0\" fill=\"none\"/>")!.Value;

            Assert.True(box.Y <= -500, $"top {box.Y}");
            Assert.True(box.Y + box.Height >= 499, $"bottom {box.Y + box.Height}");
        }

        [Fact]
        public void AFlatArc_AddsNothing()
        {
            AssertBox(Extent("<path d=\"M10 10 A0 5 0 0 1 60 10\" fill=\"none\"/>"), 10, 10, 50, 0);
        }

        [Fact]
        public void AGroupTransformAndAUseOffset_Compose()
        {
            AssertBox(Extent("<defs><rect id=\"r\" width=\"10\" height=\"10\"/></defs><g transform=\"scale(2)\"><use xlink:href=\"#r\" x=\"5\" y=\"7\"/></g>"), 10, 14, 20, 20);
        }

        [Fact]
        public void ATransformThatOverflows_IsIgnored()
        {
            AssertBox(Extent("<rect width=\"10\" height=\"10\"/><g transform=\"scale(1e200)\"><rect x=\"-1e200\" width=\"2e200\" height=\"1\"/></g>"), 0, 0, 10, 10);
        }

        [Fact]
        public void AShapeWithNonFiniteGeometry_IsIgnored()
        {
            AssertBox(Extent("<rect width=\"10\" height=\"10\"/><rect width=\"1e999\" height=\"5\"/>"), 0, 0, 10, 10);
        }
    }
}
