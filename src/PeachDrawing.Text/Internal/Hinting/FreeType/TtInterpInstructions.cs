/****************************************************************************
 *
 * ttinterp.c
 *
 *   TrueType bytecode interpreter (body).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttinterp.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

// The instruction handlers of the interpreter (the Ins_* functions of ttinterp.c). Each takes the index of its first
// argument on the stack where FreeType takes a pointer to it (`args'), so `args[1]' is Stack[a + 1] and `args[-L]' is
// Stack[a - L]. The handlers that have no argument of their own to say are the ones FreeType calls with the context alone.
internal sealed partial class TtExecContext
{
    // Two simple bounds-checking helpers: BOUNDS( x, n ) and BOUNDSL( x, n ) of ttinterp.c.
    private static bool Bounds(int x, int n) => (uint)x >= (uint)n;

    private static bool BoundsL(int x, int n) => (uint)x >= (uint)n;

    // ---------------------------------------------------------------------------------------------------------------
    //                                       MANAGING THE STACK AND ARITHMETIC
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>MPS[]: Measure Point Size. Opcode 0x4C.</summary>
    private void InsMps(int a)
    {
        if (Version == TtInterpreterVersion.V35)
        {
            // Microsoft's GDI bytecode interpreter always returns value 12; we return the current PPEM value instead.
            Stack[a] = CurrentPpem();
        }
        else
        {
            // A possible practical application of the MPS instruction is to implement optical scaling and similar
            // features, which should be based on perceptual attributes, thus independent of the resolution.
            Stack[a] = PointSize;
        }
    }

    /// <summary>DIV[]: DIVide. Opcode 0x62.</summary>
    private void InsDiv(int a)
    {
        if (Stack[a + 1] == 0)
            Error = TtError.DivideByZero;
        else
            Stack[a] = FtCalc.MulDivNoRound(Stack[a], 64, Stack[a + 1]);
    }

    /// <summary>RS[]: Read Store. Opcode 0x43.</summary>
    private void InsRs(int a)
    {
        int i = Stack[a];

        if (BoundsL(i, StoreSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            else
                Stack[a] = 0;
        }
        else
        {
            Stack[a] = Storage[i];
        }
    }

    /// <summary>WS[]: Write Store. Opcode 0x42.</summary>
    private void InsWs(int a)
    {
        int i = Stack[a];

        if (BoundsL(i, StoreSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
        }
        else
        {
            if (IniRange == TtCodeRange.Glyph && !_storageIsGlyfCopy)
            {
                if (_glyfStorage.Length < StoreSize)
                    _glyfStorage = new int[StoreSize];

                Array.Copy(Storage, _glyfStorage, StoreSize);
                Storage = _glyfStorage;
                _storageIsGlyfCopy = true;
            }

            Storage[i] = Stack[a + 1];
        }
    }

    /// <summary>WCVTP[]: Write CVT in Pixel units. Opcode 0x44.</summary>
    private void InsWcvtp(int a)
    {
        int i = Stack[a];

        if (BoundsL(i, CvtSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
        }
        else
        {
            WriteCvt(i, Stack[a + 1]);
        }
    }

    /// <summary>WCVTF[]: Write CVT in Funits. Opcode 0x70.</summary>
    private void InsWcvtf(int a)
    {
        int i = Stack[a];

        if (BoundsL(i, CvtSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
        }
        else
        {
            // FreeType writes `exc->cvt[I]' directly here, without the copy-on-write check of the other writers, so a glyph
            // program's WCVTF leaks into the size for the next glyph. The copy is made here as well: the size's cvt is
            // shared between threads and must not change.
            WriteCvt(i, FtCalc.MulFix(Stack[a + 1], Metrics.Scale));
        }
    }

    /// <summary>RCVT[]: Read CVT. Opcode 0x45.</summary>
    private void InsRcvt(int a)
    {
        int i = Stack[a];

        if (BoundsL(i, CvtSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            else
                Stack[a] = 0;
        }
        else
        {
            Stack[a] = ReadCvt(i);
        }
    }

    /// <summary>MINDEX[]: Move INDEXed element. Opcode 0x26.</summary>
    private void InsMindex(int a)
    {
        int l = Stack[a];

        if (l <= 0 || l > _args)
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
        }
        else
        {
            int k = Stack[a - l];

            if (ChargeWork(l))
                return;

            // FT_ARRAY_MOVE( args - L, args - L + 1, L - 1 )
            Array.Copy(Stack, a - l + 1, Stack, a - l, l - 1);

            Stack[a - 1] = k;
        }
    }

    /// <summary>CINDEX[]: Copy INDEXed element. Opcode 0x25.</summary>
    private void InsCindex(int a)
    {
        int l = Stack[a];

        if (l <= 0 || l > _args)
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            Stack[a] = 0;
        }
        else
        {
            Stack[a] = Stack[a - l];
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                       MANAGING THE FLOW OF CONTROL
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>SLOOP[]: Set LOOP variable. Opcode 0x17.</summary>
    private void InsSloop(int a)
    {
        if (Stack[a] < 0)
        {
            Error = TtError.BadArgument;
        }
        else
        {
            // we heuristically limit the number of loops to 16 bits
            GS.Loop = Stack[a] > 0xFFFF ? 0xFFFF : Stack[a];
        }
    }

    // Returns true on failure (SUCCESS is 0 and FAILURE is 1 in FreeType).
    private bool SkipCode()
    {
        // skipping is work too: a program can skip a long stretch of code over and over
        if (ChargeWork(1))
            return true;

        _ip += _length;

        if (_ip < _codeSize)
        {
            _opcode = _code[_ip];

            _length = OpcodeLength[_opcode];
            if (_length < 0)
            {
                if (_ip + 1 >= _codeSize)
                {
                    Error = TtError.CodeOverflow;
                    return true;
                }

                _length = 2 - _length * _code[_ip + 1];
            }

            return false;
        }

        Error = TtError.CodeOverflow;
        return true;
    }

    /// <summary>IF[]: IF test. Opcode 0x58.</summary>
    private void InsIf(int a)
    {
        if (Stack[a] != 0)
            return;

        int nIfs = 1;
        bool outOfIf = false;

        do
        {
            if (SkipCode())
                return;

            switch (_opcode)
            {
                case 0x58: // IF
                    nIfs++;
                    break;

                case 0x1B: // ELSE
                    outOfIf = nIfs == 1;
                    break;

                case 0x59: // EIF
                    nIfs--;
                    outOfIf = nIfs == 0;
                    break;
            }
        }
        while (!outOfIf);
    }

    /// <summary>ELSE[]: ELSE. Opcode 0x1B.</summary>
    private void InsElse()
    {
        int nIfs = 1;

        do
        {
            if (SkipCode())
                return;

            switch (_opcode)
            {
                case 0x58: // IF
                    nIfs++;
                    break;

                case 0x59: // EIF
                    nIfs--;
                    break;
            }
        }
        while (nIfs != 0);
    }

    /// <summary>JMPR[]: JuMP Relative. Opcode 0x1C.</summary>
    private void InsJmpr(int a)
    {
        if (Stack[a] == 0 && _args == 0)
        {
            Error = TtError.BadArgument;
            return;
        }

        _ip = unchecked(_ip + Stack[a]);
        if (_ip < 0 ||
            (_callTop > 0 && _ip > DefOf(_callStack[_callTop - 1]).End))
        {
            Error = TtError.BadArgument;
            return;
        }

        _length = 0;

        if (Stack[a] < 0)
        {
            if (++_negJumpCounter > _negJumpCounterMax)
                Error = TtError.ExecutionTooLong;
        }
    }

    private ref TtDefRecord DefOf(in TtCallRecord call) =>
        ref call.DefIsInstruction ? ref IDefs[call.DefIndex] : ref FDefs[call.DefIndex];

    // ---------------------------------------------------------------------------------------------------------------
    //                                  DEFINING AND USING FUNCTIONS AND INSTRUCTIONS
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>FDEF[]: Function DEFinition. Opcode 0x2C.</summary>
    private void InsFdef(int a)
    {
        // FDEF is only allowed in `prep' or `fpgm'
        if (IniRange == TtCodeRange.Glyph)
        {
            Error = TtError.DefInGlyfBytecode;
            return;
        }

        // some font programs are broken enough to redefine functions! We will then parse the current table.
        uint n = (uint)Stack[a];
        int rec = 0;

        for (; rec < NumFDefs; rec++)
        {
            if (FDefs[rec].Opc == n)
                break;
        }

        if (rec == NumFDefs)
        {
            // check that there is enough room for new functions
            if (NumFDefs >= MaxFDefs)
            {
                Error = TtError.TooManyFunctionDefs;
                return;
            }

            NumFDefs++;
        }

        // Although FDEF takes unsigned 32-bit integer, func # must be within unsigned 16-bit integer
        if (n > 0xFFFFU)
        {
            Error = TtError.TooManyFunctionDefs;
            return;
        }

        FDefs[rec].Range = CurRange;
        FDefs[rec].Opc = (ushort)n;
        FDefs[rec].Start = _ip + 1;
        FDefs[rec].Active = true;

        if (n > MaxFunc)
            MaxFunc = (ushort)n;

        // Now skip the whole function definition. We don't allow nested IDEFS & FDEFs.
        while (!SkipCode())
        {
            switch (_opcode)
            {
                case 0x89: // IDEF
                case 0x2C: // FDEF
                    Error = TtError.NestedDefs;
                    return;

                case 0x2D: // ENDF
                    FDefs[rec].End = _ip;
                    return;
            }
        }
    }

    /// <summary>ENDF[]: END Function definition. Opcode 0x2D.</summary>
    private void InsEndf()
    {
        if (_callTop <= 0) // We encountered an ENDF without a call
        {
            Error = TtError.EndfInExecStream;
            return;
        }

        _callTop--;

        ref TtCallRecord rec = ref _callStack[_callTop];

        rec.CurCount--;

        if (rec.CurCount > 0)
        {
            _callTop++;
            _ip = DefOf(rec).Start;
            _length = 0;
        }
        else
        {
            // Loop through the current function
            GotoCodeRange(rec.CallerRange, rec.CallerIp);
        }

        // Exit the current call frame.

        // NOTE: If the last instruction of a program is a CALL or LOOPCALL, the return address is always out of the code
        //       range. This is a valid address, and it is why we do not test the result of Ins_Goto_CodeRange() here!
    }

    // Finds the definition of function f (the lookup shared by CALL and LOOPCALL); returns its index or -1.
    private int FindFunction(uint f)
    {
        // Except for some old Apple fonts, all functions in a TrueType font are defined in increasing order, starting from
        // 0. This means that we normally have
        //
        //    exc->maxFunc+1 == exc->numFDefs
        //    exc->FDefs[n].opc == n for n in 0..exc->maxFunc
        //
        // If this isn't true, we need to look up the function table.
        if (MaxFunc + 1 == NumFDefs && f < (uint)FDefs.Length && FDefs[f].Opc == f)
            return (int)f;

        // look up the FDefs table
        for (int i = 0; i < NumFDefs; i++)
        {
            if (FDefs[i].Opc == f)
                return i;
        }

        return -1;
    }

    /// <summary>CALL[]: CALL function. Opcode 0x2B.</summary>
    private void InsCall(int a)
    {
        // first of all, check the index
        uint f = (uint)Stack[a];
        if (f >= MaxFunc + 1)
        {
            Error = TtError.InvalidReference;
            return;
        }

        if (FDefs.Length == 0)
        {
            Error = TtError.InvalidReference;
            return;
        }

        int def = FindFunction(f);
        if (def < 0)
        {
            Error = TtError.InvalidReference;
            return;
        }

        // check that the function is active
        if (!FDefs[def].Active)
        {
            Error = TtError.InvalidReference;
            return;
        }

        // check the call stack
        if (_callTop >= CallStackSize)
        {
            Error = TtError.StackOverflow;
            return;
        }

        ref TtCallRecord pCrec = ref _callStack[_callTop];

        pCrec.CallerRange = CurRange;
        pCrec.CallerIp = _ip + 1;
        pCrec.CurCount = 1;
        pCrec.DefIsInstruction = false;
        pCrec.DefIndex = def;

        _callTop++;

        GotoCodeRange(FDefs[def].Range, FDefs[def].Start);
    }

    /// <summary>LOOPCALL[]: LOOP and CALL function. Opcode 0x2A.</summary>
    private void InsLoopcall(int a)
    {
        // first of all, check the index
        uint f = (uint)Stack[a + 1];
        if (f >= MaxFunc + 1)
        {
            Error = TtError.InvalidReference;
            return;
        }

        int def = FindFunction(f);
        if (def < 0)
        {
            Error = TtError.InvalidReference;
            return;
        }

        // check that the function is active
        if (!FDefs[def].Active)
        {
            Error = TtError.InvalidReference;
            return;
        }

        // check stack
        if (_callTop >= CallStackSize)
        {
            Error = TtError.StackOverflow;
            return;
        }

        if (Stack[a] > 0)
        {
            ref TtCallRecord pCrec = ref _callStack[_callTop];

            pCrec.CallerRange = CurRange;
            pCrec.CallerIp = _ip + 1;
            pCrec.CurCount = Stack[a];
            pCrec.DefIsInstruction = false;
            pCrec.DefIndex = def;

            _callTop++;

            GotoCodeRange(FDefs[def].Range, FDefs[def].Start);

            _loopcallCounter += (uint)Stack[a];
            if (_loopcallCounter > _loopcallCounterMax)
                Error = TtError.ExecutionTooLong;

            ChargeWork(Stack[a]);
        }
    }

    /// <summary>IDEF[]: Instruction DEFinition. Opcode 0x89.</summary>
    private void InsIdef(int a)
    {
        // we enable IDEF only in `prep' or `fpgm'
        if (IniRange == TtCodeRange.Glyph)
        {
            Error = TtError.DefInGlyfBytecode;
            return;
        }

        // First of all, look for the same function in our table
        int def = 0;
        for (; def < NumIDefs; def++)
        {
            if (IDefs[def].Opc == (uint)Stack[a])
                break;
        }

        if (def == NumIDefs)
        {
            // check that there is enough room for a new instruction
            if (NumIDefs >= MaxIDefs)
            {
                Error = TtError.TooManyInstructionDefs;
                return;
            }

            NumIDefs++;
        }

        // opcode must be unsigned 8-bit integer
        if (0 > Stack[a] || Stack[a] > 0x00FF)
        {
            Error = TtError.TooManyInstructionDefs;
            return;
        }

        IDefs[def].Opc = (byte)Stack[a];
        IDefs[def].Start = _ip + 1;
        IDefs[def].Range = CurRange;
        IDefs[def].Active = true;

        if ((uint)Stack[a] > MaxIns)
            MaxIns = (byte)Stack[a];

        // Now skip the whole function definition. We don't allow nested IDEFs & FDEFs.
        while (!SkipCode())
        {
            switch (_opcode)
            {
                case 0x89: // IDEF
                case 0x2C: // FDEF
                    Error = TtError.NestedDefs;
                    return;

                case 0x2D: // ENDF
                    IDefs[def].End = _ip;
                    return;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                       PUSHING DATA ONTO THE INTERPRETER STACK
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>NPUSHB[]: PUSH N Bytes. Opcode 0x40.</summary>
    private void InsNpushb(int a)
    {
        int ip = _ip;

        if (++ip >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        int l = _code[ip];

        if (ip + l >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        if (Bounds(l, StackSize + 1 - Top))
        {
            Error = TtError.StackOverflow;
            return;
        }

        for (int k = 0; k < l; k++)
            Stack[a + k] = _code[++ip];

        NewTop += l;
        _ip = ip;
    }

    /// <summary>NPUSHW[]: PUSH N Words. Opcode 0x41.</summary>
    private void InsNpushw(int a)
    {
        int ip = _ip;

        if (++ip >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        int l = _code[ip];

        if (ip + 2 * l >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        if (Bounds(l, StackSize + 1 - Top))
        {
            Error = TtError.StackOverflow;
            return;
        }

        // note casting for sign-extension
        for (int k = 0; k < l; k++, ip += 2)
            Stack[a + k] = (short)((_code[ip + 1] << 8) | _code[ip + 2]);

        NewTop += l;
        _ip = ip;
    }

    /// <summary>PUSHB[abc]: PUSH Bytes. Opcodes 0xB0-0xB7.</summary>
    private void InsPushb(int a)
    {
        int ip = _ip;

        int l = _opcode - 0xB0 + 1;

        if (ip + l >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        if (Bounds(l, StackSize + 1 - Top))
        {
            Error = TtError.StackOverflow;
            return;
        }

        for (int k = 0; k < l; k++)
            Stack[a + k] = _code[++ip];

        _ip = ip;
    }

    /// <summary>PUSHW[abc]: PUSH Words. Opcodes 0xB8-0xBF.</summary>
    private void InsPushw(int a)
    {
        int ip = _ip;

        int l = _opcode - 0xB8 + 1;

        if (ip + 2 * l >= _codeSize)
        {
            Error = TtError.CodeOverflow;
            return;
        }

        if (Bounds(l, StackSize + 1 - Top))
        {
            Error = TtError.StackOverflow;
            return;
        }

        // note casting for sign-extension
        for (int k = 0; k < l; k++, ip += 2)
            Stack[a + k] = (short)((_code[ip + 1] << 8) | _code[ip + 2]);

        _ip = ip;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                          MANAGING THE GRAPHICS STATE
    // ---------------------------------------------------------------------------------------------------------------

    // Ins_SxVTL: computes the vector of a line between two points; returns true on failure.
    private bool SxVtl(int aIdx1, int aIdx2, ref short vecX, ref short vecY)
    {
        byte opcode = _opcode;

        if (Bounds(aIdx1, Zp2.NPoints) || Bounds(aIdx2, Zp1.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return true;
        }

        int a = unchecked(Zp1.CurX[aIdx2] - Zp2.CurX[aIdx1]);
        int b = unchecked(Zp1.CurY[aIdx2] - Zp2.CurY[aIdx1]);

        // If p1 == p2, SPvTL and SFvTL behave the same as SPvTCA[X] and SFvTCA[X], respectively.
        //
        // Confirmed by Greg Hitchcock.
        if (a == 0 && b == 0)
        {
            a = 0x4000;
            opcode = 0;
        }

        if ((opcode & 1) != 0)
        {
            int c = b; // counter-clockwise rotation
            b = a;
            a = unchecked(-c);
        }

        Normalize(a, b, ref vecX, ref vecY);

        return false;
    }

    /// <summary>SVTCA[a], SPvTCA[a], SFvTCA[a]: Set (F and P) Vectors to Coordinate Axis. Opcodes 0x00-0x05.</summary>
    private void InsSxyTca()
    {
        byte opcode = _opcode;

        short aa = (short)((opcode & 1) << 14);
        short bb = (short)(aa ^ 0x4000);

        if (opcode < 4)
        {
            GS.ProjX = aa;
            GS.ProjY = bb;

            GS.DualX = aa;
            GS.DualY = bb;
        }

        if ((opcode & 2) == 0)
        {
            GS.FreeX = aa;
            GS.FreeY = bb;
        }

        ComputeFuncs();
    }

    /// <summary>SPvTL[a]: Set PVector To Line. Opcodes 0x06-0x07.</summary>
    private void InsSpvtl(int a)
    {
        if (!SxVtl((ushort)Stack[a + 1], (ushort)Stack[a], ref GS.ProjX, ref GS.ProjY))
        {
            GS.DualX = GS.ProjX;
            GS.DualY = GS.ProjY;
            ComputeFuncs();
        }
    }

    /// <summary>SFvTL[a]: Set FVector To Line. Opcodes 0x08-0x09.</summary>
    private void InsSfvtl(int a)
    {
        if (!SxVtl((ushort)Stack[a + 1], (ushort)Stack[a], ref GS.FreeX, ref GS.FreeY))
            ComputeFuncs();
    }

    /// <summary>SFvTPv[]: Set FVector To PVector. Opcode 0x0E.</summary>
    private void InsSfvtpv()
    {
        GS.FreeX = GS.ProjX;
        GS.FreeY = GS.ProjY;
        ComputeFuncs();
    }

    /// <summary>SPvFS[]: Set PVector From Stack. Opcode 0x0A.</summary>
    private void InsSpvfs(int a)
    {
        // Only use low 16bits, then sign extend
        int y = (short)Stack[a + 1];
        int x = (short)Stack[a];

        Normalize(x, y, ref GS.ProjX, ref GS.ProjY);

        GS.DualX = GS.ProjX;
        GS.DualY = GS.ProjY;
        ComputeFuncs();
    }

    /// <summary>SFvFS[]: Set FVector From Stack. Opcode 0x0B.</summary>
    private void InsSfvfs(int a)
    {
        // Only use low 16bits, then sign extend
        int y = (short)Stack[a + 1];
        int x = (short)Stack[a];

        Normalize(x, y, ref GS.FreeX, ref GS.FreeY);
        ComputeFuncs();
    }

    /// <summary>GPv[]: Get Projection Vector. Opcode 0x0C.</summary>
    private void InsGpv(int a)
    {
        Stack[a] = GS.ProjX;
        Stack[a + 1] = GS.ProjY;
    }

    /// <summary>GFv[]: Get Freedom Vector. Opcode 0x0D.</summary>
    private void InsGfv(int a)
    {
        Stack[a] = GS.FreeX;
        Stack[a + 1] = GS.FreeY;
    }

    /// <summary>SDS[]: Set Delta Shift. Opcode 0x5F.</summary>
    private void InsSds(int a)
    {
        if ((uint)Stack[a] > 6U)
            Error = TtError.BadArgument;
        else
            GS.DeltaShift = (ushort)Stack[a];
    }

    /// <summary>RTHG[]: Round To Half Grid. Opcode 0x19.</summary>
    private void InsRthg()
    {
        GS.RoundState = TtRound.ToHalfGrid;
        _roundFunc = TtRound.ToHalfGrid;
    }

    /// <summary>RTG[]: Round To Grid. Opcode 0x18.</summary>
    private void InsRtg()
    {
        GS.RoundState = TtRound.ToGrid;
        _roundFunc = TtRound.ToGrid;
    }

    /// <summary>RTDG[]: Round To Double Grid. Opcode 0x3D.</summary>
    private void InsRtdg()
    {
        GS.RoundState = TtRound.ToDoubleGrid;
        _roundFunc = TtRound.ToDoubleGrid;
    }

    /// <summary>GC[a]: Get Coordinate projected onto. Opcodes 0x46-0x47. Measures from the original glyph must be taken along the dual projection vector.</summary>
    private void InsGc(int a)
    {
        int l = Stack[a];
        int r;

        if (BoundsL(l, Zp2.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            r = 0;
        }
        else
        {
            if ((_opcode & 1) != 0)
                r = DualProject(Zp2.OrgX[l], Zp2.OrgY[l]);
            else
                r = Project(Zp2.CurX[l], Zp2.CurY[l]);
        }

        Stack[a] = r;
    }

    /// <summary>SCFS[]: Set Coordinate From Stack. Opcode 0x48. <c>OA := OA + ( value - OA.p )/( f.p ) * f</c>.</summary>
    private void InsScfs(int a)
    {
        ushort l = (ushort)Stack[a];

        if (Bounds(l, Zp2.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        int k = Project(Zp2.CurX[l], Zp2.CurY[l]);

        Move(Zp2, l, unchecked(Stack[a + 1] - k));

        // UNDOCUMENTED! The MS rasterizer does that with twilight points (confirmed by Greg Hitchcock)
        if (GS.Gep2 == 0)
        {
            Zp2.OrgX[l] = Zp2.CurX[l];
            Zp2.OrgY[l] = Zp2.CurY[l];
        }
    }

    /// <summary>
    /// MD[a]: Measure Distance. Opcodes 0x49-0x4A. UNDOCUMENTED: measure taken in the original glyph must be along the
    /// dual projection vector; the flag attributes are inverted (0 measures the original outline, 1 the grid-fitted one);
    /// and `zp0 - zp1', and not `zp2 - zp1'.
    /// </summary>
    private void InsMd(int a)
    {
        ushort k = (ushort)Stack[a + 1];
        ushort l = (ushort)Stack[a];
        int d;

        if (Bounds(l, Zp0.NPoints) || Bounds(k, Zp1.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            d = 0;
        }
        else
        {
            if ((_opcode & 1) != 0)
            {
                d = ProjectPoints(Zp0.CurX, Zp0.CurY, l, Zp1.CurX, Zp1.CurY, k);
            }
            else
            {
                // XXX: UNDOCUMENTED: twilight zone special case
                if (GS.Gep0 == 0 || GS.Gep1 == 0)
                {
                    d = DualProjectPoints(Zp0.OrgX, Zp0.OrgY, l, Zp1.OrgX, Zp1.OrgY, k);
                }
                else
                {
                    // x_scale == y_scale: pixels are square (non-square pixels are not ported)
                    d = DualProjectPoints(Zp0.OrusX, Zp0.OrusY, l, Zp1.OrusX, Zp1.OrusY, k);
                    d = FtCalc.MulFix(d, Metrics.XScale);
                }
            }
        }

        Stack[a] = d;
    }

    /// <summary>SDPvTL[a]: Set Dual PVector to Line. Opcodes 0x86-0x87.</summary>
    private void InsSdpvtl(int a)
    {
        byte opcode = _opcode;

        ushort p1 = (ushort)Stack[a + 1];
        ushort p2 = (ushort)Stack[a];

        if (Bounds(p2, Zp1.NPoints) || Bounds(p1, Zp2.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        int A, B, C;

        {
            A = unchecked(Zp1.OrgX[p2] - Zp2.OrgX[p1]);
            B = unchecked(Zp1.OrgY[p2] - Zp2.OrgY[p1]);

            // If v1 == v2, SDPvTL behaves the same as SVTCA[X], respectively.
            //
            // Confirmed by Greg Hitchcock.
            if (A == 0 && B == 0)
            {
                A = 0x4000;
                opcode = 0;
            }
        }

        if ((opcode & 1) != 0)
        {
            C = B; // counter-clockwise rotation
            B = A;
            A = unchecked(-C);
        }

        Normalize(A, B, ref GS.DualX, ref GS.DualY);

        {
            A = unchecked(Zp1.CurX[p2] - Zp2.CurX[p1]);
            B = unchecked(Zp1.CurY[p2] - Zp2.CurY[p1]);

            if (A == 0 && B == 0)
            {
                A = 0x4000;
                opcode = 0;
            }
        }

        if ((opcode & 1) != 0)
        {
            C = B; // counter-clockwise rotation
            B = A;
            A = unchecked(-C);
        }

        Normalize(A, B, ref GS.ProjX, ref GS.ProjY);
        ComputeFuncs();
    }

    // Selects one of the two zones (0 the twilight zone, 1 the glyph zone); returns the zone or null for a bad selector.
    private TtGlyphZone? ZoneFor(int selector)
    {
        switch (selector)
        {
            case 0: return Twilight;
            case 1: return Pts;
            default:
                if (PedanticHinting)
                    Error = TtError.InvalidReference;
                return null;
        }
    }

    /// <summary>SZP0[]: Set Zone Pointer 0. Opcode 0x13.</summary>
    private void InsSzp0(int a)
    {
        TtGlyphZone? zone = ZoneFor(Stack[a]);
        if (zone is null)
            return;

        Zp0 = zone;
        GS.Gep0 = (ushort)Stack[a];
    }

    /// <summary>SZP1[]: Set Zone Pointer 1. Opcode 0x14.</summary>
    private void InsSzp1(int a)
    {
        TtGlyphZone? zone = ZoneFor(Stack[a]);
        if (zone is null)
            return;

        Zp1 = zone;
        GS.Gep1 = (ushort)Stack[a];
    }

    /// <summary>SZP2[]: Set Zone Pointer 2. Opcode 0x15.</summary>
    private void InsSzp2(int a)
    {
        TtGlyphZone? zone = ZoneFor(Stack[a]);
        if (zone is null)
            return;

        Zp2 = zone;
        GS.Gep2 = (ushort)Stack[a];
    }

    /// <summary>SZPS[]: Set Zone PointerS. Opcode 0x16.</summary>
    private void InsSzps(int a)
    {
        TtGlyphZone? zone = ZoneFor(Stack[a]);
        if (zone is null)
            return;

        Zp0 = zone;
        Zp1 = Zp0;
        Zp2 = Zp0;

        GS.Gep0 = (ushort)Stack[a];
        GS.Gep1 = (ushort)Stack[a];
        GS.Gep2 = (ushort)Stack[a];
    }

    /// <summary>INSTCTRL[]: INSTruction ConTRoL. Opcode 0x8E.</summary>
    private void InsInstctrl(int a)
    {
        uint k = (uint)Stack[a + 1];
        uint l = (uint)Stack[a];

        // selector values cannot be `OR'ed; they are indices starting with index 1, not flags
        if (k < 1 || k > 3)
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        // convert index to flag value
        uint kf = 1u << (int)(k - 1);

        if (l != 0)
        {
            // arguments to selectors look like flag values
            if (l != kf)
            {
                if (PedanticHinting)
                    Error = TtError.InvalidReference;
                return;
            }
        }

        // INSTCTRL should only be used in the CVT program
        if (IniRange == TtCodeRange.Cvt)
        {
            GS.InstructControl = (byte)(GS.InstructControl & ~(byte)kf);
            GS.InstructControl |= (byte)l;
        }

        // except to change the subpixel flags temporarily
        else if (IniRange == TtCodeRange.Glyph && k == 3)
        {
            // Native ClearType fonts sign a waiver that turns off all backward compatibility hacks and lets them program
            // points to the grid like it's 1996. They might sign a waiver for just one glyph, though.
            if (Version == TtInterpreterVersion.V40)
                BackwardCompatibility = (int)((l & 4) ^ 4);
        }
        else if (PedanticHinting)
        {
            Error = TtError.InvalidReference;
        }
    }

    /// <summary>SCANCTRL[]: SCAN ConTRoL. Opcode 0x85.</summary>
    private void InsScanctrl(int a)
    {
        // Get Threshold
        int t = Stack[a] & 0xFF;

        if (t == 0xFF)
        {
            GS.ScanControl = true;
            return;
        }
        else if (t == 0)
        {
            GS.ScanControl = false;
            return;
        }

        // the rotated and stretched flags of a size are always false here
        if ((Stack[a] & 0x100) != 0 && Metrics.Ppem <= t)
            GS.ScanControl = true;

        if ((Stack[a] & 0x800) != 0 && Metrics.Ppem > t)
            GS.ScanControl = false;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                MANAGING OUTLINES
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>FLIPPT[]: FLIP PoinT. Opcode 0x80.</summary>
    private void InsFlippt(int a)
    {
        int loop = GS.Loop;

        if (NewTop < loop)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;
            GS.Loop = 1;
            return;
        }

        NewTop -= loop;

        // See the notes on backward compatibility mode in FreeType's ttinterp.h.
        if (BackwardCompatibility == 0x7)
        {
            GS.Loop = 1;
            return;
        }

        if (ChargeWork(loop))
        {
            GS.Loop = 1;
            return;
        }

        while (loop-- != 0)
        {
            ushort point = (ushort)Stack[--a];

            if (Bounds(point, Pts.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else
            {
                Pts.Tags[point] ^= FtTag.On;
            }
        }

        GS.Loop = 1;
    }

    /// <summary>FLIPRGON[]: FLIP RanGe ON. Opcode 0x81.</summary>
    private void InsFliprgon(int a)
    {
        // See the notes on backward compatibility mode in FreeType's ttinterp.h.
        if (BackwardCompatibility == 0x7)
            return;

        ushort k = (ushort)Stack[a + 1];
        ushort l = (ushort)Stack[a];

        if (Bounds(k, Pts.NPoints) || Bounds(l, Pts.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        if (ChargeWork(Math.Max(0, k - l + 1)))
            return;

        for (int i = l; i <= k; i++)
            Pts.Tags[i] |= FtTag.On;
    }

    /// <summary>FLIPRGOFF: FLIP RanGe OFF. Opcode 0x82.</summary>
    private void InsFliprgoff(int a)
    {
        // See the notes on backward compatibility mode in FreeType's ttinterp.h.
        if (BackwardCompatibility == 0x7)
            return;

        ushort k = (ushort)Stack[a + 1];
        ushort l = (ushort)Stack[a];

        if (Bounds(k, Pts.NPoints) || Bounds(l, Pts.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        if (ChargeWork(Math.Max(0, k - l + 1)))
            return;

        for (int i = l; i <= k; i++)
            Pts.Tags[i] = (byte)(Pts.Tags[i] & ~FtTag.On);
    }

    // Compute_Point_Displacement: returns true on failure.
    private bool ComputePointDisplacement(out int x, out int y, out TtGlyphZone zone, out ushort refp)
    {
        TtGlyphZone zp;
        ushort p;

        if ((_opcode & 1) != 0)
        {
            zp = Zp0;
            p = GS.Rp1;
        }
        else
        {
            zp = Zp1;
            p = GS.Rp2;
        }

        x = 0;
        y = 0;
        zone = zp;

        if (Bounds(p, zp.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            refp = 0;
            return true;
        }

        refp = p;

        int d = ProjectPoints(zp.CurX, zp.CurY, p, zp.OrgX, zp.OrgY, p);

        x = FtCalc.MulFix(d, _moveX);
        y = FtCalc.MulFix(d, _moveY);

        return false;
    }

    // See the notes on backward compatibility mode in FreeType's ttinterp.h.
    private void MoveZp2Point(int point, int dx, int dy, bool touch)
    {
        if (GS.FreeX != 0)
        {
            if (BackwardCompatibility == 0)
                Zp2.CurX[point] = unchecked(Zp2.CurX[point] + dx);

            if (touch)
                Zp2.Tags[point] |= FtTag.TouchX;
        }

        if (GS.FreeY != 0)
        {
            if (BackwardCompatibility != 0x7)
                Zp2.CurY[point] = unchecked(Zp2.CurY[point] + dy);

            if (touch)
                Zp2.Tags[point] |= FtTag.TouchY;
        }
    }

    /// <summary>SHP[a]: SHift Point by the last point. Opcodes 0x32-0x33.</summary>
    private void InsShp(int a)
    {
        int loop = GS.Loop;

        if (NewTop < loop)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;
            GS.Loop = 1;
            return;
        }

        NewTop -= loop;

        if (ComputePointDisplacement(out int dx, out int dy, out _, out _))
            return;

        if (ChargeWork(loop))
            return;

        while (loop-- != 0)
        {
            ushort point = (ushort)Stack[--a];

            if (Bounds(point, Zp2.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else
            {
                MoveZp2Point(point, dx, dy, true);
            }
        }

        GS.Loop = 1;
    }

    /// <summary>
    /// SHC[a]: SHift Contour. Opcodes 0x34-35. UNDOCUMENTED: According to Greg Hitchcock, there is one (virtual) contour in
    /// the twilight zone, namely contour number zero which includes all points of it.
    /// </summary>
    private void InsShc(int a)
    {
        ushort contour = (ushort)Stack[a];
        int bounds = GS.Gep2 == 0 ? 1 : Zp2.NContours;

        if (Bounds(contour, bounds))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        if (ComputePointDisplacement(out int dx, out int dy, out TtGlyphZone zp, out ushort refp))
            return;

        ushort start;
        if (contour == 0)
            start = 0;
        else
            start = (ushort)(Zp2.Contours[contour - 1] + 1 - Zp2.FirstPoint);

        // we use the number of points if in the twilight zone
        ushort limit;
        if (GS.Gep2 == 0)
            limit = (ushort)Zp2.NPoints;
        else
            limit = (ushort)(Zp2.Contours[contour] + 1 - Zp2.FirstPoint);

        if (ChargeWork(Math.Max(0, limit - start)))
            return;

        for (ushort i = start; i < limit; i++)
        {
            if (!ReferenceEquals(zp, Zp2) || refp != i)
                MoveZp2Point(i, dx, dy, true);
        }
    }

    /// <summary>SHZ[a]: SHift Zone. Opcodes 0x36-37.</summary>
    private void InsShz(int a)
    {
        if (Bounds(Stack[a], 2))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        if (ComputePointDisplacement(out int dx, out int dy, out TtGlyphZone zp, out ushort refp))
            return;

        // XXX: UNDOCUMENTED! SHZ doesn't move the phantom points. Twilight zone has no real contours, so use `n_points'.
        //      Normal zone's `n_points' includes phantoms, so must use end of last contour.
        ushort limit;
        if (GS.Gep2 == 0)
            limit = (ushort)Zp2.NPoints;
        else if (GS.Gep2 == 1 && Zp2.NContours > 0)
            limit = (ushort)(Zp2.Contours[Zp2.NContours - 1] + 1);
        else
            limit = 0;

        // In a component of a composite glyph FreeType takes the end of the last contour, which is counted from the start of the whole
        // outline, as a count from the start of the zone, and so moves points past the zone: into memory of the outline that holds no
        // point (the loader overwrites it when it loads the next component). Nothing there is ever read, so the loop stops at the zone.
        if (limit > Zp2.NPoints)
            limit = (ushort)Zp2.NPoints;

        if (ChargeWork(limit))
            return;

        // XXX: UNDOCUMENTED! SHZ doesn't touch the points
        for (ushort i = 0; i < limit; i++)
        {
            if (!ReferenceEquals(zp, Zp2) || refp != i)
                MoveZp2Point(i, dx, dy, false);
        }
    }

    /// <summary>SHPIX[]: SHift points by a PIXel amount. Opcode 0x38.</summary>
    private void InsShpix(int a)
    {
        int loop = GS.Loop;
        bool inTwilight = GS.Gep0 == 0 || GS.Gep1 == 0 || GS.Gep2 == 0;

        if (NewTop < loop)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;
            GS.Loop = 1;
            return;
        }

        NewTop -= loop;

        int dx = MulFix14(Stack[a], GS.FreeX);
        int dy = MulFix14(Stack[a], GS.FreeY);

        if (ChargeWork(loop))
        {
            GS.Loop = 1;
            return;
        }

        while (loop-- != 0)
        {
            ushort point = (ushort)Stack[--a];

            if (Bounds(point, Zp2.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else if (BackwardCompatibility != 0)
            {
                // Special case: allow SHPIX to move points in the twilight zone. Otherwise, treat SHPIX the same as DELTAP.
                // Unbreaks various fonts such as older versions of Rokkitt and DTL Argo T Light that would glitch severely
                // after calling ALIGNRP after a blocked SHPIX.
                if (inTwilight ||
                    (BackwardCompatibility != 0x7 &&
                     ((IsComposite && GS.FreeY != 0) ||
                      (Zp2.Tags[point] & FtTag.TouchY) != 0)))
                    MoveZp2Point(point, 0, dy, true);
            }
            else
            {
                MoveZp2Point(point, dx, dy, true);
            }
        }

        GS.Loop = 1;
    }

    /// <summary>MSIRP[a]: Move Stack Indirect Relative Position. Opcodes 0x3A-0x3B.</summary>
    private void InsMsirp(int a)
    {
        ushort point = (ushort)Stack[a];

        if (Bounds(point, Zp1.NPoints) ||
            Bounds(GS.Rp0, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        // UNDOCUMENTED! The MS rasterizer does that with twilight points (confirmed by Greg Hitchcock)
        if (GS.Gep1 == 0)
        {
            Zp1.OrgX[point] = Zp0.OrgX[GS.Rp0];
            Zp1.OrgY[point] = Zp0.OrgY[GS.Rp0];
            MoveOrig(Zp1, point, Stack[a + 1]);
            Zp1.CurX[point] = Zp1.OrgX[point];
            Zp1.CurY[point] = Zp1.OrgY[point];
        }

        int distance = ProjectPoints(Zp1.CurX, Zp1.CurY, point, Zp0.CurX, Zp0.CurY, GS.Rp0);

        Move(Zp1, point, unchecked(Stack[a + 1] - distance));

        GS.Rp1 = GS.Rp0;
        GS.Rp2 = point;

        if ((_opcode & 1) != 0)
            GS.Rp0 = point;
    }

    /// <summary>MDAP[a]: Move Direct Absolute Point. Opcodes 0x2E-0x2F.</summary>
    private void InsMdap(int a)
    {
        ushort point = (ushort)Stack[a];

        if (Bounds(point, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        int distance;
        if ((_opcode & 1) != 0)
        {
            int curDist = Project(Zp0.CurX[point], Zp0.CurY[point]);
            distance = unchecked(Round(curDist, 0) - curDist);
        }
        else
        {
            distance = 0;
        }

        Move(Zp0, point, distance);

        GS.Rp0 = point;
        GS.Rp1 = point;
    }

    /// <summary>MIAP[a]: Move Indirect Absolute Point. Opcodes 0x3E-0x3F.</summary>
    private void InsMiap(int a)
    {
        int cvtEntry = Stack[a + 1];
        ushort point = (ushort)Stack[a];

        if (Bounds(point, Zp0.NPoints) ||
            BoundsL(cvtEntry, CvtSize))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            GS.Rp0 = point;
            GS.Rp1 = point;
            return;
        }

        // UNDOCUMENTED!
        //
        // The behaviour of an MIAP instruction is quite different when used in the twilight zone.
        //
        // First, no control value cut-in test is performed as it would fail anyway. Second, the original point, i.e.
        // (org_x,org_y) of zp0.point, is set to the absolute, unrounded distance found in the CVT.
        //
        // This is used in the CVT programs of the Microsoft fonts Arial, Times, etc., in order to re-adjust some key font
        // heights. It allows the use of the IP instruction in the twilight zone, which otherwise would be invalid
        // according to the specification.
        //
        // We implement it with a special sequence for the twilight zone. This is a bad hack, but it seems to work.
        //
        // Confirmed by Greg Hitchcock.
        int distance = ReadCvt(cvtEntry);

        if (GS.Gep0 == 0) // If in twilight zone
        {
            Zp0.OrgX[point] = MulFix14(distance, GS.FreeX);
            Zp0.OrgY[point] = MulFix14(distance, GS.FreeY);
            Zp0.CurX[point] = Zp0.OrgX[point];
            Zp0.CurY[point] = Zp0.OrgY[point];
        }

        int orgDist = Project(Zp0.CurX[point], Zp0.CurY[point]);

        if ((_opcode & 1) != 0) // rounding and control cut-in flag
        {
            int controlValueCutin = GS.ControlValueCutin;

            int delta = unchecked(distance - orgDist);
            if (delta < 0)
                delta = unchecked(-delta);

            if (delta > controlValueCutin)
                distance = orgDist;

            distance = Round(distance, 0);
        }

        Move(Zp0, point, unchecked(distance - orgDist));

        GS.Rp0 = point;
        GS.Rp1 = point;
    }

    /// <summary>MDRP[abcde]: Move Direct Relative Point. Opcodes 0xC0-0xDF.</summary>
    private void InsMdrp(int a)
    {
        ushort point = (ushort)Stack[a];

        if (Bounds(point, Zp1.NPoints) ||
            Bounds(GS.Rp0, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            MdrpEpilogue(point);
            return;
        }

        // XXX: Is there some undocumented feature while in the twilight zone?

        // XXX: UNDOCUMENTED: twilight zone special case
        int orgDist;
        if (GS.Gep0 == 0 || GS.Gep1 == 0)
        {
            orgDist = DualProjectPoints(Zp1.OrgX, Zp1.OrgY, point, Zp0.OrgX, Zp0.OrgY, GS.Rp0);
        }
        else
        {
            // x_scale == y_scale: pixels are square (non-square pixels are not ported)
            orgDist = DualProjectPoints(Zp1.OrusX, Zp1.OrusY, point, Zp0.OrusX, Zp0.OrusY, GS.Rp0);
            orgDist = FtCalc.MulFix(orgDist, Metrics.XScale);
        }

        // single width cut-in test

        // |org_dist - single_width_value| < single_width_cutin
        if (GS.SingleWidthCutin > 0 &&
            orgDist < unchecked(GS.SingleWidthValue + GS.SingleWidthCutin) &&
            orgDist > unchecked(GS.SingleWidthValue - GS.SingleWidthCutin))
        {
            if (orgDist >= 0)
                orgDist = GS.SingleWidthValue;
            else
                orgDist = -GS.SingleWidthValue;
        }

        // round flag
        int distance;
        if ((_opcode & 4) != 0)
            distance = Round(orgDist, 0);
        else
            distance = RoundNone(orgDist, 0);

        // minimum distance flag
        if ((_opcode & 8) != 0)
        {
            int minimumDistance = GS.MinimumDistance;

            if (orgDist >= 0)
            {
                if (distance < minimumDistance)
                    distance = minimumDistance;
            }
            else
            {
                if (distance > unchecked(-minimumDistance))
                    distance = unchecked(-minimumDistance);
            }
        }

        // now move the point
        orgDist = ProjectPoints(Zp1.CurX, Zp1.CurY, point, Zp0.CurX, Zp0.CurY, GS.Rp0);

        Move(Zp1, point, unchecked(distance - orgDist));

        MdrpEpilogue(point);
    }

    private void MdrpEpilogue(ushort point)
    {
        GS.Rp1 = GS.Rp0;
        GS.Rp2 = point;

        if ((_opcode & 16) != 0)
            GS.Rp0 = point;
    }

    /// <summary>MIRP[abcde]: Move Indirect Relative Point. Opcodes 0xE0-0xFF.</summary>
    private void InsMirp(int a)
    {
        ushort point = (ushort)Stack[a];
        int cvtEntry = unchecked(Stack[a + 1] + 1);

        // XXX: UNDOCUMENTED! cvt[-1] = 0 always
        if (Bounds(point, Zp1.NPoints) ||
            BoundsL(cvtEntry, CvtSize + 1) ||
            Bounds(GS.Rp0, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            MirpEpilogue(point);
            return;
        }

        int cvtDist;
        if (cvtEntry == 0)
            cvtDist = 0;
        else
            cvtDist = ReadCvt(cvtEntry - 1);

        // single width test
        int delta = unchecked(cvtDist - GS.SingleWidthValue);
        if (delta < 0)
            delta = unchecked(-delta);

        if (delta < GS.SingleWidthCutin)
        {
            if (cvtDist >= 0)
                cvtDist = GS.SingleWidthValue;
            else
                cvtDist = -GS.SingleWidthValue;
        }

        // UNDOCUMENTED! The MS rasterizer does that with twilight points (confirmed by Greg Hitchcock)
        if (GS.Gep1 == 0)
        {
            Zp1.OrgX[point] = unchecked(Zp0.OrgX[GS.Rp0] + MulFix14(cvtDist, GS.FreeX));
            Zp1.OrgY[point] = unchecked(Zp0.OrgY[GS.Rp0] + MulFix14(cvtDist, GS.FreeY));
            Zp1.CurX[point] = Zp1.OrgX[point];
            Zp1.CurY[point] = Zp1.OrgY[point];
        }

        int orgDist = DualProjectPoints(Zp1.OrgX, Zp1.OrgY, point, Zp0.OrgX, Zp0.OrgY, GS.Rp0);
        int curDist = ProjectPoints(Zp1.CurX, Zp1.CurY, point, Zp0.CurX, Zp0.CurY, GS.Rp0);

        // auto-flip test
        if (GS.AutoFlip)
        {
            if ((orgDist ^ cvtDist) < 0)
                cvtDist = unchecked(-cvtDist);
        }

        // control value cut-in and round
        int distance;
        if ((_opcode & 4) != 0)
        {
            // XXX: UNDOCUMENTED! Only perform cut-in test when both points refer to the same zone.
            if (GS.Gep0 == GS.Gep1)
            {
                int controlValueCutin = GS.ControlValueCutin;

                // XXX: According to Greg Hitchcock, the following wording is the right one:
                //
                //        When the absolute difference between the value in the table [CVT] and the measurement directly from
                //        the outline is _greater_ than the cut_in value, the outline measurement is used.
                //
                //      This is from `instgly.doc'. The description in `ttinst2.doc', version 1.66, is thus incorrect since
                //      it implies `>=' instead of `>'.
                delta = unchecked(cvtDist - orgDist);
                if (delta < 0)
                    delta = unchecked(-delta);

                if (delta > controlValueCutin)
                    cvtDist = orgDist;
            }

            distance = Round(cvtDist, 0);
        }
        else
        {
            distance = RoundNone(cvtDist, 0);
        }

        // minimum distance test
        if ((_opcode & 8) != 0)
        {
            int minimumDistance = GS.MinimumDistance;

            if (orgDist >= 0)
            {
                if (distance < minimumDistance)
                    distance = minimumDistance;
            }
            else
            {
                if (distance > unchecked(-minimumDistance))
                    distance = unchecked(-minimumDistance);
            }
        }

        Move(Zp1, point, unchecked(distance - curDist));

        MirpEpilogue(point);
    }

    private void MirpEpilogue(ushort point)
    {
        GS.Rp1 = GS.Rp0;
        GS.Rp2 = point;

        if ((_opcode & 16) != 0)
            GS.Rp0 = point;
    }

    /// <summary>ALIGNRP[]: ALIGN Relative Point. Opcode 0x3C.</summary>
    private void InsAlignrp(int a)
    {
        int loop = GS.Loop;

        if (NewTop < loop)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;
            GS.Loop = 1;
            return;
        }

        NewTop -= loop;

        if (Bounds(GS.Rp0, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            GS.Loop = 1;
            return;
        }

        if (ChargeWork(loop))
        {
            GS.Loop = 1;
            return;
        }

        while (loop-- != 0)
        {
            ushort point = (ushort)Stack[--a];

            if (Bounds(point, Zp1.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else
            {
                int distance = ProjectPoints(Zp1.CurX, Zp1.CurY, point, Zp0.CurX, Zp0.CurY, GS.Rp0);

                Move(Zp1, point, unchecked(-distance));
            }
        }

        GS.Loop = 1;
    }

    /// <summary>ISECT[]: moves point to InterSECTion. Opcode 0x0F.</summary>
    private void InsIsect(int a)
    {
        ushort point = (ushort)Stack[a];

        ushort a0 = (ushort)Stack[a + 1];
        ushort a1 = (ushort)Stack[a + 2];
        ushort b0 = (ushort)Stack[a + 3];
        ushort b1 = (ushort)Stack[a + 4];

        if (Bounds(b0, Zp0.NPoints) ||
            Bounds(b1, Zp0.NPoints) ||
            Bounds(a0, Zp1.NPoints) ||
            Bounds(a1, Zp1.NPoints) ||
            Bounds(point, Zp2.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        unchecked
        {
            // Cramer's rule
            int dbx = Zp0.CurX[b1] - Zp0.CurX[b0];
            int dby = Zp0.CurY[b1] - Zp0.CurY[b0];

            int dax = Zp1.CurX[a1] - Zp1.CurX[a0];
            int day = Zp1.CurY[a1] - Zp1.CurY[a0];

            int dx = Zp0.CurX[b0] - Zp1.CurX[a0];
            int dy = Zp0.CurY[b0] - Zp1.CurY[a0];

            int discriminant = FtCalc.MulDiv(dax, -dby, 0x40) + FtCalc.MulDiv(day, dbx, 0x40);
            int dotproduct = FtCalc.MulDiv(dax, dbx, 0x40) + FtCalc.MulDiv(day, dby, 0x40);

            // The discriminant above is actually a cross product of vectors da and db. Together with the dot product, they
            // can be used as surrogates for sine and cosine of the angle between the vectors. Indeed,
            //       dotproduct   = |da||db|cos(angle)
            //       discriminant = |da||db|sin(angle)     .
            // We use these equations to reject grazing intersections by thresholding abs(tan(angle)) at 1/19, corresponding
            // to 3 degrees.
            int absDiscriminant = discriminant < 0 ? -discriminant : discriminant;
            int absDotproduct = dotproduct < 0 ? -dotproduct : dotproduct;

            if (19 * absDiscriminant > absDotproduct)
            {
                int val = FtCalc.MulDiv(dx, -dby, 0x40) + FtCalc.MulDiv(dy, dbx, 0x40);

                int rx = FtCalc.MulDiv(val, dax, discriminant);
                int ry = FtCalc.MulDiv(val, day, discriminant);

                // XXX: Block in backward_compatibility and/or post-IUP?
                Zp2.CurX[point] = Zp1.CurX[a0] + rx;
                Zp2.CurY[point] = Zp1.CurY[a0] + ry;
            }
            else
            {
                // else, take the middle of the middles of A and B

                // XXX: Block in backward_compatibility and/or post-IUP?
                Zp2.CurX[point] = ((Zp1.CurX[a0] + Zp1.CurX[a1]) + (Zp0.CurX[b0] + Zp0.CurX[b1])) / 4;
                Zp2.CurY[point] = ((Zp1.CurY[a0] + Zp1.CurY[a1]) + (Zp0.CurY[b0] + Zp0.CurY[b1])) / 4;
            }
        }

        Zp2.Tags[point] |= FtTag.TouchBoth;
    }

    /// <summary>ALIGNPTS[]: ALIGN PoinTS. Opcode 0x27.</summary>
    private void InsAlignpts(int a)
    {
        ushort p1 = (ushort)Stack[a];
        ushort p2 = (ushort)Stack[a + 1];

        if (Bounds(p1, Zp1.NPoints) ||
            Bounds(p2, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        int distance = ProjectPoints(Zp0.CurX, Zp0.CurY, p2, Zp1.CurX, Zp1.CurY, p1) / 2;

        Move(Zp1, p1, distance);
        Move(Zp0, p2, unchecked(-distance));
    }

    /// <summary>IP[]: Interpolate Point. Opcode 0x39. "SOMETIMES, DUMBER CODE IS BETTER CODE"</summary>
    private void InsIp(int a)
    {
        int loop = GS.Loop;

        if (NewTop < loop)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;
            GS.Loop = 1;
            return;
        }

        NewTop -= loop;

        if (Bounds(GS.Rp1, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            GS.Loop = 1;
            return;
        }

        // We need to deal in a special way with the twilight zone. Otherwise, by definition, the value of
        // exc->twilight.orus[n] is (0,0), for every n.
        bool twilight = GS.Gep0 == 0 || GS.Gep1 == 0 || GS.Gep2 == 0;

        int[] orusBaseX = twilight ? Zp0.OrgX : Zp0.OrusX;
        int[] orusBaseY = twilight ? Zp0.OrgY : Zp0.OrusY;
        int baseIndex = GS.Rp1;

        // cur_base is a pointer into zp0 in FreeType: it is read again for every point, so it is indexed here.
        int oldRange;
        int curRange;

        // XXX: There are some glyphs in some braindead but popular fonts out there (e.g. [aeu]grave in monotype.ttf)
        //      calling IP[] with bad values of rp[12]. Do something sane when this odd thing happens.
        if (Bounds(GS.Rp2, Zp1.NPoints))
        {
            oldRange = 0;
            curRange = 0;
        }
        else
        {
            if (twilight)
                oldRange = DualProjectPoints(Zp1.OrgX, Zp1.OrgY, GS.Rp2, orusBaseX, orusBaseY, baseIndex);
            else
                oldRange = DualProjectPoints(Zp1.OrusX, Zp1.OrusY, GS.Rp2, orusBaseX, orusBaseY, baseIndex);

            curRange = ProjectPoints(Zp1.CurX, Zp1.CurY, GS.Rp2, Zp0.CurX, Zp0.CurY, baseIndex);
        }

        if (ChargeWork(loop))
        {
            GS.Loop = 1;
            return;
        }

        while (loop-- != 0)
        {
            uint point = (uint)Stack[--a];

            // check point bounds
            if (Bounds((int)point, Zp2.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }

                continue;
            }

            int orgDist;
            if (twilight)
                orgDist = DualProjectPoints(Zp2.OrgX, Zp2.OrgY, (int)point, orusBaseX, orusBaseY, baseIndex);
            else
                orgDist = DualProjectPoints(Zp2.OrusX, Zp2.OrusY, (int)point, orusBaseX, orusBaseY, baseIndex);

            int curDist = ProjectPoints(Zp2.CurX, Zp2.CurY, (int)point, Zp0.CurX, Zp0.CurY, baseIndex);

            int newDist;
            if (orgDist != 0)
            {
                if (oldRange != 0)
                {
                    newDist = FtCalc.MulDiv(orgDist, curRange, oldRange);
                }
                else
                {
                    // This is the same as what MS does for the invalid case:
                    //
                    //   delta = (Original_Pt - Original_RP1) - (Current_Pt - Current_RP1)         ;
                    //
                    // In FreeType speak:
                    //
                    //   delta = org_dist - cur_dist          .
                    //
                    // We move `point' by `new_dist - cur_dist' after leaving this block, thus we have
                    //
                    //   new_dist - cur_dist = delta                   ,
                    //   new_dist - cur_dist = org_dist - cur_dist     ,
                    //              new_dist = org_dist                .
                    newDist = orgDist;
                }
            }
            else
            {
                newDist = 0;
            }

            Move(Zp2, (ushort)point, unchecked(newDist - curDist));
        }

        GS.Loop = 1;
    }

    /// <summary>UTP[a]: UnTouch Point. Opcode 0x29.</summary>
    private void InsUtp(int a)
    {
        ushort point = (ushort)Stack[a];

        if (Bounds(point, Zp0.NPoints))
        {
            if (PedanticHinting)
                Error = TtError.InvalidReference;
            return;
        }

        byte mask = 0xFF;

        if (GS.FreeX != 0)
            mask = (byte)(mask & ~FtTag.TouchX);

        if (GS.FreeY != 0)
            mask = (byte)(mask & ~FtTag.TouchY);

        Zp0.Tags[point] &= mask;
    }

    // iup_worker_shift_
    private static void IupWorkerShift(int[] orgs, int[] curs, int p1, int p2, int p)
    {
        int dx = unchecked(curs[p] - orgs[p]);
        if (dx != 0)
        {
            for (int i = p1; i < p; i++)
                curs[i] = unchecked(curs[i] + dx);

            for (int i = p + 1; i <= p2; i++)
                curs[i] = unchecked(curs[i] + dx);
        }
    }

    // iup_worker_interpolate_
    private static void IupWorkerInterpolate(int[] orgs, int[] curs, int[] orus, int maxPoints, int p1, int p2, int ref1, int ref2)
    {
        if (p1 > p2)
            return;

        if (Bounds(ref1, maxPoints) || Bounds(ref2, maxPoints))
            return;

        int orus1 = orus[ref1];
        int orus2 = orus[ref2];

        if (orus1 > orus2)
        {
            (orus1, orus2) = (orus2, orus1);
            (ref1, ref2) = (ref2, ref1);
        }

        int org1 = orgs[ref1];
        int org2 = orgs[ref2];
        int cur1 = curs[ref1];
        int cur2 = curs[ref2];
        int delta1 = unchecked(cur1 - org1);
        int delta2 = unchecked(cur2 - org2);

        if (cur1 == cur2 || orus1 == orus2)
        {
            // trivial snap or shift of untouched points
            for (int i = p1; i <= p2; i++)
            {
                int x = orgs[i];

                if (x <= org1)
                    x = unchecked(x + delta1);
                else if (x >= org2)
                    x = unchecked(x + delta2);
                else
                    x = cur1;

                curs[i] = x;
            }
        }
        else
        {
            int scale = 0;
            bool scaleValid = false;

            // interpolation
            for (int i = p1; i <= p2; i++)
            {
                int x = orgs[i];

                if (x <= org1)
                {
                    x = unchecked(x + delta1);
                }
                else if (x >= org2)
                {
                    x = unchecked(x + delta2);
                }
                else
                {
                    if (!scaleValid)
                    {
                        scaleValid = true;
                        scale = FtCalc.DivFix(unchecked(cur2 - cur1), unchecked(orus2 - orus1));
                    }

                    x = unchecked(cur1 + FtCalc.MulFix(unchecked(orus[i] - orus1), scale));
                }

                curs[i] = x;
            }
        }
    }

    /// <summary>IUP[a]: Interpolate Untouched Points. Opcodes 0x30-0x31.</summary>
    private void InsIup()
    {
        // See the notes on backward compatibility mode in FreeType's ttinterp.h. Allow IUP until it has been called on both
        // axes. Immediately return on subsequent ones.
        if (BackwardCompatibility == 0x7)
            return;
        else if (BackwardCompatibility != 0)
            BackwardCompatibility |= 1 << (_opcode & 1);

        // ignore empty outlines
        if (Pts.NContours == 0)
            return;

        byte mask;
        int[] orgs, curs, orus;

        if ((_opcode & 1) != 0)
        {
            mask = FtTag.TouchX;
            orgs = Pts.OrgX;
            curs = Pts.CurX;
            orus = Pts.OrusX;
        }
        else
        {
            mask = FtTag.TouchY;
            orgs = Pts.OrgY;
            curs = Pts.CurY;
            orus = Pts.OrusY;
        }

        int maxPoints = Pts.NPoints;

        if (ChargeWork(maxPoints))
            return;

        int contour = 0;
        uint point = 0;

        do
        {
            uint endPoint = unchecked((uint)(Pts.Contours[contour] - Pts.FirstPoint));
            uint firstPoint = point;

            if (Bounds((int)endPoint, maxPoints))
                endPoint = (uint)(maxPoints - 1);

            while (point <= endPoint && (Pts.Tags[point] & mask) == 0)
                point++;

            if (point <= endPoint)
            {
                uint firstTouched = point;
                uint curTouched = point;

                point++;

                while (point <= endPoint)
                {
                    if ((Pts.Tags[point] & mask) != 0)
                    {
                        IupWorkerInterpolate(orgs, curs, orus, maxPoints, (int)curTouched + 1, (int)point - 1, (int)curTouched, (int)point);
                        curTouched = point;
                    }

                    point++;
                }

                if (curTouched == firstTouched)
                {
                    IupWorkerShift(orgs, curs, (int)firstPoint, (int)endPoint, (int)curTouched);
                }
                else
                {
                    IupWorkerInterpolate(orgs, curs, orus, maxPoints, (ushort)(curTouched + 1), (int)endPoint, (int)curTouched, (int)firstTouched);

                    if (firstTouched > 0)
                        IupWorkerInterpolate(orgs, curs, orus, maxPoints, (int)firstPoint, (int)firstTouched - 1, (int)curTouched, (int)firstTouched);
                }
            }

            contour++;
        }
        while (contour < Pts.NContours);
    }

    /// <summary>DELTAPn[]: DELTA exceptions P1, P2, P3. Opcodes 0x5D, 0x71, 0x72.</summary>
    private void InsDeltap(int a)
    {
        int nump = Stack[a]; // signed value for convenience

        if (nump < 0 || nump > NewTop / 2)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;

            nump = NewTop / 2;
        }

        NewTop -= 2 * nump;

        int p = CurrentPpem() - GS.DeltaBase;

        switch (_opcode)
        {
            case 0x5D:
                break;

            case 0x71:
                p -= 16;
                break;

            case 0x72:
                p -= 32;
                break;
        }

        // check applicable range of adjusted ppem
        if ((p & ~0xF) != 0) // P < 0 || P > 15
            return;

        p <<= 4;
        int f = 1 << (6 - GS.DeltaShift);

        if (ChargeWork(nump))
            return;

        while (nump-- != 0)
        {
            ushort ai = (ushort)Stack[--a];
            int b = Stack[--a];

            // XXX: Because some popular fonts contain some invalid DeltaP instructions, we simply ignore them when the
            //      stacked point reference is off limit, rather than returning an error. As a delta instruction doesn't
            //      change a glyph in great ways, this shouldn't be a problem.
            if (Bounds(ai, Zp0.NPoints))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else
            {
                if ((b & 0xF0) == p)
                {
                    b = (b & 0xF) - 8;
                    if (b >= 0)
                        b++;
                    b = unchecked(b * f);

                    // See the notes on backward compatibility mode in FreeType's ttinterp.h.
                    if (BackwardCompatibility != 0)
                    {
                        if (BackwardCompatibility != 0x7 &&
                            ((IsComposite && GS.FreeY != 0) ||
                             (Zp0.Tags[ai] & FtTag.TouchY) != 0))
                            Move(Zp0, ai, b);
                    }
                    else
                    {
                        Move(Zp0, ai, b);
                    }
                }
            }
        }
    }

    /// <summary>DELTACn[]: DELTA exceptions C1, C2, C3. Opcodes 0x73, 0x74, 0x75.</summary>
    private void InsDeltac(int a)
    {
        int nump = Stack[a]; // signed value for convenience

        if (nump < 0 || nump > NewTop / 2)
        {
            if (PedanticHinting)
                Error = TtError.TooFewArguments;

            nump = NewTop / 2;
        }

        NewTop -= 2 * nump;

        int p = CurrentPpem() - GS.DeltaBase;

        switch (_opcode)
        {
            case 0x73:
                break;

            case 0x74:
                p -= 16;
                break;

            case 0x75:
                p -= 32;
                break;
        }

        // check applicable range of adjusted ppem
        if ((p & ~0xF) != 0) // P < 0 || P > 15
            return;

        p <<= 4;
        int f = 1 << (6 - GS.DeltaShift);

        if (ChargeWork(nump))
            return;

        while (nump-- != 0)
        {
            int ai = Stack[--a];
            int b = Stack[--a];

            if (BoundsL(ai, CvtSize))
            {
                if (PedanticHinting)
                {
                    Error = TtError.InvalidReference;
                    return;
                }
            }
            else
            {
                if ((b & 0xF0) == p)
                {
                    b = (b & 0xF) - 8;
                    if (b >= 0)
                        b++;
                    b = unchecked(b * f);

                    MoveCvt(ai, b);
                }
            }
        }
    }

    /// <summary>GETINFO[]: GET INFOrmation. Opcode 0x88.</summary>
    private void InsGetinfo(int a)
    {
        int k = 0;

        if ((Stack[a] & 1) != 0)
            k = (int)Version;

        // GLYPH ROTATED, selector bit 1 (return bit 8) and GLYPH STRETCHED, selector bit 2 (return bit 9): a size here
        // is never rotated or stretched.

        // VARIATION GLYPH: selector bit 3, return bit 10
        if ((Stack[a] & 8) != 0 && HasBlend)
            k |= 1 << 10;

        // BI-LEVEL HINTING AND GRAYSCALE RENDERING: selector bit 5, return bit 12
        if ((Stack[a] & 32) != 0 && Grayscale)
            k |= 1 << 12;

        // Toggle the following flags only outside of monochrome mode. Otherwise, instructions may behave weirdly and
        // rendering results may differ between v35 and v40 mode, e.g., in `Times New Roman Bold Italic'.
        if (Version == TtInterpreterVersion.V40 && Mode != TtRenderMode.Mono)
        {
            // HINTING FOR SUBPIXEL, selector bit 6, return bit 13: v40 does subpixel hinting by default.
            if ((Stack[a] & 64) != 0)
                k |= 1 << 13;

            // VERTICAL LCD SUBPIXELS?, selector bit 8, return bit 15: only in the vertical LCD render mode, which is
            // not one of the modes here.

            // SUBPIXEL POSITIONED?, selector bit 10, return bit 17
            //
            // XXX: FreeType supports it, dependent on what client does?
            if ((Stack[a] & 1024) != 0)
                k |= 1 << 17;

            // SYMMETRICAL SMOOTHING, selector bit 11, return bit 18: the only smoothing method FreeType supports unless
            // someone sets FT_LOAD_TARGET_MONO.
            if ((Stack[a] & 2048) != 0 && Mode != TtRenderMode.Mono)
                k |= 1 << 18;

            // CLEARTYPE HINTING AND GRAYSCALE RENDERING, selector bit 12, return bit 19: grayscale rendering is what
            // FreeType does anyway unless someone sets FT_LOAD_TARGET_MONO or FT_LOAD_TARGET_LCD(_V).
            if ((Stack[a] & 4096) != 0 && Mode != TtRenderMode.Mono)
                k |= 1 << 19;
        }

        Stack[a] = k;
    }

    /// <summary>GETVARIATION[]: get normalized variation (blend) coordinates. Opcode 0x91. UNDOCUMENTED by Apple; active only if a font has GX variation axes.</summary>
    private void InsGetvariation(int a)
    {
        int numAxes = BlendCoordinates.Length;

        if (Bounds(numAxes, StackSize + 1 - Top))
        {
            Error = TtError.StackOverflow;
            return;
        }

        for (int i = 0; i < numAxes; i++)
            Stack[a + i] = BlendCoordinates[i] >> 2; // convert 16.16 to 2.14 format

        NewTop += numAxes;
    }

    // Ins_UNKNOWN: an opcode with no meaning of its own, which a font may have defined with IDEF.
    private void InsUnknown()
    {
        for (int d = 0; d < NumIDefs; d++)
        {
            if ((byte)IDefs[d].Opc == _opcode && IDefs[d].Active)
            {
                if (_callTop >= CallStackSize)
                {
                    Error = TtError.StackOverflow;
                    return;
                }

                ref TtCallRecord call = ref _callStack[_callTop++];

                call.CallerRange = CurRange;
                call.CallerIp = _ip + 1;
                call.CurCount = 1;
                call.DefIsInstruction = true;
                call.DefIndex = d;

                GotoCodeRange(IDefs[d].Range, IDefs[d].Start);

                return;
            }
        }

        Error = TtError.InvalidOpcode;
    }
}
