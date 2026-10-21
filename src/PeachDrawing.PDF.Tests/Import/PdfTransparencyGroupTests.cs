using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>A form that is a transparency group, drawn while a constant alpha is in force, is composited as a whole at that alpha.</summary>
    public class PdfTransparencyGroupTests
    {
        private const string TwoSquares = "1 0 0 rg 0 0 60 60 re f 0 0 1 rg 30 30 60 60 re f";

        private static byte[] Page(string formEntries, string content = "/G1 gs /Fm Do") =>
            TestPdf.Build(content, resources: "/XObject << /Fm 5 0 R >> /ExtGState << /G1 << /ca 0.5 >> >>",
                extraObjects: [TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 100 100] " + formEntries, TwoSquares)]);

        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int x, int yPdf, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var buffer = canvas.ToPixelBuffer();
            var i = ((99 - yPdf) * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            var a = span[i + 3];                                   // premultiplied: undo it so colours read as drawn
            return a == 0 ? (span[i], span[i + 1], span[i + 2], a) : ((byte)Math.Min(255, span[i] * 255 / a), (byte)Math.Min(255, span[i + 1] * 255 / a), (byte)Math.Min(255, span[i + 2] * 255 / a), a);
        }

        [Fact]
        public void TransparencyGroup_IsCompositedAtTheAlphaAsAWhole()
        {
            var pdf = Page("/Group << /S /Transparency /CS /DeviceRGB /I true >>");

            // Half-transparent red where only red was drawn, half-transparent blue where blue covers red: the red does not show through the blue.
            var red = At(pdf, 10, 10, out var unsupported);
            var overlap = At(pdf, 45, 45, out _);
            Assert.Empty(unsupported);
            Assert.InRange(red.A, 110, 145);
            Assert.True(red.R > 200 && red.B < 60, $"red at half alpha, was {red}");
            Assert.InRange(overlap.A, 110, 145);
            Assert.True(overlap.B > 200 && overlap.R < 60, $"blue alone at half alpha in the overlap, was {overlap}");
        }

        [Fact]
        public void FormWithoutAGroup_KeepsTheAlphaOfEachShape()
        {
            var pdf = Page("");

            // Each square is painted at half alpha on its own, so blue over red leaves some red in the overlap.
            var overlap = At(pdf, 45, 45, out _);
            Assert.True(overlap.R > 40, $"red still shows through the blue, was {overlap}");
        }

        [Fact]
        public void GroupWithoutAlpha_IsDrawnAsBefore()
        {
            var pdf = Page("/Group << /S /Transparency >>", content: "/Fm Do");

            var overlap = At(pdf, 45, 45, out var unsupported);
            Assert.Empty(unsupported);
            Assert.True(overlap.B > 200 && overlap.R < 60 && overlap.A > 250, $"opaque blue, was {overlap}");
        }

        [Fact]
        public void AlphaSetInsideTheGroup_StartsFromOne()
        {
            // A group starts with alpha 1 whatever it was outside: a shape drawn without setting alpha is opaque within the group, and the
            // group as a whole is then at the outside alpha.
            var pdf = Page("/Group << /S /Transparency >>");

            Assert.InRange(At(pdf, 10, 10, out _).A, 110, 145);
        }
    }
}
