using Xunit;

namespace PeachDrawing.Tests
{
    /// <summary>Which image media types a context says it can decode, for a document choosing between <c>&lt;source type&gt;</c> alternatives.</summary>
    public class SupportsImageTypeTests
    {
        [Theory]
        [InlineData("image/png", true)]
        [InlineData("IMAGE/AVIF", true)]
        [InlineData(" image/webp ", true)]
        [InlineData("image/jxl", true)]
        [InlineData("image/svg+xml", true)]
        [InlineData("image/jxl-unknown", false)]
        [InlineData("text/html", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Default_IsWhatPeachImageReads(string? type, bool expected)
        {
            var context = new RasterRenderContext();

            Assert.Equal(expected, context.SupportsImageType(type!));
        }
    }
}
