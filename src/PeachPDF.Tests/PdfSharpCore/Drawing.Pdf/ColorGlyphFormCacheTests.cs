using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Drawing.Pdf;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing.Pdf
{
    /// <summary>
    /// Unit tests for the color-glyph form cache's key. The equality rules matter because getting one
    /// wrong is silent in both directions: too strict and a repeated emoji embeds its artwork again per
    /// occurrence, too loose and one occurrence is painted with another's colors.
    ///
    /// These drive <c>Selector</c> directly rather than through a rendered document, because a
    /// dictionary only calls <c>Equals</c> on entries whose hash already matched - so the cases where
    /// two keys differ can only be observed here.
    /// </summary>
    public class ColorGlyphFormCacheTests
    {
        private static OpenTypeDescriptor Descriptor(string path) =>
            new("color-form-cache-test", "color-form-cache-test", XFontStyle.Regular,
                XFontSource.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface,
                new XPdfFontOptions(PdfFontEncoding.Unicode));

        private static Dictionary<int, XColor> Overrides(params (int Entry, XColor Color)[] entries)
        {
            var map = new Dictionary<int, XColor>();
            foreach ((int entry, XColor color) in entries)
                map[entry] = color;
            return map;
        }

        private static ColorGlyphFormCache.Selector Key(OpenTypeDescriptor descriptor, int glyphId = 7,
            int paletteIndex = 0, XColor? foreground = null, IReadOnlyDictionary<int, XColor>? overrides = null)
            => new(descriptor, glyphId, paletteIndex, foreground ?? XColors.Black, overrides);

        [Fact]
        public void SameGlyphSameStyling_IsTheSameKey()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.Equal(Key(descriptor), Key(descriptor));
            Assert.Equal(Key(descriptor).GetHashCode(), Key(descriptor).GetHashCode());
            Assert.True(Key(descriptor).Equals((object)Key(descriptor)));
        }

        [Fact]
        public void DifferentGlyphOrPaletteOrForeground_IsADifferentKey()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.NotEqual(Key(descriptor), Key(descriptor, glyphId: 8));
            Assert.NotEqual(Key(descriptor), Key(descriptor, paletteIndex: 1));
            Assert.NotEqual(Key(descriptor), Key(descriptor, foreground: XColors.Red));
        }

        [Fact]
        public void DifferentFont_IsADifferentKey()
        {
            // Glyph ids only mean anything within one font, so the descriptor's identity is part of the
            // key even when every other component matches.
            Assert.NotEqual(Key(Descriptor(BundledFonts.ColorV0)), Key(Descriptor(BundledFonts.ColorV1)));
        }

        [Fact]
        public void NonSelectorObject_IsNeverEqual()
        {
            Assert.False(Key(Descriptor(BundledFonts.ColorV0)).Equals("not a selector"));
        }

        [Fact]
        public void EqualOverrides_InDistinctDictionaries_AreTheSameKey()
        {
            // The resolved font-palette reaches the backend as a fresh dictionary per text run, so this
            // is the case that decides whether font-palette output dedupes at all.
            var descriptor = Descriptor(BundledFonts.ColorV0);
            var left = Overrides((0, XColors.Lime), (4, XColors.Blue));
            var right = Overrides((4, XColors.Blue), (0, XColors.Lime)); // same pairs, inserted the other way round

            Assert.Equal(Key(descriptor, overrides: left), Key(descriptor, overrides: right));
            Assert.Equal(Key(descriptor, overrides: left).GetHashCode(), Key(descriptor, overrides: right).GetHashCode());
        }

        [Fact]
        public void OverridesDifferingInColor_AreDifferentKeys()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.NotEqual(
                Key(descriptor, overrides: Overrides((0, XColors.Lime))),
                Key(descriptor, overrides: Overrides((0, XColors.Blue))));
        }

        [Fact]
        public void OverridesDifferingInWhichEntryTheyReplace_AreDifferentKeys()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.NotEqual(
                Key(descriptor, overrides: Overrides((0, XColors.Lime))),
                Key(descriptor, overrides: Overrides((4, XColors.Lime))));
        }

        [Fact]
        public void OverridesDifferingInCount_AreDifferentKeys()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.NotEqual(
                Key(descriptor, overrides: Overrides((0, XColors.Lime))),
                Key(descriptor, overrides: Overrides((0, XColors.Lime), (4, XColors.Blue))));
        }

        [Fact]
        public void OverridesAgainstNone_AreDifferentKeys()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.NotEqual(Key(descriptor), Key(descriptor, overrides: Overrides((0, XColors.Lime))));
            Assert.NotEqual(Key(descriptor, overrides: Overrides((0, XColors.Lime))), Key(descriptor));
        }

        [Fact]
        public void ColorsThatWriteIdentically_AreTheSameKey()
        {
            // The key compares colors at the 8-bit-per-channel resolution the content stream itself
            // keeps, so two XColor values the output cannot tell apart never split into two forms.
            var descriptor = Descriptor(BundledFonts.ColorV0);

            Assert.Equal(
                Key(descriptor, foreground: XColor.FromArgb(255, 18, 52, 86)),
                Key(descriptor, foreground: XColor.FromArgb(255, 18, 52, 86)));
        }

        [Fact]
        public void Cache_ReturnsWhatWasStored_AndMissesOnAnUnknownKey()
        {
            var descriptor = Descriptor(BundledFonts.ColorV0);
            var cache = new ColorGlyphFormCache();
            var stored = new ColorGlyphForm(null, -12.5, -34.5);

            Assert.False(cache.TryGetForm(Key(descriptor), out _));

            cache.AddForm(Key(descriptor), stored);

            Assert.True(cache.TryGetForm(Key(descriptor), out ColorGlyphForm found));
            Assert.Equal(stored, found);
            Assert.False(cache.TryGetForm(Key(descriptor, glyphId: 8), out _));
        }
    }
}
