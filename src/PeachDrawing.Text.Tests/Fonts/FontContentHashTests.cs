using PeachDrawing.Text.Internal.Fonts;
using PeachPDF.Tests.TestSupport;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// The identity of a font file's bytes must be a collision-resistant hash: the process-wide font source cache and the face names
    /// of one resolver are keyed by it, and a font from a document that shared another's Adler-32 (which takes three edited bytes)
    /// used to be served the other's parsed data.
    /// </summary>
    public class FontContentHashTests
    {
        // ----- The hash itself -----

        [Fact]
        public void Compute_KnownVectors_AreTheFirst128BitsOfTheSha256()
        {
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb924", FontContentHash.Compute([]).ToString());
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223", FontContentHash.Compute("abc"u8.ToArray()).ToString());
        }

        [Fact]
        public void Compute_OfABundledFont_IsTheTruncatedSha256OfTheFile()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);

            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant(), FontContentHash.Compute(bytes).ToString());
        }

        [Fact]
        public void PortableSha256_MatchesThePlatformDigest_AtEveryPaddingBoundary()
        {
            var random = new Random(1470);
            // Around one and two blocks (the length field needs 9 bytes of the last block), plus a large buffer.
            int[] lengths = [0, 1, 54, 55, 56, 57, 63, 64, 65, 118, 119, 120, 127, 128, 129, 1000, 100_003];
            foreach (int length in lengths)
            {
                byte[] data = new byte[length];
                random.NextBytes(data);
                byte[] portable = new byte[32];

                PortableSha256.Hash(data, portable);

                Assert.Equal(SHA256.HashData(data), portable);
            }
        }

        [Fact]
        public void Compute_WithoutAPlatformSha256_GivesTheSameHash()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);
            var expected = FontContentHash.Compute(bytes);

            // A platform that throws, or that says it cannot, is left to the portable digest.
            Assert.Equal(expected, FontContentHash.Compute(bytes, static (_, _) => throw new PlatformNotSupportedException()));
            Assert.Equal(expected, FontContentHash.Compute(bytes, static (_, _) => throw new CryptographicException()));
            Assert.Equal(expected, FontContentHash.Compute(bytes, static (_, _) => false));
        }

        [Fact]
        public void Compute_LetsAnUnrelatedFailureOfThePlatformThrough()
        {
            Assert.Throws<InvalidOperationException>(() => FontContentHash.Compute([1], static (_, _) => throw new InvalidOperationException()));
        }

        [Fact]
        public void Default_IsEmpty_AndAComputedHashIsNot()
        {
            Assert.True(default(FontContentHash).IsEmpty);
            Assert.False(FontContentHash.Compute([1]).IsEmpty);
        }

        [Fact]
        public void Equality_FollowsTheBytes()
        {
            var one = FontContentHash.Compute([1, 2, 3]);
            var same = FontContentHash.Compute([1, 2, 3]);
            var other = FontContentHash.Compute([1, 2, 4]);

            Assert.True(one == same);
            Assert.True(one.Equals((object)same));
            Assert.Equal(one.GetHashCode(), same.GetHashCode());
            Assert.True(one != other);
            Assert.False(one.Equals((object)"not a hash"));
        }

        [Fact]
        public void GetOrComputeHash_RemembersTheHashOfABuffer()
        {
            byte[] bytes = File.ReadAllBytes(BundledFonts.Ttf);

            var first = FontFileData.GetOrComputeHash(bytes);

            Assert.Equal(FontContentHash.Compute(bytes), first);
            Assert.Equal(first, FontFileData.GetOrComputeHash(bytes));
        }

        // ----- Two fonts with equal Adler-32 -----

        [Fact]
        public void ConstructedVariants_AreDifferentFontsWithEqualLengthAndAdler32()
        {
            byte[] original = File.ReadAllBytes(BundledFonts.Ttf);
            var (b, _) = Variant(original, 1);
            var (c, _) = Variant(original, 2);

            // The premise of the tests below: the old key could not tell these apart.
            Assert.Equal(Adler32(original), Adler32(b));
            Assert.Equal(Adler32(original), Adler32(c));
            Assert.Equal(original.Length, b.Length);
            Assert.NotEqual(original, b);
            Assert.NotEqual(b, c);
            Assert.NotEqual(FontContentHash.Compute(original), FontContentHash.Compute(b));
            Assert.NotEqual(FontContentHash.Compute(b), FontContentHash.Compute(c));
        }

        [Fact]
        public void FontsWithEqualAdler32_DoNotShareACacheEntry_AndEachReadsItsOwnData()
        {
            byte[] original = File.ReadAllBytes(BundledFonts.Ttf);
            var (bytesB, glyphB) = Variant(original, 1);
            var (bytesC, glyphC) = Variant(original, 2);

            var sourceA = FontFileData.GetOrCreateFrom(original);
            var sourceB = FontFileData.GetOrCreateFrom(bytesB);
            var sourceC = FontFileData.GetOrCreateFrom(bytesC);

            Assert.NotSame(sourceA, sourceB);
            Assert.NotSame(sourceB, sourceC);
            Assert.NotEqual(sourceA.Key, sourceB.Key);
            Assert.NotEqual(sourceB.Key, sourceC.Key);
            Assert.NotEqual(sourceB.KeyText, sourceC.KeyText);

            // Each source holds and parses its own bytes: the one glyph whose advance was edited reads the edited advance, in that
            // font only.
            Assert.Same(bytesB, sourceB.Bytes);
            Assert.Same(bytesC, sourceC.Bytes);
            Assert.Equal(AdvanceIn(sourceA, glyphB) + Widening, AdvanceIn(sourceB, glyphB));
            Assert.Equal(AdvanceIn(sourceA, glyphC) + Widening, AdvanceIn(sourceC, glyphC));
            Assert.Equal(AdvanceIn(sourceA, glyphB), AdvanceIn(sourceC, glyphB));
            Assert.Equal(AdvanceIn(sourceA, glyphC), AdvanceIn(sourceB, glyphC));

            // A copy of the content still finds the entry that holds it (content-addressed identity, not buffer identity).
            Assert.Same(sourceB, FontFileData.GetOrCreateFrom((byte[])bytesB.Clone()));
            Assert.Same(sourceC, FontFileData.GetOrCreateFrom((byte[])bytesC.Clone()));
            Assert.Same(sourceA, FontFileData.GetOrCreateFrom((byte[])original.Clone()));
        }

        [Fact]
        public void FontsWithEqualAdler32_GetDifferentFaceNames_AndEachFetchesItsOwnBytes()
        {
            byte[] original = File.ReadAllBytes(BundledFonts.Ttf);
            var (bytesB, _) = Variant(original, 1);
            var (bytesC, _) = Variant(original, 2);

            var resolver = new FontResolver();
            resolver.AddFont(new MemoryStream(original), "FamA");
            resolver.AddFont(new MemoryStream(bytesB), "FamB");
            resolver.AddFont(new MemoryStream(bytesC), "FamC");

            var faceA = resolver.ResolveTypeface("FamA", 400, false).FaceName;
            var faceB = resolver.ResolveTypeface("FamB", 400, false).FaceName;
            var faceC = resolver.ResolveTypeface("FamC", 400, false).FaceName;

            // B and C both differ from what A registered under the internal name, and used to get one and the same suffix.
            Assert.NotEqual(faceB, faceC);
            Assert.NotEqual(faceA, faceB);
            Assert.Equal(original, resolver.GetFont(faceA));
            Assert.Equal(bytesB, resolver.GetFont(faceB));
            Assert.Equal(bytesC, resolver.GetFont(faceC));

            // The suffix is the content hash, all of it.
            Assert.EndsWith("#" + FontContentHash.Compute(bytesB), faceB);
            Assert.EndsWith("#" + FontContentHash.Compute(bytesC), faceC);
        }

        [Fact]
        public async Task TheCache_UnderConcurrentLookups_GivesOneSourcePerContent()
        {
            byte[] original = File.ReadAllBytes(BundledFonts.Otf);
            var contents = new List<byte[]> { original };
            for (int i = 1; i <= 4; i++)
                contents.Add(Variant(original, i).Bytes);

            var results = new FontFileData[64][];
            await Task.WhenAll(Enumerable.Range(0, results.Length).Select(worker => Task.Run(() =>
            {
                // A fresh copy per lookup, so neither the buffer memo nor a shared reference can stand in for content identity.
                results[worker] = contents.Select(content => FontFileData.GetOrCreateFrom((byte[])content.Clone())).ToArray();
            })));

            for (int content = 0; content < contents.Count; content++)
            {
                var sources = results.Select(row => row[content]).Distinct().ToList();
                Assert.Single(sources);
                Assert.Equal(contents[content], sources[0].Bytes);
            }
            Assert.Equal(contents.Count, results[0].Distinct().Count());
        }

        // ----- Construction of the colliding fonts -----

        // Widening one advance width by the +1, -2, +1 edit below is +256 - 2.
        private const int Widening = 254;

        private static uint Adler32(byte[] bytes)
        {
            uint s1 = 1, s2 = 0;
            foreach (byte value in bytes)
            {
                s1 = (s1 + value) % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return (s2 << 16) | s1;
        }

        private static int AdvanceIn(FontFileData source, int glyph) => source.Fontface.hmtx.Metrics[glyph].advanceWidth;

        /// <summary>
        /// A copy of a font whose <c>hmtx</c> entry of the <paramref name="ordinal"/>th glyph that can take the edit is changed by
        /// adding 1, -2 and 1 to three consecutive bytes. The second difference of the byte weights of an Adler-32 is zero, so the
        /// sums stay equal and so does the length, while the font is still a font: that glyph is 254 units wider.
        /// </summary>
        private static (byte[] Bytes, int Glyph) Variant(byte[] original, int ordinal)
        {
            int hmtx = TableOffset(original, "hmtx");
            byte[] copy = (byte[])original.Clone();
            int found = 0;
            for (int glyph = 1; glyph < 200; glyph++)
            {
                int p = hmtx + 4 * glyph;
                // advance high, advance low, side bearing high: none of them may wrap.
                if (copy[p] == 255 || copy[p + 1] < 2 || copy[p + 2] == 255)
                    continue;
                if (++found < ordinal)
                    continue;

                copy[p] += 1;
                copy[p + 1] -= 2;
                copy[p + 2] += 1;
                return (copy, glyph);
            }
            throw new InvalidOperationException("The font has no hmtx entries the edit can take.");
        }

        private static int TableOffset(byte[] font, string tag)
        {
            int tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (int i = 0; i < tableCount; i++)
            {
                int record = 12 + 16 * i;
                if (System.Text.Encoding.ASCII.GetString(font, record, 4) == tag)
                    return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            }
            throw new InvalidOperationException($"No {tag} table.");
        }
    }
}
