using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>Fonts made hostile on purpose: the bundled synthetic TrueType font with a table replaced or its bytes scrambled.</summary>
    internal static class HostileFonts
    {
        public static byte[] Original() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "HintingOpcodes.ttf"));

        /// <summary>The table directory of an sfnt: tag, offset, length.</summary>
        public static List<(string Tag, int Offset, int Length)> Tables(byte[] font)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            var tables = new List<(string, int, int)>();
            for (int i = 0; i < count; i++)
            {
                int entry = 12 + 16 * i;
                tables.Add((System.Text.Encoding.ASCII.GetString(font, entry, 4),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8)),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 12))));
            }

            return tables;
        }

        /// <summary>The font with one table's bytes replaced (the tables are laid out again, so the new one may have any length).</summary>
        public static byte[] WithTable(byte[] font, string tag, byte[] data)
        {
            var tables = Tables(font);
            var parts = tables.Select(t => (t.Tag, Data: t.Tag == tag ? data : font.AsSpan(t.Offset, t.Length).ToArray())).ToList();
            if (parts.All(p => p.Tag != tag))
                parts.Add((tag, data));

            return Assemble(font, parts);
        }

        /// <summary>The font without one of its tables.</summary>
        public static byte[] WithoutTable(byte[] font, string tag) =>
            Assemble(font, Tables(font).Where(t => t.Tag != tag).Select(t => (t.Tag, font.AsSpan(t.Offset, t.Length).ToArray())).ToList());

        private static byte[] Assemble(byte[] font, List<(string Tag, byte[] Data)> parts)
        {
            parts.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            var output = new List<byte>();
            output.AddRange(font.AsSpan(0, 4).ToArray());
            var header = new byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)parts.Count);
            output.AddRange(header);
            output.AddRange(new byte[parts.Count * 16]);

            var directory = new List<(string Tag, uint Checksum, int Offset, int Length)>();
            foreach (var (t, bytes) in parts)
            {
                while (output.Count % 4 != 0)
                    output.Add(0);

                directory.Add((t, Checksum(bytes), output.Count, bytes.Length));
                output.AddRange(bytes);
            }

            while (output.Count % 4 != 0)
                output.Add(0);

            var result = output.ToArray();
            for (int i = 0; i < directory.Count; i++)
            {
                int entry = 12 + 16 * i;
                System.Text.Encoding.ASCII.GetBytes(directory[i].Tag, result.AsSpan(entry));
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry + 4), directory[i].Checksum);
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry + 8), (uint)directory[i].Offset);
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(entry + 12), (uint)directory[i].Length);
            }

            return result;
        }

        public static byte[] TableBytes(byte[] font, string tag)
        {
            var (_, offset, length) = Tables(font).Single(t => t.Tag == tag);
            return font.AsSpan(offset, length).ToArray();
        }

        /// <summary>Overwrites some bytes of one table, at random, in place.</summary>
        public static void Scramble(byte[] font, string tag, Random random, int count)
        {
            var (_, offset, length) = Tables(font).Single(t => t.Tag == tag);
            for (int i = 0; i < count; i++)
                font[offset + random.Next(length)] = (byte)random.Next(256);
        }

        private static uint Checksum(byte[] data)
        {
            uint sum = 0;
            for (int i = 0; i < data.Length; i += 4)
            {
                uint word = 0;
                for (int k = 0; k < 4; k++)
                    word = (word << 8) | (i + k < data.Length ? data[i + k] : (byte)0);
                sum = unchecked(sum + word);
            }

            return sum;
        }

        /// <summary>Instruction bytes: PUSHW[0] with a value, and so on, written as a program.</summary>
        public static byte[] Program(params byte[] bytes) => bytes;

        public static byte[] PushWord(short value) => [0xB8, (byte)(value >> 8), (byte)value];
    }
}
