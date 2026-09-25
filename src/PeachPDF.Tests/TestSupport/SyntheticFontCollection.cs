using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Builds an OpenType font collection (<c>.ttc</c>) from ordinary fonts: a <c>ttcf</c> header, one table
    /// directory per face, and every table stored once - a table two faces share byte for byte is shared in
    /// the file too, and every offset is absolute from the start of the file, as in a real collection.
    /// </summary>
    internal static class SyntheticFontCollection
    {
        internal static byte[] Build(params byte[][] fonts)
        {
            var faceTables = fonts.Select(f =>
            {
                var tables = SyntheticUvsFont.ReadTables(f, out var version);
                return (Version: version, Tables: tables);
            }).ToList();

            // Layout: header (12 + 4n) | n table directories | table data.
            var headerSize = 12 + 4 * fonts.Length;
            var directorySizes = faceTables.Select(f => 12 + 16 * f.Tables.Count).ToList();
            var dataStart = headerSize + directorySizes.Sum();

            var dataOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
            using var data = new MemoryStream();
            string Key(byte[] bytes) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(bytes));

            foreach (var table in faceTables.SelectMany(f => f.Tables.Values))
            {
                var key = Key(table);
                if (dataOffsets.ContainsKey(key))
                    continue;

                dataOffsets[key] = dataStart + (int)data.Length;
                data.Write(table);
                for (var pad = table.Length; (pad & 3) != 0; pad++)
                    data.WriteByte(0);
            }

            using var file = new MemoryStream();
            void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); file.Write(b); }
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); file.Write(b); }

            file.Write(Encoding.ASCII.GetBytes("ttcf"));
            U32(0x00010000);
            U32((uint)fonts.Length);

            var faceOffset = headerSize;
            foreach (var size in directorySizes)
            {
                U32((uint)faceOffset);
                faceOffset += size;
            }

            foreach (var (version, tables) in faceTables)
            {
                var entrySelector = (int)Math.Floor(Math.Log2(tables.Count));
                var searchRange = (1 << entrySelector) * 16;

                U32(version);
                U16(tables.Count);
                U16(searchRange);
                U16(entrySelector);
                U16(tables.Count * 16 - searchRange);

                foreach (var (tag, table) in tables)
                {
                    file.Write(Encoding.ASCII.GetBytes(tag));
                    U32(0);
                    U32((uint)dataOffsets[Key(table)]);
                    U32((uint)table.Length);
                }
            }

            data.Position = 0;
            data.CopyTo(file);
            return file.ToArray();
        }
    }
}
