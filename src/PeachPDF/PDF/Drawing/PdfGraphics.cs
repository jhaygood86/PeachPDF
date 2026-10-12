#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

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

namespace PeachPDF.PDF.Drawing
{
    /// <summary>
    /// Represents a drawing surface for a fixed size page.
    /// </summary>
    internal sealed class PdfGraphics : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the PdfGraphics class for drawing on a PDF page.
        /// </summary>
        PdfGraphics(PdfPage page, PdfPageAppendMode options)
        {
            if (page == null)
                throw new ArgumentNullException("page");

            if (page.Owner == null)
                throw new ArgumentException("You cannot draw on a page that is not owned by a PdfDocument object.", "page");

            if (page.RenderContent != null)
                throw new InvalidOperationException("An PdfGraphics object already exists for this page and must be disposed before a new one can be created.");

            if (page.Owner.IsReadOnly)
                throw new InvalidOperationException("Cannot create PdfGraphics for a page of a document that cannot be modified. Use PdfDocumentOpenMode.Modify.");

            _gsStack = new GraphicsStateStack(this);
            PdfContent content = null;
            switch (options)
            {
                case PdfPageAppendMode.Replace:
                    page.Contents.Elements.Clear();
                    goto case PdfPageAppendMode.Append;

                case PdfPageAppendMode.Prepend:
                    content = page.Contents.PrependContent();
                    break;

                case PdfPageAppendMode.Append:
                    content = page.Contents.AppendContent();
                    break;
            }
            page.RenderContent = content;
            _renderer = new PdfGraphicsRenderer(page, this, options);
            _pageSizePoints = new Size(page.Width, page.Height);
            _pageSize = new Size(page.Width, page.Height);

            Initialize();
        }

        PdfGraphics(Size size)
        {
            _gsStack = new GraphicsStateStack(this);
            _pageSizePoints = new Size(size.Width, size.Height);
            _pageSize = new Size(size.Width, size.Height);

            Initialize();
        }

        /// <summary>
        /// Initializes a new instance of the PdfGraphics class used for drawing on a form.
        /// </summary>
        PdfGraphics(PdfForm form)
        {
            if (form == null)
                throw new ArgumentNullException("form");

            _form = form;
            form.AssociateGraphics(this);

            _gsStack = new GraphicsStateStack(this);
            _drawGraphics = false;
            if (form.Owner != null)
                _renderer = new PdfGraphicsRenderer(form, this);
            _pageSize = form.Size;
            Initialize();
        }

        /// <summary>
        /// Creates the measure context. This is a graphics context created only for querying measures of text.
        /// Drawing on a measure context has no effect.
        /// </summary>
        public static PdfGraphics CreateMeasureContext(Size size)
        {
            return new PdfGraphics(size);
        }

        /// <summary>
        /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.PdfPage object.
        /// </summary>
        public static PdfGraphics FromPdfPage(PdfPage page)
        {
            return new PdfGraphics(page, PdfPageAppendMode.Append);
        }

        /// <summary>
        /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.PdfPage object.
        /// </summary>
        public static PdfGraphics FromPdfPage(PdfPage page, PdfPageAppendMode options)
        {
            return new PdfGraphics(page, options);
        }

        /// <summary>
        /// Creates a new instance of the PdfGraphics class from a PeachPDF.PDF.Drawing.PdfForm object.
        /// </summary>
        public static PdfGraphics FromForm(PdfForm form)
        {
            if (form.Gfx != null)
                return form.Gfx;

            return new PdfGraphics(form);
        }

        /// <summary>
        /// Internal setup.
        /// </summary>
        void Initialize()
        {
            _pageOrigin = new PaintPoint();

            double pageHeight = _pageSize.Height;
            PdfPage targetPage = PdfPage;
            PaintPoint trimOffset = new PaintPoint();
            if (targetPage != null && targetPage.TrimMargins.AreSet)
            {
                pageHeight += targetPage.TrimMargins.Top + targetPage.TrimMargins.Bottom;
                trimOffset = new PaintPoint(targetPage.TrimMargins.Left, targetPage.TrimMargins.Top);
            }

            Matrix3x2 matrix = Matrix3x2.Identity;
            if (trimOffset != new PaintPoint())
                matrix = PdfMatrix.Translation(trimOffset.X, -trimOffset.Y) * matrix;

            DefaultViewMatrix = matrix;
            _transform = Matrix3x2.Identity;
        }

        /// <summary>
        /// Releases all resources used by this object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_form != null)
                    _form.Finish();

