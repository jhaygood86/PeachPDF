// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// The LZW bit-order conversion that lets a GIF frame be embedded without decoding it: GIF packs its codes least significant bit
/// first, PDF's <c>/LZWDecode</c> most significant bit first.
/// </summary>
internal static class PdfGifLzw
{
    /// <summary>
    /// Re-packs a GIF frame's LZW-compressed bytes (bit-packed least-significant-bit-first, GIF's own
    /// convention - see <see cref="PdfImage"/>'s GIF passthrough's own remarks) into PDF <c>/LZWDecode</c>'s
    /// bit-packing convention (most-significant-bit-first). Preserves the exact same sequence of code
    /// values and code widths GIF's own encoder chose - both conventions grow from 9 to 12 bits using
    /// the identical "early change" trigger (widen the moment the code table becomes full for the
    /// current width, confirmed against PeachImage's own <c>GifLzwEncoder</c>/<c>GifLzwDecoder</c>
    /// source, matching PDF's own default <c>/EarlyChange 1</c>) - so this only ever needs to track
    /// that shared code-table-size bookkeeping to find code boundaries, never PeachImage's actual
    /// prefix/suffix dictionary contents (no pixel decode happens here). Hardcodes GIF's Clear/End
    /// codes at 256/257 (start code width 9) rather than deriving them from a <c>MinCodeSize</c>
    /// parameter, since <see cref="ImageSource.IImageSource.GifPassthrough"/>'s eligibility gate
    /// already guarantees <c>MinCodeSize == 8</c> for every caller - the same fixed values PDF's
    /// <c>/LZWDecode</c> itself always uses (it has no <c>MinCodeSize</c> concept of its own).
    /// </summary>
    internal static byte[] RepackGifLzwForPdf(byte[] gifLzwData)
    {
        const int clearCode = 256;
        const int endCode = 257;
        const int maxCodeTableSize = 4096;

        int inBytePos = 0;
        ulong inBitBuffer = 0;
        int inBitCount = 0;
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
        int outBitBuffer = 0;
        int outBitCount = 0;
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

        // GIF and PDF/TIFF both grow code width from 9 to 12 bits using "early change" timing, but not
        // at the *same* code count: GIF grows the moment the table becomes full for the current width
        // (nextCode == maxCode - confirmed against PeachImage's own GifLzwEncoder/GifLzwDecoder source),
        // while PDF/TIFF grows one code earlier (nextCode == maxCode - 1 - confirmed empirically against
        // a real libtiff-generated LZW stream: decoding it with GIF's own trigger diverges exactly at
        // the first width-growth boundary, decoding with this one-earlier trigger matches perfectly).
        // So this needs two independent code-width trackers sharing one nextCode counter: readCodeSize
        // to find code boundaries in the GIF bitstream, writeCodeSize to decide how many bits the *same*
        // code value needs in the PDF bitstream - the two can differ for a stretch around each boundary.
        int nextCode = endCode + 1;
        int readCodeSize = 9, readMaxCode = 1 << 9;
        int writeCodeSize = 9, writeMaxCode = 1 << 9;
        int prevCode = -1;

        while (TryReadLsbFirst(readCodeSize, out int code))
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
            {
                break;
            }

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
        {
            output.WriteByte((byte)((outBitBuffer << (8 - outBitCount)) & 0xFF));
        }

        return output.ToArray();
    }
}
