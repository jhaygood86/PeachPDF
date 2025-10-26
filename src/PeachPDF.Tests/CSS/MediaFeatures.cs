namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssMediaFeaturesTests
    {
        [Fact]
        public void CssMediaFeatureFactory()
        {
            var aspectRatio = MediaFeatureFactory.Instance.Create(FeatureNames.AspectRatio);
            Assert.NotNull(aspectRatio);
            Assert.IsType<AspectRatioMediaFeature>(aspectRatio);
            Assert.False(aspectRatio.IsMaximum);
            Assert.False(aspectRatio.IsMinimum);

            var colorIndex = MediaFeatureFactory.Instance.Create(FeatureNames.ColorIndex);
            Assert.NotNull(colorIndex);
            Assert.IsType<ColorIndexMediaFeature>(colorIndex);
            Assert.False(colorIndex.IsMaximum);
            Assert.False(colorIndex.IsMinimum);

            var deviceWidth = MediaFeatureFactory.Instance.Create(FeatureNames.DeviceWidth);
            Assert.NotNull(deviceWidth);
            Assert.IsType<DeviceWidthMediaFeature>(deviceWidth);
            Assert.False(deviceWidth.IsMaximum);
            Assert.False(deviceWidth.IsMinimum);

            var monochrome = MediaFeatureFactory.Instance.Create(FeatureNames.MaxMonochrome);
            Assert.NotNull(monochrome);
            Assert.IsType<MonochromeMediaFeature>(monochrome);
            Assert.True(monochrome.IsMaximum);
            Assert.False(monochrome.IsMinimum);

            var illegal = MediaFeatureFactory.Instance.Create("illegal");
            Assert.Null(illegal);
        }

        [Fact]
        public void CssMediaWidthValidation()
        {
            var width = new WidthMediaFeature(FeatureNames.Width);
            /*var check = width.TrySetValue(new Length(100, Length.Unit.Px));
            var valid = width.Validate(new RenderDevice(100, 0));
            var invalid = width.Validate(new RenderDevice(0, 0));
            Assert.True(check);
            Assert.True(valid);
            Assert.False(invalid);*/
        }

        [Fact]
        public void CssMediaMaxHeightValidation()
        {
            var height = new HeightMediaFeature(FeatureNames.MaxHeight);
            /*var check = height.TrySetValue(new Length(100, Length.Unit.Px));
            var valid = height.Validate(new RenderDevice(0, 99));
            var invalid = height.Validate(new RenderDevice(0, 101));
            Assert.True(check);
            Assert.True(valid);
            Assert.False(invalid);*/
        }

        [Fact]
        public void CssMediaMinDeviceWidthValidation()
        {
            var devwidth = new DeviceWidthMediaFeature(FeatureNames.MinDeviceWidth);
            /*var check = devwidth.TrySetValue(new Length(100, Length.Unit.Px));
            var valid = devwidth.Validate(new RenderDevice(101, 0));
            var invalid = devwidth.Validate(new RenderDevice(99, 0));
            Assert.True(check);
            Assert.True(valid);
            Assert.False(invalid);*/
        }

        [Fact]
        public void CssMediaAspectRatio()
        {
            var ratio = new AspectRatioMediaFeature(FeatureNames.AspectRatio);
            /*var check = ratio.TrySetValue(new CssValueList(new List<ICssValue>(new ICssValue[] {
                new Number(1f, Number.Unit.Integer),
                null, //CssValue.Delimiter,
                new Number(1f, Number.Unit.Integer)
            })));
            var valid = ratio.Validate(new RenderDevice(100, 100));
            var invalid = ratio.Validate(new RenderDevice(16, 9));
            Assert.True(check);
            Assert.True(valid);
            Assert.False(invalid);*/
        }
    }
}







