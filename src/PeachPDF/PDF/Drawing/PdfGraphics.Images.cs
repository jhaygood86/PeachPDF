// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Core;
using PeachPDF.PDF.Rendering;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using System;

#pragma warning disable 1587
// ReSharper disable UseNullPropagation
// ReSharper disable RedundantNameQualifier
// ReSharper disable UseNameofExpression

namespace PeachPDF.PDF.Drawing;

internal sealed partial class PdfGraphics : IDisposable
{
    // ----- DrawImage ----------------------------------------------------------------------------

    /// <summary>
    /// Draws the specified image.
    /// </summary>
    public void DrawImage(PdfRasterImage image, PaintPoint point)
    {
        DrawImage(image, point.X, point.Y);
    }

    /// <summary>
    /// Draws the specified image.
    /// </summary>
    public void DrawImage(PdfRasterImage image, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(image);

        CheckFormConsistency(image);

        double width = image.PointWidth;
        double height = image.PointHeight;

        if (_renderer is not null)
            _renderer.DrawImage(image, x, y, image.PointWidth, image.PointHeight);
        //image.Width * 72 / image.HorizontalResolution,
        //image.Height * 72 / image.HorizontalResolution);
    }

    /// <summary>
    /// Draws the specified image.
    /// </summary>
    public void DrawImage(PdfRasterImage image, Rect rect)
    {
        DrawImage(image, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Draws the specified image.
    /// </summary>
    public void DrawImage(PdfRasterImage image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);

        CheckFormConsistency(image);

        if (_renderer is not null)
            _renderer.DrawImage(image, x, y, width, height);
    }

    // TODO: calculate destination size
    //public void DrawImage(PdfRasterImage image, double x, double y, GdiRectF srcRect)
    //public void DrawImage(PdfRasterImage image, double x, double y, Rect srcRect)

    /// <summary>
    /// Draws the specified image.
    /// </summary>
    public void DrawImage(PdfRasterImage image, Rect destRect, Rect srcRect)
    {
        ArgumentNullException.ThrowIfNull(image);

        CheckFormConsistency(image);

        if (_renderer is not null)
            _renderer.DrawImage(image, destRect, srcRect);
    }

    //TODO?
    //public void DrawImage(PdfRasterImage image, Rectangle destRect, double srcX, double srcY, double srcWidth, double srcHeight, GraphicsUnit srcUnit);
    //public void DrawImage(PdfRasterImage image, Rectangle destRect, double srcX, double srcY, double srcWidth, double srcHeight, GraphicsUnit srcUnit);

    /// <summary>
    /// Checks whether drawing is allowed and disposes the PdfGraphics object, if necessary.
    /// </summary>
    void CheckFormConsistency(PdfRasterImage image)
    {
        PdfForm? xForm = image as PdfForm;
        if (xForm is not null)
        {
            // Force disposing of PdfGraphics that draws the content
            xForm.Finish();

            // ReSharper disable once MergeSequentialChecks
            if (_renderer is not null && (_renderer as PdfGraphicsRenderer) is not null)
            {
                if (xForm.Owner is not null && xForm.Owner != ((PdfGraphicsRenderer)_renderer).Owner)
                    throw new InvalidOperationException(
                        "A PdfForm object is always bound to the document it was created for and cannot be drawn in the context of another document.");

                if (xForm == ((PdfGraphicsRenderer)_renderer)._form)
                    throw new InvalidOperationException(
                        "A PdfForm cannot be drawn on itself.");
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/> with <paramref name="maskImage"/>
    /// applied as a luminosity soft mask, scoped to just this one placement - see
    /// <see cref="PdfGraphicsRenderer.DrawImageMasked"/> for why the mask must be applied this
    /// way (atomically, sharing the placement's own "cm") rather than as a separate push/pop pair.
    /// </summary>
    internal void DrawImageMasked(PdfForm image, PdfForm maskImage, Rect destRect)
    {
        (_renderer as PdfGraphicsRenderer)?.DrawImageMasked(image, maskImage, destRect);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/>, composited as a single
    /// flattened result at constant <paramref name="opacity"/> and (if not <c>"Normal"</c>)
    /// <paramref name="pdfBlendModeName"/> - see
    /// <see cref="PdfGraphicsRenderer.DrawImageWithOpacity"/> for why this differs from simply
    /// reducing the alpha of each shape painted into the tile.
    /// </summary>
    internal void DrawImageWithOpacity(PdfForm image, Rect destRect, double opacity, string pdfBlendModeName = "Normal")
    {
        (_renderer as PdfGraphicsRenderer)?.DrawImageWithOpacity(image, destRect, opacity, pdfBlendModeName);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/>, composited through a
    /// channel-independent <see cref="ColorMatrix"/> - see
    /// <see cref="PdfGraphicsRenderer.DrawImageWithColorMatrix"/>.
    /// </summary>
    internal void DrawImageWithColorMatrix(PdfForm image, Rect destRect, ColorMatrix matrix)
    {
        (_renderer as PdfGraphicsRenderer)?.DrawImageWithColorMatrix(image, destRect, matrix);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/> with <paramref name="maskImage"/>
    /// applied as an <c>/Alpha</c>-subtype soft mask - see
    /// <see cref="PdfGraphicsRenderer.DrawImageAlphaMasked"/>.
    /// </summary>
    internal void DrawImageAlphaMasked(PdfForm image, PdfForm maskImage, Rect destRect, bool invert = false)
    {
        (_renderer as PdfGraphicsRenderer)?.DrawImageAlphaMasked(image, maskImage, destRect, invert);
    }

    /// <summary>
    /// Paints <paramref name="bottom"/> then <paramref name="top"/> blended over it at
    /// <paramref name="destRect"/> - see <see cref="PdfGraphicsRenderer.DrawImageBlendedOver"/>.
    /// </summary>
    internal void DrawImageBlendedOver(PdfForm top, PdfForm bottom, Rect destRect, string pdfBlendModeName)
    {
        (_renderer as PdfGraphicsRenderer)?.DrawImageBlendedOver(top, bottom, destRect, pdfBlendModeName);
    }
}
