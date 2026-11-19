using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// For one of the CJK character collections (Adobe-Japan1, GB1, CNS1, Korea1), the Unicode character each CID stands for: what turns the CIDs a
/// predefined CMap gives into text to extract, search, or draw with an installed font when the file does not embed one.
/// </summary>
internal sealed class PdfCidUnicode
{
    private readonly int[] _cidStart, _count, _codePoint;

    internal PdfCidUnicode(int[] cidStart, int[] count, int[] codePoint)
    {
        _cidStart = cidStart;
        _count = count;
        _codePoint = codePoint;
    }

    /// <summary>The character of a CID.</summary>
    /// <param name="cid">the CID</param>
    /// <param name="codePoint">the Unicode code point</param>
    /// <returns><see langword="true"/> when the collection has a Unicode character for the CID</returns>
    internal bool TryGet(int cid, out int codePoint)
    {
        int lo = 0, hi = _cidStart.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            if (cid < _cidStart[mid]) hi = mid - 1;
            else if (cid >= _cidStart[mid] + _count[mid]) lo = mid + 1;
            else
            {
                codePoint = _codePoint[mid] + (cid - _cidStart[mid]);
                return true;
            }
        }

        codePoint = 0;
        return false;
    }
}

/// <summary>
/// The predefined CMaps of ISO 32000-1 table 118 and the Unicode of each CJK collection's CIDs, read from one embedded resource compiled from Adobe's
/// CMap resources (BSD-3-Clause, see THIRD-PARTY-LICENSES.md). The resource is deflated and read once, on first use; each CMap is made from its bytes
/// when first asked for. The format is written by the generator test and read here, and the two are kept in step by a round-trip test.
/// </summary>
internal static class PdfPredefinedCMaps
{
    private const string ResourceName = "PeachDrawing.PDF.CMaps.bin";
    private static readonly byte[] Magic = "PCM1"u8.ToArray();

    private static readonly Lazy<Bundle?> Loaded = new(LoadBundle);
    private static readonly ConcurrentDictionary<string, PdfCMap?> Maps = new();
    private static readonly ConcurrentDictionary<string, PdfCidUnicode?> Unicodes = new();

    private sealed record Bundle(byte[] Data, Dictionary<string, (int Offset, int Length)> CMaps, Dictionary<string, (int Offset, int Length)> Unicode);

    /// <summary>The names of the predefined CMaps in the bundle.</summary>
    internal static IReadOnlyCollection<string> Names => Loaded.Value?.CMaps.Keys ?? (IReadOnlyCollection<string>)[];

    /// <summary>A predefined CMap by the name <c>/Encoding</c> gives it, without the slash.</summary>
    /// <param name="name">the name, such as <c>UniJIS-UCS2-H</c></param>
    /// <returns>the CMap, or null when it is not one the bundle holds (or the bundle cannot be read on this host)</returns>
    internal static PdfCMap? Get(string name)
    {
        if (name is "Identity-H" or "Identity-V") return PdfCMap.Identity(name == "Identity-V");
        return Maps.GetOrAdd(name, static n =>
        {
            if (Loaded.Value is not { } bundle || !bundle.CMaps.TryGetValue(n, out var at)) return null;
            return PdfCMapBinary.DecodeMap(n, bundle.Data.AsSpan(at.Offset, at.Length), Get);
        });
    }

    /// <summary>The CID-to-Unicode table of a collection.</summary>
    /// <param name="ordering">the <c>/Ordering</c> of the CIDSystemInfo: Japan1, GB1, CNS1 or Korea1</param>
    /// <returns>the table, or null for another ordering</returns>
    internal static PdfCidUnicode? UnicodeOf(string ordering) => Unicodes.GetOrAdd(ordering, static o =>
    {
        if (Loaded.Value is not { } bundle || !bundle.Unicode.TryGetValue(o, out var at)) return null;
        return PdfCMapBinary.DecodeCidUnicode(bundle.Data.AsSpan(at.Offset, at.Length));
    });

