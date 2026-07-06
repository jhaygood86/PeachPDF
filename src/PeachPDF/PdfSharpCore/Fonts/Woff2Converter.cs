using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PeachPDF.PdfSharpCore.Fonts
{
    /// <summary>
    /// Converts WOFF2 font data to OpenType/TrueType format (W3C WOFF2 spec).
    /// </summary>
    internal static class Woff2Converter
    {
        private const uint Woff2Signature = 0x774F4632; // 'wOF2'

        // WOFF2 known-tag list (W3C WOFF2 spec §5, Table 2)
        private static readonly uint[] KnownTags =
        {
            Tag("cmap"), Tag("head"), Tag("hhea"), Tag("hmtx"), Tag("maxp"), Tag("name"),
            Tag("OS/2"), Tag("post"), Tag("cvt "), Tag("fpgm"), Tag("glyf"), Tag("loca"),
            Tag("prep"), Tag("CFF "), Tag("VORG"), Tag("EBDT"), Tag("EBLC"), Tag("gasp"),
            Tag("hdmx"), Tag("kern"), Tag("LTSH"), Tag("PCLT"), Tag("VDMX"), Tag("vhea"),
            Tag("vmtx"), Tag("BASE"), Tag("GDEF"), Tag("GPOS"), Tag("GSUB"), Tag("EBSC"),
            Tag("JSTF"), Tag("MATH"), Tag("CBDT"), Tag("CBLC"), Tag("COLR"), Tag("CPAL"),
            Tag("SVG "), Tag("sbix"), Tag("acnt"), Tag("avar"), Tag("bdat"), Tag("bloc"),
            Tag("bsln"), Tag("cvar"), Tag("fdsc"), Tag("feat"), Tag("fmtx"), Tag("fvar"),
            Tag("gvar"), Tag("hsty"), Tag("just"), Tag("lcar"), Tag("mort"), Tag("morx"),
            Tag("opbd"), Tag("prop"), Tag("trak"), Tag("Zapf"), Tag("Silf"), Tag("Glat"),
            Tag("Gloc"), Tag("Feat"), Tag("Sill"),
        };

        private static readonly uint GlyfTag = Tag("glyf");
        private static readonly uint LocaTag = Tag("loca");

        public static bool IsWoff2(byte[] data) =>
            data.Length >= 4 && ReadUInt32BE(data, 0) == Woff2Signature;

        public static byte[] Convert(byte[] woff2)
        {
            // Header (48 bytes)
            if (woff2.Length < 48)
                throw new InvalidDataException("WOFF2: file too short to contain a valid header.");

            uint flavor = ReadUInt32BE(woff2, 4);
            int numTables = (int)ReadUInt16BE(woff2, 12);
            uint totalCompressedSize = ReadUInt32BE(woff2, 20);

            // Parse variable-length table directory starting at byte 48
            int pos = 48;
            var entries = new List<TableEntry>(numTables);
            for (int i = 0; i < numTables; i++)
            {
                if (pos >= woff2.Length)
                    throw new InvalidDataException($"WOFF2: file truncated while reading table directory entry {i}.");

                var entry = new TableEntry();
                byte flags = woff2[pos++];
                int tagIndex = flags & 0x3F;

                if (tagIndex == 63)
                {
                    if (pos + 4 > woff2.Length)
                        throw new InvalidDataException("WOFF2: file truncated reading arbitrary tag.");
                    entry.Tag = ReadUInt32BE(woff2, pos); pos += 4;
                }
                else
                {
                    entry.Tag = KnownTags[tagIndex];
                }

                // transformVersion: bits 7:6 of the flags byte
                int transformVersion = (flags >> 6) & 0x3;

                bool isGlyfOrLoca = (entry.Tag == GlyfTag || entry.Tag == LocaTag);
                if (isGlyfOrLoca)
                {
                    // For glyf/loca: 0 = transformed, 3 = identity; 1 and 2 are reserved.
                    if (transformVersion == 1 || transformVersion == 2)
                        throw new InvalidDataException(
                            $"WOFF2: reserved transformVersion {transformVersion} for glyf/loca table.");
                    entry.Transformed = (transformVersion == 0);
                }
                else
                {
                    // For all other tables: 0 = identity; 1, 2, 3 are reserved.
                    if (transformVersion != 0)
                        throw new InvalidDataException(
                            $"WOFF2: reserved transformVersion {transformVersion} for non-glyf/loca table.");
                    entry.Transformed = false;
                }

                entry.OrigLength = ReadBase128(woff2, ref pos);

                if (entry.Transformed)
                    entry.TransformLength = ReadBase128(woff2, ref pos);
                else
                    entry.TransformLength = entry.OrigLength;

                entries.Add(entry);
            }

            // Brotli-compressed table data starts right after the table directory
            int compressedStart = pos;
            if ((long)compressedStart + totalCompressedSize > woff2.Length)
                throw new InvalidDataException(
                    "WOFF2: declared compressed block extends beyond end of file.");

            byte[] uncompressed;
            using (var cs = new MemoryStream(woff2, compressedStart, (int)totalCompressedSize))
            using (var brotli = new BrotliStream(cs, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                brotli.CopyTo(ms);
                uncompressed = ms.ToArray();
            }

            // Slice each table from the decompressed block
            int srcPos = 0;
            var rawTables = new byte[numTables][];
            for (int i = 0; i < numTables; i++)
            {
                int len = checked((int)entries[i].TransformLength);
                if (srcPos + len > uncompressed.Length)
                    throw new InvalidDataException(
                        $"WOFF2: table {i} extends beyond end of decompressed data.");
                rawTables[i] = new byte[len];
                Array.Copy(uncompressed, srcPos, rawTables[i], 0, len);
                srcPos += len;
            }

            // Inverse-transform glyf/loca if needed
            int glyfIdx = -1, locaIdx = -1;
            for (int i = 0; i < numTables; i++)
            {
                if (entries[i].Tag == GlyfTag) glyfIdx = i;
                if (entries[i].Tag == LocaTag) locaIdx = i;
            }

            if (glyfIdx >= 0 && entries[glyfIdx].Transformed)
            {
                uint numGlyphs = 0;
                bool longLoca = false;
                for (int i = 0; i < numTables; i++)
                {
                    if (entries[i].Tag == Tag("maxp") && rawTables[i].Length >= 6)
                        numGlyphs = ReadUInt16BE(rawTables[i], 4);
                    if (entries[i].Tag == Tag("head") && rawTables[i].Length >= 52)
                        longLoca = ReadInt16BE(rawTables[i], 50) != 0;
                }

                (byte[] glyfOut, byte[] locaOut) = UntransformGlyf(rawTables[glyfIdx], numGlyphs, longLoca);

                rawTables[glyfIdx] = glyfOut;
                if (locaIdx >= 0) rawTables[locaIdx] = locaOut;
            }

            // Rebuild OpenType
            int esel = 0;
            while ((1 << (esel + 1)) <= numTables) esel++;
            int srange = (1 << esel) * 16;
            int rshift = numTables * 16 - srange;

            int headerSize = 12 + 16 * numTables;
            var tableOffsets = new int[numTables];
            int cur = headerSize;
            for (int i = 0; i < numTables; i++)
            {
                tableOffsets[i] = cur;
                cur += rawTables[i].Length;
                cur += (4 - (cur & 3)) & 3;
            }

            byte[] result = new byte[cur];
            int wp = 0;

            WriteUInt32BE(result, wp, flavor); wp += 4;
            WriteUInt16BE(result, wp, (ushort)numTables); wp += 2;
            WriteUInt16BE(result, wp, (ushort)srange); wp += 2;
            WriteUInt16BE(result, wp, (ushort)esel); wp += 2;
            WriteUInt16BE(result, wp, (ushort)rshift); wp += 2;

            for (int i = 0; i < numTables; i++)
            {
                WriteUInt32BE(result, wp, entries[i].Tag); wp += 4;
                WriteUInt32BE(result, wp, CalcChecksum(rawTables[i])); wp += 4;
                WriteUInt32BE(result, wp, (uint)tableOffsets[i]); wp += 4;
                WriteUInt32BE(result, wp, (uint)rawTables[i].Length); wp += 4;
            }

            for (int i = 0; i < numTables; i++)
                Array.Copy(rawTables[i], 0, result, tableOffsets[i], rawTables[i].Length);

            return result;
        }

        // ---------------------------------------------------------------------------
        // glyf inverse transform (W3C WOFF2 spec §6.2)
        // ---------------------------------------------------------------------------

        // Sign helper matching the WOFF2 reference decoder's `withSign`: bit 0 of the (masked)
        // flag selects the sign of the decoded magnitude.
        private static int WithSign(int flag, int baseValue)
        {
            return (flag & 1) != 0 ? baseValue : -baseValue;
        }

        private static (byte[] glyfOut, byte[] locaOut) UntransformGlyf(
            byte[] src, uint numGlyphs, bool longLoca)
        {
            // Transformed glyf header (36 bytes):
            //  0 reserved         UInt16
            //  2 optionFlags      UInt16
            //  4 numGlyphs        UInt16
            //  6 indexFormat      UInt16  (0=short loca, 1=long loca)
            //  8 nContourStreamSize   UInt32
            // 12 nPointsStreamSize    UInt32
            // 16 flagStreamSize       UInt32
            // 20 glyphStreamSize      UInt32
            // 24 compositeStreamSize  UInt32
            // 28 bboxStreamSize       UInt32
            // 32 instructionStreamSize UInt32

            int p = 0;
            p += 2; // reserved
            p += 2; // optionFlags
            numGlyphs = ReadUInt16BE(src, p); p += 2;
            int indexFormat = ReadUInt16BE(src, p); p += 2;
            int nContourStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            int nPointsStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            int flagStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            int glyphStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            int compositeStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            int bboxStreamSize = (int)ReadUInt32BE(src, p); p += 4;
            p += 4; // instructionStreamSize (computed independently below via bboxBase + bboxStreamSize)

            // Stream base offsets
            int nCBase = p;
            int nPBase = nCBase + nContourStreamSize;
            int fBase = nPBase + nPointsStreamSize;
            int gBase = fBase + flagStreamSize;
            int cBase = gBase + glyphStreamSize;
            int bboxBase = cBase + compositeStreamSize;
            // Per W3C WOFF2 spec section 5.1, instructions for both simple and composite glyphs
            // are stored in their own instructionStream, which follows bboxStream -- they are NOT
            // inline in glyphStream. Only the per-glyph instruction *length* prefix (a 255UInt16)
            // is read from glyphStream/compositeStream; the instruction bytes themselves must be
            // read from this separate stream.
            int iBase = bboxBase + bboxStreamSize;

            // bboxStream starts with a bboxBitmap, padded up to a 4-byte (32-bit) boundary
            // per the reference decoder (bboxBitmapSize = ceil(numGlyphs/32) * 4 bytes) --
            // NOT simply ceil(numGlyphs/8) as a tightly-packed bitmap would be.
            int bitmapBytes = (((int)numGlyphs + 31) >> 5) << 2;
            int bboxValBase = bboxBase + bitmapBytes;

            // Stream cursors
            int nCSrc = nCBase;
            int nPSrc = nPBase;
            int fSrc = fBase;
            int gSrc = gBase;
            int cSrc = cBase;
            int bboxValSrc = bboxValBase;
            int iSrc = iBase;

            var glyfOut = new MemoryStream();
            var locaOffsets = new int[(int)numGlyphs + 1];

            for (int g = 0; g < (int)numGlyphs; g++)
            {
                locaOffsets[g] = (int)glyfOut.Length;
                int nContours = ReadInt16BE(src, nCSrc); nCSrc += 2;

                bool bboxPresent = ((src[bboxBase + (g >> 3)] >> (7 - (g & 7))) & 1) == 1;

                if (nContours == 0)
                {
                    // Empty glyph — write nothing, loca entry will be same as next
                    continue;
                }

                if (nContours > 0)
                {
                    // Simple glyph
                    short xMin = 0, yMin = 0, xMax = 0, yMax = 0;
                    if (bboxPresent)
                    {
                        xMin = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        yMin = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        xMax = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        yMax = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                    }

                    // Count total points from nPoints stream
                    int totalPoints = 0;
                    var endPts = new short[nContours];
                    for (int c = 0; c < nContours; c++)
                    {
                        int nPts = Read255UInt16(src, ref nPSrc);
                        totalPoints += nPts;
                        endPts[c] = (short)(totalPoints - 1);
                    }

                    // Read flags and decode coordinates
                    var ttfFlags = new byte[totalPoints];
                    var xCoords = new short[totalPoints];
                    var yCoords = new short[totalPoints];

                    // Flag stream: exactly one byte per point, no repeat/run-length encoding
                    // (unlike the *output* TTF flags, which do support a repeat scheme -- see
                    // the write-side loop below). The high bit is the on-curve flag (0 = on
                    // curve, per the W3C WOFF2 reference decoder); the low 7 bits select one of
                    // 128 coordinate "triplet" encodings decoded below.
                    var w2flags = new byte[totalPoints];
                    for (int pt0 = 0; pt0 < totalPoints; pt0++)
                        w2flags[pt0] = src[fSrc++];

                    // Decode coordinates from the glyph stream using the WOFF2 triplet encoding
                    // (W3C WOFF2 spec sec. 5.2 / reference decoder's _decodeTriplets).
                    int prevX = 0, prevY = 0;
                    int pt;
                    for (pt = 0; pt < totalPoints; pt++)
                    {
                        int rawFlag = w2flags[pt];
                        bool onCurve = (rawFlag & 0x80) == 0;
                        int flag = rawFlag & 0x7F;

                        int nBytes = flag < 84 ? 1 : flag < 120 ? 2 : flag < 124 ? 3 : 4;
                        int dx, dy;
                        if (flag < 10)
                        {
                            dx = 0;
                            dy = WithSign(flag, ((flag & 14) << 7) + src[gSrc]);
                        }
                        else if (flag < 20)
                        {
                            dx = WithSign(flag, (((flag - 10) & 14) << 7) + src[gSrc]);
                            dy = 0;
                        }
                        else if (flag < 84)
                        {
                            int b0 = flag - 20;
                            int b1 = src[gSrc];
                            dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
                            dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
                        }
                        else if (flag < 120)
                        {
                            int b0 = flag - 84;
                            dx = WithSign(flag, 1 + ((b0 / 12) << 8) + src[gSrc]);
                            dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + src[gSrc + 1]);
                        }
                        else if (flag < 124)
                        {
                            int b2 = src[gSrc + 1];
                            dx = WithSign(flag, (src[gSrc] << 4) + (b2 >> 4));
                            dy = WithSign(flag >> 1, ((b2 & 0x0F) << 8) + src[gSrc + 2]);
                        }
                        else
                        {
                            dx = WithSign(flag, (src[gSrc] << 8) + src[gSrc + 1]);
                            dy = WithSign(flag >> 1, (src[gSrc + 2] << 8) + src[gSrc + 3]);
                        }
                        gSrc += nBytes;

                        prevX += dx;
                        prevY += dy;
                        xCoords[pt] = (short)prevX;
                        yCoords[pt] = (short)prevY;
                        ttfFlags[pt] = onCurve ? (byte)1 : (byte)0;
                    }

                    // Read instruction length from glyph stream
                    int instrLen = Read255UInt16(src, ref gSrc);

                    // Compute bbox from coords if not present
                    if (!bboxPresent)
                    {
                        xMin = xMax = xCoords[0];
                        yMin = yMax = yCoords[0];
                        for (pt = 1; pt < totalPoints; pt++)
                        {
                            if (xCoords[pt] < xMin) xMin = xCoords[pt];
                            if (xCoords[pt] > xMax) xMax = xCoords[pt];
                            if (yCoords[pt] < yMin) yMin = yCoords[pt];
                            if (yCoords[pt] > yMax) yMax = yCoords[pt];
                        }
                    }

                    // Write glyph header
                    WriteInt16(glyfOut, (short)nContours);
                    WriteInt16(glyfOut, xMin);
                    WriteInt16(glyfOut, yMin);
                    WriteInt16(glyfOut, xMax);
                    WriteInt16(glyfOut, yMax);

                    // endPtsOfContours
                    foreach (var ep in endPts) WriteInt16(glyfOut, ep);

                    // instructionLength
                    WriteUInt16(glyfOut, (ushort)instrLen);

                    // instructions live in the separate instructionStream (see NOTE above)
                    for (int b = 0; b < instrLen; b++)
                        glyfOut.WriteByte(src[iSrc++]);

                    // flags (TTF compressed form: run-length encode identical flags)
                    pt = 0;
                    while (pt < totalPoints)
                    {
                        byte flag = ttfFlags[pt];
                        int run = 1;
                        while (run < 255 && pt + run < totalPoints && ttfFlags[pt + run] == flag)
                            run++;
                        if (run > 1)
                        {
                            glyfOut.WriteByte((byte)(flag | 0x08)); // repeat flag
                            glyfOut.WriteByte((byte)(run - 1));
                            pt += run;
                        }
                        else
                        {
                            glyfOut.WriteByte(flag);
                            pt++;
                        }
                    }

                    // x coordinates (delta-encoded)
                    prevX = 0;
                    for (pt = 0; pt < totalPoints; pt++)
                    {
                        int dx = xCoords[pt] - prevX;
                        prevX = xCoords[pt];
                        WriteInt16(glyfOut, (short)dx);
                    }

                    // y coordinates (delta-encoded)
                    prevY = 0;
                    for (pt = 0; pt < totalPoints; pt++)
                    {
                        int dy = yCoords[pt] - prevY;
                        prevY = yCoords[pt];
                        WriteInt16(glyfOut, (short)dy);
                    }

                    // Pad to 4 bytes
                    while ((glyfOut.Length & 3) != 0) glyfOut.WriteByte(0);
                }
                else // nContours == -1: composite glyph
                {
                    // bbox always present for composites
                    if (bboxPresent)
                    {
                        short xMin = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        short yMin = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        short xMax = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;
                        short yMax = ReadInt16BE(src, bboxValSrc); bboxValSrc += 2;

                        WriteInt16(glyfOut, -1); // nContours = -1
                        WriteInt16(glyfOut, xMin);
                        WriteInt16(glyfOut, yMin);
                        WriteInt16(glyfOut, xMax);
                        WriteInt16(glyfOut, yMax);
                    }
                    else
                    {
                        WriteInt16(glyfOut, -1);
                        WriteInt16(glyfOut, 0);
                        WriteInt16(glyfOut, 0);
                        WriteInt16(glyfOut, 0);
                        WriteInt16(glyfOut, 0);
                    }

                    // Copy composite records from composite stream
                    bool moreComponents;
                    bool hasInstructions = false;
                    do
                    {
                        ushort compFlags = ReadUInt16BE(src, cSrc); cSrc += 2;
                        ushort glyphIndex = ReadUInt16BE(src, cSrc); cSrc += 2;
                        WriteUInt16(glyfOut, compFlags);
                        WriteUInt16(glyfOut, glyphIndex);

                        moreComponents = (compFlags & 0x0020) != 0;
                        hasInstructions = hasInstructions || (compFlags & 0x0100) != 0;
                        bool argsAreWords = (compFlags & 0x0001) != 0;
                        int argBytes = argsAreWords ? 4 : 2;
                        for (int b = 0; b < argBytes; b++)
                            glyfOut.WriteByte(src[cSrc++]);

                        if ((compFlags & 0x0008) != 0) // WE_HAVE_A_SCALE
                        {
                            glyfOut.WriteByte(src[cSrc++]); glyfOut.WriteByte(src[cSrc++]);
                        }
                        else if ((compFlags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
                        {
                            for (int b = 0; b < 4; b++) glyfOut.WriteByte(src[cSrc++]);
                        }
                        else if ((compFlags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
                        {
                            for (int b = 0; b < 8; b++) glyfOut.WriteByte(src[cSrc++]);
                        }
                    } while (moreComponents);

                    if (hasInstructions)
                    {
                        int instrLen = Read255UInt16(src, ref gSrc);
                        WriteUInt16(glyfOut, (ushort)instrLen);
                        // instructions live in the separate instructionStream (see NOTE above)
                        for (int b = 0; b < instrLen; b++)
                            glyfOut.WriteByte(src[iSrc++]);
                    }

                    while ((glyfOut.Length & 3) != 0) glyfOut.WriteByte(0);
                }
            }

            locaOffsets[(int)numGlyphs] = (int)glyfOut.Length;

            byte[] glyfBytes = glyfOut.ToArray();

            // Build loca
            bool useLongLoca = (indexFormat != 0) || longLoca;
            byte[] locaBytes;
            if (!useLongLoca)
            {
                locaBytes = new byte[((int)numGlyphs + 1) * 2];
                for (int i = 0; i <= (int)numGlyphs; i++)
                    WriteUInt16BE(locaBytes, i * 2, (ushort)(locaOffsets[i] / 2));
            }
            else
            {
                locaBytes = new byte[((int)numGlyphs + 1) * 4];
                for (int i = 0; i <= (int)numGlyphs; i++)
                    WriteUInt32BE(locaBytes, i * 4, (uint)locaOffsets[i]);
            }

            return (glyfBytes, locaBytes);
        }

        // 255UInt16 variable-length encoding (WOFF2 spec §6.2)
        private static int Read255UInt16(byte[] src, ref int pos)
        {
            const byte wordCode = 253;
            const byte oneMoreByteCode1 = 255;
            const byte oneMoreByteCode2 = 254;
            const int lowestUCode = 253;

            if (pos >= src.Length)
                throw new InvalidDataException("WOFF2: truncated 255UInt16 value.");
            byte b = src[pos++];
            if (b == wordCode)
            {
                if (pos + 1 >= src.Length)
                    throw new InvalidDataException("WOFF2: truncated 255UInt16 value.");
                int v = src[pos++] << 8;
                v |= src[pos++];
                return v;
            }
            if (b == oneMoreByteCode1)
            {
                if (pos >= src.Length)
                    throw new InvalidDataException("WOFF2: truncated 255UInt16 value.");
                return src[pos++] + lowestUCode;
            }
            if (b == oneMoreByteCode2)
            {
                if (pos >= src.Length)
                    throw new InvalidDataException("WOFF2: truncated 255UInt16 value.");
                return src[pos++] + lowestUCode * 2;
            }
            return b;
        }

        private static uint ReadBase128(byte[] data, ref int pos)
        {
            uint result = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pos >= data.Length)
                    throw new InvalidDataException("WOFF2: file truncated in base128 value.");
                byte b = data[pos++];
                if (i == 0 && b == 0x80) throw new InvalidDataException("WOFF2: base128 leading zero");
                result = (result << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return result;
            }
            throw new InvalidDataException("WOFF2: base128 overflow");
        }

        // ---------------------------------------------------------------------------
        // Primitives
        // ---------------------------------------------------------------------------

        private static uint ReadUInt32BE(byte[] d, int o) =>
            ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        private static ushort ReadUInt16BE(byte[] d, int o) =>
            (ushort)(((ushort)d[o] << 8) | d[o + 1]);

        private static short ReadInt16BE(byte[] d, int o) =>
            (short)(((ushort)d[o] << 8) | d[o + 1]);

        private static void WriteUInt32BE(byte[] d, int o, uint v)
        { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

        private static void WriteUInt16BE(byte[] d, int o, ushort v)
        { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }

        private static void WriteUInt16(Stream s, ushort v)
        { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

        private static void WriteInt16(Stream s, short v) => WriteUInt16(s, (ushort)v);

        private static uint CalcChecksum(byte[] table)
        {
            uint sum = 0;
            int len = (table.Length + 3) & ~3;
            for (int i = 0; i < len; i += 4)
            {
                uint w = 0;
                for (int b = 0; b < 4 && i + b < table.Length; b++)
                    w |= (uint)table[i + b] << (24 - b * 8);
                sum += w;
            }
            return sum;
        }

        private static uint Tag(string t) =>
            ((uint)t[0] << 24) | ((uint)t[1] << 16) | ((uint)t[2] << 8) | t[3];

        private sealed class TableEntry
        {
            public uint Tag;
            public bool Transformed;
            public uint OrigLength;
            public uint TransformLength;
        }
    }
}
