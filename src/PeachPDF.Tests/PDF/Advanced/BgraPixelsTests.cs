using PeachPDF.PDF.Internal;

namespace PeachPDF.Tests.PdfEngineTests.Advanced;

public class BgraPixelsTests
{
    public static TheoryData<int> Widths() => new() { 0, 1, 2, 3, 7, 8, 9, 15, 16, 17, 31, 32, 33, 100 };

    [Theory]
    [MemberData(nameof(Widths))]
    public void SplitRow_SeparatesChannelsPerPixel(int width)
    {
        byte[] bgra = new byte[4 * width];
        new Random(width).NextBytes(bgra);
        byte[] rgb = new byte[3 * width];
        byte[] alpha = new byte[width];

        BgraPixels.SplitRow(bgra, rgb, alpha);

        for (int i = 0; i < width; i++)
        {
            Assert.Equal(bgra[4 * i + 2], rgb[3 * i]);
            Assert.Equal(bgra[4 * i + 1], rgb[3 * i + 1]);
            Assert.Equal(bgra[4 * i], rgb[3 * i + 2]);
            Assert.Equal(bgra[4 * i + 3], alpha[i]);
        }
    }

    [Fact]
    public void SplitRow_RejectsShortBuffers()
    {
        Assert.Throws<ArgumentException>(() => BgraPixels.SplitRow(new byte[7], new byte[6], new byte[2]));
        Assert.Throws<ArgumentException>(() => BgraPixels.SplitRow(new byte[8], new byte[5], new byte[2]));
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void PackTransparency_SetsOneBitPerTransparentPixel_MostSignificantFirst(int width)
    {
        byte[] alpha = new byte[width];
        new Random(width + 1).NextBytes(alpha);
        byte[] packed = new byte[(width + 7) / 8];
        Array.Fill(packed, (byte)0xFF);   // stale content must be overwritten

        BgraPixels.PackTransparency(alpha, packed);

        for (int i = 0; i < width; i++)
        {
            bool bit = (packed[i / 8] & (0x80 >> (i % 8))) != 0;
            Assert.Equal(alpha[i] < 128, bit);
        }

        // The padding bits of the last byte stay clear.
        for (int i = width; i < packed.Length * 8; i++)
            Assert.Equal(0, packed[i / 8] & (0x80 >> (i % 8)));
    }

    [Fact]
    public void PackTransparency_RejectsAShortMaskRow()
    {
        Assert.Throws<ArgumentException>(() => BgraPixels.PackTransparency(new byte[9], new byte[1]));
    }

    [Theory]
    [InlineData(new byte[] { 255, 255, 255 }, false, false)]
    [InlineData(new byte[] { 255, 0, 255 }, true, false)]
    [InlineData(new byte[] { 0, 0 }, true, false)]
    [InlineData(new byte[] { 255, 254 }, true, true)]
    [InlineData(new byte[] { 1 }, true, true)]
    [InlineData(new byte[0], false, false)]
    public void Classify_ReportsTransparencyAndPartialTransparency(byte[] alpha, bool transparent, bool partial)
    {
        bool anyTransparent = false;
        bool anyPartlyTransparent = false;

        BgraPixels.Classify(alpha, ref anyTransparent, ref anyPartlyTransparent);

        Assert.Equal(transparent, anyTransparent);
        Assert.Equal(partial, anyPartlyTransparent);
    }

    [Fact]
    public void Classify_NeverClearsAFlagThatIsAlreadySet()
    {
        bool anyTransparent = true;
        bool anyPartlyTransparent = true;

        BgraPixels.Classify(new byte[] { 255, 255 }, ref anyTransparent, ref anyPartlyTransparent);

        Assert.True(anyTransparent);
        Assert.True(anyPartlyTransparent);
    }
}
