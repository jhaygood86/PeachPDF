/****************************************************************************
 *
 * cffparse.c
 *
 *   CFF token stream parser (body)
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): cffparse.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>The fields of a CFF Top DICT or Font DICT that hinting reads (<c>CFF_FontRecDictRec</c>).</summary>
internal sealed class CffFontDict
{
    /// <summary>The sentinel FreeType uses for a missing SID (and a missing registry: the font is not CID-keyed).</summary>
    public const uint NoSid = 0xFFFFU;

    public uint CidRegistry = NoSid;
    public uint CharstringType = 2;
    public uint CharstringsOffset;
    public uint CharsetOffset;
    public uint PrivateSize;
    public uint PrivateOffset;
    public uint FdArrayOffset;
    public uint FdSelectOffset;

    public bool HasFontMatrix;

    // the font matrix as 16.16 numbers, its offset in font units, and the units per em that go with the normalized matrix
    public int MatrixXx = 0x10000, MatrixYx, MatrixXy, MatrixYy = 0x10000;
    public int OffsetX, OffsetY;
    public uint UnitsPerEm;
}

/// <summary>The fields of a CFF Private DICT that the Adobe engine reads (<c>CFF_PrivateRec</c>).</summary>
internal sealed class CffPrivate
{
    public const int MaxBlueValues = 14, MaxOtherBlues = 10, MaxFamilyBlues = 14, MaxFamilyOtherBlues = 10;

    public int NumBlueValues;
    public readonly int[] BlueValues = new int[MaxBlueValues];
    public int NumOtherBlues;
    public readonly int[] OtherBlues = new int[MaxOtherBlues];
    public int NumFamilyBlues;
    public readonly int[] FamilyBlues = new int[MaxFamilyBlues];
    public int NumFamilyOtherBlues;
    public readonly int[] FamilyOtherBlues = new int[MaxFamilyOtherBlues];

    /// <summary>BlueScale times 1000, in 16.16.</summary>
    public int BlueScale;
    public int BlueShift;
    public int BlueFuzz;

    /// <summary>StdHW.</summary>
    public int StandardWidth;

    /// <summary>StdVW.</summary>
    public int StandardHeight;

    public int LanguageGroup;
    public int DefaultWidth;
    public int NominalWidth;
    public uint LocalSubrsOffset;
    public int InitialRandomSeed;
}

/// <summary>
/// The parser of CFF DICT data (<c>cff_parser_run</c> and the number readers), reading the operators the hinter needs from a Top DICT, a
/// Font DICT or a Private DICT. Operands are kept as positions in the data, as in FreeType, and converted by the operator's own reader.
/// </summary>
internal static class CffParser
{
    private const int MaxStackDepth = 96;

    // operator codes: the second byte of a two-byte operator is added to 0x100
    private const int OpCharset = 15;
    private const int OpEncoding = 16;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;
    private const int OpCharstringType = 0x106;
    private const int OpFontMatrix = 0x107;
    private const int OpCidRos = 0x11E;
    private const int OpFdArray = 0x124;
    private const int OpFdSelect = 0x125;

    private const int OpBlueValues = 6;
    private const int OpOtherBlues = 7;
    private const int OpFamilyBlues = 8;
    private const int OpFamilyOtherBlues = 9;
    private const int OpStdHw = 10;
    private const int OpStdVw = 11;
    private const int OpSubrs = 19;
    private const int OpDefaultWidthX = 20;
    private const int OpNominalWidthX = 21;
    private const int OpBlueScale = 0x109;
    private const int OpBlueShift = 0x10A;
    private const int OpBlueFuzz = 0x10B;
    private const int OpLanguageGroup = 0x111;
    private const int OpInitialRandomSeed = 0x113;

    private static readonly int[] PowerTens =
    [
        1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000,
    ];

    // maximum values allowed for multiplying with the corresponding `power_tens' element
    private static readonly int[] PowerTenLimits =
    [
        int.MaxValue / 1, int.MaxValue / 10, int.MaxValue / 100, int.MaxValue / 1000, int.MaxValue / 10000, int.MaxValue / 100000,
        int.MaxValue / 1000000, int.MaxValue / 10000000, int.MaxValue / 100000000, int.MaxValue / 1000000000,
    ];

    /// <summary>What a DICT holds: the fields of a Top DICT (or Font DICT), or those of a Private DICT.</summary>
    public enum Kind
    {
        Top,
        Private,
    }

    /// <summary>
    /// Runs the operators of the DICT in <c>data[start..limit)</c>, storing into <paramref name="top"/> or <paramref name="priv"/> what
    /// they say. FreeType's errors here (a stack that over- or underflows, a truncated operator) fail the loading of the font.
    /// </summary>
    /// <exception cref="HintingException">The DICT is malformed in a way FreeType refuses.</exception>
    public static void Run(byte[] data, int start, int limit, Kind kind, CffFontDict? top, CffPrivate? priv)
    {
        var stack = new int[MaxStackDepth]; // the positions of the operands
        int stackTop = 0;
        int p = start;

        while (p < limit)
        {
            uint v = data[p];

            // Opcode 31 is legacy MM T2 operator, not a number.  Opcode 255 is reserved and should not appear in fonts; it is
            // used internally for CFF2 blends.
            if (v >= 27 && v != 31 && v != 255)
            {
                // it's a number; we will push its position on the stack
                if (stackTop >= MaxStackDepth)
                    throw new HintingException("A CFF DICT has too many operands.");

                stack[stackTop++] = p;

                // now, skip it
                if (v == 30)
                {
                    // skip real number
                    p++;
                    for (; ; )
                    {
                        // An unterminated floating point number at the end of a dictionary is invalid but harmless.
                        if (p >= limit)
                            return;

                        v = (uint)data[p] >> 4;
                        if (v == 15)
                            break;

                        v = (uint)data[p] & 0xF;
                        if (v == 15)
                            break;

                        p++;
                    }
                }
                else if (v == 28)
                {
                    p += 2;
                }
                else if (v == 29)
                {
                    p += 4;
                }
                else if (v > 246)
                {
                    p += 1;
                }
            }
            else
            {
                // This is not a number, hence it's an operator.  Compute its code and look for it in our current list.
                if (stackTop >= MaxStackDepth)
                    throw new HintingException("A CFF DICT has too many operands.");

                int numArgs = stackTop;
                int code = (int)v;

                if (v == 12)
                {
                    // two byte operator
                    p++;
                    if (p >= limit)
                        throw new HintingException("A CFF DICT ends inside an operator.");

                    code = 0x100 | data[p];
                }

                Handle(data, limit, kind, top, priv, code, stack, numArgs);

                // clear stack
                stackTop = 0;
            }

            p++;
        }
    }

    private static void Handle(byte[] data, int limit, Kind kind, CffFontDict? top, CffPrivate? priv, int code, int[] stack, int numArgs)
    {
        if (kind == Kind.Top && top is not null)
        {
            switch (code)
            {
                case OpCharset:
                    top.CharsetOffset = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpEncoding:
                    RequireArgs(stack, numArgs);
                    break;

                case OpCharStrings:
                    top.CharstringsOffset = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpCharstringType:
                    top.CharstringType = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpFdArray:
                    top.FdArrayOffset = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpFdSelect:
                    top.FdSelectOffset = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpPrivate:
                    ParsePrivateDictOperator(data, limit, top, stack, numArgs);
                    break;

                case OpFontMatrix:
                    ParseFontMatrix(data, limit, top, stack, numArgs);
                    break;

                case OpCidRos:
                    ParseCidRos(data, limit, top, stack, numArgs);
                    break;
            }

            return;
        }

        if (kind == Kind.Private && priv is not null)
        {
            switch (code)
            {
                case OpBlueValues:
                    priv.NumBlueValues = DeltaFixed(data, limit, stack, numArgs, CffPrivate.MaxBlueValues, priv.BlueValues);
                    break;

                case OpOtherBlues:
                    priv.NumOtherBlues = DeltaFixed(data, limit, stack, numArgs, CffPrivate.MaxOtherBlues, priv.OtherBlues);
                    break;

                case OpFamilyBlues:
                    priv.NumFamilyBlues = DeltaFixed(data, limit, stack, numArgs, CffPrivate.MaxFamilyBlues, priv.FamilyBlues);
                    break;

                case OpFamilyOtherBlues:
                    priv.NumFamilyOtherBlues = DeltaFixed(data, limit, stack, numArgs, CffPrivate.MaxFamilyOtherBlues, priv.FamilyOtherBlues);
                    break;

                case OpBlueScale:
                    priv.BlueScale = DoFixed(data, limit, RequireArgs(stack, numArgs), 3);
                    break;

                case OpBlueShift:
                    priv.BlueShift = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpBlueFuzz:
                    priv.BlueFuzz = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpStdHw:
                    priv.StandardWidth = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpStdVw:
                    priv.StandardHeight = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpLanguageGroup:
                    priv.LanguageGroup = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpInitialRandomSeed:
                    priv.InitialRandomSeed = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpSubrs:
                    priv.LocalSubrsOffset = (uint)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpDefaultWidthX:
                    priv.DefaultWidth = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;

                case OpNominalWidthX:
                    priv.NominalWidth = (int)Num(data, limit, RequireArgs(stack, numArgs));
                    break;
            }
        }
    }

    // check that we have enough arguments (FreeType's Stack_Underflow, which fails the font); returns the position of the first operand
    private static int RequireArgs(int[] stack, int numArgs)
    {
        if (numArgs < 1)
            throw new HintingException("A CFF DICT operator has no operand.");

        return stack[0];
    }

    // cff_kind_delta_fixed: an array of 16.16 numbers, each stored as the sum of it and those before it
    private static int DeltaFixed(byte[] data, int limit, int[] stack, int numArgs, int arrayMax, int[] destination)
    {
        if (numArgs > arrayMax)
            numArgs = arrayMax;

        int count = numArgs & 0xFF; // the count is stored in a byte
        int val = 0;
        for (int i = 0; i < numArgs; i++)
        {
            val = unchecked(val + DoFixed(data, limit, stack[i], 0));
            destination[i] = val;
        }

        return count;
    }

    private static void ParsePrivateDictOperator(byte[] data, int limit, CffFontDict dict, int[] stack, int numArgs)
    {
        if (numArgs < 2)
            throw new HintingException("The Private operator of a CFF DICT lacks an operand.");

        int tmp = Num(data, limit, stack[0]);
        if (tmp < 0)
            throw new HintingException("The size of a Private DICT is invalid.");

        dict.PrivateSize = (uint)tmp;

        tmp = Num(data, limit, stack[1]);
        if (tmp < 0)
            throw new HintingException("The offset of a Private DICT is invalid.");

        dict.PrivateOffset = (uint)tmp;
    }

    private static void ParseCidRos(byte[] data, int limit, CffFontDict dict, int[] stack, int numArgs)
    {
        if (numArgs < 3)
            throw new HintingException("The ROS operator of a CFF DICT lacks an operand.");

        dict.CidRegistry = (uint)Num(data, limit, stack[0]);
        _ = Num(data, limit, stack[1]); // the ordering
        _ = Num(data, limit, stack[2]); // the supplement
    }

    private static void ParseFontMatrix(byte[] data, int limit, CffFontDict dict, int[] stack, int numArgs)
    {
        if (numArgs < 6)
            throw new HintingException("The FontMatrix operator of a CFF DICT lacks an operand.");

        var values = new int[6];
        var scalings = new int[6];

        dict.HasFontMatrix = true;

        // We expect a well-formed font matrix, that is, the matrix elements `xx' and `yy' are of approximately the same magnitude.  To
        // avoid loss of precision, we use the magnitude of the largest matrix element to scale all other elements.  The scaling factor
        // is then contained in the `units_per_em' value.
        int maxScaling = int.MinValue;
        int minScaling = int.MaxValue;

        for (int i = 0; i < 6; i++)
        {
            values[i] = FixedDynamic(data, limit, stack[i], out scalings[i]);
            if (values[i] != 0)
            {
                if (scalings[i] > maxScaling)
                    maxScaling = scalings[i];

                if (scalings[i] < minScaling)
                    minScaling = scalings[i];
            }
        }

        if (maxScaling < -9 || maxScaling > 0 || (maxScaling - minScaling) < 0 || (maxScaling - minScaling) > 9)
        {
            // return default matrix in case of unlikely values
            UseDefaultMatrix(dict);
            return;
        }

        for (int i = 0; i < 6; i++)
        {
            int value = values[i];

            if (value == 0)
                continue;

            int divisor = PowerTens[maxScaling - scalings[i]];
            int halfDivisor = divisor >> 1;

            if (value < 0)
            {
                if (int.MinValue + halfDivisor < value)
                    values[i] = (value - halfDivisor) / divisor;
                else
                    values[i] = int.MinValue / divisor;
            }
            else
            {
                if (int.MaxValue - halfDivisor > value)
                    values[i] = (value + halfDivisor) / divisor;
                else
                    values[i] = int.MaxValue / divisor;
            }
        }

        dict.MatrixXx = values[0];
        dict.MatrixYx = values[1];
        dict.MatrixXy = values[2];
        dict.MatrixYy = values[3];
        dict.OffsetX = values[4];
        dict.OffsetY = values[5];

        dict.UnitsPerEm = (uint)PowerTens[-maxScaling];

        if (!FtCalc.MatrixCheck(dict.MatrixXx, dict.MatrixXy, dict.MatrixYx, dict.MatrixYy))
            UseDefaultMatrix(dict);
    }

    private static void UseDefaultMatrix(CffFontDict dict)
    {
        dict.MatrixXx = 0x10000;
        dict.MatrixYx = 0;
        dict.MatrixXy = 0;
        dict.MatrixYy = 0x10000;
        dict.OffsetX = 0;
        dict.OffsetY = 0;
        dict.UnitsPerEm = 1;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Numbers
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Reads an integer operand (<c>cff_parse_integer</c>); the limit checks detect the immediate crossing of the DICT's end.</summary>
    private static int Integer(byte[] data, int limit, int start)
    {
        int p = start;
        int v = data[p++];
        int val;

        if (v == 28)
        {
            if (p + 2 > limit && limit >= p)
                return 0;

            val = (short)((At(data, p) << 8) | At(data, p + 1));
        }
        else if (v == 29)
        {
            if (p + 4 > limit && limit >= p)
                return 0;

            val = (At(data, p) << 24) | (At(data, p + 1) << 16) | (At(data, p + 2) << 8) | At(data, p + 3);
        }
        else if (v < 247)
        {
            val = v - 139;
        }
        else if (v < 251)
        {
            if (p + 1 > limit && limit >= p)
                return 0;

            val = (v - 247) * 256 + At(data, p) + 108;
        }
        else
        {
            if (p + 1 > limit && limit >= p)
                return 0;

            val = -(v - 251) * 256 - At(data, p) - 108;
        }

        return val;
    }

    // FT_ABS on a 32-bit FT_Long: the absolute value of the smallest number is itself
    private static int Abs32(int value) => value < 0 ? unchecked(-value) : value;

    private static byte At(byte[] data, int position) => position < data.Length ? data[position] : (byte)0;

    /// <summary>Reads a real operand (<c>cff_parse_real</c>): the number as 16.16, or, with <paramref name="scaling"/>, as a mantissa and a power of ten.</summary>
    private static int Real(byte[] data, int start, int limit, int powerTen, bool wantScaling, out int scaling)
    {
        int p = start;
        int nib;
        uint phase;

        int result = 0;
        int number = 0;
        int exponent = 0;
        int sign = 0, exponentSign = 0, haveOverflow = 0;
        int exponentAdd = 0, integerLength = 0, fractionLength = 0;

        scaling = 0;

        // First of all, read the integer part.
        phase = 4;

        for (; ; )
        {
            // If we entered this iteration with phase == 4, we need to read a new byte.  This also skips past the initial 0x1E.
            if (phase != 0)
            {
                p++;

                // Make sure we don't read past the end.
                if (p + 1 > limit && limit >= p)
                    return Bad();
            }

            // Get the nibble.
            nib = (At(data, p) >> (int)phase) & 0xF;
            phase = 4 - phase;

            if (nib == 0xE)
            {
                sign = 1;
            }
            else if (nib > 9)
            {
                break;
            }
            else
            {
                // Increase exponent if we can't add the digit.
                if (number >= 0xCCCCCCC)
                    exponentAdd++;

                // Skip leading zeros.
                else if (nib != 0 || number != 0)
                {
                    integerLength++;
                    number = unchecked(number * 10 + nib);
                }
            }
        }

        // Read fraction part, if any.
        if (nib == 0xA)
        {
            for (; ; )
            {
                // If we entered this iteration with phase == 4, we need to read a new byte.
                if (phase != 0)
                {
                    p++;

                    // Make sure we don't read past the end.
                    if (p + 1 > limit && limit >= p)
                        return Bad();
                }

                // Get the nibble.
                nib = (At(data, p) >> (int)phase) & 0xF;
                phase = 4 - phase;
                if (nib >= 10)
                    break;

                // Skip leading zeros if possible.
                if (nib == 0 && number == 0)
                {
                    exponentAdd--;
                }

                // Only add digit if we don't overflow.
                else if (number < 0xCCCCCCC && fractionLength < 9)
                {
                    fractionLength++;
                    number = unchecked(number * 10 + nib);
                }
            }
        }

        // Read exponent, if any.
        if (nib == 12)
        {
            exponentSign = 1;
            nib = 11;
        }

        if (nib == 11)
        {
            for (; ; )
            {
                // If we entered this iteration with phase == 4, we need to read a new byte.
                if (phase != 0)
                {
                    p++;

                    // Make sure we don't read past the end.
                    if (p + 1 > limit && limit >= p)
                        return Bad();
                }

                // Get the nibble.
                nib = (At(data, p) >> (int)phase) & 0xF;
                phase = 4 - phase;
                if (nib >= 10)
                    break;

                // Arbitrarily limit exponent.
                if (exponent > 1000)
                    haveOverflow = 1;
                else
                    exponent = exponent * 10 + nib;
            }

            if (exponentSign != 0)
                exponent = -exponent;
        }

        if (number == 0)
            goto Exit;

        if (haveOverflow != 0)
        {
            result = exponentSign != 0 ? 0 : 0x7FFFFFFF;
            goto Exit;
        }

        // We don't check `power_ten' and `exponent_add'.
        exponent += powerTen + exponentAdd;

        if (wantScaling)
        {
            // Only use `fraction_length'.
            fractionLength += integerLength;
            exponent += integerLength;

            if (fractionLength <= 5)
            {
                if (number > 0x7FFF)
                {
                    result = FtCalc.DivFix(number, 10);
                    scaling = exponent - fractionLength + 1;
                }
                else
                {
                    if (exponent > 0)
                    {
                        // Make `scaling' as small as possible.
                        int newFractionLength = Math.Min(exponent, 5);
                        int shift = newFractionLength - fractionLength;

                        if (shift > 0)
                        {
                            exponent -= newFractionLength;
                            number *= PowerTens[shift];
                            if (number > 0x7FFF)
                            {
                                number /= 10;
                                exponent += 1;
                            }
                        }
                        else
                        {
                            exponent -= fractionLength;
                        }
                    }
                    else
                    {
                        exponent -= fractionLength;
                    }

                    result = (int)((uint)number << 16);
                    scaling = exponent;
                }
            }
            else
            {
                if ((number / PowerTens[fractionLength - 5]) > 0x7FFF)
                {
                    result = FtCalc.DivFix(number, PowerTens[fractionLength - 4]);
                    scaling = exponent - 4;
                }
                else
                {
                    result = FtCalc.DivFix(number, PowerTens[fractionLength - 5]);
                    scaling = exponent - 5;
                }
            }
        }
        else
        {
            integerLength += exponent;
            fractionLength -= exponent;

            if (integerLength > 5)
            {
                result = 0x7FFFFFFF;
                goto Exit;
            }

            if (integerLength < -5)
            {
                result = 0;
                goto Exit;
            }

            // Remove non-significant digits.
            if (integerLength < 0)
            {
                number /= PowerTens[-integerLength];
                fractionLength += integerLength;
            }

            // this can only happen if exponent was non-zero
            if (fractionLength == 10)
            {
                number /= 10;
                fractionLength -= 1;
            }

            // Convert into 16.16 format.
            if (fractionLength > 0)
            {
                if ((number / PowerTens[fractionLength]) > 0x7FFF)
                    goto Exit;

                result = FtCalc.DivFix(number, PowerTens[fractionLength]);
            }
            else
            {
                number = unchecked(number * PowerTens[-fractionLength]);

                if (number > 0x7FFF)
                {
                    result = 0x7FFFFFFF;
                    goto Exit;
                }

                result = (int)((uint)number << 16);
            }
        }

    Exit:
        if (sign != 0)
            result = unchecked(-result);

        return result;

        static int Bad() => 0;
    }

    /// <summary>Reads a number, either integer or real (<c>cff_parse_num</c>); a real is truncated to an integer.</summary>
    private static int Num(byte[] data, int limit, int position)
    {
        if (data[position] == 30)
        {
            // binary-coded decimal is truncated to integer
            return Real(data, position, limit, 0, false, out _) >> 16;
        }

        if (data[position] == 255)
        {
            // 16.16 fixed-point is used internally for CFF2 blend results; it does not occur in a Top, Font or Private DICT
            return (short)((((uint)At(data, position + 1) << 16) | ((uint)At(data, position + 2) << 8) | At(data, position + 3)) + 0x80U >> 8);
        }

        return Integer(data, limit, position);
    }

    /// <summary>Reads a floating point number, either integer or real, as 16.16 and times <c>10^scaling</c> (<c>do_fixed</c>).</summary>
    private static int DoFixed(byte[] data, int limit, int position, int scaling)
    {
        if (data[position] == 30)
            return Real(data, position, limit, scaling, false, out _);

        if (data[position] == 255)
        {
            int val32 = (int)(((uint)At(data, position + 1) << 24) | ((uint)At(data, position + 2) << 16) | ((uint)At(data, position + 3) << 8) | At(data, position + 4));

            if (scaling != 0)
            {
                if (Abs32(val32) > PowerTenLimits[scaling])
                    return val32 > 0 ? 0x7FFFFFFF : -0x7FFFFFFF;

                val32 = unchecked(val32 * PowerTens[scaling]);
            }

            return val32;
        }

        int val = Integer(data, limit, position);

        if (scaling != 0)
        {
            if (unchecked(Abs32(val) << 16) > PowerTenLimits[scaling])
                return val > 0 ? 0x7FFFFFFF : -0x7FFFFFFF;

            val = unchecked(val * PowerTens[scaling]);
        }

        if (val > 0x7FFF)
            return 0x7FFFFFFF;

        if (val < -0x7FFF)
            return -0x7FFFFFFF;

        return (int)((uint)val << 16);
    }

    /// <summary>
    /// Reads a floating point number, either integer or real, as precisely as possible: the number as 16.16 and, in
    /// <paramref name="scaling"/>, the power of ten it was scaled by (<c>cff_parse_fixed_dynamic</c>).
    /// </summary>
    private static int FixedDynamic(byte[] data, int limit, int position, out int scaling)
    {
        if (data[position] == 30)
            return Real(data, position, limit, 0, true, out scaling);

        int number = Integer(data, limit, position);

        if (number > 0x7FFF)
        {
            int integerLength;
            for (integerLength = 5; integerLength < 10; integerLength++)
            {
                if (number < PowerTens[integerLength])
                    break;
            }

            if ((number / PowerTens[integerLength - 5]) > 0x7FFF)
            {
                scaling = integerLength - 4;
                return FtCalc.DivFix(number, PowerTens[integerLength - 4]);
            }

            scaling = integerLength - 5;
            return FtCalc.DivFix(number, PowerTens[integerLength - 5]);
        }

        scaling = 0;
        return (int)((uint)number << 16);
    }
}
