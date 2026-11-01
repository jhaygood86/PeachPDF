using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Text;
using Xunit;

namespace PeachDrawing.Text.Tests.Text
{
    /// <summary>
    /// A word shaped once is not shaped again: <see cref="Shaper.Shape"/> hands back the run it already made for the same face,
    /// text and settings, and only for settings that can be found again.
    /// </summary>
    /// <remarks>
    /// Uses the synthetic feature-test font, whose glyphs are A, B, C and D and whose <c>kern</c> pair C, D is -50: the one
    /// observable thing the settings change, which is what proves a hit is only ever served to the settings it was made for.
    /// </remarks>
    public class ShapedRunCacheTests
    {
        private static Typeface Load()
        {
            var set = new FontSet();
            var options = new AddOptions { FamilyName = "ShapedRuns-" + Guid.NewGuid().ToString("N") };
            var family = set.AddFile(BundledFonts.VariableFeatureTest, options);
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        [Fact]
        public void TheSameFaceTextAndSettings_GiveTheSameRun()
        {
            var face = Load();

            var first = Shaper.Shape(face, "AB", ShapeSettings.Default);
            var second = Shaper.Shape(face, "AB", ShapeSettings.Default);

            Assert.Same(first, second);
            Assert.Equal([1, 2], first.Glyphs.Select(g => g.GlyphIndex).ToArray());
        }

        [Fact]
        public void AnotherText_GivesAnotherRun()
        {
            var face = Load();

            Assert.NotSame(Shaper.Shape(face, "AB", ShapeSettings.Default), Shaper.Shape(face, "BA", ShapeSettings.Default));
        }

        [Fact]
        public void AnotherFace_NeverSharesARun()
        {
            var one = Load();
            var other = Load();

            Assert.NotSame(Shaper.Shape(one, "AB", ShapeSettings.Default), Shaper.Shape(other, "AB", ShapeSettings.Default));
            Assert.Same(one, Shaper.Shape(one, "AB", ShapeSettings.Default).Typeface);
            Assert.Same(other, Shaper.Shape(other, "AB", ShapeSettings.Default).Typeface);
        }

        [Fact]
        public void AnotherSetting_IsNeverServedTheRunMadeForTheFirst()
        {
            var face = Load();
            var kerned = ShapeSettings.Default;
            var unkerned = ShapeSettings.Default with { Kerning = false };

            // Asked in both orders and more than once, so a hit made for one is checked against the other.
            for (var round = 0; round < 3; round++)
            {
                Assert.Equal(-50, Shaper.Shape(face, "CD", kerned).Glyphs[0].XAdvanceDelta);
                Assert.Equal(0, Shaper.Shape(face, "CD", unkerned).Glyphs[0].XAdvanceDelta);
            }
        }

        [Fact]
        public void SettingsCarryingPerCallLists_AreShapedEveryTimeAndNeverKept()
        {
            var face = Load();
            var settings = ShapeSettings.Default with { ExplicitFeatures = [new FeatureSetting("zzzz", 1)] };

            var before = face.ShapedRuns.Count;
            var first = Shaper.Shape(face, "AB", settings);
            var second = Shaper.Shape(face, "AB", settings);

            Assert.NotSame(first, second);
            Assert.Equal(before, face.ShapedRuns.Count);

            // Same glyphs as the cached path gives for the same text: the bypass changes nothing but the sharing.
            Assert.Equal(
                Shaper.Shape(face, "AB", ShapeSettings.Default).Glyphs.Select(g => g.GlyphIndex),
                first.Glyphs.Select(g => g.GlyphIndex));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IsCacheable_IsFalseForEveryListASettingCanCarry(bool empty)
        {
            // An empty list is as findable as none, so it still caches; a non-empty one never does.
            Assert.Equal(empty, ShapedRunCache.IsCacheable(ShapeSettings.Default with { JoiningForms = empty ? [] : [ArabicJoiningForm.Isol] }));
            Assert.Equal(empty, ShapedRunCache.IsCacheable(ShapeSettings.Default with { UseCategories = empty ? [] : [UseCategory.B] }));
            Assert.Equal(empty, ShapedRunCache.IsCacheable(ShapeSettings.Default with { KhmerCategories = empty ? [] : [KhmerCategory.C] }));
            Assert.Equal(empty, ShapedRunCache.IsCacheable(ShapeSettings.Default with { ExplicitFeatures = empty ? [] : [new FeatureSetting("liga", 0)] }));
        }

        [Fact]
        public void AFullCache_StartsOver_AndStillShapesEverything()
        {
            var face = Load();
            var kept = Shaper.Shape(face, "ABCD", ShapeSettings.Default);

            // More distinct strings than one face keeps: eight letters of four glyphs each.
            var distinct = ShapedRunCache.Capacity + 100;
            for (var i = 0; i < distinct; i++)
            {
                var text = new StringBuilder();
                for (var digit = 0; digit < 8; digit++) text.Append((char)('A' + ((i >> (2 * digit)) & 3)));
                Shaper.Shape(face, text.ToString(), ShapeSettings.Default);
            }

            Assert.InRange(face.ShapedRuns.Count, 1, ShapedRunCache.Capacity);

            // Whether or not it survived the reset, the word shapes to the same glyphs.
            Assert.Equal(kept.Glyphs.Select(g => g.GlyphIndex), Shaper.Shape(face, "ABCD", ShapeSettings.Default).Glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void ShapingFromSeveralThreads_GivesEveryThreadTheRightGlyphs()
        {
            var face = Load();
            var words = new[] { "AB", "BA", "CD", "DC", "ABCD", "DCBA", "AAAA", "BBBB" };

            // The reference comes from the path that is never cached, so it does not depend on the thing under test.
            var uncached = ShapeSettings.Default with { ExplicitFeatures = [new FeatureSetting("zzzz", 1)] };
            var expected = words.ToDictionary(w => w, w => Shaper.Shape(face, w, uncached).Glyphs.Select(g => g.GlyphIndex).ToArray());

            Parallel.For(0, 4000, i =>
            {
                var word = words[i % words.Length];
                var glyphs = Shaper.Shape(face, word, ShapeSettings.Default).Glyphs.Select(g => g.GlyphIndex).ToArray();
                Assert.Equal(expected[word], glyphs);
            });
        }

        [Fact]
        public void ShapingAWordAlreadySeen_AllocatesNothingPerCall()
        {
            var face = Load();
            var settings = ShapeSettings.Default;
            Shaper.Shape(face, "ABCD", settings);

            const int calls = 1000;
            var allocated = AllocationProbe.Bytes(() => Shaper.Shape(face, "ABCD", settings), calls);

            // A hit looks the key up and returns the run it holds. A few hundred bytes of slack rather than zero, so this says
            // "not per call" without depending on the runtime never allocating anything incidental.
            Assert.True(allocated < 4096, $"{calls:N0} cached shapes allocated {allocated} bytes; a hit should allocate nothing.");
        }
    }
}
