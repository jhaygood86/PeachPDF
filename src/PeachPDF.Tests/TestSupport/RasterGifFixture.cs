using PeachImage;
using PeachImage.Formats.Gif;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Builds real, decodable GIF fixtures for pass-through eligibility tests. Most cases go through
    /// PeachImage's own <c>GifEncoder</c> (a hand-picked minimal file isn't reliably decodable - see
    /// <c>RasterPngFixture</c>'s own reasoning); interlaced and partial-canvas frames aren't producible by
    /// that encoder (it never writes either), so those go through <see cref="BuildCustomGif"/> instead - a
    /// minimal hand-rolled LZW writer using GIF's "no compression" encoding (every code is a fresh literal
    /// symbol, never a back-reference), which still produces spec-valid, correctly-decodable data since the
    /// decoder's dictionary-growth bookkeeping only depends on how many codes have been emitted, not on
    /// what they encode.
    /// </summary>
    internal static class RasterGifFixture
    {
        /// <summary>
        /// A real GIF with a genuinely full 256-distinct-color palette (each pixel's raster index maps to
        /// one of 256 colors, guaranteeing every palette entry is used) - so <c>GifPassthroughInfo.MinCodeSize</c>
        /// is exactly 8, the pass-through eligibility gate.
        /// </summary>
        public static byte[] MakeFullPaletteGifBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < width * height; i++)
            {
                int color = i % 256;
                pixels[i * 3] = (byte)((color * 53) % 256);
                pixels[i * 3 + 1] = (byte)((color * 97) % 256);
                pixels[i * 3 + 2] = (byte)((color * 181) % 256);
            }

            using var ms = new MemoryStream();
            image.Save(ms, "gif", new GifEncoderOptions { MaxColors = 256 });
            return ms.ToArray();
        }

        /// <summary>A real GIF quantized to a small palette - <c>MinCodeSize &lt; 8</c>, disqualifying it from pass-through.</summary>
        public static byte[] MakeSmallPaletteGifBytes(int width, int height, int maxColors = 4)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                bool alt = (i / 3) % 2 == 0;
                pixels[i] = alt ? (byte)200 : (byte)20;
                pixels[i + 1] = alt ? (byte)80 : (byte)160;
                pixels[i + 2] = alt ? (byte)40 : (byte)220;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "gif", new GifEncoderOptions { MaxColors = maxColors });
            return ms.ToArray();
        }

        /// <summary>
        /// A real GIF with a transparent region - GifEncoder derives the transparent index from the
        /// source's alpha channel. Every opaque pixel gets a distinct color (same technique as
        /// <see cref="MakeFullPaletteGifBytes"/>) so the quantized palette is still large enough to reach
        /// <c>MinCodeSize == 8</c> - a transparent GIF whose opaque region is a single flat color would
        /// quantize to only 1-2 palette entries and never be pass-through-eligible on its own.
        /// </summary>
        public static byte[] MakeTransparentGifBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            var pixels = image.GetPixelSpan();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool transparent = x < width / 2;
                    int color = (y * width + x) % 255; // reserve one palette slot for the transparent index
                    pixels[i] = (byte)((color * 53) % 256);
                    pixels[i + 1] = (byte)((color * 97) % 256);
                    pixels[i + 2] = (byte)((color * 181) % 256);
                    pixels[i + 3] = transparent ? (byte)0 : (byte)255;
                }
            }

            using var ms = new MemoryStream();
            image.Save(ms, "gif", new GifEncoderOptions { MaxColors = 256 });
            return ms.ToArray();
        }

        /// <summary>An interlaced GIF - real palette-index data, but Adam7-style row-interlaced, disqualifying it from pass-through.</summary>
        public static byte[] MakeInterlacedGifBytes(int width, int height)
        {
            var (palette, indices) = BuildCheckerIndices(width, height);
            return BuildCustomGif(width, height, width, height, left: 0, top: 0, palette, indices, interlace: true, transparentIndex: null);
        }

        /// <summary>A GIF frame smaller than its own logical screen (canvas) - disqualifying it from pass-through (no canvas-compositing step in a byte-for-byte embed).</summary>
        public static byte[] MakePartialCanvasGifBytes(int canvasWidth, int canvasHeight, int frameWidth, int frameHeight)
        {
            var (palette, indices) = BuildCheckerIndices(frameWidth, frameHeight);
            return BuildCustomGif(canvasWidth, canvasHeight, frameWidth, frameHeight, left: 0, top: 0, palette, indices, interlace: false, transparentIndex: null);
        }

        private static (byte[] Palette, byte[] Indices) BuildCheckerIndices(int width, int height)
        {
            byte[] palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];
            var indices = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    indices[y * width + x] = (byte)((x + y) % palette.Length / 3);
                }
            }
            return (palette, indices);
        }

        /// <summary>
        /// Hand-builds a minimal, spec-valid GIF89a: header, logical screen descriptor + global color
        /// table, optional Graphic Control Extension (when <paramref name="transparentIndex"/> is given),
        /// one Image Descriptor (<paramref name="left"/>/<paramref name="top"/>/<paramref name="frameWidth"/>/
        /// <paramref name="frameHeight"/> against a <paramref name="canvasWidth"/>x<paramref name="canvasHeight"/>
        /// logical screen), LZW-encoded image data (<see cref="EncodeUncompressedLzw"/>), and a trailer.
        /// </summary>
        private static byte[] BuildCustomGif(
            int canvasWidth, int canvasHeight, int frameWidth, int frameHeight, int left, int top,
            byte[] palette, byte[] indices, bool interlace, int? transparentIndex)
        {
            int paletteEntries = palette.Length / 3;
            int colorTableSizeBits = Math.Max(1, (int)Math.Ceiling(Math.Log2(paletteEntries)));
            int paddedPaletteEntries = 1 << colorTableSizeBits;
            byte minCodeSize = (byte)Math.Max(2, colorTableSizeBits);

            using var ms = new MemoryStream();
            void WriteUInt16(int value)
            {
                ms.WriteByte((byte)(value & 0xFF));
                ms.WriteByte((byte)((value >> 8) & 0xFF));
            }

            // --- Header ---
            ms.Write(Encoding.ASCII.GetBytes("GIF89a"));

            // --- Logical Screen Descriptor ---
            WriteUInt16(canvasWidth);
            WriteUInt16(canvasHeight);
            byte packed = (byte)(0x80 | ((colorTableSizeBits - 1) << 4) | (colorTableSizeBits - 1)); // GCT present, size
            ms.WriteByte(packed);
            ms.WriteByte(0); // background color index
            ms.WriteByte(0); // pixel aspect ratio

            // --- Global Color Table (padded to a power of two) ---
            for (int i = 0; i < paddedPaletteEntries; i++)
            {
                if (i < paletteEntries)
                {
                    ms.WriteByte(palette[i * 3]);
                    ms.WriteByte(palette[i * 3 + 1]);
                    ms.WriteByte(palette[i * 3 + 2]);
                }
                else
                {
                    ms.Write([0, 0, 0]);
                }
            }

            // --- Graphic Control Extension ---
            if (transparentIndex is int ti)
            {
                ms.WriteByte(0x21); // Extension introducer
                ms.WriteByte(0xF9); // Graphic Control Label
                ms.WriteByte(4);    // block size
                ms.WriteByte(0x01); // packed: transparent color flag set
                WriteUInt16(0);     // delay time
                ms.WriteByte((byte)ti);
                ms.WriteByte(0);   // block terminator
            }

            // --- Image Descriptor ---
            ms.WriteByte(0x2C);
            WriteUInt16(left);
            WriteUInt16(top);
            WriteUInt16(frameWidth);
            WriteUInt16(frameHeight);
            ms.WriteByte((byte)(interlace ? 0x40 : 0x00)); // no local color table; interlace flag

            // --- LZW-compressed image data ---
            ms.WriteByte(minCodeSize);
            var lzwBytes = EncodeUncompressedLzw(indices, minCodeSize);
            int offset = 0;
            while (offset < lzwBytes.Length)
            {
                int chunkSize = Math.Min(255, lzwBytes.Length - offset);
                ms.WriteByte((byte)chunkSize);
                ms.Write(lzwBytes, offset, chunkSize);
                offset += chunkSize;
            }
            ms.WriteByte(0); // block terminator

            // --- Trailer ---
            ms.WriteByte(0x3B);

            return ms.ToArray();
        }

        /// <summary>
        /// Encodes <paramref name="indices"/> as valid GIF LZW data using no compression at all - every
        /// code is a fresh single-symbol literal, never a back-reference into the dictionary. Still
        /// spec-valid and correctly decodable: a real decoder always adds exactly one new (unused) table
        /// entry per code after the first, so the code-width growth this method tracks is identical to
        /// what any real decoder computes, regardless of what the codes actually encode.
        /// </summary>
        private static byte[] EncodeUncompressedLzw(byte[] indices, int minCodeSize)
        {
            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int nextCode = clearCode + 2;

            var bits = new List<bool>();
            void EmitCode(int code)
            {
                for (int b = 0; b < codeSize; b++)
                {
                    bits.Add(((code >> b) & 1) != 0);
                }
            }

            EmitCode(clearCode);

            if (indices.Length > 0)
            {
                int prevCode = indices[0];
                for (int i = 1; i < indices.Length; i++)
                {
                    EmitCode(prevCode);
                    nextCode++;
                    if (nextCode == (1 << codeSize))
                    {
                        if (codeSize < 12)
                        {
                            codeSize++;
                        }
                        else
                        {
                            EmitCode(clearCode);
                            codeSize = minCodeSize + 1;
                            nextCode = clearCode + 2;
                        }
                    }
                    prevCode = indices[i];
                }
                EmitCode(prevCode);
            }

            EmitCode(endCode);

            var bytes = new byte[(bits.Count + 7) / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    bytes[i / 8] |= (byte)(1 << (i % 8));
                }
            }
            return bytes;
        }
    }
}
