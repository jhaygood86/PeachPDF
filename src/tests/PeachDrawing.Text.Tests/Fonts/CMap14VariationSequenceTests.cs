using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.Text.Internal.Text;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// cmap format 14 (Unicode Variation Sequences): parsing, the font-selection signal it gives
    /// (<see cref="EmojiProperties.FaceMatches"/>) and the dedicated glyph a non-default record swaps in
    /// during shaping. No bundled font declares U+FE0E/U+FE0F sequences, so each test derives one from a real
    /// font with <see cref="SyntheticUvsFont"/>.
    /// </summary>
    public class CMap14VariationSequenceTests
    {
        private const int Heart = 0x2764;
        private const int Smiley = 0x263A;

        private static OpenTypeFontface Face(byte[] font) => FontFileData.GetOrCreateFrom(font).Fontface;

        private static OpenTypeDescriptor Descriptor(byte[] font) =>
            new("uvs-test", "uvs-test", Face(font));

        [Fact]
        public void FontWithoutFormat14_HasNoVariationSequenceTable()
        {
            Assert.Null(Face(File.ReadAllBytes(BundledFonts.Ttf)).cmap.cmap14);
        }

        [Fact]
        public void Lookup_ReportsDefaultAndNonDefaultRecords_PerSelector()
        {
            var face = Face(SyntheticUvsFont.Build(File.ReadAllBytes(BundledFonts.Ttf),
                new SyntheticUvsFont.Sequence(Heart, 0xFE0E),
                new SyntheticUvsFont.Sequence(Smiley, 0xFE0F, Glyph: 42)));

            var table = face.cmap.cmap14;
            Assert.NotNull(table);

            Assert.Equal(VariationSequenceSupport.Default, table!.Lookup(Heart, 0xFE0E, out var defaultGlyph));
            Assert.Equal(0, defaultGlyph);

            Assert.Equal(VariationSequenceSupport.NonDefault, table.Lookup(Smiley, 0xFE0F, out var dedicatedGlyph));
            Assert.Equal(42, dedicatedGlyph);

            // The same base under the other selector, and an unlisted base, are not supported.
            Assert.Equal(VariationSequenceSupport.None, table.Lookup(Heart, 0xFE0F, out _));
            Assert.Equal(VariationSequenceSupport.None, table.Lookup(Smiley, 0xFE0E, out _));
            Assert.Equal(VariationSequenceSupport.None, table.Lookup('A', 0xFE0F, out _));
        }

        [Fact]
        public void Lookup_ReadsEverySelectorTheFontLists()
        {
            var face = Face(SyntheticUvsFont.Build(File.ReadAllBytes(BundledFonts.Ttf),
                new SyntheticUvsFont.Sequence(Heart, 0xFE00, Glyph: 7),
                new SyntheticUvsFont.Sequence(Heart, 0xFE0F),
                new SyntheticUvsFont.Sequence('A', 0xE0100, Glyph: 9)));

            var table = face.cmap.cmap14!;

            Assert.Equal(VariationSequenceSupport.NonDefault, table.Lookup(Heart, 0xFE00, out var standardized));
            Assert.Equal(7, standardized);
            Assert.Equal(VariationSequenceSupport.Default, table.Lookup(Heart, 0xFE0F, out _));
            Assert.Equal(VariationSequenceSupport.NonDefault, table.Lookup('A', 0xE0100, out var ideographic));
            Assert.Equal(9, ideographic);
        }

        [Fact]
        public void FaceMatches_OnlyConsultsThePresentationSelectors()
        {
            // CSS Fonts 4: no variation selector other than U+FE0E/U+FE0F may affect font selection, so a
            // font listing (heart, FE00) is no more a "text" font than one listing nothing - colour-ness decides.
            var colour = File.ReadAllBytes(BundledFonts.ColorEmoji);
            var face = Face(SyntheticUvsFont.Build(colour, new SyntheticUvsFont.Sequence(Heart, 0xFE00)));

            Assert.False(EmojiProperties.FaceMatches(face, Heart, EmojiPresentation.Text));
        }

        [Fact]
        public void Shape_AnyVariationSelectorWithANonDefaultRecord_SwapsInTheDedicatedGlyph()
        {
            var source = File.ReadAllBytes(BundledFonts.Ttf);
            var plain = Descriptor(source);
            var bGlyph = plain.CharCodeToGlyphIndex(new Rune('B'));
            var derived = Descriptor(SyntheticUvsFont.Build(source, new SyntheticUvsFont.Sequence('A', 0xFE00, Glyph: bGlyph)));

            Assert.Equal(bGlyph, Assert.Single(derived.Shape("A︀", ShapeSettings.Default)).GlyphIndex);
            Assert.Equal(plain.CharCodeToGlyphIndex(new Rune('A')), Assert.Single(derived.Shape("A", ShapeSettings.Default)).GlyphIndex);
        }

        [Fact]
        public void TheDerivedFont_StillMapsOrdinaryCharacters()
        {
            // The rebuilt cmap must leave the original subtables working.
            var descriptor = Descriptor(SyntheticUvsFont.Build(File.ReadAllBytes(BundledFonts.Ttf),
                new SyntheticUvsFont.Sequence(Heart, 0xFE0E)));

            Assert.NotEqual(0, descriptor.CharCodeToGlyphIndex(new Rune('A')));
            Assert.NotEqual(0, descriptor.CharCodeToGlyphIndex(new Rune(Heart)));
        }

        [Fact]
        public void FaceMatches_PrefersTheFontsOwnSequenceDeclarationOverItsColourness()
        {
            var colourBytes = File.ReadAllBytes(BundledFonts.ColorEmoji);
            var plainColour = Face(colourBytes);
            var declaresTextForm = Face(SyntheticUvsFont.Build(colourBytes, new SyntheticUvsFont.Sequence(Heart, 0xFE0E)));

            Assert.True(plainColour.IsColorFont);

            // Without a declaration, a colour font is an emoji-presentation font, not a text one.
            Assert.True(EmojiProperties.FaceMatches(plainColour, Heart, EmojiPresentation.Emoji));
            Assert.False(EmojiProperties.FaceMatches(plainColour, Heart, EmojiPresentation.Text));

            // A font that lists (heart, FE0E) supports the text sequence whatever it is made of.
            Assert.True(EmojiProperties.FaceMatches(declaresTextForm, Heart, EmojiPresentation.Text));
            // ...and still counts as an emoji font for the other request, by colour-ness.
            Assert.True(EmojiProperties.FaceMatches(declaresTextForm, Heart, EmojiPresentation.Emoji));
        }

        [Fact]
        public void FaceMatches_OutlineFont_MatchesTextButNotEmoji_AndNoPreferenceMatchesEverything()
        {
            var outline = Face(File.ReadAllBytes(BundledFonts.Ttf));

            Assert.False(outline.IsColorFont);
            Assert.True(EmojiProperties.FaceMatches(outline, Heart, EmojiPresentation.Text));
            Assert.False(EmojiProperties.FaceMatches(outline, Heart, EmojiPresentation.Emoji));
            Assert.True(EmojiProperties.FaceMatches(outline, Heart, EmojiPresentation.NoPreference));
        }

        [Fact]
        public void Shape_NonDefaultRecord_SwapsInTheDedicatedGlyph_AndStillDropsTheSelector()
        {
            var source = File.ReadAllBytes(BundledFonts.Ttf);
            var plain = Descriptor(source);
            var aGlyph = plain.CharCodeToGlyphIndex(new Rune('A'));
            var heartGlyph = plain.CharCodeToGlyphIndex(new Rune(Heart));
            Assert.NotEqual(aGlyph, heartGlyph);

            var derived = Descriptor(SyntheticUvsFont.Build(source, new SyntheticUvsFont.Sequence(Heart, 0xFE0E, Glyph: aGlyph)));

            // With the text selector the heart takes the dedicated glyph; the selector adds no glyph of its own.
            var withSelector = derived.Shape("❤︎", ShapeSettings.Default);
            Assert.Single(withSelector);
            Assert.Equal(aGlyph, withSelector[0].GlyphIndex);

            // Without it - or with the other selector, which the font does not list - the ordinary glyph is used.
            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤", ShapeSettings.Default)).GlyphIndex);
            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤️", ShapeSettings.Default)).GlyphIndex);

            // A font with no such record is unaffected by the selector.
            Assert.Equal(heartGlyph, Assert.Single(plain.Shape("❤︎", ShapeSettings.Default)).GlyphIndex);
        }

        [Fact]
        public void Shape_RealFontWithVariationSequences_UsesItsOwnDedicatedGlyph()
        {
            // STIX Two Math is the one bundled font with a real format-14 table: U+0030 U+FE00 selects
            // its slashed zero, a glyph the plain U+0030 does not use.
            var descriptor = Descriptor(File.ReadAllBytes(BundledFonts.Math));

            var plain = Assert.Single(descriptor.Shape("0", ShapeSettings.Default)).GlyphIndex;
            var variant = Assert.Single(descriptor.Shape("0︀", ShapeSettings.Default)).GlyphIndex;

            Assert.NotEqual(0, variant);
            Assert.NotEqual(plain, variant);
        }

        [Fact]
        public void Shape_FontVariantEmojiStandsInForTheSelector_ForADedicatedGlyph()
        {
            var source = File.ReadAllBytes(BundledFonts.Ttf);
            var plain = Descriptor(source);
            var aGlyph = plain.CharCodeToGlyphIndex(new Rune('A'));
            var heartGlyph = plain.CharCodeToGlyphIndex(new Rune(Heart));
            var derived = Descriptor(SyntheticUvsFont.Build(source, new SyntheticUvsFont.Sequence(Heart, 0xFE0E, Glyph: aGlyph)));

            ShapeSettings With(PeachDrawing.Text.Unicode.EmojiMode mode) => ShapeSettings.Default with { EmojiMode = mode };

            // No selector in the text: text presentation (and unicode, for a text-default character) asks
            // for the FE0E glyph; normal and emoji do not.
            Assert.Equal(aGlyph, Assert.Single(derived.Shape("❤", With(PeachDrawing.Text.Unicode.EmojiMode.Text))).GlyphIndex);
            Assert.Equal(aGlyph, Assert.Single(derived.Shape("❤", With(PeachDrawing.Text.Unicode.EmojiMode.Unicode))).GlyphIndex);
            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤", With(PeachDrawing.Text.Unicode.EmojiMode.Normal))).GlyphIndex);
            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤", With(PeachDrawing.Text.Unicode.EmojiMode.Emoji))).GlyphIndex);

            // An explicit FE0F overrides text presentation, so the FE0E glyph is not used.
            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤️", With(PeachDrawing.Text.Unicode.EmojiMode.Text))).GlyphIndex);
        }

        [Fact]
        public void MalformedFormat14Table_DoesNotStopTheFontLoading()
        {
            // A record count far larger than the font can hold must not size an array from it; the optional
            // table is dropped and the font still loads and maps ordinary characters.
            var bytes = SyntheticUvsFont.Build(File.ReadAllBytes(BundledFonts.Ttf), [new SyntheticUvsFont.Sequence(Heart, 0xFE0E)], corruptRecordCount: true);

            var descriptor = Descriptor(bytes);

            Assert.Null(descriptor.FontFace.cmap.cmap14);
            Assert.NotEqual(0, descriptor.CharCodeToGlyphIndex(new Rune('A')));
        }

        [Fact]
        public void TrailingCmapRecordPointingOutsideTheFont_DoesNotStopTheFontLoading()
        {
            var bytes = SyntheticUvsFont.Build(File.ReadAllBytes(BundledFonts.Ttf), [], addDanglingRecord: true);

            var descriptor = Descriptor(bytes);

            Assert.NotEqual(0, descriptor.CharCodeToGlyphIndex(new Rune('A')));
        }

        [Fact]
        public void Shape_DefaultRecord_KeepsTheOrdinaryGlyph()
        {
            var source = File.ReadAllBytes(BundledFonts.Ttf);
            var heartGlyph = Descriptor(source).CharCodeToGlyphIndex(new Rune(Heart));
            var derived = Descriptor(SyntheticUvsFont.Build(source, new SyntheticUvsFont.Sequence(Heart, 0xFE0F)));

            Assert.Equal(heartGlyph, Assert.Single(derived.Shape("❤️", ShapeSettings.Default)).GlyphIndex);
        }
    }
}
