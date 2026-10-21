#nullable enable

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// An annotation to put on a page that is neither a link nor a form field: a highlight, a note, a stamp, a drawing. It says what kind of
    /// annotation it is, where it sits, what it holds, and how it looks.
    /// </summary>
    public sealed class PdfAnnotationDefinition
    {
        /// <summary>The annotation type as the PDF names it, without the slash: <c>Highlight</c>, <c>Text</c>, <c>Square</c>, <c>Stamp</c>, <c>Ink</c>...</summary>
        public required string Subtype { get; init; }

        /// <summary>
        /// Where the annotation sits, in points from the page's top-left corner with y down: its box, or the bounding box of the drawn shape when it
        /// was turned.
        /// </summary>
        public required Rect Bounds { get; init; }

        /// <summary>The size, in points, the appearance is drawn at.</summary>
        public required Size ContentSize { get; init; }

        /// <summary>
        /// The linear part of the transform that carries the upright appearance to where the annotation sits (translation left out), in the same
        /// y-down convention as <see cref="Bounds"/>, or <see langword="null"/> when it is not turned.
        /// </summary>
        public Matrix3x2? AppearanceTransform { get; init; }

        /// <summary>Draws the annotation's appearance onto a canvas of <see cref="ContentSize"/>, or <see langword="null"/> for an annotation with none.</summary>
        public Action<Canvas>? PaintAppearance { get; init; }

        /// <summary>The layout units per point the appearance is drawn at.</summary>
        public double PixelsPerPoint { get; init; } = 1;

        /// <summary>The text of the annotation (a note's text, or a description of what it marks).</summary>
        public string? Contents { get; init; }

        /// <summary>The name of the person who made the annotation.</summary>
        public string? Title { get; init; }

        /// <summary>A short description of the annotation's subject.</summary>
        public string? Subject { get; init; }

        /// <summary>The annotation's colour (its border, a note's background, a highlight's fill), or <see langword="null"/>.</summary>
        public PaintColor? Color { get; init; }

        /// <summary>The colour that fills the inside of a closed shape, or <see langword="null"/>.</summary>
        public PaintColor? InteriorColor { get; init; }

        /// <summary>The opacity the annotation is drawn with, from 0 to 1, or <see langword="null"/> for opaque.</summary>
        public double? Opacity { get; init; }

        /// <summary>The width of the annotation's border, in points, or <see langword="null"/> for the default.</summary>
        public double? BorderWidth { get; init; }

        /// <summary>The name of a note's or stamp's icon (<c>Comment</c>, <c>Approved</c>...), or <see langword="null"/>.</summary>
        public string? IconName { get; init; }

        /// <summary>
        /// The regions a text markup annotation (a highlight, underline, strike-out or squiggly) covers, four points per region, in points from the
        /// page's top-left corner with y down.
        /// </summary>
        public IReadOnlyList<PaintPoint> QuadPoints { get; init; } = [];

        /// <summary>The strokes of an ink annotation, each a run of points in points from the page's top-left corner with y down.</summary>
        public IReadOnlyList<IReadOnlyList<PaintPoint>> InkStrokes { get; init; } = [];

        /// <summary>The vertices of a polygon or polyline annotation, in points from the page's top-left corner with y down.</summary>
        public IReadOnlyList<PaintPoint> Vertices { get; init; } = [];

        /// <summary>The two ends of a line annotation, in points from the page's top-left corner with y down; empty for any other kind.</summary>
        public IReadOnlyList<PaintPoint> LineEnds { get; init; } = [];

        /// <summary>Whether the annotation cannot be edited.</summary>
        public bool ReadOnly { get; init; }

        /// <summary>Whether the annotation cannot be deleted or moved.</summary>
        public bool Locked { get; init; }

        /// <summary>Whether the annotation keeps its size however the page is zoomed.</summary>
        public bool NoZoom { get; init; }

        /// <summary>Whether the annotation stays upright however the page is turned.</summary>
        public bool NoRotate { get; init; }

        /// <summary>Whether the annotation is left out when the page is printed.</summary>
        public bool NotPrinted { get; init; }
    }

    /// <summary>
    /// Adds annotations that are not links or form fields (highlights, notes, stamps, drawings) to the pages of a document, each with the
    /// appearance it is given, so a reader shows it as drawn.
    /// </summary>
    /// <remarks>Get one from <see cref="PdfComposer.CreateAnnotationBuilder"/>.</remarks>
    public sealed class PdfAnnotationBuilder
    {
        private readonly PdfDocument _document;
        private readonly PdfRenderContext _renderContext;

        internal PdfAnnotationBuilder(PdfDocument document, PdfRenderContext renderContext)
        {
            _document = document;
            _renderContext = renderContext;
        }

        /// <summary>Puts an annotation on a page.</summary>
        /// <param name="page">the page it goes on</param>
        /// <param name="annotation">what it is, where it is and how it looks</param>
        public void AddAnnotation(PdfComposerPage page, PdfAnnotationDefinition annotation)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(annotation);

            var height = page.HeightPoints;
            var bounds = annotation.Bounds;
            var engine = new PdfGenericAnnotation(_document, annotation.Subtype)
            {
                Rectangle = new PdfRectangle(new Rect(bounds.X, height - (bounds.Y + bounds.Height), bounds.Width, bounds.Height)),
                Text = annotation.Contents,
            };

            engine.SetString("/T", annotation.Title);
            engine.SetString("/Subj", annotation.Subject);
            if (annotation.Color is { } color)
                engine.SetNumbers("/C", Components(color));
            if (annotation.InteriorColor is { } interior)
                engine.SetNumbers("/IC", Components(interior));
            if (annotation.Opacity is { } opacity)
                engine.SetNumber("/CA", Math.Clamp(opacity, 0, 1));
            if (annotation.BorderWidth is { } width)
                engine.SetNumbers("/Border", [0, 0, Math.Max(0, width)]);
            if (annotation.IconName is { Length: > 0 } icon)
                engine.SetName("/Name", icon);

            if (annotation.QuadPoints.Count > 0)
                engine.SetNumbers("/QuadPoints", Flip(annotation.QuadPoints, height));
            if (annotation.InkStrokes.Count > 0)
                engine.SetNumberLists("/InkList", annotation.InkStrokes.Select(stroke => Flip(stroke, height)));
            if (annotation.Vertices.Count > 0)
                engine.SetNumbers("/Vertices", Flip(annotation.Vertices, height));
            if (annotation.LineEnds.Count == 2)
                engine.SetNumbers("/L", Flip(annotation.LineEnds, height));

            var flags = annotation.NotPrinted ? (PdfAnnotationFlags)0 : PdfAnnotationFlags.Print;
            if (annotation.ReadOnly) flags |= PdfAnnotationFlags.ReadOnly;
            if (annotation.Locked) flags |= PdfAnnotationFlags.Locked;
            if (annotation.NoZoom) flags |= PdfAnnotationFlags.NoZoom;
            if (annotation.NoRotate) flags |= PdfAnnotationFlags.NoRotate;
            engine.Flags = flags;

            if (annotation.PaintAppearance is { } paint)
            {
                var form = PdfFormBuilder.CreateAppearanceForm(_document, _renderContext, annotation.PixelsPerPoint, annotation.ContentSize, paint);
                Matrix3x2? matrix = annotation.AppearanceTransform is { } m ? new Matrix3x2(m.M11, -m.M12, -m.M21, m.M22, 0, 0) : null;
                PdfFormBuilder.ApplyMatrix(form, matrix);
                var ap = new PdfDictionary(_document);
                ap.Elements.SetReference("/N", form);
                engine.Elements.SetObject(PdfAnnotation.Keys.AP, ap);
            }

            page.Page.Annotations.Add(engine);
        }

        private static IEnumerable<double> Components(PaintColor color)
        {
            var (r, g, b) = color.ToRgb();
            return [r / 255.0, g / 255.0, b / 255.0];
        }

        private static IEnumerable<double> Flip(IEnumerable<PaintPoint> points, double pageHeight)
        {
            foreach (var point in points)
            {
                yield return point.X;
                yield return pageHeight - point.Y;
            }
        }
    }
}
