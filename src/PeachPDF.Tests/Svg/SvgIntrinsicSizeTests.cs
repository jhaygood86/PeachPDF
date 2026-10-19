using PeachDrawing.Core;
using PeachPDF.Svg;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <see cref="SvgIntrinsicSize.HasNaturalSize"/>: whether an svg has a natural size in an axis, as
    /// opposed to only a preferred aspect ratio (a <c>viewBox</c> alone).
    /// </summary>
    public class SvgIntrinsicSizeTests
    {
        private static readonly Rect ViewBox = new(0, 0, 96, 48);

        private static SvgDocument Doc(double? width = null, double? height = null, bool viewBox = false) =>
            new() { Width = width, Height = height, ViewBox = viewBox ? ViewBox : null };

        [Theory]
        // one attribute and no viewBox: the other axis takes the 300x150px default object size
        [InlineData(96.0, null, false, true, true)]
        [InlineData(null, 48.0, false, true, true)]
        [InlineData(96.0, 48.0, false, true, true)]
        // the other attribute plus a viewBox gives the ratio, so both axes have a natural size
        [InlineData(96.0, null, true, true, true)]
        [InlineData(null, 48.0, true, true, true)]
        // a viewBox alone is an aspect ratio, not a size
        [InlineData(null, null, true, false, false)]
        // nothing at all
        [InlineData(null, null, false, false, false)]
        // a zero size is not a natural size
        [InlineData(0.0, null, false, false, false)]
        [InlineData(null, 0.0, false, false, false)]
        [InlineData(0.0, null, true, false, false)]
        [InlineData(null, 0.0, true, false, false)]
        public void HasNaturalSize_ReportsEachAxisOnItsOwn(
            double? width, double? height, bool viewBox, bool horizontal, bool vertical)
        {
            var document = Doc(width, height, viewBox);

            Assert.Equal(horizontal, SvgIntrinsicSize.HasNaturalSize(document, horizontal: true));
            Assert.Equal(vertical, SvgIntrinsicSize.HasNaturalSize(document, horizontal: false));
        }

        [Fact]
        public void ResolveForBox_GivesTheMissingAxisTheDefaultObjectSize_AndNoRatio()
        {
            Assert.Equal((96.0, 150.0, false), SvgIntrinsicSize.ResolveForBox(Doc(width: 96)));
            Assert.Equal((300.0, 48.0, false), SvgIntrinsicSize.ResolveForBox(Doc(height: 48)));
            Assert.Equal((96.0, 48.0, true), SvgIntrinsicSize.ResolveForBox(Doc(width: 96, height: 48)));
            Assert.Equal((96.0, 48.0, true), SvgIntrinsicSize.ResolveForBox(Doc(viewBox: true)));
            Assert.Equal((null, null, false), SvgIntrinsicSize.ResolveForBox(Doc()));
            Assert.Equal((null, null, false), SvgIntrinsicSize.ResolveForBox(null));
        }

        [Fact]
        public void HasNaturalSize_NoDocument_HasNone()
        {
            Assert.False(SvgIntrinsicSize.HasNaturalSize(null, horizontal: true));
            Assert.False(SvgIntrinsicSize.HasNaturalSize(null, horizontal: false));
        }

        [Fact]
        public void HasNaturalSize_ADegenerateViewBoxDoesNotCountAsARatio()
        {
            var document = new SvgDocument { Width = 96, ViewBox = new Rect(0, 0, 0, 48) };

            Assert.True(SvgIntrinsicSize.HasNaturalSize(document, horizontal: true));   // its own width
            Assert.True(SvgIntrinsicSize.HasNaturalSize(document, horizontal: false));  // the default height
        }
    }
}
