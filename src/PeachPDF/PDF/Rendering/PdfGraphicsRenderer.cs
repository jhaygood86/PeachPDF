// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Linq;
using System.Numerics;
using PeachPDF.PDF.Drawing;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Core;
using PeachPDF.PDF.Internal;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

// ReSharper disable RedundantNameQualifier
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace PeachPDF.PDF.Rendering;

/// <summary>
/// Represents a drawing surface for PdfPages.
/// </summary>
internal sealed partial class PdfGraphicsRenderer
{
    public PdfGraphicsRenderer(PdfPage page, PdfGraphics gfx, PdfPageAppendMode options)
    {
        _page = page;
        _colorMode = page._document.Options.ColorMode;
        _options = options;
        _gfx = gfx;
        _content = new PdfContentWriter();
        page.RenderContent._pdfRenderer = this;
        _gfxState = new PdfGraphicsState(this);
    }

    public PdfGraphicsRenderer(PdfForm form, PdfGraphics gfx)
    {
        _form = form;
        _colorMode = form.Owner.Options.ColorMode;
        _gfx = gfx;
        _content = new PdfContentWriter();
        form.PdfRenderer = this;
        _gfxState = new PdfGraphicsState(this);
    }

    /// <summary>
    /// Gets the content created by this renderer.
    /// </summary>
    byte[] GetContent()
    {
        EndPage();
        return _content.ToArray();
    }

    public PdfPageAppendMode PageOptions => _options;

    public void Close()
    {
        if (_page is not null)
        {
            PdfContent content2 = _page.RenderContent;
            content2.CreateStream(GetContent());
            _content.Clear();

            _gfx = null!;
            _page.RenderContent._pdfRenderer = null!;
            _page.RenderContent = null!;
            _page = null!;
        }
        else if (_form is not null)
        {
            _form._pdfForm.CreateStream(GetContent());
            _content.Clear();
            _gfx = null!;
            _form.PdfRenderer = null!;
            _form = null!;
        }
    }

    // --------------------------------------------------------------------------------------------

    //void SetPageLayout(down, point(0, 0), unit

    // ----- DrawLine -----------------------------------------------------------------------------

    /// <summary>
    /// Strokes a single connection of two points.
    /// </summary>
    public void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
    {
        DrawLines(pen, new PaintPoint[] { new PaintPoint(x1, y1), new PaintPoint(x2, y2) });
    }

    // ----- DrawLines ----------------------------------------------------------------------------

    /// <summary>
    /// Strokes a series of connected points.
    /// </summary>
    public void DrawLines(Pen pen, PaintPoint[] points)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(points);

        int count = points.Length;
        if (count == 0)
            return;

        Realize(pen);

