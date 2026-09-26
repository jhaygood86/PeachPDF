/****************************************************************************
 *
 * psintrp.c
 *
 *   Adobe's CFF Interpreter (body).
 *
 * Copyright 2007-2014 Adobe Systems Incorporated.
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

/****************************************************************************
 *
 * psintrp.h
 *
 *   Adobe's CFF Interpreter (specification).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psintrp.c, psintrp.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>The methods that build the mask of active hints, from <c>psintrp.c</c> (the data is in <c>pshints.h</c>).</summary>
internal sealed partial class Cf2HintMask
{
    /// <summary><c>cf2_hintmask_init</c>.</summary>
    public void Init(Cf2Error error)
    {
        Error = error;
        IsValid = false;
        IsNew = false;
        BitCount = 0;
        ByteCount = 0;
        Array.Clear(Mask);
    }

    private int SetCounts(int bitCount)
    {
        if (bitCount > MaxHints)
        {
            // total of h and v stems must be <= 96
            Error.Set(Cf2Error.InvalidGlyphFormat);
            return 0;
        }

        BitCount = bitCount;
        ByteCount = (BitCount + 7) / 8;

        IsValid = true;
        IsNew = true;

        return bitCount;
    }

    /// <summary>Consumes the hintmask bytes from the charstring, advancing the source pointer (<c>cf2_hintmask_read</c>).</summary>
    public void Read(Cf2Buffer charstring, int bitCount)
    {
        // initialize counts and isValid
        if (SetCounts(bitCount) == 0)
            return;

        // set mask and advance interpreter's charstring pointer
        for (int i = 0; i < ByteCount; i++)
            Mask[i] = (byte)charstring.ReadByte();
    }

    /// <summary><c>cf2_hintmask_setAll</c>.</summary>
    public void SetAll(int bitCount)
    {
        uint mask = (uint)((1 << (-bitCount & 7)) - 1);

        // initialize counts and isValid
        if (SetCounts(bitCount) == 0)
            return;

        // set mask to all ones
        for (int i = 0; i < ByteCount; i++)
            Mask[i] = 0xFF;

        // clear unused bits
        // bitCount -> mask, 0 -> 0, 1 -> 7f, 2 -> 3f, ... 6 -> 3, 7 -> 1
        Mask[ByteCount - 1] &= (byte)~mask;
    }
}

/// <summary>Adobe's Type 2 charstring interpreter (<c>cf2_interpT2CharString</c>), for CFF fonts.</summary>
/// <remarks>The parts for Type 1 and for CFF2 fonts are not ported: the engine is only used on CFF fonts here.</remarks>
internal static class Cf2Interpreter
{
    // Type2 charstring opcodes
    private const int CmdReserved0 = 0;
    private const int CmdHstem = 1;
    private const int CmdReserved2 = 2;
    private const int CmdVstem = 3;
    private const int CmdVmoveto = 4;
    private const int CmdRlineto = 5;
    private const int CmdHlineto = 6;
    private const int CmdVlineto = 7;
    private const int CmdRrcurveto = 8;
    private const int CmdClosepath = 9;
    private const int CmdCallsubr = 10;
    private const int CmdReturn = 11;
    private const int CmdEsc = 12;
    private const int CmdHsbw = 13;
    private const int CmdEndchar = 14;
    private const int CmdVsindex = 15;
    private const int CmdBlend = 16;
    private const int CmdReserved17 = 17;
    private const int CmdHstemhm = 18;
    private const int CmdHintmask = 19;
    private const int CmdCntrmask = 20;
    private const int CmdRmoveto = 21;
    private const int CmdHmoveto = 22;
    private const int CmdVstemhm = 23;
    private const int CmdRcurveline = 24;
    private const int CmdRlinecurve = 25;
    private const int CmdVvcurveto = 26;
    private const int CmdHhcurveto = 27;
    private const int CmdExtendednmbr = 28;
    private const int CmdCallgsubr = 29;
    private const int CmdVhcurveto = 30;
    private const int CmdHvcurveto = 31;

    private const int EscDotsection = 0;
    private const int EscVstem3 = 1;
    private const int EscHstem3 = 2;
    private const int EscAnd = 3;
    private const int EscOr = 4;
    private const int EscNot = 5;
    private const int EscSeac = 6;
    private const int EscSbw = 7;
    private const int EscReserved8 = 8;
    private const int EscAbs = 9;
    private const int EscAdd = 10;
    private const int EscSub = 11;
    private const int EscDiv = 12;
    private const int EscReserved13 = 13;
    private const int EscNeg = 14;
    private const int EscEq = 15;
    private const int EscCallothersubr = 16;
    private const int EscPop = 17;
    private const int EscDrop = 18;
    private const int EscReserved19 = 19;
    private const int EscPut = 20;
    private const int EscGet = 21;
    private const int EscIfelse = 22;
    private const int EscRandom = 23;
    private const int EscMul = 24;
    private const int EscReserved25 = 25;
    private const int EscSqrt = 26;
    private const int EscDup = 27;
    private const int EscExch = 28;
    private const int EscIndex = 29;
    private const int EscRoll = 30;
    private const int EscReserved31 = 31;
    private const int EscReserved32 = 32;
    private const int EscSetcurrentpt = 33;
    private const int EscHflex = 34;
    private const int EscFlex = 35;
    private const int EscHflex1 = 36;
    private const int EscFlex1 = 37;
    private const int EscReserved38 = 38;

    private const int OperandStackSize = 48;

    /// <summary>The maximum subroutine nesting (<c>CF2_MAX_SUBR</c>); only 10 are allowed but some fonts exceed it.</summary>
    private const int MaxSubr = 16;

    private const int StorageSize = 32;

    /// <summary>
    /// The most stem hints a charstring may declare. FreeType keeps every one (a glyph of more than 96 fails at its first hint mask or move,
    /// and a charstring of a megabyte of <c>hstem</c> operators would make hundreds of megabytes of them first); more than this is an error.
    /// </summary>
    private const int MaxStemHints = 4096;

    // `stemHintArray' does not change once we start drawing the outline.
    private static void DoStems(Cf2Font font, Cf2Stack opStack, Cf2ArrStack<Cf2StemHint> stemHintArray, ref int width, ref bool haveWidth, int hintOffset)
    {
        int count = opStack.Count;
        bool hasWidthArg = (count & 1) != 0;

        // variable accumulates delta values from operand stack
        int position = hintOffset;

        if (hasWidthArg && !haveWidth)
            width = unchecked(opStack.GetReal(0) + Cf2Fixed.FromInt(font.Decoder.CurrentSubfont.Private.NominalWidth));

        if (stemHintArray.Count > MaxStemHints)
            font.Error.Set(Cf2Error.InvalidGlyphFormat);

        for (int i = hasWidthArg ? 1 : 0; i < count && stemHintArray.Count <= MaxStemHints; i += 2)
        {
            // construct a CF2_StemHint and push it onto the list
            Cf2StemHint stemhint = default;

            stemhint.Min = position = unchecked(position + opStack.GetReal(i));
            stemhint.Max = position = unchecked(position + opStack.GetReal(i + 1));

            stemhint.Used = false;
            stemhint.MaxDs = stemhint.MinDs = 0;

            stemHintArray.Push(stemhint); // defer error check
        }

        opStack.Clear();

        // cf2_doStems must define a width (may be default)
        haveWidth = true;
    }

    private static void DoFlex(Cf2Stack opStack, ref int curX, ref int curY, Cf2GlyphPath glyphPath, ReadOnlySpan<bool> readFromStack, bool doConditionalLastRead)
    {
        Span<int> vals = stackalloc int[14];
        int idx;
        bool isHFlex;
        int top;

        vals[0] = curX;
        vals[1] = curY;
        idx = 0;
        isHFlex = !readFromStack[9];
        top = isHFlex ? 9 : 10;

        for (int i = 0; i < top; i++)
        {
            vals[i + 2] = vals[i];
            if (readFromStack[i])
                vals[i + 2] = unchecked(vals[i + 2] + opStack.GetReal(idx++));
        }

        if (isHFlex)
            vals[9 + 2] = curY;

        if (doConditionalLastRead)
        {
            bool lastIsX = Cf2Fixed.Abs(unchecked(vals[10] - curX)) > Cf2Fixed.Abs(unchecked(vals[11] - curY));
            int lastVal = opStack.GetReal(idx);

            if (lastIsX)
            {
                vals[12] = unchecked(vals[10] + lastVal);
                vals[13] = curY;
            }
            else
            {
                vals[12] = curX;
                vals[13] = unchecked(vals[11] + lastVal);
            }
        }
        else
        {
            if (readFromStack[10])
                vals[12] = unchecked(vals[10] + opStack.GetReal(idx++));
            else
                vals[12] = curX;

            if (readFromStack[11])
                vals[13] = unchecked(vals[11] + opStack.GetReal(idx));
            else
                vals[13] = curY;
        }

        for (int j = 0; j < 2; j++)
            glyphPath.CurveTo(vals[j * 6 + 2], vals[j * 6 + 3], vals[j * 6 + 4], vals[j * 6 + 5], vals[j * 6 + 6], vals[j * 6 + 7]);

        opStack.Clear();

        curX = vals[12];
        curY = vals[13];
    }

    // which of the twelve operands of each flex operator are on the stack
    private static readonly bool[] HflexOperands = [true, false, true, true, true, false, true, false, true, false, true, false];
    private static readonly bool[] FlexOperands = [true, true, true, true, true, true, true, true, true, true, true, true];
    private static readonly bool[] Hflex1Operands = [true, true, true, true, true, false, true, false, true, true, true, false];
    private static readonly bool[] Flex1Operands = [true, true, true, true, true, true, true, true, true, true, false, false];

    /// <summary>
    /// Runs a charstring and draws it to <paramref name="callbacks"/> (<c>cf2_interpT2CharString</c>).
    /// </summary>
    /// <remarks>
    /// <c>font.Error</c> is a shared error code used by many objects in this routine. Before the code continues from an error, it must
    /// check and record the error in it. The idea is that this shared error code will record the first error encountered. Unimplemented
    /// opcodes are ignored.
    /// </remarks>
    public static void Interpret(Cf2Font font, Cf2Buffer buf, Cf2OutlineCallbacks callbacks, FtVector translation, bool doingSeac,
        int curX, int curY, out int width)
    {
        // lastError is used for errors that are immediately tested
        int lastError = 0;

        // pointer to parsed font object
        Cf2Decoder decoder = font.Decoder;
        CffPrivate priv = decoder.CurrentSubfont.Private;

        Cf2Error error = font.Error;

        int scaleY = font.InnerTransform.D;
        int nominalWidthX = Cf2Fixed.FromInt(priv.NominalWidth);

        // save this for hinting seac accents
        int hintOriginY = curY;

        var storage = new int[StorageSize]; // for `put' and `get'

        // the instruction limit is the font's, shared by the charstring, its accent and the run again for the winding order (FreeType's
        // is a local of each call)

        var subrStack = new Cf2ArrStack<Cf2Buffer>(error);

        bool haveWidth;
        Cf2Buffer? charstring = null;

        int charstringIndex = -1; // initialize to empty

        // objects used for hinting
        var hStemHintArray = new Cf2ArrStack<Cf2StemHint>(error);
        var vStemHintArray = new Cf2ArrStack<Cf2StemHint>(error);

        var hintMask = new Cf2HintMask();
        Cf2HintMap? counterHintMap = null; // for the counter masks, made when the first is met and used again
        Cf2HintMask? counterMask = null;
        Cf2GlyphPath glyphPath;

        // initialize CF2_StemHint arrays
        hintMask.Init(error);

        // initialize path map to manage drawing operations
        //
        // Note: last 4 params are used to handle `MoveToPermissive', which may need to call `hintMap.Build'
        glyphPath = new Cf2GlyphPath(font, callbacks, scaleY, hStemHintArray, vStemHintArray, hintMask, hintOriginY, translation);

        // Initialize state for width parsing.  From the CFF Spec:
        //
        //   The first stack-clearing operator, which must be one of hstem, hstemhm, vstem, vstemhm, cntrmask, hintmask, hmoveto,
        //   vmoveto, rmoveto, or endchar, takes an additional argument - the width (as described earlier), which may be expressed as
        //   zero or one numeric argument.
        //
        // What we implement here uses the first validly specified width, but does not detect errors for specifying more than one width.
        //
        // If one of the above operators occurs without explicitly specifying a width, we assume the default width.
        haveWidth = false;
        width = Cf2Fixed.FromInt(priv.DefaultWidth);

        // allocate an operand stack
        var opStack = new Cf2Stack(error, OperandStackSize);

        // initialize subroutine stack by placing top level charstring as first element (max depth plus one for the charstring)
        // Note: Caller owns and must finalize the first charstring.  Our copy of it does not change that requirement.
        subrStack.SetCount(MaxSubr + 1);

        charstring = subrStack.GetRef(0);

        // catch errors so far
        if (error.Value != 0)
            goto Exit;

        charstring.CopyFrom(buf); // structure copy
        charstringIndex = 0; // entry is valid now

        // main interpreter loop
        while (true)
        {
            int op1; // first opcode byte

            if (charstring.IsEnd())
            {
                // If we've reached the end of the charstring, simulate a cf2_cmdRETURN or cf2_cmdENDCHAR.
                if (charstringIndex != 0)
                    op1 = CmdReturn; // end of buffer for subroutine
                else
                    op1 = CmdEndchar; // end of buffer for top level charstring
            }
            else
            {
                op1 = (byte)charstring.ReadByte();
            }

            // check for errors once per loop
            if (error.Value != 0)
                goto Exit;

            if (--font.InstructionsLeft == 0)
            {
                lastError = Cf2Error.InvalidGlyphFormat;
                goto Exit;
            }

            switch (op1)
            {
                case CmdReserved0:
                case CmdReserved2:
                case CmdReserved17:
                    // we may get here if we have a prior error
                    break;

                case CmdVsindex:
                case CmdBlend:
                    // CFF2 operators: not for a CFF font, so clear the stack and ignore
                    break;

                case CmdHstemhm:
                case CmdHstem:
                    // never add hints after the mask is computed
                    if (hintMask.IsValid)
                        break;

                    // add left-sidebearing correction in Type 1 mode
                    DoStems(font, opStack, hStemHintArray, ref width, ref haveWidth, 0);

                    break;

                case CmdVstemhm:
                case CmdVstem:
                    // never add hints after the mask is computed
                    if (hintMask.IsValid)
                        break;

                    DoStems(font, opStack, vStemHintArray, ref width, ref haveWidth, 0);

                    break;

                case CmdVmoveto:
                    if (opStack.Count > 1 && !haveWidth)
                        width = unchecked(opStack.GetReal(0) + nominalWidthX);

                    // width is defined or default after this
                    haveWidth = true;

                    curY = unchecked(curY + opStack.PopFixed());

                    glyphPath.MoveTo(curX, curY);

                    break;

                case CmdRlineto:
                {
                    int count = opStack.Count;

                    for (int idx = 0; idx < count; idx += 2)
                    {
                        curX = unchecked(curX + opStack.GetReal(idx + 0));
                        curY = unchecked(curY + opStack.GetReal(idx + 1));

                        glyphPath.LineTo(curX, curY);
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdHlineto:
                case CmdVlineto:
                {
                    int count = opStack.Count;

                    bool isX = op1 == CmdHlineto;

                    for (int idx = 0; idx < count; idx++)
                    {
                        int v = opStack.GetReal(idx);

                        if (isX)
                            curX = unchecked(curX + v);
                        else
                            curY = unchecked(curY + v);

                        isX = !isX;

                        glyphPath.LineTo(curX, curY);
                    }

                    opStack.Clear();
                    continue;
                }

                case CmdRcurveline:
                case CmdRrcurveto:
                {
                    int count = opStack.Count;
                    int idx = 0;

                    while (idx + 6 <= count)
                    {
                        int x1 = unchecked(opStack.GetReal(idx + 0) + curX);
                        int y1 = unchecked(opStack.GetReal(idx + 1) + curY);
                        int x2 = unchecked(opStack.GetReal(idx + 2) + x1);
                        int y2 = unchecked(opStack.GetReal(idx + 3) + y1);
                        int x3 = unchecked(opStack.GetReal(idx + 4) + x2);
                        int y3 = unchecked(opStack.GetReal(idx + 5) + y2);

                        glyphPath.CurveTo(x1, y1, x2, y2, x3, y3);

                        curX = x3;
                        curY = y3;
                        idx += 6;
                    }

                    if (op1 == CmdRcurveline)
                    {
                        curX = unchecked(curX + opStack.GetReal(idx + 0));
                        curY = unchecked(curY + opStack.GetReal(idx + 1));

                        glyphPath.LineTo(curX, curY);
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdClosepath:
                    // not a CFF operator
                    break;

                case CmdCallgsubr:
                case CmdCallsubr:
                {
                    if (charstringIndex > MaxSubr)
                    {
                        // max subr plus one for charstring
                        lastError = Cf2Error.InvalidGlyphFormat;
                        goto Exit; // overflow of stack
                    }

                    // push our current CFF charstring region on subrStack
                    charstring = subrStack.GetRef(charstringIndex + 1);

                    // set up the new CFF region and pointer
                    int subrNum = opStack.PopInt();

                    if (op1 == CmdCallgsubr)
                    {
                        if (decoder.InitGlobalRegionBuffer(subrNum, charstring))
                        {
                            lastError = Cf2Error.InvalidGlyphFormat;
                            goto Exit; // subroutine lookup or stream error
                        }
                    }
                    else
                    {
                        // cf2_cmdCALLSUBR
                        if (decoder.InitLocalRegionBuffer(subrNum, charstring))
                        {
                            lastError = Cf2Error.InvalidGlyphFormat;
                            goto Exit; // subroutine lookup or stream error
                        }
                    }

                    charstringIndex += 1; // entry is valid now
                    continue; // do not clear the stack
                }

                case CmdReturn:
                    if (charstringIndex < 1)
                    {
                        // Note: cannot return from top charstring
                        lastError = Cf2Error.InvalidGlyphFormat;
                        goto Exit; // underflow of stack
                    }

                    // restore position in previous charstring
                    charstring = subrStack.GetRef(--charstringIndex);
                    continue; // do not clear the stack

                case CmdEsc:
                {
                    int op2 = (byte)charstring.ReadByte();

                    // first switch for 2-byte operators handles CFF2 and opcodes that are reserved for both CFF and CFF2
                    switch (op2)
                    {
                        case EscHflex:
                            DoFlex(opStack, ref curX, ref curY, glyphPath, HflexOperands, false /* doConditionalLastRead */);
                            continue;

                        case EscFlex:
                            DoFlex(opStack, ref curX, ref curY, glyphPath, FlexOperands, false /* doConditionalLastRead */);
                            break; // TODO (in FreeType): why is this not a continue?

                        case EscHflex1:
                            DoFlex(opStack, ref curX, ref curY, glyphPath, Hflex1Operands, false /* doConditionalLastRead */);
                            continue;

                        case EscFlex1:
                            DoFlex(opStack, ref curX, ref curY, glyphPath, Flex1Operands, true /* doConditionalLastRead */);
                            continue;

                        // these opcodes are always reserved
                        case EscReserved8:
                        case EscReserved13:
                        case EscReserved19:
                        case EscReserved25:
                        case EscReserved31:
                        case EscReserved32:
                            break;

                        default:
                            if (op2 >= EscReserved38)
                                break;

                            // second switch for 2-byte operators handles CFF (and Type 1, which is not ported)
                            switch (op2)
                            {
                                case EscDotsection:
                                    // something about `flip type of locking' -- ignore it
                                    break;

                                case EscVstem3:
                                case EscHstem3:
                                    // Type 1 only
                                    break;

                                case EscAnd:
                                {
                                    int arg2 = opStack.PopFixed();
                                    int arg1 = opStack.PopFixed();

                                    opStack.PushInt(arg1 != 0 && arg2 != 0 ? 1 : 0);
                                    continue; // do not clear the stack
                                }

                                case EscOr:
                                {
                                    int arg2 = opStack.PopFixed();
                                    int arg1 = opStack.PopFixed();

                                    opStack.PushInt(arg1 != 0 || arg2 != 0 ? 1 : 0);
                                    continue; // do not clear the stack
                                }

                                case EscNot:
                                {
                                    int arg = opStack.PopFixed();

                                    opStack.PushInt(arg == 0 ? 1 : 0);
                                    continue; // do not clear the stack
                                }

                                case EscSeac:
                                case EscSbw:
                                    // Type 1 only
                                    break;

                                case EscAbs:
                                {
                                    int arg = opStack.PopFixed();

                                    if (arg < -Cf2Fixed.Max)
                                        opStack.PushFixed(Cf2Fixed.Max);
                                    else
                                        opStack.PushFixed(arg < 0 ? -arg : arg);

                                    continue; // do not clear the stack
                                }

                                case EscAdd:
                                {
                                    int summand2 = opStack.PopFixed();
                                    int summand1 = opStack.PopFixed();

                                    opStack.PushFixed(unchecked(summand1 + summand2));
                                    continue; // do not clear the stack
                                }

                                case EscSub:
                                {
                                    int subtrahend = opStack.PopFixed();
                                    int minuend = opStack.PopFixed();

                                    opStack.PushFixed(unchecked(minuend - subtrahend));
                                    continue; // do not clear the stack
                                }

                                case EscDiv:
                                {
                                    int divisor = opStack.PopFixed();
                                    int dividend = opStack.PopFixed();

                                    opStack.PushFixed(FtCalc.DivFix(dividend, divisor));
                                    continue; // do not clear the stack
                                }

                                case EscNeg:
                                {
                                    int arg = opStack.PopFixed();

                                    if (arg < -Cf2Fixed.Max)
                                        opStack.PushFixed(Cf2Fixed.Max);
                                    else
                                        opStack.PushFixed(-arg);

                                    continue; // do not clear the stack
                                }

                                case EscEq:
                                {
                                    int arg2 = opStack.PopFixed();
                                    int arg1 = opStack.PopFixed();

                                    opStack.PushInt(arg1 == arg2 ? 1 : 0);
                                    continue; // do not clear the stack
                                }

                                case EscCallothersubr:
                                    // Type 1 only; the stack is not cleared
                                    continue;

                                case EscPop:
                                    // Type 1 only; the stack is not cleared
                                    continue;

                                case EscDrop:
                                    _ = opStack.PopFixed();
                                    continue; // do not clear the stack

                                case EscPut:
                                {
                                    int idx = opStack.PopInt();
                                    int val = opStack.PopFixed();

                                    if ((uint)idx < StorageSize)
                                        storage[idx] = val;

                                    continue; // do not clear the stack
                                }

                                case EscGet:
                                {
                                    int idx = opStack.PopInt();

                                    if ((uint)idx < StorageSize)
                                        opStack.PushFixed(storage[idx]);

                                    continue; // do not clear the stack
                                }

                                case EscIfelse:
                                {
                                    int cond2 = opStack.PopFixed();
                                    int cond1 = opStack.PopFixed();
                                    int arg2 = opStack.PopFixed();
                                    int arg1 = opStack.PopFixed();

                                    opStack.PushFixed(cond1 <= cond2 ? arg1 : arg2);
                                    continue; // do not clear the stack
                                }

                                case EscRandom: // in spec
                                {
                                    // only use the lower 16 bits of `random' to generate a number in the range (0;1]
                                    int r = (int)((decoder.RandomState & 0xFFFF) + 1);

                                    decoder.RandomState = Random(decoder.RandomState);

                                    opStack.PushFixed(r);
                                    continue; // do not clear the stack
                                }

                                case EscMul:
                                {
                                    int factor2 = opStack.PopFixed();
                                    int factor1 = opStack.PopFixed();

                                    opStack.PushFixed(FtCalc.MulFix(factor1, factor2));
                                    continue; // do not clear the stack
                                }

                                case EscSqrt:
                                {
                                    int arg = opStack.PopFixed();
                                    if (arg > 0)
                                        arg = (int)FtCalc.SqrtFixed((uint)arg);
                                    else
                                        arg = 0;

                                    opStack.PushFixed(arg);
                                    continue; // do not clear the stack
                                }

                                case EscDup:
                                {
                                    int arg = opStack.PopFixed();

                                    opStack.PushFixed(arg);
                                    opStack.PushFixed(arg);
                                    continue; // do not clear the stack
                                }

                                case EscExch:
                                {
                                    int arg2 = opStack.PopFixed();
                                    int arg1 = opStack.PopFixed();

                                    opStack.PushFixed(arg2);
                                    opStack.PushFixed(arg1);
                                    continue; // do not clear the stack
                                }

                                case EscIndex:
                                {
                                    int idx = opStack.PopInt();
                                    int size = opStack.Count;

                                    if (size > 0)
                                    {
                                        // for `cf2_stack_getReal', index 0 is bottom of stack
                                        int grIdx;

                                        if (idx < 0)
                                            grIdx = size - 1;
                                        else if ((uint)idx >= (uint)size)
                                            grIdx = 0;
                                        else
                                            grIdx = size - 1 - idx;

                                        opStack.PushFixed(opStack.GetReal(grIdx));
                                    }

                                    continue; // do not clear the stack
                                }

                                case EscRoll:
                                {
                                    int idx = opStack.PopInt();
                                    int count = opStack.PopInt();

                                    opStack.Roll(count, idx);
                                    continue; // do not clear the stack
                                }

                                case EscSetcurrentpt:
                                    // Type 1 only
                                    break;
                            }

                            break;
                    }

                    break;
                } // case cf2_cmdESC

                case CmdHsbw:
                    // Type 1 only
                    break;

                case CmdEndchar:
                    if (opStack.Count == 1 || opStack.Count == 5)
                    {
                        if (!haveWidth)
                            width = unchecked(opStack.GetReal(0) + nominalWidthX);
                    }

                    // width is defined or default after this
                    haveWidth = true;

                    // close path if still open
                    glyphPath.CloseOpenPath();

                    // seac (charstring ending with args on stack)
                    if (opStack.Count > 1)
                    {
                        // must be either 4 or 5 -- this is a (deprecated) implied `seac' operator
                        Cf2Buffer component = new();
                        int error2;

                        if (doingSeac)
                        {
                            lastError = Cf2Error.InvalidGlyphFormat;
                            goto Exit; // nested seac
                        }

                        int achar = opStack.PopInt();
                        int bchar = opStack.PopInt();

                        curY = opStack.PopFixed();
                        curX = opStack.PopFixed();

                        error2 = decoder.GetSeacComponent(achar, component);
                        if (error2 != 0)
                        {
                            lastError = error2; // pass FreeType error through
                            goto Exit;
                        }

                        Interpret(font, component, callbacks, translation, true, curX, curY, out _);

                        error2 = decoder.GetSeacComponent(bchar, component);
                        if (error2 != 0)
                        {
                            lastError = error2; // pass FreeType error through
                            goto Exit;
                        }

                        Interpret(font, component, callbacks, translation, true, 0, 0, out _);
                    }

                    goto Exit;

                case CmdCntrmask:
                case CmdHintmask:
                    // never add hints after the mask is computed
                    if (opStack.Count > 1 && hintMask.IsValid)
                        break;

                    // if there are arguments on the stack, there this is an implied cf2_cmdVSTEMHM
                    DoStems(font, opStack, vStemHintArray, ref width, ref haveWidth, 0);

                    if (op1 == CmdHintmask)
                    {
                        // consume the hint mask bytes which follow the operator
                        hintMask.Read(charstring, hStemHintArray.Count + vStemHintArray.Count);
                    }
                    else
                    {
                        // Consume the counter mask bytes which follow the operator: Build a temporary hint map, just to place and lock
                        // those stems participating in the counter mask.  These are most likely the dominant hstems, and are grouped
                        // together in a few counter groups, not necessarily in correspondence with the hint groups.  This reduces the
                        // chances of conflicts between hstems that are initially placed in separate hint groups and then brought
                        // together.  The positions are copied back to `hStemHintArray', so we can discard `counterMask' and
                        // `counterHintMap'.
                        counterHintMap ??= new Cf2HintMap();
                        counterMask ??= new Cf2HintMask();

                        counterHintMap.Init(font, glyphPath.InitialHintMap, glyphPath.HintMoves, scaleY);
                        counterMask.Init(error);

                        counterMask.Read(charstring, hStemHintArray.Count + vStemHintArray.Count);
                        counterHintMap.Build(hStemHintArray, vStemHintArray, counterMask, 0, false);
                    }

                    break;

                case CmdRmoveto:
                    if (opStack.Count > 2 && !haveWidth)
                        width = unchecked(opStack.GetReal(0) + nominalWidthX);

                    // width is defined or default after this
                    haveWidth = true;

                    curY = unchecked(curY + opStack.PopFixed());
                    curX = unchecked(curX + opStack.PopFixed());

                    glyphPath.MoveTo(curX, curY);

                    break;

                case CmdHmoveto:
                    if (opStack.Count > 1 && !haveWidth)
                        width = unchecked(opStack.GetReal(0) + nominalWidthX);

                    // width is defined or default after this
                    haveWidth = true;

                    curX = unchecked(curX + opStack.PopFixed());

                    glyphPath.MoveTo(curX, curY);

                    break;

                case CmdRlinecurve:
                {
                    int count = opStack.Count;
                    int idx = 0;

                    while (idx + 6 < count)
                    {
                        curX = unchecked(curX + opStack.GetReal(idx + 0));
                        curY = unchecked(curY + opStack.GetReal(idx + 1));

                        glyphPath.LineTo(curX, curY);
                        idx += 2;
                    }

                    while (idx < count)
                    {
                        int x1 = unchecked(opStack.GetReal(idx + 0) + curX);
                        int y1 = unchecked(opStack.GetReal(idx + 1) + curY);
                        int x2 = unchecked(opStack.GetReal(idx + 2) + x1);
                        int y2 = unchecked(opStack.GetReal(idx + 3) + y1);
                        int x3 = unchecked(opStack.GetReal(idx + 4) + x2);
                        int y3 = unchecked(opStack.GetReal(idx + 5) + y2);

                        glyphPath.CurveTo(x1, y1, x2, y2, x3, y3);

                        curX = x3;
                        curY = y3;
                        idx += 6;
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdVvcurveto:
                {
                    int count1 = opStack.Count;
                    int idx = 0;

                    // if `cf2_stack_count' isn't of the form 4n or 4n+1, we enforce it by clearing the second bit (and sorting the
                    // stack indexing to suit)
                    int count = (int)((uint)count1 & ~2U);
                    idx += count1 - count;

                    while (idx < count)
                    {
                        int x1, y1, x2, y2, x3, y3;

                        if (((count - idx) & 1) != 0)
                        {
                            x1 = unchecked(opStack.GetReal(idx) + curX);

                            idx++;
                        }
                        else
                        {
                            x1 = curX;
                        }

                        y1 = unchecked(opStack.GetReal(idx + 0) + curY);
                        x2 = unchecked(opStack.GetReal(idx + 1) + x1);
                        y2 = unchecked(opStack.GetReal(idx + 2) + y1);
                        x3 = x2;
                        y3 = unchecked(opStack.GetReal(idx + 3) + y2);

                        glyphPath.CurveTo(x1, y1, x2, y2, x3, y3);

                        curX = x3;
                        curY = y3;
                        idx += 4;
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdHhcurveto:
                {
                    int count1 = opStack.Count;
                    int idx = 0;

                    // if `cf2_stack_count' isn't of the form 4n or 4n+1, we enforce it by clearing the second bit (and sorting the
                    // stack indexing to suit)
                    int count = (int)((uint)count1 & ~2U);
                    idx += count1 - count;

                    while (idx < count)
                    {
                        int x1, y1, x2, y2, x3, y3;

                        if (((count - idx) & 1) != 0)
                        {
                            y1 = unchecked(opStack.GetReal(idx) + curY);

                            idx++;
                        }
                        else
                        {
                            y1 = curY;
                        }

                        x1 = unchecked(opStack.GetReal(idx + 0) + curX);
                        x2 = unchecked(opStack.GetReal(idx + 1) + x1);
                        y2 = unchecked(opStack.GetReal(idx + 2) + y1);
                        x3 = unchecked(opStack.GetReal(idx + 3) + x2);
                        y3 = y2;

                        glyphPath.CurveTo(x1, y1, x2, y2, x3, y3);

                        curX = x3;
                        curY = y3;
                        idx += 4;
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdVhcurveto:
                case CmdHvcurveto:
                {
                    int count1 = opStack.Count;
                    int idx = 0;

                    bool alternate = op1 == CmdHvcurveto;

                    // if `cf2_stack_count' isn't of the form 8n, 8n+1, 8n+4, or 8n+5, we enforce it by clearing the second bit (and
                    // sorting the stack indexing to suit)
                    int count = (int)((uint)count1 & ~2U);
                    idx += count1 - count;

                    while (idx < count)
                    {
                        int x1, x2, x3, y1, y2, y3;

                        if (alternate)
                        {
                            x1 = unchecked(opStack.GetReal(idx + 0) + curX);
                            y1 = curY;
                            x2 = unchecked(opStack.GetReal(idx + 1) + x1);
                            y2 = unchecked(opStack.GetReal(idx + 2) + y1);
                            y3 = unchecked(opStack.GetReal(idx + 3) + y2);

                            if (count - idx == 5)
                            {
                                x3 = unchecked(opStack.GetReal(idx + 4) + x2);

                                idx++;
                            }
                            else
                            {
                                x3 = x2;
                            }

                            alternate = false;
                        }
                        else
                        {
                            x1 = curX;
                            y1 = unchecked(opStack.GetReal(idx + 0) + curY);
                            x2 = unchecked(opStack.GetReal(idx + 1) + x1);
                            y2 = unchecked(opStack.GetReal(idx + 2) + y1);
                            x3 = unchecked(opStack.GetReal(idx + 3) + x2);

                            if (count - idx == 5)
                            {
                                y3 = unchecked(opStack.GetReal(idx + 4) + y2);

                                idx++;
                            }
                            else
                            {
                                y3 = y2;
                            }

                            alternate = true;
                        }

                        glyphPath.CurveTo(x1, y1, x2, y2, x3, y3);

                        curX = x3;
                        curY = y3;
                        idx += 4;
                    }

                    opStack.Clear();
                    continue; // no need to clear stack again
                }

                case CmdExtendednmbr:
                {
                    int byte1 = charstring.ReadByte();
                    int byte2 = charstring.ReadByte();

                    int v = (short)((byte1 << 8) | byte2);

                    opStack.PushInt(v);
                    continue;
                }

                default:
                    // numbers
                    if (op1 <= 246)
                    {
                        // -107 .. 107
                        opStack.PushInt(op1 - 139);
                    }
                    else if (op1 <= 250)
                    {
                        int v = op1;
                        v -= 247;
                        v *= 256;
                        v += charstring.ReadByte();
                        v += 108;

                        // 108 .. 1131
                        opStack.PushInt(v);
                    }
                    else if (op1 <= 254)
                    {
                        int v = op1;
                        v -= 251;
                        v *= 256;
                        v += charstring.ReadByte();
                        v = -v - 108;

                        // -1131 .. -108
                        opStack.PushInt(v);
                    }
                    else
                    {
                        // op1 == 255
                        uint byte1 = (uint)charstring.ReadByte();
                        uint byte2 = (uint)charstring.ReadByte();
                        uint byte3 = (uint)charstring.ReadByte();
                        uint byte4 = (uint)charstring.ReadByte();

                        int v = unchecked((int)((byte1 << 24) | (byte2 << 16) | (byte3 << 8) | byte4));

                        opStack.PushFixed(v);
                    }

                    continue; // don't clear stack
            } // end of switch statement checking `op1'

            opStack.Clear();
        } // end of main interpreter loop

    Exit:
        // check whether last error seen is also the first one
        error.Set(lastError);
    }

    // a 32bit version of the `xorshift' algorithm (cff_random)
    private static uint Random(uint r)
    {
        r ^= r << 13;
        r ^= r >> 17;
        r ^= r << 5;

        return r;
    }
}
