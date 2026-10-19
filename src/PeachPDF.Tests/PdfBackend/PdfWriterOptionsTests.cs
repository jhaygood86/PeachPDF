using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// The small pieces of the PDF writer PeachPDF drives through its internals bridge: the layout-unit conversions, the encryption options'
    /// copy and comparison, and the context hook a host overrides.
    /// </summary>
    public class PdfWriterOptionsTests
    {
        [Fact]
        public void Units_ConvertPointsRectsAndSizesBetweenLayoutUnitsAndPoints()
        {
            Assert.Equal(new PaintPoint(20, 40), Units.ToPixels(new PaintPoint(10, 20), 2));
            Assert.Equal(new PaintPoint(5, 10), Units.ToPoints(new PaintPoint(10, 20), 2));

            var size = Units.ToPixels(new PeachDrawing.Core.Size(10, 20), 2);
            Assert.Equal(20, size.Width);
            Assert.Equal(40, size.Height);
            var back = Units.ToPoints(size, 2);
            Assert.Equal(10, back.Width);
            Assert.Equal(20, back.Height);

            var rect = Units.ToPixels(new Rect(1, 2, 3, 4), 3);
            Assert.Equal(new Rect(3, 6, 9, 12), rect);
            Assert.Equal(new Rect(1, 2, 3, 4), Units.ToPoints(rect, 3));
        }

        [Fact]
        public void Units_ConvertAPointArrayAndASystemColor()
        {
            var points = Units.ToPoints([new PaintPoint(10, 20), new PaintPoint(30, 40)], 10);

            Assert.Equal([new PaintPoint(1, 2), new PaintPoint(3, 4)], points);
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), Units.Convert(System.Drawing.Color.Red));
        }

        [Fact]
        public void ReadOnlyEncryptionOptions_ForbidEditingAndKeepThePasswords()
        {
            var options = PeachDrawing.PDF.Engine.PdfEncryptionOptions.ReadOnly("user", "owner");

            Assert.Equal("user", options.UserPassword);
            Assert.Equal("owner", options.OwnerPassword);
            Assert.Equal(PeachDrawing.PDF.Engine.PdfPermissions.ReadOnly, options.Permissions);
            Assert.True(options.EncryptMetadata);
        }

        [Fact]
        public void EncryptionOptions_CloneIsAnIndependentCopy_AndSameAsComparesEveryProperty()
        {
            var original = new PeachDrawing.PDF.Engine.PdfEncryptionOptions
            {
                UserPassword = "u",
                OwnerPassword = "o",
                Permissions = PeachDrawing.PDF.Engine.PdfPermissions.AllowPrint,
                EncryptMetadata = false,
            };

            var copy = original.Clone();
            original.UserPassword = "changed";

            Assert.Equal("u", copy.UserPassword);
            Assert.False(original.SameAs(copy));
            Assert.False(original.SameAs(null));

            original.UserPassword = "u";
            Assert.True(original.SameAs(copy));

            foreach (var change in new Action<PeachDrawing.PDF.Engine.PdfEncryptionOptions>[]
            {
                o => o.OwnerPassword = "x",
                o => o.Permissions = PeachDrawing.PDF.Engine.PdfPermissions.All,
                o => o.EncryptMetadata = true,
            })
            {
                var other = original.Clone();
                change(other);
                Assert.False(original.SameAs(other));
            }
        }

        [Fact]
        public void TheOptionConversions_CoverPageOrientationToo()
        {
            Assert.Equal(PeachDrawing.PDF.Engine.PageOrientation.Landscape, PeachPDF.PageOrientation.Landscape.ToEngine());
            Assert.Equal(PeachDrawing.PDF.Engine.PageOrientation.Portrait, PeachPDF.PageOrientation.Portrait.ToEngine());
        }

        [Fact]
        public void ABaseContext_HasNoVariationSyntaxOfItsOwn()
        {
            var context = new DerivedContext();

            Assert.Null(context.Resolve("wght 700", 16));
        }

        private sealed class DerivedContext : PeachDrawing.PDF.PdfRenderContext
        {
            public IReadOnlyList<PeachDrawing.Text.AxisSetting>? Resolve(string? variations, double size) => ResolveVariationAxes(variations, size);
        }
    }
}