                _drawGraphics = false;

                if (_renderer != null)
                {
                    _renderer.Close();
                    _renderer = null;
                }
            }
        }
        bool _disposed;

        /// <summary>
        /// Gets the current page origin. Setting the origin is not yet implemented.
        /// </summary>
        public PaintPoint PageOrigin
        {
            get { return _pageOrigin; }
            set
            {
                // Is there really anybody who needes to set the page origin?
                if (value != new PaintPoint())
                    throw new NotImplementedException("PageOrigin cannot be modified in current implementation.");
            }
        }
        PaintPoint _pageOrigin;

        /// <summary>
        /// Gets the current size of the page.
        /// </summary>
        public Size PageSize
        {
            get { return _pageSize; }
            //set
            //{
            //  //TODO
            //  throw new NotImplementedException("PageSize cannot be modified in current implementation.");
            //}
        }
        Size _pageSize;
        Size _pageSizePoints;

        #region Drawing

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
            if (pen == null)
                throw new ArgumentNullException("pen");

            if (_renderer != null)
                _renderer.DrawLines(pen, new PaintPoint[] { new PaintPoint(x1, y1), new PaintPoint(x2, y2) });
        }

        // ----- DrawLines ----------------------------------------------------------------------------
        /// <summary>
        /// Draws a series of line segments that connect an array of points.
        /// </summary>
        public void DrawLines(Pen pen, PaintPoint[] points)
        {
            if (pen == null)
                throw new ArgumentNullException("pen");
            if (points == null)
                throw new ArgumentNullException("points");
            if (points.Length < 2)
                throw new ArgumentException("The point array must contain 2 or more points.", "points");

            if (_renderer != null)
                _renderer.DrawLines(pen, points);
        }

        /// <summary>
        /// Draws a series of line segments that connect an array of x and y pairs.
        /// </summary>
        public void DrawLines(Pen pen, double x, double y, params double[] value)
        {
            if (pen == null)
                throw new ArgumentNullException("pen");
            if (value == null)
                throw new ArgumentNullException("value");

            int length = value.Length;
            PaintPoint[] points = new PaintPoint[length / 2 + 1];
            points[0].X = x;
            points[0].Y = y;
            for (int idx = 0; idx < length / 2; idx++)
            {
                points[idx + 1].X = value[2 * idx];
                points[idx + 1].Y = value[2 * idx + 1];
            }
            DrawLines(pen, points);
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
            if (pen == null)
                throw new ArgumentNullException("pen");

            if (_drawGraphics)
            {

            }

            if (_renderer != null)
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
            if (brush == null)
                throw new ArgumentNullException("brush");

            if (_renderer != null)
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
            if (pen == null && brush == null)
                throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");

            if (_renderer != null)
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
            if (pen == null)
                throw new ArgumentNullException("pen");
            if (points == null)
                throw new ArgumentNullException("points");
            if (points.Length < 2)
                throw new ArgumentException("The point array must contain 2 or more points.", "points");

            if (_renderer != null)
                _renderer.DrawPolygon(pen, null, points, FillMode.EvenOdd);  // FillMode is ignored
        }

        // ----- fill -----

        /// <summary>
        /// Draws a polygon defined by an array of points.
        /// </summary>
        public void DrawPolygon(Brush brush, PaintPoint[] points, FillMode fillmode)
        {
            if (brush == null)
                throw new ArgumentNullException("brush");
            if (points == null)
                throw new ArgumentNullException("points");
            if (points.Length < 2)
                throw new ArgumentException("The point array must contain 2 or more points.", "points");

            if (_renderer != null)
                _renderer.DrawPolygon(null, brush, points, fillmode);
        }

        // ----- stroke and fill -----

        /// <summary>
        /// Draws a polygon defined by an array of points.
        /// </summary>
        public void DrawPolygon(Pen pen, Brush brush, PaintPoint[] points, FillMode fillmode)
        {
            if (pen == null && brush == null)
                throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");
            if (points == null)
                throw new ArgumentNullException("points");
            if (points.Length < 2)
                throw new ArgumentException("The point array must contain 2 or more points.", "points");

            if (_renderer != null)
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
            if (pen == null)
                throw new ArgumentNullException("pen");
            if (path == null)
                throw new ArgumentNullException("path");

            if (_renderer != null)
                _renderer.DrawPath(pen, null, path);
        }

        // ----- fill -----

        /// <summary>
        /// Draws a graphical path.
        /// </summary>
        public void DrawPath(Brush brush, GraphicsPath path)
        {
            if (brush == null)
                throw new ArgumentNullException("brush");
            if (path == null)
                throw new ArgumentNullException("path");

            if (_renderer != null)
                _renderer.DrawPath(null, brush, path);
        }

        // ----- stroke and fill -----

        /// <summary>
        /// Draws a graphical path.
        /// </summary>
        public void DrawPath(Pen pen, Brush brush, GraphicsPath path)
        {
            if (pen == null && brush == null)
            {
                // ReSharper disable once NotResolvedInText
                throw new ArgumentNullException("pen and brush", "Pen or Brush or both must not be null.");
            }
            if (path == null)
                throw new ArgumentNullException("path");

            if (_renderer != null)
                _renderer.DrawPath(pen, brush, path);
        }

        // ----- DrawString ---------------------------------------------------------------------------

        /// <summary>
        /// Draws the specified text string.
        /// </summary>
        /// <summary>
        /// When true, <see cref="DrawString(string, TypefaceFont, Brush, Rect, PdfStringFormat, double, FontPalette?, ShapeSettings?, string?)"/>
        /// shows text in PDF text rendering mode 3 (neither filled nor stroked): the glyphs are laid out, embedded and
        /// extractable exactly as visible text is, but paint nothing. Used to keep text selectable and searchable over content
        /// that was drawn as a bitmap. Text decorations and colour-glyph artwork are not painted either.
        /// </summary>
        public bool InvisibleText { get; set; }

        /// <summary>What draws the SVG documents of a font's glyphs (OpenType SVG) on this graphics, or <see langword="null"/> when nothing does.</summary>
        internal PeachDrawing.Core.ISvgGlyphPainter? SvgGlyphPainter { get; set; }

        /// <summary>The render context pixel canvases are built from when a glyph is drawn through the raster backend, or <see langword="null"/> when there is none.</summary>
        internal PeachDrawing.Core.RenderContext? RasterContext { get; set; }

        public void DrawString(string s, TypefaceFont font, Brush brush, PaintPoint point)
        {
            DrawString(s, font, brush, new Rect(point.X, point.Y, 0, 0), PdfStringFormats.Default);
        }


        /// <summary>
        /// Draws the specified text string.
        /// </summary>
        public void DrawString(string s, TypefaceFont font, Brush brush, PaintPoint point, PdfStringFormat format)
        {
            DrawString(s, font, brush, new Rect(point.X, point.Y, 0, 0), format);
        }

        /// <summary>
        /// Draws the specified text string.
        /// </summary>
        public void DrawString(string s, TypefaceFont font, Brush brush, double x, double y)
        {
            DrawString(s, font, brush, new Rect(x, y, 0, 0), PdfStringFormats.Default);
        }

        /// <summary>
        /// Draws the specified text string.
        /// </summary>
        public void DrawString(string s, TypefaceFont font, Brush brush, double x, double y, PdfStringFormat format, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null, string? logicalText = null)
        {
            DrawString(s, font, brush, new Rect(x, y, 0, 0), format, letterSpacing, fontPalette, features, logicalText);
        }


        /// <summary>
        /// Draws the specified text string.
        /// </summary>
        public void DrawString(string s, TypefaceFont font, Brush brush, Rect layoutRectangle)
        {
            DrawString(s, font, brush, layoutRectangle, PdfStringFormats.Default);
        }

        /// <summary>
        /// Draws the specified text string. <paramref name="logicalText"/> is the true logical-order
        /// (pre-bidi-mirroring) source <paramref name="text"/> was derived from, when the two differ -
        /// see <see cref="PeachDrawing.Core.Canvas"/>'s own equivalent overload for why this
        /// exists (ToUnicode CMap fidelity for a reversed/mirrored RTL word).
        /// </summary>
        public void DrawString(string text, TypefaceFont font, Brush brush, Rect layoutRectangle, PdfStringFormat format, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null, string? logicalText = null)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            if (font == null)
                throw new ArgumentNullException("font");
            if (brush == null)
                throw new ArgumentNullException("brush");

            if (format != null && format.LineAlignment == PdfLineAlignment.BaseLine && layoutRectangle.Height != 0)
                throw new InvalidOperationException("DrawString: With PdfLineAlignment.BaseLine the height of the layout rectangle must be 0.");

            if (text.Length == 0)
                return;

            if (format == null)
                format = PdfStringFormats.Default;

            if (_renderer != null)
                _renderer.DrawString(text, font, brush, layoutRectangle, format, letterSpacing, fontPalette, features ?? ShapeSettings.Default, logicalText);
        }

        /// <summary>
        /// Draws each glyph in <paramref name="glyphs"/> at its own explicit position, addressed
        /// directly by font glyph index - see <see cref="PeachDrawing.Core.Canvas.DrawGlyphs"/>'s
        /// own remarks for why this exists alongside the ordinary character-based <c>DrawString</c>.
        /// </summary>
        public void DrawGlyphsAtPositions(System.Collections.Generic.IReadOnlyList<(int GlyphIndex, double X, double Y)> glyphs, TypefaceFont font, Brush brush)
        {
            if (font == null)
                throw new ArgumentNullException("font");
            if (brush == null)
                throw new ArgumentNullException("brush");

            if (_renderer != null)
                _renderer.DrawGlyphsAtPositions(glyphs, font, brush);
        }

        // ----- MeasureString ------------------------------------------------------------------------

        /// <summary>
        /// Measures the specified string when drawn with the specified font.
        /// </summary>
        public Size MeasureString(string text, TypefaceFont font, PdfStringFormat stringFormat, ShapeSettings? features = null)
        {
            if (text == null)
                throw new ArgumentNullException("text");
            if (font == null)
                throw new ArgumentNullException("font");
            if (stringFormat == null)
                throw new ArgumentNullException("stringFormat");

            Size size = FontHelper.MeasureString(text, font, PdfStringFormats.Default, features ?? ShapeSettings.Default);
            return size;
        }

        /// <summary>
        /// Measures the specified string when drawn with the specified font.
        /// </summary>
        public Size MeasureString(string text, TypefaceFont font, ShapeSettings? features = null)
        {
            return MeasureString(text, font, PdfStringFormats.Default, features);
        }

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
            if (image == null)
                throw new ArgumentNullException("image");

            CheckFormConsistency(image);

            double width = image.PointWidth;
            double height = image.PointHeight;

            if (_renderer != null)
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
            if (image == null)
                throw new ArgumentNullException("image");

            CheckFormConsistency(image);

            if (_renderer != null)
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
            if (image == null)
                throw new ArgumentNullException("image");

            CheckFormConsistency(image);

            if (_renderer != null)
                _renderer.DrawImage(image, destRect, srcRect);
        }

        //TODO?
        //public void DrawImage(PdfRasterImage image, Rectangle destRect, double srcX, double srcY, double srcWidth, double srcHeight, GraphicsUnit srcUnit);
        //public void DrawImage(PdfRasterImage image, Rectangle destRect, double srcX, double srcY, double srcWidth, double srcHeight, GraphicsUnit srcUnit);

        void DrawMissingImageRect(Rect rect)
        {
        }

        /// <summary>
        /// Checks whether drawing is allowed and disposes the PdfGraphics object, if necessary.
        /// </summary>
        void CheckFormConsistency(PdfRasterImage image)
        {
            PdfForm xForm = image as PdfForm;
            if (xForm != null)
            {
                // Force disposing of PdfGraphics that draws the content
                xForm.Finish();

                // ReSharper disable once MergeSequentialChecks
                if (_renderer != null && (_renderer as PdfGraphicsRenderer) != null)
                {
                    if (xForm.Owner != null && xForm.Owner != ((PdfGraphicsRenderer)_renderer).Owner)
                        throw new InvalidOperationException(
                            "A PdfForm object is always bound to the document it was created for and cannot be drawn in the context of another document.");

                    if (xForm == ((PdfGraphicsRenderer)_renderer)._form)
                        throw new InvalidOperationException(
                            "A PdfForm cannot be drawn on itself.");
                }
            }
        }

        #endregion

        // --------------------------------------------------------------------------------------------

        #region Save and Restore

        /// <summary>
        /// Saves the current state of this PdfGraphics object and identifies the saved state with the
        /// returned PdfGraphicsStateToken object.
        /// </summary>
        public PdfGraphicsStateToken Save()
        {
            PdfGraphicsStateToken xState = null;
            xState = new PdfGraphicsStateToken();
            InternalGraphicsState iState = new InternalGraphicsState(this, xState);
            iState.Transform = _transform;
            _gsStack.Push(iState);

            if (_renderer != null)
                _renderer.Save(xState);

            return xState;
        }

        /// <summary>
        /// Restores the state of this PdfGraphics object to the state represented by the specified 
        /// PdfGraphicsStateToken object.
        /// </summary>
        public void Restore(PdfGraphicsStateToken state)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            _gsStack.Restore(state.InternalState);
            _transform = state.InternalState.Transform;

            if (_renderer != null)
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

        /// <summary>
        /// Gets the current graphics state level. The default value is 0. Each call of Save or BeginContainer
        /// increased and each call of Restore or EndContainer decreased the value by 1.
        /// </summary>
        public int GraphicsStateLevel
        {
            get { return _gsStack.Count; }
        }

        #endregion

        // --------------------------------------------------------------------------------------------

        #region Transformation

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
        /// Gets the current transformation matrix.
        /// The transformation matrix canot be set. Insted use Save/Restore or BeginContainer/EndContainer to
        /// save the state before Transform is called and later restore to the previous transform.
        /// </summary>
        public Matrix3x2 Transform
        {
            get { return _transform; }
        }

        /// <summary>
        /// Applies a new transformation to the current transformation matrix.
        /// </summary>
        void AddTransform(Matrix3x2 transform)
        {
            _transform = transform * _transform;
            if (_renderer != null)
                _renderer.AddTransform(transform);
        }

        #endregion

        // --------------------------------------------------------------------------------------------

        #region Clipping

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
            if (path == null)
                throw new ArgumentNullException("path");

            if (_renderer != null)
                _renderer.IntersectClip(path);
        }

        //public void SetClip(Graphics g);
        //public void SetClip(Graphics g, CombineMode combineMode);
        //public void SetClip(GraphicsPath path, CombineMode combineMode);
        //public void SetClip(Rectangle rect, CombineMode combineMode);
        //public void SetClip(GdiRectF rect, CombineMode combineMode);
        //public void SetClip(Region region, CombineMode combineMode);
        //public void IntersectClip(Region region);
        //public void ExcludeClip(Region region);

        #endregion

        // --------------------------------------------------------------------------------------------

        #region Miscellaneous

        /// <summary>
        /// Writes a comment to the output stream. Comments have no effect on the rendering of the output.
        /// They may be useful to mark a position in a content stream of a PDF document.
        /// </summary>
        public void WriteComment(string comment)
        {
            if (comment == null)
                throw new ArgumentNullException("comment");

            if (_drawGraphics)
            {
                // TODO: Do something if metafile?
            }

            if (_renderer != null)
                _renderer.WriteComment(comment);
        }

        #endregion

        // --------------------------------------------------------------------------------------------

        /// <summary>
        /// The transformation matrix from the PdfGraphics page space to the Graphics world space.
        /// (The name 'default view matrix' comes from Microsoft OS/2 Presentation Manager. I choose
        /// this name because I have no better one.)
        /// </summary>
        internal Matrix3x2 DefaultViewMatrix = Matrix3x2.Identity;

        /// <summary>
        /// Indicates whether to send drawing operations to _gfx or _dc.
        /// </summary>
        bool _drawGraphics;

        readonly PdfForm _form = null!;

        /// <summary>
        /// Interface to an (optional) renderer. Currently it is the PdfGraphicsRenderer, if defined.
        /// </summary>
        PdfGraphicsRenderer _renderer = null!;

        /// <summary>
        /// The transformation matrix from PdfGraphics world space to page unit space.
        /// </summary>
        Matrix3x2 _transform = Matrix3x2.Identity;

        /// <summary>
        /// The graphics state stack.
        /// </summary>
        readonly GraphicsStateStack _gsStack;

        /// <summary>
        /// Gets the PDF page that serves as drawing surface if PDF is rendered,
        /// or null, if no such object exists.
        /// </summary>
        public PdfPage PdfPage
        {
            get
            {
                PdfGraphicsRenderer renderer = _renderer as PdfGraphicsRenderer;
                return renderer != null ? renderer._page : null;
            }
        }

        /// <summary>
        /// The owning <see cref="PdfDocument"/>, whether this <see cref="PdfGraphics"/> is bound to a real
        /// page (<see cref="PdfPage"/>) or to a Form XObject (<c>FromForm</c> - as a <c>CreateTile</c>
        /// tile is). Falling back to the form's own owner is what lets a tile be created recursively from
        /// inside another tile (e.g. nested CSS/SVG group <c>opacity</c>, or opacity inside a mask/pattern
        /// tile) - <see cref="PdfPage"/> alone is null for a form-bound instance.
        /// </summary>
        internal PdfDocument Owner => PdfPage?.Owner ?? _form?.Owner;

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
}