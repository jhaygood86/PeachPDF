using System.Buffers.Binary;
using System.Text;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// <see cref="SfntRepair"/> on subsets written by macOS (Quartz): a symbolic TrueType font whose only cmap is a Macintosh (1,0)
    /// subtable and whose name table names nothing but the PostScript name. The font loader reads neither, so they are rewritten.
    /// </summary>
    public class SfntRepairTests
    {
        private static byte[] U16(params int[] values)
        {
            var d = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(i * 2), (ushort)values[i]);
            return d;
        }

        private static byte[] Sfnt(params (string Tag, byte[] Data)[] tables)
        {
            var ordered = tables.OrderBy(t => t.Tag, StringComparer.Ordinal).ToArray();
            int dataStart = 12 + ordered.Length * 16, total = dataStart;
            foreach (var t in ordered) total += (t.Data.Length + 3) & ~3;
            var o = new byte[total];
            BinaryPrimitives.WriteUInt32BigEndian(o, 0x00010000);
            BinaryPrimitives.WriteUInt16BigEndian(o.AsSpan(4), (ushort)ordered.Length);
            int pos = dataStart;
            for (int i = 0; i < ordered.Length; i++)
            {
                int rec = 12 + i * 16;
                Encoding.ASCII.GetBytes(ordered[i].Tag, o.AsSpan(rec, 4));
                BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 8), (uint)pos);
                BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(rec + 12), (uint)ordered[i].Data.Length);
                ordered[i].Data.CopyTo(o, pos);
                pos += (ordered[i].Data.Length + 3) & ~3;
            }
            return o;
        }

        private static byte[] Table(byte[] font, string tag)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (int i = 0; i < count; i++)
            {
                int rec = 12 + i * 16;
                if (Encoding.ASCII.GetString(font, rec, 4) != tag) continue;
                return font.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 12))).ToArray();
            }
            throw new InvalidOperationException(tag);
        }

        private static byte[] Head() { var h = new byte[54]; BinaryPrimitives.WriteUInt16BigEndian(h.AsSpan(18), 1000); return h; }
        private static byte[] Hhea() => new byte[36];

        /// <summary>A cmap of one subtable on the given platform and encoding.</summary>
        private static byte[] Cmap(int platform, int encoding, byte[] subtable)
        {
            var d = new byte[12 + subtable.Length];
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(4), (ushort)platform);
            BinaryPrimitives.WriteUInt16BigEndian(d.AsSpan(6), (ushort)encoding);
            BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(8), 12);
            subtable.CopyTo(d, 12);
            return d;
        }

        /// <summary>Looks a code up in a format 4 subtable of platform 3 encoding 1 (what the repair writes).</summary>
        private static int Lookup(byte[] cmap, int code)
        {
            Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(4)));
            var t = cmap.AsSpan(BinaryPrimitives.ReadInt32BigEndian(cmap.AsSpan(8)));
            int segs = BinaryPrimitives.ReadUInt16BigEndian(t[6..]) / 2;
            for (int i = 0; i < segs; i++)
            {
                int end = BinaryPrimitives.ReadUInt16BigEndian(t[(14 + i * 2)..]);
                int start = BinaryPrimitives.ReadUInt16BigEndian(t[(16 + segs * 2 + i * 2)..]);
                int delta = BinaryPrimitives.ReadUInt16BigEndian(t[(16 + segs * 4 + i * 2)..]);
                if (code >= start && code <= end) return (code + delta) & 0xFFFF;
            }
            return 0;
        }

        private static byte[] QuartzName() => U16(0, 1, 18, 1, 0, 0, 6, 14, 0).Concat(Encoding.ASCII.GetBytes("AAAAAC+ArialMT")).ToArray();

        [Fact]
        public void MacFormat6Cmap_IsCarriedOverAtF000PlusCode()
        {
            // codes 33..35 -> glyphs 5, 0 (unmapped), 7
            var format6 = U16(6, 16, 0, 33, 3, 5, 0, 7);
            var font = Sfnt(("cmap", Cmap(1, 0, format6)), ("head", Head()), ("hhea", Hhea()), ("name", QuartzName()));

            var repaired = SfntRepair.AddMissingTables(font, "family");

            var cmap = Table(repaired, "cmap");
            Assert.Equal(5, Lookup(cmap, 0xF000 + 33));
            Assert.Equal(0, Lookup(cmap, 0xF000 + 34));
            Assert.Equal(7, Lookup(cmap, 0xF000 + 35));
            Assert.Equal(0, Lookup(cmap, 33));
        }

        [Fact]
        public void MacFormat0Cmap_IsCarriedOverAtF000PlusCode()
        {
            var format0 = new byte[262];
            BinaryPrimitives.WriteUInt16BigEndian(format0, 0);
            BinaryPrimitives.WriteUInt16BigEndian(format0.AsSpan(2), 262);
            format0[6 + 65] = 9;
            var font = Sfnt(("cmap", Cmap(1, 0, format0)), ("head", Head()), ("hhea", Hhea()), ("name", QuartzName()));

            var cmap = Table(SfntRepair.AddMissingTables(font, "family"), "cmap");

            Assert.Equal(9, Lookup(cmap, 0xF000 + 65));
        }

        [Fact]
        public void NameTableWithOnlyAMacPostScriptName_GetsAWindowsFamilyAndFullName()
        {
            var font = Sfnt(("cmap", Cmap(1, 0, U16(6, 12, 0, 33, 1, 5))), ("head", Head()), ("hhea", Hhea()), ("name", QuartzName()));

            var name = Table(SfntRepair.AddMissingTables(font, "pdfimport-x"), "name");

            Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(name.AsSpan(6)));    // first record: Windows platform
            Assert.True(name.AsSpan().IndexOf(Encoding.BigEndianUnicode.GetBytes("pdfimport-x")) >= 0);
        }

        [Fact]
        public void UnicodeCmap_IsLeftAlone()
        {
            var cmap = Cmap(3, 1, U16(4, 14, 0, 2, 0, 0, 0));
            var font = Sfnt(("cmap", cmap), ("head", Head()), ("hhea", Hhea()), ("name", QuartzName()));

            var repaired = SfntRepair.AddMissingTables(font, "family");

            Assert.Equal(cmap, Table(repaired, "cmap"));
        }
    }
}
