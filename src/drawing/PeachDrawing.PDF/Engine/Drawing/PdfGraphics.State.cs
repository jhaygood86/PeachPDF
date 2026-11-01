// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Rendering;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;

#pragma warning disable 1587
// ReSharper disable UseNullPropagation
// ReSharper disable RedundantNameQualifier
// ReSharper disable UseNameofExpression

namespace PeachDrawing.PDF.Engine.Drawing;

internal sealed partial class PdfGraphics : IDisposable
{
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Saves the current state of this PdfGraphics object and identifies the saved state with the
    /// returned PdfGraphicsStateToken object.
    /// </summary>
    public PdfGraphicsStateToken Save()
    {
        PdfGraphicsStateToken? xState = null;
        xState = new PdfGraphicsStateToken();
        InternalGraphicsState iState = new InternalGraphicsState(this, xState);
        iState.Transform = _transform;
        _gsStack.Push(iState);

        if (_renderer is not null)
            _renderer.Save(xState);

        return xState;
    }

    /// <summary>
    /// Restores the state of this PdfGraphics object to the state represented by the specified 
    /// PdfGraphicsStateToken object.
    /// </summary>
    public void Restore(PdfGraphicsStateToken state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _gsStack.Restore(state.InternalState);
        _transform = state.InternalState.Transform;

        if (_renderer is not null)
            _renderer.Restore(state);
    }

    /// <summary>
    /// Restores the state of this PdfGraphics object to the state before the most recently call of Save.
    /// </summary>
    public void Restore()
    {
        if (_gsStack.Count == 0)
            throw new InvalidOperationException("Cannot restore without preceding save operation.");
        Restore(_gsStack.Current.State);
    }

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Multiplies the transformation matrix of this object and specified matrix.
    /// </summary>
    public void MultiplyTransform(Matrix3x2 matrix)
    {
        AddTransform(matrix);
    }

    /// <summary>
    /// Translates the coordinate system by prepending a translation to the transformation matrix of this object.
    /// </summary>
    public void TranslateTransform(double dx, double dy)
    {
        AddTransform(PdfMatrix.Translation(dx, dy));
    }

    /// <summary>
    /// Applies a new transformation to the current transformation matrix.
    /// </summary>
    void AddTransform(Matrix3x2 transform)
    {
        _transform = transform * _transform;
        if (_renderer is not null)
            _renderer.AddTransform(transform);
    }

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Updates the clip region of this PdfGraphics to the intersection of the 
    /// current clip region and the specified rectangle.
    /// </summary>
    public void IntersectClip(Rect rect)
    {
        GraphicsPath path = new PdfGraphicsPath();
        path.AddRectangle(rect);
        IntersectClip(path);
    }

    /// <summary>
    /// Updates the clip region of this PdfGraphics to the intersection of the 
    /// current clip region and the specified graphical path.
    /// </summary>
    public void IntersectClip(GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (_renderer is not null)
            _renderer.IntersectClip(path);
    }

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a comment to the output stream. Comments have no effect on the rendering of the output.
    /// They may be useful to mark a position in a content stream of a PDF document.
    /// </summary>
    public void WriteComment(string comment)
    {
        ArgumentNullException.ThrowIfNull(comment);

        if (_drawGraphics)
        {
            // TODO: Do something if metafile?
        }

        if (_renderer is not null)
            _renderer.WriteComment(comment);
    }

    /// <summary>
    /// Activates a separable/HSL PDF blend mode for subsequent painting - see
    /// <see cref="PdfGraphicsRenderer.SetBlendMode"/>. Scope with <see cref="Save()"/>/
    /// <see cref="Restore(PdfGraphicsStateToken)"/> so the blend mode is restored to Normal afterward.
    /// </summary>
    internal void SetBlendMode(string pdfBlendModeName)
    {
        (_renderer as PdfGraphicsRenderer)?.SetBlendMode(pdfBlendModeName);
    }

    /// <summary>
    /// Begins a tagged marked-content sequence for the given structure type and marked-content
    /// identifier - see <see cref="PdfGraphicsRenderer.BeginMarkedContent"/>.
    /// </summary>
    internal void BeginMarkedContent(string structureType, int mcid)
    {
        (_renderer as PdfGraphicsRenderer)?.BeginMarkedContent(structureType, mcid);
    }

    /// <summary>
    /// Ends a marked-content sequence started by <see cref="BeginMarkedContent"/> or
    /// <see cref="BeginArtifact"/>.
    /// </summary>
    internal void EndMarkedContent()
    {
        (_renderer as PdfGraphicsRenderer)?.EndMarkedContent();
    }

    /// <summary>
    /// Begins an artifact marked-content sequence - see
    /// <see cref="PdfGraphicsRenderer.BeginArtifact"/>.
    /// </summary>
    internal void BeginArtifact()
    {
        (_renderer as PdfGraphicsRenderer)?.BeginArtifact();
    }

    /// <summary>
    /// Begins the "/Tx" marked-content sequence around a form field's variable text - see
    /// <see cref="PdfGraphicsRenderer.BeginVariableText"/>.
    /// </summary>
    internal void BeginVariableText()
    {
        (_renderer as PdfGraphicsRenderer)?.BeginVariableText();
    }

    /// <summary>Closes the sequence <see cref="BeginVariableText"/> opened.</summary>
    internal void EndVariableText()
    {
        (_renderer as PdfGraphicsRenderer)?.EndVariableText();
    }
}
