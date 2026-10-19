using System.IO;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// An independent statement of how a GIF frame's LZW codes are re-packed for PDF's <c>/LZWDecode</c>, so the pass-through embedding tests
    /// can check the stream a document carries against it without reaching into the PDF writer. GIF packs codes least-significant-bit-first
    /// and widens the code size the moment the table is full; PDF packs most-significant-bit-first and widens one code earlier.
    /// </summary>
    internal static class GifLzwReference
    {
        public static byte[] RepackGifLzwForPdf(byte[] gifLzwData)
        {
            const int clearCode = 256;
            const int endCode = 257;
            const int maxCodeTableSize = 4096;

            var inBytePos = 0;
            ulong inBitBuffer = 0;
            var inBitCount = 0;

            bool TryReadLsbFirst(int bits, out int code)
            {
                while (inBitCount < bits)
                {
                    if (inBytePos >= gifLzwData.Length)
                    {
                        code = 0;
                        return false;
                    }

                    inBitBuffer |= (ulong)gifLzwData[inBytePos++] << inBitCount;
                    inBitCount += 8;
                }

                code = (int)(inBitBuffer & ((1UL << bits) - 1));
                inBitBuffer >>= bits;
                inBitCount -= bits;
                return true;
            }

            using var output = new MemoryStream();
            var outBitBuffer = 0;
            var outBitCount = 0;

            void WriteMsbFirst(int code, int bits)
            {
                outBitBuffer = (outBitBuffer << bits) | code;
                outBitCount += bits;
                while (outBitCount >= 8)
                {
                    outBitCount -= 8;
                    output.WriteByte((byte)((outBitBuffer >> outBitCount) & 0xFF));
                }

                outBitBuffer &= (1 << outBitCount) - 1;
            }

            var nextCode = endCode + 1;
            int readCodeSize = 9, readMaxCode = 1 << 9;
            int writeCodeSize = 9, writeMaxCode = 1 << 9;
            var prevCode = -1;

            while (TryReadLsbFirst(readCodeSize, out var code))
            {
                WriteMsbFirst(code, writeCodeSize);

                if (code == clearCode)
                {
                    nextCode = endCode + 1;
                    readCodeSize = writeCodeSize = 9;
                    readMaxCode = writeMaxCode = 1 << 9;
                    prevCode = -1;
                    continue;
                }

                if (code == endCode)
                    break;

                if (prevCode != -1 && nextCode < maxCodeTableSize)
                {
                    nextCode++;
                    if (nextCode == readMaxCode && readCodeSize < 12)
                    {
                        readCodeSize++;
                        readMaxCode = 1 << readCodeSize;
                    }

                    if (nextCode == writeMaxCode - 1 && writeCodeSize < 12)
                    {
                        writeCodeSize++;
                        writeMaxCode = 1 << writeCodeSize;
                    }
                }

                prevCode = code;
            }

            if (outBitCount > 0)
                output.WriteByte((byte)((outBitBuffer << (8 - outBitCount)) & 0xFF));

            return output.ToArray();
        }
    }
}
