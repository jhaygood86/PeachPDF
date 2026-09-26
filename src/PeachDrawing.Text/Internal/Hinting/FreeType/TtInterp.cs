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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): ttinterp.c, ttinterp.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The TrueType bytecode interpreter's execution context (<c>TT_ExecContextRec</c>): the state one run of a program
/// works on, with the main loop <see cref="RunIns"/> and the graphics-state plumbing. The instruction handlers are in
/// <c>TtInterpInstructions.cs</c>.
/// </summary>
/// <remarks>
/// A context is not shared between threads and is reused from glyph to glyph. What a run may change of the size it works
/// for is copied, never written through: <see cref="Cvt"/> and <see cref="Storage"/> start as references to the size's
/// arrays and are copied on the first write of a glyph program, exactly as FreeType's <c>glyfCvt</c>/<c>glyfStorage</c>.
/// </remarks>
internal sealed partial class TtExecContext
{
    /// <summary>
    /// The instruction budget (<c>TT_CONFIG_OPTION_MAX_RUNNABLE_OPCODES</c>). FreeType applies it to each run of a program; here it
    /// is shared by every program run while one glyph is loaded (see <see cref="ResetBudget"/>), so a composite glyph cannot multiply it.
    /// </summary>
    private const int MaxRunnableOpcodes = 1000000;

    /// <summary>
    /// The budget of loop iterations (<c>SLOOP</c>, <c>LOOPCALL</c>, <c>IUP</c>, deltas, flips, skipped instructions) shared by
    /// every program run while one glyph is loaded. FreeType has no such limit; see PORTING-NOTES.md.
    /// </summary>
    private const long MaxLoopWork = 16L * 1024 * 1024;

    /// <summary>The depth of the call stack (<c>callSize</c>).</summary>
    private const int CallStackSize = 32;

    // ---- configuration of the run (FreeType keeps these on the face, the driver and the size)

    public TtInterpreterVersion Version = TtInterpreterVersion.V40;
    public TtRenderMode Mode = TtRenderMode.Normal;
    public bool Grayscale;
    public bool PedanticHinting;

    /// <summary>The number of glyphs in the face, which bounds the loop counters.</summary>
    public int NumGlyphs;

    /// <summary>Whether the face is a variation instance, and its normalized coordinates in 16.16 (<c>face->blend</c>).</summary>
    public bool HasBlend;
    public int[] BlendCoordinates = [];

    // ---- instructions state

    /// <summary>The last execution error.</summary>
    public int Error;

    public int Top;
    public int StackSize;
    public int[] Stack = [];

    /// <summary>The index of the first argument of the current instruction, and the new top after it.</summary>
    private int _args;
    public int NewTop;

    public TtGlyphZone Zp0 = new(), Zp1 = new(), Zp2 = new(), Pts = new(), Twilight = new();

    /// <summary>The point size in 26.6 (<c>MPS</c>).</summary>
    public int PointSize;

    public TtSizeMetrics Metrics;

    public TtGraphicsState GS;

    public int IniRange;
    public int CurRange;
    private byte[] _code = [];
    private int _ip;
    private int _codeSize;

    private byte _opcode;
    private int _length;

    public int CvtSize;
    public int[] Cvt = [];
    private int[] _glyfCvt = [];
    private bool _cvtIsGlyfCopy;

    /// <summary>The instructions of the glyph being hinted.</summary>
    public byte[] GlyphIns = [];
    public int GlyphSize;

    public TtDefRecord[] FDefs = [];
    public int NumFDefs;
    public int MaxFDefs;
    public TtDefRecord[] IDefs = [];
    public int NumIDefs;
    public int MaxIDefs;
    public uint MaxFunc;
    public uint MaxIns;

    private int _callTop;
    private readonly TtCallRecord[] _callStack = new TtCallRecord[CallStackSize];

    private readonly byte[]?[] _codeRangeBase = new byte[3][];
    private readonly int[] _codeRangeSize = new int[3];

    public int StoreSize;
    public int[] Storage = [];
    private int[] _glyfStorage = [];
    private bool _storageIsGlyfCopy;

    // values used for the `SuperRounding'
    private int _period;
    private int _phase;
    private int _threshold;

    public bool InstructionTrap;
    public bool IsComposite;

    // ---- the graphics-state derived functions

    private int _roundFunc = TtRound.ToGrid;
    private int _moveX, _moveY;
    private int _projKind, _dualKind, _moveKind;

    /// <summary>
    /// Activates backward compatibility (bit 2) and tracks IUP (bits 0-1). If this is zero, the interpreter is either in
    /// v35 or in native ClearType mode.
    /// </summary>
    public int BackwardCompatibility;

    // We maintain two counters (in addition to the instruction counter) that act as loop detectors for LOOPCALL and jump
    // opcodes with negative arguments.
    private ulong _loopcallCounter;
    private ulong _loopcallCounterMax;
    private ulong _negJumpCounter;
    private ulong _negJumpCounterMax;

    private long _loopWork;
    private int _opsRun;

    /// <summary>
    /// Starts a new budget of instructions and loop work. The caller does it once for a font program, once for a CVT program and once
    /// for each glyph loaded (with all of its components), not for each program run.
    /// </summary>
    public void ResetBudget()
    {
        _loopWork = 0;
        _opsRun = 0;
    }

