using System.Buffers.Binary;
using System.IO.Compression;
using PeachDrawing.Text;

namespace PeachDrawing.Text.Benchmarks;

/// <summary>Loads the bundled fonts the way a caller of the public API does, and counts the glyphs of one.</summary>
internal static class FontFiles
{
    /// <summary>The bundled font files by the short names the benchmarks give them, which BenchmarkDotNet prints in full where it shortens a long file name.</summary>
    private static readonly Dictionary<string, string> Files = new()
    {
        ["LiberationSans"] = "LiberationSans-Regular.woff",
        ["LiberationSerif"] = "LiberationSerif-Regular.woff",
        ["SourceSans3"] = "SourceSans3-Regular.ttf",
        ["HintingOpcodes"] = "HintingOpcodes.ttf",
        ["HintingCff"] = "HintingCff.otf",
        ["HintingCffCid"] = "HintingCffCid.otf",
        ["SourceCodePro"] = "SourceCodePro-Regular.otf",
    };

    private static byte[] Read(string font) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, Files[font]));

    /// <summary>The typeface a bundled font gives, from a font set of its own so that nothing it caches is shared with an earlier load.</summary>
    public static Typeface Load(string font)
    {
        byte[] data = Read(font);
        var set = new FontSet();
        var family = set.AddData(data, new AddOptions { FamilyName = "Bench-" + Guid.NewGuid().ToString("N") });
        if (!family.TryMatch(new TypefaceQuery(), out var match))
            throw new InvalidOperationException(font + " matched no face.");
        return match.Typeface;
    }

    /// <summary>The number of glyphs the <c>maxp</c> table of a bundled sfnt or WOFF font gives.</summary>
    public static int GlyphCount(string font)
    {
        byte[] data = Read(font);
        uint signature = BinaryPrimitives.ReadUInt32BigEndian(data);

        if (signature == 0x774F4646) // 'wOFF'
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12));
            for (int i = 0; i < tables; i++)
            {
                var entry = data.AsSpan(44 + i * 20);
                if (BinaryPrimitives.ReadUInt32BigEndian(entry) != 0x6D617870) // 'maxp'
                    continue;

                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
                int compressed = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                int original = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
                if (compressed == original)
                    return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 4));

                using var zlib = new ZLibStream(new MemoryStream(data, offset, compressed), CompressionMode.Decompress);
                var maxp = new byte[original];
                zlib.ReadExactly(maxp);
                return BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
            }
        }
        else
        {
            int tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
            for (int i = 0; i < tables; i++)
            {
                var entry = data.AsSpan(12 + i * 16);
                if (BinaryPrimitives.ReadUInt32BigEndian(entry) == 0x6D617870) // 'maxp'
                    return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]) + 4));
            }
        }

        throw new InvalidOperationException(font + " has no maxp table.");
    }
}
