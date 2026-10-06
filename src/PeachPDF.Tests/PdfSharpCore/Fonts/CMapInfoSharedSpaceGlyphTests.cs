using PeachDrawing.Text.Shaping;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// A font that draws U+00A0 NO-BREAK SPACE with its space glyph (the bundled Source Sans 3 does, as
    /// Arial does) gives that glyph two candidate ToUnicode destinations. A glyph has only one, and
    /// every word separator is shown with it, so whichever space was drawn last used to decide how every
    /// word break of the document extracts.
    /// </summary>
    public class CMapInfoSharedSpaceGlyphTests
    {
        private static CMapInfo NewCMapInfo() => new(TypefaceFixtures.FromFile(BundledFonts.Ttf));

        private static int SpaceGlyph(CMapInfo cmapInfo)
        {
            cmapInfo.AddShapedText(" ", ShapeSettings.Default);
            return Assert.Single(cmapInfo.LigatureGlyphToText, e => e.Value == " ").Key;
        }

        [Fact]
        public void BundledFont_DrawsTheNoBreakSpaceWithTheSpaceGlyph()
        {
            // The premise of the other tests: otherwise they would pass without exercising anything.
            var cmapInfo = NewCMapInfo();
            var space = SpaceGlyph(cmapInfo);

            cmapInfo.AddShapedText("\u00A0", ShapeSettings.Default);

            Assert.Single(cmapInfo.LigatureGlyphToText);
            Assert.True(cmapInfo.LigatureGlyphToText.ContainsKey(space));
        }

        [Fact]
        public void NoBreakSpaceDrawnAfterASpace_LeavesTheGlyphMappedToSpace()
        {
            var cmapInfo = NewCMapInfo();
            cmapInfo.AddShapedText("first second", ShapeSettings.Default);
            var space = SpaceGlyph(cmapInfo);

            cmapInfo.AddShapedText("third\u00A0fourth", ShapeSettings.Default);

            Assert.Equal(" ", cmapInfo.LigatureGlyphToText[space]);
        }

        [Fact]
        public void SpaceDrawnAfterANoBreakSpace_TakesTheGlyphBack()
        {
            var cmapInfo = NewCMapInfo();
            cmapInfo.AddShapedText("first\u00A0second", ShapeSettings.Default);

            cmapInfo.AddShapedText("third fourth", ShapeSettings.Default);

            Assert.Equal(" ", cmapInfo.LigatureGlyphToText[SpaceGlyph(cmapInfo)]);
        }

        [Fact]
        public void ShapedGlyphRegisteredDirectly_FollowsTheSameRule()
        {
            // AddShapedGlyph is the colour-font path's entry point into the same map.
            var cmapInfo = NewCMapInfo();
            var space = SpaceGlyph(cmapInfo);

            cmapInfo.AddShapedGlyph(space, "\u00A0");

            Assert.Equal(" ", cmapInfo.LigatureGlyphToText[space]);
        }

        [Fact]
        public void NoBreakSpaceAlone_KeepsItsOwnMapping()
        {
            // Only a competing U+0020 overrides it: a document with no ordinary space keeps U+00A0.
            var cmapInfo = NewCMapInfo();

            cmapInfo.AddShapedText("first\u00A0second", ShapeSettings.Default);

            Assert.Contains(cmapInfo.LigatureGlyphToText, e => e.Value == "\u00A0");
        }
    }
}
