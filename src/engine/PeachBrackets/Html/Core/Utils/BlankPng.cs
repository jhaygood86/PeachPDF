using System;
using System.IO;
using System.IO.Compression;

namespace PeachBrackets.Html.Utils
{
    /// <summary>A fully transparent PNG of a given size: what stands for an image the page may not show (see <c>HtmlContainerInt.HideCrossOriginImages</c>), so layout still knows its size.</summary>
    internal static class BlankPng
    {
        public static byte[] Create(int width, int height)
        {
            width = Math.Clamp(width, 1, 8192);
            height = Math.Clamp(height, 1, 8192);

            using var raw = new MemoryStream();
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = new byte[1 + (width * 4)];
                for (var y = 0; y < height; y++)
                    z.Write(row, 0, row.Length);
            }

            using var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8; // bit depth
            header[9] = 6; // RGBA
            Chunk(png, "IHDR", header);
            Chunk(png, "IDAT", raw.ToArray());
            Chunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            stream.Write(length);

            var body = new byte[4 + data.Length];
            for (var i = 0; i < 4; i++)
                body[i] = (byte)type[i];

            data.CopyTo(body, 4);
            stream.Write(body);

            var crc = new byte[4];
            WriteInt(crc, 0, unchecked((int)Crc32(body)));
            stream.Write(crc);
        }

        private static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }

            return ~crc;
        }
    }
}
