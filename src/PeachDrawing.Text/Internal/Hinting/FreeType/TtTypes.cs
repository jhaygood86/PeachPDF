/****************************************************************************
 *
 * ttobjs.h
 *
 *   Objects manager (specification).
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
 * ttinterp.h
 *
 *   TrueType bytecode interpreter (specification).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttobjs.h, ttinterp.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>Point tag bits of an outline (<c>ftimage.h</c>); the touch bits are reserved for the TrueType hinter.</summary>
internal static class FtTag
{
    public const byte On = 0x01;
    public const byte HasScanMode = 0x04;
    public const byte TouchX = 0x08;
    public const byte TouchY = 0x10;
    public const byte TouchBoth = TouchX | TouchY;
}

/// <summary>The error codes of the interpreter (<c>tterrors.h</c>, <c>fterrors.h</c>); 0 is success.</summary>
internal static class TtError
{
    public const int Ok = 0;
    public const int InvalidGlyphIndex = 0x10;
    public const int InvalidTable = 0x08;
    public const int InvalidOpcode = 0x80;
    public const int TooFewArguments = 0x81;
    public const int StackOverflow = 0x82;
    public const int CodeOverflow = 0x83;
    public const int BadArgument = 0x84;
    public const int DivideByZero = 0x85;
    public const int InvalidReference = 0x86;
    public const int DebugOpCode = 0x87;
    public const int EndfInExecStream = 0x88;
    public const int NestedDefs = 0x89;
    public const int InvalidCodeRange = 0x8A;
    public const int ExecutionTooLong = 0x8B;
    public const int TooManyFunctionDefs = 0x8C;
    public const int TooManyInstructionDefs = 0x8D;
    public const int DefInGlyfBytecode = 0x8E;
    public const int InvalidOutline = 0x14;
    public const int InvalidComposite = 0x15;
    public const int TooManyHints = 0x16;
    public const int InvalidPixelSize = 0x17;
}

/// <summary>Rounding modes of the graphics state (<c>ttinterp.h</c>).</summary>
internal static class TtRound
{
    public const int Off = 5;
    public const int ToHalfGrid = 0;
    public const int ToGrid = 1;
    public const int ToDoubleGrid = 2;
    public const int UpToGrid = 4;
    public const int DownToGrid = 3;
    public const int Super = 6;
    public const int Super45 = 7;
}

/// <summary>The three code ranges that can be active at once (<c>TT_CodeRange_Tag</c>): font program, CVT program, glyph program.</summary>
internal static class TtCodeRange
{
    public const int None = 0;
    public const int Font = 1;
    public const int Cvt = 2;
    public const int Glyph = 3;
}

/// <summary>The TrueType interpreter versions FreeType offers.</summary>
internal enum TtInterpreterVersion
{
    /// <summary>The original interpreter: full grid-fitting, x direction included.</summary>
    V35 = 35,

    /// <summary>The "minimal subpixel hinting" interpreter FreeType uses by default: x-direction moves are ignored.</summary>
    V40 = 40,
}

/// <summary>The render modes that change how bytecode runs (<c>FT_Render_Mode</c>; the other values are not used here).</summary>
internal enum TtRenderMode
{
    Normal = 0,
    Mono = 2,
}

/// <summary>
/// A glyph zone (<c>TT_GlyphZoneRec</c>): the points the interpreter moves. FreeType's <c>FT_Vector</c> arrays are held
/// as parallel x and y arrays so the two axes of <c>IUP</c> can share one code path.
/// </summary>
internal sealed class TtGlyphZone
{
    public int NPoints;
    public int NContours;

    /// <summary>Original (scaled) positions.</summary>
    public int[] OrgX = [], OrgY = [];

    /// <summary>Current positions: what the outline becomes.</summary>
    public int[] CurX = [], CurY = [];

    /// <summary>Original positions in font units, unscaled.</summary>
    public int[] OrusX = [], OrusY = [];

    public byte[] Tags = [];

    /// <summary>The index of the last point of each contour.</summary>
    public ushort[] Contours = [];

    /// <summary>The point number the zone's arrays start at (non-zero for the components of a composite glyph).</summary>
    public int FirstPoint;

    /// <summary>Makes room for <paramref name="points"/> points and <paramref name="contours"/> contours and clears the zone to zero.</summary>
    public void Allocate(int points, int contours)
    {
        if (OrgX.Length < points)
        {
            OrgX = new int[points];
            OrgY = new int[points];
            CurX = new int[points];
            CurY = new int[points];
            OrusX = new int[points];
            OrusY = new int[points];
            Tags = new byte[points];
        }
        else
        {
            Array.Clear(OrgX, 0, points);
            Array.Clear(OrgY, 0, points);
            Array.Clear(CurX, 0, points);
            Array.Clear(CurY, 0, points);
            Array.Clear(OrusX, 0, points);
            Array.Clear(OrusY, 0, points);
            Array.Clear(Tags, 0, points);
        }

        if (Contours.Length < contours)
            Contours = new ushort[contours];
        else
            Array.Clear(Contours, 0, contours);

        NPoints = points;
        NContours = contours;
        FirstPoint = 0;
    }

    /// <summary>
    /// A zone that shares the arrays of another one but has counts of its own, as a copy of a FreeType zone record does: the
    /// interpreter may lower the number of twilight points for a run without touching the size's zone.
    /// </summary>
    public static TtGlyphZone AliasOf(TtGlyphZone other) => new()
    {
        NPoints = other.NPoints,
        NContours = other.NContours,
        OrgX = other.OrgX,
        OrgY = other.OrgY,
        CurX = other.CurX,
        CurY = other.CurY,
        OrusX = other.OrusX,
        OrusY = other.OrusY,
        Tags = other.Tags,
        Contours = other.Contours,
        FirstPoint = other.FirstPoint,
    };

    /// <summary>Copies the contents of another zone into this one, making it an independent copy.</summary>
    public void CopyFrom(TtGlyphZone other)
    {
        Allocate(other.NPoints, other.NContours);
        Array.Copy(other.OrgX, OrgX, other.NPoints);
        Array.Copy(other.OrgY, OrgY, other.NPoints);
        Array.Copy(other.CurX, CurX, other.NPoints);
        Array.Copy(other.CurY, CurY, other.NPoints);
        Array.Copy(other.OrusX, OrusX, other.NPoints);
        Array.Copy(other.OrusY, OrusY, other.NPoints);
        Array.Copy(other.Tags, Tags, other.NPoints);
        Array.Copy(other.Contours, Contours, other.NContours);
        FirstPoint = other.FirstPoint;
    }
}

/// <summary>The TrueType graphics state (<c>TT_GraphicsState</c>).</summary>
internal struct TtGraphicsState
{
    public ushort Rp0, Rp1, Rp2;
    public ushort Gep0, Gep1, Gep2;

    // The vectors are F2Dot14 numbers (FT_UnitVector holds FT_Short).
    public short DualX, DualY;
    public short ProjX, ProjY;
    public short FreeX, FreeY;

    public int Loop;
    public int RoundState;

    // Device-specific compensations: FreeType sets all of them to zero and no instruction changes them.

    // default values below can be modified by 'fpgm' and 'prep'
    public int MinimumDistance;
    public int ControlValueCutin;
    public int SingleWidthCutin;
    public int SingleWidthValue;
    public ushort DeltaBase;
    public ushort DeltaShift;

    public bool AutoFlip;
    public byte InstructControl;

    // According to Greg Hitchcock from Microsoft, the `scan_control' variable as documented in the TrueType
    // specification is a 32-bit integer; the high-word part holds the SCANTYPE value, the low-word part the SCANCTRL
    // value. We separate it into two fields.
    public bool ScanControl;
    public int ScanType;

    /// <summary>The state a size starts from (<c>tt_default_graphics_state</c>).</summary>
    public static TtGraphicsState Default => new()
    {
        Rp0 = 0, Rp1 = 0, Rp2 = 0,
        Gep0 = 1, Gep1 = 1, Gep2 = 1,
        DualX = 0x4000, DualY = 0,
        ProjX = 0x4000, ProjY = 0,
        FreeX = 0x4000, FreeY = 0,
        Loop = 1,
        RoundState = 1,
        MinimumDistance = 64,
        ControlValueCutin = 68,
        SingleWidthCutin = 0,
        SingleWidthValue = 0,
        DeltaBase = 9,
        DeltaShift = 3,
        AutoFlip = true,
        InstructControl = 0,
        ScanControl = false,
        ScanType = 0,
    };
}

/// <summary>A function or instruction definition (<c>TT_DefRecord</c>).</summary>
internal struct TtDefRecord
{
    /// <summary>In which code range it is located.</summary>
    public int Range;

    /// <summary>Where it starts.</summary>
    public int Start;

    /// <summary>Where it ends.</summary>
    public int End;

    /// <summary>The function number, or the instruction code.</summary>
    public uint Opc;

    public bool Active;
}

/// <summary>A call record (<c>TT_CallRec</c>); the definition is named by its table and index because C# has no interior pointers.</summary>
internal struct TtCallRecord
{
    public int CallerRange;
    public int CallerIp;
    public int CurCount;
    public bool DefIsInstruction;
    public int DefIndex;
}

/// <summary>Metrics of a size that the interpreter reads (<c>TT_Size_Metrics</c> and the used part of <c>FT_Size_Metrics</c>).</summary>
internal struct TtSizeMetrics
{
    /// <summary>The scale from font units to 26.6 pixels, 16.16.</summary>
    public int Scale;

    /// <summary>The (integer) pixels per em.</summary>
    public int Ppem;

    public int XScale;
    public int YScale;
    public int XPpem;
    public int YPpem;
}
