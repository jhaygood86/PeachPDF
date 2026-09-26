/****************************************************************************
 *
 * ftcalc.c
 *
 *   Arithmetic computations (body).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ftcalc.c, ftobjs.h (the pixel-rounding macros), ftoutln.c (FT_Vector_Transform).
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;
using System.Numerics;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// FreeType's fixed-point arithmetic (<c>FT_MulFix</c>, <c>FT_DivFix</c>, <c>FT_MulDiv</c>, ...) and the pixel-grid
/// rounding macros. Bit-exact 26.6 and 16.16 arithmetic is the whole point of the port: every function here reproduces
/// the rounding of the FreeType routine of the same name, for the 64-bit-integer configuration and a 32-bit
/// <c>FT_Long</c> (see PORTING-NOTES.md).
/// </summary>
internal static class FtCalc
{
    /// <summary>Multiplies two 16.16 numbers, or a value by a 16.16 factor (<c>FT_MulFix</c>).</summary>
    public static int MulFix(int a, int b)
    {
        long ab = (long)a * b;

        // this requires arithmetic right shift of signed numbers
        return (int)((ab + 0x8000L + (ab >> 63)) >> 16);
    }

    /// <summary><c>(a * b) / c</c>, rounded to nearest, with a 64-bit intermediate (<c>FT_MulDiv</c>).</summary>
    public static int MulDiv(int a, int b, int c)
    {
        int s = 1;
        ulong ua = MoveSign(a, ref s);
        ulong ub = MoveSign(b, ref s);
        ulong uc = MoveSign(c, ref s);

        ulong d = uc > 0 ? (ua * ub + (uc >> 1)) / uc : 0x7FFFFFFFUL;
        int d32 = unchecked((int)d);

        return s < 0 ? unchecked(-d32) : d32;
    }

    /// <summary><c>(a * b) / c</c> with the result truncated and not rounded (<c>FT_MulDiv_No_Round</c>).</summary>
    public static int MulDivNoRound(int a, int b, int c)
    {
        int s = 1;
        ulong ua = MoveSign(a, ref s);
        ulong ub = MoveSign(b, ref s);
        ulong uc = MoveSign(c, ref s);

        ulong d = uc > 0 ? ua * ub / uc : 0x7FFFFFFFUL;
        int d32 = unchecked((int)d);

        return s < 0 ? unchecked(-d32) : d32;
    }

    /// <summary><c>(a * 2^16) / b</c>, rounded to nearest (<c>FT_DivFix</c>).</summary>
    public static int DivFix(int a, int b)
    {
        int s = 1;
        ulong ua = MoveSign(a, ref s);
        ulong ub = MoveSign(b, ref s);

        ulong q = ub > 0 ? ((ua << 16) + (ub >> 1)) / ub : 0x7FFFFFFFUL;
        int q32 = unchecked((int)q);

        return s < 0 ? unchecked(-q32) : q32;
    }

    /// <summary>The index of the most significant set bit, 0 for zero (<c>FT_MSB</c>).</summary>
    public static int Msb(uint z) => z == 0 ? 0 : 31 - BitOperations.LeadingZeroCount(z);

    /// <summary>
    /// Normalizes a vector to a length of 1.0 in 16.16 (<c>FT_Vector_NormLen</c>), in place, and returns its original length.
    /// </summary>
    public static uint VectorNormLen(ref int vectorX, ref int vectorY)
    {
        int x_ = vectorX;
        int y_ = vectorY;
        int sx = 1;
        int sy = 1;

        uint x = MoveSign32(x_, ref sx);
        uint y = MoveSign32(y_, ref sy);

        // trivial cases
        if (x == 0)
        {
            if (y > 0)
                vectorY = sy * 0x10000;
            return y;
        }
        else if (y == 0)
        {
            if (x > 0)
                vectorX = sx * 0x10000;
            return x;
        }

        // Estimate length and prenormalize by shifting so that the new approximate length is between 2/3 and 4/3.
        // The magic constant 0xAAAAAAAAUL (2/3 of 2^32) helps achieve this in 16.16 fixed-point representation.
        uint l = x > y ? x + (y >> 1) : y + (x >> 1);

        int shift = 31 - Msb(l);
        shift -= 15 + (l >= (0xAAAAAAAAu >> shift) ? 1 : 0);

        if (shift > 0)
        {
            x <<= shift;
            y <<= shift;

            // re-estimate length for tiny vectors
            l = x > y ? x + (y >> 1) : y + (x >> 1);
        }
        else
        {
            x >>= -shift;
            y >>= -shift;
            l >>= -shift;
        }

        // lower linear approximation for reciprocal length minus one
        int b = 0x10000 - (int)l;

        x_ = (int)x;
        y_ = (int)y;

        uint u;
        uint v;
        int z;

        // Newton's iterations
        unchecked
        {
            do
            {
                u = (uint)(x_ + ((x_ * b) >> 16));
                v = (uint)(y_ + ((y_ * b) >> 16));

                // Normalized squared length in the parentheses approaches 2^32. On two's complement systems,
                // converting to signed gives the difference with 2^32 even if the expression wraps around.
                z = -(int)(u * u + v * v) / 0x200;
                z = z * ((0x10000 + b) >> 8) / 0x10000;

                b += z;
            }
            while (z > 0);

            vectorX = sx < 0 ? -(int)u : (int)u;
            vectorY = sy < 0 ? -(int)v : (int)v;

            // Conversion to signed helps to recover from likely wrap around in calculating the prenormalized
            // length, because it gives the correct difference with 2^32 on two's complement systems.
            l = (uint)(0x10000 + (int)(u * x + v * y) / 0x10000);
            if (shift > 0)
                l = (l + (1u << (shift - 1))) >> shift;
            else
                l <<= -shift;
        }

        return l;
    }

    /// <summary>Transfers the sign of a value to a running sign and returns its magnitude (<c>FT_MOVE_SIGN</c>).</summary>
    private static ulong MoveSign(int x, ref int s)
    {
        if (x < 0)
        {
            s = -s;
            return 0UL - (ulong)(long)x;
        }

        return (ulong)x;
    }

    private static uint MoveSign32(int x, ref int s)
    {
        if (x < 0)
        {
            s = -s;
            return 0U - (uint)x;
        }

        return (uint)x;
    }

    // The rounding macros of ftobjs.h. The additions wrap, as FreeType's ADD_LONG does.

    /// <summary>Rounds down to a whole pixel (<c>FT_PIX_FLOOR</c>).</summary>
    public static int PixFloor(int x) => x & ~63;

    /// <summary>Rounds to the nearest whole pixel (<c>FT_PIX_ROUND</c>).</summary>
    public static int PixRound(int x) => unchecked(x + 32) & ~63;

    /// <summary>Rounds up to a whole pixel (<c>FT_PIX_CEIL</c>).</summary>
    public static int PixCeil(int x) => unchecked(x + 63) & ~63;

    /// <summary>Rounds to the nearest multiple of a power of two (<c>FT_PAD_ROUND</c>).</summary>
    public static int PadRound(int x, int n) => unchecked(x + n / 2) & ~(n - 1);

    /// <summary>Rounds a 16.16 number to the nearest whole number, still in 16.16 (<c>FT_RoundFix</c>).</summary>
    public static int RoundFix(int a) => unchecked(a + (0x8000 - (a < 0 ? 1 : 0))) & ~0xFFFF;

    /// <summary>The square root of a 16.16 number, in 16.16, rounded (<c>FT_SqrtFixed</c>; the 64-bit-integer code path).</summary>
    public static uint SqrtFixed(uint v)
    {
        if (v == 0)
            return 0;

        ulong r = ((ulong)v << 16) - 1;
        uint q = 1u << ((17 + Msb(v)) >> 1);
        uint t;

        // Babylonian method with rounded-up division
        do
        {
            t = q;
            q = unchecked(t + (uint)(r / t) + 1) >> 1;
        }
        while (q != t);

        return q;
    }

    /// <summary>
    /// <c>FT_Matrix_Multiply_Scaled</c>: multiplies matrix <paramref name="a"/> into matrix <paramref name="b"/> (in place), both in 16.16
    /// but the product divided by <c>0x10000 * scaling</c>. The matrices are (xx, xy, yx, yy).
    /// </summary>
    public static void MatrixMultiplyScaled(int axx, int axy, int ayx, int ayy, ref int bxx, ref int bxy, ref int byx, ref int byy, int scaling)
    {
        int val = unchecked(0x10000 * scaling);

        int xx = unchecked(MulDiv(axx, bxx, val) + MulDiv(axy, byx, val));
        int xy = unchecked(MulDiv(axx, bxy, val) + MulDiv(axy, byy, val));
        int yx = unchecked(MulDiv(ayx, bxx, val) + MulDiv(ayy, byx, val));
        int yy = unchecked(MulDiv(ayx, bxy, val) + MulDiv(ayy, byy, val));

        bxx = xx;
        bxy = xy;
        byx = yx;
        byy = yy;
    }

    /// <summary><c>FT_Vector_Transform_Scaled</c>: transforms a vector by a matrix, the products divided by <c>0x10000 * scaling</c>.</summary>
    public static void VectorTransformScaled(ref int x, ref int y, int xx, int xy, int yx, int yy, int scaling)
    {
        int val = unchecked(0x10000 * scaling);

        int xz = unchecked(MulDiv(x, xx, val) + MulDiv(y, xy, val));
        int yz = unchecked(MulDiv(x, yx, val) + MulDiv(y, yy, val));

        x = xz;
        y = yz;
    }

    /// <summary><c>FT_Matrix_Check</c>: whether a matrix is invertible without numerical trouble.</summary>
    public static bool MatrixCheck(int xx, int xy, int yx, int yy)
    {
        long val = (long)Math.Abs((long)xx) | Math.Abs((long)xy) | Math.Abs((long)yx) | Math.Abs((long)yy);

        // we only handle non-zero 32-bit values
        if (val == 0 || val > 0x7FFFFFFFL)
            return false;

        // scale the matrix to avoid the temp1 overflow, which is more stringent than avoiding the temp2 overflow
        int shift = Msb((uint)val) - 12;

        if (shift > 0)
        {
            xx >>= shift;
            xy >>= shift;
            yx >>= shift;
            yy >>= shift;
        }

        uint temp1 = unchecked(32U * (uint)Math.Abs(unchecked(xx * yy - xy * yx)));
        uint temp2 = unchecked((uint)(xx * xx) + (uint)(xy * xy) + (uint)(yx * yx) + (uint)(yy * yy));

        return temp1 > temp2;
    }

    /// <summary><c>FT_Vector_Transform</c> (of ftoutln.c): a vector times a 16.16 matrix.</summary>
    public static void VectorTransform(ref int x, ref int y, int xx, int xy, int yx, int yy)
    {
        int xz = unchecked(MulFix(x, xx) + MulFix(y, xy));
        int yz = unchecked(MulFix(x, yx) + MulFix(y, yy));

        x = xz;
        y = yz;
    }
}
