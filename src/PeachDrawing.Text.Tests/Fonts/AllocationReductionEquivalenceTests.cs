using PeachDrawing.Text.Internal.Fonts;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The allocation-trimming rewrites (word-wise table checksum, span-based hyphenation lookup, case-insensitive
    /// family dictionary, cached range query) must answer exactly what the code they replaced did.
    /// </summary>
    public class AllocationReductionEquivalenceTests
    {
        private static uint ReferenceChecksum(byte[] bytes)
        {
            uint byte3 = 0, byte2 = 0, byte1 = 0, byte0 = 0;
            for (int i = 0; i < bytes.Length;)
            {
                byte3 += bytes[i++];
                byte2 += bytes[i++];
                byte1 += bytes[i++];
                byte0 += bytes[i++];
            }
            return (byte3 << 24) + (byte2 << 16) + (byte1 << 8) + byte0;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(1024)]
        [InlineData(65536)]
        public void CalcChecksum_MatchesTheByteLaneSum(int length)
        {
            var bytes = new byte[length];
            new Random(length + 7).NextBytes(bytes);
            Array.Fill(bytes, (byte)0xFF, 0, Math.Min(length, 16)); // force lane overflow carries

            Assert.Equal(ReferenceChecksum(bytes), OpenTypeFontTable.CalcChecksum(bytes));
        }

        [Fact]
        public void FindHyphenationPoints_IsCaseInsensitive()
        {
            var lower = HyphenationEngine.FindHyphenationPoints("hyphenation", "en-US");
            Assert.NotEmpty(lower);
            Assert.Equal(lower, HyphenationEngine.FindHyphenationPoints("HYPHENATION", "en-US"));
            Assert.Equal(lower, HyphenationEngine.FindHyphenationPoints("Hyphenation", "en-US"));
        }

        [Theory]
        [InlineData(20)]
        [InlineData(23)]  // 253 chars, the longest wrapped word still on the stack is 256
        [InlineData(24)]  // 264 chars, spills to the heap
        [InlineData(60)]
        public void FindHyphenationPoints_LongWords_RepeatTheShortWordsBreaks(int repeats)
        {
            var one = HyphenationEngine.FindHyphenationPoints("hyphenation", "en-US");
            var word = string.Concat(Enumerable.Repeat("hyphenation", repeats));
            var points = HyphenationEngine.FindHyphenationPoints(word, "en-US");

            // The first copy is hyphenated exactly as it is on its own; the span/heap boundary changes nothing.
            Assert.Equal(one, points.Where(p => p <= 8));
            Assert.True(points.Count >= repeats);
        }

        [Fact]
        public void HasExplicitRanges_IgnoresCase_AndSeesAFaceAddedAfterTheAnswerWasCached()
        {
            var resolver = new FontResolver { NullIfFontNotFound = true };
            resolver.AddFont(File.OpenRead(BundledFonts.Ttf), "MixedCase");

            Assert.False(resolver.HasExplicitRanges("MixedCase"));
            Assert.False(resolver.HasExplicitRanges("MIXEDCASE"));
            Assert.False(resolver.HasExplicitRanges("nonexistent family"));
            Assert.True(resolver.TryGetFamilyName("mixedCASE", out var display));
            Assert.Equal("MixedCase", display);

            resolver.AddFont(File.OpenRead(BundledFonts.Otf), "mixedcase", null, null, null,
                [new RuneInterval(new Rune(0x61), new Rune(0x7A))]);

            Assert.True(resolver.HasExplicitRanges("MixedCase"));
            Assert.True(resolver.HasExplicitRanges("MIXEDCASE"));
        }
    }
}
