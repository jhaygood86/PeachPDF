/****************************************************************************
 *
 * ttgxvar.c
 *
 *   TrueType GX Font Variation loader
 *
 * Copyright (C) 2004-2026 by
 * David Turner, Robert Wilhelm, Werner Lemberg, and George Williams.
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
 * sfobjs.c
 *
 *   SFNT object management (base).
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
 * ftstream.c
 *
 *   I/O stream support (body).
 *
 * Copyright (C) 2000-2026 by
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
 * ftutil.c
 *
 *   FreeType utility file for memory and list management (body).
 *
 * Copyright (C) 2002-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttgxvar.c, sfobjs.c (the check of the fvar table), ftstream.c (the memory stream),
// ftutil.c (the size limit of an array).
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>The error codes of the variation loaders. They are the port's own: 0 is success, and what matters is only that a load fails where FreeType's does.</summary>
internal static class TtVarError
{
    public const int Ok = 0;
    public const int InvalidTable = 1;
    public const int InvalidStreamOperation = 3;
    public const int TableMissing = 4;
    public const int ArrayTooLarge = 5;

    /// <summary>The variation data asks for more work than the port does for one glyph or one set of control values (see <see cref="TtBlend.MaxDeltaWork"/>): FreeType has no such limit.</summary>
    public const int WorkLimit = 6;
}

/// <summary>
/// The part of FreeType's memory stream (<c>ftstream.c</c>) that the loaders of the variation tables use: a position in the bytes of the font file, reads that are checked against
/// the size of the <em>file</em> (not of the table, as FreeType checks them), and a frame, in which the <c>FT_GET_*</c> reads (<c>FT_Stream_GetUShort</c> and the others) give
/// zero, and do not move the cursor, when fewer bytes than they need are left in the frame; the reads through a pointer of the caller (the <c>FT_NEXT_*</c> macros) are not
/// checked, and the caller checks the room it needs, as FreeType's code does.
/// </summary>
internal sealed class FtMemStream
{
    private readonly byte[] _base;
    private readonly long _size;

    // the frame (stream->cursor and stream->limit), as offsets from the start of the file
    private long _cursor;
    private long _limit;

    public FtMemStream(byte[] data)
    {
        _base = data;
        _size = data.Length;
    }

    /// <summary>The position (<c>stream->pos</c>).</summary>
    public uint Pos { get; private set; }

    /// <summary><c>FT_Stream_Seek</c>: seeking to the first position after the file is valid.</summary>
    public bool Seek(uint pos)
    {
        if (pos > _size)
            return false;

        Pos = pos;
        return true;
    }

    /// <summary><c>FT_Stream_ReadUShort</c>.</summary>
    public bool ReadUShort(out int value)
    {
        if (Pos + 1L < _size)
        {
            value = (_base[Pos] << 8) | _base[Pos + 1];
            Pos += 2;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary><c>FT_Stream_ReadULong</c>.</summary>
    public bool ReadULong(out uint value)
    {
        if (Pos + 3L < _size)
        {
            value = ((uint)_base[Pos] << 24) | ((uint)_base[Pos + 1] << 16) | ((uint)_base[Pos + 2] << 8) | _base[Pos + 3];
            Pos += 4;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// <c>FT_Stream_Read</c> of <paramref name="count"/> bytes, of which only whether it fits is needed: a read at or past the end of the file fails (even a read of no bytes), as does one
    /// that is cut short. The position moves by what was read.
    /// </summary>
    public bool SkipRead(uint count)
    {
        if (Pos >= _size)
            return false;

        long available = _size - Pos;
        long read = Math.Min(available, count);
        Pos = (uint)(Pos + read);
        return read >= count;
    }

    /// <summary><c>FT_Stream_EnterFrame</c> of a memory stream: <c>false</c> when the position is at or past the end of the file, or fewer than <paramref name="count"/> bytes follow it.</summary>
    public bool EnterFrame(uint count)
    {
        if (Pos >= _size || _size - Pos < count)
            return false;

        _cursor = Pos;
        _limit = Pos + (long)count;
        Pos += count;
        return true;
    }

    /// <summary><c>FT_Stream_ExitFrame</c>.</summary>
    public void ExitFrame()
    {
        _cursor = 0;
        _limit = 0;
    }

    /// <summary><c>stream->cursor - stream->base</c>.</summary>
    public long FrameCursor => _cursor;

    /// <summary><c>stream->limit - stream->base</c>.</summary>
    public long FrameLimit => _limit;

    /// <summary>The <c>FT_Stream_SeekSet</c> macro of <c>ttgxvar.c</c>: a cursor that would pass the end of the frame is put at the end of it.</summary>
    public void FrameSeekSet(uint offset) => _cursor = offset < (ulong)_limit ? offset : _limit;

    /// <summary>The byte at an offset of the file (zero past the end of it).</summary>
    public int ByteAt(long offset) => (ulong)offset < (ulong)_size ? _base[offset] : 0;

    /// <summary><c>FT_GET_BYTE</c> (<c>FT_Stream_GetByte</c>): zero at the end of the frame.</summary>
    public int GetByte()
    {
        if (_cursor >= _limit)
            return 0;

        return ByteAt(_cursor++);
    }

    /// <summary><c>FT_GET_USHORT</c> (<c>FT_Stream_GetUShort</c>): zero, with the cursor where it was, when the frame has fewer than two bytes left.</summary>
    public int GetUShort()
    {
        if (_cursor + 1 >= _limit)
            return 0;

        int value = (ByteAt(_cursor) << 8) | ByteAt(_cursor + 1);
        _cursor += 2;
        return value;
    }

    /// <summary><c>FT_GET_SHORT</c>.</summary>
    public int GetShort() => (short)GetUShort();

    /// <summary><c>FT_GET_ULONG</c> (<c>FT_Stream_GetULong</c>): zero, with the cursor where it was, when the frame has fewer than four bytes left.</summary>
    public uint GetULong()
    {
        if (_cursor + 3 >= _limit)
            return 0;

        uint value = ((uint)ByteAt(_cursor) << 24) | ((uint)ByteAt(_cursor + 1) << 16) | ((uint)ByteAt(_cursor + 2) << 8) | (uint)ByteAt(_cursor + 3);
        _cursor += 4;
        return value;
    }

    /// <summary><c>FT_GET_LONG</c>.</summary>
    public int GetLong() => unchecked((int)GetULong());

    /// <summary>The 16-bit number at an offset of the file (the <c>FT_NEXT_USHORT</c> of a local pointer).</summary>
    public int UShortAt(long offset) => (ByteAt(offset) << 8) | ByteAt(offset + 1);

    /// <summary>The most points a packed list can hold (<c>ft_var_readpackedpoints</c> reads a 15-bit count).</summary>
    public const int MaxPackedPoints = 0x7FFF;

    /// <summary><c>ft_var_readpackedpoints</c>: the points a tuple's deltas apply to, read into <paramref name="points"/> (which has room for <see cref="MaxPackedPoints"/>). The result
    /// is <see cref="PointsKind.Failed"/> where FreeType's is null (the count is 0 then), <see cref="PointsKind.All"/> for its <c>ALL_POINTS</c> (there is a delta for every point).</summary>
    public PointsKind ReadPackedPoints(ushort[] points, out int pointCount)
    {
        pointCount = 0;

        int n = GetByte();
        if (n == 0)
            return PointsKind.All;

        if ((n & 0x80) != 0)
        {
            n &= 0x7F;
            n <<= 8;
            n |= GetByte();
        }

        long p = _cursor;
        ushort first = 0;
        int i = 0;
        while (i < n)
        {
            if (p >= _limit)
                return PointsKind.Failed;

            int runcnt = ByteAt(p++);
            int cnt = runcnt & 0x7F;

            // first point not included in run count
            cnt++;
            if (cnt > n - i)
                cnt = n - i;

            if ((runcnt & 0x80) != 0)
            {
                if (2L * cnt > _limit - p)
                    return PointsKind.Failed;

                for (int j = 0; j < cnt; j++)
                {
                    first = unchecked((ushort)(first + UShortAt(p)));
                    p += 2;
                    points[i++] = first;
                }
            }
            else
            {
                if (cnt > _limit - p)
                    return PointsKind.Failed;

                for (int j = 0; j < cnt; j++)
                {
                    first = unchecked((ushort)(first + ByteAt(p++)));
                    points[i++] = first;
                }
            }
        }

        _cursor = p;
        pointCount = n;
        return PointsKind.Some;
    }

    /// <summary><c>ft_var_readpackeddeltas</c>: <paramref name="deltaCount"/> deltas in 16.16 (whole numbers of font units), read into <paramref name="deltas"/>; false when the data is too short.</summary>
    public bool ReadPackedDeltas(Span<int> deltas, int deltaCount)
    {
        long p = _cursor;
        int i = 0;
        while (i < deltaCount)
        {
            if (p >= _limit)
                return false;

            int runcnt = ByteAt(p++);
            int cnt = runcnt & 0x3F;

            // first point not included in run count
            cnt++;
            if (cnt > deltaCount - i)
                cnt = deltaCount - i;

            if ((runcnt & 0x80) != 0)
            {
                for (int j = 0; j < cnt; j++)
                    deltas[i++] = 0;
            }
            else if ((runcnt & 0x40) != 0)
            {
                if (2L * cnt > _limit - p)
                    return false;

                for (int j = 0; j < cnt; j++)
                {
                    deltas[i++] = unchecked((int)((uint)(short)UShortAt(p) << 16));
                    p += 2;
                }
            }
            else
            {
                if (cnt > _limit - p)
                    return false;

                for (int j = 0; j < cnt; j++)
                    deltas[i++] = unchecked((int)((uint)(sbyte)ByteAt(p++) << 16));
            }
        }

        _cursor = p;
        return true;
    }
}

/// <summary>What <see cref="FtMemStream.ReadPackedPoints"/> found: FreeType's null, its <c>ALL_POINTS</c>, or a list.</summary>
internal enum PointsKind
{
    /// <summary>The data is too short (FreeType's null).</summary>
    Failed,

    /// <summary>A delta for every point (<c>ALL_POINTS</c>).</summary>
    All,

    /// <summary>A list of points.</summary>
    Some,
}

/// <summary>One axis of a variation region of an item variation store (<c>GX_AxisCoordsRec</c>), in 16.16.</summary>
internal struct TtAxisCoords
{
    public int StartCoord;
    public int PeakCoord;
    public int EndCoord;
}

/// <summary>The data of one item variation data subtable (<c>GX_ItemVarDataRec</c>).</summary>
internal sealed class TtItemVarData
{
    public int ItemCount;
    public int WordDeltaCount;
    public bool LongWords;
    public int RegionIdxCount;
    public int[] RegionIndices = [];

    /// <summary>Where the delta sets start in the font file (FreeType copies them; the bytes of the file are read in place).</summary>
    public long DeltaSetOffset;
}

/// <summary>A delta-set index mapping (<c>GX_DeltaSetIdxMapRec</c>); it has arrays only when it has entries, as in FreeType.</summary>
internal sealed class TtDeltaSetIndexMap
{
    public uint MapCount;

    /// <summary>The entries read so far: FreeType allocates arrays of <see cref="MapCount"/> entries first, and a load that fails leaves the ones it did not reach zero.</summary>
    public uint[]? OuterIndex;
    public uint[]? InnerIndex;

    /// <summary>Whether FreeType has arrays (its <c>innerIndex</c> is not null).</summary>
    public bool HasEntries;

    public uint Outer(uint index) => OuterIndex is { } a && index < a.Length ? a[index] : 0;

    public uint Inner(uint index) => InnerIndex is { } a && index < a.Length ? a[index] : 0;
}

/// <summary>An item variation store (<c>GX_ItemVarStoreRec</c>): regions of the design space and, for each item, a delta for the regions it varies over.</summary>
internal sealed class TtItemVarStore
{
    public int AxisCount;
    public int RegionCount;

    /// <summary>The regions, an array of axes each; null until the region list is read.</summary>
    public TtAxisCoords[][]? RegionList;

    public int DataCount;

    /// <summary>The variation data subtables; null until FreeType allocates them, and the ones a failed load did not reach are empty.</summary>
    public TtItemVarData[]? VarData;

    private byte[] _data = [];

    /// <summary><c>tt_var_load_item_variation_store</c>. The store is filled as it is read, and what a failed load has read stays.</summary>
    /// <returns>An error code of <see cref="TtVarError"/>.</returns>
    public int Load(FtMemStream stream, uint offset, byte[] fileData, int mmAxisCount)
    {
        _data = fileData;

        if (!stream.Seek(offset) || !stream.ReadUShort(out int format))
            return TtVarError.InvalidStreamOperation;

        if (format != 1)
            return TtVarError.InvalidTable;

        // read top level fields
        if (!stream.ReadULong(out uint regionOffset) || !stream.ReadUShort(out int dataCount))
            return TtVarError.InvalidStreamOperation;

        // we need at least one entry in `itemStore->varData'
        if (dataCount == 0)
            return TtVarError.InvalidTable;

        // make temporary copy of item variation data offsets; we will parse region list first, then come back
        var dataOffsetArray = new uint[dataCount];

        if (!stream.EnterFrame((uint)dataCount * 4))
            return TtVarError.InvalidStreamOperation;

        for (int i = 0; i < dataCount; i++)
            dataOffsetArray[i] = stream.GetULong();

        stream.ExitFrame();

        // parse array of region records (region list)
        if (!stream.Seek(unchecked(offset + regionOffset)))
            return TtVarError.InvalidStreamOperation;

        if (!stream.ReadUShort(out int axisCount) || !stream.ReadUShort(out int regionCount))
            return TtVarError.InvalidStreamOperation;

        if (axisCount != mmAxisCount)
            return TtVarError.InvalidTable;

        AxisCount = axisCount;

        // new constraint in OpenType 1.8.4
        if (regionCount >= 32768)
            return TtVarError.InvalidTable;

        RegionList = new TtAxisCoords[regionCount][];
        RegionCount = regionCount;

        if (!stream.EnterFrame((uint)((long)regionCount * axisCount * 6)))
            return TtVarError.InvalidTable;

        for (int i = 0; i < regionCount; i++)
        {
            var axisCoords = new TtAxisCoords[axisCount];
            RegionList[i] = axisCoords;

            for (int j = 0; j < axisCount; j++)
            {
                int start = stream.GetShort();
                int peak = stream.GetShort();
                int end = stream.GetShort();

                // immediately tag invalid ranges with special peak = 0
                if ((start < 0 && end > 0) || start > peak || peak > end)
                    peak = 0;

                axisCoords[j].StartCoord = start << 2;
                axisCoords[j].PeakCoord = peak << 2;
                axisCoords[j].EndCoord = end << 2;
            }
        }

        stream.ExitFrame();

        // end of region list parse; use dataOffsetArray now to parse varData items
        VarData = new TtItemVarData[dataCount];
        for (int i = 0; i < dataCount; i++)
            VarData[i] = new TtItemVarData();

        DataCount = dataCount;
        long regionIndexTotal = 0;

        for (int i = 0; i < dataCount; i++)
        {
            TtItemVarData varData = VarData[i];

            if (!stream.Seek(unchecked(offset + dataOffsetArray[i])))
                return TtVarError.InvalidStreamOperation;

            if (!stream.ReadUShort(out int itemCount) || !stream.ReadUShort(out int wordDeltaCount) || !stream.ReadUShort(out int regionIdxCount))
                return TtVarError.InvalidStreamOperation;

            bool longWords = (wordDeltaCount & 0x8000) != 0;
            wordDeltaCount &= 0x7FFF;

            // check some data consistency
            if (wordDeltaCount > regionIdxCount)
                return TtVarError.InvalidTable;

            if (regionIdxCount > RegionCount)
                return TtVarError.InvalidTable;

            // Data sets may share a header (their offsets can point at the same one), and each names up to 32,767 regions: the ones of all of them together may not be more than the
            // font has bytes (as for the store of a CFF2 font), where FreeType allocates and walks all of them
            regionIndexTotal += regionIdxCount;
            if (regionIndexTotal > _data.Length)
                return TtVarError.InvalidTable;

            // parse region indices
            varData.RegionIndices = new int[regionIdxCount];
            varData.RegionIdxCount = regionIdxCount;
            varData.WordDeltaCount = wordDeltaCount;
            varData.LongWords = longWords;

            if (!stream.EnterFrame((uint)regionIdxCount * 2))
                return TtVarError.InvalidTable;

            for (int j = 0; j < varData.RegionIdxCount; j++)
            {
                varData.RegionIndices[j] = stream.GetUShort();

                if (varData.RegionIndices[j] >= RegionCount)
                {
                    stream.ExitFrame();
                    return TtVarError.InvalidTable;
                }
            }

            stream.ExitFrame();

            uint perRegionSize = (uint)(wordDeltaCount + regionIdxCount);
            if (longWords)
                perRegionSize *= 2;

            // FT_QALLOC_MULT: an array of more than FT_INT_MAX bytes is refused
            if (itemCount != 0 && perRegionSize != 0 && itemCount > int.MaxValue / perRegionSize)
                return TtVarError.ArrayTooLarge;

            uint total = (uint)itemCount * perRegionSize;
            varData.DeltaSetOffset = stream.Pos;

            if (!stream.SkipRead(total))
                return TtVarError.InvalidTable;

            varData.ItemCount = itemCount;
        }

        return TtVarError.Ok;
    }

    /// <summary><c>tt_var_load_delta_set_index_mapping</c>.</summary>
    /// <returns>An error code of <see cref="TtVarError"/>.</returns>
    public int LoadDeltaSetIndexMapping(FtMemStream stream, uint offset, TtDeltaSetIndexMap map, uint tableLen)
    {
        if (!stream.Seek(offset) || !stream.ReadUShort(out int formatAndEntry))
            return TtVarError.InvalidStreamOperation;

        // FT_READ_BYTE( format ) and FT_READ_BYTE( entryFormat ) are two bytes; ReadUShort reads both at once (a file shorter than that fails either way)
        int format = formatAndEntry >> 8;
        int entryFormat = formatAndEntry & 0xFF;

        if (format == 0)
        {
            if (!stream.ReadUShort(out int count))
                return TtVarError.InvalidStreamOperation;

            map.MapCount = (uint)count;
        }
        else if (format == 1) // new in OpenType 1.9
        {
            if (!stream.ReadULong(out uint count))
                return TtVarError.InvalidStreamOperation;

            map.MapCount = count;
        }
        else
        {
            return TtVarError.InvalidTable;
        }

        if ((entryFormat & 0xC0) != 0)
            return TtVarError.InvalidTable;

        // bytes per entry: 1, 2, 3, or 4
        uint entrySize = (uint)(((entryFormat & 0x30) >> 4) + 1);
        int innerBitCount = (entryFormat & 0x0F) + 1;
        uint innerIndexMask = (1u << innerBitCount) - 1;

        // rough sanity check
        if (unchecked(map.MapCount * entrySize) > tableLen)
            return TtVarError.InvalidTable;

        // FT_NEW_ARRAY( map->innerIndex, map->mapCount ) and FT_NEW_ARRAY( map->outerIndex, map->mapCount )
        if (map.MapCount > int.MaxValue / 4)
            return TtVarError.ArrayTooLarge;

        map.HasEntries = map.MapCount > 0;

        uint frameSize = unchecked(map.MapCount * entrySize);
        if (!stream.EnterFrame(frameSize))
        {
            // FreeType has its zeroed arrays by now: an entry that was not read is 0
            return TtVarError.InvalidTable;
        }

        // the arrays are what the frame holds
        var outer = new uint[map.MapCount];
        var inner = new uint[map.MapCount];
        map.OuterIndex = outer;
        map.InnerIndex = inner;

        for (uint i = 0; i < map.MapCount; i++)
        {
            uint mapData = 0;

            // read map data one unsigned byte at a time, big endian
            for (uint j = 0; j < entrySize; j++)
                mapData = (mapData << 8) | (uint)stream.GetByte();

            // new in OpenType 1.8.4
            if (mapData == 0xFFFFFFFFu)
            {
                // no variation data for this item
                outer[i] = 0xFFFF;
                inner[i] = 0xFFFF;
                continue;
            }

            uint outerIndex = mapData >> innerBitCount;

            if (outerIndex >= DataCount)
            {
                stream.ExitFrame();
                return TtVarError.InvalidTable;
            }

            outer[i] = outerIndex;

            uint innerIndex = mapData & innerIndexMask;

            if (innerIndex >= VarData![outerIndex].ItemCount)
            {
                stream.ExitFrame();
                return TtVarError.InvalidTable;
            }

            inner[i] = innerIndex;
        }

        stream.ExitFrame();
        return TtVarError.Ok;
    }

    /// <summary><c>tt_calculate_scalar</c>: how far a location is inside a region, in 16.16.</summary>
    public static int CalculateScalar(TtAxisCoords[] axis, int axisCount, ReadOnlySpan<int> normalizedCoords)
    {
        int scalar = 0x10000;

        // Inner loop steps through axes in this region.
        for (int j = 0; j < axisCount; j++)
        {
            int ncv = normalizedCoords[j];
            TtAxisCoords a = axis[j];

            // Compute the scalar contribution of this axis, with peak of 0 used for invalid axes.
            if (a.PeakCoord == ncv || a.PeakCoord == 0)
                continue;

            // Ignore this region if coordinates are out of range.
            if (ncv <= a.StartCoord || ncv >= a.EndCoord)
            {
                scalar = 0;
                break;
            }

            // Cumulative product of all the axis scalars.
            if (ncv < a.PeakCoord)
                scalar = FtCalc.MulDiv(scalar, unchecked(ncv - a.StartCoord), unchecked(a.PeakCoord - a.StartCoord));
            else // ncv > axis->peakCoord
                scalar = FtCalc.MulDiv(scalar, unchecked(a.EndCoord - ncv), unchecked(a.EndCoord - a.PeakCoord));
        }

        return scalar;
    }

    /// <summary>
    /// The most steps (a region, an axis of it) the deltas of one <c>MVAR</c> value, or of the cross-axis mapping of one <c>avar</c> table, may make the port take. The cost of a delta is
    /// its regions times the axes, which the size of the file bounds; a table of thousands of values and axes multiplies it, and FreeType takes all the steps (minutes for a font of some
    /// megabytes). A real font takes a few hundred.
    /// </summary>
    public const long MaxStoreWork = 1L << 26;

    /// <summary><c>tt_var_get_item_delta</c>: the delta of an item at a location, rounded to a whole number.</summary>
    public int GetItemDelta(ReadOnlySpan<int> normalizedCoords, uint outerIndex, uint innerIndex)
    {
        long unlimited = long.MaxValue;
        return GetItemDelta(normalizedCoords, outerIndex, innerIndex, ref unlimited);
    }

    /// <summary><c>tt_var_get_item_delta</c> that stops adding when the steps it takes would pass what is left of <paramref name="budget"/> (the delta is then 0).</summary>
    public int GetItemDelta(ReadOnlySpan<int> normalizedCoords, uint outerIndex, uint innerIndex, ref long budget)
    {
        // OpenType 1.8.4+: No variation data for this item as indices have special value 0xFFFF.
        if (outerIndex == 0xFFFF && innerIndex == 0xFFFF)
            return 0;

        // See pseudo code from `Font Variations Overview' in the OpenType specification.
        if (VarData is null || outerIndex >= DataCount)
            return 0; // Out of range.

        TtItemVarData varData = VarData[outerIndex];

        if (innerIndex >= varData.ItemCount)
            return 0; // Out of range.

        if (varData.RegionIdxCount == 0)
            return 0; // Avoid "applying zero offset to null pointer".

        budget -= (long)varData.RegionIdxCount * Math.Max(AxisCount, 1);
        if (budget < 0)
            return 0;

        // Parse delta set. Deltas are (word_delta_count + region_idx_count) bytes each if `longWords' isn't set, and twice as much otherwise.
        uint perRegionSize = (uint)(varData.WordDeltaCount + varData.RegionIdxCount);
        uint shiftBase = 1;
        if (varData.LongWords)
        {
            shiftBase = 2;
            perRegionSize *= 2;
        }

        long bytes = varData.DeltaSetOffset + (long)perRegionSize * innerIndex;
        long returnValue = 0;

        // outer loop steps through master designs to be blended
        for (int master = 0; master < varData.RegionIdxCount; master++)
        {
            int regionIndex = varData.RegionIndices[master];

            int scalar = CalculateScalar(RegionList![regionIndex], AxisCount, normalizedCoords);

            if (scalar != 0)
            {
                int delta;

                if (varData.LongWords)
                {
                    if (master < varData.WordDeltaCount)
                    {
                        delta = ReadLong(bytes);
                        bytes += 4;
                    }
                    else
                    {
                        delta = (short)ReadUShort(bytes);
                        bytes += 2;
                    }
                }
                else
                {
                    if (master < varData.WordDeltaCount)
                    {
                        delta = (short)ReadUShort(bytes);
                        bytes += 2;
                    }
                    else
                    {
                        delta = (sbyte)ByteAt(bytes);
                        bytes += 1;
                    }
                }

                returnValue = unchecked(returnValue + (long)delta * scalar);
            }
            else
            {
                // Branch-free, yay.
                bytes += shiftBase << (master < varData.WordDeltaCount ? 1 : 0);
            }
        }

        // ft_round_and_shift16: `(FT_ItemVarDelta)( returnValue + 0x8000L ) >> 16' casts to a 32-bit `FT_Long' first
        return unchecked((int)(returnValue + 0x8000L)) >> 16;
    }

    private int ByteAt(long offset) => (ulong)offset < (ulong)_data.Length ? _data[offset] : 0;

    private int ReadUShort(long offset) => (ByteAt(offset) << 8) | ByteAt(offset + 1);

    private int ReadLong(long offset) => unchecked((int)(((uint)ByteAt(offset) << 24) | ((uint)ByteAt(offset + 1) << 16) | ((uint)ByteAt(offset + 2) << 8) | (uint)ByteAt(offset + 3)));
}

/// <summary>One axis of an <c>avar</c> segment map: the pairs that map a normalized coordinate to another (<c>GX_AVarSegmentRec</c>), in 16.16.</summary>
internal sealed class TtAvarSegment
{
    public int PairCount;
    public int[] FromCoord = [];
    public int[] ToCoord = [];
}

/// <summary>
/// The variation tables of a TrueType or CFF2 font as FreeType reads them (what <c>GX_Blend</c> holds that does not depend on a location): the axes of <c>fvar</c>, <c>avar</c>, the shared
/// tuples and glyph offsets of <c>gvar</c>, and <c>HVAR</c>, <c>VVAR</c> and <c>MVAR</c>. Read once for a font, shared by every location; immutable.
/// </summary>
internal sealed class TtVarTables
{
    private static readonly ConditionalWeakTable<OpenTypeFontface, StrongBox<TtVarTables?>> Cache = new();

    private readonly OpenTypeFontface _font;
    private readonly byte[] _data;

    /// <summary>The number of axes (<c>blend->num_axis</c>).</summary>
    public int NumAxis { get; }

    /// <summary>The font.</summary>
    public OpenTypeFontface Font => _font;

    /// <summary>The bytes of the font file.</summary>
    public byte[] FontData => _data;

    // the axes, in 16.16 (`FT_Var_Axis')
    private readonly int[] _minimum;
    private readonly int[] _default;
    private readonly int[] _maximum;

    // avar
    private bool _hasAvarTable;
    private TtAvarSegment[]? _avarSegments;
    private readonly TtItemVarStore _avarStore = new();
    private readonly TtDeltaSetIndexMap _avarMap = new();

    // MVAR
    private TtItemVarStore? _mvarStore;
    private (uint Tag, uint Outer, uint Inner)[]? _mvarValues;

    private readonly Lazy<GvarData> _gvar;
    private readonly Lazy<HvvarData?> _hvar;
    private readonly Lazy<HvvarData?> _vvar;

    private TtVarTables(OpenTypeFontface font, int[] minimum, int[] defaults, int[] maximum)
    {
        _font = font;
        _data = font.FontSource.Bytes;
        NumAxis = minimum.Length;
        _minimum = minimum;
        _default = defaults;
        _maximum = maximum;

        VerticalInfo = GotoTable(font, "vhea", out _, out uint vheaLength) && vheaLength >= 36 && GotoTable(font, "vmtx", out _, out _);

        LoadAvar();
        LoadMvar();

        _gvar = new Lazy<GvarData>(LoadGvar, LazyThreadSafetyMode.PublicationOnly);
        _hvar = new Lazy<HvvarData?>(() => LoadHvvar("HVAR"), LazyThreadSafetyMode.PublicationOnly);
        _vvar = new Lazy<HvvarData?>(() => LoadHvvar("VVAR"), LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    /// The variation tables of a font, or null when FreeType would not treat it as a variable font: it has no <c>fvar</c> table, or one that <c>sfnt_init_face</c> does not accept.
    /// </summary>
    public static TtVarTables? For(OpenTypeFontface font) => Cache.GetValue(font, static f => new StrongBox<TtVarTables?>(TryCreate(f))).Value;

    /// <summary>
    /// The normalized coordinates FreeType has for a location of a font, in 16.16, one for each axis (zero at the defaults), or null for a font that is not variable. They are made from the
    /// design coordinates the way FreeType makes them (<see cref="Normalize"/>), not by widening the package's own coordinates, which are rounded to 2.14.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="variation">The location, or null for the defaults.</param>
    public static int[]? NormalizedCoordinates(OpenTypeFontface font, VariationCoordinates? variation)
    {
        if (For(font) is not { } tables)
            return null;

        if (variation is null)
            return new int[tables.NumAxis];

        // the design coordinates are multiples of 1/64, so this is exact
        var design = new int[tables.NumAxis];
        for (int i = 0; i < design.Length && i < variation.UserValues.Length; i++)
            design[i] = (int)Math.Round(variation.UserValues[i] * 65536);

        return tables.Normalize(design);
    }

    private static TtVarTables? TryCreate(OpenTypeFontface font)
    {
        // test whether current face is a GX font with named instances
        if (!GotoTable(font, "fvar", out uint fvarStart, out uint fvarLen) || fvarLen < 20)
            return null;

        byte[] data = font.FontSource.Bytes;
        uint version = ReadULong(data, fvarStart);
        int offset = ReadUShort(data, fvarStart + 4);
        int numAxes = ReadUShort(data, fvarStart + 8);
        int axisSize = ReadUShort(data, fvarStart + 10);
        int numInstances = ReadUShort(data, fvarStart + 12);
        int instanceSize = ReadUShort(data, fvarStart + 14);

        // check that the data is bound by the table length
        if (version != 0x00010000u ||
            axisSize != 20 ||
            numAxes == 0 ||
            // `num_axes' limit implied by 16-bit `instance_size'
            numAxes > 0x3FFE ||
            !(instanceSize == 4 + 4 * numAxes || instanceSize == 6 + 4 * numAxes) ||
            // `num_instances' limit implied by limited range of name IDs
            numInstances > 0x7EFF ||
            unchecked((uint)(offset + axisSize * numAxes + instanceSize * numInstances)) > fvarLen)
            return null;

        // TT_Get_MM_Var: the axes
        var minimum = new int[numAxes];
        var defaults = new int[numAxes];
        var maximum = new int[numAxes];

        uint axisAt = fvarStart + (uint)offset;
        for (int i = 0; i < numAxes; i++, axisAt += 20)
        {
            // the data is bound by the table (checked above), so there is nothing to fail
            minimum[i] = unchecked((int)ReadULong(data, axisAt + 4));
            defaults[i] = unchecked((int)ReadULong(data, axisAt + 8));
            maximum[i] = unchecked((int)ReadULong(data, axisAt + 12));

            if (minimum[i] > defaults[i] || defaults[i] > maximum[i])
            {
                minimum[i] = defaults[i];
                maximum[i] = defaults[i];
            }
        }

        return new TtVarTables(font, minimum, defaults, maximum);
    }

    // goto_table: a table that is inside the file
    internal static bool GotoTable(OpenTypeFontface font, string tag, out uint offset, out uint length)
    {
        offset = 0;
        length = 0;

        // tt_face_lookup_table: a table of no bytes is no table
        if (!font.TableDictionary.TryGetValue(tag, out var entry) || entry.Length == 0)
            return false;

        long fileLength = font.FontSource.Bytes.Length;
        if (entry.Offset < 0 || entry.Length < 0 || (long)entry.Offset + entry.Length > fileLength)
            return false;

        offset = (uint)entry.Offset;
        length = (uint)entry.Length;
        return true;
    }

    private static int ReadUShort(byte[] data, long at) => (ByteAt(data, at) << 8) | ByteAt(data, at + 1);

    private static uint ReadULong(byte[] data, long at) =>
        ((uint)ByteAt(data, at) << 24) | ((uint)ByteAt(data, at + 1) << 16) | ((uint)ByteAt(data, at + 2) << 8) | (uint)ByteAt(data, at + 3);

    private static int ByteAt(byte[] data, long at) => (ulong)at < (ulong)data.Length ? data[at] : 0;

    // ---------------------------------------------------------------------------------------------------------------
    //                                                     avar
    // ---------------------------------------------------------------------------------------------------------------

    // ft_var_load_avar
    private void LoadAvar()
    {
        if (!GotoTable(_font, "avar", out uint tableOffset, out uint tableLen))
            return;

        var stream = new FtMemStream(_data);
        if (!stream.Seek(tableOffset) || !stream.EnterFrame(tableLen))
            return;

        int version = stream.GetLong();
        int axisCount = stream.GetLong();

        if (version != 0x00010000 && version != 0x00020000)
        {
            stream.ExitFrame();
            return;
        }

        if (axisCount != NumAxis)
        {
            stream.ExitFrame();
            return;
        }

        // FT_NEW( blend->avar_table )
        _hasAvarTable = true;

        var segments = new TtAvarSegment[axisCount];
        _avarSegments = segments;

        for (int i = 0; i < axisCount; i++)
        {
            var segment = new TtAvarSegment();
            segments[i] = segment;

            segment.PairCount = stream.GetUShort();
            if ((uint)segment.PairCount * 4 > tableLen)
            {
                // Failure. Free everything we have done so far.
                _avarSegments = null;
                stream.ExitFrame();
                return;
            }

            segment.FromCoord = new int[segment.PairCount];
            segment.ToCoord = new int[segment.PairCount];

            for (int j = 0; j < segment.PairCount; j++)
            {
                segment.FromCoord[j] = stream.GetShort() << 2;
                segment.ToCoord[j] = stream.GetShort() << 2;
            }
        }

        if (version < 0x00020000)
        {
            stream.ExitFrame();
            return;
        }

        uint axisMapOffset = stream.GetULong();
        uint storeOffset = stream.GetULong();

        if (storeOffset != 0)
        {
            int error = _avarStore.Load(stream, unchecked(tableOffset + storeOffset), _data, NumAxis);
            if (error != 0)
            {
                stream.ExitFrame();
                return;
            }
        }

        if (axisMapOffset != 0)
        {
            int error = _avarStore.LoadDeltaSetIndexMapping(stream, unchecked(tableOffset + axisMapOffset), _avarMap, tableLen);
            if (error != 0)
            {
                stream.ExitFrame();
                return;
            }
        }

        stream.ExitFrame();
    }

    /// <summary>
    /// <c>ft_var_to_normalized</c>: the normalized coordinates, in 16.16, of design coordinates (in 16.16, one for each axis): each axis mapped to -1 to 1 by its minimum,
    /// default and maximum, and then by the segment maps (and cross-axis mapping) of <c>avar</c>.
    /// </summary>
    /// <param name="design">The design coordinates in 16.16.</param>
    public int[] Normalize(ReadOnlySpan<int> design)
    {
        int numCoords = Math.Min(design.Length, NumAxis);
        var normalized = new int[NumAxis];

        // Axis normalization is a two-stage process. First we normalize based on the [min,def,max] values for the axis to be [-1,0,1]. Then, if there's an `avar' table, we renormalize this range.
        int i;
        for (i = 0; i < numCoords; i++)
        {
            int coord = design[i];

            if (coord > _default[i])
            {
                normalized[i] = coord >= _maximum[i]
                    ? 0x10000
                    : FtCalc.DivFix(unchecked(coord - _default[i]), unchecked(_maximum[i] - _default[i]));
            }
            else if (coord < _default[i])
            {
                normalized[i] = coord <= _minimum[i]
                    ? -0x10000
                    : FtCalc.DivFix(unchecked(coord - _default[i]), unchecked(_default[i] - _minimum[i]));
            }
            else
            {
                normalized[i] = 0;
            }
        }

        for (; i < NumAxis; i++)
            normalized[i] = 0;

        if (_hasAvarTable)
        {
            if (_avarSegments is { } segments)
            {
                for (i = 0; i < NumAxis; i++)
                {
                    TtAvarSegment av = segments[i];

                    for (int j = 1; j < av.PairCount; j++)
                    {
                        if (normalized[i] < av.FromCoord[j])
                        {
                            normalized[i] =
                                unchecked(FtCalc.MulDiv(
                                    unchecked(normalized[i] - av.FromCoord[j - 1]),
                                    unchecked(av.ToCoord[j] - av.ToCoord[j - 1]),
                                    unchecked(av.FromCoord[j] - av.FromCoord[j - 1])) +
                                  av.ToCoord[j - 1]);
                            break;
                        }
                    }
                }
            }

            if (_avarStore.VarData is not null)
            {
                var newNormalized = new int[NumAxis];
                long budget = TtItemVarStore.MaxStoreWork;

                // Install our half-normalized coordinates for the next Item Variation Store to work with.
                for (i = 0; i < NumAxis; i++)
                {
                    int v = normalized[i];
                    uint innerIndex = (uint)i;
                    uint outerIndex = 0;

                    if (_avarMap.HasEntries)
                    {
                        uint idx = (uint)i;

                        if (idx >= _avarMap.MapCount)
                            idx = _avarMap.MapCount - 1;

                        outerIndex = _avarMap.Outer(idx);
                        innerIndex = _avarMap.Inner(idx);
                    }

                    int delta = _avarStore.GetItemDelta(normalized, outerIndex, innerIndex, ref budget);

                    // Convert delta in F2DOT14 to 16.16 before adding.
                    v = unchecked(v + delta * 4);

                    // Clamp value to range [-1, 1].
                    v = v >= 0x10000 ? 0x10000 : v;
                    v = v <= -0x10000 ? -0x10000 : v;

                    newNormalized[i] = v;
                }

                for (i = 0; i < NumAxis; i++)
                    normalized[i] = newNormalized[i];
            }
        }

        return normalized;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                     MVAR
    // ---------------------------------------------------------------------------------------------------------------

    // ft_var_load_mvar: called when the multiple-master data of the face is first loaded
    private void LoadMvar()
    {
        if (!GotoTable(_font, "MVAR", out uint tableOffset, out uint tableLen))
            return;

        var stream = new FtMemStream(_data);
        if (!stream.Seek(tableOffset))
            return;

        // skip minor version
        if (!stream.ReadUShort(out int majorVersion) || !stream.Seek(unchecked(stream.Pos + 2u)))
            return;

        if (majorVersion != 1)
            return;

        // skip reserved entry and value record size
        if (!stream.Seek(unchecked(stream.Pos + 4u)))
            return;

        if (!stream.ReadUShort(out int valueCount) || !stream.ReadUShort(out int storeOffset))
            return;

        uint recordsOffset = stream.Pos;

        var store = new TtItemVarStore();
        if (store.Load(stream, unchecked(tableOffset + (uint)storeOffset), _data, NumAxis) != 0)
            return;

        var values = new (uint Tag, uint Outer, uint Inner)[valueCount];

        if (!stream.Seek(recordsOffset) || !stream.EnterFrame((uint)valueCount * 8))
            return;

        bool error = false;
        for (int i = 0; i < valueCount; i++)
        {
            uint tag = stream.GetULong();
            uint outer = (uint)stream.GetUShort();
            uint inner = (uint)stream.GetUShort();
            values[i] = (tag, outer, inner);

            // new in OpenType 1.8.4
            if (outer == 0xFFFFu && inner == 0xFFFFu)
            {
                // no variation data for this item
                continue;
            }

            if (outer >= store.DataCount || inner >= store.VarData![outer].ItemCount)
            {
                error = true;
                break;
            }
        }

        stream.ExitFrame();

        if (error)
            return;

        _mvarStore = store;
        _mvarValues = values;
    }

    /// <summary>
    /// <c>tt_apply_mvar</c> for one value: <paramref name="unmodified"/> plus the deltas <c>MVAR</c> has for <paramref name="tag"/> at a location, as FreeType adds them (the last record with a
    /// delta that is not zero wins, as each is applied to the unmodified value in turn).
    /// </summary>
    public short MvarAdjust(ReadOnlySpan<int> normalizedCoords, uint tag, short unmodified)
    {
        if (_mvarStore is not { } store || _mvarValues is not { } values)
            return unmodified;

        short result = unmodified;
        long budget = TtItemVarStore.MaxStoreWork;
        foreach (var (valueTag, outer, inner) in values)
        {
            // FreeType adds the delta of every value and keeps the ones of the fields it has; only the ones of the field asked for are added here
            if (valueTag != tag)
                continue;

            int delta = store.GetItemDelta(normalizedCoords, outer, inner, ref budget);

            if (delta != 0)
            {
                // since we handle both signed and unsigned values as FT_Short, ensure proper overflow arithmetic
                result = unchecked((short)(unmodified + (short)delta));
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                 HVAR and VVAR
    // ---------------------------------------------------------------------------------------------------------------

    internal sealed class HvvarData
    {
        public TtItemVarStore ItemStore = new();
        public TtDeltaSetIndexMap WidthMap = new();
    }

    // ft_var_load_hvvar: null when the load fails (FreeType then does not adjust the advances)
    private HvvarData? LoadHvvar(string tag)
    {
        if (!GotoTable(_font, tag, out uint tableOffset, out uint tableLen))
            return null;

        var stream = new FtMemStream(_data);
        if (!stream.Seek(tableOffset))
            return null;

        // skip minor version
        if (!stream.ReadUShort(out int majorVersion) || !stream.Seek(unchecked(stream.Pos + 2u)))
            return null;

        if (majorVersion != 1)
            return null;

        if (!stream.ReadULong(out uint storeOffset) || !stream.ReadULong(out uint widthMapOffset))
            return null;

        var table = new HvvarData();

        if (table.ItemStore.Load(stream, unchecked(tableOffset + storeOffset), _data, NumAxis) != 0)
            return null;

        if (widthMapOffset != 0)
        {
            if (table.ItemStore.LoadDeltaSetIndexMapping(stream, unchecked(tableOffset + widthMapOffset), table.WidthMap, tableLen) != 0)
                return null;
        }

        return table;
    }

    /// <summary>Whether the font has vertical metrics (<c>face->vertical_info</c>): a <c>vhea</c> table and a <c>vmtx</c> table.</summary>
    public bool VerticalInfo { get; }

    /// <summary>The <c>HVAR</c> table when it loaded (<c>TT_FACE_FLAG_VAR_HADVANCE</c>).</summary>
    internal HvvarData? Hvar => _hvar.Value;

    /// <summary>The <c>VVAR</c> table when it loaded (<c>TT_FACE_FLAG_VAR_VADVANCE</c>).</summary>
    internal HvvarData? Vvar => _vvar.Value;

    // ---------------------------------------------------------------------------------------------------------------
    //                                                     gvar
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>What <c>ft_var_load_gvar</c> loads: the glyph offsets and the shared tuples, or the error that stops FreeType from setting a location.</summary>
    internal sealed class GvarData
    {
        /// <summary><see cref="TtVarError.Ok"/>, <see cref="TtVarError.TableMissing"/> (fine: no glyph varies) or a real error (no location can be set).</summary>
        public int Error;

        /// <summary><c>blend->glyphoffsets</c>: where the data of each glyph starts, with one more entry for where the last ends.</summary>
        public uint[]? GlyphOffsets;

        public int GlyphCount;

        /// <summary><c>blend->tuplecoords</c>: the shared tuples, in 16.16, one after another (<see cref="NumAxis"/> each).</summary>
        public int[] TupleCoords = [];

        public int TupleCount;
    }

    internal GvarData Gvar => _gvar.Value;

    // ft_var_load_gvar
    private GvarData LoadGvar()
    {
        var result = new GvarData();
        var stream = new FtMemStream(_data);

        if (!GotoTable(_font, "gvar", out uint gvarStart, out uint tableLen))
        {
            result.Error = TtVarError.TableMissing;
            return result;
        }

        if (!stream.Seek(gvarStart) || !stream.EnterFrame(20))
        {
            result.Error = TtVarError.InvalidStreamOperation;
            return result;
        }

        int version = stream.GetLong();
        int axisCount = stream.GetUShort();
        int globalCoordCount = stream.GetUShort();
        uint offsetToCoord = stream.GetULong();
        int glyphCount = stream.GetUShort();
        int flags = stream.GetUShort();
        uint offsetToDataField = stream.GetULong();
        stream.ExitFrame();

        if (version != 0x00010000)
        {
            result.Error = TtVarError.InvalidTable;
            return result;
        }

        if (axisCount != (ushort)NumAxis)
        {
            result.Error = TtVarError.InvalidTable;
            return result;
        }

        // rough sanity check, ignoring offsets
        if (unchecked((uint)globalCoordCount * (uint)axisCount) > tableLen / 2)
        {
            result.Error = TtVarError.InvalidTable;
            return result;
        }

        // offsets can be either 2 or 4 bytes (one more offset than glyphs, to mark size of last)
        uint offsetsLen = unchecked((uint)(glyphCount + 1) * ((flags & 1) != 0 ? 4u : 2u));

        // rough sanity check
        if (offsetsLen > tableLen)
        {
            result.Error = TtVarError.InvalidTable;
            return result;
        }

        uint offsetToData = unchecked(gvarStart + offsetToDataField);

        if (!stream.EnterFrame(offsetsLen))
        {
            result.Error = TtVarError.InvalidStreamOperation;
            return result;
        }

        var glyphOffsets = new uint[glyphCount + 1];
        uint limit = unchecked(gvarStart + tableLen);
        uint maxOffset = 0;

        for (int i = 0; i <= glyphCount; i++)
        {
            glyphOffsets[i] = unchecked(offsetToData + ((flags & 1) != 0 ? stream.GetULong() : (uint)stream.GetUShort() * 2));

            if (maxOffset <= glyphOffsets[i])
                maxOffset = glyphOffsets[i];
            else
                glyphOffsets[i] = maxOffset;

            // use `<', not `<='
            if (limit < glyphOffsets[i])
                glyphOffsets[i] = limit;
        }

        stream.ExitFrame();

        if (globalCoordCount != 0)
        {
            if (!stream.Seek(unchecked(gvarStart + offsetToCoord)) || !stream.EnterFrame((uint)(globalCoordCount * axisCount * 2L)))
            {
                result.Error = TtVarError.InvalidStreamOperation;
                return result;
            }

            var tupleCoords = new int[axisCount * globalCoordCount];
            for (int i = 0; i < globalCoordCount; i++)
            {
                for (int j = 0; j < axisCount; j++)
                    tupleCoords[i * axisCount + j] = stream.GetShort() << 2;
            }

            stream.ExitFrame();

            result.TupleCoords = tupleCoords;
            result.TupleCount = globalCoordCount;
        }

        result.GlyphOffsets = glyphOffsets;
        result.GlyphCount = glyphCount;
        return result;
    }
}

/// <summary>
/// A location of a variable font as FreeType keeps it (<c>GX_Blend</c> with its normalized coordinates set): the normalized coordinates in 16.16 and everything that follows from them:
/// the control values of <c>cvar</c>, the deltas of <c>gvar</c> that move the points of a glyph, and the adjustments of <c>HVAR</c>, <c>VVAR</c> and <c>MVAR</c>. Immutable, so one
/// instance serves any number of threads (a glyph load uses scratch of its own).
/// </summary>
internal sealed class TtBlend
{
    private const int TupleCountMask = 0x0FFF;
    private const int TuplesSharePointNumbers = 0x8000;
    private const int EmbeddedTupleCoord = 0x8000;
    private const int IntermediateTuple = 0x4000;
    private const int PrivatePointNumbers = 0x2000;
    private const int TupleIndexMask = 0x0FFF;

    private readonly TtVarTables _tables;
    private readonly byte[] _data;

    private TtBlend(TtVarTables tables, byte[] data, int[] normalized)
    {
        _tables = tables;
        _data = data;
        NormalizedCoords = normalized;

        bool doBlend = false;
        foreach (int c in normalized)
        {
            if (c != 0)
            {
                doBlend = true;
                break;
            }
        }

        DoBlend = doBlend;
    }

    /// <summary>The number of axes (<c>blend->num_axis</c>).</summary>
    public int NumAxis => _tables.NumAxis;

    /// <summary>The normalized coordinates in 16.16, one for each axis (<c>blend->normalizedcoords</c>). Do not change.</summary>
    public int[] NormalizedCoords { get; }

    /// <summary>Whether any coordinate is not zero (<c>face->doblend</c>, and what makes the face a variation instance): nothing varies at the defaults.</summary>
    public bool DoBlend { get; }

    /// <summary>Whether the advance widths follow <c>HVAR</c> (<c>TT_FACE_FLAG_VAR_HADVANCE</c>), which needs a location that varies.</summary>
    public bool HAdvanceSupport => DoBlend && _tables.Hvar is not null;

    /// <summary>Whether the advance heights follow <c>VVAR</c> (<c>TT_FACE_FLAG_VAR_VADVANCE</c>): only a font with vertical metrics asks for them.</summary>
    public bool VAdvanceSupport => DoBlend && _tables.VerticalInfo && _tables.Vvar is not null;

    /// <summary>
    /// <c>TT_Set_MM_Blend</c>: the location given by normalized coordinates (in 16.16, one for each axis of the font; the missing ones are 0). Null when FreeType refuses to set it: a coordinate is outside
    /// -1 to 1, or the <c>gvar</c> table is malformed (whatever the coordinates: a face set to the defaults is refused as well).
    /// </summary>
    /// <param name="tables">The variation tables of the font.</param>
    /// <param name="normalized">The normalized coordinates in 16.16.</param>
    /// <param name="isCff2">Whether the font has CFF2 outlines, which have no <c>gvar</c> deltas.</param>
    public static TtBlend? TryCreate(TtVarTables tables, ReadOnlySpan<int> normalized, bool isCff2)
    {
        int numCoords = Math.Min(normalized.Length, tables.NumAxis);
        var coords = new int[tables.NumAxis];

        for (int i = 0; i < numCoords; i++)
        {
            if (normalized[i] < -0x10000 || normalized[i] > 0x10000)
                return null;

            coords[i] = normalized[i];
        }

        if (!isCff2)
        {
            // While a missing 'gvar' table is acceptable, an incorrect SFNT table offset or size for 'gvar', or an inconsistent 'gvar' table is not.
            int error = tables.Gvar.Error;
            if (error != TtVarError.TableMissing && error != TtVarError.Ok)
                return null;
        }

        return new TtBlend(tables, tables.FontData, coords);
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                   the metrics
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary><c>tt_hvadvance_adjust</c>: an advance in font units after <c>HVAR</c> (or <c>VVAR</c>), or unchanged when the font has none.</summary>
    /// <param name="vertical">Whether it is an advance height.</param>
    /// <param name="glyphIndex">The glyph.</param>
    /// <param name="advance">The advance from <c>hmtx</c> (or <c>vmtx</c>).</param>
    public int AdjustAdvance(bool vertical, int glyphIndex, int advance)
    {
        if (!DoBlend)
            return advance;

        TtVarTables.HvvarData? table = vertical ? _tables.Vvar : _tables.Hvar;
        if (table is null)
            return advance;

        // advance width or height adjustments are always present in an `HVAR' or `VVAR' table; no need to test for this capability
        uint outerIndex, innerIndex;
        if (table.WidthMap.HasEntries)
        {
            uint idx = (uint)glyphIndex;

            if (idx >= table.WidthMap.MapCount)
                idx = table.WidthMap.MapCount - 1;

            // trust that HVAR parser has checked indices
            outerIndex = table.WidthMap.Outer(idx);
            innerIndex = table.WidthMap.Inner(idx);
        }
        else
        {
            // no widthMap data
            outerIndex = 0;
            innerIndex = (uint)glyphIndex;
        }

        int delta = table.ItemStore.GetItemDelta(NormalizedCoords, outerIndex, innerIndex);

        // *avalue = ADD_INT( *avalue, delta ), and the caller keeps 16 bits (an FT_UShort)
        return unchecked(advance + delta) & 0xFFFF;
    }

    /// <summary><c>tt_apply_mvar</c> for one value of the font: the value with what <c>MVAR</c> adds to it at this location.</summary>
    public short MvarAdjust(uint tag, short unmodified) => _tables.MvarAdjust(NormalizedCoords, tag, unmodified);

    // ---------------------------------------------------------------------------------------------------------------
    //                                                      tuples
    // ---------------------------------------------------------------------------------------------------------------

    // ft_var_apply_tuple: how much a tuple applies to the location, in 16.16 (0: not at all)
    private int ApplyTuple(int tupleIndex, ReadOnlySpan<int> tupleCoords, ReadOnlySpan<int> imStartCoords, ReadOnlySpan<int> imEndCoords)
    {
        int apply = 0x10000;

        for (int i = 0; i < NumAxis; i++)
        {
            if (tupleCoords[i] == 0)
                continue;

            int ncv = NormalizedCoords[i];

            if (ncv == 0)
            {
                apply = 0;
                break;
            }

            if (tupleCoords[i] == ncv)
            {
                // `apply' does not change
                continue;
            }

            if ((tupleIndex & IntermediateTuple) == 0)
            {
                // not an intermediate tuple
                if ((tupleCoords[i] > ncv && ncv > 0) || (tupleCoords[i] < ncv && ncv < 0))
                {
                    apply = FtCalc.MulDiv(apply, ncv, tupleCoords[i]);
                }
                else
                {
                    apply = 0;
                    break;
                }
            }
            else
            {
                // intermediate tuple
                if (ncv <= imStartCoords[i] || ncv >= imEndCoords[i])
                {
                    apply = 0;
                    break;
                }

                if (ncv < tupleCoords[i])
                    apply = FtCalc.MulDiv(apply, unchecked(ncv - imStartCoords[i]), unchecked(tupleCoords[i] - imStartCoords[i]));
                else // ncv > tuple_coords[i]
                    apply = FtCalc.MulDiv(apply, unchecked(imEndCoords[i] - ncv), unchecked(imEndCoords[i] - tupleCoords[i]));
            }
        }

        return apply;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                      cvar
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>tt_face_vary_cvt</c>: changes the control values (in 26.6) by the <c>cvar</c> table at this location. Most errors are ignored, as in FreeType (it is perfectly valid not to have a
    /// <c>cvar</c> table even if there is a <c>gvar</c> and <c>fvar</c> table); the ones it reports (an invalid header, an invalid tuple index) leave the control values as they were.
    /// </summary>
    /// <returns>An error code of <see cref="TtVarError"/>.</returns>
    public int VaryCvt(int[] cvt)
    {
        if (cvt.Length == 0)
            return TtVarError.Ok;

        var tables = _tables;
        if (!TtVarTables.GotoTable(tables.Font, "cvar", out uint tableStart, out uint tableLen))
            return TtVarError.Ok;

        var stream = new FtMemStream(_data);
        if (!stream.Seek(tableStart) || !stream.EnterFrame(tableLen))
            return TtVarError.Ok;

        // the scratch of this load: the deltas and the lists of points that are read, and the summed deltas
        int[] deltaBuffer = ArrayPool<int>.Shared.Rent(Math.Max(cvt.Length, FtMemStream.MaxPackedPoints));
        ushort[] sharedBuffer = ArrayPool<ushort>.Shared.Rent(FtMemStream.MaxPackedPoints);
        ushort[] pointBuffer = ArrayPool<ushort>.Shared.Rent(FtMemStream.MaxPackedPoints);

        try
        {
            if (stream.GetLong() != 0x00010000)
                return TtVarError.Ok;

            int tupleCount = stream.GetUShort();
            uint offsetToData = (uint)stream.GetUShort();

            // rough sanity test
            if (offsetToData + (uint)((tupleCount & TupleCountMask) * 4) > tableLen)
                return TtVarError.InvalidTable;

            offsetToData += tableStart;

            PointsKind sharedKind = PointsKind.Failed;
            int spointCount = 0;

            if ((tupleCount & TuplesSharePointNumbers) != 0)
            {
                long here = stream.FrameCursor;

                stream.FrameSeekSet(offsetToData);
                sharedKind = stream.ReadPackedPoints(sharedBuffer, out spointCount);
                offsetToData = (uint)stream.FrameCursor;

                stream.FrameSeekSet((uint)here);
            }

            int numAxis = NumAxis;
            var peakCoords = new int[numAxis];
            var imStartCoords = new int[numAxis];
            var imEndCoords = new int[numAxis];
            var cvtDeltas = new int[cvt.Length];

            TtVarTables.GvarData gvar = _tables.Gvar;
            long work = 0;

            int tuples = tupleCount & TupleCountMask;
            for (int i = 0; i < tuples; i++)
            {
                int tupleDataSize = stream.GetUShort();
                int tupleIndex = stream.GetUShort();
                ReadOnlySpan<int> tupleCoords;

                if ((tupleIndex & EmbeddedTupleCoord) != 0)
                {
                    for (int j = 0; j < numAxis; j++)
                        peakCoords[j] = stream.GetShort() << 2;

                    tupleCoords = peakCoords;
                }
                else if ((tupleIndex & TupleIndexMask) < gvar.TupleCount)
                {
                    tupleCoords = gvar.TupleCoords.AsSpan((tupleIndex & TupleIndexMask) * numAxis, numAxis);
                }
                else
                {
                    return TtVarError.InvalidTable;
                }

                if ((tupleIndex & IntermediateTuple) != 0)
                {
                    for (int j = 0; j < numAxis; j++)
                        imStartCoords[j] = stream.GetShort() << 2;
                    for (int j = 0; j < numAxis; j++)
                        imEndCoords[j] = stream.GetShort() << 2;
                }

                // a font of 16,382 axes has 4,095 tuples of them: weighing a tuple against the location is work as well
                work += numAxis;
                if (work > MaxDeltaWork)
                    return TtVarError.WorkLimit;

                int apply = ApplyTuple(tupleIndex, tupleCoords, imStartCoords, imEndCoords);

                if (apply == 0) // tuple isn't active for our blend
                {
                    offsetToData += (uint)tupleDataSize;
                    continue;
                }

                long savedCursor = stream.FrameCursor;

                stream.FrameSeekSet(offsetToData);

                PointsKind kind;
                ushort[] points;
                int pointCount;

                if ((tupleIndex & PrivatePointNumbers) != 0)
                {
                    kind = stream.ReadPackedPoints(pointBuffer, out pointCount);
                    points = pointBuffer;
                }
                else
                {
                    kind = sharedKind;
                    points = sharedBuffer;
                    pointCount = spointCount;
                }

                // a table can make every one of 4,095 tuples read the same data over and over: the work of one set of control values is bounded
                int deltaCount = pointCount == 0 ? cvt.Length : pointCount;
                work += deltaCount;
                if (work > MaxDeltaWork)
                    return TtVarError.WorkLimit;

                bool read = stream.ReadPackedDeltas(deltaBuffer, deltaCount);

                if (kind == PointsKind.Failed || !read)
                {
                    // failure, ignore it
                }
                else if (kind == PointsKind.All)
                {
                    // this means that there are deltas for every entry in cvt
                    for (int j = 0; j < cvt.Length; j++)
                        cvtDeltas[j] = unchecked(cvtDeltas[j] + FtCalc.MulFix(deltaBuffer[j], apply));
                }
                else
                {
                    for (int j = 0; j < pointCount; j++)
                    {
                        int pindex = points[j];
                        if ((uint)pindex >= (uint)cvt.Length)
                            continue;

                        cvtDeltas[pindex] = unchecked(cvtDeltas[pindex] + FtCalc.MulFix(deltaBuffer[j], apply));
                    }
                }

                offsetToData += (uint)tupleDataSize;

                stream.FrameSeekSet((uint)savedCursor);
            }

            for (int i = 0; i < cvt.Length; i++)
                cvt[i] = unchecked(cvt[i] + FixedToFdot6(cvtDeltas[i]));

            return TtVarError.Ok;
        }
        finally
        {
            stream.ExitFrame();
            ArrayPool<int>.Shared.Return(deltaBuffer);
            ArrayPool<ushort>.Shared.Return(sharedBuffer);
            ArrayPool<ushort>.Shared.Return(pointBuffer);
        }
    }

    /// <summary>
    /// The most delta values one glyph (or one set of control values) may make the port read and add: for each tuple that applies, the points of the glyph, and the deltas it lists. A table can
    /// make each of its 4,095 tuples apply to the same 65,535 points, which FreeType does the arithmetic of (a font of a few kilobytes costs it hundreds of millions of operations and gigabytes
    /// of allocations a glyph); a real font is far below this, and a glyph or a location that goes over is refused, so that the unhinted outline is used.
    /// </summary>
    public const long MaxDeltaWork = 1 << 24;

    // FT_fixedToFdot6
    private static int FixedToFdot6(int x) => unchecked(x + 0x200) >> 10;

    // FT_fixedToInt
    private static int FixedToInt(int x) => (short)(unchecked((uint)x + 0x8000u) >> 16);

    // ---------------------------------------------------------------------------------------------------------------
    //                                                 the points of a glyph
    // ---------------------------------------------------------------------------------------------------------------

    // Shift the original coordinates of all points between indices `p1' and `p2', using the same difference as given by index `ref'. modeled after `af_iup_shift'
    private static void DeltaShift(int p1, int p2, int refPoint, Span<int> inX, Span<int> inY, Span<int> outX, Span<int> outY)
    {
        int deltaX = unchecked(outX[refPoint] - inX[refPoint]);
        int deltaY = unchecked(outY[refPoint] - inY[refPoint]);

        if (deltaX == 0 && deltaY == 0)
            return;

        for (int p = p1; p < refPoint; p++)
        {
            outX[p] = unchecked(outX[p] + deltaX);
            outY[p] = unchecked(outY[p] + deltaY);
        }

        for (int p = refPoint + 1; p <= p2; p++)
        {
            outX[p] = unchecked(outX[p] + deltaX);
            outY[p] = unchecked(outY[p] + deltaY);
        }
    }

    // Interpolate the original coordinates of all points with indices between `p1' and `p2', using `ref1' and `ref2' as the reference point indices.
    // modeled after `af_iup_interp', `_iup_worker_interpolate', and `Ins_IUP' with spec differences in handling ill-defined cases.
    private static void DeltaInterpolate(int p1, int p2, ref int ref1, ref int ref2, Span<int> inX, Span<int> inY, Span<int> outX, Span<int> outY)
    {
        if (p1 > p2)
            return;

        // handle both horizontal and vertical coordinates
        for (int i = 0; i <= 1; i++)
        {
            // shift array pointers so that we can access `foo.y' as `foo.x'
            Span<int> inPoints = i == 0 ? inX : inY;
            Span<int> outPoints = i == 0 ? outX : outY;

            if (inPoints[ref1] > inPoints[ref2])
            {
                int p = ref1;
                ref1 = ref2;
                ref2 = p;
            }

            int in1 = inPoints[ref1];
            int in2 = inPoints[ref2];
            int out1 = outPoints[ref1];
            int out2 = outPoints[ref2];
            int d1 = unchecked(out1 - in1);
            int d2 = unchecked(out2 - in2);

            // If the reference points have the same coordinate but different delta, inferred delta is zero. Otherwise interpolate.
            if (in1 != in2 || out1 == out2)
            {
                int scale = in1 != in2 ? FtCalc.DivFix(unchecked(out2 - out1), unchecked(in2 - in1)) : 0;

                for (int p = p1; p <= p2; p++)
                {
                    int o = inPoints[p];

                    if (o <= in1)
                        o = unchecked(o + d1);
                    else if (o >= in2)
                        o = unchecked(o + d2);
                    else
                        o = unchecked(out1 + FtCalc.MulFix(unchecked(o - in1), scale));

                    outPoints[p] = o;
                }
            }
        }
    }

    // Interpolate points without delta values, similar to the `IUP' hinting instruction. modeled after `Ins_IUP'
    private static void InterpolateDeltas(ReadOnlySpan<ushort> contours, int nContours, Span<int> outX, Span<int> outY, Span<int> inX, Span<int> inY, Span<bool> hasDelta)
    {
        // ignore empty outlines
        if (nContours == 0)
            return;

        int contour = 0;
        int point = 0;

        do
        {
            int endPoint = contours[contour];
            int firstPoint = point;

            // search first point that has a delta
            while (point <= endPoint && !hasDelta[point])
                point++;

            if (point <= endPoint)
            {
                int firstDelta = point;
                int curDelta = point;

                point++;

                while (point <= endPoint)
                {
                    // search next point that has a delta and interpolate intermediate points
                    if (hasDelta[point])
                    {
                        int r1 = curDelta;
                        int r2 = point;
                        DeltaInterpolate(curDelta + 1, point - 1, ref r1, ref r2, inX, inY, outX, outY);
                        curDelta = point;
                    }

                    point++;
                }

                // shift contour if we only have a single delta
                if (curDelta == firstDelta)
                {
                    DeltaShift(firstPoint, endPoint, curDelta, inX, inY, outX, outY);
                }
                else
                {
                    // otherwise handle remaining points at the end and beginning of the contour
                    int r1 = curDelta;
                    int r2 = firstDelta;
                    DeltaInterpolate(curDelta + 1, endPoint, ref r1, ref r2, inX, inY, outX, outY);

                    if (firstDelta > 0)
                    {
                        r1 = curDelta;
                        r2 = firstDelta;
                        DeltaInterpolate(firstPoint, firstDelta - 1, ref r1, ref r2, inX, inY, outX, outY);
                    }
                }
            }

            contour++;
        }
        while (contour < nContours);
    }

    /// <summary>
    /// <c>TT_Vary_Apply_Glyph_Deltas</c>: moves the points of a glyph, and its phantom points, by the deltas of <c>gvar</c> at this location. The deltas are added up in 16.16, the
    /// points that a tuple gives no delta are interpolated as the <c>IUP</c> instruction would, and the sum is rounded to a whole number of font units for the points and to 26.6 for
    /// <paramref name="unroundedX"/> and <paramref name="unroundedY"/>, which the caller scales.
    /// </summary>
    /// <param name="glyphIndex">The glyph.</param>
    /// <param name="pointCount">The number of points of the glyph, without the four phantom points (which follow them).</param>
    /// <param name="x">The x coordinates in font units, phantom points included; changed.</param>
    /// <param name="y">The y coordinates in font units, phantom points included; changed.</param>
    /// <param name="contours">The last point of each contour.</param>
    /// <param name="contourCount">The number of contours.</param>
    /// <param name="unroundedX">The x coordinates in 26.6 font units, without the deltas' rounding to whole units; written.</param>
    /// <param name="unroundedY">The y coordinates in 26.6 font units; written.</param>
    /// <param name="reachedTheEnd">Whether the deltas were applied (the caller then takes the phantom points from the points); false when the glyph has no variation data.</param>
    /// <returns>An error code of <see cref="TtVarError"/>: the glyph cannot be loaded.</returns>
    public int ApplyGlyphDeltas(int glyphIndex, int pointCount, Span<int> x, Span<int> y, ReadOnlySpan<ushort> contours, int contourCount,
        Span<int> unroundedX, Span<int> unroundedY, out bool reachedTheEnd)
    {
        reachedTheEnd = false;
        int nPoints = pointCount + 4;

        for (int i = 0; i < nPoints; i++)
        {
            unroundedX[i] = unchecked(x[i] * 64);
            unroundedY[i] = unchecked(y[i] * 64);
        }

        if (!DoBlend)
            return TtVarError.Ok;

        TtVarTables.GvarData gvar = _tables.Gvar;

        if (gvar.GlyphOffsets is null || (uint)glyphIndex >= (uint)gvar.GlyphCount || gvar.GlyphOffsets[glyphIndex] == gvar.GlyphOffsets[glyphIndex + 1])
        {
            // no variation data for this glyph
            return TtVarError.Ok;
        }

        uint dataSize = unchecked(gvar.GlyphOffsets[glyphIndex + 1] - gvar.GlyphOffsets[glyphIndex]);

        var stream = new FtMemStream(_data);
        if (!stream.Seek(gvar.GlyphOffsets[glyphIndex]) || !stream.EnterFrame(dataSize))
            return TtVarError.InvalidStreamOperation;

        int numAxis = NumAxis;

        // the scratch of this load: the summed deltas and the points in 16.16, before and after a tuple's deltas; the deltas and the lists of points that are read
        int deltaRoom = Math.Max(nPoints, FtMemStream.MaxPackedPoints);
        int[] pool = ArrayPool<int>.Shared.Rent(6 * nPoints + 3 * numAxis + 2 * deltaRoom);
        bool[] hasDelta = ArrayPool<bool>.Shared.Rent(nPoints);
        ushort[] sharedBuffer = ArrayPool<ushort>.Shared.Rent(FtMemStream.MaxPackedPoints);
        ushort[] pointBuffer = ArrayPool<ushort>.Shared.Rent(FtMemStream.MaxPackedPoints);

        try
        {
            uint glyphStart = (uint)stream.FrameCursor;

            // each set of glyph variation data is formatted similarly to `cvar'
            int tupleCount = stream.GetUShort();
            uint offsetToData = (uint)stream.GetUShort();

            // rough sanity test
            if (offsetToData > dataSize || (uint)((tupleCount & TupleCountMask) * 4) > dataSize)
                return TtVarError.InvalidTable;

            offsetToData += glyphStart;

            PointsKind sharedKind = PointsKind.Failed;
            int spointCount = 0;

            if ((tupleCount & TuplesSharePointNumbers) != 0)
            {
                long here = stream.FrameCursor;

                stream.FrameSeekSet(offsetToData);
                sharedKind = stream.ReadPackedPoints(sharedBuffer, out spointCount);
                offsetToData = (uint)stream.FrameCursor;

                stream.FrameSeekSet((uint)here);
            }

            Span<int> pointDeltasX = pool.AsSpan(0, nPoints);
            Span<int> pointDeltasY = pool.AsSpan(nPoints, nPoints);
            Span<int> orgX = pool.AsSpan(2 * nPoints, nPoints);
            Span<int> orgY = pool.AsSpan(3 * nPoints, nPoints);
            Span<int> outX = pool.AsSpan(4 * nPoints, nPoints);
            Span<int> outY = pool.AsSpan(5 * nPoints, nPoints);
            Span<int> peakCoords = pool.AsSpan(6 * nPoints, numAxis);
            Span<int> imStartCoords = pool.AsSpan(6 * nPoints + numAxis, numAxis);
            Span<int> imEndCoords = pool.AsSpan(6 * nPoints + 2 * numAxis, numAxis);
            Span<int> deltasXBuffer = pool.AsSpan(6 * nPoints + 3 * numAxis, deltaRoom);
            Span<int> deltasYBuffer = pool.AsSpan(6 * nPoints + 3 * numAxis + deltaRoom, deltaRoom);

            pointDeltasX.Clear();
            pointDeltasY.Clear();
            peakCoords.Clear();
            imStartCoords.Clear();
            imEndCoords.Clear();

            for (int j = 0; j < nPoints; j++)
            {
                orgX[j] = unchecked((int)((uint)x[j] << 16));
                orgY[j] = unchecked((int)((uint)y[j] << 16));
            }

            long p = stream.FrameCursor;
            long work = 0;

            tupleCount &= TupleCountMask;
            for (int i = 0; i < tupleCount; i++)
            {
                // Enter frame for four bytes.
                if (4 > stream.FrameLimit - p)
                    return TtVarError.InvalidTable;

                int tupleDataSize = stream.UShortAt(p);
                int tupleIndex = stream.UShortAt(p + 2);
                p += 4;

                ReadOnlySpan<int> tupleCoords;

                if ((tupleIndex & EmbeddedTupleCoord) != 0)
                {
                    if (2L * numAxis > stream.FrameLimit - p)
                        return TtVarError.InvalidTable;

                    for (int j = 0; j < numAxis; j++)
                    {
                        peakCoords[j] = (short)stream.UShortAt(p) << 2;
                        p += 2;
                    }

                    tupleCoords = peakCoords;
                }
                else if ((tupleIndex & TupleIndexMask) < gvar.TupleCount)
                {
                    tupleCoords = gvar.TupleCoords.AsSpan((tupleIndex & TupleIndexMask) * numAxis, numAxis);
                }
                else
                {
                    return TtVarError.InvalidTable;
                }

                if ((tupleIndex & IntermediateTuple) != 0)
                {
                    if (4L * numAxis > stream.FrameLimit - p)
                        return TtVarError.InvalidTable;

                    for (int j = 0; j < numAxis; j++)
                    {
                        imStartCoords[j] = (short)stream.UShortAt(p) << 2;
                        p += 2;
                    }

                    for (int j = 0; j < numAxis; j++)
                    {
                        imEndCoords[j] = (short)stream.UShortAt(p) << 2;
                        p += 2;
                    }
                }

                // a font of 16,382 axes has 4,095 tuples of them: weighing a tuple against the location is work as well
                work += numAxis;
                if (work > MaxDeltaWork)
                    return TtVarError.WorkLimit;

                int apply = ApplyTuple(tupleIndex, tupleCoords, imStartCoords, imEndCoords);

                if (apply == 0) // tuple isn't active for our blend
                {
                    offsetToData += (uint)tupleDataSize;
                    continue;
                }

                long savedCursor = stream.FrameCursor;

                stream.FrameSeekSet(offsetToData);

                PointsKind kind;
                ushort[] points;
                int pointNumbers;

                if ((tupleIndex & PrivatePointNumbers) != 0)
                {
                    kind = stream.ReadPackedPoints(pointBuffer, out pointNumbers);
                    points = pointBuffer;
                }
                else
                {
                    kind = sharedKind;
                    points = sharedBuffer;
                    pointNumbers = spointCount;
                }

                // a table can make every one of 4,095 tuples read the same data over and over: the work of one glyph is bounded
                int deltaCount = pointNumbers == 0 ? nPoints : pointNumbers;
                work += nPoints + deltaCount;
                if (work > MaxDeltaWork)
                    return TtVarError.WorkLimit;

                bool readX = stream.ReadPackedDeltas(deltasXBuffer, deltaCount);
                bool readY = stream.ReadPackedDeltas(deltasYBuffer, deltaCount);

                if (kind == PointsKind.Failed || !readY || !readX)
                {
                    // failure, ignore it
                }
                else if (kind == PointsKind.All)
                {
                    // this means that there are deltas for every point in the glyph
                    for (int j = 0; j < nPoints; j++)
                    {
                        int pointDeltaX = FtCalc.MulFix(deltasXBuffer[j], apply);
                        int pointDeltaY = FtCalc.MulFix(deltasYBuffer[j], apply);

                        pointDeltasX[j] = unchecked(pointDeltasX[j] + pointDeltaX);
                        pointDeltasY[j] = unchecked(pointDeltasY[j] + pointDeltaY);
                    }
                }
                else
                {
                    // we have to interpolate the missing deltas similar to the IUP bytecode instruction
                    for (int j = 0; j < nPoints; j++)
                    {
                        hasDelta[j] = false;
                        outX[j] = orgX[j];
                        outY[j] = orgY[j];
                    }

                    for (int j = 0; j < pointNumbers; j++)
                    {
                        ushort idx = points[j];

                        if (idx >= nPoints)
                            continue;

                        hasDelta[idx] = true;

                        outX[idx] = unchecked(outX[idx] + FtCalc.MulFix(deltasXBuffer[j], apply));
                        outY[idx] = unchecked(outY[idx] + FtCalc.MulFix(deltasYBuffer[j], apply));
                    }

                    // no need to handle phantom points here, since solitary points can't be interpolated
                    InterpolateDeltas(contours, contourCount, outX, outY, orgX, orgY, hasDelta);

                    for (int j = 0; j < nPoints; j++)
                    {
                        pointDeltasX[j] = unchecked(pointDeltasX[j] + (outX[j] - orgX[j]));
                        pointDeltasY[j] = unchecked(pointDeltasY[j] + (outY[j] - orgY[j]));
                    }
                }

                offsetToData += (uint)tupleDataSize;

                stream.FrameSeekSet((uint)savedCursor);
            }

            // To avoid double adjustment of advance width or height, do not move phantom points if there is HVAR or VVAR support, respectively.
            if (HAdvanceSupport)
            {
                pointDeltasX[nPoints - 4] = 0;
                pointDeltasY[nPoints - 4] = 0;
                pointDeltasX[nPoints - 3] = 0;
                pointDeltasY[nPoints - 3] = 0;
            }

            if (VAdvanceSupport)
            {
                pointDeltasX[nPoints - 2] = 0;
                pointDeltasY[nPoints - 2] = 0;
                pointDeltasX[nPoints - 1] = 0;
                pointDeltasY[nPoints - 1] = 0;
            }

            for (int i = 0; i < nPoints; i++)
            {
                unroundedX[i] = unchecked(unroundedX[i] + FixedToFdot6(pointDeltasX[i]));
                unroundedY[i] = unchecked(unroundedY[i] + FixedToFdot6(pointDeltasY[i]));

                x[i] = unchecked(x[i] + FixedToInt(pointDeltasX[i]));
                y[i] = unchecked(y[i] + FixedToInt(pointDeltasY[i]));
            }

            reachedTheEnd = true;
            return TtVarError.Ok;
        }
        finally
        {
            stream.ExitFrame();
            ArrayPool<int>.Shared.Return(pool);
            ArrayPool<bool>.Shared.Return(hasDelta);
            ArrayPool<ushort>.Shared.Return(sharedBuffer);
            ArrayPool<ushort>.Shared.Return(pointBuffer);
        }
    }
}
