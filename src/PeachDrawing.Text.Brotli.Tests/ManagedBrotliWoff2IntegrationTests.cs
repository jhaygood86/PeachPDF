using System.Text;
using PeachDrawing.Text;
using PeachDrawing.Text.Brotli;

namespace PeachDrawing.Text.Brotli.Tests
{
    /// <summary>
    /// End-to-end proof that a real WOFF2 font loads correctly through the managed decoder: not just that the raw Brotli
    /// bytes match the BCL's, but that the whole font-loading pipeline (WOFF2 table decompression, glyf/loca reconstruction,
    /// cmap, metrics) produces the exact same result as loading the same file with the BCL's own decoder. This is the case
    /// PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor exists for.
    /// </summary>
    /// <remarks>
    /// Runs sequentially (not in parallel with other tests in this collection) because <c>SetDecompressor</c> is process-wide,
    /// mutable, shared state - see <see cref="ManagedBrotliRegistrationCollection"/>.
    /// </remarks>
    [Collection(ManagedBrotliRegistrationCollection.Name)]
    public class ManagedBrotliWoff2IntegrationTests : IDisposable
    {
        private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Inter-Medium.woff2");

        public ManagedBrotliWoff2IntegrationTests()
        {
            // Belt-and-suspenders: every test starts from "no custom decoder registered", regardless of what a previous test
            // (in this class or elsewhere) left behind.
            ManagedBrotliDecompressor.Unregister();
        }

        public void Dispose() => ManagedBrotliDecompressor.Unregister();

        [Fact]
        public void LoadingThroughTheManagedDecoderMatchesLoadingThroughTheBcl()
        {
            Assert.True(File.Exists(FontPath), $"Test font not found at {FontPath}");

            var bcl = LoadTypeface(registerManagedDecoder: false);
            var managed = LoadTypeface(registerManagedDecoder: true);

            Assert.Equal(bcl.FamilyName, managed.FamilyName);
            Assert.Equal(bcl.Metrics.UnitsPerEm, managed.Metrics.UnitsPerEm);
            Assert.Equal(bcl.Metrics.NormalLineAscent, managed.Metrics.NormalLineAscent);
            Assert.Equal(bcl.Metrics.NormalLineDescent, managed.Metrics.NormalLineDescent);

            // A handful of characters actually present in "Inter": exercise cmap, advances and real outline data (glyf/loca,
            // which WOFF2 reconstructs from its own transformed table format - not a pass-through of the sfnt bytes).
            foreach (var ch in "Inter0123456789.,!?")
            {
                var rune = new Rune(ch);
                Assert.Equal(bcl.TryMapRune(rune, out var bclGlyph), managed.TryMapRune(rune, out var managedGlyph));
                Assert.Equal(bclGlyph, managedGlyph);

                Assert.Equal(bcl.GetAdvance(bclGlyph), managed.GetAdvance(managedGlyph));

                var bclHasOutline = bcl.TryGetOutline(bclGlyph, out var bclOutline);
                var managedHasOutline = managed.TryGetOutline(managedGlyph, out var managedOutline);
                Assert.Equal(bclHasOutline, managedHasOutline);
                if (bclHasOutline)
                {
                    Assert.Equal(bclOutline.Contours.Count, managedOutline.Contours.Count);
                    for (int c = 0; c < bclOutline.Contours.Count; c++)
                    {
                        var bclContour = bclOutline.Contours[c];
                        var managedContour = managedOutline.Contours[c];
                        Assert.Equal(bclContour.Start, managedContour.Start);
                        Assert.Equal(bclContour.Segments.Count, managedContour.Segments.Count);
                        for (int s = 0; s < bclContour.Segments.Count; s++)
                        {
                            var bclSegment = bclContour.Segments[s];
                            var managedSegment = managedContour.Segments[s];
                            Assert.Equal(bclSegment.IsCubic, managedSegment.IsCubic);
                            Assert.Equal(bclSegment.Control1, managedSegment.Control1);
                            Assert.Equal(bclSegment.Control2, managedSegment.Control2);
                            Assert.Equal(bclSegment.End, managedSegment.End);
                        }
                    }
                }
            }
        }

        private static Typeface LoadTypeface(bool registerManagedDecoder)
        {
            if (registerManagedDecoder)
            {
                ManagedBrotliDecompressor.Register();
            }
            else
            {
                ManagedBrotliDecompressor.Unregister();
            }

            try
            {
                var fontSet = new FontSet();
                var family = fontSet.AddFile(FontPath);
                Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
                return match.Typeface;
            }
            finally
            {
                ManagedBrotliDecompressor.Unregister();
            }
        }
    }

    /// <summary>
    /// Groups every test that registers <see cref="ManagedBrotliDecompressor"/> so xunit runs them sequentially:
    /// <c>PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor</c> is process-wide mutable state (by design - see
    /// its own remarks), and xunit otherwise runs test classes in parallel.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ManagedBrotliRegistrationCollection
    {
        public const string Name = "Managed Brotli registration";
    }
}
