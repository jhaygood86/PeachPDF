using PeachDrawing.Core;
using PeachDrawing.Filters;

namespace PeachDrawing.Tests
{
    public class FilterOpsMorphologyTests
    {
        private static RasterSurface Surface(int w, int h) => new(w, h, 0, 0, 1, 1);

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Morphology_VectorFormMatchesTheScalarReference(bool dilate)
        {
            var random = new Random(dilate ? 21 : 22);
            foreach (var (w, h) in new[] { (1, 1), (3, 2), (9, 9), (20, 7), (33, 41), (64, 3) })
            {
                var source = Surface(w, h);
                random.NextBytes(source.Pixels);
                foreach (var (rx, ry) in new[] { (0, 0), (1, 0), (0, 2), (1, 1), (2, 3), (4, 4), (9, 2), (2, 30) })
                {
                    var expected = Surface(w, h);
                    var actual = Surface(w, h);
                    FilterOps.MorphologyScalar(source, expected, rx, ry, dilate);
                    FilterOps.MorphologyVector(source, actual, rx, ry, dilate);

                    Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), $"{w}x{h} radius {rx},{ry} dilate {dilate}");
                }
            }
        }
    }
}
