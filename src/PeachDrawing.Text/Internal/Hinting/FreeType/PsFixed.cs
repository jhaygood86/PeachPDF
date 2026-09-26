/****************************************************************************
 *
 * psfixed.h
 *
 *   Adobe's code for Fixed-Point Mathematics (specification only).
 *
 * Copyright 2007-2013 Adobe Systems Incorporated.
 *
 * This software, and all works of authorship, whether in source or
 * object code form as indicated by the copyright notice(s) included
 * herein (collectively, the "Work") is made available, and may only be
 * used, modified, and distributed under the FreeType Project License,
 * LICENSE.TXT.  Additionally, subject to the terms and conditions of the
 * FreeType Project License, each contributor to the Work hereby grants
 * to any individual or legal entity exercising permissions granted by
 * the FreeType Project License and this section (hereafter, "You" or
 * "Your") a perpetual, worldwide, non-exclusive, no-charge,
 * royalty-free, irrevocable (except as stated in this section) patent
 * license to make, have made, use, offer to sell, sell, import, and
 * otherwise transfer the Work, where such license applies only to those
 * patent claims licensable by such contributor that are necessarily
 * infringed by their contribution(s) alone or by combination of their
 * contribution(s) with the Work to which such contribution(s) was
 * submitted.  If You institute patent litigation against any entity
 * (including a cross-claim or counterclaim in a lawsuit) alleging that
 * the Work or a contribution incorporated within the Work constitutes
 * direct or contributory patent infringement, then any patent licenses
 * granted to You under this License for that Work shall terminate as of
 * the date such litigation is filed.
 *
 * By using, modifying, or distributing the Work you indicate that you
 * have read and understood the terms and conditions of the
 * FreeType Project License as well as those provided in this section,
 * and you accept them fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psfixed.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The 16.16 fixed-point numbers of Adobe's CFF engine (<c>CF2_Fixed</c>) and the macros that convert them. Arithmetic wraps, as it does
/// in the C code, which relies on <c>ADD_INT32</c> and its companions to do that without undefined behaviour.
/// </summary>
internal static class Cf2Fixed
{
    public const int Max = 0x7FFFFFFF;
    public const int Min = unchecked((int)0x80000000);
    public const int One = 0x10000;
    public const int Epsilon = 0x0001;

    /// <summary><c>cf2_intToFixed</c>.</summary>
    public static int FromInt(int i) => unchecked((int)((uint)i << 16));

    /// <summary><c>cf2_fixedToInt</c>: the number rounded to an integer, as a 16-bit value.</summary>
    public static int ToInt(int x) => (short)(((uint)x + 0x8000U) >> 16);

    /// <summary><c>cf2_fixedRound</c>.</summary>
    public static int Round(int x) => unchecked((int)(((uint)x + 0x8000U) & 0xFFFF0000U));

    /// <summary><c>cf2_doubleToFixed</c>.</summary>
    public static int FromDouble(double f) => unchecked((int)(f * 65536.0 + 0.5));

    /// <summary><c>cf2_fixedAbs</c>.</summary>
    public static int Abs(int x) => x < 0 ? unchecked(-x) : x;

    /// <summary><c>cf2_fixedFloor</c>.</summary>
    public static int Floor(int x) => unchecked((int)((uint)x & 0xFFFF0000U));

    /// <summary><c>cf2_fixedFraction</c>.</summary>
    public static int Fraction(int x) => unchecked(x - Floor(x));

    /// <summary><c>cf2_fracToFixed</c>: from 2.30 to 16.16.</summary>
    public static int FracToFixed(int x) => unchecked(x + 0x2000 - (x < 0 ? 1 : 0)) >> 14;
}
