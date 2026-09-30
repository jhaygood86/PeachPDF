using PeachDrawing.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    public class TypefaceQueryTests
    {
        [Fact]
        public void From_CarriesWeightWidthItalicAndMustCoverThrough()
        {
            var rune = new System.Text.Rune('A');
            var query = TypefaceQuery.From(600, 87.5, true, rune, null, null);

            Assert.Equal(600, query.Weight);
            Assert.Equal(TypefaceQuery.NormalWidth, query.Width);
            Assert.True(query.IsItalic);
            Assert.Equal(rune, query.MustCover);
            Assert.Equal(87.5, query.WidthPercent);
            Assert.Null(query.ObliqueAngle);
        }

        [Fact]
        public void From_WithNoObliqueSkew_LeavesObliqueAngleNull()
        {
            var query = TypefaceQuery.From(400, 100, false, null, null, obliqueSkewSinus: null);

            Assert.Null(query.ObliqueAngle);
        }

        [Fact]
        public void From_ConvertsAnObliqueSkewSineToItsAngleInDegrees()
        {
            // sin(14deg), the CSS Fonts default oblique angle.
            var sinOf14Degrees = System.Math.Sin(14.0 * System.Math.PI / 180);

            var query = TypefaceQuery.From(400, 100, true, null, null, sinOf14Degrees);

            Assert.NotNull(query.ObliqueAngle);
            Assert.Equal(14.0, query.ObliqueAngle!.Value, 3);
        }

        [Fact]
        public void From_ClampsAnOutOfRangeSineBeforeTakingItsArcsine()
        {
            // A sine outside [-1, 1] can't come from a real angle; Math.Asin would return NaN
            // unclamped - this must produce a real, finite angle instead (the closest valid one).
            var query = TypefaceQuery.From(400, 100, true, null, null, obliqueSkewSinus: 1.5);

            Assert.NotNull(query.ObliqueAngle);
            Assert.True(double.IsFinite(query.ObliqueAngle!.Value));
            Assert.Equal(90.0, query.ObliqueAngle.Value, 3);
        }

        [Fact]
        public void From_PassesAxesThrough()
        {
            var axes = new[] { new AxisSetting("wght", 550) };

            var query = TypefaceQuery.From(400, 100, false, null, axes, null);

            Assert.Same(axes, query.Axes);
        }
    }
}
