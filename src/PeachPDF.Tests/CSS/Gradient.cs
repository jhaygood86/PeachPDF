namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class GradientTests : CssConstructionFunctions
    {
        [Fact]
        public void InLinearGradient()
        {
            var source = "linear-gradient(135deg, red, blue)";
            var value = ParseValue(source);
            Assert.Equal(1, value.Count);
            Assert.Equal("linear-gradient", value[0].Data);
        }

        [Fact]
        public void InRadialGradient()
        {
            var source = "radial-gradient(ellipse farthest-corner at 45px 45px , #00FFFF, rgba(0, 0, 255, 0) 50%, #0000FF 95%)";
            var value = ParseValue(source);
            Assert.Equal("radial-gradient", value[0].Data);
        }

        [Fact]
        public void BackgroundImageLinearGradientWithAngle()
        {
            var source = "background-image: linear-gradient(135deg, red, blue)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageLinearGradientWithSide()
        {
            var source = "background-image: linear-gradient(to right, red, orange, yellow, green, blue, indigo, violet)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageLinearGradientWithCornerAndRgba()
        {
            var source = "background-image: linear-gradient(to bottom right, red, rgba(255,0,0,0))";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageLinearGradientWithSideAndHsl()
        {
            var source = "background-image: linear-gradient(to bottom, hsl(0, 80%, 70%), #bada55)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageLinearGradientNoAngle()
        {
            var source = "background-image: linear-gradient(yellow, blue 20%, #0f0)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientCircleFarthestCorner()
        {
            var source = "background-image: radial-gradient(circle farthest-corner at 45px 45px , #00FFFF 0%, rgba(0, 0, 255, 0) 50%, #0000FF 95%)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientEllipseFarthestCorner()
        {
            var source = "background-image: radial-gradient(ellipse farthest-corner at 470px 47px , #FFFF80 20%, rgba(204, 153, 153, 0.4) 30%, #E6E6FF 60%)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);

        }

        [Fact]
        public void BackgroundImageRadialGradientFarthestCornerWithPoint()
        {
            var source = "background-image: radial-gradient(farthest-corner at 45px 45px , #FF0000 0%, #0000FF 100%)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientSingleSize()
        {
            var source = "background-image: radial-gradient(16px at 60px 50% , #000000 0%, #000000 14px, rgba(0, 0, 0, 0.3) 18px, rgba(0, 0, 0, 0) 19px)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientCircle()
        {
            var source = "background-image: radial-gradient(circle, yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientOnlyGradientStops()
        {
            var source = "background-image: radial-gradient(yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientEllipseAtCenter()
        {
            var source = "background-image: radial-gradient(ellipse at center, yellow 0%, green 100%)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientFarthestCornerWithoutPoint()
        {
            var source = "background-image: radial-gradient(farthest-corner, yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientClosestSideWithPoint()
        {
            var source = "background-image: radial-gradient(closest-side at 20px 30px, red, yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientSizeAndPoint()
        {
            var source = "background-image: radial-gradient(20px 30px at 20px 30px, red, yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientClosestSideCircleShuffledWithPoint()
        {
            var source = "background-image: radial-gradient(closest-side circle at 20px 30px, red, yellow, green)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRadialGradientFarthestSideLeftBottom()
        {
            var source = "background-image: radial-gradient(farthest-side at left bottom, red, yellow 50px, green);";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRepeatingLinearGradientRedBlue()
        {
            var source = "background-image: repeating-linear-gradient(red, blue 20px, red 40px)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRepeatingRadialGradientRedBlue()
        {
            var source = "background-image: repeating-radial-gradient(red, blue 20px, red 40px)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }

        [Fact]
        public void BackgroundImageRepeatingRadialGradientFunky()
        {
            var source = "background-image: repeating-radial-gradient(circle closest-side at 20px 30px, red, yellow, green 100%, yellow 150%, red 200%)";
            var property = ParseDeclaration(source);
            Assert.IsType<BackgroundImageProperty>(property);
            var backgroundImage = property as BackgroundImageProperty;
            Assert.True(backgroundImage.HasValue);
            Assert.False(backgroundImage.IsInitial);
        }
    }
}