    private static Bundle? LoadBundle()
    {
        try
        {
            using var raw = typeof(PdfPredefinedCMaps).Assembly.GetManifestResourceStream(ResourceName);
            if (raw is null) return null;
            using var inflated = new DeflateStream(raw, CompressionMode.Decompress);
            using var copy = new MemoryStream();
            inflated.CopyTo(copy);
            var data = copy.ToArray();
            return Index(data);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static Bundle? Index(byte[] data)
    {
        var reader = new PdfCMapBinary.Reader(data);
        foreach (var m in Magic) if (reader.Byte() != m) return null;
        var cmaps = new Dictionary<string, (int, int)>();
        for (int n = reader.Varint(); n > 0; n--)
        {
            string name = reader.String();
            int length = reader.Varint();
            cmaps[name] = (reader.Position, length);
            reader.Skip(length);
        }

        var unicode = new Dictionary<string, (int, int)>();
        for (int n = reader.Varint(); n > 0; n--)
        {
            string ordering = reader.String();
            int length = reader.Varint();
            unicode[ordering] = (reader.Position, length);
            reader.Skip(length);
        }

        return new Bundle(data, cmaps, unicode);
    }

    /// <summary>Compiles CMaps and CID-to-Unicode tables into the deflated resource form.</summary>
    internal static byte[] Compile(IReadOnlyList<PdfCMap> cmaps, IReadOnlyDictionary<string, IReadOnlyList<(int Cid, int CodePoint)>> unicode)
    {
        var w = new PdfCMapBinary.Writer();
        foreach (var m in Magic) w.Byte(m);
        w.Varint(cmaps.Count);
        foreach (var map in cmaps)
        {
            w.String(map.Name);
            var blob = PdfCMapBinary.EncodeMap(map);
            w.Varint(blob.Length);
            w.Bytes(blob);
        }

        w.Varint(unicode.Count);
        foreach (var (ordering, pairs) in unicode)
        {
            w.String(ordering);
            var blob = PdfCMapBinary.EncodeCidUnicode(pairs);
            w.Varint(blob.Length);
            w.Bytes(blob);
        }

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) deflate.Write(w.ToArray());
        return output.ToArray();
    }
}

/// <summary>The binary form of a CMap and of a CID-to-Unicode table: variable-length integers, with ranges stored as differences.</summary>
internal static class PdfCMapBinary
{
    internal sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        internal void Byte(byte b) => _stream.WriteByte(b);

        internal void Bytes(byte[] b) => _stream.Write(b);

        internal void Varint(long value)
        {
            ulong v = (ulong)value;
            while (v >= 0x80)
            {
                _stream.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }

            _stream.WriteByte((byte)v);
        }

        internal void Zigzag(long value) => Varint((value << 1) ^ (value >> 63));

        internal void String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            Varint(bytes.Length);
            Bytes(bytes);
        }

