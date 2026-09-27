using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>Typefaces as a caller outside the engine gets them: through a <see cref="FontSet"/>.</summary>
    internal static class TypefaceFixtures
    {
        private static readonly ConcurrentDictionary<string, Typeface> SharedByPath = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>The typeface a font file gives, as a caller outside the engine gets it: through a font set.</summary>
        public static Typeface FromFile(string path) => FromBytes(System.IO.File.ReadAllBytes(path));

        /// <summary>
        /// The typeface a font file gives, loaded once per process and shared: a typeface is immutable, so tests that only read one
        /// (shape with it, map characters, measure) need not parse the file again for each test.
        /// </summary>
        public static Typeface Shared(string path) => SharedByPath.GetOrAdd(path, FromFile);

        /// <summary>
        /// The family name a font set registers a font file under when its caller gives none, which is the name a page's
        /// <c>font-family</c> uses for a font added from that file.
        /// </summary>
        public static string FamilyNameOf(string path) => new FontSet().AddFile(path).Name;

        /// <summary>The family name a font set registers font data under when its caller gives none.</summary>
        public static string FamilyNameOf(byte[] data) => new FontSet().AddData(data).Name;

        /// <summary>The typeface font data gives, as a caller outside the engine gets it: through a font set.</summary>
        public static Typeface FromBytes(byte[] data)
        {
            var set = new FontSet();
            var family = set.AddData(data, new AddOptions { FamilyName = "TestFonts-" + System.Guid.NewGuid().ToString("N") });
            if (!family.TryMatch(new TypefaceQuery(), out var match))
                throw new System.InvalidOperationException("The font data matched no face.");
            return match.Typeface;
        }
    }

    /// <summary>Short forms of the public typeface calls the tests make over and over.</summary>
    internal static class TypefaceTestExtensions
    {
        /// <summary>The glyphs <see cref="Shaper.Shape"/> gives for <paramref name="text"/>.</summary>
        public static IReadOnlyList<PlacedGlyph> ShapeGlyphs(this Typeface face, string text, in ShapeSettings settings) =>
            Shaper.Shape(face, text, settings).Glyphs;

        /// <summary>The glyph the font maps <paramref name="rune"/> to; 0, the missing glyph, when it maps to none.</summary>
        public static int GlyphOf(this Typeface face, Rune rune)
        {
            face.TryMapRune(rune, out var glyph);
            return glyph;
        }

        /// <summary>The glyph the font maps the character to; 0, the missing glyph, when it maps to none.</summary>
        public static int GlyphOf(this Typeface face, char c) => face.GlyphOf(new Rune(c));

        /// <summary>The horizontal advance of a glyph, in design units.</summary>
        public static int AdvanceOf(this Typeface face, int glyph) => face.GetAdvance((ushort)glyph);
    }
}
