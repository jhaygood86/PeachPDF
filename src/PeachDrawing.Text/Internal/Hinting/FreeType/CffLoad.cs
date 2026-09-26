/****************************************************************************
 *
 * cffload.c
 *
 *   OpenType and CFF data/program tables loader (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

/****************************************************************************
 *
 * cffobjs.c
 *
 *   OpenType objects manager (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): cffload.c, cffobjs.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// One CFF INDEX (<c>CFF_IndexRec</c>): the position and size of its parts in the font's bytes. Everything is read from the bytes of the
/// whole font file, as FreeType reads from the stream, with the bounds checks a stream makes.
/// </summary>
internal sealed class CffIndex
{
    private readonly byte[] _data;

    /// <summary>The number of elements.</summary>
    public int Count { get; private set; }

    private int _offSize;
    private int _start;
    private int _hdrSize;

    /// <summary>The position of the first byte of element data (<c>data_offset</c>).</summary>
    public long DataOffset { get; private set; }

    /// <summary>The size of the element data (<c>data_size</c>).</summary>
    public long DataSize { get; private set; }

    private uint[]? _offsets;

    private CffIndex(byte[] data) => _data = data;

    /// <summary>
    /// Reads an INDEX at <paramref name="pos"/> and moves <paramref name="pos"/> past it (<c>cff_index_init</c>).
    /// </summary>
    /// <exception cref="HintingException">The INDEX is malformed or reaches past the end of the font.</exception>
    public static CffIndex Read(byte[] data, ref int pos)
    {
        var idx = new CffIndex(data) { _start = pos, _hdrSize = 3 };

        int count = ReadUShort(data, ref pos);

        if (count > 0)
        {
            // there is at least one element; read the offset size, then access the offset table to compute the index's total size
            int offSize = ReadByte(data, ref pos);

            if (offSize < 1 || offSize > 4)
                throw new HintingException("A CFF INDEX has an invalid offset size.");

            idx.Count = count;
            idx._offSize = offSize;
            long size = (long)(count + 1) * offSize;

            idx.DataOffset = idx._start + idx._hdrSize + size;

            Skip(data, ref pos, size - offSize);

            size = ReadOffset(data, ref pos, offSize);

            if (size == 0)
                throw new HintingException("A CFF INDEX has an empty data area.");

            idx.DataSize = --size;

            // the data is loaded or skipped: either way it must be in the font
            Skip(data, ref pos, size);
        }

        return idx;
    }

    private uint OffsetAt(int element)
    {
        int at = _start + _hdrSize + element * _offSize;
        uint result = 0;
        for (int n = 0; n < _offSize; n++)
            result = (result << 8) | (at + n < _data.Length && at + n >= 0 ? _data[at + n] : (byte)0);

        return result;
    }

    private void LoadOffsets()
    {
        if (Count > 0 && _offsets is null)
        {
            var offsets = new uint[Count + 1];
            for (int i = 0; i <= Count; i++)
            {
                int at = _start + _hdrSize + i * _offSize;
                if (at + _offSize > _data.Length)
                    throw new HintingException("The offsets of a CFF INDEX reach past the end of the font.");

                offsets[i] = OffsetAt(i);
            }

            _offsets = offsets;
        }
    }

    /// <summary>
    /// The positions of the start of every element and of the end of the last (<c>cff_index_get_pointers</c>), with the sanity checks
    /// FreeType makes of the offsets: the array has <see cref="Count"/> + 1 entries and an empty INDEX gives none.
    /// </summary>
    public int[] GetPointers()
    {
        if (Count == 0)
            return [];

        LoadOffsets();
        var offsets = _offsets!;
        var table = new int[Count + 1];

        uint curOffset = unchecked(offsets[0] - 1);

        // sanity check
        if (curOffset != 0)
            curOffset = 0;

        long baseOffset = DataOffset;
        table[0] = (int)(baseOffset + curOffset);

        for (int n = 1; n <= Count; n++)
        {
            uint nextOffset = unchecked(offsets[n] - 1);

            // two sanity checks for invalid offset tables
            if (nextOffset < curOffset)
                nextOffset = curOffset;
            else if (nextOffset > DataSize)
                nextOffset = (uint)DataSize;

            table[n] = (int)(baseOffset + nextOffset);
            curOffset = nextOffset;
        }

        return table;
    }

    /// <summary>
    /// Finds an element without loading the whole table of offsets (<c>cff_index_access_element</c>): its position and length, or an
    /// empty element (a length of zero).
    /// </summary>
    /// <returns><see langword="false"/> when there is no such element.</returns>
    public bool TryGetElement(int element, out int position, out int length)
    {
        position = 0;
        length = 0;

        if (element < 0 || element >= Count)
            return false;

        // compute start and end offsets
        uint off1, off2 = 0;

        if (_offsets is null)
        {
            int at = _start + _hdrSize + element * _offSize;
            if (at + _offSize > _data.Length)
                throw new HintingException("The offsets of a CFF INDEX reach past the end of the font.");

            off1 = OffsetAt(element);
            if (off1 != 0)
            {
                do
                {
                    element++;
                    off2 = OffsetAt(element);
                }
                while (off2 == 0 && element < Count);
            }
        }
        else
        {
            off1 = _offsets[element];
            if (off1 != 0)
            {
                do
                {
                    element++;
                    off2 = _offsets[element];
                }
                while (off2 == 0 && element < Count);
            }
        }

        // XXX: should check off2 does not exceed the end of this entry; at present, only truncate off2 at the end of this stream
        long streamSize = _data.Length;
        if (off2 > streamSize + 1 || DataOffset > streamSize - off2 + 1)
            off2 = (uint)(streamSize - DataOffset + 1);

        // access element
        if (off1 != 0 && off2 > off1)
        {
            length = (int)(off2 - off1);
            position = (int)(DataOffset + off1 - 1);
            return true;
        }

        // empty index element
        return true;
    }

    private static int ReadByte(byte[] data, ref int pos)
    {
        if ((uint)pos >= (uint)data.Length)
            throw new HintingException("A CFF table ends too soon.");

        return data[pos++];
    }

    private static int ReadUShort(byte[] data, ref int pos)
    {
        if (pos < 0 || (long)pos + 2 > data.Length)
            throw new HintingException("A CFF table ends too soon.");

        int v = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        return v;
    }

    private static uint ReadOffset(byte[] data, ref int pos, int size)
    {
        if (pos < 0 || (long)pos + size > data.Length)
            throw new HintingException("A CFF table ends too soon.");

        uint result = 0;
        for (int n = 0; n < size; n++)
            result = (result << 8) | data[pos++];

        return result;
    }

    private static void Skip(byte[] data, ref int pos, long count)
    {
        if (count < 0 || pos < 0 || pos + count > data.Length)
            throw new HintingException("A CFF table ends too soon.");

        pos += (int)count;
    }
}

/// <summary>One font of a CFF font set: a Top DICT (or, in a CID-keyed font, a Font DICT) and its Private DICT and local subroutines (<c>CFF_SubFontRec</c>).</summary>
internal sealed class CffSubFont
{
    public readonly CffFontDict FontDict = new();
    public readonly CffPrivate Private = new();

    /// <summary>The seed of the <c>random</c> operator; see <see cref="CffFont"/>.</summary>
    public uint Random;

    /// <summary>The position of every local subroutine and of the end of the last, or empty.</summary>
    public int[] LocalSubrs = [];

    public int NumLocalSubrs => LocalSubrs.Length == 0 ? 0 : LocalSubrs.Length - 1;
}

/// <summary>A CFF font as the hinter reads it (<c>CFF_FontRec</c>): read once, immutable after, and shared by every size.</summary>
/// <remarks>
/// Only the parts the Adobe engine needs are loaded: the Top DICT, the Font DICTs and Private DICTs, the CharStrings and subroutines, the
/// FDSelect and the charset. The <c>random</c> operator of the charstring language draws from a generator that FreeType seeds from the
/// address of an object unless it is asked for a seed; here it is always the seed of the Private DICT (<c>initialRandomSeed</c>), which
/// is what FreeType does when its random seed property is set to zero, and every glyph starts from it.
/// </remarks>
internal sealed class CffFont
{
    private const int MaxCidFonts = 256;

    public byte[] Data { get; }
    public CffSubFont TopFont { get; } = new();
    public CffSubFont[] SubFonts { get; private set; } = [];
    public CffIndex CharStrings { get; private set; } = null!;
    public int[] GlobalSubrs { get; private set; } = [];
    public int NumGlobalSubrs => GlobalSubrs.Length == 0 ? 0 : GlobalSubrs.Length - 1;
    public int NumGlyphs { get; private set; }

    /// <summary>Whether the font is CID-keyed (its Top DICT has a <c>ROS</c> operator).</summary>
    public bool IsCidKeyed => TopFont.FontDict.CidRegistry != CffFontDict.NoSid;

    /// <summary>The string id (or, in a CID-keyed font, the CID) of every glyph, from the charset.</summary>
    public ushort[] Sids { get; private set; } = [];

    private readonly Dictionary<int, int[]> _subrsByStart = [];
    private byte[] _fdSelect = [];
    private int _fdSelectFormat;
    private bool _hasFdSelect;
    private int _fdSelectPosition;

    private CffFont(byte[] data) => Data = data;

    /// <summary>
    /// Reads the CFF table at <paramref name="tableOffset"/> (<c>cff_font_load</c> and the font-matrix part of <c>cff_face_init</c>).
    /// </summary>
    /// <param name="data">The bytes of the font file.</param>
    /// <param name="tableOffset">Where the <c>CFF </c> table begins.</param>
    /// <param name="unitsPerEm">The units per em of the font's <c>head</c> table.</param>
    /// <exception cref="HintingException">FreeType would refuse the font.</exception>
    public static CffFont Load(byte[] data, int tableOffset, int unitsPerEm)
    {
        var font = new CffFont(data);
        int baseOffset = tableOffset;
        int pos = baseOffset;

        if (pos < 0 || (long)pos + 4 > data.Length)
            throw new HintingException("The CFF table is truncated.");

        int versionMajor = data[pos];
        int headerSize = data[pos + 2];
        int absoluteOffset = data[pos + 3];

        if (versionMajor != 1 || headerSize < 4 || absoluteOffset > 4)
            throw new HintingException("The CFF table has an unsupported header.");

        // skip the rest of the header
        pos = baseOffset + headerSize;

        // for CFF, read the name, top dict, string and global subrs index
        CffIndex nameIndex = CffIndex.Read(data, ref pos);

        // if we have an empty font name, it must be the only font in the CFF
        if (nameIndex.Count > 1 && nameIndex.DataSize < nameIndex.Count)
            throw new HintingException("The CFF table names more fonts than it has.");

        CffIndex fontDictIndex = CffIndex.Read(data, ref pos);
        _ = CffIndex.Read(data, ref pos); // the strings
        CffIndex globalSubrsIndex = CffIndex.Read(data, ref pos);

        // there must be a Top DICT index entry for each name index entry
        if (nameIndex.Count > fontDictIndex.Count)
            throw new HintingException("The CFF table has too few Top DICT entries.");

        // a font in an SFNT wrapper is only one font
        if (nameIndex.Count > 1)
            throw new HintingException("The CFF table has several fonts in an SFNT wrapper.");

        // now, parse the top-level font dictionary
        font.SubfontLoad(font.TopFont, fontDictIndex, 0, baseOffset);

        CffFontDict dict = font.TopFont.FontDict;

        pos = (int)Math.Min(int.MaxValue, (long)baseOffset + dict.CharstringsOffset);
        font.CharStrings = CffIndex.Read(data, ref pos);

        // now, check for a CID font
        if (dict.CidRegistry != CffFontDict.NoSid)
        {
            // this is a CID-keyed font, we must now allocate a table of sub-fonts, then load each of them separately
            pos = (int)Math.Min(int.MaxValue, (long)baseOffset + dict.FdArrayOffset);
            CffIndex fdIndex = CffIndex.Read(data, ref pos);

            // a font with too many Font DICTs is used without them
            if (fdIndex.Count <= MaxCidFonts)
            {
                var subFonts = new CffSubFont[fdIndex.Count];
                for (int i = 0; i < subFonts.Length; i++)
                    subFonts[i] = new CffSubFont();

                // now load each subfont independently
                for (int i = 0; i < subFonts.Length; i++)
                    font.SubfontLoad(subFonts[i], fdIndex, i, baseOffset);

                font.SubFonts = subFonts;

                // now load the FD Select array
                font.LoadFdSelect(font.CharStrings.Count, (int)Math.Min(int.MaxValue, (long)baseOffset + dict.FdSelectOffset));
            }
        }

        // read the charstrings index now
        if (dict.CharstringsOffset == 0)
            throw new HintingException("The CFF font has no CharStrings.");

        font.NumGlyphs = font.CharStrings.Count;
        font.GlobalSubrs = globalSubrsIndex.GetPointers();

        // read the Charset table if available
        if (font.NumGlyphs > 0)
            font.LoadCharset(font.NumGlyphs, baseOffset, dict.CharsetOffset);

        font.NormalizeMatrices(unitsPerEm);
        return font;
    }

    // cff_subfont_load (code CFF_CODE_TOPDICT, for a Top DICT and for a Font DICT)
    private void SubfontLoad(CffSubFont subfont, CffIndex index, int fontIndex, int baseOffset)
    {
        CffFontDict top = subfont.FontDict;

        if (!index.TryGetElement(fontIndex, out int dictPos, out int dictLen))
            throw new HintingException("The CFF table has no such font dictionary.");

        CffParser.Run(Data, dictPos, dictPos + dictLen, CffParser.Kind.Top, top, null);

        // if it is a CID font, we stop there
        if (top.CidRegistry != CffFontDict.NoSid)
            return;

        // Parse the private dictionary, if any.
        LoadPrivateDict(subfont, baseOffset);

        // The random number generator: the seed of the Private DICT (see the remarks of the class).
        subfont.Random = (uint)subfont.Private.InitialRandomSeed;

        // read the local subrs, if any
        CffPrivate priv = subfont.Private;
        if (priv.LocalSubrsOffset != 0)
        {
            long at = (long)baseOffset + top.PrivateOffset + priv.LocalSubrsOffset;
            if (at > int.MaxValue)
                throw new HintingException("The local subroutines are out of the font.");

            // Font DICTs that point at the same INDEX share its table of pointers (a font with 256 of them, each with 65,535 subroutines,
            // would otherwise hold them 256 times over)
            if (!_subrsByStart.TryGetValue((int)at, out int[]? pointers))
            {
                int pos = (int)at;
                pointers = CffIndex.Read(Data, ref pos).GetPointers();
                _subrsByStart[(int)at] = pointers;
            }

            subfont.LocalSubrs = pointers;
        }
    }

    // cff_load_private_dict
    private void LoadPrivateDict(CffSubFont subfont, int baseOffset)
    {
        CffFontDict top = subfont.FontDict;
        CffPrivate priv = subfont.Private;

        if (top.PrivateOffset == 0 || top.PrivateSize == 0)
            return; // no private DICT, do nothing

        // set defaults
        priv.BlueShift = 7;
        priv.BlueFuzz = 1;
        priv.BlueScale = (int)(0.039625 * 0x10000L * 1000);

        long start = (long)baseOffset + top.PrivateOffset;
        long end = start + top.PrivateSize;
        if (end > Data.Length || start > Data.Length)
            throw new HintingException("The Private DICT reaches past the end of the font.");

        CffParser.Run(Data, (int)start, (int)end, CffParser.Kind.Private, null, priv);

        // ensure that `num_blue_values' is even
        priv.NumBlueValues &= ~1;

        // sanitize `initialRandomSeed' to be a positive value, if necessary
        if (priv.InitialRandomSeed < 0)
            priv.InitialRandomSeed = unchecked(-priv.InitialRandomSeed);
        else if (priv.InitialRandomSeed == 0)
            priv.InitialRandomSeed = 987654321;

        // some sanitizing to avoid overflows later on; the upper limits are ad-hoc values
        if (priv.BlueShift > 1000 || priv.BlueShift < 0)
            priv.BlueShift = 7;

        if (priv.BlueFuzz > 1000 || priv.BlueFuzz < 0)
            priv.BlueFuzz = 1;
    }

    // CFF_Load_FD_Select
    private void LoadFdSelect(int numGlyphs, int offset)
    {
        int pos = offset;
        if (pos < 0 || pos >= Data.Length)
            throw new HintingException("The FDSelect is out of the font.");

        int format = Data[pos++];
        int dataSize;

        switch (format)
        {
            case 0: // format 0, that's simple
                dataSize = numGlyphs;
                break;

            case 3: // format 3, a tad more complex
                if ((long)pos + 2 > Data.Length)
                    throw new HintingException("The FDSelect is truncated.");

                int numRanges = (Data[pos] << 8) | Data[pos + 1];
                pos += 2;

                if (numRanges == 0)
                    throw new HintingException("The FDSelect is empty.");

                dataSize = numRanges * 3 + 2;
                break;

            default:
                throw new HintingException("The FDSelect has an unknown format.");
        }

        if ((long)pos + dataSize > Data.Length)
            throw new HintingException("The FDSelect is truncated.");

        _fdSelectFormat = format;
        _fdSelectPosition = pos;
        _fdSelect = new byte[dataSize];
        Array.Copy(Data, pos, _fdSelect, 0, dataSize);
        _hasFdSelect = true;
    }

    /// <summary>The Font DICT of a glyph (<c>cff_fd_select_get</c>).</summary>
    public int FdSelectGet(int glyphIndex)
    {
        int fd = 0;

        // if there is no FDSelect, return zero
        if (!_hasFdSelect)
            return fd;

        switch (_fdSelectFormat)
        {
            case 0:
                fd = glyphIndex < _fdSelect.Length ? _fdSelect[glyphIndex] : 0;
                break;

            case 3:
            {
                // look up the ranges array; the reads go on past the array in FreeType, into the font, and so they do here
                int p = _fdSelectPosition;
                int pLimit = p + _fdSelect.Length;

                uint first = (uint)((At(p) << 8) | At(p + 1));
                p += 2;
                do
                {
                    if ((uint)glyphIndex < first)
                        break;

                    int fd2 = At(p++);
                    uint limit = (uint)((At(p) << 8) | At(p + 1));
                    p += 2;

                    if ((uint)glyphIndex < limit)
                    {
                        fd = fd2;
                        break;
                    }

                    first = limit;
                }
                while (p < pLimit);

                break;
            }
        }

        return fd;
    }

    private byte At(int position) => (uint)position < (uint)Data.Length ? Data[position] : (byte)0;

    // cff_charset_load (not inverted: the font is in an SFNT wrapper)
    private void LoadCharset(int numGlyphs, int baseOffset, uint offset)
    {
        var sids = new ushort[numGlyphs];

        // If the offset is greater than 2, we have to parse the charset table.
        if (offset > 2)
        {
            long at = (long)baseOffset + offset;
            if (at >= Data.Length)
                throw new HintingException("The charset is out of the font.");

            int pos = (int)at;
            int format = Data[pos++];

            // assign the .notdef glyph
            sids[0] = 0;

            switch (format)
            {
                case 0:
                    if (numGlyphs > 0)
                    {
                        if ((long)pos + (numGlyphs - 1) * 2 > Data.Length)
                            throw new HintingException("The charset is truncated.");

                        for (int j = 1; j < numGlyphs; j++)
                        {
                            sids[j] = (ushort)((Data[pos] << 8) | Data[pos + 1]);
                            pos += 2;
                        }
                    }

                    break;

                case 1:
                case 2:
                {
                    int j = 1;

                    while (j < numGlyphs)
                    {
                        // Read the first glyph sid of the range.
                        if ((long)pos + 2 > Data.Length)
                            throw new HintingException("The charset is truncated.");

                        int glyphSid = (Data[pos] << 8) | Data[pos + 1];
                        pos += 2;

                        // Read the number of glyphs in the range.
                        int nleft;
                        if (format == 2)
                        {
                            if ((long)pos + 2 > Data.Length)
                                throw new HintingException("The charset is truncated.");

                            nleft = (Data[pos] << 8) | Data[pos + 1];
                            pos += 2;
                        }
                        else
                        {
                            if ((long)pos + 1 > Data.Length)
                                throw new HintingException("The charset is truncated.");

                            nleft = Data[pos++];
                        }

                        // try to rescue some of the SIDs if `nleft' is too large
                        if (glyphSid > 0xFFFF - nleft)
                            nleft = 0xFFFF - glyphSid;

                        // Fill in the range of sids -- `nleft + 1' glyphs.
                        for (int i = 0; j < numGlyphs && i <= nleft; i++, j++, glyphSid++)
                            sids[j] = (ushort)glyphSid;
                    }

                    break;
                }

                default:
                    throw new HintingException("The charset has an unknown format.");
            }
        }
        else
        {
            // Parse default tables corresponding to offset == 0, 1, or 2.
            ushort[] predefined = offset switch
            {
                0 => CffTables.IsoAdobeCharset,
                1 => CffTables.ExpertCharset,
                _ => CffTables.ExpertSubsetCharset,
            };

            if (numGlyphs > predefined.Length)
                throw new HintingException("The implicit charset is larger than the predefined charset.");

            Array.Copy(predefined, sids, numGlyphs);
        }

        Sids = sids;
    }

    /// <summary>The glyph of a character of the Adobe standard encoding (<c>cff_lookup_glyph_by_stdcharcode</c>), or -1.</summary>
    public int LookupGlyphByStdCharCode(int charcode)
    {
        // CID-keyed fonts don't have glyph names
        if (Sids.Length == 0)
            return -1;

        // check range of standard char code
        if (charcode < 0 || charcode > 255)
            return -1;

        // Get code to SID mapping from `cff_standard_encoding'.
        ushort glyphSid = CffTables.StandardEncoding[charcode];

        for (int n = 0; n < NumGlyphs; n++)
        {
            if (Sids[n] == glyphSid)
                return n;
        }

        return -1;
    }

    // The font-matrix part of cff_face_init: the matrices are normalized so that yy is 1, with the scaling in the units per em, and a
    // Font DICT's matrix is concatenated with the Top DICT's.
    private void NormalizeMatrices(int headUnitsPerEm)
    {
        CffFontDict dict = TopFont.FontDict;

        if (!dict.HasFontMatrix)
            dict.UnitsPerEm = (uint)headUnitsPerEm; // not a pure CFF: the units per em of the face

        // Normalize the font matrix so that `matrix->yy' is 1; if it is zero, we use `matrix->yx' instead.  The scaling is done with
        // `units_per_em' then (at this point, it already contains the scaling factor, but without normalization of the matrix).
        // Note that the offsets must be expressed in integer font units.
        Normalize(dict);

        for (int i = SubFonts.Length; i > 0; i--)
        {
            CffFontDict sub = SubFonts[i - 1].FontDict;
            CffFontDict top = dict;

            if (sub.HasFontMatrix)
            {
                // if we have a top-level matrix, concatenate the subfont matrix
                if (top.HasFontMatrix)
                {
                    int scaling;
                    if (top.UnitsPerEm > 1 && sub.UnitsPerEm > 1)
                        scaling = (int)Math.Min(top.UnitsPerEm, sub.UnitsPerEm);
                    else
                        scaling = 1;

                    FtCalc.MatrixMultiplyScaled(top.MatrixXx, top.MatrixXy, top.MatrixYx, top.MatrixYy,
                        ref sub.MatrixXx, ref sub.MatrixXy, ref sub.MatrixYx, ref sub.MatrixYy, scaling);
                    FtCalc.VectorTransformScaled(ref sub.OffsetX, ref sub.OffsetY, top.MatrixXx, top.MatrixXy, top.MatrixYx, top.MatrixYy, scaling);

                    sub.UnitsPerEm = (uint)FtCalc.MulDiv((int)sub.UnitsPerEm, (int)top.UnitsPerEm, scaling);
                }
            }
            else
            {
                sub.MatrixXx = top.MatrixXx;
                sub.MatrixXy = top.MatrixXy;
                sub.MatrixYx = top.MatrixYx;
                sub.MatrixYy = top.MatrixYy;
                sub.OffsetX = top.OffsetX;
                sub.OffsetY = top.OffsetY;

                sub.UnitsPerEm = top.UnitsPerEm;
            }

            Normalize(sub);
        }
    }

    private static int WrappingAbs(int value) => value < 0 ? unchecked(-value) : value;

    private static void Normalize(CffFontDict d)
    {
        // FT_ABS wraps for the smallest number, where Math.Abs throws
        int temp = d.MatrixYy != 0 ? WrappingAbs(d.MatrixYy) : WrappingAbs(d.MatrixYx);

        if (temp != 0x10000)
        {
            d.UnitsPerEm = (uint)FtCalc.DivFix((int)d.UnitsPerEm, temp);

            d.MatrixXx = FtCalc.DivFix(d.MatrixXx, temp);
            d.MatrixYx = FtCalc.DivFix(d.MatrixYx, temp);
            d.MatrixXy = FtCalc.DivFix(d.MatrixXy, temp);
            d.MatrixYy = FtCalc.DivFix(d.MatrixYy, temp);
            d.OffsetX = FtCalc.DivFix(d.OffsetX, temp);
            d.OffsetY = FtCalc.DivFix(d.OffsetY, temp);
        }

        d.OffsetX >>= 16;
        d.OffsetY >>= 16;
    }

    /// <summary>The number of the subroutines' bias for a charstring type (<c>cff_compute_bias</c>).</summary>
    public static int ComputeBias(uint charstringType, int numSubrs)
    {
        if (charstringType == 1)
            return 0;

        if (numSubrs < 1240)
            return 107;

        if (numSubrs < 33900)
            return 1131;

        return 32768;
    }
}
