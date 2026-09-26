using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachDrawing.Text.Outlines;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>The hinting engine's own behaviour: what a size keeps between glyphs, what the font's programs can switch off, the caches.</summary>
    public class HintingEngineTests
    {
        [Fact]
        public void AGlyphIsHintedTheSameWhateverWasLoadedBeforeIt()
        {
            // The glyph programs of the fixture write control values, storage and twilight points. FreeType lets that leak from one glyph to the next
            // within a size; here a size is immutable and every glyph starts from what the CVT program left.
            var face = HintingFixtures.Face("HintingOpcodes.ttf");
            var size = TtSize.Create(face, 800, TtInterpreterVersion.V40, TtRenderMode.Normal);

            var forward = new List<TtHintedGlyph>();
            for (int g = 1; g <= 390; g++)
                forward.Add(TtGlyphLoader.Load(size, g));

            for (int g = 390; g >= 1; g--)
            {
                var again = TtGlyphLoader.Load(size, g);
                var first = forward[g - 1];
                Assert.True(first.X.AsSpan(0, first.NPoints).SequenceEqual(again.X.AsSpan(0, again.NPoints)), $"glyph {g}: x differs after other glyphs were loaded");
                Assert.True(first.Y.AsSpan(0, first.NPoints).SequenceEqual(again.Y.AsSpan(0, again.NPoints)), $"glyph {g}: y differs after other glyphs were loaded");
                Assert.Equal(first.Advance, again.Advance);
            }
        }

        [Fact]
        public void TheSizeCanBeUsedFromManyThreadsAtOnce()
        {
            var face = HintingFixtures.Face("HintingOpcodes.ttf");
            var size = TtSize.Create(face, 800, TtInterpreterVersion.V35, TtRenderMode.Mono);
            var expected = Enumerable.Range(1, 390).Select(g => TtGlyphLoader.Load(size, g)).ToArray();

            Parallel.For(0, 8, t =>
            {
                var random = new Random(t);
                for (int i = 0; i < 400; i++)
                {
                    int g = 1 + random.Next(390);
                    var actual = TtGlyphLoader.Load(size, g);
                    Assert.True(actual.X.AsSpan(0, actual.NPoints).SequenceEqual(expected[g - 1].X.AsSpan(0, expected[g - 1].NPoints)));
                    Assert.True(actual.Y.AsSpan(0, actual.NPoints).SequenceEqual(expected[g - 1].Y.AsSpan(0, expected[g - 1].NPoints)));
                }
            });
        }

        [Fact]
        public void AFontCanSwitchHintingOffInItsCvtProgramAndTheOutlineIsThenOnlyScaled()
        {
            // INSTCTRL with selector 1 and value 1: "do not execute glyph instructions"
            byte[] prep = [0xB1, 1, 1, 0x8E]; // PUSHB[1] 1 1, INSTCTRL
            var font = HostileFonts.WithTable(HostileFonts.Original(), "prep", prep);
            var typeface = TypefaceFixtures.FromBytes(font);
            Assert.True(typeface.TryMapRune(new Rune('!'), out var glyph));

            Assert.True(typeface.TryGetOutline(glyph, out var design));
            Assert.True(typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard }, out var outline));

            Assert.False(outline.IsGridFitted);
            Assert.Null(outline.GridFittedAdvance);
            Assert.False(typeface.TryGetGridFittedAdvance(glyph, new OutlineRequest { PixelsPerEm = 20, GridFitting = GridFitting.Standard }, out _));

            var patched = TtFace.TryCreate(typeface.Face.Fontface, typeface.Face.FamilyName, null, null)!;
            var size = TtSize.Create(patched, 20 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
            Assert.True(size.HintingDisabled);
            Assert.False(TtGlyphLoader.Load(size, glyph).IsHinted);
        }

        [Fact]
        public void TheV40InterpreterStartsInBackwardCompatibilityAndTheOtherModesDoNot()
        {
            var face = HintingFixtures.Face("LiberationSans-Regular.woff");

            Assert.Equal(4, TtSize.Create(face, 12 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal).BackwardCompatibility);
            Assert.Equal(0, TtSize.Create(face, 12 * 64, TtInterpreterVersion.V40, TtRenderMode.Mono).BackwardCompatibility);
            Assert.Equal(0, TtSize.Create(face, 12 * 64, TtInterpreterVersion.V35, TtRenderMode.Normal).BackwardCompatibility);
            Assert.Equal(0, TtSize.Create(face, 12 * 64, TtInterpreterVersion.V35, TtRenderMode.Mono).BackwardCompatibility);
        }

        [Fact]
        public void ASizeThatRoundsToNothingIsRejected()
        {
            var face = HintingFixtures.Face("LiberationSans-Regular.woff");
            Assert.Throws<HintingException>(() => TtSize.Create(face, 0, TtInterpreterVersion.V40, TtRenderMode.Normal));
            Assert.Throws<HintingException>(() => TtSize.Create(face, 31, TtInterpreterVersion.V40, TtRenderMode.Normal)); // just under half a pixel
            Assert.Throws<HintingException>(() => TtSize.Create(face, 65536 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal));
        }

        [Fact]
        public void AGlyphIndexThatIsNotInTheFontIsRejected()
        {
            var face = HintingFixtures.Face("LiberationSans-Regular.woff");
            var size = TtSize.Create(face, 12 * 64, TtInterpreterVersion.V40, TtRenderMode.Normal);
            Assert.Throws<HintingException>(() => TtGlyphLoader.Load(size, face.NumGlyphs));
            Assert.Throws<HintingException>(() => TtGlyphLoader.Load(size, -1));
        }

        [Fact]
        public void TheGlyphProgramsOfARealFontRunToTheirEnd()
        {
            // Liberation Sans hints cleanly: no glyph program of the ASCII range stops with an error, at any size
            var face = HintingFixtures.Face("LiberationSans-Regular.woff");
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "LiberationSans-Regular.woff"));

            foreach (double ppem in new[] { 8.0, 11, 12, 13, 16, 24, 32, 64 })
            {
                var size = TtSize.Create(face, (int)(ppem * 64), TtInterpreterVersion.V40, TtRenderMode.Normal);
                for (char c = '!'; c <= '~'; c++)
                {
                    Assert.True(typeface.TryMapRune(new Rune(c), out var glyph));
                    var loaded = TtGlyphLoader.Load(size, glyph);
                    Assert.True(loaded.ProgramError == 0, $"'{c}' at {ppem} ppem stopped with error {loaded.ProgramError}");
                }
            }
        }

        [Fact]
        public void TheLeastRecentlyUsedEntryIsTheOneEvicted()
        {
            var cache = new LruCache<int, string>(3);
            cache.Set(1, "a");
            cache.Set(2, "b");
            cache.Set(3, "c");

            Assert.True(cache.TryGet(1, out _)); // 1 is now the most recent, 2 the oldest
            cache.Set(4, "d");

            Assert.False(cache.TryGet(2, out _));
            Assert.True(cache.TryGet(1, out var one));
            Assert.Equal("a", one);
            Assert.True(cache.TryGet(3, out _));
            Assert.True(cache.TryGet(4, out _));

            cache.Set(3, "c2"); // replacing an entry does not grow the cache
            cache.Set(5, "e");
            Assert.True(cache.TryGet(3, out var three));
            Assert.Equal("c2", three);
        }

        [Fact]
        public void ManyMoreGlyphsAndSizesThanTheCachesHoldStillGiveTheRightOutlines()
        {
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, "LiberationSans-Regular.woff"));
            Assert.True(typeface.TryMapRune(new Rune('B'), out var glyph));

            Assert.True(typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 10, GridFitting = GridFitting.Standard }, out var before));

            // 40 sizes evict the first size (the limit is 32 per face), thousands of glyph loads evict its glyphs
            for (int size = 11; size < 51; size++)
                for (ushort g = 1; g < 120; g++)
                    typeface.TryGetOutline(g, new OutlineRequest { PixelsPerEm = size, GridFitting = GridFitting.Standard }, out _);

            Assert.True(typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 10, GridFitting = GridFitting.Standard }, out var after));
            Assert.NotSame(before, after); // it was evicted and hinted again...
            Assert.Equal(before.Contours.Count, after.Contours.Count);
            for (int c = 0; c < before.Contours.Count; c++)
                Assert.Equal(before.Contours[c].Segments.Select(s => s.End), after.Contours[c].Segments.Select(s => s.End)); // ...to the same outline
        }

        [Fact]
        public void AFontWhoseSizeFailsAnswersUnhintedEveryTime()
        {
            var font = HostileFonts.WithTable(HostileFonts.Original(), "prep", [.. HostileFonts.PushWord(-3), 0x1C]);
            var typeface = TypefaceFixtures.FromBytes(font);
            Assert.True(typeface.TryMapRune(new Rune('!'), out var glyph));

            for (int i = 0; i < 3; i++)
            {
                Assert.True(typeface.TryGetOutline(glyph, new OutlineRequest { PixelsPerEm = 12, GridFitting = GridFitting.Standard }, out var outline));
                Assert.False(outline.IsGridFitted);
            }
        }
    }
}
