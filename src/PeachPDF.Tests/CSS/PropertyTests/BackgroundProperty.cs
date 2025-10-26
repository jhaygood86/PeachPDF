namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class BackgroundPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void BackgroundAttachmentScrollLegal()
        {
            var snippet = "background-attachment : scroll";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("scroll", concrete.Value);
        }

        [Fact]
        public void BackgroundAttachmentInitialLegal()
        {
            var snippet = "background-attachment : initial";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("initial", concrete.Value);
        }

        [Fact]
        public void BackgroundAttachmentFixedUppercaseLegal()
        {
            var snippet = "background-attachment : Fixed ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("fixed", concrete.Value);
        }

        [Fact]
        public void BackgroundAttachmentFixedLocalLegal()
        {
            var snippet = "background-attachment : fixed  ,  local ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("fixed, local", concrete.Value);
        }

        [Fact]
        public void BackgroundAttachmentFixedLocalScrollScrollLegal()
        {
            var snippet = "background-attachment : fixed  ,  local,scroll,scroll ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("fixed, local, scroll, scroll", concrete.Value);
        }

        [Fact]
        public void BackgroundAttachmentNoneIllegal()
        {
            var snippet = "background-attachment : none ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-attachment", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundAttachmentProperty>(property);
            var concrete = (BackgroundAttachmentProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BackgroundClipPaddingBoxUppercaseLegal()
        {
            var snippet = "background-clip : Padding-Box ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-clip", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundClipProperty>(property);
            var concrete = (BackgroundClipProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("padding-box", concrete.Value);
        }

        [Fact]
        public void BackgroundClipPaddingBoxBorderBoxLegal()
        {
            var snippet = "background-clip : Padding-Box, border-box ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-clip", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundClipProperty>(property);
            var concrete = (BackgroundClipProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("padding-box, border-box", concrete.Value);
        }

        [Fact]
        public void BackgroundClipContentBoxLegal()
        {
            var snippet = "background-clip : content-box";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-clip", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundClipProperty>(property);
            var concrete = (BackgroundClipProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("content-box", concrete.Value);
        }

        [Fact]
        public void BackgroundColorTealLegal()
        {
            var snippet = "background-color : teal";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundColorProperty>(property);
            var concrete = (BackgroundColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(0, 128, 128)", concrete.Value);
        }

        [Theory]
        [InlineData("background-color: rgb(255, 255, 128)", "rgb(255, 255, 128)")]
        [InlineData("background-color: hsla(50, 33%, 25%, 0.75)", "hsla(50deg, 33%, 25%, 0.75)")]
        [InlineData("background-color : rgb(255  ,  255  ,  128)", "rgb(255, 255, 128)")]
        [InlineData("background-color: Transparent", "rgba(0, 0, 0, 0)")]
        [InlineData("background-color: #F09", "rgb(255, 0, 153)")]
        [InlineData("background-color: #F09F", "rgb(255, 0, 153)")]
        [InlineData("background-color: #AABBCC", "rgb(170, 187, 204)")]
        [InlineData("background-color: #AABBCC11", "rgba(170, 187, 204, 0.07)")]
        public void BackgroundColorRgbLegal(string snippet, string expectedValue)
        {
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundColorProperty>(property);
            var concrete = (BackgroundColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal(expectedValue, concrete.Value);
        }

        [Fact]
        public void BackgroundColorMultipleIllegal()
        {
            var snippet = "background-color : #bbff00, transparent, red, #ff00ff";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundColorProperty>(property);
            var concrete = (BackgroundColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BackgroundImageNoneLegal()
        {
            var snippet = "background-image: NONE";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void BackgroundImageUrlAndNoneLegal()
        {
            var snippet = "background-image: url(\"img/sprites.svg?v=1bc768be1b3c\"),none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"img/sprites.svg?v=1bc768be1b3c\"), none", concrete.Value);
        }

        [Fact]
        public void BackgroundImageUrlLegal()
        {
            var snippet = "background-image: url(image.png)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\")", concrete.Value);
        }

        [Fact]
        public void BackgroundImageUrlAbsoluteLegal()
        {
            var snippet = "background-image: url(http://www.example.com/images/bck.png)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"http://www.example.com/images/bck.png\")", concrete.Value);
        }

        [Fact]
        public void BackgroundImageUrlsLegal()
        {
            var snippet = "background-image: url(image.png),url('bla.png')";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\"), url(\"bla.png\")", concrete.Value);
        }

        [Fact]
        public void BackgroundImageUrlNoneUrlLegal()
        {
            var snippet = "background-image: url(image.png),none, url(foo.gif)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"image.png\"), none, url(\"foo.gif\")", concrete.Value);
        }

        [Fact]
        public void BackgroundOriginContentBoxLegal()
        {
            var snippet = "background-origin: CONTENT-BOX";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundOriginProperty>(property);
            var concrete = (BackgroundOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("content-box", concrete.Value);
        }

        [Fact]
        public void BackgroundOriginContentBoxPaddingBoxLegal()
        {
            var snippet = "background-origin: CONTENT-BOX, Padding-Box";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundOriginProperty>(property);
            var concrete = (BackgroundOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("content-box, padding-box", concrete.Value);
        }

        [Fact]
        public void BackgroundOriginBorderBoxLegal()
        {
            var snippet = "background-origin: border-box";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundOriginProperty>(property);
            var concrete = (BackgroundOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("border-box", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionTopLegal()
        {
            var snippet = "background-position: top";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("top", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionPercentPercentLegal()
        {
            var snippet = "background-position: 25% 75%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25% 75%", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionCenterPercentLegal()
        {
            var snippet = "background-position: center 75%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("center 75%", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionRightLengthBottomLengthLegal()
        {
            var snippet = "background-position: right 20px bottom 20px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right 20px bottom 20px", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionLengthLengthCenterMultipleLegal()
        {
            var snippet = "background-position: 10px 20px, center";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("10px 20px, center", concrete.Value);
        }

        [Fact]
        public void BackgroundPositionZeroMultipleLegal()
        {
            var snippet = "background-position: 0 0, 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundPositionProperty>(property);
            var concrete = (BackgroundPositionProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0 0, 0 0", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRepeatXLegal()
        {
            var snippet = "background-repeat: repeat-x";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat-x", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRepeatYLegal()
        {
            var snippet = "background-repeat: repeat-y";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat-y", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRepeatLegal()
        {
            var snippet = "background-repeat: REPEAT";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRoundLegal()
        {
            var snippet = "background-repeat: rounD";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("round", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRepeatSpaceLegal()
        {
            var snippet = "background-repeat: repeat space";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat space", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatRepeatXSpaceIllegal()
        {
            var snippet = "background-repeat: repeat-x space";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BackgroundRepeatRepeatXRepeatYMultipleLegal()
        {
            var snippet = "background-repeat: repeat-X, repeat-Y";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat-x, repeat-y", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatSpaceRoundLegal()
        {
            var snippet = "background-repeat: space round";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("space round", concrete.Value);
        }

        [Fact]
        public void BackgroundRepeatNoRepeatRepeatXIllegal()
        {
            var snippet = "background-repeat: no-repeat repeat-x";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BackgroundRepeatRepeatRepeatNoRepeatRepeatLegal()
        {
            var snippet = "background-repeat: repeat repeat, no-repeat repeat";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-repeat", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundRepeatProperty>(property);
            var concrete = (BackgroundRepeatProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("repeat repeat, no-repeat repeat", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeLengthLegal()
        {
            var snippet = "background-size: 2em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("2em", concrete.Value);
        }

        [Fact]
        public void BackgroundSizePercentLegal()
        {
            var snippet = "background-size: 20%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("20%", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeAutoAutoLegal()
        {
            var snippet = "background-size: auto auto";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto auto", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeAutoLengthLegal()
        {
            var snippet = "background-size: auto 50px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto 50px", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeLengthLengthLegal()
        {
            var snippet = "background-size: 25px 50px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("25px 50px", concrete.Value);
        }

        [Fact]
        public void BackgroundSizePercentPercentLegal()
        {
            var snippet = "background-size: 50% 50%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("50% 50%", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeAutoUppercaseLegal()
        {
            var snippet = "background-size: AUTO";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("auto", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeCoverLegal()
        {
            var snippet = "background-size: cover";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("cover", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeContainCoverMultipleLegal()
        {
            var snippet = "background-size: contain,cover";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("contain, cover", concrete.Value);
        }

        [Fact]
        public void BackgroundSizeContainLengthAutoPercentLegal()
        {
            var snippet = "background-size: contain,100px,auto,20%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-size", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundSizeProperty>(property);
            var concrete = (BackgroundSizeProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("contain, 100px, auto, 20%", concrete.Value);
        }

        [Fact]
        public void BackgroundRedLegal()
        {
            var snippet = "background: red";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundProperty>(property);
            var concrete = (BackgroundProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void BackgroundWhiteImageLegal()
        {
            var snippet = "background: white url(\"pendant.png\");";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundProperty>(property);
            var concrete = (BackgroundProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"pendant.png\") rgb(255, 255, 255)", concrete.Value);
        }

        [Fact]
        public void BackgroundImageLegal()
        {
            var snippet = "background: url(\"topbanner.png\") #00d repeat-y fixed";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundProperty>(property);
            var concrete = (BackgroundProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"topbanner.png\") repeat-y fixed rgb(0, 0, 221)", concrete.Value);
        }

        [Fact]
        public void BackgroundWithoutColorLegal()
        {
            var snippet = "background: url(\"img_tree.png\") no-repeat right top";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundProperty>(property);
            var concrete = (BackgroundProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"img_tree.png\") right top no-repeat", concrete.Value);
        }

        [Fact]
        public void BackgroundImageDataUrlLegal()
        {
            var url = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEcAAAAcCAMAAAAEJ1IZAAAABGdBTUEAALGPC/xhBQAAVAI/VAI/VAI/VAI/VAI/VAI/VAAAA////AI/VRZ0U8AAAAFJ0Uk5TYNV4S2UbgT/Gk6uQt585w2wGXS0zJO2lhGttJK6j4YqZSobH1AAAAAElFTkSuQmCC";
            var snippet = "background-image: url('" + url + "')";
            var property = ParseDeclaration(snippet);
            Assert.Equal("background-image", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BackgroundImageProperty>(property);
            var concrete = (BackgroundImageProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("url(\"" + url + "\")", concrete.Value);
        }
    }
}








