// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Collections.Generic;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Contains all used ExtGState objects of a document.
/// </summary>
internal sealed class PdfExtGStateTable : PdfResourceTable
{
    /// <summary>
    /// Initializes a new instance of this class, which is a singleton for each document.
    /// </summary>
    public PdfExtGStateTable(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Gets a PdfExtGState with the key 'CA' set to the specified alpha value.
    /// </summary>
    public PdfExtGState GetExtGStateStroke(double alpha)
    {
        string key = PdfExtGState.MakeKey(alpha);
        PdfExtGState? extGState;
        if (!_strokeAlphaValues.TryGetValue(key, out extGState))
        {
            extGState = new PdfExtGState(Owner);
            //extGState.Elements[PdfExtGState.Keys.CA] = new PdfReal(alpha);
            extGState.StrokeAlpha = alpha;
            _strokeAlphaValues[key] = extGState;
        }
        return extGState;
    }

    /// <summary>
    /// Gets a PdfExtGState with the key 'ca' set to the specified alpha value.
    /// </summary>
    public PdfExtGState GetExtGStateNonStroke(double alpha)
    {
        string key = PdfExtGState.MakeKey(alpha);
        PdfExtGState? extGState;
        if (!_nonStrokeStates.TryGetValue(key, out extGState))
        {
            extGState = new PdfExtGState(Owner);
            //extGState.Elements[PdfExtGState.Keys.ca] = new PdfReal(alpha);
            extGState.NonStrokeAlpha = alpha;
            _nonStrokeStates[key] = extGState;
        }
        return extGState;
    }

    /// <summary>
    /// Gets a PdfExtGState with both 'ca' (non-stroking alpha) and 'CA' (stroking alpha) set to the
    /// same alpha value - used to composite a whole transparency-group Form XObject (e.g. for CSS/SVG
    /// group <c>opacity</c>) with a single <c>gs</c> operator, rather than needing separate stroke/non-stroke states.
    /// </summary>
    public PdfExtGState GetExtGState(double alpha)
    {
        string key = PdfExtGState.MakeKey(alpha);
        PdfExtGState? extGState;
        if (!_combinedAlphaStates.TryGetValue(key, out extGState))
        {
            extGState = new PdfExtGState(Owner)
            {
                StrokeAlpha = alpha,
                NonStrokeAlpha = alpha
            };

            _combinedAlphaStates[key] = extGState;
        }
        return extGState;
    }

    /// <summary>
    /// Same as <see cref="GetExtGState(double)"/>, additionally setting a non-Normal <c>/BM</c> in
    /// the same ExtGState - the mechanism behind CSS <c>mix-blend-mode</c> combined with group
    /// <c>opacity</c> on one tile, one <c>gs</c> rather than two nested ones. <paramref name="pdfBlendModeName"/>
    /// of <c>"Normal"</c> is cached and looked up identically to a call to the single-parameter
    /// overload - the two never diverge into separate cache entries for the same effective state.
    /// </summary>
    public PdfExtGState GetExtGState(double alpha, string pdfBlendModeName)
    {
        if (pdfBlendModeName is null or "Normal")
            return GetExtGState(alpha);

        string key = PdfExtGState.MakeKey(alpha) + pdfBlendModeName;
        PdfExtGState? extGState;
        if (!_combinedAlphaBlendStates.TryGetValue(key, out extGState))
        {
            extGState = new PdfExtGState(Owner)
            {
                StrokeAlpha = alpha,
                NonStrokeAlpha = alpha
            };
            extGState.Elements.SetName(PdfExtGState.Keys.BM, pdfBlendModeName);

            _combinedAlphaBlendStates[key] = extGState;
        }
        return extGState;
    }

    readonly Dictionary<string, PdfExtGState> _strokeAlphaValues = new Dictionary<string, PdfExtGState>();
    readonly Dictionary<string, PdfExtGState> _nonStrokeStates = new Dictionary<string, PdfExtGState>();
    readonly Dictionary<string, PdfExtGState> _combinedAlphaStates = new Dictionary<string, PdfExtGState>();
    readonly Dictionary<string, PdfExtGState> _combinedAlphaBlendStates = new Dictionary<string, PdfExtGState>();
}