    // The number of arguments popped and pushed by each opcode, high nibble popped and low nibble pushed. Opcodes with a
    // varying number of parameters in the data stream (NPUSHB, NPUSHW) have zero here and a negative value in the length
    // table below.
    private static readonly byte[] PopPushCount =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x02, 0x02, 0x00, 0x50,
        0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x00, 0x00, 0x10, 0x00, 0x10, 0x10, 0x10, 0x10,
        0x12, 0x10, 0x00, 0x22, 0x01, 0x11, 0x10, 0x20, 0x00, 0x10, 0x20, 0x10, 0x10, 0x00, 0x10, 0x10,
        0x00, 0x00, 0x00, 0x00, 0x10, 0x10, 0x10, 0x10, 0x10, 0x00, 0x20, 0x20, 0x00, 0x00, 0x20, 0x20,
        0x00, 0x00, 0x20, 0x11, 0x20, 0x11, 0x11, 0x11, 0x20, 0x21, 0x21, 0x01, 0x01, 0x00, 0x00, 0x10,
        0x21, 0x21, 0x21, 0x21, 0x21, 0x21, 0x11, 0x11, 0x10, 0x00, 0x21, 0x21, 0x11, 0x10, 0x10, 0x10,
        0x21, 0x21, 0x21, 0x21, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
        0x20, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x20, 0x20, 0x00, 0x00, 0x00, 0x00, 0x10, 0x10,
        0x00, 0x20, 0x20, 0x00, 0x00, 0x10, 0x20, 0x20, 0x11, 0x10, 0x33, 0x21, 0x21, 0x10, 0x20, 0x00,
        0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10,
        0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10,
        0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
        0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,
    ];

    private static readonly sbyte[] OpcodeLength =
    [
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        -1, -2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 3, 4, 5, 6, 7, 8, 9, 3, 5, 7, 9, 11, 13, 15, 17,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    ];

    /// <summary>Creates an empty context (<c>TT_New_Context</c>); the caller fills in the arrays.</summary>
    public TtExecContext()
    {
    }

    // A thread reuses one context (and so its stack and zone arrays) from glyph to glyph: hinting never runs inside itself.
    [ThreadStatic]
    private static TtExecContext? s_cached;

    /// <summary>Takes this thread's context, or makes one; give it back with <see cref="Return"/>.</summary>
    public static TtExecContext Rent()
    {
        TtExecContext exec = s_cached ?? new TtExecContext();
        s_cached = null;
        return exec;
    }

    /// <summary>Gives a context back for the next glyph, dropping what it holds of the last one.</summary>
    public static void Return(TtExecContext exec)
    {
        // Do not keep a font's arrays alive from a thread-static: the stack and the working copies are only capacity and stay.
        exec.Cvt = [];
        exec.Storage = [];
        exec.FDefs = [];
        exec.IDefs = [];
        exec.GlyphIns = [];
        exec.BlendCoordinates = [];
        exec._code = [];
        for (int i = 0; i < exec._codeRangeBase.Length; i++)
            exec._codeRangeBase[i] = null;
        exec.Zp0 = exec.Zp1 = exec.Zp2 = exec.Pts = exec.Twilight = new TtGlyphZone();
        s_cached = exec;
    }

    /// <summary>Makes sure the stack has room for <see cref="StackSize"/> elements.</summary>
    public void EnsureStack()
    {
        if (Stack.Length < StackSize)
            Stack = new int[StackSize];
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                              CODERANGE FUNCTIONS
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Sets a code range (<c>TT_Set_CodeRange</c>) and makes it the current one, starting at its first byte.</summary>
    public void SetCodeRange(int range, byte[] code, int length)
    {
        _codeRangeBase[range - 1] = code;
        _codeRangeSize[range - 1] = length;

        _code = code;
        _codeSize = length;
        _ip = 0;
        CurRange = range;
        IniRange = range;
    }

    /// <summary>
    /// Makes the font program and the CVT program callable from a glyph program, as they are in FreeType's context after a size's
    /// programs have run: the code range table keeps them. A program the font does not have stays a range that is not there.
    /// </summary>
    public void SetProgramRanges(byte[] fontProgram, byte[] cvtProgram)
    {
        _codeRangeBase[TtCodeRange.Font - 1] = fontProgram.Length > 0 ? fontProgram : null;
        _codeRangeSize[TtCodeRange.Font - 1] = fontProgram.Length;
        _codeRangeBase[TtCodeRange.Cvt - 1] = cvtProgram.Length > 0 ? cvtProgram : null;
        _codeRangeSize[TtCodeRange.Cvt - 1] = cvtProgram.Length;
        _codeRangeBase[TtCodeRange.Glyph - 1] = null;
        _codeRangeSize[TtCodeRange.Glyph - 1] = 0;
    }

    /// <summary>Clears a code range (<c>TT_Clear_CodeRange</c>).</summary>
    public void ClearCodeRange(int range)
    {
        _codeRangeBase[range - 1] = null;
        _codeRangeSize[range - 1] = 0;
    }

    /// <summary>
    /// Prepares a context for hinting with a size (<c>TT_Load_Context</c>): cvt and storage are the size's arrays again, ready to
    /// be copied when a glyph program writes to them.
    /// </summary>
    public void ResetWorkingCopies()
    {
        _cvtIsGlyfCopy = false;
        _storageIsGlyfCopy = false;
        GlyphIns = [];
        GlyphSize = 0;
    }

    /// <summary>
    /// Copies the graphics-state values the CVT program may change into a size's state (<c>TT_Save_Context</c>). Only these
    /// values can be modified by the CVT program.
    /// </summary>
    public void SaveContext(ref TtGraphicsState sizeState)
    {
        sizeState.MinimumDistance = GS.MinimumDistance;
        sizeState.ControlValueCutin = GS.ControlValueCutin;
        sizeState.SingleWidthCutin = GS.SingleWidthCutin;
        sizeState.SingleWidthValue = GS.SingleWidthValue;
        sizeState.DeltaBase = GS.DeltaBase;
        sizeState.DeltaShift = GS.DeltaShift;
        sizeState.AutoFlip = GS.AutoFlip;
        sizeState.InstructControl = GS.InstructControl;
        sizeState.ScanControl = GS.ScanControl;
        sizeState.ScanType = GS.ScanType;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                 ARITHMETIC
    // ---------------------------------------------------------------------------------------------------------------

    // Compute (a*b)/2^14 with maximum accuracy and rounding.
    private static int MulFix14(int a, int b)
    {
        long ab = (long)a * b;

        ab += 0x2000 + (ab >> 63); // rounding phase

        return unchecked((int)(ab >> 14));
    }

    // compute (ax*bx+ay*by)/2^14 with maximum accuracy and rounding
    private static int DotFix14(int ax, int ay, int bx, int by)
    {
        long c = (long)ax * bx + (long)ay * by;

        c += 0x2000 + (c >> 63); // rounding phase

        return unchecked((int)(c >> 14));
    }

    private int CurrentPpem() => Metrics.Ppem;

    // ---------------------------------------------------------------------------------------------------------------
    //                                       CVT AND STORAGE (copy on write)
    // ---------------------------------------------------------------------------------------------------------------

    private int ReadCvt(int idx) => Cvt[idx];

    private void ModifyCvtCheck()
    {
        if (IniRange == TtCodeRange.Glyph && !_cvtIsGlyfCopy)
        {
            if (_glyfCvt.Length < CvtSize)
                _glyfCvt = new int[CvtSize];

            Array.Copy(Cvt, _glyfCvt, CvtSize);
            Cvt = _glyfCvt;
            _cvtIsGlyfCopy = true;
        }
    }

    private void WriteCvt(int idx, int value)
    {
        ModifyCvtCheck();
        Cvt[idx] = value;
    }

    private void MoveCvt(int idx, int value)
    {
        ModifyCvtCheck();
        Cvt[idx] = unchecked(Cvt[idx] + value);
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                              CODE RANGE JUMPS
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Goes to a certain code range in the instruction stream (<c>Ins_Goto_CodeRange</c>). Returns true on failure.</summary>
    private bool GotoCodeRange(int aRange, int aIp)
    {
        if (aRange < 1 || aRange > 3)
        {
            Error = TtError.BadArgument;
            return true;
        }

        byte[]? baseCode = _codeRangeBase[aRange - 1];

        if (baseCode is null) // invalid coderange
        {
            Error = TtError.InvalidCodeRange;
            return true;
        }

        // NOTE: Because the last instruction of a program may be a CALL which will return to the first byte *after* the
        //       code range, we test for aIP <= Size, instead of aIP < Size.
        if (aIp > _codeRangeSize[aRange - 1])
        {
            Error = TtError.CodeOverflow;
            return true;
        }

        _code = baseCode;
        _codeSize = _codeRangeSize[aRange - 1];
        _ip = aIp;
        _length = 0;
        CurRange = aRange;

        return false;
    }

    // Apple's TrueType specification gives the following order of operations in instructions that move points.
    //
    //   - check single width cut-in (MIRP, MDRP)
    //   - check control value cut-in (MIRP, MIAP)
    //   - apply engine compensation (MIRP, MDRP)
    //   - round distance (MIRP, MDRP) or value (MIAP, MDAP)
    //   - check minimum distance (MIRP,MDRP)
    //   - move point (MIRP, MDRP, MIAP, MSIRP, MDAP)
    //
    // For rounding instructions, engine compensation happens before rounding.

    // ---------------------------------------------------------------------------------------------------------------
    //                                                POINT MOVEMENT
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves a point by a given distance along the freedom vector; the point is `touched' (<c>Direct_Move</c> and its
    /// specialisations for the coordinate axes). See the notes on backward compatibility mode in the header of the file.
    /// </summary>
    private void Move(TtGlyphZone zone, int point, int distance)
    {
        switch (_moveKind)
        {
            case MoveKindX:
                if (BackwardCompatibility == 0)
                    zone.CurX[point] = unchecked(zone.CurX[point] + distance);

                zone.Tags[point] |= FtTag.TouchX;
                break;

            case MoveKindY:
                if (BackwardCompatibility != 0x7)
                    zone.CurY[point] = unchecked(zone.CurY[point] + distance);

                zone.Tags[point] |= FtTag.TouchY;
                break;

            default:
            {
                int v = _moveX;
                if (v != 0)
                {
                    // Exception to the post-IUP curfew: Allow the x component of diagonal moves, but only post-IUP.
                    // DejaVu tries to adjust diagonal stems like on `Z' and `z' post-IUP.
                    if (BackwardCompatibility == 0)
                        zone.CurX[point] = unchecked(zone.CurX[point] + FtCalc.MulFix(distance, v));

                    zone.Tags[point] |= FtTag.TouchX;
                }

                v = _moveY;
                if (v != 0)
                {
                    if (BackwardCompatibility != 0x7)
                        zone.CurY[point] = unchecked(zone.CurY[point] + FtCalc.MulFix(distance, v));

                    zone.Tags[point] |= FtTag.TouchY;
                }

                break;
            }
        }
    }

    /// <summary>Moves the *original* position of a point; the point is not touched (<c>Direct_Move_Orig</c>).</summary>
    private void MoveOrig(TtGlyphZone zone, int point, int distance)
    {
        switch (_moveKind)
        {
            case MoveKindX:
                zone.OrgX[point] = unchecked(zone.OrgX[point] + distance);
                break;

            case MoveKindY:
                zone.OrgY[point] = unchecked(zone.OrgY[point] + distance);
                break;

            default:
            {
                int v = _moveX;
                if (v != 0)
                    zone.OrgX[point] = unchecked(zone.OrgX[point] + FtCalc.MulFix(distance, v));

                v = _moveY;
                if (v != 0)
                    zone.OrgY[point] = unchecked(zone.OrgY[point] + FtCalc.MulFix(distance, v));

                break;
            }
        }
    }

    private const int MoveKindGeneral = 0;
    private const int MoveKindX = 1;
    private const int MoveKindY = 2;

    // ---------------------------------------------------------------------------------------------------------------
    //                                                   ROUNDING
    // ---------------------------------------------------------------------------------------------------------------

    private int Round(int distance, int compensation)
    {
        switch (_roundFunc)
        {
            case TtRound.ToGrid: return RoundToGrid(distance, compensation);
            case TtRound.ToHalfGrid: return RoundToHalfGrid(distance, compensation);
            case TtRound.DownToGrid: return RoundDownToGrid(distance, compensation);
            case TtRound.UpToGrid: return RoundUpToGrid(distance, compensation);
            case TtRound.ToDoubleGrid: return RoundToDoubleGrid(distance, compensation);
            case TtRound.Super: return RoundSuper(distance, compensation);
            case TtRound.Super45: return RoundSuper45(distance, compensation);
            default: return RoundNone(distance, compensation);
        }
    }

    /// <summary>Does not round, but adds engine compensation.</summary>
    private static int RoundNone(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = unchecked(distance + compensation);
            if (val < 0)
                val = 0;
        }
        else
        {
            val = unchecked(distance - compensation);
            if (val > 0)
                val = 0;
        }

        return val;
    }

    /// <summary>Rounds value to grid after adding engine compensation.</summary>
    private static int RoundToGrid(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = FtCalc.PixFloor(unchecked(unchecked(distance + compensation) + 32));
            if (val < 0)
                val = 0;
        }
        else
        {
            val = unchecked(-FtCalc.PixFloor(unchecked(unchecked(compensation - distance) + 32)));
            if (val > 0)
                val = 0;
        }

        return val;
    }

    /// <summary>Rounds value to half grid after adding engine compensation.</summary>
    private static int RoundToHalfGrid(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = unchecked(FtCalc.PixFloor(unchecked(distance + compensation)) + 32);
            if (val < 0)
                val = 32;
        }
        else
        {
            val = unchecked(-(FtCalc.PixFloor(unchecked(compensation - distance)) + 32));
            if (val > 0)
                val = -32;
        }

        return val;
    }

    /// <summary>Rounds value down to grid after adding engine compensation.</summary>
    private static int RoundDownToGrid(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = FtCalc.PixFloor(unchecked(distance + compensation));
            if (val < 0)
                val = 0;
        }
        else
        {
            val = unchecked(-FtCalc.PixFloor(unchecked(compensation - distance)));
            if (val > 0)
                val = 0;
        }

        return val;
    }

    /// <summary>Rounds value up to grid after adding engine compensation.</summary>
    private static int RoundUpToGrid(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = FtCalc.PixFloor(unchecked(unchecked(distance + compensation) + 63));
            if (val < 0)
                val = 0;
        }
        else
        {
            val = unchecked(-FtCalc.PixFloor(unchecked(unchecked(compensation - distance) + 63)));
            if (val > 0)
                val = 0;
        }

        return val;
    }

    /// <summary>Rounds value to double grid after adding engine compensation.</summary>
    private static int RoundToDoubleGrid(int distance, int compensation)
    {
        int val;

        if (distance >= 0)
        {
            val = unchecked(unchecked(distance + compensation) + 16) & ~31;
            if (val < 0)
                val = 0;
        }
        else
        {
            val = unchecked(-(unchecked(unchecked(compensation - distance) + 16) & ~31));
            if (val > 0)
                val = 0;
        }

        return val;
    }

    /// <summary>
    /// Super-rounds value to grid after adding engine compensation. The TrueType specification says very little about the
    /// relationship between rounding and engine compensation. However, it seems from the description of super round that
    /// we should add the compensation before rounding.
    /// </summary>
    private int RoundSuper(int distance, int compensation)
    {
        int val;

        unchecked
        {
            if (distance >= 0)
            {
                val = (distance + (_threshold - _phase + compensation)) & -_period;
                val += _phase;
                if (val < 0)
                    val = _phase;
            }
            else
            {
                val = -(((_threshold - _phase + compensation) - distance) & -_period);
                val -= _phase;
                if (val > 0)
                    val = -_phase;
            }
        }

        return val;
    }

    /// <summary>Super-rounds value to grid after adding engine compensation; a separate function as it may need greater precision.</summary>
    private int RoundSuper45(int distance, int compensation)
    {
        int val;

        unchecked
        {
            if (distance >= 0)
            {
                val = ((distance + (_threshold - _phase + compensation)) / _period) * _period;
                val += _phase;
                if (val < 0)
                    val = _phase;
            }
            else
            {
                val = -((((_threshold - _phase + compensation) - distance) / _period) * _period);
                val -= _phase;
                if (val > 0)
                    val = -_phase;
            }
        }

        return val;
    }

    /// <summary>Sets Super Round parameters (<c>SetSuperRound</c>).</summary>
    private void SetSuperRound(int gridPeriod, int selector)
    {
        switch (selector & 0xC0)
        {
            case 0:
                _period = gridPeriod / 2;
                break;

            case 0x40:
                _period = gridPeriod;
                break;

            case 0x80:
                _period = gridPeriod * 2;
                break;

            // This opcode is reserved, but...
            case 0xC0:
                _period = gridPeriod;
                break;
        }

        switch (selector & 0x30)
        {
            case 0:
                _phase = 0;
                break;

            case 0x10:
                _phase = _period / 4;
                break;

            case 0x20:
                _phase = _period / 2;
                break;

            case 0x30:
                _phase = _period * 3 / 4;
                break;
        }

        if ((selector & 0x0F) == 0)
            _threshold = _period - 1;
        else
            _threshold = ((selector & 0x0F) - 4) * _period / 8;

        // convert to F26Dot6 format
        _period >>= 8;
        _phase >>= 8;
        _threshold >>= 8;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                PROJECTIONS
    // ---------------------------------------------------------------------------------------------------------------

    // Computes the projection of the vector (dx, dy) along the current projection vector (Project, Project_x, Project_y).
    private int Project(int dx, int dy)
    {
        switch (_projKind)
        {
            case 0: return dx;
            case 1: return dy;
            default: return DotFix14(dx, dy, GS.ProjX, GS.ProjY);
        }
    }

    // Computes the projection of the vector (dx, dy) along the current dual vector (Dual_Project, Project_x, Project_y).
    private int DualProject(int dx, int dy)
    {
        switch (_dualKind)
        {
            case 0: return dx;
            case 1: return dy;
            default: return DotFix14(dx, dy, GS.DualX, GS.DualY);
        }
    }

    // PROJECT( v1, v2 ) and DUALPROJ( v1, v2 ): the projection of v1 - v2, each vector an (x, y) pair of arrays and an index.
    private int ProjectPoints(int[] x1, int[] y1, int i1, int[] x2, int[] y2, int i2) =>
        Project(unchecked(x1[i1] - x2[i2]), unchecked(y1[i1] - y2[i2]));

    private int DualProjectPoints(int[] x1, int[] y1, int i1, int[] x2, int[] y2, int i2) =>
        DualProject(unchecked(x1[i1] - x2[i2]), unchecked(y1[i1] - y2[i2]));

    /// <summary>Computes the projection and movement functions according to the current graphics state (<c>Compute_Funcs</c>).</summary>
    private void ComputeFuncs()
    {
        int fDotP = (GS.ProjX * GS.FreeX + GS.ProjY * GS.FreeY + 0x2000) >> 14;

        if (fDotP >= 0x3FFE)
        {
            // commonly collinear
            _moveX = GS.FreeX * 4;
            _moveY = GS.FreeY * 4;
        }
        else if (-0x400 < fDotP && fDotP < 0x400)
        {
            // prohibitively orthogonal
            _moveX = 0;
            _moveY = 0;
        }
        else
        {
            _moveX = GS.FreeX * 0x10000 / fDotP;
            _moveY = GS.FreeY * 0x10000 / fDotP;
        }

        if (fDotP >= 0x3FFE && GS.FreeX == 0x4000)
            _moveKind = MoveKindX;
        else if (fDotP >= 0x3FFE && GS.FreeY == 0x4000)
            _moveKind = MoveKindY;
        else
            _moveKind = MoveKindGeneral;

        if (GS.ProjX == 0x4000)
            _projKind = 0;
        else if (GS.ProjY == 0x4000)
            _projKind = 1;
        else
            _projKind = 2;

        if (GS.DualX == 0x4000)
            _dualKind = 0;
        else if (GS.DualY == 0x4000)
            _dualKind = 1;
        else
            _dualKind = 2;
    }

    /// <summary>
    /// Norms a vector (<c>Normalize</c>). In case both coordinates are zero the result is left untouched: "It seems that
    /// it is possible to try to normalize the vector (0,0)".
    /// </summary>
    private static void Normalize(int vx, int vy, ref short rx, ref short ry)
    {
        if (vx == 0 && vy == 0)
            return;

        FtCalc.VectorNormLen(ref vx, ref vy);

        rx = unchecked((short)(vx / 4));
        ry = unchecked((short)(vy / 4));
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                              LOOP WORK ACCOUNTING
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Charges the work of a loop against the run's budget; returns true (with the error set) when the budget is spent.</summary>
    private bool ChargeWork(long units)
    {
        _loopWork += units;
        if (_loopWork > MaxLoopWork)
        {
            Error = TtError.ExecutionTooLong;
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------------------------
    //                                                 RUN
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Executes a run of opcodes (<c>TT_RunIns</c>). It exits on errors (returning the error), on reaching the end of the
    /// main code range (returning 0), and after one instruction if <see cref="InstructionTrap"/> is set. Reaching the end of
    /// a code range within a function call is an error.
    /// </summary>
    /// <remarks>The documented DEBUG opcode pops a value from the stack; that behaviour is unsupported: here a DEBUG opcode is always an error.</remarks>
    public int RunIns()
    {
        do
        {
            // increment instruction counter and check if we didn't run this program for too long (e.g. infinite loops)
            if (++_opsRun > MaxRunnableOpcodes)
            {
                Error = TtError.ExecutionTooLong;
                return Error;
            }

            Error = TtError.Ok;
            _opcode = _code[_ip];
            _length = 1;

            // First, let's check for empty stack and overflow
            int popPush = PopPushCount[_opcode];
            _args = Top - (popPush >> 4);

            // `args' is the top of the stack once arguments have been popped. One can also interpret it as the index of
            // the last argument.
            if (_args < 0)
            {
                if (PedanticHinting)
                {
                    Error = TtError.TooFewArguments;
                    return Error;
                }

                // push zeroes onto the stack
                for (int i = 0; i < popPush >> 4; i++)
                    Stack[i] = 0;
                _args = 0;
            }

            NewTop = _args + (popPush & 15);

            // `new_top' is the new top of the stack, after the instruction's execution. `top' will be set to `new_top'
            // after the `switch' statement.
            if (NewTop > StackSize)
            {
                Error = TtError.StackOverflow;
                return Error;
            }

            int a = _args;
            byte opcode = _opcode;

            switch (opcode)
            {
                case 0x00: // SVTCA y
                case 0x01: // SVTCA x
                case 0x02: // SPvTCA y
                case 0x03: // SPvTCA x
                case 0x04: // SFvTCA y
                case 0x05: // SFvTCA x
                    InsSxyTca();
                    break;

                case 0x06: // SPvTL //
                case 0x07: // SPvTL +
                    InsSpvtl(a);
                    break;

                case 0x08: // SFvTL //
                case 0x09: // SFvTL +
                    InsSfvtl(a);
                    break;

                case 0x0A: InsSpvfs(a); break;
                case 0x0B: InsSfvfs(a); break;
                case 0x0C: InsGpv(a); break;
                case 0x0D: InsGfv(a); break;
                case 0x0E: InsSfvtpv(); break;
                case 0x0F: InsIsect(a); break;
                case 0x10: GS.Rp0 = (ushort)Stack[a]; break; // SRP0
                case 0x11: GS.Rp1 = (ushort)Stack[a]; break; // SRP1
                case 0x12: GS.Rp2 = (ushort)Stack[a]; break; // SRP2
                case 0x13: InsSzp0(a); break;
                case 0x14: InsSzp1(a); break;
                case 0x15: InsSzp2(a); break;
                case 0x16: InsSzps(a); break;
                case 0x17: InsSloop(a); break;
                case 0x18: InsRtg(); break;
                case 0x19: InsRthg(); break;
                case 0x1A: GS.MinimumDistance = Stack[a]; break; // SMD
                case 0x1B: InsElse(); break;
                case 0x1C: InsJmpr(a); break;
                case 0x1D: GS.ControlValueCutin = Stack[a]; break; // SCVTCI
                case 0x1E: GS.SingleWidthCutin = Stack[a]; break; // SSWCI
                case 0x1F: GS.SingleWidthValue = FtCalc.MulFix(Stack[a], Metrics.Scale); break; // SSW
                case 0x20: Stack[a + 1] = Stack[a]; break; // DUP
                case 0x21: break; // POP
                case 0x22: NewTop = 0; break; // CLEAR
                case 0x23: (Stack[a], Stack[a + 1]) = (Stack[a + 1], Stack[a]); break; // SWAP
                case 0x24: Stack[a] = Top; break; // DEPTH
                case 0x25: InsCindex(a); break;
                case 0x26: InsMindex(a); break;
                case 0x27: InsAlignpts(a); break;
                case 0x28: InsUnknown(); break; // RAW
                case 0x29: InsUtp(a); break;
                case 0x2A: InsLoopcall(a); break;
                case 0x2B: InsCall(a); break;
                case 0x2C: InsFdef(a); break;
                case 0x2D: InsEndf(); break;
                case 0x2E: // MDAP
                case 0x2F: // MDAP
                    InsMdap(a);
                    break;

                case 0x30: // IUP
                case 0x31: // IUP
                    InsIup();
                    break;

                case 0x32: // SHP
                case 0x33: // SHP
                    InsShp(a);
                    break;

                case 0x34: // SHC
                case 0x35: // SHC
                    InsShc(a);
                    break;

                case 0x36: // SHZ
                case 0x37: // SHZ
                    InsShz(a);
                    break;

                case 0x38: InsShpix(a); break;
                case 0x39: InsIp(a); break;
                case 0x3A: // MSIRP
                case 0x3B: // MSIRP
                    InsMsirp(a);
                    break;

                case 0x3C: InsAlignrp(a); break;
                case 0x3D: InsRtdg(); break;
                case 0x3E: // MIAP
                case 0x3F: // MIAP
                    InsMiap(a);
                    break;

                case 0x40: InsNpushb(a); break;
                case 0x41: InsNpushw(a); break;
                case 0x42: InsWs(a); break;
                case 0x43: InsRs(a); break;
                case 0x44: InsWcvtp(a); break;
                case 0x45: InsRcvt(a); break;
                case 0x46: // GC
                case 0x47: // GC
                    InsGc(a);
                    break;

                case 0x48: InsScfs(a); break;
                case 0x49: // MD
                case 0x4A: // MD
                    InsMd(a);
                    break;

                case 0x4B: Stack[a] = CurrentPpem(); break; // MPPEM
                case 0x4C: InsMps(a); break;
                case 0x4D: GS.AutoFlip = true; break; // FLIPON
                case 0x4E: GS.AutoFlip = false; break; // FLIPOFF
                case 0x4F: Error = TtError.DebugOpCode; break; // DEBUG
                case 0x50: Stack[a] = Stack[a] < Stack[a + 1] ? 1 : 0; break; // LT
                case 0x51: Stack[a] = Stack[a] <= Stack[a + 1] ? 1 : 0; break; // LTEQ
                case 0x52: Stack[a] = Stack[a] > Stack[a + 1] ? 1 : 0; break; // GT
                case 0x53: Stack[a] = Stack[a] >= Stack[a + 1] ? 1 : 0; break; // GTEQ
                case 0x54: Stack[a] = Stack[a] == Stack[a + 1] ? 1 : 0; break; // EQ
                case 0x55: Stack[a] = Stack[a] != Stack[a + 1] ? 1 : 0; break; // NEQ
                case 0x56: Stack[a] = (Round(Stack[a], 0) & 64) == 64 ? 1 : 0; break; // ODD
                case 0x57: Stack[a] = (Round(Stack[a], 0) & 64) == 0 ? 1 : 0; break; // EVEN
                case 0x58: InsIf(a); break;
                case 0x59: break; // EIF
                case 0x5A: Stack[a] = Stack[a] != 0 && Stack[a + 1] != 0 ? 1 : 0; break; // AND
                case 0x5B: Stack[a] = Stack[a] != 0 || Stack[a + 1] != 0 ? 1 : 0; break; // OR
                case 0x5C: Stack[a] = Stack[a] == 0 ? 1 : 0; break; // NOT
                case 0x5D: InsDeltap(a); break;
                case 0x5E: GS.DeltaBase = (ushort)Stack[a]; break; // SDB
                case 0x5F: InsSds(a); break;
                case 0x60: Stack[a] = unchecked(Stack[a] + Stack[a + 1]); break; // ADD
                case 0x61: Stack[a] = unchecked(Stack[a] - Stack[a + 1]); break; // SUB
                case 0x62: InsDiv(a); break;
                case 0x63: Stack[a] = FtCalc.MulDiv(Stack[a], Stack[a + 1], 64); break; // MUL
                case 0x64: if (Stack[a] < 0) Stack[a] = unchecked(-Stack[a]); break; // ABS
                case 0x65: Stack[a] = unchecked(-Stack[a]); break; // NEG
                case 0x66: Stack[a] = FtCalc.PixFloor(Stack[a]); break; // FLOOR
                case 0x67: Stack[a] = FtCalc.PixFloor(unchecked(Stack[a] + 63)); break; // CEILING
                case 0x68: // ROUND
                case 0x69: // ROUND
                case 0x6A: // ROUND
                case 0x6B: // ROUND
                    Stack[a] = Round(Stack[a], 0); // the engine compensation is zero
                    break;

                case 0x6C: // NROUND
                case 0x6D: // NROUND
                case 0x6E: // NROUND
                case 0x6F: // NROUND
                    Stack[a] = RoundNone(Stack[a], 0);
                    break;

                case 0x70: InsWcvtf(a); break;
                case 0x71: // DELTAP2
                case 0x72: // DELTAP3
                    InsDeltap(a);
                    break;

                case 0x73: // DELTAC1
                case 0x74: // DELTAC2
                case 0x75: // DELTAC3
                    InsDeltac(a);
                    break;

                case 0x76: // SROUND
                    SetSuperRound(0x4000, Stack[a]);
                    GS.RoundState = TtRound.Super;
                    _roundFunc = TtRound.Super;
                    break;

                case 0x77: // S45Round
                    SetSuperRound(0x2D41, Stack[a]);
                    GS.RoundState = TtRound.Super45;
                    _roundFunc = TtRound.Super45;
                    break;

                case 0x78: // JROT
                    if (Stack[a + 1] != 0)
                        InsJmpr(a);
                    break;

                case 0x79: // JROF
                    if (Stack[a + 1] == 0)
                        InsJmpr(a);
                    break;

                case 0x7A: // ROFF
                    GS.RoundState = TtRound.Off;
                    _roundFunc = TtRound.Off;
                    break;

                case 0x7B: InsUnknown(); break;
                case 0x7C: // RUTG
                    GS.RoundState = TtRound.UpToGrid;
                    _roundFunc = TtRound.UpToGrid;
                    break;

                case 0x7D: // RDTG
                    GS.RoundState = TtRound.DownToGrid;
                    _roundFunc = TtRound.DownToGrid;
                    break;

                case 0x7E: break; // SANGW: instruction not supported anymore
                case 0x7F: break; // AA: intentionally no longer supported
                case 0x80: InsFlippt(a); break;
                case 0x81: InsFliprgon(a); break;
                case 0x82: InsFliprgoff(a); break;
                case 0x83: // UNKNOWN
                case 0x84: // UNKNOWN
                    InsUnknown();
                    break;

                case 0x85: InsScanctrl(a); break;
                case 0x86: // SDPvTL
                case 0x87: // SDPvTL
                    InsSdpvtl(a);
                    break;

                case 0x88: InsGetinfo(a); break;
                case 0x89: InsIdef(a); break;
                case 0x8A: // ROLL
                {
                    int A = Stack[a + 2];
                    int B = Stack[a + 1];
                    int C = Stack[a];

                    Stack[a + 2] = C;
                    Stack[a + 1] = A;
                    Stack[a] = B;
                    break;
                }

                case 0x8B: if (Stack[a + 1] > Stack[a]) Stack[a] = Stack[a + 1]; break; // MAX
                case 0x8C: if (Stack[a + 1] < Stack[a]) Stack[a] = Stack[a + 1]; break; // MIN
                case 0x8D: if (Stack[a] >= 0) GS.ScanType = Stack[a] & 0xFFFF; break; // SCANTYPE
                case 0x8E: InsInstctrl(a); break;
                case 0x8F: // ADJUST
                case 0x90: // ADJUST
                    InsUnknown();
                    break;

                case 0x91:
                    // it is the job of the application to `activate' GX handling, that is, calling any of the GX API
                    // functions on the current font to select a variation instance
                    if (HasBlend)
                        InsGetvariation(a);
                    else
                        InsUnknown();
                    break;

                case 0x92:
                    // there is at least one MS font (LaoUI.ttf version 5.01) that uses IDEFs for 0x91 and 0x92; for this
                    // reason we activate GETDATA for GX fonts only, similar to GETVARIATION
                    if (HasBlend)
                        Stack[a] = 17;
                    else
                        InsUnknown();
                    break;

                default:
                    if (opcode >= 0xE0)
                        InsMirp(a);
                    else if (opcode >= 0xC0)
                        InsMdrp(a);
                    else if (opcode >= 0xB8)
                        InsPushw(a);
                    else if (opcode >= 0xB0)
                        InsPushb(a);
                    else
                        InsUnknown();
                    break;
            }

            if (Error != 0)
            {
                if (Error == TtError.InvalidOpcode)
                {
                    // looking for redefined instructions
                    bool redefined = false;
                    for (int d = 0; d < NumIDefs; d++)
                    {
                        if (IDefs[d].Active && _opcode == (byte)IDefs[d].Opc)
                        {
                            if (_callTop >= CallStackSize)
                            {
                                Error = TtError.InvalidReference;
                                return Error;
                            }

                            ref TtCallRecord callrec = ref _callStack[_callTop];

                            callrec.CallerRange = CurRange;
                            callrec.CallerIp = _ip + 1;
                            callrec.CurCount = 1;
                            callrec.DefIsInstruction = true;
                            callrec.DefIndex = d;

                            if (GotoCodeRange(IDefs[d].Range, IDefs[d].Start))
                                return Error;

                            redefined = true;
                            break;
                        }
                    }

                    if (!redefined)
                        return Error;

                    // LSuiteLabel_
                }
                else
                {
                    return Error;
                }
            }
            else
            {
                Top = NewTop;
                _ip += _length;
            }

            // LSuiteLabel_:
            if (_ip >= _codeSize)
            {
                if (_callTop > 0)
                {
                    Error = TtError.CodeOverflow;
                    return Error;
                }

                return TtError.Ok;
            }
        }
        while (!InstructionTrap);

        return TtError.Ok;
    }

    /// <summary>
    /// Executes the program set up in the context (<c>TT_Run_Context</c>): the zones <see cref="Pts"/> and
    /// <see cref="Twilight"/> and the code range are the caller's to prepare, the graphics state comes from the size's.
    /// </summary>
    public int RunContext(in TtGraphicsState sizeGraphicsState)
    {
        Zp0 = Pts;
        Zp1 = Pts;
        Zp2 = Pts;

        // We restrict the number of twilight points to a reasonable, heuristic value to avoid slow execution of malformed
        // bytecode. The selected value is large enough to support fonts hinted with `ttfautohint`, which uses twilight
        // points to store vertical coordinates of (auto-hinter) segments.
        long numTwilightPoints = Math.Max(30, 2 * ((long)Pts.NPoints + CvtSize));
        if (Twilight.NPoints > numTwilightPoints)
        {
            if (numTwilightPoints > 0xFFFF)
                numTwilightPoints = 0xFFFF;

            Twilight.NPoints = (int)numTwilightPoints;
        }

        // Set up loop detectors. We restrict the number of LOOPCALL loops and the number of JMPR, JROT, and JROF calls with
        // a negative argument to values that depend on various parameters like the size of the CVT table or the number of
        // points in the current glyph (if applicable).
        //
        // The idea is that in real-world bytecode you either iterate over all CVT entries (in the `prep' table), or over
        // all points (or contours, in the `glyf' table) of a glyph, and such iterations don't happen very often.
        _loopcallCounter = 0;
        _negJumpCounter = 0;

        // The maximum values are heuristic.
        if (Pts.NPoints != 0)
            _loopcallCounterMax = (ulong)(Math.Max(50, 10 * Pts.NPoints) + Math.Max(50u, (uint)CvtSize / 10));
        else
            _loopcallCounterMax = 300ul + 22ul * (uint)CvtSize;

        // as a protection against an unreasonable number of CVT entries we assume at most 100 control values per glyph for
        // the counter
        if (_loopcallCounterMax > 100ul * (uint)NumGlyphs)
            _loopcallCounterMax = 100ul * (uint)NumGlyphs;

        _negJumpCounterMax = _loopcallCounterMax;

        // reset graphics state
        GS = sizeGraphicsState;
        _roundFunc = TtRound.ToGrid;
        ComputeFuncs();

        // Reset IUP tracking bits in the backward compatibility mode. See the notes on that mode in FreeType's ttinterp.h.
        BackwardCompatibility &= ~0x3;

        // some glyphs leave something on the stack, so we clean it before a new execution
        Top = 0;
        _callTop = 0;

        InstructionTrap = false;

        return RunIns();
    }
}
