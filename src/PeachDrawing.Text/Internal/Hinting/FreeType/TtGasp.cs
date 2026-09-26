/****************************************************************************
 *
 * ftgasp.c
 *
 *   Access of TrueType's `gasp' table (body).
 *
 * Copyright (C) 2007-2026 by
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
 * ftgasp.h
 *
 *   Access of TrueType's 'gasp' table (specification).
 *
 * Copyright (C) 2007-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ftgasp.c, ttload.c, ftgasp.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using PeachDrawing.Text.Internal.Fonts.OpenType;
using System;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The <c>gasp</c> table of a font: for each range of sizes, whether the font wants grid-fitting and anti-aliasing there
/// (<c>tt_face_load_gasp</c> and <c>FT_Get_Gasp</c>). Immutable, so one instance serves any number of threads.
/// </summary>
/// <remarks>
/// FreeType loads the table and answers the query, and leaves it to its caller what to do with the answer. The hinting engine applies
/// <see cref="GridFit"/>: a size at which the font does not ask for grid-fitting is not hinted (see <see cref="AllowsGridFit"/>).
/// </remarks>
internal sealed class TtGasp
{
    /// <summary>There is no usable table, or no range of it reaches the size (<c>FT_GASP_NO_TABLE</c>): it is up to the caller what to do.</summary>
    public const int NoTable = -1;

    /// <summary>Grid-fitting and hinting should be performed (<c>FT_GASP_DO_GRIDFIT</c>); really TrueType bytecode interpretation, and, if not set, no hinting.</summary>
    public const int GridFit = 0x01;

    /// <summary>Anti-aliased rendering should be performed (<c>FT_GASP_DO_GRAY</c>).</summary>
    public const int Gray = 0x02;

    /// <summary>Grid-fitting must be used with ClearType's symmetric smoothing (<c>FT_GASP_SYMMETRIC_GRIDFIT</c>).</summary>
    public const int SymmetricGridFit = 0x04;

    /// <summary>Smoothing along multiple axes must be used with ClearType (<c>FT_GASP_SYMMETRIC_SMOOTHING</c>).</summary>
    public const int SymmetricSmoothing = 0x08;

    private readonly int _version;
    private readonly ushort[] _maxPpem;
    private readonly ushort[] _flags;

    private TtGasp(int version, ushort[] maxPpem, ushort[] flags)
    {
        _version = version;
        _maxPpem = maxPpem;
        _flags = flags;
    }

    /// <summary>The number of ranges of the table.</summary>
    public int RangeCount => _maxPpem.Length;

    /// <summary>
    /// Reads the <c>gasp</c> table of a font, or returns null for a font that has none (or none FreeType would use: a version above 1, or a
    /// table too short for the ranges it declares).
    /// </summary>
    public static TtGasp? TryRead(OpenTypeFontface font)
    {
        if (!font.TableDictionary.TryGetValue("gasp", out var entry))
            return null;

        byte[] data = font.FontSource.Bytes;
        if (entry.Offset < 0 || entry.Length < 4 || (long)entry.Offset + entry.Length > data.Length)
            return null;

        ReadOnlySpan<byte> table = data.AsSpan(entry.Offset, entry.Length);
        int version = BinaryPrimitives.ReadUInt16BigEndian(table);
        int count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);

        // only support versions 0 and 1 of the table
        if (version >= 2)
            return null;

        // FreeType reads the ranges past the end of the table when the font file goes on there; the ranges have to be inside the table here
        if (4 + count * 4L > table.Length)
            return null;

        var maxPpem = new ushort[count];
        var flags = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            maxPpem[i] = BinaryPrimitives.ReadUInt16BigEndian(table[(4 + 4 * i)..]);
            flags[i] = BinaryPrimitives.ReadUInt16BigEndian(table[(6 + 4 * i)..]);
        }

        return new TtGasp(version, maxPpem, flags);
    }

    /// <summary>
    /// The flags of the first range whose largest size is at least <paramref name="ppem"/> (<c>FT_Get_Gasp</c>), with the bits a version 0 table
    /// does not define cleared, or <see cref="NoTable"/> when the table has no ranges or none reaches the size.
    /// </summary>
    /// <param name="ppem">The vertical size in whole pixels per em.</param>
    public int GetFlags(int ppem)
    {
        for (int i = 0; i < _maxPpem.Length; i++)
        {
            if (ppem <= _maxPpem[i])
            {
                int result = _flags[i];

                // ensure that we don't have spurious bits
                if (_version == 0)
                    result &= 3;

                return result;
            }
        }

        return NoTable;
    }

    /// <summary>
    /// Whether the font asks for grid-fitting at a size: the range that covers it has <see cref="GridFit"/> set. Where the table says nothing
    /// about the size (<see cref="NoTable"/>) it is up to the caller, and nothing is held back.
    /// </summary>
    /// <param name="ppem">The vertical size in whole pixels per em.</param>
    public bool AllowsGridFit(int ppem)
    {
        int flags = GetFlags(ppem);
        return flags == NoTable || (flags & GridFit) != 0;
    }
}
