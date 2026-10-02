using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>SVG text honouring the whole <c>font-family</c> list, including per-character fallback when the first family lacks a glyph.</summary>
    public class SvgTextFontFallbackTests
    {
        private static async Task<(TestRecordingGraphics G, string Nabla, string Recursive)> Render(string familyAttr, string text, string extra = "")
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var nabla = TypefaceFixtures.FamilyNameOf(BundledFonts.Nabla);
            var recursive = TypefaceFixtures.FamilyNameOf(BundledFonts.Recursive);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Nabla, nabla);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Recursive, recursive);

            var family = familyAttr.Replace("NABLA", nabla).Replace("RECURSIVE", recursive);
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><text x="10" y="50" font-size="30" font-family="{family}" {extra}>{text}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return (g, nabla, recursive);
        }

        [Fact]
        public async Task UnknownFirstFamily_FallsThroughToNextFamilyInList()
        {
            var (g, _, _) = await Render("NoSuchFamilyXyz, RECURSIVE", "abc");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("abc", draw.Text);
            Assert.True(draw.Font.HasGlyph(new System.Text.Rune('b')));
        }

        [Fact]
        public async Task CharacterMissingFromFirstFamily_UsesNextFamily()
        {
            // Nabla only covers A E L P T; "g" must come from Recursive, in its own draw call with its own font.
            var (g, _, _) = await Render("NABLA, RECURSIVE", "Ag");

            Assert.Equal(["A", "g"], g.DrawStringCalls.Select(c => c.Text).ToArray());
            Assert.NotSame(g.DrawStringCalls[0].Font, g.DrawStringCalls[1].Font);
            Assert.True(g.DrawStringCalls[1].Font.HasGlyph(new System.Text.Rune('g')));
        }

        [Fact]
        public async Task CharactersCoveredByFirstFamily_StayInOneBatch()
        {
            var (g, _, _) = await Render("NABLA, RECURSIVE", "PALETTE");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("PALETTE", draw.Text);
        }

        [Fact]
        public async Task QuotedFamilyNames_AreUnquoted()
        {
            var (g, _, _) = await Render("'NoSuchFamilyXyz', 'RECURSIVE'", "abc");

            Assert.Equal("abc", Assert.Single(g.DrawStringCalls).Text);
        }

        [Fact]
        public async Task CombiningMarkAndVariationSelector_StayWithTheirBase()
        {
            // Nabla has no U+0301 or U+FE0F; they must not be sent to another font on their own, which would split the cluster.
            var (g, _, _) = await Render("NABLA, RECURSIVE", "A\u0301P\uFE0F");

            Assert.Equal("A\u0301P\uFE0F", string.Concat(g.DrawStringCalls.Select(c => c.Text)));
            Assert.Single(g.DrawStringCalls.Select(c => c.Font).Distinct());
        }
    }
}
