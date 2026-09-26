/****************************************************************************
 *
 * psstack.c
 *
 *   Adobe's code for emulating a CFF stack (body).
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

/****************************************************************************
 *
 * psstack.h
 *
 *   Adobe's code for emulating a CFF stack (specification).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psstack.c, psstack.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>The kinds of number the operand stack holds (<c>CF2_NumberType</c>).</summary>
internal enum Cf2NumberType : byte
{
    /// <summary>16.16.</summary>
    Fixed,

    /// <summary>2.30.</summary>
    Frac,

    /// <summary>32.0.</summary>
    Int,
}

/// <summary>A number on the operand stack (<c>CF2_StackNumber</c>): a value and what it means.</summary>
internal struct Cf2StackNumber
{
    public int Value;
    public Cf2NumberType Type;
}

/// <summary>The operand stack of a charstring (<c>CF2_Stack</c>).</summary>
internal sealed class Cf2Stack
{
    private readonly Cf2StackNumber[] _buffer;
    private readonly Cf2Error _error;
    private int _top;

    /// <summary><c>cf2_stack_init</c>.</summary>
    public Cf2Stack(Cf2Error error, int stackSize)
    {
        _error = error;
        _buffer = new Cf2StackNumber[stackSize];
    }

    /// <summary><c>cf2_stack_count</c>.</summary>
    public int Count => _top;

    /// <summary><c>cf2_stack_pushInt</c>.</summary>
    public void PushInt(int val)
    {
        if (_top == _buffer.Length)
        {
            _error.Set(Cf2Error.StackOverflow);
            return; // stack overflow
        }

        _buffer[_top].Value = val;
        _buffer[_top].Type = Cf2NumberType.Int;
        _top++;
    }

    /// <summary><c>cf2_stack_pushFixed</c>.</summary>
    public void PushFixed(int val)
    {
        if (_top == _buffer.Length)
        {
            _error.Set(Cf2Error.StackOverflow);
            return; // stack overflow
        }

        _buffer[_top].Value = val;
        _buffer[_top].Type = Cf2NumberType.Fixed;
        _top++;
    }

    /// <summary><c>cf2_stack_popInt</c>: this function is only allowed to pop an integer type.</summary>
    public int PopInt()
    {
        if (_top == 0)
        {
            _error.Set(Cf2Error.StackUnderflow);
            return 0; // underflow
        }

        if (_buffer[_top - 1].Type != Cf2NumberType.Int)
        {
            _error.Set(Cf2Error.SyntaxError);
            return 0; // type mismatch
        }

        _top--;

        return _buffer[_top].Value;
    }

    /// <summary><c>cf2_stack_popFixed</c>: type mismatch is silently cast.</summary>
    public int PopFixed()
    {
        if (_top == 0)
        {
            _error.Set(Cf2Error.StackUnderflow);
            return Cf2Fixed.FromInt(0); // underflow
        }

        _top--;

        return _buffer[_top].Type switch
        {
            Cf2NumberType.Int => Cf2Fixed.FromInt(_buffer[_top].Value),
            Cf2NumberType.Frac => Cf2Fixed.FracToFixed(_buffer[_top].Value),
            _ => _buffer[_top].Value,
        };
    }

    /// <summary><c>cf2_stack_getReal</c>: type mismatch is silently cast.</summary>
    public int GetReal(int idx)
    {
        if ((uint)idx >= (uint)_top)
        {
            _error.Set(Cf2Error.StackOverflow);
            return Cf2Fixed.FromInt(0); // bounds error
        }

        return _buffer[idx].Type switch
        {
            Cf2NumberType.Int => Cf2Fixed.FromInt(_buffer[idx].Value),
            Cf2NumberType.Frac => Cf2Fixed.FracToFixed(_buffer[idx].Value),
            _ => _buffer[idx].Value,
        };
    }

    /// <summary><c>cf2_stack_setReal</c>: provides random access to the stack.</summary>
    public void SetReal(int idx, int val)
    {
        if ((uint)idx > (uint)_top)
        {
            _error.Set(Cf2Error.StackOverflow);
            return;
        }

        // an index equal to the count is the first free slot, which FreeType allows to be written
        if (idx == _buffer.Length)
            return;

        _buffer[idx].Value = val;
        _buffer[idx].Type = Cf2NumberType.Fixed;
    }

    /// <summary><c>cf2_stack_pop</c>: discards (pops) <paramref name="num"/> values from the stack.</summary>
    public void Pop(int num)
    {
        if ((uint)num > (uint)_top)
        {
            _error.Set(Cf2Error.StackUnderflow);
            return;
        }

        _top -= num;
    }

    /// <summary><c>cf2_stack_roll</c>.</summary>
    public void Roll(int count, int shift)
    {
        Cf2StackNumber last = default;

        if (count < 2)
            return; // nothing to do (values 0 and 1), or undefined value

        if ((uint)count > (uint)_top)
        {
            _error.Set(Cf2Error.StackOverflow);
            return;
        }

        if (shift < 0)
            shift = -((-shift) % count);
        else
            shift %= count;

        if (shift == 0)
            return; // nothing to do

        // We use the following algorithm to do the rolling, which needs two temporary variables only.
        //
        // Example:
        //
        //   count = 8
        //   shift = 2
        //
        //   stack indices before roll:  7 6 5 4 3 2 1 0
        //   stack indices after roll:   1 0 7 6 5 4 3 2
        //
        // The value of index 0 gets moved to index 2, while the old value of index 2 gets moved to index 4, and so on.  We thus have
        // the following copying chains for shift value 2.
        //
        //   0 -> 2 -> 4 -> 6 -> 0
        //   1 -> 3 -> 5 -> 7 -> 1
        //
        // If `count' and `shift' are incommensurable, we have a single chain only.  Otherwise, increase the start index by 1 after
        // the first chain, then do the next chain until all elements in all chains are handled.
        int startIdx = -1;
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            if (startIdx == idx)
            {
                startIdx++;
                idx = startIdx;
                last = _buffer[idx];
            }

            idx += shift;
            if (idx >= count)
                idx -= count;
            else if (idx < 0)
                idx += count;

            Cf2StackNumber tmp = _buffer[idx];
            _buffer[idx] = last;
            last = tmp;
        }
    }

    /// <summary><c>cf2_stack_clear</c>.</summary>
    public void Clear() => _top = 0;
}
