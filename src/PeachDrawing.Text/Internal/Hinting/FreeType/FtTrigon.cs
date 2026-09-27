/****************************************************************************
 *
 * fttrigon.c
 *
 *   FreeType trigonometric functions (body).
 *
 * Copyright (C) 2001-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): fttrigon.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The CORDIC vector length of FreeType's <c>fttrigon.c</c>, which the glyph loader needs for the offset of a composite
/// glyph component that carries a scale (<c>FT_Hypot</c>).
/// </summary>
internal static class FtTrigon
{
    // the Cordic shrink factor 0.858785336480436 * 2^32
    private const uint TrigScale = 0xDBD95B16U;

    // the highest bit in overflow-safe vector components, MSB of 0.858785336480436 * sqrt(0.5) * 2^30
    private const int TrigSafeMsb = 29;

    // this table was generated for FT_PI = 180L << 16, i.e. degrees
    private const int TrigMaxIters = 23;

    private const int AnglePi = 180 << 16;
    private const int AnglePi2 = AnglePi / 2;

    private static readonly int[] ArctanTable =
    [
        1740967, 919879, 466945, 234379, 117304, 58666, 29335,
        14668, 7334, 3667, 1833, 917, 458, 229, 115,
        57, 29, 14, 7, 4, 2, 1,
    ];

    /// <summary>The length of a vector (<c>FT_Hypot</c>, that is <c>FT_Vector_Length</c>).</summary>
    public static int Hypot(int x, int y)
    {
        // handle trivial cases
        if (x == 0)
            return y < 0 ? unchecked(-y) : y;
        else if (y == 0)
            return x < 0 ? unchecked(-x) : x;

        // general case
        int shift = Prenorm(ref x, ref y);
        PseudoPolarize(ref x, ref y);

        x = Downscale(x);

        if (shift > 0)
            return unchecked(x + (1 << (shift - 1))) >> shift;

        return unchecked((int)((uint)x << -shift));
    }

    // multiply a given value by the CORDIC shrink factor
    private static int Downscale(int val)
    {
        int s = 1;

        if (val < 0)
        {
            val = unchecked(-val);
            s = -1;
        }

        // 0x40000000 comes from regression analysis between true and CORDIC hypotenuse, so it minimizes the error
        val = unchecked((int)(((ulong)(uint)val * TrigScale + 0x40000000UL) >> 32));

        return s < 0 ? unchecked(-val) : val;
    }

    // undefined and never called for the zero vector
    private static int Prenorm(ref int vx, ref int vy)
    {
        int x = vx;
        int y = vy;

        int ax = x < 0 ? unchecked(-x) : x;
        int ay = y < 0 ? unchecked(-y) : y;
        int shift = FtCalc.Msb((uint)(ax | ay));

        if (shift <= TrigSafeMsb)
        {
            shift = TrigSafeMsb - shift;
            vx = unchecked((int)((uint)x << shift));
            vy = unchecked((int)((uint)y << shift));
        }
        else
        {
            shift -= TrigSafeMsb;
            vx = x >> shift;
            vy = y >> shift;
            shift = -shift;
        }

        return shift;
    }

    private static void PseudoPolarize(ref int vx, ref int vy)
    {
        int theta;
        int x = vx;
        int y = vy;

        // Get the vector into [-PI/4,PI/4] sector
        if (y > x)
        {
            if (y > -x)
            {
                theta = AnglePi2;
                int xtemp = y;
                y = -x;
                x = xtemp;
            }
            else
            {
                theta = y > 0 ? AnglePi : -AnglePi;
                x = -x;
                y = -y;
            }
        }
        else
        {
            if (y < -x)
            {
                theta = -AnglePi2;
                int xtemp = -y;
                y = x;
                x = xtemp;
            }
            else
            {
                theta = 0;
            }
        }

        int arctan = 0;

        // Pseudorotations, with right shifts
        for (int i = 1, b = 1; i < TrigMaxIters; b <<= 1, i++)
        {
            int xtemp;
            if (y > 0)
            {
                xtemp = x + ((y + b) >> i);
                y = y - ((x + b) >> i);
                x = xtemp;
                theta += ArctanTable[arctan++];
            }
            else
            {
                xtemp = x - ((y + b) >> i);
                y = y + ((x + b) >> i);
                x = xtemp;
                theta -= ArctanTable[arctan++];
            }
        }

        // round theta to acknowledge its error that mostly comes from accumulated rounding errors in the arctan table
        if (theta >= 0)
            theta = FtCalc.PadRound(theta, 16);
        else
            theta = -FtCalc.PadRound(-theta, 16);

        vx = x;
        vy = theta;
    }
}
