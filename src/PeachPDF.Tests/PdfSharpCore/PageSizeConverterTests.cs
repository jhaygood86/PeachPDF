using PeachPDF.PdfSharpCore;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class PageSizeConverterTests
    {
        [Theory]
        [InlineData(PageSize.A4, 595, 842)]
        [InlineData(PageSize.Letter, 612, 792)]
        [InlineData(PageSize.Legal, 612, 1008)]
        [InlineData(PageSize.A0, 2384, 3370)]
        [InlineData(PageSize.Tabloid, 792, 1224)]
        public void ToSize_KnownPageSize_ReturnsExpectedPointDimensions(PageSize pageSize, double expectedWidth, double expectedHeight)
        {
            var size = PageSizeConverter.ToSize(pageSize);

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
