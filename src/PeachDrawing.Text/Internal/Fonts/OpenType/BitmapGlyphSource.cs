#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Reader for the OpenType bitmap colour glyph tables: Google's CBDT/CBLC (Noto Color Emoji's bitmap build)
// and Apple's sbix (Apple Color Emoji). Both store one PNG (sbix: also JPEG) per glyph per size ("strike").
// Only the picture and where it sits relative to the glyph origin are read here; drawing is up to the
// caller (ColorGlyphPainter for PDF, RasterGraphics for bitmaps).
//
// https://learn.microsoft.com/en-us/typography/opentype/spec/cbdt
// https://learn.microsoft.com/en-us/typography/opentype/spec/cblc
// https://learn.microsoft.com/en-us/typography/opentype/spec/sbix
//
#endregion

using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>One glyph picture of one strike.</summary>
    /// <param name="Data">The encoded image (PNG, or for sbix also JPEG).</param>
    /// <param name="Ppem">The pixels per em of the strike the picture belongs to: the picture is drawn at <c>fontSize / Ppem</c> per pixel.</param>
    /// <param name="Width">Picture width in strike pixels.</param>
    /// <param name="Height">Picture height in strike pixels.</param>
    /// <param name="BearingX">Distance from the glyph origin to the picture's left edge, in strike pixels.</param>
    /// <param name="BearingTop">Distance from the baseline UP to the picture's top edge, in strike pixels.</param>
    internal readonly record struct BitmapGlyph(byte[] Data, int Ppem, int Width, int Height, double BearingX, double BearingTop);

    /// <summary>
    /// The bitmap colour glyphs of a font: <c>CBDT</c>/<c>CBLC</c> or <c>sbix</c>. Absent on almost every font
    /// (<see cref="TryCreate"/> returns null).
    /// </summary>
    internal sealed class BitmapGlyphSource
    {
        private readonly OpenTypeFontface _face;
        private readonly List<CbStrike>? _cbStrikes;
        private readonly List<SbixStrike>? _sbixStrikes;
        private readonly int _numGlyphs;

        private sealed record CbStrike(int Ppem, int StartGlyph, int EndGlyph, int SubTableArrayOffset, int SubTableCount);

        private sealed record SbixStrike(int Ppem, int Offset);

        private readonly int _cblcOffset;
        private readonly int _cbdtOffset;
        private readonly int _cbdtLength;
        private readonly int _sbixOffset;
        private readonly int _sbixLength;

        private BitmapGlyphSource(OpenTypeFontface face, int numGlyphs)
        {
            _face = face;
            _numGlyphs = numGlyphs;

            if (face.TableDictionary.TryGetValue(TableTagNames.CBLC, out var cblc) && face.TableDictionary.TryGetValue(TableTagNames.CBDT, out var cbdt))
            {
                _cblcOffset = cblc.Offset;
                _cbdtOffset = cbdt.Offset;
                _cbdtLength = cbdt.Length;
                _cbStrikes = ReadCblcStrikes();
            }
            else if (face.TableDictionary.TryGetValue(TableTagNames.Sbix, out var sbix))
            {
                _sbixOffset = sbix.Offset;
                _sbixLength = sbix.Length;
                _sbixStrikes = ReadSbixStrikes();
            }
        }

        /// <summary>The bitmap glyph source of <paramref name="face"/>, or null when it has no bitmap colour tables (or unreadable ones).</summary>
        public static BitmapGlyphSource? TryCreate(OpenTypeFontface face, int numGlyphs)
        {
            var hasCb = face.TableDictionary.ContainsKey(TableTagNames.CBLC) && face.TableDictionary.ContainsKey(TableTagNames.CBDT);
            if (!hasCb && !face.TableDictionary.ContainsKey(TableTagNames.Sbix))
                return null;

            try
            {
                var source = new BitmapGlyphSource(face, numGlyphs);
                return (source._cbStrikes is { Count: > 0 }) || (source._sbixStrikes is { Count: > 0 }) ? source : null;
            }
            catch (Exception)
            {
                return null; // a malformed table is the same as no table
            }
        }

        /// <summary>Whether the font has a picture for <paramref name="glyphId"/> in any strike.</summary>
        public bool HasGlyph(int glyphId)
        {
            lock (_face.SyncRoot)
            {
                try
                {
                    if (_cbStrikes is not null)
                    {
                        foreach (var strike in _cbStrikes)
                        {
                            if (TryLocateCb(strike, glyphId, out _))
                                return true;
                        }

                        return false;
                    }

                    if (_sbixStrikes is not null)
                    {
                        foreach (var strike in _sbixStrikes)
                        {
                            if (TryReadSbix(strike, glyphId, 0, out _))
                                return true;
                        }
                    }

                    return false;
                }
                catch (Exception)
                {
                    return false; // a malformed table has no picture for the glyph
                }
            }
        }

        /// <summary>
        /// The picture of <paramref name="glyphId"/> from the strike best suited to a font size of <paramref name="ppem"/> pixels per em: the
        /// smallest strike at least that large (so the picture is scaled down rather than up), else the largest there is.
        /// </summary>
        public bool TryGet(int glyphId, double ppem, out BitmapGlyph glyph)
        {
            glyph = default;
            lock (_face.SyncRoot)
            {
                try
                {
                    if (_cbStrikes is not null)
                    {
                        foreach (var strike in Order(_cbStrikes, s => s.Ppem, ppem))
                        {
                            if (TryReadCb(strike, glyphId, out glyph))
                                return true;
                        }
                    }

                    if (_sbixStrikes is not null)
                    {
                        foreach (var strike in Order(_sbixStrikes, s => s.Ppem, ppem))
                        {
                            if (TryReadSbix(strike, glyphId, 0, out glyph))
                                return true;
                        }
                    }
                }
                catch (Exception)
                {
                    // A truncated table: no picture, and the glyph falls back to its outline.
                }
            }

            return false;
        }

        /// <summary>Strikes in the order to try them: the smallest that is at least <paramref name="ppem"/> first, then larger ones, then the smaller ones from the biggest down.</summary>
        private static IEnumerable<T> Order<T>(List<T> strikes, Func<T, int> ppemOf, double ppem)
        {
            var atLeast = new List<T>();
            var below = new List<T>();
            foreach (var strike in strikes)
                (ppemOf(strike) >= ppem ? atLeast : below).Add(strike);

            atLeast.Sort((a, b) => ppemOf(a).CompareTo(ppemOf(b)));
            below.Sort((a, b) => ppemOf(b).CompareTo(ppemOf(a)));
            foreach (var s in atLeast)
                yield return s;
            foreach (var s in below)
                yield return s;
        }

        // ---- CBLC / CBDT ------------------------------------------------------------------------------

        private List<CbStrike> ReadCblcStrikes()
        {
            var strikes = new List<CbStrike>();
            lock (_face.SyncRoot)
            {
                _face.Position = _cblcOffset;
                _face.ReadUShort(); // majorVersion
                _face.ReadUShort(); // minorVersion
                var numSizes = (int)_face.ReadULong();
                for (var i = 0; i < numSizes && i < 256; i++)
                {
                    _face.Position = _cblcOffset + 8 + i * 48;
                    var arrayOffset = (int)_face.ReadULong();
                    _face.ReadULong(); // indexTablesSize
                    var subTables = (int)_face.ReadULong();
                    _face.ReadULong(); // colorRef
                    _face.Position += 24; // hori and vert SbitLineMetrics (12 bytes each)
                    var start = _face.ReadUShort();
                    var end = _face.ReadUShort();
                    int ppemX = _face.ReadByte();
                    _face.ReadByte(); // ppemY
                    strikes.Add(new CbStrike(ppemX, start, end, arrayOffset, subTables));
                }
            }

            return strikes;
        }

        /// <summary>Finds the index subtable that covers a glyph: its absolute offset, the glyph's position within it.</summary>
        private bool TryLocateCb(CbStrike strike, int glyphId, out CbLocation location)
        {
            location = default;
            if (glyphId < strike.StartGlyph || glyphId > strike.EndGlyph)
                return false;

            var arrayStart = _cblcOffset + strike.SubTableArrayOffset;
            for (var i = 0; i < strike.SubTableCount; i++)
            {
                _face.Position = arrayStart + i * 8;
                int first = _face.ReadUShort();
                int last = _face.ReadUShort();
                var additional = (int)_face.ReadULong();
                if (glyphId < first || glyphId > last)
                    continue;

                var table = arrayStart + additional;
                _face.Position = table;
                int indexFormat = _face.ReadUShort();
                int imageFormat = _face.ReadUShort();
                var imageDataOffset = (int)_face.ReadULong();
                location = new CbLocation(table, indexFormat, imageFormat, imageDataOffset, first, last);
                return IsPresent(location, glyphId);
            }

            return false;
        }

        private readonly record struct CbLocation(int Table, int IndexFormat, int ImageFormat, int ImageDataOffset, int First, int Last);

        /// <summary>Whether the subtable holds data for the glyph (formats 4 and 5 list their glyphs; 1 to 3 are dense over the range, with an empty entry meaning none).</summary>
        private bool IsPresent(CbLocation location, int glyphId) => TryGetCbData(location, glyphId, out _, out _, out _);

        /// <summary>The image data range for a glyph: absolute CBDT offset, byte length, and the metrics shared by the subtable (formats 2 and 5), if any.</summary>
        private bool TryGetCbData(CbLocation location, int glyphId, out int offset, out int length, out (int Height, int Width, int BearingX, int BearingY)? sharedMetrics)
        {
            offset = 0;
            length = 0;
            sharedMetrics = null;
            var afterHeader = location.Table + 8;

            switch (location.IndexFormat)
            {
                case 1:
                {
                    var index = glyphId - location.First;
                    _face.Position = afterHeader + index * 4;
                    var start = (int)_face.ReadULong();
                    var end = (int)_face.ReadULong();
                    offset = location.ImageDataOffset + start;
                    length = end - start;
                    return length > 0;
                }

                case 3:
                {
                    var index = glyphId - location.First;
                    _face.Position = afterHeader + index * 2;
                    int start = _face.ReadUShort();
                    int end = _face.ReadUShort();
                    offset = location.ImageDataOffset + start;
                    length = end - start;
                    return length > 0;
                }

                case 2:
                {
                    _face.Position = afterHeader;
                    var imageSize = (int)_face.ReadULong();
                    sharedMetrics = ReadBigMetrics();
                    offset = location.ImageDataOffset + (glyphId - location.First) * imageSize;
                    length = imageSize;
                    return imageSize > 0;
                }

                case 4:
                {
                    _face.Position = afterHeader;
                    var count = (int)_face.ReadULong();
                    for (var i = 0; i <= count && i < 65536; i++)
                    {
                        int id = _face.ReadUShort();
                        int start = _face.ReadUShort();
                        if (id != glyphId)
                            continue;

                        _face.ReadUShort(); // next glyph's id
                        int end = _face.ReadUShort();
                        offset = location.ImageDataOffset + start;
                        length = end - start;
                        return length > 0;
                    }

                    return false;
                }

                case 5:
                {
                    _face.Position = afterHeader;
                    var imageSize = (int)_face.ReadULong();
                    sharedMetrics = ReadBigMetrics();
                    var count = (int)_face.ReadULong();
                    for (var i = 0; i < count && i < 65536; i++)
                    {
                        if (_face.ReadUShort() != glyphId)
                            continue;

                        offset = location.ImageDataOffset + i * imageSize;
                        length = imageSize;
                        return imageSize > 0;
                    }

                    return false;
                }

                default:
                    return false;
            }
        }

        private (int Height, int Width, int BearingX, int BearingY) ReadBigMetrics()
        {
            int height = _face.ReadByte();
            int width = _face.ReadByte();
            int bearingX = (sbyte)_face.ReadByte();
            int bearingY = (sbyte)_face.ReadByte();
            _face.Position += 4; // horiAdvance, vertBearingX, vertBearingY, vertAdvance
            return (height, width, bearingX, bearingY);
        }

        private bool TryReadCb(CbStrike strike, int glyphId, out BitmapGlyph glyph)
        {
            glyph = default;
            if (!TryLocateCb(strike, glyphId, out var location) || !TryGetCbData(location, glyphId, out var offset, out var length, out var shared))
                return false;

            // Only the PNG image formats: 17 (small metrics), 18 (big metrics), 19 (metrics in the index subtable).
            var position = _cbdtOffset + offset;
            _face.Position = position;
            int height, width, bearingX, bearingY;
            switch (location.ImageFormat)
            {
                case 17:
                    height = _face.ReadByte();
                    width = _face.ReadByte();
                    bearingX = (sbyte)_face.ReadByte();
                    bearingY = (sbyte)_face.ReadByte();
                    _face.ReadByte(); // advance
                    break;

                case 18:
                    (height, width, bearingX, bearingY) = ReadBigMetrics();
                    break;

                case 19 when shared is { } metrics:
                    (height, width, bearingX, bearingY) = metrics;
                    break;

                default:
                    return false;
            }

            var dataLength = (int)_face.ReadULong();
            if (dataLength <= 0 || offset + length > _cbdtLength + 1 || dataLength > length)
                return false;

            glyph = new BitmapGlyph(_face.ReadBytes(dataLength), strike.Ppem, width, height, bearingX, bearingY);
            return true;
        }

        // ---- sbix -------------------------------------------------------------------------------------

        private List<SbixStrike> ReadSbixStrikes()
        {
            var strikes = new List<SbixStrike>();
            lock (_face.SyncRoot)
            {
                _face.Position = _sbixOffset;
                _face.ReadUShort(); // version
                _face.ReadUShort(); // flags
                var numStrikes = (int)_face.ReadULong();
                var offsets = new List<int>(Math.Clamp(numStrikes, 0, 256));
                for (var i = 0; i < numStrikes && i < 256; i++)
                    offsets.Add((int)_face.ReadULong());

                foreach (var strikeOffset in offsets)
                {
                    _face.Position = _sbixOffset + strikeOffset;
                    int ppem = _face.ReadUShort();
                    strikes.Add(new SbixStrike(ppem, strikeOffset));
                }
            }

            return strikes;
        }

        private bool TryReadSbix(SbixStrike strike, int glyphId, int depth, out BitmapGlyph glyph)
        {
            glyph = default;
            if (glyphId < 0 || glyphId >= _numGlyphs || depth > 4)
                return false;

            var strikeStart = _sbixOffset + strike.Offset;
            _face.Position = strikeStart + 4 + glyphId * 4;
            var start = (int)_face.ReadULong();
            var end = (int)_face.ReadULong();
            var length = end - start;
            if (length <= 8 || strike.Offset + end > _sbixLength)
                return false;

            _face.Position = strikeStart + start;
            var originX = _face.ReadShort();
            var originY = _face.ReadShort();
            var type = _face.ReadTag();
            var payload = length - 8;

            switch (type)
            {
                case "dupe":
                    return payload >= 2 && TryReadSbix(strike, _face.ReadUShort(), depth + 1, out glyph);

                case "png ":
                case "jpg ":
                {
                    var data = _face.ReadBytes(payload);
                    if (!ImageSize(data, out var width, out var height))
                        return false;

                    // The origin offsets place the picture's lower-left corner relative to the glyph origin, y up.
                    glyph = new BitmapGlyph(data, strike.Ppem, width, height, originX, originY + height);
                    return true;
                }

                default:
                    return false; // tiff, mask: not drawn
            }
        }

        /// <summary>The pixel size of a PNG or JPEG, read from its header without decoding it.</summary>
        private static bool ImageSize(byte[] data, out int width, out int height)
        {
            width = height = 0;
            if (data.Length >= 24 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
                return width > 0 && height > 0;
            }

            // JPEG: walk the markers to a start-of-frame.
            if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
            {
                var i = 2;
                while (i + 8 < data.Length)
                {
                    if (data[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    var marker = data[i + 1];
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    {
                        height = (data[i + 5] << 8) | data[i + 6];
                        width = (data[i + 7] << 8) | data[i + 8];
                        return width > 0 && height > 0;
                    }

                    if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
                    {
                        i += 2;
                        continue;
                    }

                    i += 2 + ((data[i + 2] << 8) | data[i + 3]);
                }
            }

            return false;
        }
    }
}
