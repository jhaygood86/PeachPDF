using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// <see cref="ShapeSettings.LanguageSystemTag"/> selects the OpenType language system the font's substitution features are read
    /// from. The bundled Source Sans 3 lists <c>smcp</c> under its default Latin language system but not under <c>SLA </c> (checked
    /// with fontTools), so asking for small capitals under that tag leaves the letter as it was.
    /// </summary>
    public class ShapeLanguageSystemTagTests
    {
        private static Typeface Face()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Ttf, new AddOptions { FamilyName = "LangSys-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static int Glyph(Typeface face, ShapeSettings settings) => Shaper.Shape(face, "a", settings).Glyphs[0].GlyphIndex;

        [Fact]
        public void TheDefaultLanguageSystem_SubstitutesSmallCaps()
        {
            var face = Face();
            face.TryMapRune(new Rune('a'), out var plain);

            Assert.NotEqual(plain, Glyph(face, new ShapeSettings(Caps: CapsMode.SmallCaps)));
        }

        [Fact]
        public void ALanguageSystemTagThatLacksTheFeature_LeavesTheLetterAlone()
        {
            var face = Face();
            face.TryMapRune(new Rune('a'), out var plain);

            Assert.Equal(plain, Glyph(face, new ShapeSettings(Caps: CapsMode.SmallCaps, LanguageSystemTag: "SLA ")));
        }

        [Fact]
        public void TheTagWinsOverTheLanguage_AndAnUnknownTagFallsBackToTheDefaultLanguageSystem()
        {
            var face = Face();
            face.TryMapRune(new Rune('a'), out var plain);
            var smallCaps = Glyph(face, new ShapeSettings(Caps: CapsMode.SmallCaps));

            Assert.Equal(plain, Glyph(face, new ShapeSettings(Caps: CapsMode.SmallCaps, Language: "en", LanguageSystemTag: "SLA ")));
            Assert.Equal(smallCaps, Glyph(face, new ShapeSettings(Caps: CapsMode.SmallCaps, LanguageSystemTag: "ZZZ ")));
        }
    }
}
