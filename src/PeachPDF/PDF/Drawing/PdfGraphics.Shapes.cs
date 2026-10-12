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
    // ----- DrawLine -----------------------------------------------------------------------------

    /// <summary>
    /// Draws a line connecting two PaintPoint structures.
    /// </summary>
    public void DrawLine(Pen pen, PaintPoint pt1, PaintPoint pt2)
    {
        DrawLine(pen, pt1.X, pt1.Y, pt2.X, pt2.Y);
    }

    /// <summary>
    /// Draws a line connecting the two points specified by coordinate pairs.
    /// </summary>
    public void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(pen);

        if (_renderer is not null)
            _renderer.DrawLines(pen, new PaintPoint[] { new PaintPoint(x1, y1), new PaintPoint(x2, y2) });
    }

    // ----- DrawLines ----------------------------------------------------------------------------
    /// <summary>
    /// Draws a series of line segments that connect an array of points.
    /// </summary>
    public void DrawLines(Pen pen, PaintPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
            throw new ArgumentException("The point array must contain 2 or more points.", "points");

        if (_renderer is not null)
            _renderer.DrawLines(pen, points);
    }

    // ----- DrawBezier ---------------------------------------------------------------------------
    // ----- DrawBeziers --------------------------------------------------------------------------

    // ----- DrawCurve ----------------------------------------------------------------------------

    // ----- DrawArc ------------------------------------------------------------------------------

    // ----- DrawRectangle ------------------------------------------------------------------------

    // ----- stroke -----

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Pen pen, Rect rect)
    {
        DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Pen pen, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(pen);

        if (_drawGraphics)
        {

        }

        if (_renderer is not null)
            _renderer.DrawRectangle(pen, null, x, y, width, height);
    }

    // ----- fill -----

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Brush brush, Rect rect)
    {
        DrawRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Brush brush, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(brush);

        if (_renderer is not null)
            _renderer.DrawRectangle(null, brush, x, y, width, height);
    }

    // ----- stroke and fill -----

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Pen pen, Brush brush, Rect rect)
    {
        DrawRectangle(pen, brush, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    public void DrawRectangle(Pen pen, Brush brush, double x, double y, double width, double height)
    {
        if (pen is null && brush is null)
            throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");

        if (_renderer is not null)
            _renderer.DrawRectangle(pen, brush, x, y, width, height);
    }

    // ----- DrawRectangles -----------------------------------------------------------------------

    // ----- stroke -----

    // ----- fill -----

    // ----- stroke and fill -----

    // ----- DrawRoundedRectangle -----------------------------------------------------------------

    // ----- stroke -----

    // ----- fill -----

    // ----- stroke and fill -----

    // ----- DrawEllipse --------------------------------------------------------------------------

    // ----- stroke -----

    // ----- fill -----

    // ----- stroke and fill -----

    /// <summary>
    /// Draws a polygon defined by an array of points.
    /// </summary>
    public void DrawPolygon(Pen pen, PaintPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
            throw new ArgumentException("The point array must contain 2 or more points.", "points");

        if (_renderer is not null)
            _renderer.DrawPolygon(pen, null, points, FillMode.EvenOdd);  // FillMode is ignored
    }

    // ----- fill -----

    /// <summary>
    /// Draws a polygon defined by an array of points.
    /// </summary>
    public void DrawPolygon(Brush brush, PaintPoint[] points, FillMode fillmode)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
            throw new ArgumentException("The point array must contain 2 or more points.", "points");

        if (_renderer is not null)
            _renderer.DrawPolygon(null, brush, points, fillmode);
    }

    // ----- stroke and fill -----

    /// <summary>
    /// Draws a polygon defined by an array of points.
    /// </summary>
    public void DrawPolygon(Pen pen, Brush brush, PaintPoint[] points, FillMode fillmode)
    {
        if (pen is null && brush is null)
            throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
            throw new ArgumentException("The point array must contain 2 or more points.", "points");

        if (_renderer is not null)
            _renderer.DrawPolygon(pen, brush, points, fillmode);
    }

    // ----- DrawPie ------------------------------------------------------------------------------

    // ----- stroke -----

    // ----- fill -----

    // ----- stroke and fill -----

    // ----- DrawClosedCurve ----------------------------------------------------------------------

    // ----- stroke -----

    // ----- fill -----

    // ----- stroke and fill -----

    // ----- DrawPath -----------------------------------------------------------------------------

    // ----- stroke -----

    /// <summary>
    /// Draws a graphical path.
    /// </summary>
    public void DrawPath(Pen pen, GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(path);

        if (_renderer is not null)
            _renderer.DrawPath(pen, null, path);
    }

    // ----- fill -----

    /// <summary>
    /// Draws a graphical path.
    /// </summary>
    public void DrawPath(Brush brush, GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(path);

        if (_renderer is not null)
            _renderer.DrawPath(null, brush, path);
    }

    // ----- stroke and fill -----

    /// <summary>
    /// Draws a graphical path.
    /// </summary>
    public void DrawPath(Pen pen, Brush brush, GraphicsPath path)
    {
        if (pen is null && brush is null)
        {
            // ReSharper disable once NotResolvedInText
            throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");
        }
        ArgumentNullException.ThrowIfNull(path);

        if (_renderer is not null)
            _renderer.DrawPath(pen, brush, path);
    }
}
