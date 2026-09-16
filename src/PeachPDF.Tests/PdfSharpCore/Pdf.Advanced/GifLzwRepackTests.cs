using PeachImage;
using PeachImage.Formats.Gif;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System.IO;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    /// <summary>
    /// Covers <see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf"/> directly (no PDF rendering/rasterization needed -
    /// this is a pure byte-transform), verifying it against an independent, from-scratch MSB-first
    /// (PDF/TIFF-convention) LZW decoder rather than round-tripping through the same repack logic.
    /// </summary>
    /// <remarks>
    /// This transform exists because GIF and PDF's <c>/LZWDecode</c> are NOT byte-compatible the way
    /// <c>MinCodeSize == 8</c> alone suggests - two separate, real incompatibilities, both found only by
    /// actually rasterizing a naive verbatim-bytes embed and watching PDFium and MuPDF agree on corrupted
    /// output (the exact pitfall this repo's own testing conventions warn about - a token or a small
    /// fixture passing is not proof a pass-through payload is byte-correct):
    /// <list type="bullet">
    /// <item>Bit order: GIF packs LZW codes least-significant-bit-first; PDF/TIFF packs them
    /// most-significant-bit-first.</item>
    /// <item>Code-width growth timing: both use "early change" semantics, but at different code counts -
    /// GIF grows when its table becomes full for the current width (confirmed against PeachImage's own
    /// <c>GifLzwEncoder</c>/<c>GifLzwDecoder</c> source), while PDF/TIFF grows <em>one code earlier</em>
    /// (confirmed empirically: decoding a real libtiff-generated LZW stream - via Pillow's <c>tiff_lzw</c>
    /// compression - with GIF's own trigger diverges exactly at the first growth boundary; with this
    /// one-earlier trigger it decodes perfectly). A small (&lt;256-code) fixture never reaches either
    /// boundary and can't catch a timing bug here - <see cref="LargeFullPaletteGif_RepackedStream_DecodesToOriginalPixels"/>
    /// is deliberately large enough (4096 codes) to cross the first two growth boundaries (9→10 bits at
    /// 511 codes, 10→11 at 1023).
    /// </list>
    /// </remarks>
    public class GifLzwRepackTests
    {
        /// <summary>
        /// A from-scratch MSB-first LZW decoder implementing PDF/TIFF's own conventions (Clear=256,
        /// End=257, starts at 9-bit codes, widens one code earlier than GIF's own trigger) - written
        /// independently of <see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf"/> so this test can't just be
        /// validating the repack logic against its own assumptions.
        /// </summary>
        private static byte[] DecodePdfStyleMsbFirstLzw(byte[] data, int pixelCount)
        {
            const int clearCode = 256;
            const int endCode = 257;
            const int maxCodeTableSize = 4096;

            int bytePos = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            bool TryReadCode(int bits, out int code)
            {
                while (bitCount < bits)
                {
                    if (bytePos >= data.Length) { code = 0; return false; }
                    bitBuffer = (bitBuffer << 8) | data[bytePos++];
                    bitCount += 8;
                }
                bitCount -= bits;
                code = (bitBuffer >> bitCount) & ((1 << bits) - 1);
                return true;
            }

            var prefix = new ushort[maxCodeTableSize];
            var suffix = new byte[maxCodeTableSize];
            var stack = new byte[maxCodeTableSize];
            var output = new byte[pixelCount];
            int writePos = 0;

            int codeSize = 9;
            int nextCode = endCode + 1;
            int maxCode = 1 << codeSize;
            int prevCode = -1;

            while (writePos < pixelCount && TryReadCode(codeSize, out int code))
            {
                if (code == clearCode)
                {
                    nextCode = endCode + 1;
                    codeSize = 9;
                    maxCode = 1 << codeSize;
                    prevCode = -1;
                    continue;
                }
                if (code == endCode) break;

                bool isNewCode = code == nextCode && prevCode != -1;
                int stackTop = 0;
                int c = isNewCode ? prevCode : code;
                while (c >= endCode + 1)
                {
                    stack[stackTop++] = suffix[c];
                    c = prefix[c];
                }
                stack[stackTop++] = (byte)c;
                byte firstChar = (byte)c;

                for (int i = stackTop - 1; i >= 0 && writePos < pixelCount; i--) output[writePos++] = stack[i];
                if (isNewCode && writePos < pixelCount) output[writePos++] = firstChar;

                if (prevCode != -1 && nextCode < maxCodeTableSize)
                {
                    prefix[nextCode] = (ushort)prevCode;
                    suffix[nextCode] = firstChar;
                    nextCode++;
                    // The one-code-earlier trigger this whole test class exists to lock in - see the class
                    // remarks. Changing this back to "== maxCode" (GIF's own trigger) should make
                    // LargeFullPaletteGif_RepackedStream_DecodesToOriginalPixels fail.
                    if (nextCode == maxCode - 1 && codeSize < 12) { codeSize++; maxCode = 1 << codeSize; }
                }
                prevCode = code;
            }

            return output;
        }

        [Fact]
        public void SmallGif_RepackedStream_DecodesToOriginalPixels()
        {
            // 16x16 = 256 pixels - enough to cover the full 0-255 palette range (MinCodeSize == 8, this
            // fixture's own eligibility floor - see RasterGifFixture.MakeFullPaletteGifBytes), but few
            // enough codes to never reach a code-width growth boundary - a baseline sanity check for bit
            // order and Clear/End handling, independent of the growth-timing bug the large fixture below
            // specifically targets.
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(16, 16);
            GifPassthrough.TryRead(new MemoryStream(bytes), out var info);
            using var decodedImage = Image.Load(new MemoryStream(bytes));
            var expectedRgb = decodedImage.GetPixelSpan();

            var pdfLzwData = PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf(info.LzwData);
            var actualIndices = DecodePdfStyleMsbFirstLzw(pdfLzwData, 16 * 16);

            for (int i = 0; i < actualIndices.Length; i++)
            {
                int idx = actualIndices[i];
                Assert.Equal(expectedRgb[i * 3], info.Palette[idx * 3]);
                Assert.Equal(expectedRgb[i * 3 + 1], info.Palette[idx * 3 + 1]);
                Assert.Equal(expectedRgb[i * 3 + 2], info.Palette[idx * 3 + 2]);
            }
        }

        [Fact]
        public void LargeFullPaletteGif_RepackedStream_DecodesToOriginalPixels()
        {
            // 4096 codes - crosses the 9->10 bit boundary (at 511 codes) and the 10->11 boundary (at
            // 1023), the exact scenario that caught the real bug (see the class remarks): a naive repack
            // reusing GIF's own growth trigger for the PDF-side width decoded correctly up to the first
            // boundary and diverged into visible corruption immediately after it.
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(64, 64);
            GifPassthrough.TryRead(new MemoryStream(bytes), out var info);
            using var decodedImage = Image.Load(new MemoryStream(bytes));
            var expectedRgb = decodedImage.GetPixelSpan();

            var pdfLzwData = PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf(info.LzwData);
            var actualIndices = DecodePdfStyleMsbFirstLzw(pdfLzwData, 64 * 64);

            for (int i = 0; i < actualIndices.Length; i++)
            {
                int idx = actualIndices[i];
                Assert.True(
                    expectedRgb[i * 3] == info.Palette[idx * 3] &&
                    expectedRgb[i * 3 + 1] == info.Palette[idx * 3 + 1] &&
                    expectedRgb[i * 3 + 2] == info.Palette[idx * 3 + 2],
                    $"Pixel {i} mismatch.");
            }
        }

        [Fact]
        public void TruncatedLzwData_StopsGracefullyWithoutThrowing()
        {
            // A GIF frame's LzwData always ends with an End code (257) by construction (a real,
            // successfully-parsed GifPassthroughInfo guarantees this - see GifPassthrough's own remarks),
            // but the repack's own bit reader still needs a defined, non-throwing behavior for a stream
            // that runs out mid-code, the same defensive posture GifLzwDecoder itself takes for
            // corrupt/truncated input.
            var bytes = RasterGifFixture.MakeFullPaletteGifBytes(16, 16);
            GifPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var truncated = info.LzwData[..(info.LzwData.Length / 2)];

            var pdfLzwData = PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage.RepackGifLzwForPdf(truncated);

            Assert.NotEmpty(pdfLzwData);
        }
    }
}
