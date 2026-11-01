using PeachImage;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using System;
using System.Collections.Generic;
using System.IO;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Builds asymmetric test pictures (a lopsided "F") carrying an Exif Orientation tag, so a test can tell every one of
    /// the eight orientations apart. PeachImage decodes a JPEG/PNG to its stored raster without applying the tag, so the
    /// bytes here are what a camera would write: the raster as stored plus the tag naming how to turn it upright.
    /// </summary>
    internal static class OrientedImageFixture
    {
        /// <summary>The Exif block of an Orientation-only IFD0: TIFF header, one SHORT entry, no next IFD.</summary>
        public static byte[] TiffBlock(int orientation, bool littleEndian = true)
        {
            var b = new List<byte>();
            void U16(int v) { if (littleEndian) { b.Add((byte)v); b.Add((byte)(v >> 8)); } else { b.Add((byte)(v >> 8)); b.Add((byte)v); } }
            void U32(int v) { if (littleEndian) { U16(v & 0xFFFF); U16(v >> 16); } else { U16(v >> 16); U16(v & 0xFFFF); } }

            b.Add((byte)(littleEndian ? 'I' : 'M')); b.Add((byte)(littleEndian ? 'I' : 'M'));
            U16(0x2A);
            U32(8);
            U16(1);          // one entry
            U16(0x0112);     // Orientation
            U16(3);          // SHORT
            U32(1);          // count
            U16(orientation); U16(0); // value (left-justified in the 4-byte field)
            U32(0);          // no next IFD
            return [.. b];
        }

        /// <summary>A JPEG made from <paramref name="image"/> with an APP1 Exif segment (Orientation <paramref name="orientation"/>) after SOI.</summary>
        public static byte[] Jpeg(int orientation, int width = 24, int height = 16)
        {
            using var image = FShape(width, height);
            using var ms = new MemoryStream();
            image.Save(ms, "jpeg", new JpegEncoderOptions { Quality = 95 });
            return orientation == 0 ? ms.ToArray() : InsertAfterSoi(ms.ToArray(), TiffBlock(orientation));
        }

        /// <summary>A PNG with an <c>eXIf</c> chunk (Orientation <paramref name="orientation"/>) before its first IDAT; no chunk for 0.</summary>
        public static byte[] Png(int orientation, int width = 24, int height = 16) =>
            PngWith(orientation == 0 ? null : TiffBlock(orientation), width, height);

        /// <summary>A PNG carrying <paramref name="tiffBlock"/> as its <c>eXIf</c> chunk (none when null).</summary>
        public static byte[] PngWith(byte[]? tiffBlock, int width = 24, int height = 16)
        {
            using var image = FShape(width, height);
            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions());
            var png = ms.ToArray();
            if (tiffBlock is null) return png;

            var at = IndexOfChunk(png, "IDAT");
            var chunk = PngChunk("eXIf", tiffBlock);
            var result = new byte[png.Length + chunk.Length];
            Array.Copy(png, 0, result, 0, at);
            Array.Copy(chunk, 0, result, at, chunk.Length);
            Array.Copy(png, at, result, at + chunk.Length, png.Length - at);
            return result;
        }

        /// <summary>A hand-built RIFF/WebP container holding only an EXIF chunk (enough for the orientation reader).</summary>
        public static byte[] WebpContainer(int orientation, bool withExifPrefix)
        {
            var tiff = TiffBlock(orientation);
            var payload = new List<byte>();
            if (withExifPrefix) payload.AddRange("Exif\0\0"u8.ToArray());
            payload.AddRange(tiff);
            if (payload.Count % 2 == 1) payload.Add(0);

            var body = new List<byte>();
            body.AddRange("WEBP"u8.ToArray());
            body.AddRange("EXIF"u8.ToArray());
            body.AddRange(BitConverter.GetBytes(payload.Count - (payload.Count % 2 == 1 ? 1 : 0)));
            body.AddRange(payload);

            var file = new List<byte>();
            file.AddRange("RIFF"u8.ToArray());
            file.AddRange(BitConverter.GetBytes(body.Count));
            file.AddRange(body);
            return [.. file];
        }

        /// <summary>A lopsided "F" on white: a tall stem on the left, a long top bar and a short middle bar, so no flip or turn looks like another.</summary>
        public static Image FShape(int width, int height)
        {
            var image = Image.Create(width, height, PixelFormat.Rgba32);
            var px = new byte[width * height * 4];
            for (var i = 0; i < px.Length; i += 4) { px[i] = 255; px[i + 1] = 255; px[i + 2] = 255; px[i + 3] = 255; }

            void Box(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
            {
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        var o = (y * width + x) * 4;
                        px[o] = r; px[o + 1] = g; px[o + 2] = b;
                    }
            }

            var u = Math.Max(1, height / 8);
            Box(u, u, u * 3, height - u, 0, 0, 0);                  // stem
            Box(u, u, width - u * 2, u * 3, 0, 0, 0);               // top bar (long)
            Box(u, u * 4, width / 2, u * 6, 0, 0, 0);               // middle bar (short)
            Box(u, u, u * 3, u * 3, 220, 0, 0);                     // red marker: top-left of the upright F
            px.CopyTo(image.GetPixelSpan());
            return image;
        }

        private static byte[] InsertAfterSoi(byte[] jpeg, byte[] tiff)
        {
            var segment = new List<byte> { 0xFF, 0xE1 };
            var length = 2 + 6 + tiff.Length;
            segment.Add((byte)(length >> 8)); segment.Add((byte)length);
            segment.AddRange("Exif\0\0"u8.ToArray());
            segment.AddRange(tiff);

            var result = new byte[jpeg.Length + segment.Count];
            result[0] = jpeg[0]; result[1] = jpeg[1];
            segment.CopyTo(result, 2);
            Array.Copy(jpeg, 2, result, 2 + segment.Count, jpeg.Length - 2);
            return result;
        }

        private static int IndexOfChunk(byte[] png, string type)
        {
            var i = 8;
            while (i + 8 <= png.Length)
            {
                var len = (png[i] << 24) | (png[i + 1] << 16) | (png[i + 2] << 8) | png[i + 3];
                if (string.CompareOrdinal(System.Text.Encoding.ASCII.GetString(png, i + 4, 4), type) == 0) return i;
                i += 12 + len;
            }

            throw new InvalidOperationException("no " + type + " chunk");
        }

        private static byte[] PngChunk(string type, byte[] data)
        {
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var chunk = new byte[12 + data.Length];
            chunk[0] = (byte)(data.Length >> 24); chunk[1] = (byte)(data.Length >> 16); chunk[2] = (byte)(data.Length >> 8); chunk[3] = (byte)data.Length;
            Array.Copy(typeBytes, 0, chunk, 4, 4);
            Array.Copy(data, 0, chunk, 8, data.Length);

            uint crc = 0xFFFFFFFF;
            for (var i = 4; i < 8 + data.Length; i++)
            {
                crc ^= chunk[i];
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            crc ^= 0xFFFFFFFF;
            var o = 8 + data.Length;
            chunk[o] = (byte)(crc >> 24); chunk[o + 1] = (byte)(crc >> 16); chunk[o + 2] = (byte)(crc >> 8); chunk[o + 3] = (byte)crc;
            return chunk;
        }
    }
}
