using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// What a <see cref="Typeface"/> says about itself without any size: its metrics, its glyph mapping and advances, its
    /// vertical metrics, and the questions a renderer asks before choosing it. Each answer is also compared with the
    /// engine's own descriptor, which the public members wrap, so a mistake in the wrapper cannot hide.
    /// </summary>
    public class TypefaceMetricsPublicApiTests
    {
        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Metrics-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void Metrics_AreTheFontsDesignUnitDimensions()
        {
            var metrics = Face(BundledFonts.Ttf).Metrics;

            Assert.Equal(1000, metrics.UnitsPerEm);
            Assert.InRange(metrics.CellAscent, 1, 2 * metrics.UnitsPerEm);
            Assert.InRange(metrics.CellDescent, 1, metrics.UnitsPerEm);
            Assert.True(metrics.LineSpacing >= metrics.CellAscent + metrics.CellDescent);
            Assert.True(metrics.NormalLineAscent > 0);
            Assert.True(metrics.NormalLineDescent > 0);
            Assert.True(metrics.NormalLineGap >= 0);
            Assert.True(metrics.UnderlinePosition < 0);
            Assert.True(metrics.UnderlineThickness > 0);
            Assert.True(metrics.StrikeoutThickness > 0);
            Assert.True(metrics.StrikeoutPosition > 0);
            Assert.InRange(metrics.CapHeight, 1, metrics.UnitsPerEm);
            Assert.InRange(metrics.XHeight, 1, metrics.CapHeight);
            Assert.Equal(0f, metrics.ItalicAngle);
            Assert.True(metrics.XMin < metrics.XMax);
            Assert.True(metrics.YMin < metrics.YMax);
        }

        [Fact]
        public void Metrics_MatchTheEnginesDescriptor()
        {
            var typeface = Face(BundledFonts.Ttf);
            var metrics = typeface.Metrics;
            var descriptor = typeface.Face.Descriptor;

            Assert.Equal(descriptor.UnitsPerEm, metrics.UnitsPerEm);
            Assert.Equal(descriptor.Ascender, metrics.CellAscent);
            Assert.Equal(descriptor.Descender, metrics.CellDescent);
            Assert.Equal(descriptor.LineSpacing, metrics.LineSpacing);
            Assert.Equal(descriptor.NormalLineHeightAscent, metrics.NormalLineAscent);
            Assert.Equal(descriptor.NormalLineHeightDescent, metrics.NormalLineDescent);
            Assert.Equal(descriptor.NormalLineHeightGap, metrics.NormalLineGap);
            Assert.Equal(descriptor.UnderlinePosition, metrics.UnderlinePosition);
            Assert.Equal(descriptor.UnderlineThickness, metrics.UnderlineThickness);
            Assert.Equal(descriptor.StrikeoutPosition, metrics.StrikeoutPosition);
            Assert.Equal(descriptor.StrikeoutSize, metrics.StrikeoutThickness);
            Assert.Equal(descriptor.CapHeight, metrics.CapHeight);
            Assert.Equal(descriptor.XHeight, metrics.XHeight);
            Assert.Equal(descriptor.HasAuthenticXHeight, metrics.HasMeasuredXHeight);
            Assert.Equal(descriptor.ItalicAngle, metrics.ItalicAngle);
            Assert.Equal((descriptor.XMin, descriptor.YMin, descriptor.XMax, descriptor.YMax), (metrics.XMin, metrics.YMin, metrics.XMax, metrics.YMax));
        }

        [Fact]
        public void Metrics_ArePreparedOncePerTypeface()
        {
            var typeface = Face(BundledFonts.Ttf);

            Assert.Same(typeface.Metrics, typeface.Metrics);
        }

        [Fact]
        public void Metrics_OfAnItalicFace_HaveAnItalicAngle()
        {
            var set = new FontSet();
            var family = set.AddFile(BundledFonts.Recursive, new AddOptions { FamilyName = "Angle-" + Guid.NewGuid().ToString("N"), IsItalic = true });
            Assert.True(family.TryMatch(new TypefaceQuery(IsItalic: true), out var match));

            // The font was registered as italic regardless of what its tables say, and the angle is whatever the tables say.
            Assert.Equal(match.Typeface.Face.Descriptor.ItalicAngle, match.Typeface.Metrics.ItalicAngle);
        }

        [Fact]
        public void TryMapRune_FindsTheGlyphOfACharacterTheFontHasAndReportsTheOnesItDoesNot()
        {
            var typeface = Face(BundledFonts.Ttf);

            Assert.True(typeface.TryMapRune(new Rune('A'), out var glyph));
            Assert.NotEqual(0, glyph);
            Assert.Equal(typeface.Face.Descriptor.CharCodeToGlyphIndex(new Rune('A')), glyph);
            Assert.True(typeface.HasGlyph(new Rune('A')));

            Assert.False(typeface.TryMapRune(new Rune(0x10FFFF), out var missing));
            Assert.Equal(0, missing);
            Assert.False(typeface.HasGlyph(new Rune(0x10FFFF)));
        }

        [Fact]
        public void GetAdvance_IsTheHorizontalAdvanceInDesignUnits()
        {
            var typeface = Face(BundledFonts.Ttf);
            typeface.TryMapRune(new Rune('A'), out var a);
            typeface.TryMapRune(new Rune('i'), out var i);

            Assert.True(typeface.GetAdvance(a) > typeface.GetAdvance(i));
            Assert.Equal(typeface.Face.Descriptor.GlyphIndexToWidth(a), typeface.GetAdvance(a));
            // A glyph number past the last metric shares the last metric, as in a monospaced font.
            Assert.Equal(typeface.GetAdvance(ushort.MaxValue), typeface.GetAdvance(ushort.MaxValue - 1));
        }

        [Fact]
        public void VerticalMetrics_OfAFontWithNone_AreOneEmAndHaveNoOrigin()
        {
            var typeface = Face(BundledFonts.Ttf);
            typeface.TryMapRune(new Rune('A'), out var glyph);

            Assert.False(typeface.HasVerticalMetrics);
            Assert.False(typeface.HasVerticalOrigin);
            Assert.Equal(typeface.Metrics.UnitsPerEm, typeface.GetVerticalAdvance(glyph));
        }

        [Fact]
        public void VerticalMetrics_AgreeWithTheEnginesDescriptor_ForACjkFont()
        {
            var typeface = Face(BundledFonts.Cjk);
            typeface.TryMapRune(new Rune(0x4E00), out var glyph);
            var descriptor = typeface.Face.Descriptor;

            Assert.Equal(descriptor.HasVerticalMetrics, typeface.HasVerticalMetrics);
            Assert.Equal(descriptor.HasVerticalOrigin, typeface.HasVerticalOrigin);
            Assert.Equal(descriptor.GlyphIndexToVerticalAdvance(glyph), typeface.GetVerticalAdvance(glyph));
            var (x, y) = descriptor.GlyphIndexToVerticalOrigin(glyph);
            Assert.Equal(new System.Drawing.Point(x, y), typeface.GetVerticalOrigin(glyph));
        }

        [Fact]
        public void TryGetScriptPosition_GivesTheDesignersRecommendation_OrReportsThereIsNone()
        {
            var typeface = Face(BundledFonts.Ttf);
            var descriptor = typeface.Face.Descriptor;

            foreach (var placement in new[] { ScriptPlacement.Subscript, ScriptPlacement.Superscript })
            {
                var expected = descriptor.GetSubSuperscriptMetrics(placement == ScriptPlacement.Superscript);
                Assert.Equal(expected is not null, typeface.TryGetScriptPosition(placement, out var position));
                if (expected is { } e)
                {
                    Assert.Equal(new ScriptPosition(e.SizeScale, e.BaselineShift), position);
                    Assert.InRange(position.SizeScale, 0.01, 1.0);
                    Assert.True(position.BaselineShift > 0);
                }
                else
                {
                    Assert.Equal(default, position);
                }
            }
        }

        [Fact]
        public void HasColorGlyphs_IsTrueForAColourFontAndFalseForAnOrdinaryOne()
        {
            Assert.False(Face(BundledFonts.Ttf).HasColorGlyphs);
            Assert.True(Face(BundledFonts.ColorV0).HasColorGlyphs);
        }

        [Fact]
        public void MatchesEmojiPresentation_PrefersAColourFaceForEmojiAndAnOutlineFaceForText()
        {
            var ordinary = Face(BundledFonts.Ttf);
            var colour = Face(BundledFonts.ColorV0);
            var heart = new Rune(0x2764);

            Assert.True(ordinary.MatchesEmojiPresentation(heart, EmojiPresentation.NoPreference));
            Assert.True(ordinary.MatchesEmojiPresentation(heart, EmojiPresentation.Text));
            Assert.False(ordinary.MatchesEmojiPresentation(heart, EmojiPresentation.Emoji));
            Assert.True(colour.MatchesEmojiPresentation(heart, EmojiPresentation.Emoji));
            Assert.False(colour.MatchesEmojiPresentation(heart, EmojiPresentation.Text));
        }

        [Fact]
        public void SupportsFeatures_AgreesWithTheEnginesGsubCheck_ForEveryTagOfSomeFixtures()
        {
            string[] fonts = [BundledFonts.Ttf, BundledFonts.Otf, BundledFonts.Recursive, BundledFonts.CcmpLigature, BundledFonts.GsubTestLookup3, BundledFonts.Math];
            string[] tags = ["smcp", "c2sc", "liga", "ccmp", "locl", "sups", "subs", "calt", "ss01", "zzzz"];
            var anySupported = false;

            foreach (var path in fonts)
            {
                var typeface = Face(path);
                foreach (var tag in tags)
                {
                    var set = new HashSet<string> { tag };
                    var expected = typeface.Face.Descriptor.SupportsFeatureTags(set);
                    Assert.Equal(expected, typeface.SupportsFeatures(set));
                    anySupported |= expected;
                }
            }

            // Otherwise the comparison above could pass on a set of fonts that support nothing.
            Assert.True(anySupported);
        }

        [Fact]
        public void SupportsFeatures_RequiresEveryTag_AndRejectsNull()
        {
            var typeface = Face(BundledFonts.Ttf);

            Assert.False(typeface.SupportsFeatures(new HashSet<string> { "zzzz" }));
            Assert.False(typeface.SupportsFeatures(new HashSet<string> { "kern", "zzzz" }));
            Assert.Throws<ArgumentNullException>(() => typeface.SupportsFeatures(null!));
        }

        [Fact]
        public void ScriptPosition_IsAValueWithItsTwoFractions()
        {
            var position = new ScriptPosition(0.6, 0.2);

            Assert.Equal(0.6, position.SizeScale);
            Assert.Equal(0.2, position.BaselineShift);
            Assert.Equal(position, new ScriptPosition(0.6, 0.2));
        }
    }
}
