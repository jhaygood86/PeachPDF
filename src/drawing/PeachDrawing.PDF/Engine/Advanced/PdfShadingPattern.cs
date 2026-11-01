// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using System.Numerics;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Rendering;
using System;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Represents a shading pattern dictionary.
/// </summary>
internal sealed class PdfShadingPattern : PdfDictionaryWithContentStream
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfShadingPattern"/> class.
    /// </summary>
    public PdfShadingPattern(PdfDocument document)
        : base(document)
    {
        Elements.SetName(Keys.Type, "/Pattern");
        Elements[Keys.PatternType] = new PdfInteger(2);
    }

    /// <summary>
    /// Setups the shading pattern from the specified brush.
    /// </summary>
    internal PdfExtGState? AlphaExtGState { get; private set; }

    internal void SetupFromBrush(Brush brush, Matrix3x2 matrix, PdfGraphicsRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(brush);

        PdfShading shading = new PdfShading(_document);

        if (brush is ConicGradientBrush)
        {
            // Type 4 shading has binary stream content; PDF spec requires stream objects to be indirect.
            _document._irefTable.Add(shading);
            shading.SetupFromBrush(brush, renderer);
            Elements.SetReference(Keys.Shading, shading);
            Elements.SetMatrix(Keys.Matrix, matrix);
        }
        else
        {
            shading.SetupFromBrush(brush, renderer);
            Elements[Keys.Shading] = shading;

            // An elliptical radial shading states its /Coords as the unit circle at the origin and
            // carries the ellipse's real size and position in EllipsePatternMatrix instead (see
            // PdfShading.SetupRadialMultiStop). That matrix maps the unit circle into exactly the
            // space a circular shading writes its /Coords in - it does NOT reach page space on its
            // own - so it composes with the view matrix rather than replacing it. Replacing it
            // dropped the CTM, which is identity only while nothing is transformed; under an SVG
            // viewBox scale or a CSS transform it placed the shading in raw page coordinates,
            // usually off the page entirely, leaving the shape filled with the flat /Extend color.
            if (shading.EllipsePatternMatrix is { } ellipseMatrix)
                matrix = ellipseMatrix * matrix;

            Elements.SetMatrix(Keys.Matrix, matrix);
        }

        AlphaExtGState = shading.AlphaExtGState;
    }

    /// <summary>
    /// Common keys for all streams.
    /// </summary>
    internal sealed new class Keys : PdfDictionaryWithContentStream.Keys
    {
        /// <summary>
        /// (Optional) The type of PDF object that this dictionary describes; if present,
        /// must be Pattern for a pattern dictionary.
        /// </summary>
        [PdfKey(PdfKeyType.Name | PdfKeyType.Required)]
        public const string Type = "/Type";

        /// <summary>
        /// (Required) A code identifying the type of pattern that this dictionary describes;
        /// must be 2 for a shading pattern.
        /// </summary>
        [PdfKey(PdfKeyType.Integer | PdfKeyType.Required)]
        public const string PatternType = "/PatternType";

        /// <summary>
        /// (Required) A shading object (see below) defining the shading pattern's gradient fill.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Required)]
        public const string Shading = "/Shading";

        /// <summary>
        /// (Optional) An array of six numbers specifying the pattern matrix.
        /// Default value: the identity matrix [1 0 0 1 0 0].
        /// </summary>
        [PdfKey(PdfKeyType.Array | PdfKeyType.Optional)]
        public const string Matrix = "/Matrix";

        /// <summary>
        /// (Optional) A graphics state parameter dictionary containing graphics state parameters
        /// to be put into effect temporarily while the shading pattern is painted. Any parameters
        /// that are not so specified are inherited from the graphics state that was in effect
        /// at the beginning of the content stream in which the pattern is defined as a resource.
        /// </summary>
        [PdfKey(PdfKeyType.Dictionary | PdfKeyType.Optional)]
        public const string ExtGState = "/ExtGState";

        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        internal static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
