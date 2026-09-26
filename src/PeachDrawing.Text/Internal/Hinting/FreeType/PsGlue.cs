/****************************************************************************
 *
 * psglue.h
 *
 *   Adobe's code for shared stuff (specification only).
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
 * pserror.h
 *
 *   Adobe's code for error handling (specification).
 *
 * Copyright 2006-2013 Adobe Systems Incorporated.
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
 * psread.h
 *
 *   Adobe's code for stream handling (specification).
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
 * psarrst.h
 *
 *   Adobe's code for Array Stacks (specification).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psglue.h, pserror.h, psread.h, psarrst.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>A point in 16.16 or 26.6 units, as <c>FT_Vector</c> is.</summary>
internal struct FtVector
{
    public int X, Y;
}

/// <summary>
/// The error of a run of Adobe's engine (<c>FT_Error*</c> shared by its objects). Only the first error is recorded, which is what the
/// engine relies on: it goes on after most errors and reports the first at the end.
/// </summary>
internal sealed class Cf2Error
{
    // the FreeType errors the engine raises
    public const int InvalidGlyphFormat = 0x09;
    public const int StackOverflow = 0x92;
    public const int StackUnderflow = 0x91;
    public const int SyntaxError = 0x8F;
    public const int GlyphTooBig = 0x17;
    public const int InvalidSizeHandle = 0x23;
    public const int InvalidArgument = 0x06;

    public int Value;

    /// <summary><c>cf2_setError</c>: records an error unless one was recorded.</summary>
    public void Set(int error)
    {
        if (Value == 0)
            Value = error;
    }
}

/// <summary>The elements of a glyph outline (<c>CF2_PathOp</c>).</summary>
internal static class Cf2PathOp
{
    public const int MoveTo = 1;
    public const int LineTo = 2;
    public const int QuadTo = 3;
    public const int CubeTo = 4;
}

/// <summary>A matrix of 16.16 numbers (<c>CF2_Matrix</c>).</summary>
internal struct Cf2Matrix
{
    public int A, B, C, D, Tx, Ty;
}

/// <summary>What a path element calls its consumer with (<c>CF2_CallbackParamsRec</c>).</summary>
internal struct Cf2CallbackParams
{
    public FtVector Pt0, Pt1, Pt2, Pt3;
    public int Op;
}

/// <summary>The consumer of the outline Adobe's engine builds (<c>CF2_OutlineCallbacksRec</c>).</summary>
internal abstract class Cf2OutlineCallbacks
{
    /// <summary>The momentum of the winding, to detect the direction of a contour.</summary>
    public int WindingMomentum;

    public abstract void MoveTo(in Cf2CallbackParams p);

    public abstract void LineTo(in Cf2CallbackParams p);

    public abstract void CubeTo(in Cf2CallbackParams p);
}

/// <summary>
/// A region of charstring bytes that reads bytes with a check for the end (<c>CF2_Buffer</c>): a read past the end returns zero.
/// </summary>
internal sealed class Cf2Buffer
{
    public byte[] Data = [];
    public int Start;
    public int End;
    public int Ptr;

    public void CopyFrom(Cf2Buffer other)
    {
        Data = other.Data;
        Start = other.Start;
        End = other.End;
        Ptr = other.Ptr;
    }

    public void Set(byte[] data, int start, int end)
    {
        Data = data;
        Start = start;
        End = end;
        Ptr = start;
    }

    /// <summary><c>cf2_buf_readByte</c>: reading past the end of the buffer returns zero (its error is not recorded by the callers).</summary>
    public int ReadByte()
    {
        if (Ptr < End)
            return Data[Ptr++];

        return 0;
    }

    /// <summary><c>cf2_buf_isEnd</c>: note the end condition can occur without error.</summary>
    public bool IsEnd() => Ptr >= End;
}

/// <summary>
/// A growable array with a shared error (<c>CF2_ArrStack</c>): reading an element that is not there is an error and gives the first.
/// </summary>
internal sealed class Cf2ArrStack<T>
    where T : new()
{
    private readonly Cf2Error _error;
    private T[] _items = [];

    public Cf2ArrStack(Cf2Error error) => _error = error;

    /// <summary><c>cf2_arrstack_size</c>: the number of items.</summary>
    public int Count { get; private set; }

    /// <summary><c>cf2_arrstack_clear</c>.</summary>
    public void Clear() => Count = 0;

    /// <summary><c>cf2_arrstack_setCount</c>: sets the count, ensuring the allocation is sufficient.</summary>
    public void SetCount(int numElements)
    {
        if (numElements > _items.Length)
            Grow(numElements);

        Count = numElements;
    }

    private void Grow(int numElements)
    {
        int old = _items.Length;
        Array.Resize(ref _items, numElements);
        for (int i = old; i < numElements; i++)
            _items[i] = new T();
    }

    /// <summary><c>cf2_arrstack_getPointer</c>: a reference to the element at an index; an index that is not there is an error and gives the first.</summary>
    public ref T GetRef(int idx)
    {
        if (idx >= Count || idx < 0)
        {
            // overflow
            _error.Set(Cf2Error.StackOverflow);
            idx = 0; // choose safe default
        }

        if (_items.Length == 0)
            Grow(1);

        return ref _items[idx];
    }

    /// <summary><c>cf2_arrstack_push</c>: appends an element.</summary>
    public void Push(in T item)
    {
        if (Count == _items.Length)
            Grow(_items.Length * 2 + 16);

        _items[Count] = item;
        Count++;
    }
}
