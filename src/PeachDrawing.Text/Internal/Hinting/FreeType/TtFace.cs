/****************************************************************************
 *
 * ttobjs.c
 *
 *   Objects manager (body).
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
 * ttpload.c
 *
 *   TrueType-specific tables loader (body).
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
 * ttload.c
 *
 *   Load the basic TrueType tables, i.e., tables that can be either in
 *   TTF or OTF fonts (body).
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
 * ttmtx.c
 *
 *   Load the metrics tables common to TTF and OTF fonts (body).
 *
 * Copyright (C) 2006-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttobjs.c, ttpload.c, ttload.c, ttmtx.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachDrawing.Text.Internal.Fonts.OpenType.Variations;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// What FreeType's <c>TT_Face</c> holds of a TrueType font for hinting: the font's tables as the loader and the
/// interpreter read them (<c>tt_face_load_maxp</c>, <c>tt_face_load_loca</c>, <c>tt_face_load_cvt</c>, <c>fpgm</c>, <c>prep</c>,
/// <c>hdmx</c>, the metrics tables), read once from the font's bytes without going through any shared cursor.
/// </summary>
/// <remarks>Immutable after construction, so one instance serves any number of threads.</remarks>
internal sealed class TtFace
{
    /// <summary>The bytes of the font file (an sfnt: WOFF and WOFF2 fonts are converted before they get here).</summary>
    public byte[] Data { get; }

    public int UnitsPerEm { get; }

    /// <summary>The flags of the <c>head</c> table; bit 3 asks for integer ppem scaling.</summary>
    public int HeadFlags { get; }

    /// <summary>The number of glyphs (<c>maxp</c>, adjusted by the length of <c>loca</c>).</summary>
    public int NumGlyphs { get; private set; }

    // The maximum profile (`tt_face_load_maxp') the interpreter needs.
    public int MaxTwilightPoints { get; }
    public int MaxStorage { get; }
    public int MaxFunctionDefs { get; }
    public int MaxInstructionDefs { get; }
    public int MaxStackElements { get; }

    /// <summary>The size of the largest glyph program the font declares (<c>maxSizeOfInstructions</c>).</summary>
    public int MaxSizeOfInstructions { get; }

    /// <summary>
    /// Whether the font has any TrueType instructions to run: a font program, a CVT program, or glyph programs (which a font must
    /// declare the size of). A font without them is only scaled by the loader, and is not hinted at all.
    /// </summary>
    public bool HasInstructions => FontProgram.Length > 0 || CvtProgram.Length > 0 || MaxSizeOfInstructions > 0;

    /// <summary>The control values in 26.6 (the values of the <c>cvt</c> table, times 64).</summary>
    public int[] Cvt { get; }

    /// <summary>The font program (<c>fpgm</c>), empty when the font has none.</summary>
    public byte[] FontProgram { get; }

    /// <summary>The CVT program (<c>prep</c>), empty when the font has none.</summary>
    public byte[] CvtProgram { get; }

    /// <summary>The offset of the <c>glyf</c> table in the font bytes, and its length.</summary>
    public int GlyfOffset { get; }
    public int GlyfLength { get; }

    private readonly int _locaOffset;
    private readonly bool _longLoca;

    /// <summary>The number of entries of the location table (<c>num_locations</c>).</summary>
    public int NumLocations { get; }

    private readonly int _hmtxOffset;
    private readonly int _hmtxSize;
    private readonly int _numHMetrics;

    private readonly int _vmtxOffset;
    private readonly int _vmtxSize;
    private readonly int _numVMetrics;

    /// <summary>Whether the font has both <c>vhea</c> and <c>vmtx</c> (<c>face->vertical_info</c>).</summary>
    public bool VerticalInfo { get; }

    /// <summary>The version of the OS/2 table, 0xFFFF when there is none.</summary>
    public int Os2Version { get; }

    public int TypoAscender { get; }
    public int TypoDescender { get; }
    public int HheaAscender { get; }
    public int HheaDescender { get; }

    /// <summary>Whether the <c>post</c> table says the font is fixed pitch.</summary>
    public bool IsFixedPitch { get; }

    /// <summary>The offsets of the records of the <c>hdmx</c> table, sorted by ppem.</summary>
    private readonly int[] _hdmxRecords;

    /// <summary>Whether the font is one of the "tricky" fonts (<c>FT_FACE_FLAG_TRICKY</c>) whose bytecode must run without backward compatibility.</summary>
    public bool IsTricky { get; }

    /// <summary>The variation tables of a variable font, and the location a variation instance reads them at (null for the default instance).</summary>
    public GvarTable? Gvar { get; }

    /// <summary>The normalized coordinates of the instance, in the range -1 to 1; null for a font that is not an instance.</summary>
    public double[]? Normalized { get; }

    /// <summary>The advance of a glyph in font units at the instance's location, when that differs from the <c>hmtx</c> one (an <c>HVAR</c> table).</summary>
    public Func<int, int>? InstanceAdvance { get; }

    private TtFace(OpenTypeFontface font, string? familyName, VariationCoordinates? variation, Func<int, int>? instanceAdvance)
    {
        Data = font.FontSource.Bytes;
        var tables = font.TableDictionary;

        // head
        ReadOnlySpan<byte> head = Table(tables, "head", 54);
        HeadFlags = BinaryPrimitives.ReadUInt16BigEndian(head[16..]);
        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head[18..]);
        _longLoca = BinaryPrimitives.ReadInt16BigEndian(head[50..]) != 0;

        if (UnitsPerEm == 0)
            throw new HintingException("The font's units per em is zero.");

        // maxp
        ReadOnlySpan<byte> maxp = Table(tables, "maxp", 6);
        NumGlyphs = BinaryPrimitives.ReadUInt16BigEndian(maxp[4..]);
        uint maxpVersion = BinaryPrimitives.ReadUInt32BigEndian(maxp);

        if (maxpVersion >= 0x10000 && maxp.Length >= 32) // 6 bytes of header and 26 of profile
        {
            MaxTwilightPoints = BinaryPrimitives.ReadUInt16BigEndian(maxp[16..]);
            MaxStorage = BinaryPrimitives.ReadUInt16BigEndian(maxp[18..]);
            MaxFunctionDefs = BinaryPrimitives.ReadUInt16BigEndian(maxp[20..]);
            MaxInstructionDefs = BinaryPrimitives.ReadUInt16BigEndian(maxp[22..]);
            MaxStackElements = BinaryPrimitives.ReadUInt16BigEndian(maxp[24..]);
            MaxSizeOfInstructions = BinaryPrimitives.ReadUInt16BigEndian(maxp[26..]);

            // XXX: an adjustment that is necessary to load certain broken fonts like `Keystrokes MT' :-(
            //
            //   We allocate 64 function entries by default when the maxFunctionDefs value is smaller.
            if (MaxFunctionDefs < 64)
                MaxFunctionDefs = 64;

            // we add 4 phantom points later
            if (MaxTwilightPoints > 0xFFFF - 4)
                MaxTwilightPoints = 0xFFFF - 4;
        }
        else if (maxpVersion >= 0x10000)
        {
            throw new HintingException("The maxp table is truncated.");
        }

        // glyf and loca
        if (tables.TryGetValue("glyf", out var glyf) && IsInside(glyf))
        {
            GlyfOffset = glyf.Offset;
            GlyfLength = glyf.Length;
        }

        TableDirectoryEntry loca = tables.TryGetValue("loca", out var locaEntry) && IsInside(locaEntry)
            ? locaEntry
            : throw new HintingException("The font has no loca table.");

        int shift = _longLoca ? 2 : 1;
        long tableLength = loca.Length;

        if (tableLength > 0x10000L << shift)
            tableLength = 0x10000L << shift;

        NumLocations = (int)(tableLength >> shift);
        _locaOffset = loca.Offset;

        if (NumLocations != NumGlyphs + 1)
        {
            // we only handle the case where `maxp' gives a larger value
            if (NumLocations < NumGlyphs + 1)
            {
                long newLocaLength = (long)(NumGlyphs + 1) << shift;

                long pos = loca.Offset;
                long dist = 0x7FFFFFFFL;
                bool found = false;

                // compute the distance to next table in font file
                foreach (TableDirectoryEntry entry in tables.Values)
                {
                    long diff = entry.Offset - pos;

                    if (diff > 0 && diff < dist)
                    {
                        dist = diff;
                        found = true;
                    }
                }

                if (!found)
                {
                    // `loca' is the last table
                    dist = Data.Length - pos;
                }

                if (newLocaLength <= dist)
                {
                    NumLocations = NumGlyphs + 1;
                    tableLength = newLocaLength;
                }
                else
                {
                    NumGlyphs = NumLocations != 0 ? NumLocations - 1 : 0;
                }
            }
        }

        if (_locaOffset + tableLength > Data.Length)
            throw new HintingException("The loca table reaches past the end of the font.");

        // cvt (optional): `*cur = FT_GET_SHORT() * 64'
        if (tables.TryGetValue("cvt ", out var cvt) && IsInside(cvt))
        {
            int count = cvt.Length / 2;
            Cvt = new int[count];
            for (int i = 0; i < count; i++)
                Cvt[i] = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(cvt.Offset + 2 * i)) * 64;
        }
        else
        {
            Cvt = [];
        }

        // fpgm and prep are optional
        FontProgram = tables.TryGetValue("fpgm", out var fpgm) && IsInside(fpgm) ? Data.AsSpan(fpgm.Offset, fpgm.Length).ToArray() : [];
        CvtProgram = tables.TryGetValue("prep", out var prep) && IsInside(prep) ? Data.AsSpan(prep.Offset, prep.Length).ToArray() : [];

        // hhea and hmtx (required)
        ReadOnlySpan<byte> hhea = Table(tables, "hhea", 36);
        HheaAscender = BinaryPrimitives.ReadInt16BigEndian(hhea[4..]);
        HheaDescender = BinaryPrimitives.ReadInt16BigEndian(hhea[6..]);
        _numHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea[34..]);

        TableDirectoryEntry hmtx = tables.TryGetValue("hmtx", out var hmtxEntry) && IsInside(hmtxEntry)
            ? hmtxEntry
            : throw new HintingException("The font has no hmtx table.");
        _hmtxOffset = hmtx.Offset;
        _hmtxSize = hmtx.Length;

        // vhea and vmtx (optional)
        if (tables.TryGetValue("vhea", out var vheaEntry) && IsInside(vheaEntry) && vheaEntry.Length >= 36 &&
            tables.TryGetValue("vmtx", out var vmtxEntry) && IsInside(vmtxEntry))
        {
            _numVMetrics = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(vheaEntry.Offset + 34));
            _vmtxOffset = vmtxEntry.Offset;
            _vmtxSize = vmtxEntry.Length;
            VerticalInfo = true;
        }

        // OS/2: we support fonts where the table doesn't exist by setting the version to 0xFFFF
        Os2Version = 0xFFFF;
        if (tables.TryGetValue("OS/2", out var os2) && IsInside(os2) && os2.Length >= 78)
        {
            Os2Version = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(os2.Offset));
            TypoAscender = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(os2.Offset + 68));
            TypoDescender = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(os2.Offset + 70));
        }

        // post
        if (tables.TryGetValue("post", out var post) && IsInside(post) && post.Length >= 16)
            IsFixedPitch = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(post.Offset + 12)) != 0;

        _hdmxRecords = LoadHdmx(tables);

        IsTricky = CheckTrickiness(tables, familyName);

        if (variation is { IsDefault: false } && font.Variations?.Gvar is { } gvar)
        {
            Gvar = gvar;
            Normalized = variation.Normalized;
            InstanceAdvance = instanceAdvance;
        }
    }

    /// <summary>
    /// Reads the tables of a font for hinting. Returns null when the font is not one this port can hint: it has no
    /// TrueType outlines, or a table it needs is missing.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="familyName">The family name, which the trickiness check reads.</param>
    /// <param name="variation">The location of a variable font instance, or null.</param>
    /// <param name="instanceAdvance">The advance in font units of a glyph at that location, or null.</param>
    public static TtFace? TryCreate(OpenTypeFontface font, string? familyName, VariationCoordinates? variation, Func<int, int>? instanceAdvance)
    {
        var tables = font.TableDictionary;
        if (!tables.ContainsKey("glyf") || !tables.ContainsKey("loca") || !tables.ContainsKey("head") ||
            !tables.ContainsKey("maxp") || !tables.ContainsKey("hhea") || !tables.ContainsKey("hmtx"))
            return null;

        try
        {
            return new TtFace(font, familyName, variation, instanceAdvance);
        }
        catch (Exception ex) when (ex is HintingException or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    private bool IsInside(TableDirectoryEntry entry) =>
        entry.Offset >= 0 && entry.Length >= 0 && (long)entry.Offset + entry.Length <= Data.Length;

    private ReadOnlySpan<byte> Table(IDictionary<string, TableDirectoryEntry> tables, string tag, int minimumLength)
    {
        if (!tables.TryGetValue(tag, out var entry) || !IsInside(entry) || entry.Length < minimumLength)
            throw new HintingException("The font has no usable " + tag + " table.");

        return Data.AsSpan(entry.Offset, entry.Length);
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                         glyph locations (tt_face_get_location)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds where a glyph's data starts in the <c>glyf</c> table and how long it is (<c>tt_face_get_location</c>), with the
    /// same repairs of broken <c>loca</c> data.
    /// </summary>
    public int GetLocation(int gindex, out int size)
    {
        uint pos1 = 0;
        uint pos2 = 0;

        if ((uint)gindex < (uint)NumLocations)
        {
            if (_longLoca)
            {
                int p = _locaOffset + gindex * 4;
                int pLimit = _locaOffset + NumLocations * 4;

                pos1 = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(p));
                pos2 = pos1;

                if (p + 4 + 4 <= pLimit)
                    pos2 = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(p + 4));
            }
            else
            {
                int p = _locaOffset + gindex * 2;
                int pLimit = _locaOffset + NumLocations * 2;

                pos1 = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(p));
                pos2 = pos1;

                if (p + 2 + 2 <= pLimit)
                    pos2 = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(p + 2));

                pos1 <<= 1;
                pos2 <<= 1;
            }
        }

        // Check broken location data.
        if (pos1 > (uint)GlyfLength)
        {
            size = 0;
            return 0;
        }

        if (pos2 > (uint)GlyfLength)
        {
            // We try to sanitize the last `loca' entry.
            if (gindex == NumLocations - 2)
            {
                pos2 = (uint)GlyfLength;
            }
            else
            {
                size = 0;
                return 0;
            }
        }

        // The `loca' table must be ordered; it refers to the length of an entry as the difference between the current and
        // the next position. However, there do exist (malformed) fonts which don't obey this rule, so we are only able to
        // provide an upper bound for the size.
        //
        // We get (intentionally) a wrong, non-zero result in case the `glyf' table is missing.
        if (pos2 >= pos1)
            size = (int)(pos2 - pos1);
        else
            size = (int)((uint)GlyfLength - pos1);

        return (int)pos1;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                              metrics (tt_face_get_metrics)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The left side bearing and the advance width of a glyph in font units (<c>TT_Get_HMetrics</c>).</summary>
    public void GetHMetrics(int gindex, out int lsb, out int advance)
    {
        GetMetrics(_hmtxOffset, _hmtxSize, _numHMetrics, gindex, out lsb, out advance);

        if (InstanceAdvance is { } adjust)
            advance = adjust(gindex);
    }

    /// <summary>The top side bearing and the advance height of a glyph in font units, computed when the font has no vertical metrics (<c>TT_Get_VMetrics</c>).</summary>
    public void GetVMetrics(int gindex, int yMax, out int tsb, out int advanceHeight)
    {
        if (VerticalInfo)
        {
            GetMetrics(_vmtxOffset, _vmtxSize, _numVMetrics, gindex, out tsb, out advanceHeight);
        }
        else if (Os2Version != 0xFFFF)
        {
            tsb = (short)(TypoAscender - yMax);
            advanceHeight = (ushort)Math.Abs(TypoAscender - TypoDescender);
        }
        else
        {
            tsb = (short)(HheaAscender - yMax);
            advanceHeight = (ushort)Math.Abs(HheaAscender - HheaDescender);
        }
    }

    private void GetMetrics(int tableOffset, int tableSize, int k, int gindex, out int bearing, out int advance)
    {
        long tablePos = tableOffset;
        long tableEnd = tablePos + tableSize;

        bearing = 0;
        advance = 0;

        if (k > 0)
        {
            if (gindex < k)
            {
                tablePos += 4L * gindex;
                if (tablePos + 4 > tableEnd)
                    return;

                advance = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan((int)tablePos));
                bearing = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan((int)tablePos + 2));
            }
            else
            {
                tablePos += 4L * (k - 1);
                if (tablePos + 2 > tableEnd)
                    return;

                advance = BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan((int)tablePos));

                tablePos += 4 + 2L * (gindex - k);
                if (tablePos + 2 <= tableEnd)
                    bearing = BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan((int)tablePos));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                     hdmx
    // ---------------------------------------------------------------------------------------------------------------

    private int[] LoadHdmx(IDictionary<string, TableDirectoryEntry> tables)
    {
        // this table is optional
        if (!tables.TryGetValue("hdmx", out var entry) || !IsInside(entry) || entry.Length < 8)
            return [];

        ReadOnlySpan<byte> table = Data.AsSpan(entry.Offset, entry.Length);

        // Given that `hdmx' tables are losing its importance (for example, variation fonts introduced in OpenType 1.8
        // must not have this table) we no longer test for a correct `version' field.
        int numRecords = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        uint recordSize = BinaryPrimitives.ReadUInt32BigEndian(table[4..]);

        // There are at least two fonts, HANNOM-A and HANNOM-B version 2.0 (2005), which get this wrong: The upper two bytes
        // of the size value are set to 0xFF instead of 0x00. We catch and fix this.
        if (recordSize >= 0xFFFF0000U)
            recordSize &= 0xFFFFU;

        // The limit for `num_records' is a heuristic value.
        if (numRecords > 255 || numRecords == 0)
            return [];

        // Out-of-spec tables are rejected. The record size must be equal to the number of glyphs + 2 + 32-bit padding.
        if ((int)recordSize != ((NumGlyphs + 2 + 3) & ~3))
            return [];

        var records = new List<int>(numRecords);
        int p = 8;
        for (int nn = 0; nn < numRecords; nn++)
        {
            if (p + (long)recordSize > table.Length)
                break;

            records.Add(entry.Offset + p);
            p += (int)recordSize;
        }

        // The records must be already sorted by ppem but it does not hurt to make sure so that the binary search works later.
        records.Sort((x, y) => Data[x] - Data[y]);
        return records.ToArray();
    }

    /// <summary>
    /// The offset of the advance width table for a pixel size if the <c>hdmx</c> table has one, or -1
    /// (<c>tt_face_get_device_metrics</c>). The records must be sorted for the binary search to work properly.
    /// </summary>
    public int GetDeviceMetrics(int ppem)
    {
        int min = 0;
        int max = _hdmxRecords.Length;

        while (min < max)
        {
            int mid = (min + max) >> 1;

            if (Data[_hdmxRecords[mid]] > ppem)
                max = mid;
            else if (Data[_hdmxRecords[mid]] < ppem)
                min = mid + 1;
            else
                return _hdmxRecords[mid] + 2;
        }

        return -1;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                       tricky fonts (tt_check_trickyness)
    // ---------------------------------------------------------------------------------------------------------------

    // Compare the face with a list of well-known `tricky' fonts. This list shall be expanded as we find more of them.
    private static readonly string[] TrickNames =
    [
        "cpop", "DFGirl-W6-WIN-BF", "DFGothic-EB", "DFGyoSho-Lt", "DFHei", "DFHSGothic-W5", "DFHSMincho-W3", "DFHSMincho-W7",
        "DFKaiSho-SB", "DFKaiShu", "DFKai-SB", "DFMing", "DLC", "HuaTianKaiTi?", "HuaTianSongTi?", "Ming(for ISO10646)",
        "MingLiU", "MingMedium", "PMingLiU", "MingLi43",
    ];

    // The cvt, fpgm and prep tables of the tricky fonts: a checksum and a length for each.
    private static readonly (uint CvtSum, uint CvtLength, uint FpgmSum, uint FpgmLength, uint PrepSum, uint PrepLength)[] SfntIds =
    [
        // MingLiU 1995
        (0x05BCF058, 0x000002E4, 0x28233BF1, 0x000087C4, 0xA344A1EA, 0x000001E1),
        // MingLiU 1996-
        (0x05BCF058, 0x000002E4, 0x28233BF1, 0x000087C4, 0xA344A1EB, 0x000001E1),
        // DFGothic-EB
        (0x12C3EBB2, 0x00000350, 0xB680EE64, 0x000087A7, 0xCE939563, 0x00000758),
        // DFGyoSho-Lt
        (0x11E5EAD4, 0x00000350, 0xCE5956E9, 0x0000BC85, 0x8272F416, 0x00000045),
        // DFHei-Md-HK-BF
        (0x1257EB46, 0x00000350, 0xF699D160, 0x0000715F, 0xD222F568, 0x000003BC),
        // DFHSGothic-W5
        (0x1262EB4E, 0x00000350, 0xE86A5D64, 0x00007940, 0x7850F729, 0x000005FF),
        // DFHSMincho-W3
        (0x122DEB0A, 0x00000350, 0x3D16328A, 0x0000859B, 0xA93FC33B, 0x000002CB),
        // DFHSMincho-W7
        (0x125FEB26, 0x00000350, 0xA5ACC982, 0x00007EE1, 0x90999196, 0x0000041F),
        // DFKaiShu
        (0x11E5EAD4, 0x00000350, 0x5A30CA3B, 0x00009063, 0x13A42602, 0x0000007E),
        // DFKaiShu, variant
        (0x11E5EAD4, 0x00000350, 0xA6E78C01, 0x00008998, 0x13A42602, 0x0000007E),
        // DFKaiShu-Md-HK-BF
        (0x11E5EAD4, 0x00000360, 0x9DB282B2, 0x0000C06E, 0x53E6D7CA, 0x00000082),
        // DFMing-Bd-HK-BF
        (0x1243EB18, 0x00000350, 0xBA0A8C30, 0x000074AD, 0xF3D83409, 0x0000037B),
        // DLCLiShu
        (0x07DCF546, 0x00000308, 0x40FE7C90, 0x00008E2A, 0x608174B5, 0x0000007A),
        // DLCHayBold
        (0xEB891238, 0x00000308, 0xD2E4DCD4, 0x0000676F, 0x8EA5F293, 0x000003B8),
        // HuaTianKaiTi
        (0xFFFBFFFC, 0x00000008, 0x9C9E48B8, 0x0000BEA2, 0x70020112, 0x00000008),
        // HuaTianSongTi
        (0xFFFBFFFC, 0x00000008, 0x0A5A0483, 0x00017C39, 0x70020112, 0x00000008),
        // NEC fadpop7.ttf
        (0x00000000, 0x00000000, 0x40C92555, 0x000000E5, 0xA39B58E3, 0x0000117C),
        // NEC fadrei5.ttf
        (0x00000000, 0x00000000, 0x33C41652, 0x000000E5, 0x26D6C52A, 0x00000F6A),
        // NEC fangot7.ttf
        (0x00000000, 0x00000000, 0x6DB1651D, 0x0000019D, 0x6C6E4B03, 0x00002492),
        // NEC fangyo5.ttf
        (0x00000000, 0x00000000, 0x40C92555, 0x000000E5, 0xDE51FAD0, 0x0000117C),
        // NEC fankyo5.ttf
        (0x00000000, 0x00000000, 0x85E47664, 0x000000E5, 0xA6C62831, 0x00001CAA),
        // NEC fanrgo5.ttf
        (0x00000000, 0x00000000, 0x2D891CFD, 0x0000019D, 0xA0604633, 0x00001DE8),
        // NEC fangot5.ttc
        (0x00000000, 0x00000000, 0x40AA774C, 0x000001CB, 0x9B5CAA96, 0x00001F9A),
        // NEC fanmin3.ttc
        (0x00000000, 0x00000000, 0x0D3DE9CB, 0x00000141, 0xD4127766, 0x00002280),
        // NEC FA-Gothic, 1996
        (0x00000000, 0x00000000, 0x4A692698, 0x000001F0, 0x340D4346, 0x00001FCA),
        // NEC FA-Minchou, 1996
        (0x00000000, 0x00000000, 0xCD34C604, 0x00000166, 0x6CF31046, 0x000022B0),
        // NEC FA-RoundGothicB, 1996
        (0x00000000, 0x00000000, 0x5DA75315, 0x0000019D, 0x40745A5F, 0x000022E0),
        // NEC FA-RoundGothicM, 1996
        (0x00000000, 0x00000000, 0xF055FC48, 0x000001C2, 0x3900DED3, 0x00001E18),
        // MINGLI.TTF, 1992
        (0x00170003, 0x00000060, 0xDBB4306E, 0x000058AA, 0xD643482A, 0x00000035),
        // DFHei-Bd-WIN-HK-BF
        (0x1269EB58, 0x00000350, 0x5CD5957A, 0x00006A4E, 0xF758323A, 0x00000380),
        // DFMing-Md-WIN-HK-BF
        (0x122FEB0B, 0x00000350, 0x7F10919A, 0x000070A9, 0x7CD7E7B7, 0x0000025C),
    ];

    private bool CheckTrickiness(IDictionary<string, TableDirectoryEntry> tables, string? familyName)
    {
        // For first, check the face name for quick check.
        if (familyName is not null && CheckTrickinessFamily(familyName))
            return true;

        // Type42 fonts may lack `name' tables, we thus try to identify tricky fonts by checking the checksums of
        // Type42-persistent sfnt tables (`cvt', `fpgm', and `prep').
        return CheckTrickinessSfntIds(tables);
    }

    // Fonts embedded in PDFs are made unique by prepending randomization prefixes to their names: as defined in Section
    // 5.5.3, 'Font Subsets', of the PDF Reference, they consist of 6 uppercase letters followed by the `+' sign. For
    // safety, we do not skip prefixes violating this rule.
    private static string SkipPdfFontRandomTag(string name)
    {
        if (name.Length > 7 && name[6] == '+')
        {
            for (int i = 0; i < 6; i++)
            {
                if (name[i] is < 'A' or > 'Z')
                    return name;
            }

            return name[7..];
        }

        return name;
    }

    private static bool CheckTrickinessFamily(string name)
    {
        string withoutTag = SkipPdfFontRandomTag(name);

        foreach (string trick in TrickNames)
        {
            if (withoutTag.Contains(trick, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // Some PDF generators clear the checksums in the TrueType header table. For example, Quartz ContextPDF clears all
    // entries, or Bullzip PDF Printer clears the entries for subsetted subtables. We thus have to recalculate the checksums
    // where necessary.
    private uint SynthChecksum(TableDirectoryEntry entry)
    {
        uint checksum = 0;
        ReadOnlySpan<byte> p = Data.AsSpan(entry.Offset, entry.Length);
        int length = p.Length;
        int i = 0;

        for (; length > 3; length -= 4, i += 4)
            checksum = unchecked(checksum + BinaryPrimitives.ReadUInt32BigEndian(p[i..]));

        for (int shift = 24; length > 0; length--, shift -= 8)
            checksum = unchecked(checksum + ((uint)p[i++] << shift));

        return checksum;
    }

    private bool CheckTrickinessSfntIds(IDictionary<string, TableDirectoryEntry> tables)
    {
        var numMatchedIds = new int[SfntIds.Length];
        bool hasCvt = false, hasFpgm = false, hasPrep = false;

        foreach (var (tag, entry) in tables)
        {
            int k;
            switch (tag)
            {
                case "cvt ":
                    k = 0;
                    hasCvt = true;
                    break;

                case "fpgm":
                    k = 1;
                    hasFpgm = true;
                    break;

                case "prep":
                    k = 2;
                    hasPrep = true;
                    break;

                default:
                    continue;
            }

            if (!IsInside(entry))
                continue;

            uint checksum = 0;
            bool haveChecksum = false;

            for (int j = 0; j < SfntIds.Length; j++)
            {
                uint length = k == 0 ? SfntIds[j].CvtLength : k == 1 ? SfntIds[j].FpgmLength : SfntIds[j].PrepLength;
                uint sum = k == 0 ? SfntIds[j].CvtSum : k == 1 ? SfntIds[j].FpgmSum : SfntIds[j].PrepSum;

                if ((uint)entry.Length == length)
                {
                    if (!haveChecksum)
                    {
                        checksum = SynthChecksum(entry);
                        haveChecksum = true;
                    }

                    if (sum == checksum)
                        numMatchedIds[j]++;

                    if (numMatchedIds[j] == 3)
                        return true;
                }
            }
        }

        for (int j = 0; j < SfntIds.Length; j++)
        {
            if (!hasCvt && SfntIds[j].CvtLength == 0)
                numMatchedIds[j]++;
            if (!hasFpgm && SfntIds[j].FpgmLength == 0)
                numMatchedIds[j]++;
            if (!hasPrep && SfntIds[j].PrepLength == 0)
                numMatchedIds[j]++;
            if (numMatchedIds[j] == 3)
                return true;
        }

        return false;
    }
}
