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
    public static CffIndex Read(byte[] data, ref int pos) => Read(data, ref pos, cff2: false);

    /// <summary>
    /// Reads an INDEX of a CFF font, or of a CFF2 font, whose count is a 32-bit number (<c>cff_index_init</c>).
    /// </summary>
    /// <exception cref="HintingException">The INDEX is malformed or reaches past the end of the font.</exception>
    public static CffIndex Read(byte[] data, ref int pos, bool cff2)
    {
        var idx = new CffIndex(data) { _start = pos, _hdrSize = cff2 ? 5 : 3 };

        uint count = cff2 ? ReadULong(data, ref pos) : (uint)ReadUShort(data, ref pos);

        if (count > 0)
        {
            // there is at least one element; read the offset size, then access the offset table to compute the index's total size
            int offSize = ReadByte(data, ref pos);

            if (offSize < 1 || offSize > 4)
                throw new HintingException("A CFF INDEX has an invalid offset size.");

            // FreeType computes the size of the table of offsets in the 32-bit unsigned numbers of its Windows build, where the size wraps
            // for a count of 2^30 or more (with offsets of four bytes; 2^31 with two): it then reads a table of a few bytes for billions of
            // elements. Here the size does not wrap, and a table of offsets that is larger than the font fails when it is skipped below;
            // this only keeps the count in an int.
            if (count >= int.MaxValue)
                throw new HintingException("A CFF INDEX has too many elements.");

            idx.Count = (int)count;
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
        long at = (long)_start + _hdrSize + (long)element * _offSize;
        uint result = 0;
        for (int n = 0; n < _offSize; n++)
            result = (result << 8) | (at + n < _data.Length && at + n >= 0 ? _data[at + n] : (byte)0);

        return result;
    }

    private void LoadOffsets()
    {
        if (Count > 0 && _offsets is null)
        {
            // the table of offsets lies in the font (Read checked it), so its size bounds the allocation
            var offsets = new uint[(long)Count + 1];
            for (int i = 0; i <= Count; i++)
            {
                long at = (long)_start + _hdrSize + (long)i * _offSize;
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
            long at = (long)_start + _hdrSize + (long)element * _offSize;
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

    private static uint ReadULong(byte[] data, ref int pos)
    {
        if (pos < 0 || (long)pos + 4 > data.Length)
            throw new HintingException("A CFF table ends too soon.");

        uint v = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
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

/// <summary>The extent of one axis of a region of a CFF2 variation store, in 16.16 (<c>CFF_AxisCoords</c>).</summary>
internal readonly record struct CffAxisCoords(int StartCoord, int PeakCoord, int EndCoord);

/// <summary>What a data set of a CFF2 variation store names: the regions its deltas belong to (<c>CFF_VarData</c>).</summary>
internal sealed class CffVarData
{
    /// <summary>The regions of the data set, in the order its deltas come in a <c>blend</c> (<c>regionIndices</c>).</summary>
    public required int[] RegionIndices { get; init; }
}

/// <summary>The variation store of a CFF2 font (<c>CFF_VStoreRec</c>): the regions and, for each data set, the regions it blends.</summary>
internal sealed class CffVStore
{
    /// <summary>A font with no variation store.</summary>
    public static readonly CffVStore Empty = new();

    /// <summary>The number of axes of every region (<c>axisCount</c>).</summary>
    public int AxisCount { get; private init; }

    /// <summary>The regions: the extent of each along every axis (<c>varRegionList</c>).</summary>
    public CffAxisCoords[][] Regions { get; private init; } = [];

    /// <summary>The data sets (<c>varData</c>), which a <c>vsindex</c> chooses between.</summary>
    public CffVarData[] Data { get; private init; } = [];

    /// <summary>Reads the store the Top DICT points to (<c>cff_vstore_load</c>); a store at offset zero is no store.</summary>
    /// <exception cref="HintingException">The store is malformed or reaches past the end of the font.</exception>
    public static CffVStore Load(byte[] data, int baseOffset, uint offset)
    {
        // no offset means no vstore to parse
        if (offset == 0)
            return Empty;

        // we need to parse the table to determine its size; skip table length (the sum is FreeType's 32-bit unsigned one)
        uint at = unchecked((uint)baseOffset + offset);
        if ((long)at + 2 > data.Length)
            throw new HintingException("The variation store is out of the font.");

        // actual variation store begins after the length
        uint vsOffset = at + 2;
        int pos = (int)vsOffset;

        // check the header
        int format = ReadUShort(data, ref pos);
        if (format != 1)
            throw new HintingException("The variation store has an unknown format.");

        // read top level fields
        uint regionListOffset = ReadULong(data, ref pos);
        int dataCount = ReadUShort(data, ref pos);

        // make temporary copy of item variation data offsets; we'll parse region list first, then come back
        var dataOffsets = new uint[dataCount];
        for (int i = 0; i < dataCount; i++)
            dataOffsets[i] = ReadULong(data, ref pos);

        // parse regionList and axisLists
        pos = SeekTo(data, unchecked(vsOffset + regionListOffset));
        int axisCount = ReadUShort(data, ref pos);
        int regionCount = ReadUShort(data, ref pos);

        var regions = new CffAxisCoords[regionCount][];
        for (int i = 0; i < regionCount; i++)
        {
            var axisList = new CffAxisCoords[axisCount];

            for (int j = 0; j < axisCount; j++)
            {
                int start = (short)ReadUShort(data, ref pos);
                int peak = (short)ReadUShort(data, ref pos);
                int end = (short)ReadUShort(data, ref pos);

                // immediately tag invalid ranges with special peak = 0
                if ((start < 0 && end > 0) || start > peak || peak > end)
                    peak = 0;

                axisList[j] = new CffAxisCoords(Fdot14ToFixed(start), Fdot14ToFixed(peak), Fdot14ToFixed(end));
            }

            regions[i] = axisList;
        }

        // use dataOffsetArray now to parse varData items; entries that name the same bytes share one data set
        var sets = new CffVarData[dataCount];
        var known = new Dictionary<uint, CffVarData>();

        // The most region indexes all the data sets may name together. FreeType reads every data set in full, however many share bytes; a store
        // of a real font names each region index in bytes of its own, so this is the number of bytes of the font.
        long budget = data.Length;

        for (int i = 0; i < dataCount; i++)
        {
            if (known.TryGetValue(dataOffsets[i], out CffVarData? seen))
            {
                sets[i] = seen;
                continue;
            }

            pos = SeekTo(data, unchecked(vsOffset + dataOffsets[i]));

            // ignore `itemCount' and `shortDeltaCount' because CFF2 has no delta sets
            if ((long)pos + 4 > data.Length)
                throw new HintingException("The variation store is truncated.");

            pos += 4;

            // Note: just record values; consistency is checked later by cff_blend_build_vector when it consumes `vstore'
            int regionIdxCount = ReadUShort(data, ref pos);

            budget -= regionIdxCount;
            if (budget < 0)
                throw new HintingException("The data sets of the variation store name more regions than the font has bytes.");

            var indices = new int[regionIdxCount];
            for (int j = 0; j < regionIdxCount; j++)
                indices[j] = ReadUShort(data, ref pos);

            sets[i] = new CffVarData { RegionIndices = indices };
            known[dataOffsets[i]] = sets[i];
        }

        return new CffVStore { AxisCount = axisCount, Regions = regions, Data = sets };
    }

    // convert 2.14 to Fixed
    private static int Fdot14ToFixed(int x) => unchecked(x << 2);

    private static int SeekTo(byte[] data, uint position)
    {
        if (position > data.Length)
            throw new HintingException("The variation store is out of the font.");

        return (int)position;
    }

    private static int ReadUShort(byte[] data, ref int pos)
    {
        if ((long)pos + 2 > data.Length)
            throw new HintingException("The variation store is truncated.");

        int v = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        return v;
    }

    private static uint ReadULong(byte[] data, ref int pos)
    {
        if ((long)pos + 4 > data.Length)
            throw new HintingException("The variation store is truncated.");

        uint v = ((uint)data[pos] << 24) | ((uint)data[pos + 1] << 16) | ((uint)data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        return v;
    }
}

/// <summary>
/// The blend vector of a subfont or of a charstring run, and what it was made from (<c>CFF_BlendRec</c>): for a data set of the variation
/// store and a location, the factor each region's deltas are scaled by, the first being 1 for the default design.
/// </summary>
internal sealed class CffBlend
{
    /// <summary>The variation store the vector is made from; <see cref="CffVStore.Empty"/> until the font is known to have one.</summary>
    public CffVStore VStore = CffVStore.Empty;

    /// <summary>Whether a variation store is attached (<c>blend.font</c> is set): a <c>blend</c> operator is an error without.</summary>
    public bool HasFont;

    /// <summary>Whether a <c>blend</c> was used (a <c>vsindex</c> is not allowed after one).</summary>
    public bool UsedBV;

    /// <summary>Whether <see cref="BV"/> was built, and from what: the data set (<c>lastVsindex</c>), the number of coordinates and the normalized vector (<c>lastNDV</c>).</summary>
    public bool BuiltBV;

    /// <summary>The data set the blend vector was built for.</summary>
    public uint LastVsindex;

    /// <summary>The number of coordinates of the normalized vector it was built for.</summary>
    public int LenNdv;

    /// <summary>The normalized vector it was built for, when there was one.</summary>
    public int[]? LastNdv;

    /// <summary>The number of factors, the default one included (<c>lenBV</c>).</summary>
    public int LenBV;

    /// <summary>The factors in 16.16 (<c>BV</c>); the first is always one.</summary>
    public int[] BV = [];

    /// <summary>Whether the blend vector has to be built again for these parameters (<c>cff_blend_check_vector</c>).</summary>
    public bool CheckVector(uint vsindex, int lenNdv, int[]? ndv)
    {
        return !BuiltBV || LastVsindex != vsindex || LenNdv != lenNdv || (lenNdv != 0 && !ndv.AsSpan(0, lenNdv).SequenceEqual(LastNdv.AsSpan(0, lenNdv)));
    }

    /// <summary>
    /// Computes a blend vector from a variation store index and a normalized vector (<c>cff_blend_build_vector</c>); a length of zero
    /// produces the default blend vector, (1, 0, 0, ...). Every number is 16.16 and the products are FreeType's <c>FT_MulDiv</c>.
    /// </summary>
    /// <returns><see langword="true"/> when FreeType reports an error.</returns>
    public bool BuildVector(uint vsindex, int lenNdv, int[]? ndv)
    {
        // protect against malformed fonts
        if (!(lenNdv == 0 || ndv is not null))
            return true;

        BuiltBV = false;

        CffVStore vs = VStore;

        // VStore and fvar must be consistent
        if (lenNdv != 0 && lenNdv != vs.AxisCount)
            return true;

        if (vsindex >= (uint)vs.Data.Length)
            return true;

        // select the item variation data structure
        CffVarData varData = vs.Data[vsindex];

        // prepare buffer for the blend vector; add 1 for default component
        int len = varData.RegionIndices.Length + 1;
        var bv = new int[len];
        BV = bv;
        LenBV = len;

        // outer loop steps through master designs to be blended
        for (int master = 0; master < len; master++)
        {
            // default factor is always one
            if (master == 0)
            {
                bv[master] = 0x10000;
                continue;
            }

            // VStore array does not include default master, so subtract one
            int idx = varData.RegionIndices[master - 1];
            if (idx >= vs.Regions.Length)
                return true;

            CffAxisCoords[] varRegion = vs.Regions[idx];

            // Note: `lenNDV' could be zero.  In that case, build default blend vector (1,0,0...).
            if (lenNdv == 0)
            {
                bv[master] = 0;
                continue;
            }

            // In the normal case, initialize each component to 1 before inner loop.
            bv[master] = 0x10000;

            // inner loop steps through axes in this region
            for (int j = 0; j < lenNdv; j++)
            {
                CffAxisCoords axis = varRegion[j];

                // compute the scalar contribution of this axis with peak of 0 used for invalid axes
                if (axis.PeakCoord == ndv![j] || axis.PeakCoord == 0)
                    continue;

                // ignore this region if coords are out of range
                if (ndv[j] <= axis.StartCoord || ndv[j] >= axis.EndCoord)
                {
                    bv[master] = 0;
                    break;
                }

                // adjust proportionally
                if (ndv[j] < axis.PeakCoord)
                    bv[master] = FtCalc.MulDiv(bv[master], unchecked(ndv[j] - axis.StartCoord), unchecked(axis.PeakCoord - axis.StartCoord));
                else
                    bv[master] = FtCalc.MulDiv(bv[master], unchecked(axis.EndCoord - ndv[j]), unchecked(axis.EndCoord - axis.PeakCoord));
            }
        }

        // record the parameters used to build the blend vector
        LastVsindex = vsindex;

        if (lenNdv != 0)
        {
            // user has set a normalized vector
            LastNdv = ndv!.AsSpan(0, lenNdv).ToArray();
        }

        LenNdv = lenNdv;
        BuiltBV = true;

        return false;
    }
}

/// <summary>One font of a CFF font set: a Top DICT (or, in a CID-keyed font, a Font DICT) and its Private DICT and local subroutines (<c>CFF_SubFontRec</c>).</summary>
internal sealed class CffSubFont
{
    public readonly CffFontDict FontDict = new();

    /// <summary>The Private DICT; in a CFF2 font it is read again for the location the font is used at (<see cref="CffFont.LoadPrivateDict"/>).</summary>
    public CffPrivate Private { get; set; } = new();

    /// <summary>The blend vector of the Private DICT's own <c>blend</c> operators, and what it was made from.</summary>
    public readonly CffBlend Blend = new();

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
    /// <param name="tableOffset">Where the <c>CFF </c> or <c>CFF2</c> table begins.</param>
    /// <param name="unitsPerEm">The units per em of the font's <c>head</c> table.</param>
    /// <param name="cff2">Whether the table is a <c>CFF2</c> one (variable CFF), which FreeType gives priority over <c>CFF </c>.</param>
    /// <param name="normalizedCoordinates">
    /// The location a CFF2 font is used at: one normalized coordinate (16.16, from -1 to 1) for every axis of its <c>fvar</c> table, which
    /// FreeType keeps for a font that has one even at the default location; <see langword="null"/> for a font with no axes.
    /// </param>
    /// <exception cref="HintingException">FreeType would refuse the font.</exception>
    public static CffFont Load(byte[] data, int tableOffset, int unitsPerEm, bool cff2 = false, int[]? normalizedCoordinates = null)
    {
        var font = new CffFont(data) { IsCff2 = cff2, _baseOffset = tableOffset };
        int baseOffset = tableOffset;
        int pos = baseOffset;

        if (pos < 0 || (long)pos + (cff2 ? 5 : 4) > data.Length)
            throw new HintingException("The CFF table is truncated.");

        int versionMajor = data[pos];
        int headerSize = data[pos + 2];

        CffIndex globalSubrsIndex;
        int topDictPos = 0, topDictLength = 0;

        if (cff2)
        {
            topDictLength = (data[pos + 3] << 8) | data[pos + 4];

            if (versionMajor != 2 || headerSize < 5)
                throw new HintingException("The CFF2 table has an unsupported header.");

            // skip the rest of the header
            pos = baseOffset + headerSize;
            if (pos > data.Length)
                throw new HintingException("The CFF2 table has an unsupported header.");

            // For CFF2, the top dict data immediately follow the header and the length is stored in the header; there is no index for it.
            // Skip the top dict data for now, we will parse it later; next, read the global subrs index.
            topDictPos = pos;
            if ((long)pos + topDictLength > data.Length)
                throw new HintingException("The CFF2 Top DICT reaches past the end of the font.");

            pos += topDictLength;
            globalSubrsIndex = CffIndex.Read(data, ref pos, cff2: true);
        }
        else
        {
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
            globalSubrsIndex = CffIndex.Read(data, ref pos);

            // there must be a Top DICT index entry for each name index entry
            if (nameIndex.Count > fontDictIndex.Count)
                throw new HintingException("The CFF table has too few Top DICT entries.");

            // a font in an SFNT wrapper is only one font
            if (nameIndex.Count > 1)
                throw new HintingException("The CFF table has several fonts in an SFNT wrapper.");

            // now, parse the top-level font dictionary
            if (!fontDictIndex.TryGetElement(0, out topDictPos, out topDictLength))
                throw new HintingException("The CFF table has no such font dictionary.");
        }

        // now, parse the top-level font dictionary
        if (cff2 && topDictPos >= data.Length)
            throw new HintingException("The CFF2 Top DICT is out of the font.");

        font.SubfontLoad(font.TopFont, topDictPos, topDictLength, baseOffset, cff2 ? CffParser.Kind.Cff2Top : CffParser.Kind.Top);

        CffFontDict dict = font.TopFont.FontDict;

        pos = SeekPosition(baseOffset, dict.CharstringsOffset);
        font.CharStrings = CffIndex.Read(data, ref pos, cff2);

        // now, check for a CID or CFF2 font
        if (dict.CidRegistry != CffFontDict.NoSid || cff2)
        {
            // for CFF2, read the Variation Store if available; this must follow the Top DICT parse and precede any Private DICT
            font.VStore = CffVStore.Load(data, baseOffset, dict.VStoreOffset);

            // this is a CID-keyed font, we must now allocate a table of sub-fonts, then load each of them separately
            pos = SeekPosition(baseOffset, dict.FdArrayOffset);
            CffIndex fdIndex = CffIndex.Read(data, ref pos, cff2);

            // a font with too many Font DICTs is used without them
            if (fdIndex.Count <= MaxCidFonts)
            {
                var subFonts = new CffSubFont[fdIndex.Count];
                for (int i = 0; i < subFonts.Length; i++)
                    subFonts[i] = new CffSubFont();

                // now load each subfont independently
                for (int i = 0; i < subFonts.Length; i++)
                {
                    if (!fdIndex.TryGetElement(i, out int fontDictPos, out int fontDictLength))
                        throw new HintingException("The CFF table has no such font dictionary.");

                    font.SubfontLoad(subFonts[i], fontDictPos, fontDictLength, baseOffset, cff2 ? CffParser.Kind.Cff2FontDict : CffParser.Kind.Top);
                }

                font.SubFonts = subFonts;

                // now load the FD Select array; CFF2 omits FDSelect if there is only one FD
                if (!cff2 || fdIndex.Count > 1)
                    font.LoadFdSelect(font.CharStrings.Count, SeekPosition(baseOffset, dict.FdSelectOffset));
            }
        }

        // read the charstrings index now
        if (dict.CharstringsOffset == 0)
            throw new HintingException("The CFF font has no CharStrings.");

        font.NumGlyphs = font.CharStrings.Count;
        font.GlobalSubrs = globalSubrsIndex.GetPointers();

        // read the Charset table if available (a CFF2 font has none)
        if (!cff2 && font.NumGlyphs > 0)
            font.LoadCharset(font.NumGlyphs, baseOffset, dict.CharsetOffset);

        font.NormalizeMatrices(unitsPerEm);
        font.ApplyVariation(normalizedCoordinates);
        return font;
    }

    // FT_STREAM_SEEK( base_offset + offset ): the sum is a 32-bit unsigned number, and a position beyond the font fails the read that follows
    private static int SeekPosition(int baseOffset, uint offset) => (int)Math.Min(int.MaxValue, unchecked((uint)baseOffset + offset));

    /// <summary>Whether the font is a CFF2 one (variable CFF).</summary>
    public bool IsCff2 { get; private init; }

    /// <summary>The variation store of a CFF2 font; empty for a CFF font and for a CFF2 font that has none.</summary>
    public CffVStore VStore { get; private set; } = CffVStore.Empty;

    /// <summary>
    /// Whether the font has a variation store with data sets (<c>vstore->dataCount != 0</c>): the location then decides the Private DICTs and
    /// the charstrings' <c>blend</c> operators.
    /// </summary>
    public bool HasVariations => VStore.Data.Length != 0;

    /// <summary>The normalized coordinates (16.16) the font is used at: one for every axis of the font, or <see langword="null"/> for none.</summary>
    public int[]? Ndv { get; private set; }

    private int _baseOffset;

    // The part of cf2_font_setup that decides whether the Private DICT of a subfont is read again for the location (which FreeType does when
    // a glyph is loaded, and whose outcome is the same for every glyph of the subfont): the Private DICT is parsed with the normalized
    // vector, its `blend' operators resolved. An error of the second parse is not an error of the glyph (FreeType does not look at it),
    // and leaves the Private DICT as far as it was read.
    private void ApplyVariation(int[]? normalizedCoordinates)
    {
        if (!HasVariations)
            return;

        int lenNdv = normalizedCoordinates?.Length ?? 0;
        Ndv = normalizedCoordinates;

        foreach (CffSubFont subfont in SubFonts.Length > 0 ? SubFonts : [TopFont])
        {
            // check whether the Private DICT of the subfont needs to be reparsed
            if (!subfont.Blend.CheckVector(subfont.Private.VsIndex, lenNdv, normalizedCoordinates))
                continue;

            try
            {
                LoadPrivateDict(subfont, _baseOffset, lenNdv, normalizedCoordinates);
            }
            catch (HintingException)
            {
                // FreeType does not look at the outcome of this read (see the comment above): the Private DICT stays as far as it was read
            }
        }
    }

    // cff_subfont_load (code CFF_CODE_TOPDICT, for a Top DICT and for a Font DICT, and the codes of CFF2)
    private void SubfontLoad(CffSubFont subfont, int dictPos, int dictLen, int baseOffset, CffParser.Kind kind)
    {
        CffFontDict top = subfont.FontDict;

        // set default stack size
        top.MaxStack = IsCff2 ? (uint)CffParser.Cff2DefaultStack : 48;

        CffParser.Run(Data, dictPos, dictPos + dictLen, kind, top, null);

        // if it is a CID font, we stop there
        if (top.CidRegistry != CffFontDict.NoSid)
            return;

        // Parse the private dictionary, if any.  CFF2 does not have a private dictionary in the Top DICT but may have one in a Font DICT.  We
        // need to parse the latter here in order to load any local subrs.
        LoadPrivateDict(subfont, baseOffset, 0, null);

        // The random number generator: the seed of the Private DICT (see the remarks of the class); CFF2 has none.
        if (!IsCff2)
            subfont.Random = (uint)subfont.Private.InitialRandomSeed;

        // read the local subrs, if any
        CffPrivate priv = subfont.Private;
        if (priv.LocalSubrsOffset != 0)
        {
            // the sum is FreeType's 32-bit unsigned one: a negative `Subrs' operand points before the Private DICT
            int at = SeekPosition(baseOffset, unchecked(top.PrivateOffset + priv.LocalSubrsOffset));

            // Font DICTs that point at the same INDEX share its table of pointers (a font with 256 of them, each with 65,535 subroutines,
            // would otherwise hold them 256 times over)
            if (!_subrsByStart.TryGetValue(at, out int[]? pointers))
            {
                int pos = at;
                pointers = CffIndex.Read(Data, ref pos, IsCff2).GetPointers();
                _subrsByStart[at] = pointers;
            }

            subfont.LocalSubrs = pointers;
        }
    }

    /// <summary>
    /// Parses the Private DICT of a subfont (<c>cff_load_private_dict</c>). A CFF2 Private DICT can hold <c>blend</c> operators, which take
    /// their factors from the normalized vector <paramref name="ndv"/> (of <paramref name="lenNdv"/> coordinates, zero for none); the first
    /// call, when the font is opened, is always without one.
    /// </summary>
    /// <exception cref="HintingException">The Private DICT is malformed; the subfont's Private DICT is what was read of it.</exception>
    internal void LoadPrivateDict(CffSubFont subfont, int baseOffset, int lenNdv, int[]? ndv)
    {
        CffFontDict top = subfont.FontDict;

        // store handle needed to access memory, vstore for blend; we need this even if there is no private DICT
        subfont.Blend.VStore = VStore;
        subfont.Blend.HasFont = true;
        subfont.Blend.UsedBV = false; // clear state

        if (top.PrivateOffset == 0 || top.PrivateSize == 0)
            return; // no private DICT, do nothing

        // set defaults
        subfont.Private = new CffPrivate();
        CffPrivate priv = subfont.Private;

        priv.BlueShift = 7;
        priv.BlueFuzz = 1;
        priv.BlueScale = (int)(0.039625 * 0x10000L * 1000);

        uint start32 = unchecked((uint)baseOffset + top.PrivateOffset);
        long start = start32;
        long end = start + top.PrivateSize;
        if (end > Data.Length || start > Data.Length)
            throw new HintingException("The Private DICT reaches past the end of the font.");

        // provide inputs for blend calculations
        var blend = new CffBlendContext(subfont.Blend, lenNdv, ndv);

        CffParser.Run(Data, (int)start, (int)end, IsCff2 ? CffParser.Kind.Cff2Private : CffParser.Kind.Private, null, priv, blend, TopFont.FontDict.MaxStack);

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
