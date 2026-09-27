using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// The <c>SVG </c> table: SVG documents that draw glyphs, each covering a range of glyph ids. Documents may be gzip-compressed.
    /// Only the documents are read here; drawing them is up to the caller.
    /// </summary>
    /// <remarks>
    /// https://learn.microsoft.com/en-us/typography/opentype/spec/svg. The table comes from the font file, so it is untrusted:
    /// every offset is checked against the table, and a compressed document is inflated up to <see cref="MaxDocumentBytes"/> and
    /// no further (a small file must not become gigabytes).
    /// </remarks>
    internal sealed class SvgGlyphSource
    {
        /// <summary>The most a document may inflate to, in bytes.</summary>
        internal const int MaxDocumentBytes = 4 * 1024 * 1024;

        private readonly byte[] _bytes;
        private readonly int _listStart;
        private readonly int _tableEnd;
        private readonly (ushort First, ushort Last, uint Offset, uint Length)[] _records;
        private readonly ConcurrentDictionary<(uint Offset, uint Length), string?> _documents = new();
        private long _cachedCharacters;

        /// <summary>The most characters of inflated documents a font keeps; past it the cache is dropped and documents are read again on demand.</summary>
        private const long MaxCachedCharacters = 16L * 1024 * 1024;

        private SvgGlyphSource(byte[] bytes, int listStart, int tableEnd, (ushort, ushort, uint, uint)[] records)
        {
            _bytes = bytes;
            _listStart = listStart;
            _tableEnd = tableEnd;
            _records = records;
        }

        /// <summary>The source of <paramref name="face"/>, or null when it has no usable <c>SVG </c> table.</summary>
        internal static SvgGlyphSource? TryCreate(OpenTypeFontface face)
        {
            if (!face.TableDictionary.TryGetValue(TableTagNames.Svg, out var entry))
            {
                return null;
            }

            try
            {
                var bytes = face.FontSource.Bytes;
                int start = entry.Offset;
                int end = Math.Min(start + entry.Length, bytes.Length);
                var table = bytes.AsSpan(start, end - start);
                if (table.Length < 10 || BigEndian.U16(table, 0) != 0)
                {
                    return null;
                }

                long listOffset = BigEndian.U32(table, 2);
                if (listOffset + 2 > table.Length)
                {
                    return null;
                }

                int count = BigEndian.U16(table, (int)listOffset);
                if (count == 0 || listOffset + 2 + (long)count * 12 > table.Length)
                {
                    return null;
                }

                var records = new (ushort, ushort, uint, uint)[count];
                ushort previousFirst = 0;
                for (int i = 0; i < count; i++)
                {
                    int at = (int)listOffset + 2 + i * 12;
                    ushort first = BigEndian.U16(table, at);
                    ushort last = BigEndian.U16(table, at + 2);
                    uint offset = BigEndian.U32(table, at + 4);
                    uint length = BigEndian.U32(table, at + 8);
                    // Records are sorted by first glyph; one that is not, or that points outside the table, is a damaged table.
                    if (last < first || (i > 0 && first < previousFirst) || listOffset + offset + length > table.Length || length == 0)
                    {
                        return null;
                    }

                    previousFirst = first;
                    records[i] = (first, last, offset, length);
                }

                return new SvgGlyphSource(bytes, start + (int)listOffset, end, records);
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>Finds the record whose range holds <paramref name="glyph"/>, or -1.</summary>
        private int Find(int glyph)
        {
            // The last record that starts at or before the glyph; ranges may overlap a little in a sloppy font, so look back too.
            int low = 0, high = _records.Length - 1, found = -1;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                if (_records[middle].First <= glyph)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            for (int i = found; i >= 0; i--)
            {
                if (_records[i].Last >= glyph)
                {
                    return i;
                }

                if (found - i > 8)
                {
                    break;
                }
            }

            return -1;
        }

        internal bool HasGlyph(int glyph) => Find(glyph) >= 0;

        /// <summary>The document that draws <paramref name="glyph"/>, its glyph range and its text.</summary>
        internal bool TryGet(int glyph, out string document, out int firstGlyph, out int lastGlyph)
        {
            document = string.Empty;
            firstGlyph = lastGlyph = 0;
            int index = Find(glyph);
            if (index < 0)
            {
                return false;
            }

            // Records may share one blob (a hostile table can point thousands at one), so documents are cached by where they are, and
            // what is kept is bounded: a font must not be able to make a process hold gigabytes by asking for many glyphs.
            var (_, _, offset, length) = _records[index];
            if (!_documents.TryGetValue((offset, length), out var text))
            {
                text = Read(index);
                if (System.Threading.Interlocked.Add(ref _cachedCharacters, text?.Length ?? 0) > MaxCachedCharacters)
                {
                    _documents.Clear();
                    System.Threading.Interlocked.Exchange(ref _cachedCharacters, text?.Length ?? 0);
                }

                _documents[(offset, length)] = text;
            }

            if (text is null)
            {
                return false;
            }

            document = text;
            firstGlyph = _records[index].First;
            lastGlyph = _records[index].Last;
            return true;
        }

        private string? Read(int index)
        {
            var (_, _, offset, length) = _records[index];
            int start = _listStart + (int)offset;
            if (start < 0 || start + (long)length > _tableEnd)
            {
                return null;
            }

            var data = _bytes.AsSpan(start, (int)length);
            try
            {
                if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
                {
                    using var input = new MemoryStream(_bytes, start, (int)length, writable: false);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    var buffer = new byte[8192];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaxDocumentBytes)
                        {
                            return null;
                        }

                        output.Write(buffer, 0, read);
                    }

                    return Decode(output.GetBuffer().AsSpan(0, (int)output.Length));
                }

                return length > MaxDocumentBytes ? null : Decode(data);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static string Decode(ReadOnlySpan<byte> utf8)
        {
            // A byte order mark is not part of the document.
            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            {
                utf8 = utf8[3..];
            }

            return Encoding.UTF8.GetString(utf8);
        }
    }
}
