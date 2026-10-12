// Derived from PDFsharp (MIT) - see LICENSE.md

using System;
using System.Collections.Generic;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Represents a stack of PdfGraphicsStateToken and graphics container objects.
/// </summary>
internal sealed class GraphicsStateStack
{
    public GraphicsStateStack(PdfGraphics gfx)
    {
        _current = new InternalGraphicsState(gfx);
    }

    public int Count => _stack.Count;

    public void Push(InternalGraphicsState state)
    {
        _stack.Push(state);
        state.Pushed();
    }

    public int Restore(InternalGraphicsState state)
    {
        if (!_stack.Contains(state))
            throw new ArgumentException("State not on stack.", "state");
        if (state.Invalid)
            throw new ArgumentException("State already restored.", "state");

        int count = 1;
        InternalGraphicsState top = _stack.Pop();
        top.Popped();
        while (top != state)
        {
            count++;
            state.Invalid = true;
            top = _stack.Pop();
            top.Popped();
        }
        state.Invalid = true;
        return count;
    }

    public InternalGraphicsState Current
    {
        get
        {
            if (_stack.Count == 0)
                return _current;
            return _stack.Peek();
        }
    }

    readonly InternalGraphicsState _current;

    readonly Stack<InternalGraphicsState> _stack = new Stack<InternalGraphicsState>();
}
