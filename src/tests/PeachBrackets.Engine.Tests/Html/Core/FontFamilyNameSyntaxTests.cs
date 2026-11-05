using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A font family name can be written as a string or as a sequence of identifiers, and an identifier can contain escapes (CSS Fonts 4, "font-family
    /// name syntax"; escapes are resolved when the style sheet is tokenized, and the identifiers of an unquoted name are joined by single spaces). A
    /// site that minifies its style sheets writes <c>font-family: Source Sans\ 3</c>; it must name the same family as <c>"Source Sans 3"</c>, whether
    /// that is how an <c>@font-face</c> declares it or how a rule asks for it.
    /// </summary>
    public class FontFamilyNameSyntaxTests
    {
        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;

            foreach (var child in box.Boxes)
            {
                if (FindByTag(child, tag) is { } found)
                    return found;
            }

            return null;
        }

        // The family of the font the paragraph's text is set in.
        private static async Task<string> FamilyOfParagraphAsync(string faceName, string usedName)
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: {faceName}; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); }}
body {{ font-family: {usedName}, 'Courier New', monospace; font-size: 14pt; }}
</style></head>
<body><p>Hello</p></body>
</html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var p = FindByTag(container.Root!, "p")!;
            return p.ActualFont.Typeface!.FamilyName;
        }

        [Theory]
        [InlineData("'Fancy Face'", "'Fancy Face'")]
        [InlineData("Fancy~ Face", "'Fancy Face'")]
        [InlineData("'Fancy Face'", "Fancy~ Face")]
        [InlineData("Fancy~ Face", "Fancy~ Face")]
        [InlineData("Fancy Face", "'Fancy Face'")]
        [InlineData("'Fancy Face'", "Fancy Face")]
        [InlineData("Fancy Face", "Fancy Face")]
        [InlineData("Fancy   Face", "Fancy~ Face")]
        [InlineData("FANCY face", "'Fancy Face'")]
        public async Task EveryWayOfWritingTheName_SelectsTheDeclaredFace(string faceName, string usedName)
        {
            var expected = TypefaceFixtures.FamilyNameOf(BundledFonts.Ttf);

            // "~" stands for a backslash, which is awkward to write in a test's data: "Fancy~ Face" is the escaped-space form, Fancy\ Face.
            var backslash = ((char)92).ToString();
            Assert.Equal(expected, await FamilyOfParagraphAsync(faceName.Replace("~", backslash), usedName.Replace("~", backslash)));
        }

        [Fact]
        public async Task ANameThatIsNotDeclared_FallsBackAsBefore()
        {
            var expected = TypefaceFixtures.FamilyNameOf(BundledFonts.Ttf);

            Assert.NotEqual(expected, await FamilyOfParagraphAsync("'Fancy Face'", "'Other Face'"));
        }
    }
}
