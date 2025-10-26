namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using Xunit;

    public class BorderPropertyTests : CssConstructionFunctions
    {
        [Fact]
        public void BorderSpacingLengthLegal()
        {
            var snippet = "border-spacing: 20px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderSpacingProperty>(property);
            var concrete = (BorderSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("20px", concrete.Value);
        }

        [Fact]
        public void BorderSpacingZeroLegal()
        {
            var snippet = "border-spacing: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderSpacingProperty>(property);
            var concrete = (BorderSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderSpacingLengthLengthLegal()
        {
            var snippet = "border-spacing: 15px 3em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderSpacingProperty>(property);
            var concrete = (BorderSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px 3em", concrete.Value);
        }

        [Fact]
        public void BorderSpacingLengthZeroLegal()
        {
            var snippet = "border-spacing: 15px 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderSpacingProperty>(property);
            var concrete = (BorderSpacingProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("15px 0", concrete.Value);
        }

        [Fact]
        public void BorderSpacingPercentIllegal()
        {
            var snippet = "border-spacing: 15%";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-spacing", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderSpacingProperty>(property);
            var concrete = (BorderSpacingProperty)property;
            Assert.True(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderBottomColorRedLegal()
        {
            var snippet = "border-bottom-color: red";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomColorProperty>(property);
            var concrete = (BorderBottomColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0)", concrete.Value);
        }

        [Fact]
        public void BorderTopColorHexLegal()
        {
            var snippet = "border-top-color: #0F0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopColorProperty>(property);
            var concrete = (BorderTopColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(0, 255, 0)", concrete.Value);
        }

        [Fact]
        public void BorderRightColorRgbaLegal()
        {
            var snippet = "border-right-color: rgba(1, 1, 1, 0)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-right-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRightColorProperty>(property);
            var concrete = (BorderRightColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgba(1, 1, 1, 0)", concrete.Value);
        }

        [Fact]
        public void BorderLeftColorRgbLegal()
        {
            var snippet = "border-left-color: rgb(1, 255, 100)  !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-left-color", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<BorderLeftColorProperty>(property);
            var concrete = (BorderLeftColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(1, 255, 100)", concrete.Value);
        }

        [Fact]
        public void BorderColorTransparentLegal()
        {
            var snippet = "border-color: transparent";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgba(0, 0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void BorderColorRedGreenLegal()
        {
            var snippet = "border-color: red   green";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0) rgb(0, 128, 0)", concrete.Value);
        }

        [Fact]
        public void BorderColorRedRgbLegal()
        {
            var snippet = "border-color: red   rgb(0,0,0)";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0) rgb(0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void BorderColorRedBlueGreenLegal()
        {
            var snippet = "border-color: red blue green";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0) rgb(0, 0, 255) rgb(0, 128, 0)", concrete.Value);
        }

        [Fact]
        public void BorderColorRedBlueGreenBlackLegal()
        {
            var snippet = "border-color: red blue green   BLACK";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 0, 0) rgb(0, 0, 255) rgb(0, 128, 0) rgb(0, 0, 0)", concrete.Value);
        }

        [Fact]
        public void BorderColorRedBlueGreenBlackTransparentIllegal()
        {
            var snippet = "border-color: red blue green black transparent";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-color", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderColorProperty>(property);
            var concrete = (BorderColorProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderStyleDottedLegal()
        {
            var snippet = "border-style: dotted";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("dotted", concrete.Value);
        }

        [Fact]
        public void BorderStyleInsetOutsetUpperLegal()
        {
            var snippet = "border-style: INSET   OUTset";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("inset outset", concrete.Value);
        }

        [Fact]
        public void BorderStyleDoubleGrooveLegal()
        {
            var snippet = "border-style: double   groove";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("double groove", concrete.Value);
        }

        [Fact]
        public void BorderStyleRidgeSolidDashedLegal()
        {
            var snippet = "border-style: ridge solid dashed";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("ridge solid dashed", concrete.Value);
        }

        [Fact]
        public void BorderStyleHiddenDottedNoneNoneLegal()
        {
            var snippet = "border-style   :   hidden  dotted  NONE   nONe";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("hidden dotted none none", concrete.Value);
        }

        [Fact]
        public void BorderStyleWavyIllegal()
        {
            var snippet = "border-style: wavy";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderStyleProperty>(property);
            var concrete = (BorderStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderBottomStyleGrooveLegal()
        {
            var snippet = "border-bottom-style: GROOVE";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomStyleProperty>(property);
            var concrete = (BorderBottomStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("groove", concrete.Value);
        }

        [Fact]
        public void BorderTopStyleNoneLegal()
        {
            var snippet = "border-top-style:none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopStyleProperty>(property);
            var concrete = (BorderTopStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
        }

        [Fact]
        public void BorderRightStyleDoubleLegal()
        {
            var snippet = "border-right-style:double";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-right-style", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRightStyleProperty>(property);
            var concrete = (BorderRightStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("double", concrete.Value);
        }

        [Fact]
        public void BorderLeftStyleHiddenLegal()
        {
            var snippet = "border-left-style: hidden  !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-left-style", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<BorderLeftStyleProperty>(property);
            var concrete = (BorderLeftStyleProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("hidden", concrete.Value);
        }

        [Fact]
        public void BorderBottomWidthThinLegal()
        {
            var snippet = "border-bottom-width: THIN";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomWidthProperty>(property);
            var concrete = (BorderBottomWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px", concrete.Value);
        }

        [Fact]
        public void BorderTopWidthZeroLegal()
        {
            var snippet = "border-top-width: 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopWidthProperty>(property);
            var concrete = (BorderTopWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
        }

        [Fact]
        public void BorderRightWidthEmLegal()
        {
            var snippet = "border-right-width: 3em";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-right-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRightWidthProperty>(property);
            var concrete = (BorderRightWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3em", concrete.Value);
        }

        [Fact]
        public void BorderLeftWidthThickLegal()
        {
            var snippet = "border-left-width: thick !important";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-left-width", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<BorderLeftWidthProperty>(property);
            var concrete = (BorderLeftWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("5px", concrete.Value);
        }

        [Fact]
        public void BorderWidthMediumLegal()
        {
            var snippet = "border-width: medium";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px", concrete.Value);
        }

        [Fact]
        public void BorderWidthLengthZeroLegal()
        {
            var snippet = "border-width: 3px   0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px 0", concrete.Value);
        }

        [Fact]
        public void BorderWidthThinLengthLegal()
        {
            var snippet = "border-width: THIN   1px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px", concrete.Value);
        }

        [Fact]
        public void BorderWidthMediumThinThickLegal()
        {
            var snippet = "border-width: medium thin thick";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px 1px 5px", concrete.Value);
        }

        [Fact]
        public void BorderWidthLengthLengthLengthLengthLegal()
        {
            var snippet = "border-width:  1px  2px   3px  4px  !important ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.True(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px 2px 3px 4px", concrete.Value);
        }

        [Fact]
        public void BorderWidthLengthInEmZeroLegal()
        {
            var snippet = "border-width:  0.3em 0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.3em 0", concrete.Value);
            //Assert.Equal(new Length(0.3f, Length.Unit.Em), concrete.Top);
            //Assert.Equal(Length.Zero, concrete.Right);
            //Assert.Equal(new Length(0.3f, Length.Unit.Em), concrete.Bottom);
            //Assert.Equal(Length.Zero, concrete.Left);
        }

        [Fact]
        public void BorderWidthMediumZeroLengthThickLegal()
        {
            var snippet = "border-width:   medium 0 1px thick ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3px 0 1px 5px", concrete.Value);
            //Assert.Equal(Length.Medium, concrete.Top);
            //Assert.Equal(Length.Zero, concrete.Right);
            //Assert.Equal(new Length(1f, Length.Unit.Px), concrete.Bottom);
            //Assert.Equal(Length.Thick, concrete.Left);
        }

        [Fact]
        public void BorderWidthZerosIllegal()
        {
            var snippet = "border-width: 0 0 0 0 0";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-width", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderWidthProperty>(property);
            var concrete = (BorderWidthProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void BorderLeftZeroLegal()
        {
            var snippet = "border-left:   0 ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-left", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderLeftProperty>(property);
            var concrete = (BorderLeftProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0", concrete.Value);
            //Assert.Equal(Length.Zero, concrete.Width);
            //Assert.Equal(Color.Transparent, concrete.Color);
            //Assert.Equal(LineStyle.None, concrete.Style);
        }

        [Fact]
        public void BorderRightLineStyleLegal()
        {
            var snippet = "border-right :   dotted ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-right", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderRightProperty>(property);
            var concrete = (BorderRightProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("dotted", concrete.Value);
            //Assert.Equal(Length.Medium, concrete.Width);
            //Assert.Equal(Color.Transparent, concrete.Color);
            //Assert.Equal(LineStyle.Dotted, concrete.Style);
        }

        [Fact]
        public void BorderTopLengthRedLegal()
        {
            var snippet = "border-top :  2px red ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-top", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderTopProperty>(property);
            var concrete = (BorderTopProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("2px rgb(255, 0, 0)", concrete.Value);
            //Assert.Equal(new Length(2f, Length.Unit.Px), concrete.Width);
            //Assert.Equal(Color.Red, concrete.Color);
            //Assert.Equal(LineStyle.None, concrete.Style);
        }

        [Fact]
        public void BorderBottomRgbLegal()
        {
            var snippet = "border-bottom :  rgb(255, 100, 0) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border-bottom", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderBottomProperty>(property);
            var concrete = (BorderBottomProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("rgb(255, 100, 0)", concrete.Value);
            //Assert.Equal(Length.Medium, concrete.Width);
            //Assert.Equal(Color.FromRgb(255, 100, 0), concrete.Color);
            //Assert.Equal(LineStyle.None, concrete.Style);
        }

        [Fact]
        public void BorderGrooveRgbLegal()
        {
            var snippet = "border :  GROOVE rgb(255, 100, 0) ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal("groove rgb(255, 100, 0)", concrete.Value.CssText);
            //Assert.Equal(Length.Medium, concrete.Width);
            //Assert.Equal(Color.FromRgb(255, 100, 0), concrete.Color);
            //Assert.Equal(LineStyle.Groove, concrete.Style);
        }

        [Fact]
        public void BorderInsetGreenLengthLegal()
        {
            var snippet = "border :  inset  green 3em ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("3em inset rgb(0, 128, 0)", concrete.Value);
            //Assert.Equal(new Length(3f, Length.Unit.Em), concrete.Width);
            //Assert.Equal(Color.Green, concrete.Color);
            //Assert.Equal(LineStyle.Inset, concrete.Style);
        }

        [Fact]
        public void BorderRedSolidLengthLegal()
        {
            var snippet = "border :  red  SOLID 1px ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            //Assert.Equal("red solid 1px", concrete.Value.CssText);
            //Assert.Equal(new Length(1f, Length.Unit.Px), concrete.Width);
            //Assert.Equal(Color.Red, concrete.Color);
            //Assert.Equal(LineStyle.Solid, concrete.Style);
        }

        [Fact]
        public void BorderLengthBlackDoubleLegal()
        {
            var snippet = "border :  0.5px black double ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("0.5px double rgb(0, 0, 0)", concrete.Value);
            //Assert.Equal(new Length(0.5f, Length.Unit.Px), concrete.Width);
            //Assert.Equal(Color.Black, concrete.Color);
            //Assert.Equal(LineStyle.Double, concrete.Style);
        }

        [Fact]
        public void BorderOutSetCurrentColor()
        {
            var snippet = "border: 1px outset currentColor";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px outset currentColor", concrete.Value);
            //Assert.Equal(new Length(1f, Length.Unit.Px), concrete.Width);
            //Assert.Equal(Color.Transparent, concrete.Color);
            //Assert.Equal(LineStyle.Outset, concrete.Style);
        }

        [Fact]
        public void BorderOutSetWithNoColor()
        {
            var snippet = "border: 1px outset";
            var property = ParseDeclaration(snippet);
            Assert.Equal("border", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<BorderProperty>(property);
            var concrete = (BorderProperty)property;
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("1px outset", concrete.Value);
            //Assert.Equal(new Length(1f, Length.Unit.Px), concrete.Width);
            //Assert.Equal(Color.Transparent, concrete.Color);
            //Assert.Equal(LineStyle.Outset, concrete.Style);
        }
    }
}








