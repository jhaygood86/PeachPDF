namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssTransformPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void CssPerspectiveNoneUppercaseLegal()
        {
            var snippet = "perspective:  NONE ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveProperty>(property);
            var concrete = (PerspectiveProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveLengthPixelLegal()
        {
            var snippet = "perspective:  20px  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveProperty>(property);
            var concrete = (PerspectiveProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("20px", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveLengthEmLegal()
        {
            var snippet = "perspective:  3.5em  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveProperty>(property);
            var concrete = (PerspectiveProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3.5em", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveZeroLegal()
        {
            var snippet = "perspective:  0  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveProperty>(property);
            var concrete = (PerspectiveProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssPerspectivePercentIllegal()
        {
            var snippet = "perspective:  10%  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveProperty>(property);
            var concrete = (PerspectiveProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssPerspectiveOriginZeroLegal()
        {
            var snippet = "perspective-origin:  0  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginLengthLegal()
        {
            var snippet = "perspective-origin:  20px  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("20px", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginLeftLegal()
        {
            var snippet = "perspective-origin:  left  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginPercentLegal()
        {
            var snippet = "perspective-origin:  15%  ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15%", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginPercentPercentLegal()
        {
            var snippet = "perspective-origin:  15% 25% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15% 25%", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginLeftCenterLegal()
        {
            var snippet = "perspective-origin:  left center ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left center", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginRightBottomLegal()
        {
            var snippet = "perspective-origin:  right BOTTOM ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right bottom", concrete.Value);
        }

        [Fact]
        public void CssPerspectiveOriginTopCenterLegal()
        {
            var snippet = "perspective-origin:  top center ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("perspective-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<PerspectiveOriginProperty>(property);
            var concrete = (PerspectiveOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("center top", concrete.Value);
        }

        [Fact]
        public void CssTransformStylePreserve3DLegal()
        {
            var snippet = "transform-style:  preserve-3d ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-style", property.Name);
            Assert.True(property.HasValue);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformStyleProperty>(property);
            var concrete = (TransformStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("preserve-3d", concrete.Value);
        }

        [Fact]
        public void CssTransformStyleNoneIllegal()
        {
            var snippet = "transform-style:  none ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformStyleProperty>(property);
            var concrete = (TransformStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void CssTransformOriginXOffsetLegal()
        {
            var snippet = "transform-origin:  2px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("2px", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginXOffsetKeywordLegal()
        {
            var snippet = "transform-origin:  bottom ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("bottom", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginYOffsetLegal()
        {
            var snippet = "transform-origin:  3cm 2px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3cm 2px", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginYOffsetXKeywordLegal()
        {
            var snippet = "transform-origin:  2px left";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left 2px", concrete.Value); // Canonical order: x then y
        }

        [Fact]
        public void CssTransformOriginXKeywordYOffsetLegal()
        {
            var snippet = "transform-origin:  left 2px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left 2px", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginXKeywordYKeywordLegal()
        {
            var snippet = "transform-origin:  right top ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right top", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginYKeywordXKeywordLegal()
        {
            var snippet = "transform-origin:  top  right ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right top", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginXYZLegal()
        {
            var snippet = "transform-origin:  2px 30% 10px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("2px 30% 10px", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginYXKeywordZLegal()
        {
            var snippet = "transform-origin:  2px left 10px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left 2px 10px", concrete.Value); // Canonical order: x y z
        }

        [Fact]
        public void CssTransformOriginXKeywordYZLegal()
        {
            var snippet = "transform-origin:  left 5px -3px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left 5px -3px", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginXKeywordYKeywordZLegal()
        {
            var snippet = "transform-origin:  right bottom 2cm ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right bottom 2cm", concrete.Value);
        }

        [Fact]
        public void CssTransformOriginYKeywordXKeywordZLegal()
        {
            var snippet = "transform-origin:  bottom  right  2cm ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform-origin", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformOriginProperty>(property);
            var concrete = (TransformOriginProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("right bottom 2cm", concrete.Value);
        }

        [Fact]
        public void CssTransformNoneLegal()
        {
            var snippet = "transform:  none ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void CssTransformMatrixLegal()
        {
            var snippet = "transform:  matrix(1.0, 2.0, 3.0, 4.0, 5.0, 6.0) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("matrix(1, 2, 3, 4, 5, 6)", concrete.Value);
        }

        [Fact]
        public void CssTransformTranslateLegal()
        {
            var snippet = "transform:  translate(12px, 50%) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("translate(12px, 50%)", concrete.Value);
        }

        [Fact]
        public void CssTransformTranslateXLegal()
        {
            var snippet = "transform:  translateX(2em) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("translateX(2em)", concrete.Value);
        }

        [Fact]
        public void CssTransformTranslateYLegal()
        {
            var snippet = "transform:  translateY(3in) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("translateY(3in)", concrete.Value);
        }

        [Fact]
        public void CssTransformScaleLegal()
        {
            var snippet = "transform:  scale(2, 0.5) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("scale(2, 0.5)", concrete.Value);
        }

        [Fact]
        public void CssTransformScaleXLegal()
        {
            var snippet = "transform:  scaleX(0.1) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("scaleX(0.1)", concrete.Value);
        }

        [Fact]
        public void CssTransformScaleYLegal()
        {
            var snippet = "transform:  scaleY(1.5) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("scaleY(1.5)", concrete.Value);
        }

        [Fact]
        public void CssTransformRotateLegal()
        {
            var snippet = "transform:  rotate(0.5turn) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rotate(0.5turn)", concrete.Value);
        }

        [Fact]
        public void CssTransformSkewXLegal()
        {
            var snippet = "transform:  skewX(  30deg  ) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("skewX(30deg)", concrete.Value);
        }

        [Fact]
        public void CssTransformSkewYLegal()
        {
            var snippet = "transform:  skewY(  1.07rad  ) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("skewY(1.07rad)", concrete.Value);
        }

        [Fact]
        public void CssTransformMultipleLegal()
        {
            var snippet = "transform:  translate(50%, 50%) rotate(45deg) scale(1.5)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("translate(50%, 50%) rotate(45deg) scale(1.5)", concrete.Value);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(3, elements.Length);
        }

        [Fact]
        public void CssTransformMatrix3dLegal()
        {
            var snippet = "transform:  matrix3d(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<MatrixTransform>(element);
        }

        [Fact]
        public void CssTransformTranslate3dLegal()
        {
            var snippet = "transform:  translate3d(12px, 50%, 3em)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<TranslateTransform>(element);
        }

        [Fact]
        public void CssTransformTranslateZLegal()
        {
            var snippet = "transform:  translateZ(2px)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<TranslateTransform>(element);
        }

        [Fact]
        public void CssTransformScale3dLegal()
        {
            var snippet = "transform:  scale3d(2.5, 1.2, 0.3)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<ScaleTransform>(element);
        }

        [Fact]
        public void CssTransformScaleZLegal()
        {
            var snippet = "transform:  scaleZ(0.3)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<ScaleTransform>(element);
        }

        [Fact]
        public void CssTransformRotate3dLegal()
        {
            var snippet = "transform:  rotate3d(1, 2.0, 3.0, 10deg)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<RotateTransform>(element);
        }

        [Fact]
        public void CssTransformRotateXLegal()
        {
            var snippet = "transform:  rotateX(10deg)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<RotateTransform>(element);
        }

        [Fact]
        public void CssTransformRotateYLegal()
        {
            var snippet = "transform:  rotateY(10deg)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<RotateTransform>(element);
        }

        [Fact]
        public void CssTransformRotateZLegal()
        {
            var snippet = "transform: rotateZ(10deg)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<RotateTransform>(element);
        }

        [Fact]
        public void CssTransformPerspectiveLegal()
        {
            var snippet = "transform: perspective(17px)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("transform", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<TransformProperty>(property);
            var concrete = (TransformProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //var elements = concrete.Transforms.ToArray();
            //Assert.Equal(1, elements.Length);
            //var element = elements[0];
            //Assert.IsType<PerspectiveTransform>(element);
        }
    }
}












































