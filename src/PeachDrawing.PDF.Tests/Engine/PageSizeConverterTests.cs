using System;
using PeachDrawing.PDF.Engine;

namespace PeachPDF.Tests.PdfEngineTests
{
    public class PageSizeConverterTests
    {
        [Theory]
        [InlineData("A4", 595, 842)]
        [InlineData("Letter", 612, 792)]
        [InlineData("Legal", 612, 1008)]
        [InlineData("A0", 2384, 3370)]
        [InlineData("Tabloid", 792, 1224)]
        public void ToSize_KnownPageSize_ReturnsExpectedPointDimensions(string pageSize, double expectedWidth, double expectedHeight)
        {
            var size = PageSizeConverter.ToSize(Enum.Parse<PageSize>(pageSize));

            Assert.Equal(expectedWidth, size.Width);
            Assert.Equal(expectedHeight, size.Height);
        }

        [Fact]
        public void ToSize_ScalesByDpi()
        {
            var size = PageSizeConverter.ToSize(PageSize.A4, dpi: 144);

            Assert.Equal(595 * 2.0, size.Width);
            Assert.Equal(842 * 2.0, size.Height);
        }

        [Fact]
        public void ToSize_Undefined_Throws()
        {
            Assert.Throws<ArgumentException>(() => PageSizeConverter.ToSize(PageSize.Undefined));
        }
    }
}
