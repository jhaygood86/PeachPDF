// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;

namespace PeachDrawing.PDF.Engine.Drawing;

// In GDI+ the functions Save/Restore, BeginContainer/EndContainer, Transform, SetClip and ResetClip
// can be combined in any order. E.g. you can set a clip region, save the graphics state, empty the
// clip region and draw without clipping. Then you can restore to the previous clip region. With PDF
// this behavior is hard to implement. To solve this problem I first an automaton that keeps track
// of all clipping paths and the current transformation when the clip path was set. The automation
// manages a PDF graphics state stack to calculate the desired bahaviour. It also takes into consideration
// not to multiply with inverse matrixes when the user sets a new transformation matrix.
// After the design works on pager I decided not to implement it because it is much to large-scale.
// Instead I lay down some rules how to use the PdfGraphics class.
//
// * Before you set a transformation matrix save the graphics state (Save) or begin a new container
//   (BeginContainer).
// 
// * Instead of resetting the transformation matrix, call Restore or EndContainer. If you reset the
//   transformation, in PDF must be multiplied with the inverse matrix. That leads to round off errors
//   because in PDF file only 3 digits are used and Acrobat internally uses fixed point numbers (until
//   versioin 6 or 7 I think).
//
// * When no clip path is defined, you can set or intersect a new path.
//
// * When a clip path is already defined, you can always intersect with a new one (wich leads in general
//   to a smaller clip region).
//
// * When a clip path is already defined, you can only reset it to the empty region (ResetClip) when
//   the graphics state stack is at the same position as it had when the clip path was defined. Otherwise
//   an error occurs.
//
// Keeping these rules leads to easy to read code and best results in PDF output.

/// <summary>
/// Represents the internal state of an PdfGraphics object.
/// Used when the state is saved and restored.
/// </summary>
internal sealed class InternalGraphicsState
{
    public InternalGraphicsState(PdfGraphics gfx)
    {
        _gfx = gfx;
    }

    public InternalGraphicsState(PdfGraphics gfx, PdfGraphicsStateToken state)
    {
        _gfx = gfx;
        State = state;
        State.InternalState = this;
    }

    /// <summary>
    /// Gets or sets the current transformation matrix.
    /// </summary>
    public Matrix3x2 Transform
    {
        get => _transform;
        set => _transform = value;
    }
    Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>
    /// Called after this instanced was pushed on the internal graphics stack.
    /// </summary>
    public void Pushed()
    {
    }

    /// <summary>
    /// Called after this instanced was popped from the internal graphics stack.
    /// </summary>
    public void Popped()
    {
        Invalid = true;
    }

    public bool Invalid;

    readonly PdfGraphics _gfx;

    internal PdfGraphicsStateToken State = null!;
}
