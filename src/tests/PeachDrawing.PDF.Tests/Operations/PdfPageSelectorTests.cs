namespace PeachDrawing.PDF.Tests.Operations
{
    public class PdfPageSelectorTests
    {
        [Theory]
        [InlineData("1", 10, new[] { 1 })]
        [InlineData("r2", 10, new[] { 9 })]
        [InlineData("z", 10, new[] { 10 })]
        [InlineData("r1", 10, new[] { 10 })]
        [InlineData("1,6,4", 10, new[] { 1, 6, 4 })]
        [InlineData("3-7", 10, new[] { 3, 4, 5, 6, 7 })]
        [InlineData("7-3", 10, new[] { 7, 6, 5, 4, 3 })]
        [InlineData("1-z", 3, new[] { 1, 2, 3 })]
        [InlineData("z-1", 3, new[] { 3, 2, 1 })]
        [InlineData("1,3,5-9,15-12", 15, new[] { 1, 3, 5, 6, 7, 8, 9, 15, 14, 13, 12 })]
        [InlineData("r3-r1", 10, new[] { 8, 9, 10 })]
        [InlineData("1-10:even", 20, new[] { 2, 4, 6, 8, 10 })]
        [InlineData("1-10:odd", 20, new[] { 1, 3, 5, 7, 9 })]
        [InlineData("2-7:odd", 20, new[] { 2, 4, 6 })]
        [InlineData("1-10,x3-4", 10, new[] { 1, 2, 5, 6, 7, 8, 9, 10 })]
        [InlineData("4-10,x7-9,12-8,xr5", 12, new[] { 4, 5, 6, 10, 12, 11, 10, 9 })]
        [InlineData(" 2 , 3 ", 5, new[] { 2, 3 })]
        public void Select_ReadsEveryFormOfTheGrammar(string selector, int pageCount, int[] expected) =>
            Assert.Equal(expected, PdfPageSelector.Select(selector, pageCount));

        [Theory]
        [InlineData("")]
        [InlineData("1,,2")]
        [InlineData("a")]
        [InlineData("11")]
        [InlineData("0")]
        [InlineData("r0")]
        [InlineData("r11")]
        [InlineData("1-2:sometimes")]
        [InlineData("1-")]
        [InlineData("-3")]
        public void Select_RefusesWhatIsNotAPageOfTheDocument(string selector) =>
            Assert.Throws<FormatException>(() => PdfPageSelector.Select(selector, 10));

        [Fact]
        public void Select_ChecksItsArguments()
        {
            Assert.Throws<ArgumentNullException>(() => PdfPageSelector.Select(null!, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfPageSelector.Select("1", -1));
        }
    }
}
