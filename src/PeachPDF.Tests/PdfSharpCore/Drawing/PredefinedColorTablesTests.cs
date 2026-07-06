using PeachPDF.PdfSharpCore.Drawing;
using System.Reflection;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    public class PredefinedColorTablesTests
    {
        static PropertyInfo[] PublicStaticProperties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Static);

        [Fact]
        public void XColors_ExposesOneHundredFortyOneNamedColors()
        {
            var properties = PublicStaticProperties(typeof(XColors));

            Assert.Equal(141, properties.Length);
            foreach (var property in properties)
            {
                var color = (XColor)property.GetValue(null)!;
                Assert.False(color.IsEmpty, $"{property.Name} should not be XColor.Empty");
            }
        }

        [Fact]
        public void XColors_Black_And_White_HaveExpectedRgb()
        {
            Assert.Equal(0, XColors.Black.R);
            Assert.Equal(0, XColors.Black.G);
            Assert.Equal(0, XColors.Black.B);

            Assert.Equal(255, XColors.White.R);
            Assert.Equal(255, XColors.White.G);
            Assert.Equal(255, XColors.White.B);
        }

        [Fact]
        public void XPens_ExposesOneHundredFortyOnePredefinedPens()
        {
            var properties = PublicStaticProperties(typeof(XPens));

            Assert.Equal(141, properties.Length);
            foreach (var property in properties)
            {
                var pen = (XPen)property.GetValue(null)!;
                Assert.NotNull(pen);
                Assert.Equal(1, pen.Width);
            }
        }

        [Fact]
        public void XPens_Black_HasBlackColor()
        {
            Assert.Equal(XColors.Black, XPens.Black.Color);
        }

        [Fact]
        public void XBrushes_ExposesOneHundredFortyOnePredefinedBrushes()
        {
            var properties = PublicStaticProperties(typeof(XBrushes));

            Assert.Equal(141, properties.Length);
            foreach (var property in properties)
            {
                var brush = (XSolidBrush)property.GetValue(null)!;
                Assert.NotNull(brush);
            }
        }

        [Fact]
        public void XBrushes_Black_HasBlackColor()
        {
            Assert.Equal(XColors.Black, XBrushes.Black.Color);
        }
    }
}