        internal byte[] ToArray() => _stream.ToArray();
    }

    internal sealed class Reader(byte[] data)
    {
        private int _at;

        internal int Position => _at;

        internal void Skip(int count) => _at += count;

        internal byte Byte() => _at < data.Length ? data[_at++] : throw new InvalidDataException("The CMap data ends early.");

        internal int Varint() => checked((int)Varint64());

        /// <summary>A variable-length integer of up to 35 bits: a count or a length fits an int, but a zigzag-coded difference of 32-bit codes does not.</summary>
        internal long Varint64()
        {
            long v = 0;
            int shift = 0;
            while (true)
            {
                byte b = Byte();
                v |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("A number in the CMap data is too long.");
            }

            return v;
        }

        internal long Zigzag()
        {
            long v = Varint64();
            return (v >> 1) ^ -(v & 1);
        }

        internal string String()
        {
            int length = Varint();
            if (length < 0 || _at + length > data.Length) throw new InvalidDataException("The CMap data ends early.");
            var s = Encoding.UTF8.GetString(data, _at, length);
            _at += length;
            return s;
        }
    }

    internal static byte[] EncodeMap(PdfCMap map)
    {
        var w = new Writer();
        w.Byte(map.Vertical ? (byte)1 : (byte)0);
        w.String(map.Parent?.Name ?? "");
        w.Varint(map.Spaces.Count);
        foreach (var s in map.Spaces)
        {
            w.Byte((byte)s.Length);
            w.Varint(s.Low);
            w.Varint(s.High);
        }

        for (int length = 1; length <= 4; length++)
        {
            var ranges = new List<PdfCMap.CidRange>(map.RangesOfLength(length));
            ranges.Sort(static (a, b) => a.Low.CompareTo(b.Low));
            w.Varint(ranges.Count);
            long nextLow = 0, expectedCid = 0;
            foreach (var r in ranges)
            {
                w.Zigzag((long)r.Low - nextLow);
                w.Varint(r.High - r.Low);
                w.Zigzag(r.Cid - expectedCid);
                nextLow = (long)r.High + 1;
                expectedCid = r.Cid + (r.High - r.Low) + 1;
            }
        }

        return w.ToArray();
    }

    internal static PdfCMap DecodeMap(string name, ReadOnlySpan<byte> blob, Func<string, PdfCMap?> resolveParent)
    {
        var reader = new Reader(blob.ToArray());
        var map = new PdfCMap(name) { Vertical = reader.Byte() == 1 };
        string parent = reader.String();
        if (parent.Length > 0) map.Parent = resolveParent(parent);
        for (int n = reader.Varint(); n > 0; n--)
        {
            int length = reader.Byte();
            map.AddSpace(length, (uint)reader.Varint64(), (uint)reader.Varint64());
        }

        for (int length = 1; length <= 4; length++)
        {
            long nextLow = 0, expectedCid = 0;
            for (int n = reader.Varint(); n > 0; n--)
            {
                long low = nextLow + reader.Zigzag();
                long span = reader.Varint64();
                long cid = expectedCid + reader.Zigzag();
                map.AddRange(length, (uint)low, (uint)(low + span), (int)cid);
                nextLow = low + span + 1;
                expectedCid = cid + span + 1;
            }
        }

        return map;
    }

    internal static byte[] EncodeCidUnicode(IReadOnlyList<(int Cid, int CodePoint)> pairs)
    {
        var sorted = new List<(int Cid, int CodePoint)>(pairs);
        sorted.Sort(static (a, b) => a.Cid.CompareTo(b.Cid));
        // Runs of consecutive CIDs that map to consecutive characters.
        var runs = new List<(int Cid, int Count, int CodePoint)>();
        foreach (var (cid, cp) in sorted)
        {
            if (runs.Count > 0 && runs[^1] is var last && cid == last.Cid + last.Count && cp == last.CodePoint + last.Count)
                runs[^1] = last with { Count = last.Count + 1 };
            else if (runs.Count > 0 && runs[^1].Cid + runs[^1].Count > cid) continue;     // the same CID twice: the first character stands
            else runs.Add((cid, 1, cp));
        }

        var w = new Writer();
        w.Varint(runs.Count);
        long nextCid = 0, nextCp = 0;
        foreach (var (cid, count, cp) in runs)
        {
            w.Zigzag(cid - nextCid);
            w.Varint(count);
            w.Zigzag(cp - nextCp);
            nextCid = cid + count;
            nextCp = cp + count;
        }

        return w.ToArray();
    }

    internal static PdfCidUnicode DecodeCidUnicode(ReadOnlySpan<byte> blob)
    {
        var reader = new Reader(blob.ToArray());
        int n = reader.Varint();
        var cids = new int[n];
        var counts = new int[n];
        var cps = new int[n];
        long nextCid = 0, nextCp = 0;
        for (int i = 0; i < n; i++)
        {
            long cid = nextCid + reader.Zigzag();
            int count = reader.Varint();
            long cp = nextCp + reader.Zigzag();
            (cids[i], counts[i], cps[i]) = ((int)cid, count, (int)cp);
            nextCid = cid + count;
            nextCp = cp + count;
        }

        return new PdfCidUnicode(cids, counts, cps);
    }
}
