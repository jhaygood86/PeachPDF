namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.IO;
    using System.Linq;
    using Xunit;
    public class CssSheetTests : CssConstructionFunctions
    {
        [Fact]
        public void CssSheetOnEofDuringRuleWithoutSemicolon()
        {
            var sheet = ParseStyleSheet(@"
h1 {
 color: red;
 font-weight: bold");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var h1 = sheet.Rules[0] as StyleRule;
            Assert.Equal("h1", h1.SelectorText);
            Assert.Equal("rgb(255, 0, 0)", h1.Style.Color);
            Assert.Equal("bold", h1.Style.FontWeight);
        }

        [Fact]
        public void CssSheet1WithDoubleMarkedCommentFromIssue93()
        {
            var sheet = ParseStyleSheet(@"
            /**special css**/
            .dis-none { display: none;}
            .dis { display: block; }
            /*common css*/
            .dis2 { display: block; }
            ");
            //var css = sheet.ToCss();
            Assert.Equal(3, sheet.Rules.Length);
            Assert.Equal(".dis-none { display: none }", sheet.Rules[0].Text);
            Assert.Equal(".dis { display: block }", sheet.Rules[1].Text);
            Assert.Equal(".dis2 { display: block }", sheet.Rules[2].Text);
        }

        [Fact]
        public void CssSheet2WithDoubleMarkedCommentFromIssue93()
        {
            var sheet = ParseStyleSheet(@"
            /**special css**/
            .dis-none { display: none;}
            .dis { display: block; }
            ");
            //var css = sheet.ToCss();
            Assert.Equal(2, sheet.Rules.Length);
            Assert.Equal(".dis-none { display: none }", sheet.Rules[0].Text);
            Assert.Equal(".dis { display: block }", sheet.Rules[1].Text);
        }

        [Fact]
        public void CssSheetSerializeListStyleNone()
        {
            const string cssSrc = ".T1 {list-style:NONE}";
            const string expected = ".T1 { list-style: none }";
            var stylesheet = ParseStyleSheet(cssSrc);
            var text = stylesheet.ToCss();
            Assert.Equal(expected, text);
        }

        [Fact]
        public void CssSheetSerializeBorder1pxOutset()
        {
            const string cssSrc = ".T2 { border:1px  outset }";
            const string expected = ".T2 { border: 1px outset }";
            var stylesheet = ParseStyleSheet(cssSrc);
            var text = stylesheet.ToCss();
            Assert.Equal(expected, text);
        }

        [Fact]
        public void CssSheetSerializeBorder1pxSolidWithColor()
        {
            const string cssSrc = "#rule1 { border: 1px solid #BBCCEB; border-top: none }";
            const string expected = "#rule1 { border-right: 1px solid rgb(187, 204, 235); border-bottom: 1px solid rgb(187, 204, 235); border-left: 1px solid rgb(187, 204, 235); border-top: none }";
            var stylesheet = ParseStyleSheet(cssSrc);
            var text = stylesheet.ToCss();
            Assert.Equal(expected, text);
        }

        [Fact]
        public void CssSheetSerializeBackgroundWithUrlPositionRepeatX()
        {
            const string cssSrc = "#rule2 { background:url(/_static/img/bx_tile.gif) top left repeat-x; }";
            const string expected = "#rule2 { background: url(\"/_static/img/bx_tile.gif\") top left repeat-x }";
            var stylesheet = ParseStyleSheet(cssSrc);
            var text = stylesheet.ToCss();
            Assert.Equal(expected, text);
        }

        [Fact]
        public void CssSheetIgnoreVendorPrefixes()
        {
            var css = @".something { 
  -o-border-radius: 5px;
  -webkit-border-radius: 5px;
  border-radius: 5px;
  display: -webkit-box;
  display: -webkit-flex;
  display: -ms-flexbox;
  display: flex;
  background: -webkit-linear-gradient(red, green);
  background: linear-gradient(red, green);
}";
            var stylesheet = ParseStyleSheet(css);
            Assert.Equal(1, stylesheet.Rules.Length);
            var style = stylesheet.Rules[0] as StyleRule;
            Assert.NotNull(style);
            Assert.Equal(13, style.Style.Length);
        }

        [Fact]
        public void CssSheetSimpleStyleRuleStringification()
        {
            var css = @"html { font-family: sans-serif }";
            var stylesheet = ParseStyleSheet(css);
            Assert.Equal(1, stylesheet.Rules.Length);
            var rule = stylesheet.Rules[0];
            Assert.IsType<StyleRule>(rule);
            Assert.Equal(css, rule.Text);
        }

        [Fact]
        public void CssSheetCloseStringsEndOfLine()
        {
            var sheet = ParseStyleSheet(@"p {
        color: green;
        font-family: 'Courier New Times
        color: red;
        color: green;
      }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("p", p.SelectorText);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetOnEofDuringRuleWithinString()
        {
            var sheet = ParseStyleSheet(@"
#something {
 content: 'hi there");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var id = sheet.Rules[0] as StyleRule;
            Assert.Equal("#something", id.SelectorText);
            Assert.Equal("\"hi there\"", id.Style.Content);
        }

        [Fact]
        public void CssSheetOnEofDuringAtMediaRuleWithinString()
        {
            var sheet = ParseStyleSheet(@"  @media screen {
    p:before { content: 'Hello");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<MediaRule>(sheet.Rules[0]);
            var media = sheet.Rules[0] as MediaRule;
            Assert.Equal("screen", media.Media.MediaText);
            Assert.Equal(1, media.Rules.Length);
            Assert.IsType<StyleRule>(media.Rules[0]);
            var p = media.Rules[0] as StyleRule;
            Assert.Equal("p::before", p.SelectorText);
            Assert.Equal("\"Hello\"", p.Style.Content);
        }

        [Fact]
        public void CssSheetIgnoreUnknownProperty()
        {
            var sheet = ParseStyleSheet(@"h1 { color: red; rotation: 70minutes }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var h1 = sheet.Rules[0] as StyleRule;
            Assert.Equal("h1", h1.SelectorText);
            Assert.Equal(1, h1.Style.Length);
            Assert.Equal("color", h1.Style[0]);
            Assert.Equal("rgb(255, 0, 0)", h1.Style.Color);
        }

        [Fact]
        public void CssSheetInvalidStatementRulesetUnexpectedAtKeyword()
        {
            var sheet = ParseStyleSheet(@"p @here {color: red}");
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void CssSheetInvalidStatementAtRuleUnexpectedAtKeyword()
        {
            var sheet = ParseStyleSheet(@"@foo @bar;");
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void CssSheetInvalidStatementRulesetUnexpectedRightBrace()
        {
            var sheet = ParseStyleSheet(@"}} {{ - }}");
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void CssSheetInvalidStatementRulesetUnexpectedRightBraceWithValidQualifiedRule()
        {
            var sheet = ParseStyleSheet(@"}} {{ - }}
#hi { color: green; }");
            Assert.Equal(1, sheet.Rules.Length);
            var style = sheet.Rules[0] as StyleRule;
            Assert.NotNull(style);
            Assert.Equal("#hi", style.SelectorText);
            Assert.Equal(1, style.Style.Length);
            Assert.Equal("rgb(0, 128, 0)", style.Style.Color);
        }

        [Fact]
        public void CssSheetInvalidStatementRulesetUnexpectedRightParenthesis()
        {
            var sheet = ParseStyleSheet(@") ( {} ) p {color: red }");
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void CssSheetInvalidStatementRulesetUnexpectedRightParenthesisWithValidQualifiedRule()
        {
            var sheet = ParseStyleSheet(@") {} p {color: green }");
            Assert.Equal(1, sheet.Rules.Length);
            var style = sheet.Rules[0] as StyleRule;
            Assert.NotNull(style);
            Assert.Equal("p", style.SelectorText);
            Assert.Equal(1, style.Style.Length);
            Assert.Equal("rgb(0, 128, 0)", style.Style.Color);
        }

        [Fact]
        public void CssSheetIgnoreUnknownAtRule()
        {
            var sheet = ParseStyleSheet(@"@three-dee {
  @background-lighting {
    azimuth: 30deg;
    elevation: 190deg;
  }
  h1 { color: red }
}
h1 { color: blue }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var h1 = sheet.Rules[0] as StyleRule;
            Assert.Equal("h1", h1.SelectorText);
            Assert.Equal(1, h1.Style.Length);
            Assert.Equal("color", h1.Style[0]);
            Assert.Equal("rgb(0, 0, 255)", h1.Style.Color);
        }

        [Fact]
        public void CssSheetKeepValidValueFloat()
        {
            var sheet = ParseStyleSheet(@"img { float: left }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var img = sheet.Rules[0] as StyleRule;
            Assert.Equal("img", img.SelectorText);
            Assert.Equal(1, img.Style.Length);
            Assert.Equal("float", img.Style[0]);
            Assert.Equal("left", img.Style.Float);
        }

        [Fact]
        public void CssSheetIgnoreInvalidValueFloat()
        {
            var sheet = ParseStyleSheet(@"img { float: left here }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var img = sheet.Rules[0] as StyleRule;
            Assert.Equal("img", img.SelectorText);
            Assert.Equal(0, img.Style.Length);
        }

        [Fact]
        public void CssSheetIgnoreInvalidValueBackground()
        {
            var sheet = ParseStyleSheet(@"img { background: ""red"" }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var img = sheet.Rules[0] as StyleRule;
            Assert.Equal("img", img.SelectorText);
            Assert.Equal(0, img.Style.Length);
        }

        [Fact]
        public void CssSheetIgnoreInvalidValueBorderWidth()
        {
            var sheet = ParseStyleSheet(@"img { border-width: 3 }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var img = sheet.Rules[0] as StyleRule;
            Assert.Equal("img", img.SelectorText);
            Assert.Equal(0, img.Style.Length);
        }

        [Fact]
        public void CssSheetWellformedDeclaration()
        {
            var sheet = ParseStyleSheet(@"p { color:green; }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetMalformedDeclarationMissingColon()
        {
            var sheet = ParseStyleSheet(@"p { color:green; color }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetMalformedDeclarationMissingColonWithRecovery()
        {
            var sheet = ParseStyleSheet(@"p { color:red;   color; color:green }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetMalformedDeclarationMissingValue()
        {
            var sheet = ParseStyleSheet(@"p { color:green; color: }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetMalformedDeclarationUnexpectedTokens()
        {
            var sheet = ParseStyleSheet(@"p { color:green; color{;color:maroon} }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssSheetMalformedDeclarationUnexpectedTokensWithRecovery()
        {
            var sheet = ParseStyleSheet(@"p { color:red;   color{;color:maroon}; color:green }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var p = sheet.Rules[0] as StyleRule;
            Assert.Equal("p", p.SelectorText);
            Assert.Equal(1, p.Style.Length);
            Assert.Equal("color", p.Style[0]);
            Assert.Equal("rgb(0, 128, 0)", p.Style.Color);
        }

        [Fact]
        public void CssCreateValueListConformal()
        {
            var valueString = "24px 12px 6px";
            var list = ParseValue(valueString);
            Assert.Equal(5, list.Count);
            Assert.Equal("24px", list[0].ToValue());
            Assert.Equal(" ", list[1].ToValue());
            Assert.Equal("12px", list[2].ToValue());
            Assert.Equal(" ", list[3].ToValue());
            Assert.Equal("6px", list[4].ToValue());
        }

        [Fact]
        public void CssCreateValueListNonConformal()
        {
            var valueString = "  24px  12px 6px  13px ";
            var list = ParseValue(valueString);
            Assert.Equal(7, list.Count);
            Assert.Equal("24px", list[0].ToValue());
            Assert.Equal(" ", list[1].ToValue());
            Assert.Equal("12px", list[2].ToValue());
            Assert.Equal(" ", list[3].ToValue());
            Assert.Equal("6px", list[4].ToValue());
            Assert.Equal(" ", list[5].ToValue());
            Assert.Equal("13px", list[6].ToValue());
        }

        [Fact]
        public void CssCreateValueListEmpty()
        {
            var valueString = "";
            var value = ParseValue(valueString);
            Assert.Null(value);
        }

        [Fact]
        public void CssCreateValueListSpaces()
        {
            var valueString = "  ";
            var value = ParseValue(valueString);
            Assert.Null(value);
        }

        [Fact]
        public void CssCreateValueListIllegal()
        {
            var valueString = " , ";
            var list = ParseValue(valueString);
            Assert.Equal(1, list.Count);
        }

        [Fact]
        public void CssCreateMultipleValues()
        {
            var valueString = "Arial, Verdana, Helvetica, Sans-Serif";
            var list = ParseValue(valueString);
            Assert.Equal(10, list.Count);
            Assert.Equal("Arial", list[0].Data);
            Assert.Equal("Verdana", list[3].Data);
            Assert.Equal("Helvetica", list[6].Data);
            Assert.Equal("Sans-Serif", list[9].Data);
        }

        [Fact]
        public void CssCreateMultipleValueLists()
        {
            var valueString = "Arial 10pt bold, Verdana 12pt italic";
            var list = ParseValue(valueString);
            Assert.Equal(12, list.Count);
            Assert.Equal("Arial", list[0].ToValue());
            Assert.Equal("Verdana", list[7].ToValue());
            Assert.Equal("10pt", list[2].ToValue());
            Assert.Equal("12pt", list[9].ToValue());
            Assert.Equal("bold", list[4].ToValue());
            Assert.Equal("italic", list[11].ToValue());
        }

        [Fact]
        public void CssCreateMultipleValuesNonConformal()
        {
            var valueString = "  Arial  ,  Verdana  ,Helvetica,Sans-Serif   ";
            var list = ParseValue(valueString);
            Assert.Equal(10, list.Count);
            Assert.Equal("Arial", list[0].ToValue());
            Assert.Equal("Verdana", list[3].ToValue());
            Assert.Equal("Helvetica", list[6].ToValue());
            Assert.Equal("Sans-Serif", list[9].ToValue());
        }

        [Fact]
        public void CssColorBlack()
        {
            var valueString = "#000000";
            var value = ParseValue(valueString);
            Assert.NotNull(value);
        }

        [Fact]
        public void CssColorRed()
        {
            var valueString = "#FF0000";
            var value = ParseValue(valueString);
            Assert.NotNull(value);
        }

        [Fact]
        public void CssColorMixedShort()
        {
            var valueString = "#07C";
            var value = ParseValue(valueString);
            Assert.NotNull(value);
        }

        [Fact]
        public void CssColorGreenShort()
        {
            var valueString = "#00F";
            var value = ParseValue(valueString);
            Assert.NotNull(value);
        }

        [Fact]
        public void CssColorRedShort()
        {
            var valueString = "#F00";
            var value = ParseValue(valueString);
            Assert.NotNull(value);
        }

        [Fact]
        public void CssRgbaFunction()
        {
            var names = new[] { "border-top-color", "border-right-color", "border-bottom-color", "border-left-color" };
            var decls = ParseDeclarations("border-color: rgba(82, 168, 236, 0.8)");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);
                Assert.False(decl.IsImportant);

                //var property = (CssBorderPartColorProperty)decl;
                //var color = property.Color;
                //Assert.Equal(new Color(82, 168, 236, 0.8f), color);
            }
        }

        [Fact]
        public void CssMarginAll()
        {
            var names = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" };
            var decls = ParseDeclarations("margin: 20px;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);
                Assert.False(decl.IsImportant);
                Assert.Equal("20px", decl.Value);
            }
        }

        [Fact]
        public void CssMarginAllImportant()
        {
            var names = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" };
            var decls = ParseDeclarations("margin: 20px !important;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);
                Assert.True(decl.IsImportant);
                Assert.Equal("20px", decl.Value);
            }
        }

        [Fact]
        public void CssMarginImportantShorhandFollowedByNotImportantLonghand()
        {
            var names = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" };
            var decls = ParseDeclarations("margin: 5px !important; margin-left: 3px;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);
                Assert.True(decl.IsImportant);
                Assert.Equal("5px", decl.Value);
            }
        }

        [Fact]
        public void CssMarginImportantLonghandFollowedByNotImportantShorthand()
        {
            var names = new[] { "margin-left", "margin-top", "margin-right", "margin-bottom" };
            var decls = ParseDeclarations("margin-left: 5px !important; margin: 3px;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);

                if (i == 0)
                {
                    Assert.True(decl.IsImportant);
                    Assert.Equal("5px", decl.Value);
                }
                else
                {
                    Assert.False(decl.IsImportant);
                    Assert.Equal("3px", decl.Value);
                }
            }
        }

        [Fact]
        public void CssMarginNotImportantShorhandFollowedByImportantLonghand()
        {
            var names = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" };
            var decls = ParseDeclarations("margin: 5px; margin-left: 3px !important;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);

                if (i < 3)
                {
                    Assert.False(decl.IsImportant);
                    Assert.Equal("5px", decl.Value);
                }
                else
                {
                    Assert.True(decl.IsImportant);
                    Assert.Equal("3px", decl.Value);
                }
            }
        }

        [Fact]
        public void CssMarginNotImportantLonghandFollowedByImportantShorthand()
        {
            var names = new[] { "margin-top", "margin-right", "margin-bottom", "margin-left" };
            var decls = ParseDeclarations("margin-left: 5px; margin: 3px !important;");
            Assert.NotNull(decls);
            Assert.Equal(4, decls.Length);

            for (int i = 0; i < decls.Length; i++)
            {
                var propertyName = decls[i];
                var decl = decls.GetProperty(propertyName);
                Assert.Equal(names[i], decl.Name);
                Assert.Equal(propertyName, decl.Name);
                Assert.True(decl.IsImportant);
                Assert.Equal("3px", decl.Value);
            }
        }

        [Fact]
        public void CssSeveralFontFamily()
        {
            var prop = ParseDeclaration("font-family: \"Helvetica Neue\", Helvetica, Arial, sans-serif");
            Assert.Equal("font-family", prop.Name);
            Assert.False(prop.IsImportant);
            Assert.Equal("\"Helvetica Neue\", Helvetica, Arial, sans-serif", prop.Value);
        }

        [Fact]
        public void CssFontWithSlashAndContent()
        {
            var decl = ParseDeclarations("font: bold 1em/2em monospace; content: \" (\" attr(href) \")\"");
            Assert.NotNull(decl);
            Assert.Equal(8, decl.Length);

            Assert.Equal("bold 1em / 2em monospace", decl.GetPropertyValue("font"));

            var content = decl.GetProperty("content");
            Assert.Equal("content", content.Name);
            Assert.False(content.IsImportant);
            Assert.Equal("\" (\" attr(href) \")\"", content.Value);
        }

        [Fact]
        public void CssBackgroundWebkitGradient()
        {
            var background = ParseDeclaration("background: -webkit-gradient(linear, left top, left bottom, color-stop(0%, #FFA84C), color-stop(100%, #FF7B0D))");
            Assert.NotNull(background);
            Assert.Equal("background", background.Name);
            Assert.False(background.IsImportant);
            Assert.False(background.HasValue);
        }

        [Fact]
        public void CssBackgroundColorRgba()
        {
            var background = ParseDeclaration("background-color: rgba(255, 123, 13, 1)");
            Assert.Equal("background-color", background.Name);
            Assert.False(background.IsImportant);
            Assert.Equal("rgba(255, 123, 13, 1)", background.Value);
        }

        [Fact]
        public void CssFontWithFraction()
        {
            var font = ParseDeclaration("font:bold 40px/1.13 'PT Sans Narrow', sans-serif");
            Assert.Equal("font", font.Name);
            Assert.False(font.IsImportant);
        }

        [Fact]
        public void TextShadow()
        {
            var textShadow = ParseDeclaration("text-shadow: 0 0 10px #000");
            Assert.Equal("text-shadow", textShadow.Name);
            Assert.False(textShadow.IsImportant);
        }

        [Fact]
        public void CssBackgroundWithImage()
        {
            var background = ParseDeclaration("background:url(../images/ribbon.svg) no-repeat");
            Assert.Equal("background", background.Name);
            Assert.False(background.IsImportant);
        }

        [Fact]
        public void CssContentWithCounter()
        {
            var content = ParseDeclaration("content:counter(paging, decimal-leading-zero)");
            Assert.Equal("content", content.Name);
            Assert.False(content.IsImportant);
        }

        [Fact]
        public void CssBackgroundColorRgb()
        {
            var backgroundColor = ParseDeclaration("background-color: rgb(245, 0, 111)");
            Assert.Equal("background-color", backgroundColor.Name);
            Assert.False(backgroundColor.IsImportant);
        }

        [Fact]
        public void CssImportSheet()
        {
            var rule = "@import url(fonts.css);";
            var decl = ParseRule(rule);
            Assert.NotNull(decl);
            Assert.IsType<ImportRule>(decl);
            var importRule = (ImportRule)decl;
            Assert.Equal("fonts.css", importRule.Href);
        }

        [Fact]
        public void CssContentEscaped()
        {
            var content = ParseDeclaration("content:'\005E'");
            Assert.Equal("content", content.Name);
            Assert.False(content.IsImportant);
        }

        [Fact]
        public void CssContentCounter()
        {
            var content = ParseDeclaration("content:counter(list)'.'");
            Assert.Equal("content", content.Name);
            Assert.False(content.IsImportant);
            //Assert.Equal(CssValueType.List, content.Value.Type);
        }

        [Fact]
        public void CssTransformTranslate()
        {
            var transform = ParseDeclaration("transform:translateY(-50%)");
            Assert.Equal("transform", transform.Name);
            Assert.False(transform.IsImportant);
        }

        [Fact]
        public void CssBoxShadowMultiline()
        {
            var boxShadow = ParseDeclaration(@"
        box-shadow:
			0 0 0 10px rgba(60, 61, 64, 0.6),
			0 0 50px #3C3D40;");
            Assert.Equal("box-shadow", boxShadow.Name);
            Assert.False(boxShadow.IsImportant);
        }

        [Fact]
        public void CssDisplayBlock()
        {
            var display = ParseDeclaration("display:block");
            Assert.Equal("display", display.Name);
            Assert.False(display.IsImportant);
            Assert.Equal("block", display.Value);
        }

        [Fact]
        public void CssSheetWithDataUrlAsBackgroundImage()
        {
            var sheet = ParseStyleSheet(".App_Header_ .logo { background-image: url(\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEcAAAAcCAMAAAAEJ1IZAAAABGdBTUEAALGPC/xhBQAAVAI/VAI/VAI/VAI/VAI/VAI/VAAAA////AI/VRZ0U8AAAAFJ0Uk5TYNV4S2UbgT/Gk6uQt585w2wGXS0zJO2lhGttJK6j4YqZSobH1AAAAAElFTkSuQmCC\"); background-size: 71px 28px; background-position: 0 19px; width: 71px; }");
            Assert.NotNull(sheet);
            Assert.Equal(1, sheet.Rules.Length);
            var rule = sheet.Rules[0] as StyleRule;
            Assert.NotNull(rule);
            Assert.Equal(4, rule.Style.Length);
            Assert.Equal(".App_Header_ .logo", rule.SelectorText);
            var decl = rule.Style;
            Assert.Equal("url(\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEcAAAAcCAMAAAAEJ1IZAAAABGdBTUEAALGPC/xhBQAAVAI/VAI/VAI/VAI/VAI/VAI/VAAAA////AI/VRZ0U8AAAAFJ0Uk5TYNV4S2UbgT/Gk6uQt585w2wGXS0zJO2lhGttJK6j4YqZSobH1AAAAAElFTkSuQmCC\")", decl.BackgroundImage);
            Assert.Equal("71px 28px", decl.BackgroundSize);
            Assert.Equal("0 19px", decl.BackgroundPosition);
            Assert.Equal("71px", decl.Width);
        }

        [Fact]
        public void CssSheetFromStreamWeirdBytesLeadingToInfiniteLoop()
        {
            var bs = new byte[8];
            bs[0] = 239;
            bs[1] = 187;
            bs[2] = 191;
            bs[3] = 117;
            bs[4] = 43;
            bs[5] = 63;
            bs[6] = 63;
            bs[7] = 63;

            using (var memoryStream = new MemoryStream(bs, false))
            {
                var sheet = memoryStream.ToCssStylesheet();
            }
        }

        [Fact]
        public void CssSheetFromStreamOnlyZerosAvailable()
        {
            var bs = new byte[7180];

            using (var memoryStream = new MemoryStream(bs, false))
            {
                var sheet = memoryStream.ToCssStylesheet();
                Assert.NotNull(sheet);
                Assert.Equal(0, sheet.Rules.Length);
            }
        }

        [Fact]
        public void CssSheetFromStringWithQuestionMarksLeadingToInfiniteLoop()
        {
            var sheet = "U+???\0".ToCssStylesheet();
            Assert.NotNull(sheet);
            Assert.Equal(0, sheet.Rules.Length);
        }

        [Fact]
        public void CssDefaultSheetSupportsRoundTripping()
        {
            var originalSourceCode = @"p.info {
	font-family: arial, sans-serif;
	line-height: 150%;
	margin-left: 2em;
	padding: 1em;
	border: 3px solid red;
	background-color: #f89;
	display: inline-block;
}
p.info span {
	font-weight: bold;
}
p.info span::after {
	content: ': ';
}";
            var initialSheet = originalSourceCode.ToCssStylesheet();
            var initialSourceCode = initialSheet.ToCss();
            var finalSheet = initialSourceCode.ToCssStylesheet();
            var finalSourceCode = finalSheet.ToCss();
            Assert.Equal(initialSourceCode, finalSourceCode);
            Assert.Equal(initialSheet.Rules.Length, finalSheet.Rules.Length);
        }

        [Fact]
        public void CssParseSheetWithStyleMediaAndStyleRule()
        {
            var sheet = ParseStyleSheet(@".mobile,.tablet{display:none;} @media only screen and(max-width:51.875em){.tablet{display:block;}} .disp {display:block;}");
            Assert.Equal(3, sheet.Rules.Length);
            Assert.Equal(RuleType.Style, sheet.Rules[0].Type);
            Assert.Equal(RuleType.Media, sheet.Rules[1].Type);
            Assert.Equal(RuleType.Style, sheet.Rules[2].Type);
        }

        [Fact]
        public void CssParseSheetWithMediaAndTwoStyleRules()
        {
            var sheet = ParseStyleSheet(@"@media only screen and(max-width:51.875em){.tablet{display:block;}} .mobile,.tablet{display:none;} .disp {display:block;}");
            Assert.Equal(3, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
            Assert.Equal(RuleType.Style, sheet.Rules[1].Type);
            Assert.Equal(RuleType.Style, sheet.Rules[2].Type);
        }

        [Fact]
        public void CssParseSheetWithTwoStyleAndMediaRule()
        {
            var sheet = ParseStyleSheet(@".mobile,.tablet{display:none;} .disp {display:block;} @media only screen and(max-width:51.875em){.tablet{display:block;}}");
            Assert.Equal(3, sheet.Rules.Length);
            Assert.Equal(RuleType.Style, sheet.Rules[0].Type);
            Assert.Equal(RuleType.Style, sheet.Rules[1].Type);
            Assert.Equal(RuleType.Media, sheet.Rules[2].Type);
        }

        [Fact(Timeout = 1000)]
        public void CssParseSheetWithAtAndCommentDoesNotTakeForever()
        {
            var sheet = ParseStyleSheet(@"
            h3 {color: yellow;
            @media print {
                h3 {color: black; }
                }
            ");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Style, sheet.Rules[0].Type);
        }

        [Fact(Timeout = 1000)]
        public void CssParseSheetWithAtAndCommentDoesNotTakeForever2()
        {
            var sheet = ParseStyleSheet(@"
:root {
    --layout: {
    }
    --layout-horizontal: {
        @apply (--layout);
    }
}");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Style, sheet.Rules[0].Type);
        }

        [Fact(Timeout = 1000)]
        public void CssParseSheetWithAtAndCommentDoesNotTakeForever3()
        {
            var sheet = ParseStyleSheet(@"
@media (max-width: 991px) {
    body {
        background-color: #013668;
    }
    ;
}

@media (max-width: 991px) {
    body {
        background: #FFF;
    }
}");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.Equal(RuleType.Media, sheet.Rules[0].Type);
        }

        [Fact]
        public void CssParseImportStatementWithNoMediaTextFollowedByStyle()
        {
            var src = "@import url(import3.css); p { color : #f00; }";
            var sheet = ParseStyleSheet(src);
            Assert.Equal(2, sheet.Rules.Length);
            var import = sheet.Rules[0] as ImportRule;
            var style = sheet.Rules[1] as StyleRule;
            Assert.NotNull(import);
            Assert.NotNull(style);
            Assert.Equal(0, import.Media.Length);
            Assert.Equal("", import.Media.MediaText);
            Assert.Equal("import3.css", import.Href);
            Assert.Equal("p", style.Selector.Text);
            Assert.Equal(1, style.Style.Length);
        }

        [Fact]
        public void CssParseMediaRuleWithInvalidMediumEntities()
        {
            var src = "@media only screen and (min--moz-device-pixel-ratio:1.5),only screen and (-o-min-device-pixel-ratio:3/2),only screen and (-webkit-min-device-pixel-ratio:1.5),only screen and (min-device-pixel-ratio:1.5){.favicon{background-image:url('../img/favicons-sprite32.png?v=1b9547cf9cee3350a5b4875951e3e552');background-size:16px 5634px}}";
            var sheet = ParseStyleSheet(src);
            Assert.Equal(1, sheet.Rules.Length);
            var media = sheet.Rules[0] as MediaRule;
            Assert.NotNull(media);
            Assert.Equal(1, media.Media.Length);
            Assert.Equal(1, media.Rules.Length);
            Assert.Equal("only screen and (min-device-pixel-ratio: 1.5)", media.ConditionText);
        }

        [Fact]
        public void CssParseStyleWithInvalidSurrogatePair()
        {
            var src = @"span.berschrift2Zchn
{mso-style-name:""\00DCberschrift 2 Zchn"";
mso-style-priority:9;
mso-style-link:""\00DCberschrift 2"";
font-family:""Cambria"",""serif"";
color:#4F81BD;
font-weight:bold;}";
            var sheet = ParseStyleSheet(src);
            Assert.Equal(1, sheet.Rules.Length);
            var style = sheet.Rules[0] as StyleRule;
            Assert.NotNull(style);
            Assert.Equal("span.berschrift2Zchn", style.SelectorText);
            Assert.Equal(3, style.Style.Length);
        }

        [Fact]
        public void CssParseMsViewPortWithoutOptions()
        {
            var css = "@-ms-viewport{width:device-width} .dsip { display: block; }";
            var doc = ParseStyleSheet(css);
            var result = doc.ToCss();
            Assert.Equal(".dsip { display: block }", result);
        }

        [Fact]
        public void CssParseMsViewPortWithUnknownRules()
        {
            var css = "@-ms-viewport{width:device-width} .dsip { display: block; }";
            var doc = ParseStyleSheet(css, true, true, true, true);
            var result = doc.ToCss();
            Assert.Equal($"@-ms-viewport{{width:device-width}}{Environment.NewLine}.dsip {{ display: block }}", result);
        }

        [Fact]
        public void CssParseMediaAndMsViewPortWithoutOptions()
        {
            var css = "@media screen and (max-width: 400px) {  @-ms-viewport { width: 320px; }  }  .dsip { display: block; }";
            var doc = ParseStyleSheet(css);
            var result = doc.ToCss();
            Assert.Equal($"@media screen and (max-width: 400px) {{ }}{Environment.NewLine}.dsip {{ display: block }}", result);
        }

        [Fact]
        public void CssParseMediaAndMsViewPortWithUnknownRules()
        {
            var css = "@media screen and (max-width: 400px) {  @-ms-viewport { width: 320px; }  }  .dsip { display: block; }";
            var doc = ParseStyleSheet(css, true, true, true, true);
            var result = doc.ToCss();
            Assert.Equal($"@media screen and (max-width: 400px) {{ @-ms-viewport {{ width: 320px; }} }}{Environment.NewLine}.dsip {{ display: block }}", result);
        }

        [Fact]
        public void CssStyleSheetInsertAndDeleteShouldWork()
        {
            var parser = new StylesheetParser();
            var s = new Stylesheet(parser);
            Assert.Equal(0, s.Rules.Length);

            s.Insert("a {color: blue}", 0);
            Assert.Equal(1, s.Rules.Length);

            s.Insert("a *:first-child, a img {border: none}", 1);
            Assert.Equal(2, s.Rules.Length);

            s.RemoveAt(1);
            Assert.Equal(1, s.Rules.Length);

            s.RemoveAt(0);
            Assert.Equal(0, s.Rules.Length);
        }

        [Fact]
        public void CssStyleSheetShouldIgnoreHtmlCommentTokens()
        {
            var parser = new StylesheetParser();
            var source = "<!-- body { font-family: Verdana } div.hidden { display: none } -->";
            var sheet = parser.Parse(source);
            Assert.Equal(2, sheet.Rules.Length);

            Assert.Equal(RuleType.Style, sheet.Rules[0].Type);
            var body = sheet.Rules[0] as StyleRule;
            Assert.Equal("body", body.SelectorText);
            Assert.Equal(1, body.Style.Length);
            Assert.Equal("Verdana", body.Style.FontFamily);

            Assert.Equal(RuleType.Style, sheet.Rules[1].Type);
            var div = sheet.Rules[1] as StyleRule;
            Assert.Equal("div.hidden", div.SelectorText);
            Assert.Equal(1, div.Style.Length);
            Assert.Equal("none", div.Style.Display);
        }

        [Fact]
        public void CssStyleSheetInsertShouldSetParentStyleSheetCorrectly()
        {
            var parser = new StylesheetParser();
            var s = new Stylesheet(parser);
            s.Insert("a {color: blue}", 0);
            Assert.Equal(s, s.Rules[0].Owner);
        }

        [Fact]
        public void CssStyleSheetWithoutCommentsButStoringTrivia()
        {
            var parser = new StylesheetParser();
            const string source = ".foo { color: red; } @media print { #myid { color: green; } }";
            var sheet = parser.Parse(source);
            var comments = sheet.GetComments();
            Assert.Empty(comments);
        }

        [Fact]
        public void CssStyleSheetWithCommentInDeclaration()
        {
            var parser = new StylesheetParser(preserveComments: true);
            const string source = ".foo { /*test*/ color: red;/*test*/ } @media print { #myid { color: green; } }";
            var sheet = parser.Parse(source);
            var comments = sheet.GetComments();
            Assert.Equal(2, comments.Count());

            foreach (var comment in comments)
            {
                Assert.Equal("test", comment.Data);
            }
        }

        [Fact]
        public void CssStyleSheetWithCommentInRule()
        {
            var parser = new StylesheetParser(preserveComments: true);
            const string source = ".foo { color: red; } @media print { /*test*/ #myid { color: green; } /*test*/ }";
            var sheet = parser.Parse(source);
            var comments = sheet.GetComments();
            Assert.Equal(2, comments.Count());

            foreach (var comment in comments)
            {
                Assert.Equal("test", comment.Data);
            }
        }

        [Fact]
        public void CssStyleSheetWithCommentInMedia()
        {
            var parser = new StylesheetParser(preserveComments: true);
            var source = ".foo { color: red; } @media all /*test*/ and /*test*/ (min-width: 701px) /*test*/ { #myid { color: green; } }";
            var sheet = parser.Parse(source);
            var comments = sheet.GetComments();
            Assert.Equal(3, comments.Count());

            foreach (var comment in comments)
            {
                Assert.Equal("test", comment.Data);
            }
        }

        [Fact]
        public void CssStyleSheetSimpleRoundtrip()
        {
            var parser = new StylesheetParser(preserveComments: true);
            const string source = ".foo { color: red; } @media all /*test*/ and /*test*/ (min-width: 701px) /*test*/ { #myid { color: green; } }";
            var sheet = parser.Parse(source);
            var roundtrip = sheet.StylesheetText.Text;
            Assert.Equal(source, roundtrip);
        }


        [Fact]
        public void CssStyleSheetSelectorsGetAll()
        {
            var parser = new StylesheetParser();

            const string source = ".foo { } #bar { } @media all { div { } a > b { } @media print { script[type] { } } }";
            var sheet = parser.Parse(source);
            var roundtrip = sheet.StylesheetText.Text;
            Assert.Equal(source, roundtrip);
            var selectors = sheet.GetAll<ISelector>();
            Assert.Equal(5, selectors.Count());
            var mediaRules = sheet.GetAll<MediaRule>();
            Assert.Equal(2, mediaRules.Count());
            var descendentSelector = selectors.Skip(3).First();
            Assert.Equal("a>b", descendentSelector.Text);
            Assert.Equal("a > b ", descendentSelector.StylesheetText.Text);
        }

        [Fact]
        public void CssColorFunctionsMixAllShouldWork()
        {
            var parser = new StylesheetParser();
            const string source = @"
.rgbNumber { color: rgb(255, 128, 0); }
.rgbPercent { color: rgb(100%, 50%, 0%); }
.rgbaNumber { color: rgba(255, 128, 0, 0.0); }
.rgbaPercent { color: rgba(100%, 50%, 0%, 0.0); }
.hsl { color: hsl(120, 100%, 50%); }
.hslAngle { color: hsl(120deg, 100%, 50%); }
.hsla { color: hsla(120, 100%, 50%, 0.25); }
.hslaAngle { color: hsla(120deg, 100%, 50%, 0.25); }
.grayNumber { color: gray(128); }
.grayPercent { color: gray(50%); }
.grayPercentAlpha { color: gray(50%, 0.5); }
.hwb { color: hwb(120, 60%, 20%); }
.hwbAngle { color: hwb(120deg, 60%, 20%); }
.hwbAlpha { color: hwb(120, 10%, 50%, 0.5); }
.hwbAngleAlpha { color: hwb(120deg, 10%, 50%, 0.5); }";
            var sheet = parser.Parse(source);
            Assert.Equal(15, sheet.Rules.Length);

            var rgbNumber = (sheet.Rules[0] as StyleRule).Style.Color;
            var rgbPercent = (sheet.Rules[1] as StyleRule).Style.Color;
            var rgbaNumber = (sheet.Rules[2] as StyleRule).Style.Color;
            var rgbaPercent = (sheet.Rules[3] as StyleRule).Style.Color;
            var hsl = (sheet.Rules[4] as StyleRule).Style.Color;
            var hslAngle = (sheet.Rules[5] as StyleRule).Style.Color;
            var hsla = (sheet.Rules[6] as StyleRule).Style.Color;
            var hslaAngle = (sheet.Rules[7] as StyleRule).Style.Color;
            var grayNumber = (sheet.Rules[8] as StyleRule).Style.Color;
            var grayPercent = (sheet.Rules[9] as StyleRule).Style.Color;
            var grayPercentAlpha = (sheet.Rules[10] as StyleRule).Style.Color;
            var hwb = (sheet.Rules[11] as StyleRule).Style.Color;
            var hwbAngle = (sheet.Rules[12] as StyleRule).Style.Color;
            var hwbAlpha = (sheet.Rules[13] as StyleRule).Style.Color;
            var hwbAngleAlpha = (sheet.Rules[14] as StyleRule).Style.Color;

            Assert.NotNull(rgbNumber);
            Assert.NotNull(rgbPercent);
            Assert.NotNull(rgbaNumber);
            Assert.NotNull(rgbaPercent);
            Assert.NotNull(hsl);
            Assert.NotNull(hslAngle);
            Assert.NotNull(hsla);
            Assert.NotNull(hslaAngle);
            Assert.NotNull(grayNumber);
            Assert.NotNull(grayPercent);
            Assert.NotNull(grayPercentAlpha);
            Assert.NotNull(hwb);
            Assert.NotNull(hwbAngle);
            Assert.NotNull(hwbAlpha);
            Assert.NotNull(hwbAngleAlpha);
        }

        [Fact]
        public void ShouldBeAbleToPreserveDuplicateProperties()
        {
            var sheet = ParseStyleSheet(@"
h1 {
 color: red;
 color: some-invalid-color;",
            tolerateInvalidValues: true,
            preserveDuplicateProperties: true);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var h1 = sheet.Rules[0] as StyleRule;
            Assert.Equal("h1", h1.SelectorText);
            var props = h1.Style.Children.OfType<Property>().ToList();
            Assert.Equal("rgb(255, 0, 0)", props[0].Value);
            Assert.Equal("some-invalid-color", props[1].Value);
        }

        [Fact]
        public void Parse_ZIndex_Out_Of_Range()
        {
            var sheet = ParseStyleSheet(".style{ z-index: 99999999999999999;}");
        }

        [Fact]
        public void CanHandleGreaterThanSelectorWithNoFollowingSpace()
        {
            var sheet = ParseStyleSheet(@"#collapse-button >#icon{ }");
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<StyleRule>(sheet.Rules[0]);
            var rule = sheet.Rules[0] as StyleRule;
            Assert.IsType<ComplexSelector>(rule.Selector);
            var selector = (ComplexSelector)rule.Selector;

            var parts = selector.ToList();
            Assert.Equal(2, parts.Count());
            Assert.IsType<IdSelector>(parts[0].Selector);
            Assert.IsType<IdSelector>(parts[1].Selector);
        }
    }
}







