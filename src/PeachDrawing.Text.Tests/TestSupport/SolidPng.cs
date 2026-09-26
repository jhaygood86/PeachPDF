using System.IO.Compression;

namespace PeachDrawing.Text.Tests.TestSupport
{
    /// <summary>Makes the smallest useful PNG: a picture of one colour, for the tests of bitmap colour glyphs.</summary>
    internal static class SolidPng
    {
        /// <summary>An 8-bit RGBA PNG of <paramref name="width"/> by <paramref name="height"/> pixels, every one <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>, opaque.</summary>
        public static byte[] Make(int width, int height, byte r, byte g, byte b)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter: none
                for (int x = 0; x < width; x++)
                {
                    raw.WriteByte(r);
                    raw.WriteByte(g);
                    raw.WriteByte(b);
                    raw.WriteByte(255);
                }
            }

            var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw.ToArray());
            }

            var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var header = new byte[13];
            WriteInt(header, 0, width);
            WriteInt(header, 4, height);
            header[8] = 8; // bit depth
            header[9] = 6; // colour type: RGBA
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", []);
            return png.ToArray();
        }

        private static void WriteInt(byte[] into, int at, int value)
        {
            into[at] = (byte)(value >> 24);
            into[at + 1] = (byte)(value >> 16);
            into[at + 2] = (byte)(value >> 8);
            into[at + 3] = (byte)value;
        }

        private static void WriteChunk(Stream to, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            to.Write(length);

            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
            data.CopyTo(body, 4);
            to.Write(body);

            var crc = new byte[4];
            WriteInt(crc, 0, unchecked((int)Crc32(body)));
            to.Write(crc);
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
            }

            return ~crc;
        }
    }
}
