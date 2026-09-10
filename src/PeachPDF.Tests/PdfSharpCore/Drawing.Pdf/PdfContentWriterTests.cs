using System.Globalization;
using System.Text;
using PeachPDF.PdfSharpCore.Drawing.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing.Pdf
{
    public class PdfContentWriterTests
    {
        [Fact]
        public void ConstructorsStartEmpty()
        {
            PdfContentWriter[] writers = [new PdfContentWriter(), new PdfContentWriter(17)];

            foreach (PdfContentWriter writer in writers)
            {
                Assert.Equal(0, writer.Length);
                Assert.Empty(writer.ToArray());
                Assert.Equal(string.Empty, writer.ToString());
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void FixedChunkConstructorRejectsNonPositiveSize(int chunkSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PdfContentWriter(chunkSize));
        }

        [Fact]
        public void AppendCharWritesRawLowByteAndSupportsRepeatedAppends()
        {
            var content = new PdfContentWriter(2);

            Assert.Same(content, content.Append('A'));
            content.Append('\u0101');
            content.Append('B');

            Assert.Equal([(byte)'A', 0x01, (byte)'B'], content.ToArray());
        }

        [Fact]
        public void AppendStringHandlesNullEmptyAndOrdinaryContent()
        {
            var content = new PdfContentWriter();
            content.Append("before");
            int length = content.Length;

            Assert.Same(content, content.Append((string?)null));
            Assert.Same(content, content.Append(string.Empty));
            Assert.Equal(length, content.Length);

            content.Append(" after");
            Assert.Equal("before after", content.ToString());
        }

        [Fact]
        public void AppendCharSpanHandlesEmptyExactFitAndMultiChunkInput()
        {
            var content = new PdfContentWriter(4);
            content.Append('A');

            Assert.Same(content, content.Append(ReadOnlySpan<char>.Empty));
            content.Append("BCD".AsSpan());
            Assert.Equal(4, content.Length);

            content.Append("EFGHI".AsSpan());
            Assert.Equal("ABCDEFGHI", content.ToString());
        }

        [Fact]
        public void AppendByteSpanHandlesEmptyExactFitMultiChunkInputAndEveryByteValue()
        {
            var content = new PdfContentWriter(16);
            var expected = new byte[256];
            for (int i = 0; i < expected.Length; i++)
                expected[i] = (byte)i;

            Assert.Same(content, content.Append(ReadOnlySpan<byte>.Empty));
            content.Append(expected.AsSpan(0, 1));
            content.Append(expected.AsSpan(1, 15));
            Assert.Equal(16, content.Length);

            content.Append(expected.AsSpan(16));
            Assert.Equal(expected, content.ToArray());
        }

        [Fact]
        public void AppendFormatOverloadsUseProviderAndResetSharedBuffer()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
                var content = new PdfContentWriter();
                object?[] arguments = ["array", 1.25];

                Assert.Same(content,
                    content.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1:0.00};", arguments));
                Assert.Same(content,
                    content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.00};", 2.5));
                Assert.Same(content,
                    content.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1:0.00};", "two", 3.5));
                Assert.Same(content,
                    content.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}:{2:0.00}", "three", "args", 4.5));

                Assert.Equal("array:1.25;2.50;two:3.50;three:args:4.50", content.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void AppendFormatCanCrossAdaptiveGrowthBoundaryInOneCall()
        {
            string value = new('x', 600);
            var content = new PdfContentWriter();

            content.AppendFormat(CultureInfo.InvariantCulture, "<{0}>", value);

            Assert.Equal($"<{value}>", content.ToString());
            Assert.Equal(602, content.Length);
        }

        [Fact]
        public void AdaptiveChunksPreserveContentAcrossEveryGrowthBoundary()
        {
            int[] checkpoints = [255, 256, 1_279, 1_280, 5_375, 5_376, 21_759, 21_760, 38_143, 38_144, 38_145];
            var content = new PdfContentWriter();
            var expected = new StringBuilder(checkpoints[^1]);

            foreach (int checkpoint in checkpoints)
            {
                while (expected.Length < checkpoint)
                {
                    char value = (char)('!' + expected.Length % 90);
                    content.Append(value);
                    expected.Append(value);
                }

                Assert.Equal(checkpoint, content.Length);
            }

            string expectedText = expected.ToString();
            Assert.Equal(Encoding.ASCII.GetBytes(expectedText), content.ToArray());
            Assert.Equal(expectedText, content.ToString());
        }

        [Fact]
        public void ToStringPreservesHighByteValues()
        {
            var bytes = new byte[128];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(i + 0x80);

            var content = new PdfContentWriter(17);
            content.Append(bytes);

            string text = content.ToString();
            Assert.Equal(bytes.Length, text.Length);
            for (int i = 0; i < bytes.Length; i++)
                Assert.Equal((char)bytes[i], text[i]);
        }

        [Fact]
        public void ClearResetsAdaptiveGrowthAndSupportsReuse()
        {
            var empty = new PdfContentWriter();
            empty.Clear();
            Assert.Equal(0, empty.Length);
            Assert.Empty(empty.ToArray());
            Assert.Equal(string.Empty, empty.ToString());

            var content = new PdfContentWriter();
            content.Append(new byte[6_000]);
            content.Clear();

            Assert.Equal(0, content.Length);
            Assert.Empty(content.ToArray());
            Assert.Equal(string.Empty, content.ToString());

            long before = GC.GetAllocatedBytesForCurrentThread();
            content.Append('R');
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 1_024,
                $"The first chunk after Clear allocated {allocated:N0} bytes instead of resetting to 256 bytes.");

            string reused = "Reused after growth " + new string('z', 300);
            content.Append(reused.AsSpan(1));
            Assert.Equal(reused, content.ToString());
            Assert.Equal(Encoding.ASCII.GetBytes(reused), content.ToArray());
            Assert.Equal(reused.Length, content.Length);
        }

        [Fact]
        public void AppendMethodsSupportFluentChaining()
        {
            var content = new PdfContentWriter();

            PdfContentWriter returned = content
                .Append('A')
                .Append("B")
                .Append("C".AsSpan())
                .Append([(byte)'D'])
                .AppendFormat(CultureInfo.InvariantCulture, "{0}", "E");

            Assert.Same(content, returned);
            Assert.Equal("ABCDE", content.ToString());
        }
    }
}