        AppendPathPoint(points[0].X, points[0].Y, 'm');
        for (int idx = 1; idx < count; idx++)
            AppendPathPoint(points[idx].X, points[idx].Y, 'l');
        _content.Append("S\n");
    }

    // ----- DrawBezier ---------------------------------------------------------------------------

    // ----- DrawBeziers --------------------------------------------------------------------------

    // ----- DrawCurve ----------------------------------------------------------------------------

    // ----- DrawArc ------------------------------------------------------------------------------

    // ----- DrawRectangle ------------------------------------------------------------------------

    public void DrawRectangle(Pen? pen, Brush? brush, double x, double y, double width, double height)
    {
        if (pen is null && brush is null)
            throw new ArgumentNullException("pen and brush");

        const string format = PdfNumberFormats.ThreeDecimals;

        Realize(pen, brush);
        //AppendFormat123("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} re\n", x, y, width, -height);
        AppendFormatRect("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} re\n", x, y + height, width, height);

        if (pen is not null && brush is not null)
            _content.Append("B\n");
        else if (pen is not null)
            _content.Append("S\n");
        else
            _content.Append("f\n");
    }

    // ----- DrawRectangles -----------------------------------------------------------------------

    // ----- DrawRoundedRectangle -----------------------------------------------------------------

    // ----- DrawEllipse --------------------------------------------------------------------------

    // ----- DrawPolygon --------------------------------------------------------------------------

    public void DrawPolygon(Pen? pen, Brush? brush, PaintPoint[] points, FillMode fillmode)
    {
        Realize(pen, brush);

        int count = points.Length;
        if (points.Length < 2)
            throw new ArgumentException("The point array must contain 2 or more points.", "points");

        AppendPathPoint(points[0].X, points[0].Y, 'm');
        for (int idx = 1; idx < count; idx++)
            AppendPathPoint(points[idx].X, points[idx].Y, 'l');

        AppendStrokeFill(pen, brush, fillmode, true);
    }

    // ----- DrawPie ------------------------------------------------------------------------------

    // ----- DrawClosedCurve ----------------------------------------------------------------------

    // ----- DrawPath -----------------------------------------------------------------------------

    public void DrawPath(Pen? pen, Brush? brush, GraphicsPath path)
    {
        if (pen is null && brush is null)
            throw new ArgumentNullException("pen");

        Realize(pen, brush);
        AppendPath(path);
        AppendStrokeFill(pen, brush, path.FillMode, false);
    }

    /// <summary>
    /// Activates a separable/HSL PDF blend mode (a <c>/BM</c> ExtGState) for subsequent painting.
    /// Scope it with a surrounding q/Q; the Q restores the blend mode to Normal. Used by the
    /// COLRv1 PaintComposite interpreter and by <c>outline-color: invert</c>
    /// (<see cref="Html.Core.Handlers.OutlineDrawHandler"/>, via <c>Canvas.PushBlendMode</c>).
    /// </summary>
    internal void SetBlendMode(string pdfBlendModeName)
    {
        // PDF has no additive blend (PaintBlendMode.Plus); such a canvas paints it as a plain overlay.
        if (pdfBlendModeName == "Plus")
            pdfBlendModeName = "Normal";

        // A non-Normal /BM (any value other than "Normal"/"Compatible") is itself a transparency
        // -group-requiring construct PDF/A-1 forbids, same as an alpha ExtGState - see
        // PdfATransparencyGuard's remarks.
        if (pdfBlendModeName is not ("Normal" or "Compatible"))
        {
            PdfATransparencyGuard.RequireAllowed(Owner, _page, $"A non-default blend mode ({pdfBlendModeName})");
        }

        var extGState = new PdfExtGState(Owner);
        extGState.Elements.SetName("/BM", pdfBlendModeName);
        string gs = Resources.AddExtGState(extGState);
        Append(gs + " gs\n");
    }

    // ----- DrawString ---------------------------------------------------------------------------

    /// <summary>
    /// Where the last plain text-showing operator left a reader's pen, and where layout put the end
    /// of the run it showed.
    /// </summary>
    /// <param name="ContentLength">the content length right after the operator</param>
    /// <param name="PenX">the world-space x of the pen after its last glyph and that glyph's character spacing</param>
    /// <param name="LaidOutEndX">the world-space x layout gave the run's end: its measured width plus one <c>letter-spacing</c> per glyph</param>
    /// <param name="BaselineY">the world-space baseline the pen is on</param>
    private readonly record struct TextRun(int ContentLength, double PenX, double LaidOutEndX, double BaselineY);

    /// <summary>See <see cref="TextRun"/>; null when the last text written was not a plain run.</summary>
    private TextRun? _textRun;

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Clones the current graphics state and push it on a stack.
    /// </summary>
    public void Save(PdfGraphicsStateToken state)
    {
        // Before saving, the current transformation matrix must be completely realized.
        BeginGraphicMode();
        RealizeTransform();
        // Associate the PdfGraphicsStateToken with the current PdgGraphicsState.
        _gfxState.InternalState = state.InternalState;
        SaveState();
    }

    public void Restore(PdfGraphicsStateToken state)
    {
        BeginGraphicMode();
        RestoreState(state.InternalState);
    }

    // --------------------------------------------------------------------------------------------

    //  throw new NotImplementedException("SetPageTransform");
    //}

    public Matrix3x2 Transform
    {
        get
        {
            if (_gfxState.UnrealizedCtm.IsIdentity)
                return _gfxState.EffectiveCtm;
            return _gfxState.UnrealizedCtm * _gfxState.RealizedCtm;
        }
    }

    public void AddTransform(Matrix3x2 value)
    {
        _gfxState.AddTransform(value);
    }

    // --------------------------------------------------------------------------------------------

    public void IntersectClip(GraphicsPath path)
    {
        if (path is null)
            throw new NotImplementedException("SetClip with no path.");

        // Ensure that the graphics state stack level is at least 2, because otherwise an error
        // occurs when someone set the clip region before something was drawn.
        if (_gfxState.Level < GraphicsStackLevelWorldSpace)
            RealizeTransform();  // TODO: refactor this function

        // Only intersecting is supported: the clip never grows, it is narrowed for the rest of the current graphics state.
        if (_clipLevel == 0)
            _clipLevel = _gfxState.Level;
        _gfxState.SetAndRealizeClipPath(path);
    }

    /// <summary>
    /// Sets the clip path empty. Only possible if graphic state level has the same value as it has when
    /// the first time SetClip was invoked.
    /// </summary>
    public void ResetClip()
    {
        // No clip level means no clipping occurs and nothing is to do.
        if (_clipLevel == 0)
            return;

        // Only at the clipLevel the clipping can be reset.
        if (_clipLevel != _gfxState.Level)
            throw new NotImplementedException("Cannot reset clip region in an inner graphic state level.");

        // Must be in graphical mode before popping the graphics state.
        BeginGraphicMode();

        // Save InternalGraphicsState and transformation of the current graphical state.
        InternalGraphicsState state = _gfxState.InternalState;
        Matrix3x2 ctm = _gfxState.EffectiveCtm;
        // Empty clip path by switching back to the previous state.
        RestoreState();
        SaveState();
        // Save internal state
        _gfxState.InternalState = state;
        // Restore CTM
        // TODO: check rest of clip
        //GfxState.Transform = ctm;
    }

    /// <summary>
    /// The nesting level of the PDF graphics state stack when the clip region was set to non empty.
    /// Because of the way PDF is made the clip region can only be reset at this level.
    /// </summary>
    int _clipLevel;

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a comment to the PDF content stream. May be useful for debugging purposes.
    /// </summary>
    public void WriteComment(string comment)
    {
        comment = comment.Replace("\n", "\n% ");
        // TODO: Some more checks necessary?
        Append("% " + comment + "\n");
    }

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Appends the outline of a path: each contour as a move, its lines and curves, and a close when it is closed.
    /// </summary>
    internal void AppendPath(GraphicsPath path)
    {
        foreach (var contour in path.GetCurveContours())
        {
            AppendPathPoint(contour.Start.X, contour.Start.Y, 'm');

            foreach (var command in contour.Commands)
            {
                if (command.Kind == PathCommandKind.Cubic)
                    AppendBezier(command.Control1.X, command.Control1.Y, command.Control2.X, command.Control2.Y, command.End.X, command.End.Y);
                else
                    AppendPathPoint(command.End.X, command.End.Y, 'l');
            }

            // A contour that is only a start point has no segment for the close to end, so it emits nothing.
            if (contour.Closed && contour.Commands.Count > 0)
                Append("h\n");
        }
    }

    internal void Append(string value)
    {
        _content.Append(value);
    }

    internal void AppendFormatArgs(string format, params object[] args)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, args);
    }

    internal void AppendFormatString(string format, string s)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, s);
    }

    internal void AppendFormatFont(string format, string s, double d)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, s, d);
    }

    internal void AppendFormatInt(string format, int n)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, n);
    }

    internal void AppendFormatDouble(string format, double d)
    {
        _content.AppendFormat(CultureInfo.InvariantCulture, format, d);
    }

    private void AppendPathPoint(double x, double y, char operation, string format = PdfNumberFormats.FourDecimals)
    {
        PaintPoint result = WorldToView(new PaintPoint(x, y));
        AppendPdfNumber(_content, result.X, format);
        _content.Append(' ');
        AppendPdfNumber(_content, result.Y, format);
        _content.Append(' ').Append(operation).Append('\n');
    }

    internal void AppendFormatRect(string format, double x, double y, double width, double height)
    {
        PaintPoint point1 = WorldToView(new PaintPoint(x, y));
        _content.AppendFormat(CultureInfo.InvariantCulture, format, point1.X, point1.Y, width, height);
    }

    private void AppendBezier(double x1, double y1, double x2, double y2, double x3, double y3,
        string format = PdfNumberFormats.FourDecimals)
    {
        PaintPoint point1 = WorldToView(new PaintPoint(x1, y1));
        PaintPoint point2 = WorldToView(new PaintPoint(x2, y2));
        PaintPoint point3 = WorldToView(new PaintPoint(x3, y3));
        AppendPdfNumber(_content, point1.X, format);
        _content.Append(' ');
        AppendPdfNumber(_content, point1.Y, format);
        _content.Append(' ');
        AppendPdfNumber(_content, point2.X, format);
        _content.Append(' ');
        AppendPdfNumber(_content, point2.Y, format);
        _content.Append(' ');
        AppendPdfNumber(_content, point3.X, format);
        _content.Append(' ');
        AppendPdfNumber(_content, point3.Y, format);
        _content.Append(" c\n");
    }

    internal static void AppendPdfNumber(PdfContentWriter content, double value, string format) => content.Append(value, format);

    internal void AppendFormat(string format, PaintPoint point)
    {
        PaintPoint result = WorldToView(point);
        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y);
    }

    internal void AppendFormat(string format, double x, double y, string s)
    {
        PaintPoint result = WorldToView(new PaintPoint(x, y));
        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, s);
    }

    void AppendStrokeFill(Pen? pen, Brush? brush, FillMode fillMode, bool closePath)
    {
        if (closePath)
            _content.Append("h ");

        if (fillMode == FillMode.Nonzero)
        {
            if (pen is not null && brush is not null)
                _content.Append("B\n");
            else if (pen is not null)
                _content.Append("S\n");
            else
                _content.Append("f\n");
        }
        else
        {
            if (pen is not null && brush is not null)
                _content.Append("B*\n");
            else if (pen is not null)
                _content.Append("S\n");
            else
                _content.Append("f*\n");
        }
    }

    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Initializes the default view transformation, i.e. the transformation from the user page
    /// space to the PDF page space.
    /// </summary>
    void BeginPage()
    {
        if (_gfxState.Level != GraphicsStackLevelInitial)
            return;

        // TODO: Is PageOriging and PageScale (== Viewport) useful? Or just public DefaultViewMatrix (like Presentation Manager has had)
        // May be a BeginContainer(windows, viewport) is useful for userer that are not familar with maxtrix transformations.

        // Flip page horizontally and mirror text.

        // PDF uses a standard right-handed Cartesian coordinate system with the y axis directed up
        // and the rotation counterclockwise. Windows uses the opposite convertion with y axis
        // directed down and rotation clockwise. When I started with PDFsharp I flipped pages horizontally
        // and then mirrored text to compensate the effect that the fipping turns text upside down.
        // I found this technique during analysis of PDF documents generated with PDFlib. Unfortunately
        // this technique leads to several problems with programms that compose or view PDF documents
        // generated with PeachPDF.PDF.
        // In PDFsharp 1.4 I implement a revised technique that does not need text mirroring any more.

        DefaultViewMatrix = Matrix3x2.Identity;
        BeginPageDownwards();
    }

    void BeginPageDownwards()
    {
        PageHeightPt = Size.Height;

        // Save initial graphic state.
        SaveState();

        // Set default page transformation, if any.
        if (!DefaultViewMatrix.IsIdentity)
        {
            Debug.Assert(_gfxState.RealizedCtm.IsIdentity);
            const string format = PdfNumberFormats.SevenDecimals;
            Matrix3x2 cm = DefaultViewMatrix;
            AppendFormatArgs("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm ",
                         cm.M11, cm.M12, cm.M21, cm.M22, cm.M31, cm.M32);
        }
    }

    /// <summary>
    /// Ends the content stream, i.e. ends the text mode and balances the graphic state stack.
    /// </summary>
    void EndPage()
    {
        if (_streamMode == StreamMode.Text)
        {
            _content.Append("ET\n");
            _streamMode = StreamMode.Graphic;
        }

        while (_gfxStateStack.Count != 0)
            RestoreState();
    }

    /// <summary>
    /// Begins the graphic mode (i.e. ends the text mode).
    /// </summary>
    internal void BeginGraphicMode()
    {
        if (_streamMode != StreamMode.Graphic)
        {
            if (_streamMode == StreamMode.Text)
                _content.Append("ET\n");

            _streamMode = StreamMode.Graphic;
        }
    }

    StreamMode _streamMode;

    private static ReadOnlySpan<char> HexDigits => "0123456789ABCDEF";

    /// <summary>
    /// Makes the specified pen and brush to the current graphics objects.
    /// </summary>
    private void Realize(Pen? pen, Brush? brush)
    {
        BeginPage();
        BeginGraphicMode();
        RealizeTransform();

        if (pen is not null)
            _gfxState.RealizePen(pen, _colorMode); // page.document.Options.ColorMode);

        if (brush is not null)
        {
            // Render mode is 0 except for bold simulation.
            _gfxState.RealizeBrush(brush, _colorMode, 0, 0); // page.document.Options.ColorMode);
        }
    }

    /// <summary>
    /// Makes the specified pen to the current graphics object.
    /// </summary>
    void Realize(Pen pen)
    {
        Realize(pen, null);
    }

    /// <summary>
    /// Makes the specified brush to the current graphics object.
    /// </summary>
    void Realize(Brush brush)
    {
        Realize(null, brush);
    }

    /// <summary>
    /// Makes the specified font and brush to the current graphics objects.
    /// </summary>
    void Realize(TypefaceFont font, Brush brush, int renderingMode, double letterSpacing = 0)
    {
        BeginPage();
        RealizeTransform();
        BeginTextMode();
        _gfxState.RealizeFont(font, brush, renderingMode, letterSpacing);
    }

    /// <summary>
    /// Makes the specified image to the current graphics object.
    /// </summary>
    string Realize(PdfRasterImage image, double width, double height)
    {
        BeginPage();
        BeginGraphicMode();
        RealizeTransform();

        // The transparency set for a brush also applies to images. Set opacity to 100% so image will be drawn without transparency.
        _gfxState.RealizeNonStrokeTransparency(1, _colorMode);

        PdfForm? form = image as PdfForm;
        if (form is not null)
            return GetFormName(form);

        // width/height is the caller's own destRect, before any transform active on the graphics
        // state (an SVG viewport transform, or a CSS `transform: scale(...)`) is applied - fold in
        // that transform's own scale so a downscale decision made in GetImageName sees the image's
        // true final on-page size. Ordinary (untransformed) draws have an identity/translation-only
        // CTM here, so this is a no-op (both scales == 1) for the common case.
        //
        // Per-axis, not a single geometric-mean-of-determinant scalar: the length of the CTM's own
        // transformed X/Y basis vectors (columns (M11,M12) and (M21,M22)) gives the true horizontal/
        // vertical scale even when they differ (CSS `scaleX`/`scaleY`, a non-uniform `matrix()`), and
        // is exact under rotation too, since rotation preserves each basis vector's length. A single
        // sqrt(|det|) scalar applied to both axes - the area scale factor - would be wrong for any
        // non-uniform scale (e.g. `scaleX(3)` on a square image would under-size the stretched axis
        // and over-size the other).
        var ctm = _gfxState.RealizedCtm;
        var scaleX = Math.Sqrt(ctm.M11 * ctm.M11 + ctm.M12 * ctm.M12);
        var scaleY = Math.Sqrt(ctm.M21 * ctm.M21 + ctm.M22 * ctm.M22);
        return GetImageName(image, width * scaleX, height * scaleY);
    }

    /// <summary>
    /// Realizes the current transformation matrix, if necessary.
    /// </summary>
    void RealizeTransform()
    {
        BeginPage();

        if (_gfxState.Level == GraphicsStackLevelPageSpace)
        {
            BeginGraphicMode();
            SaveState();
        }

        //if (gfxState.MustRealizeCtm)
        if (!_gfxState.UnrealizedCtm.IsIdentity)
        {
            BeginGraphicMode();
            _gfxState.RealizeCtm();
        }
    }

    /// <summary>
    /// Convert a point from Windows world space to PDF world space.
    /// </summary>
    internal PaintPoint WorldToView(PaintPoint point)
    {
        // If EffectiveCtm is not yet realized InverseEffectiveCtm is invalid.
        Debug.Assert(_gfxState.UnrealizedCtm.IsIdentity, "Somewhere a RealizeTransform is missing.");
        // See in #else case why this is correct.
        PaintPoint pt = _gfxState.WorldTransform.Transform(point);
        return _gfxState.InverseEffectiveCtm.Transform(new PaintPoint(pt.X, PageHeightPt / DefaultViewMatrix.M22 - pt.Y));
    }

    /// <summary>
    /// Gets the owning PdfDocument of this page or form.
    /// </summary>
    internal PdfDocument Owner
    {
        get
        {
            if (_page is not null)
                return _page.Owner;
            return _form.Owner;
        }
    }

    internal PdfGraphics Gfx => _gfx;

    /// <summary>
    /// Gets the PdfResources of this page or form.
    /// </summary>
    internal PdfResources Resources
    {
        get
        {
            if (_page is not null)
                return _page.Resources;
            return _form.Resources;
        }
    }

    /// <summary>
    /// Gets the size of this page or form.
    /// </summary>
    internal Size Size
    {
        get
        {
            if (_page is not null)
                return new Size(_page.Width, _page.Height);
            return _form.Size;
        }
    }

    internal PdfPage _page = null!;

    internal PdfForm _form = null!;

    internal PdfColorMode _colorMode;

    PdfPageAppendMode _options;

    PdfGraphics _gfx;

    readonly PdfContentWriter _content;

    /// <summary>
    /// The q/Q nesting level is 0.
    /// </summary>
    const int GraphicsStackLevelInitial = 0;

    /// <summary>
    /// The q/Q nesting level is 1.
    /// </summary>
    const int GraphicsStackLevelPageSpace = 1;

    /// <summary>
    /// The q/Q nesting level is 2.
    /// </summary>
    const int GraphicsStackLevelWorldSpace = 2;

    /// <summary>
    /// Saves the current graphical state.
    /// </summary>
    void SaveState()
    {
        Debug.Assert(_streamMode == StreamMode.Graphic, "Cannot save state in text mode.");

        _gfxStateStack.Push(_gfxState);
        _gfxState = _gfxState.Clone();
        _gfxState.Level = _gfxStateStack.Count;
        Append("q\n");
    }

    /// <summary>
    /// Restores the previous graphical state.
    /// </summary>
    void RestoreState()
    {
        Debug.Assert(_streamMode == StreamMode.Graphic, "Cannot restore state in text mode.");

        _gfxState = _gfxStateStack.Pop();
        Append("Q\n");
    }

    PdfGraphicsState RestoreState(InternalGraphicsState state)
    {
        int count = 1;
        PdfGraphicsState top = _gfxStateStack.Pop();
        while (top.InternalState != state)
        {
            Append("Q\n");
            count++;
            top = _gfxStateStack.Pop();
        }
        Append("Q\n");
        _gfxState = top;
        return top;
    }

    /// <summary>
    /// The current graphical state.
    /// </summary>
    PdfGraphicsState _gfxState;

    /// <summary>
    /// The graphical state stack.
    /// </summary>
    readonly Stack<PdfGraphicsState> _gfxStateStack = new Stack<PdfGraphicsState>();

    /// <summary>
    /// The height of the PDF page in point including the trim box.
    /// </summary>
    public double PageHeightPt;

    /// <summary>
    /// The final transformation from the world space to the default page space.
    /// </summary>
    public Matrix3x2 DefaultViewMatrix = Matrix3x2.Identity;


}
