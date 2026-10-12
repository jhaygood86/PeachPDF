// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

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
internal sealed class PdfGraphicsRenderer
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

            _gfx = null;
            _page.RenderContent._pdfRenderer = null;
            _page.RenderContent = null;
            _page = null;
        }
        else if (_form is not null)
        {
            _form._pdfForm.CreateStream(GetContent());
            _content.Clear();
            _gfx = null;
            _form.PdfRenderer = null;
            _form = null;
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

    /// <summary>
    /// Records where the run just written left the pen, for <see cref="TryShowAtPen"/>; forgets it for
    /// a font shown without shaping (<paramref name="glyphs"/> null), which never continues one.
    /// </summary>
    private void RememberTextRun(TypefaceFont font, IReadOnlyList<PlacedGlyph>? glyphs, double x, double laidOutEndX, double baselineY) =>
        _textRun = glyphs is null ? null : new TextRun(_content.Length, x + ReaderAdvance(font, glyphs), laidOutEndX, baselineY);

    /// <summary>
    /// Shows <paramref name="text"/> - a run of spaces - straight after the text-showing operator
    /// written last, where that operator left the pen, instead of moving the text line to
    /// (<paramref name="x"/>, <paramref name="baselineY"/>) with a <c>Td</c> of its own. That is how a
    /// word separator anchored at the end of the word before it is written, and equally a run of
    /// preserved white space that starts where the word before it ends. Only when nothing has been
    /// written since that operator but this call's own text state, when <paramref name="x"/> is
    /// within a tenth of an em of where layout put that run's end, and
    /// within a quarter of an em of its baseline. Anywhere else the spaces do not follow that run
    /// - the last run was on another line, the word in between drew nothing (it is
    /// <c>visibility: hidden</c>), the separator was anchored to the other side of its word (right to
    /// left) - and they are positioned on their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point is what this leaves alone: showing text moves the text matrix but not the text line
    /// matrix a following <c>Td</c> is relative to (ISO 32000-1 §9.4.2), so the next word's
    /// <c>Td</c> - and every one after it - is byte for byte the one written without the separator.
    /// Readers keep the line matrix in single precision, and a <c>Td</c> of its own for each
    /// separator rounds it differently: a few hundred-thousandths of a point, which still moves a
    /// glyph edge across a pixel boundary in PDFium and MuPDF where it sat exactly on one. Showing
    /// the space inside the previous word's own operator would keep the line matrix too, but PDFium
    /// then aligns that word's glyphs to the pixel grid differently, moving whole words by a pixel.
    /// </para>
    /// <para>
    /// The glyphs land at the pen rather than at <paramref name="x"/>. The two differ by the
    /// rounding of the font's <c>/W</c> widths, or by more after a word drawn glyph by glyph (see
    /// <see cref="DrawPositionedGlyphs"/>), whose pen does not take <c>letter-spacing</c> and stops
    /// short of the word's laid-out end. Either way a space has no ink, and the pen is the start of
    /// the gap a reader sees. Moving it on to <paramref name="x"/> - with a <c>TJ</c> adjustment or
    /// a <c>Td</c> - left a strip of that gap in front of the space, which pypdf reads as a word
    /// break of its own and doubles. Whether the spaces follow the run is decided against the
    /// laid-out end instead, which <paramref name="x"/> is exactly when they do. Nor does it matter
    /// whether either was meant for the synthetic-italic text matrix: its shear keeps a horizontal
    /// advance horizontal, and a space has no ink to slant.
    /// </para>
    /// </remarks>
    private bool TryShowAtPen(double x, double baselineY, string text, TypefaceFont font, int contentLengthAtEntry)
    {
        double em = font.Size;
        if (_textRun is not { } run || run.ContentLength != contentLengthAtEntry
            || Math.Abs(run.BaselineY - baselineY) > em / 4
            || Math.Abs(run.LaidOutEndX - x) > em / 10)
            return false;

        AppendFormatArgs("{0} Tj\n", text);

        // The pen has moved on, but nothing after a separator continues from it.
        _textRun = null;
        return true;
    }

    /// <summary>
    /// How far a reader's pen moves while showing <paramref name="glyphs"/> in <paramref name="font"/>:
    /// the <c>/W</c> width written for each glyph (see <see cref="PdfTypefaceMetrics.GlyphWidth"/>), at
    /// the font size and character spacing as written.
    /// </summary>
    private double ReaderAdvance(TypefaceFont font, IReadOnlyList<PlacedGlyph> glyphs)
    {
        double fontSize = Math.Round(font.Size, 3, MidpointRounding.AwayFromZero);
        double charSpace = Math.Round(_gfxState.RealizedCharSpace, 3, MidpointRounding.AwayFromZero);
        double thousandths = 0;
        foreach (PlacedGlyph glyph in glyphs)
            thousandths += PdfTypefaceMetrics.GlyphWidth(font.Typeface, glyph.GlyphIndex);
        return thousandths * fontSize / 1000 + glyphs.Count * charSpace;
    }

    public void DrawString(string s, TypefaceFont font, Brush brush, Rect rect, PdfStringFormat format, double letterSpacing, FontPalette? fontPalette, ShapeSettings features, string? logicalText = null)
    {
        int contentLengthAtEntry = _content.Length;
        bool isOnlySpaces = s.Length > 0 && !s.AsSpan().ContainsAnyExcept(' ');
        double x = rect.X;
        double y = rect.Y;

        double lineSpace = font.Height;
        double cyAscent = lineSpace * font.Typeface.Metrics.CellAscent / font.Typeface.Metrics.LineSpacing;
        double cyDescent = lineSpace * font.Typeface.Metrics.CellDescent / font.Typeface.Metrics.LineSpacing;

        //bool bold = (font.Style & PaintFontStyle.Bold) != 0;
        //bool italic = (font.Style & PaintFontStyle.Italic) != 0;
        bool italicSimulation = (font.SyntheticStyle & SyntheticStyle.Italic) != 0;
        bool boldSimulation = (font.SyntheticStyle & SyntheticStyle.Bold) != 0;

        // Merely reading the typeface does not realize/embed the color font below.
        TypefaceMetrics metrics = font.Typeface.Metrics;
        // Invisible text (see PdfGraphics.InvisibleText) is always the plain outline text object, never colour artwork.
        bool invisible = Gfx.InvisibleText;
        bool isColorFont = font.Typeface.HasColorGlyphs && !invisible;
        IReadOnlyList<PlacedGlyph>? colorGlyphs = isColorFont ? Shaper.Shape(font.Typeface, s, features).Glyphs : null;
        double width = colorGlyphs is not null && CanMeasureAsSingleShapedRun(s)
            ? MeasureShapedRunWidth(s, font, colorGlyphs)
            : _gfx.MeasureString(s, font, features).Width;

        if (!isColorFont)
            Realize(font, brush, invisible ? 3 : boldSimulation ? 2 : 0, letterSpacing);

        switch (format.Alignment)
        {
            case PdfStringAlignment.Near:
                // nothing to do
                break;

            case PdfStringAlignment.Center:
                x += (rect.Width - width) / 2;
                break;

            case PdfStringAlignment.Far:
                x += rect.Width - width;
                break;
        }
        switch (format.LineAlignment)
        {
            case PdfLineAlignment.Near:
                y += cyAscent;
                break;

            case PdfLineAlignment.Center:
                // TODO: Use CapHeight. PDFlib also uses 3/4 of ascent
                y += (cyAscent * 3 / 4) / 2 + rect.Height / 2;
                break;

            case PdfLineAlignment.Far:
                y += -cyDescent + rect.Height;
                break;

            case PdfLineAlignment.BaseLine:
                // Nothing to do.
                break;
        }
        

        const string format2 = PdfNumberFormats.FourDecimals;

        if (isColorFont)
        {
            // PaintColor fonts (COLR/CPAL): paint each glyph's color layers as vector fills rather than
            // showing the font's monochrome outlines. Each vector glyph carries its exact source as
            // /ActualText plus a rendering-mode-3 CID text show at the same position: /ActualText
            // preserves ambiguous sequences exactly, while the invisible text supplies selection
            // geometry to viewers which cannot select marked vector content alone. The resolved
            // font-palette (index + entry overrides), when present, selects the CPAL palette.
            int paletteIndex = fontPalette?.BasePaletteIndex ?? 0;
            var colorPainter = new ColorGlyphPainter(this, font, brush, x, y,
                letterSpacing, paletteIndex, fontPalette?.Overrides.ToDictionary(entry => entry.Key, entry => entry.Value));
            colorPainter.Paint(s, colorGlyphs!, logicalText);
        }
        else
        {
            PdfFont realizedFont = _gfxState._realizedFont;
            Debug.Assert(realizedFont is not null);
            realizedFont.AddShapedText(s, features, logicalText);

            string text = null;
            IReadOnlyList<PlacedGlyph> shapedGlyphs = null;
            bool hasGposDeltas = false;
            shapedGlyphs = Shaper.Shape(font.Typeface, s, features).Glyphs;
            RequireNoMissingGlyphsForPdfA(shapedGlyphs, font);
            StringBuilder sb = new StringBuilder();
            foreach (PlacedGlyph glyph in shapedGlyphs)
            {
                sb.Append((char)glyph.GlyphIndex);
                if (glyph.XAdvanceDelta != 0 || glyph.YAdvanceDelta != 0 || glyph.XOffset != 0 || glyph.YOffset != 0)
                    hasGposDeltas = true;
            }
            s = sb.ToString();

            byte[] bytes = PdfEncoders.RawBigEndianUnicodeEncoding.GetBytes(s);
            bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true);
            text = PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);

            // Map absolute position to PDF world space.
            PaintPoint pos = new PaintPoint(x, y);
            pos = WorldToView(pos);

            double verticalOffset = 0;
            if (boldSimulation)
            {
                // Adjust baseline in case of bold simulation???
                // No, because this would change the center of the glyphs.
                //verticalOffset = font.Size * PdfConstants.BoldEmphasis / 2;
            }

            double laidOutEndX = x + width + (shapedGlyphs?.Count ?? 0) * letterSpacing;
            bool shownAtPen = isOnlySpaces
                && TryShowAtPen(x, y + verticalOffset, text, font, contentLengthAtEntry);
            if (!shownAtPen)
            {
            // A declared "oblique <angle>" (see TypefaceFont.ObliqueSkewSinus) drives the exact shear amount;
            // plain "italic"/bare "oblique" (the common case, ObliqueSkewSinus null) keeps the fixed
            // sin(20°) approximation this renderer has always used.
            double skewSinus = font.ObliqueSkewSinus ?? PdfConstants.ItalicSkewAngleSinus;

            if (italicSimulation)
            {
                if (_gfxState.ItalicSimulationOn)
                {
                    AdjustTdOffset(ref pos, verticalOffset, skewSinus);
                    AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} Td\n{2} Tj\n", pos.X, pos.Y, text);
                    RememberTextRun(font, shapedGlyphs, x, laidOutEndX, y + verticalOffset);
                }
                else
                {
                    // Italic simulation is done by skewing characters to the right.
                    Matrix3x2 m = PdfMatrix.Create(1, 0, skewSinus, 1, pos.X, pos.Y);
                    AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} {2:" + format2 + "} {3:" + format2 + "} {4:" + format2 + "} {5:" + format2 + "} Tm\n{6} Tj\n",
                        m.M11, m.M12, m.M21, m.M22, m.M31, m.M32, text);
                    _gfxState.ItalicSimulationOn = true;
                    AdjustTdOffset(ref pos, verticalOffset, null);
                    RememberTextRun(font, shapedGlyphs, x, laidOutEndX, y + verticalOffset);
                }
            }
            else
            {
                if (_gfxState.ItalicSimulationOn)
                {
                    Matrix3x2 m = PdfMatrix.Create(1, 0, 0, 1, pos.X, pos.Y);
                    AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} {2:" + format2 + "} {3:" + format2 + "} {4:" + format2 + "} {5:" + format2 + "} Tm\n{6} Tj\n",
                        m.M11, m.M12, m.M21, m.M22, m.M31, m.M32, text);
                    _gfxState.ItalicSimulationOn = false;
                    AdjustTdOffset(ref pos, verticalOffset, null);
                    RememberTextRun(font, shapedGlyphs, x, laidOutEndX, y + verticalOffset);
                }
                else if (hasGposDeltas)
                {
                    // At least one glyph carries a nonzero GPOS delta (kerning's XAdvanceDelta/
                    // XOffset, or mark positioning's XOffset/YOffset - see GposPositioner) that a
                    // static per-glyph-id /W array entry can't express. Paint each glyph as its own
                    // individually positioned Td+Tj instead of one Tj over the whole run - the
                    // common case (no GPOS deltas) never reaches this branch, so existing output is
                    // otherwise byte-for-byte unchanged.
                    DrawPositionedGlyphs(shapedGlyphs, font, new PaintPoint(x, y + verticalOffset), laidOutEndX);
                }
                else
                {
                    AdjustTdOffset(ref pos, verticalOffset, null);
                    AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} Td {2} Tj\n", pos.X, pos.Y, text);
                    RememberTextRun(font, shapedGlyphs, x, laidOutEndX, y + verticalOffset);
                }
            }
            }
        }

    }

    private static bool CanMeasureAsSingleShapedRun(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value < 32)
                return false;
        }

        return true;
    }

    private static double MeasureShapedRunWidth(string text, TypefaceFont font, IReadOnlyList<PlacedGlyph> glyphs)
    {
        int designWidth = 0;
        for (int i = 0; i < glyphs.Count; i++)
        {
            PlacedGlyph glyph = glyphs[i];
            designWidth += (int)Math.Round(font.Typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta);
        }

        double width = designWidth * font.Size / font.Typeface.Metrics.UnitsPerEm;
        if ((font.SyntheticStyle & SyntheticStyle.Bold) != 0)
        {
            int characterCount = 0;
            foreach (Rune _ in text.EnumerateRunes())
                characterCount++;
            width += characterCount * font.Size * PdfConstants.BoldEmphasis;
        }

        return width;
    }

    /// <summary>
    /// Throws if <paramref name="glyphs"/> contains a reference to the missing-glyph placeholder
    /// (glyph index 0, ".notdef") and PDF/A conformance is requested - every PDF/A part forbids a
    /// text-showing operator from referencing it (e.g. ISO 19005-2 §6.2.11.8), so this is checked
    /// right where a shaped run is produced, before it ever reaches a Tj. The ordinary CID/Unicode
    /// path checks a complete shaped run; color-font vector painting checks each source-bearing
    /// glyph before emitting its invisible selection text.
    /// </summary>
    void RequireNoMissingGlyphsForPdfA(IReadOnlyList<PlacedGlyph> glyphs, TypefaceFont font)
    {
        if (Owner.Options.PdfAConformance == PdfAConformance.None)
            return;

        foreach (var glyph in glyphs)
        {
            if (glyph.GlyphIndex == 0)
            {
                // PdfAConformanceException (not a plain InvalidOperationException) so this specific,
                // actionable message reaches the caller unwrapped - see its own remarks on why
                // FragmentPainter's generic paint-error catch would otherwise fold it into a vague
                // "Exception in box paint" HtmlRenderException.
                throw new PdfAConformanceException(
                    $"The font '{font.Typeface.FamilyName}' has no glyph for one or more characters in this text, " +
                    "and no fallback font covers them either. PDF/A forbids a text-showing operator " +
                    "from referencing the missing-glyph placeholder (.notdef - e.g. ISO 19005-2 " +
                    "§6.2.11.8). Add a font-family fallback that covers every character in the " +
                    "document (CSS font-family list, or PdfGenerator.AddFontFromStream), or remove " +
                    "the uncovered character(s).");
            }
        }
    }

    /// <summary>
    /// Paints <paramref name="glyphs"/> (already shaped, in <paramref name="startPos"/>'s world
    /// space) as a sequence of individually positioned Td+Tj pairs, one glyph at a time, instead
    /// of a single Tj over the whole run - see the one caller in <see cref="DrawString"/>. Only
    /// horizontal (X) positioning is affected by page direction sign flips the same way the rest
    /// of this renderer's Y math already is; <see cref="PlacedGlyph.YAdvanceDelta"/> (vertical
    /// writing mode's own advance axis) is intentionally not applied here - this renderer draws
    /// horizontal text only.
    /// </summary>
    void DrawPositionedGlyphs(IReadOnlyList<PlacedGlyph> glyphs, TypefaceFont font, PaintPoint startPos, double laidOutEndX)
    {
        const string format2 = PdfNumberFormats.FourDecimals;
        double scale = font.Size / font.Typeface.Metrics.UnitsPerEm;

        double penX = startPos.X;
        double penY = startPos.Y;
        double lastGlyphX = penX, lastGlyphY = penY;

        foreach (PlacedGlyph glyph in glyphs)
        {
            double glyphX = penX + glyph.XOffset * scale;
            // Font em-square is y-up; a downwards page is y-down, so a positive OpenType YOffset
            // (moves a glyph up in font space) subtracts in downwards-page Y, adds otherwise -
            // the same convention ColorGlyphPainter.Placement uses for its own Y offset.
            double glyphY = penY - glyph.YOffset * scale;

            PaintPoint glyphPos = WorldToView(new PaintPoint(glyphX, glyphY));
            AdjustTdOffset(ref glyphPos, 0, null);

            byte[] bytes = PdfEncoders.RawBigEndianUnicodeEncoding.GetBytes(((char)glyph.GlyphIndex).ToString());
            bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true);
            string glyphText = PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);

            AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} Td {2} Tj\n", glyphPos.X, glyphPos.Y, glyphText);

            // XOffset is a placement-only shift (doesn't move where the next glyph starts) - the
            // pen only ever advances by the natural width plus GPOS's own XAdvanceDelta, matching
            // FontHelper.MeasureString/PdfCanvas.GetTextOutline's identical advance math.
            penX += (font.Typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale;
            (lastGlyphX, lastGlyphY) = (glyphX, glyphY);
        }

        // A reader's pen ends where the last glyph's own Tj leaves it.
        _textRun = glyphs.Count == 0 ? null
            : new TextRun(_content.Length, lastGlyphX + ReaderAdvance(font, [glyphs[^1]]), laidOutEndX, lastGlyphY);
    }

    /// <summary>
    /// Draws each glyph in <paramref name="glyphs"/> at its own explicit world-space position,
    /// addressed directly by font glyph index - the same per-glyph <c>Td</c>+<c>Tj</c> emission
    /// <see cref="DrawPositionedGlyphs"/> already uses, generalized to take already-absolute
    /// positions instead of shaped-run deltas relative to a pen, and to realize the font/brush
    /// itself (its one caller, <see cref="DrawString(string, TypefaceFont, Brush, Rect, PdfStringFormat, double, FontPalette?, ShapeSettings, string)"/>,
    /// already did that before ever reaching <see cref="DrawPositionedGlyphs"/>). No bold/italic
    /// simulation, underline, or strikeout handling - this exists for MathML's stretchy-operator
    /// glyph assemblies (see <c>MathRenderer</c>), which are never any of those.
    /// </summary>
    public void DrawGlyphsAtPositions(IReadOnlyList<(int GlyphIndex, double X, double Y)> glyphs, TypefaceFont font, Brush brush)
    {
        if (glyphs.Count == 0)
            return;

        Realize(font, brush, 0, 0);

        // Unlike DrawString (which calls realizedFont.AddShapedText over the whole run before its
        // own draw loop), these glyphs never go through shaping at all - nothing else registers
        // them with the embedded font's subsetter/CID map. Without this, each glyph index drawn
        // below would reference a glyph the subset font never actually includes, rendering blank.
        // No real source text exists for these (a stretchy-operator assembly part/size variant
        // has no Unicode meaning of its own), so ToUnicode gets an empty mapping per glyph - the
        // glyph itself still renders correctly, only copy/paste extraction is affected.
        PdfFont realizedFont = _gfxState._realizedFont;
        foreach (var glyph in glyphs)
            realizedFont?.AddShapedGlyph(glyph.GlyphIndex, "");

        const string format2 = PdfNumberFormats.FourDecimals;
        foreach (var glyph in glyphs)
        {
            PaintPoint pos = WorldToView(new PaintPoint(glyph.X, glyph.Y));
            AdjustTdOffset(ref pos, 0, null);

            byte[] bytes = PdfEncoders.RawBigEndianUnicodeEncoding.GetBytes(((char)glyph.GlyphIndex).ToString());
            bytes = PdfEncoders.FormatStringLiteral(bytes, true, false, true);
            string glyphText = PdfEncoders.LatinOneByteEncoding.GetString(bytes, 0, bytes.Length);

            AppendFormatArgs("{0:" + format2 + "} {1:" + format2 + "} Td {2} Tj\n", pos.X, pos.Y, glyphText);
        }
    }

    // ----- DrawImage ----------------------------------------------------------------------------

    public void DrawImage(PdfRasterImage image, double x, double y, double width, double height)
    {
        const string format = PdfNumberFormats.FourDecimals;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm 100 Tz {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

    /// <summary>
    /// Draws all of <paramref name="image"/> into <paramref name="destRect"/>. <paramref name="srcRect"/>
    /// is NOT honored and cannot be at this level: PDF has no "draw this sub-rectangle of an XObject"
    /// operator, so cropping is a clip plus an off-destination placement of the whole image, which
    /// <c>PdfCanvas.DrawImage</c> composes above. Callers wanting a portion of an image must go
    /// through that; this overload exists so a whole-image draw keeps emitting the operators it always
    /// has. (Silently dropping srcRect here is what once made every <c>border-image</c> slice paint the
    /// entire source squashed into its own ninth of the frame.)
    /// </summary>
    public void DrawImage(PdfRasterImage image, Rect destRect, Rect srcRect)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do\nQ\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

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
    /// Appends a Bézier curve for a cardinal spline through pt1 and pt2.
    /// </summary>
    void AppendCurveSegment(PaintPoint pt0, PaintPoint pt1, PaintPoint pt2, PaintPoint pt3, double tension3)
    {
        AppendBezier(
            pt1.X + tension3 * (pt2.X - pt0.X), pt1.Y + tension3 * (pt2.Y - pt0.Y),
            pt2.X - tension3 * (pt3.X - pt1.X), pt2.Y - tension3 * (pt3.Y - pt1.Y),
            pt2.X, pt2.Y);
    }

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

    private void AppendMatrix(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        PaintPoint point1 = WorldToView(new PaintPoint(x1, y1));
        PaintPoint point2 = WorldToView(new PaintPoint(x2, y2));
        PaintPoint point3 = WorldToView(new PaintPoint(x3, y3));
        AppendPdfNumber(_content, point1.X, PdfNumberFormats.SevenDecimals);
        _content.Append(' ');
        AppendPdfNumber(_content, point1.Y, PdfNumberFormats.SevenDecimals);
        _content.Append(' ');
        AppendPdfNumber(_content, point2.X, PdfNumberFormats.SevenDecimals);
        _content.Append(' ');
        AppendPdfNumber(_content, point2.Y, PdfNumberFormats.SevenDecimals);
        _content.Append(' ');
        AppendPdfNumber(_content, point3.X, PdfNumberFormats.SevenDecimals);
        _content.Append(' ');
        AppendPdfNumber(_content, point3.Y, PdfNumberFormats.SevenDecimals);
        _content.Append(" cm ");
    }

    internal static void AppendPdfNumber(PdfContentWriter content, double value, string format)
    {
        Span<char> buffer = stackalloc char[64];
        if (value.TryFormat(buffer, out int written, format.AsSpan(), CultureInfo.InvariantCulture))
        {
            content.Append(buffer[..written]);
            return;
        }

        content.Append(value.ToString(format, CultureInfo.InvariantCulture));
    }

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

    internal void AppendFormatImage(string format, double x, double y, double width, double height, string name)
    {
        PaintPoint result = WorldToView(new PaintPoint(x, y));
        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, width, height, name);
    }

    void AppendStrokeFill(Pen pen, Brush brush, FillMode fillMode, bool closePath)
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
        // Take TrimBox into account.
        PageHeightPt = Size.Height;
        PaintPoint trimOffset = new PaintPoint();
        if (_page is not null && _page.TrimMargins.AreSet)
        {
            PageHeightPt += _page.TrimMargins.Top + _page.TrimMargins.Bottom;
            trimOffset = new PaintPoint(_page.TrimMargins.Left, _page.TrimMargins.Top);
        }

        if (trimOffset != new PaintPoint())
        {
            DefaultViewMatrix = PdfMatrix.Translation(trimOffset.X, -trimOffset.Y) * DefaultViewMatrix;
        }

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

    /// <summary>
    /// Begins the graphic mode (i.e. ends the text mode).
    /// </summary>
    internal void BeginTextMode()
    {
        if (_streamMode != StreamMode.Text)
        {
            _streamMode = StreamMode.Text;
            _content.Append("BT\n");
            // Text matrix is empty after BT
            _gfxState.RealizedTextPosition = new PaintPoint();
            _gfxState.ItalicSimulationOn = false;
        }
    }

    StreamMode _streamMode;

    /// <summary>
    /// Begins a tagged marked-content sequence ("/Tag&lt;&lt;/MCID n&gt;&gt;BDC") associated
    /// with the given marked-content identifier. <paramref name="structureType"/> must already
    /// be a slash-prefixed PDF name (e.g. "/P", "/Span") - the BDC operator's tag operand is
    /// written verbatim, with no prefix added here. PDF permits a marked-content sequence to
    /// either fully contain a text object (BDC BT ... ET EMC) or be fully contained inside one
    /// (BT BDC ... EMC ET), but never straddle a BT/ET boundary - callers must only wrap this
    /// around a whole leaf box's own paint calls, never around part of one.
    /// </summary>
    internal void BeginMarkedContent(string structureType, int mcid)
    {
        BeginPage();
        _content.Append($"{structureType}<</MCID {mcid}>>BDC\n");
    }

    /// <summary>
    /// Begins a <c>/Span</c> marked-content sequence whose invisible text represents
    /// <paramref name="actualText"/>. The value is a Unicode PDF text string (UTF-16BE with BOM),
    /// allowing color-font glyphs painted as paths to preserve their exact source text when copied.
    /// </summary>
    internal void BeginActualText(string actualText)
    {
        Debug.Assert(_streamMode == StreamMode.Text);

        _content.Append("/Span<</ActualText <FEFF");
        for (int i = 0; i < actualText.Length; i++)
            AppendHexCodeUnit(actualText[i]);
        _content.Append(">>>BDC\n");
    }

    /// <summary>Begins the shared rendering-mode-3 text object for one color-glyph run.</summary>
    internal void BeginInvisibleTextRun(TypefaceFont font, Brush brush)
    {
        Realize(font, brush, renderingMode: 3);
    }

    /// <summary>Ends a color-glyph run's invisible text object before subsequent graphic content.</summary>
    internal void EndInvisibleTextRun()
    {
        BeginGraphicMode();
    }

    /// <summary>
    /// Shows one already-shaped glyph at its vector paint origin using PDF text rendering mode 3
    /// (neither fill nor stroke). The text object gives viewers selectable font-derived geometry;
    /// the surrounding marked-content sequence's <c>/ActualText</c> supplies its exact Unicode,
    /// including sequences which share the same glyph ID.
    /// </summary>
    internal void DrawInvisibleGlyph(TypefaceFont font, PlacedGlyph glyph, string sourceText, double x, double y)
    {
        if (glyph.GlyphIndex == 0)
            RequireNoMissingGlyphsForPdfA(new[] { glyph }, font);

        Debug.Assert(_streamMode == StreamMode.Text);
        PdfFont realizedFont = _gfxState._realizedFont;
        Debug.Assert(realizedFont is not null);
        realizedFont.AddShapedGlyph(glyph.GlyphIndex, sourceText);

        PaintPoint pos = WorldToView(new PaintPoint(x, y));
        AdjustTdOffset(ref pos, 0, null);

        _content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} Td <", pos.X, pos.Y);
        AppendHexCodeUnit((char)glyph.GlyphIndex);
        _content.Append("> Tj\n");
    }

    private static ReadOnlySpan<char> HexDigits => "0123456789ABCDEF";

    private void AppendHexCodeUnit(char value)
    {
        Span<char> buffer = stackalloc char[4];
        buffer[0] = HexDigits[(value >> 12) & 0xF];
        buffer[1] = HexDigits[(value >> 8) & 0xF];
        buffer[2] = HexDigits[(value >> 4) & 0xF];
        buffer[3] = HexDigits[value & 0xF];
        _content.Append(buffer);
    }

    /// <summary>
    /// Ends a marked-content sequence started by <see cref="BeginMarkedContent"/> or
    /// <see cref="BeginArtifact"/>.
    /// </summary>
    internal void EndMarkedContent()
    {
        _content.Append("EMC\n");
    }

    /// <summary>
    /// Begins an artifact marked-content sequence ("/Artifact BMC") - used for content that is
    /// not part of the document's logical structure (e.g. a decorative &lt;hr&gt;).
    /// </summary>
    internal void BeginArtifact()
    {
        BeginPage();
        _content.Append("/Artifact BMC\n");
    }

    /// <summary>
    /// Begins the "/Tx" marked-content sequence that ISO 32000-1 §12.7.3.3 requires around the
    /// value a variable-text form field's appearance stream draws, and
    /// <see cref="EndVariableText"/> closes it.
    /// </summary>
    /// <remarks>
    /// This is not cosmetic structure: it is the contract by which a conforming reader updates a
    /// field the user edits. A reader regenerating the appearance keeps everything before "/Tx
    /// BMC" and after "EMC" - the field's own border and background - and replaces only what lies
    /// between them. A stream with no such sequence gives the reader no region to replace, so it
    /// appends the new value instead and the generated value stays visible underneath it: the
    /// old text, the new text, and (once the reader falls back to its own "/DA" font for codes
    /// written against an embedded subset) mojibake, all at once.
    /// <para>
    /// Both operators are emitted in graphic mode: §14.6 forbids a marked-content sequence from
    /// straddling a text object's BT/ET boundary, and the caller's own text drawing between them
    /// opens its own. This is why the pair exists rather than reusing
    /// <see cref="EndMarkedContent"/>, whose one other caller (<c>ColorGlyphPainter</c>) is
    /// deliberately inside a text object - the legal "BT BDC ... EMC ET" nesting - and would be
    /// cut short by forcing graphic mode there.
    /// </para>
    /// </remarks>
    internal void BeginVariableText()
    {
        BeginPage();
        BeginGraphicMode();
        _content.Append("/Tx BMC\n");
    }

    /// <summary>Closes the sequence <see cref="BeginVariableText"/> opened.</summary>
    internal void EndVariableText()
    {
        BeginGraphicMode();
        _content.Append("EMC\n");
    }

    /// <summary>
    /// Makes the specified pen and brush to the current graphics objects.
    /// </summary>
    private void Realize(Pen pen, Brush brush)
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
    /// PDFsharp uses the Td operator to set the text position. Td just sets the offset of the text matrix
    /// and produces lesser code as Tm.
    /// </summary>
    /// <param name="pos">The absolute text position.</param>
    /// <param name="dy">The dy.</param>
    /// <param name="skewSinus">Non-null (the sine of the italic-simulation skew angle actually in
    /// effect - see <see cref="TypefaceFont.ObliqueSkewSinus"/>) if skewing for italic simulation is
    /// currently on; null otherwise.</param>
    void AdjustTdOffset(ref PaintPoint pos, double dy, double? skewSinus)
    {
        pos.Y += dy;
        // Reference: TABLE 5.5  Text-positioning operators / Page 406
        PaintPoint posSave = pos;
        // Map from absolute to relative position.
        pos = new PaintPoint(pos.X - _gfxState.RealizedTextPosition.X, pos.Y - _gfxState.RealizedTextPosition.Y);
        if (skewSinus is { } sinus)
        {
            // In case that italic simulation is on X must be adjusted according to Y offset. Weird but works :-)
            pos.X -= sinus * pos.Y;
        }
        _gfxState.RealizedTextPosition = posSave;
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

        PdfForm form = image as PdfForm;
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
    /// Draws <paramref name="image"/> (a Form XObject, typically a rendered SVG "content" tile)
    /// at <paramref name="destRect"/> with <paramref name="maskImage"/> (another Form XObject,
    /// sized/positioned identically to <paramref name="image"/> in its own local coordinate
    /// system) attached as a <c>/Luminosity</c> soft mask (white = fully visible, black = fully
    /// transparent) via an ExtGState <c>/SMask</c> - mirroring the pattern <c>PdfShading</c>'s own
    /// alpha-gradient soft masks already use (see <c>BuildAlphaExtGStateIfNeeded</c>), just driven
    /// by an arbitrary rendered Form XObject rather than a hand-built grayscale shading function.
    /// </summary>
    /// <remarks>
    /// The <c>gs</c> that activates the mask is emitted INSIDE the same <c>q ... cm ... Do ... Q</c>
    /// block that places <paramref name="image"/> (mirroring <see cref="DrawImage(PdfRasterImage, Rect, Rect)"/>'s
    /// own Form XObject placement exactly), rather than as a separate "push before / pop after"
    /// pair relying on the ambient CTM. This distinction matters: a Form XObject created via
    /// <c>PdfGraphics.FromForm</c> (as SVG's <c>&lt;mask&gt;</c>/<c>&lt;pattern&gt;</c> "tile"
    /// rendering does) gets its own content Y-flipped relative to ITS OWN (small) height, not the
    /// page's - a coordinate convention that's only self-consistent when the form is placed via an
    /// explicit destRect (as here, and as pattern tiles already do via ordinary <c>DrawImage</c>).
    /// Relying on the CTM already ambient at some arbitrary point in the page's own content stream
    /// (built for page-style, not form-style, coordinates) to position such a form's implicit BBox
    /// - which an earlier, simpler "PushSoftMask/PopSoftMask" implementation of this feature did -
    /// silently misplaces the mask relative to the content in every case except where the two
    /// coordinate conventions happen to coincide (i.e. never, once the page has any margin/scroll/
    /// layout offset), making the masked content evaluate as fully transparent everywhere.
    /// <para>
    /// Separately, both <paramref name="image"/> and <paramref name="maskImage"/> are given their
    /// own <c>/Group &lt;&lt; /S /Transparency ... &gt;&gt;</c> dictionary. The masked CONTENT form
    /// needing this (not just the <c>/G</c> mask target) was found the hard way: MuPDF renders an
    /// active SMask correctly even when the painted content itself isn't a transparency group, but
    /// PDFium (Chrome/Edge, and by extension most other real-world viewers/printers) silently
    /// ignores the SMask entirely in that case - a PDF that "works" only needs checking in one
    /// renderer to look done and still be broken everywhere else. Don't trust a single renderer
    /// (least of all MuPDF, which is unusually forgiving of non-conformant transparency PDFs) for
    /// anything touching soft masks or transparency groups - cross-check with PDFium too (e.g.
    /// Python's <c>pypdfium2</c>) before calling it fixed.
    /// </para>
    /// </remarks>
    internal void DrawImageMasked(PdfForm image, PdfForm maskImage, Rect destRect)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();
        maskImage.Finish();

        // Checked before any group/mask/ExtGState object is built, so a PDF/A-1 rejection never
        // leaves partially-built objects registered in the document's resources/iref table.
        PdfATransparencyGuard.RequireAllowed(Owner, _page, "An SVG <mask>");

        // A /Luminosity SMask's /G target must itself be a transparency group (PDF 32000-1 §11.6.4.3)
        // - PdfForm/PdfFormXObject don't set this up automatically, so it's added here rather than
        // generalizing CreateTile's caller-agnostic form creation for a mask-only requirement.
        var maskPdfForm = maskImage.FormXObject;
        var maskGroup = new PdfDictionary();
        maskGroup.Elements["/S"] = new PdfName("/Transparency");
        maskGroup.Elements["/CS"] = new PdfName("/DeviceGray");
        maskGroup.Elements["/I"] = new PdfBoolean(true);
        maskGroup.Elements["/K"] = new PdfBoolean(false);
        maskPdfForm.Elements["/Group"] = maskGroup;

        // The CONTENT form being masked must itself be a transparency group too, or the active
        // SMask silently has no visible effect in stricter readers (Chrome/Edge/Acrobat, all
        // PDFium- or Acrobat-derived) even though it renders correctly in MuPDF regardless -
        // MuPDF applies an SMask to plain (non-group) painted content leniently; that's MuPDF
        // being forgiving of non-conformant input, not something to rely on. Every real-world PDF
        // writer that composites masked content via a Form XObject marks that content form as an
        // isolated transparency group too, not just the /G mask target.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var softMask = new PdfSoftMask(Owner);
        softMask.Elements[PdfSoftMask.Keys.S] = new PdfName("/Luminosity");
        softMask.Elements.SetReference(PdfSoftMask.Keys.G, maskPdfForm);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements["/SMask"] = softMask;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
        
    }

    /// <summary>
    /// Draws <paramref name="image"/> (a Form XObject, typically a whole box/element subtree
    /// rendered into an offscreen tile via <c>CreateTile</c>) at <paramref name="destRect"/>,
    /// composited as a single flattened result at constant <paramref name="opacity"/> - the PDF
    /// mechanism behind CSS/SVG group <c>opacity</c>. This is deliberately NOT the same as painting
    /// each of the tile's own shapes at reduced alpha individually (the simpler, but double-blending,
    /// approach SVG element opacity uses via <c>ApplyOpacity</c>): marking <paramref name="image"/>
    /// as an <b>isolated transparency group</b> (<c>/Group &lt;&lt; /S /Transparency /I true &gt;&gt;</c>)
    /// makes the PDF viewer flatten the tile's own (possibly overlapping) content into one RGBA
    /// result FIRST, at each shape's own local alpha, and only THEN apply the single <paramref name="opacity"/>
    /// value to that already-flattened result as it's composited onto the real page - so overlapping
    /// content inside the tile no longer double-darkens where it overlaps.
    /// </summary>
    /// <remarks>
    /// See the atomicity/coordinate-convention remarks on <see cref="DrawImageMasked"/> just above -
    /// the same reasoning applies here: the <c>gs</c> activating the alpha must be emitted inside the
    /// same <c>q ... cm ... Do ... Q</c> block that places <paramref name="image"/>, not separately.
    /// </remarks>
    internal void DrawImageWithOpacity(PdfForm image, Rect destRect, double opacity, string pdfBlendModeName = "Normal")
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();

        // Checked before any group/ExtGState object is built, so a PDF/A-1 rejection never leaves
        // partially-built objects registered in the document's resources/iref table. A non-Normal
        // blend mode is itself a transparency-group-requiring construct too (see SetBlendMode), but
        // opacity-less-than-1 already requires the same group, so one check covers both here.
        PdfATransparencyGuard.RequireAllowed(Owner, _page, "CSS/SVG opacity less than 1");

        // Must be an isolated transparency group so the tile's own (possibly overlapping) content is
        // flattened once before the constant alpha below is applied to the flattened result - see
        // the remarks above and on DrawImageMasked for why plain (non-group) content wouldn't work.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        // A single ExtGState carries both /ca+/CA (opacity) and /BM (mix-blend-mode) - the pair is
        // meant to composite as one operation against the destination, not as two separately-scoped
        // effects, so one gs/Do pairing is both simpler and correct.
        var extGState = Owner.ExtGStateTable.GetExtGState(opacity, pdfBlendModeName);
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        // The page is always laid out downwards, so there is only one placement to emit here.
        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/>, composited through
    /// <paramref name="matrix"/>'s channel-independent linear part via an ExtGState <c>/TR</c>
    /// transfer function - the mechanism behind channel-independent CSS <c>filter</c> functions
    /// (<c>brightness()</c>, <c>contrast()</c>, <c>invert()</c>) applied to a whole tile. See
    /// <see cref="ColorMatrix"/>'s remarks for the underlying PDF-spec research: a cross-channel
    /// matrix (<c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c>) cannot be
    /// expressed this way at all, so this throws for one rather than silently mis-rendering it.
    /// </summary>
    /// <remarks>
    /// Dual-rasterization check (this repo's own convention for anything touching graphics-state
    /// constructs - see <c>CLAUDE.md</c>'s Testing conventions) surfaced a real-world renderer gap
    /// worth knowing before debugging "it doesn't look filtered": a well-formed <c>invert()</c>
    /// matrix (a spec-correct <c>/TR</c>, verified structurally in
    /// <c>PdfGraphicsRendererColorMatrixAndMaskTests</c> and by hand against the raw PDF bytes)
    /// rendered correctly in PDFium (the red test rectangle came out cyan, as expected) but rendered
    /// completely UNCHANGED in MuPDF (still red) - MuPDF's rasterizer does not implement ExtGState
    /// <c>/TR</c> at all, silently treating it as absent rather than erroring. This is the mirror
    /// image of this codebase's usual soft-mask finding (MuPDF lenient, PDFium strict): here PDFium
    /// is the one actually honoring a legitimate, spec-conformant construct, and MuPDF is the one
    /// silently dropping it. Do not use a MuPDF-only render to conclude a channel-independent color
    /// matrix "does nothing" - check PDFium (or a real browser/Acrobat) too.
    /// </remarks>
    internal void DrawImageWithColorMatrix(PdfForm image, Rect destRect, ColorMatrix matrix)
    {
        if (!matrix.IsChannelIndependent)
        {
            throw new NotSupportedException(
                "This color matrix mixes color channels (e.g. grayscale()/sepia()/saturate()/hue-rotate() " +
                "or a feColorMatrix with off-diagonal coefficients), which a PDF ExtGState transfer " +
                "function (ISO 32000-1 §8.6.5.3) cannot express - a transfer function is evaluated " +
                "independently per color component and never sees another component's value. See " +
                "ColorMatrix's remarks for the mechanisms that do apply (a raster image's own DeviceN " +
                "tint-transform color space, or rasterizing the affected content) and why neither is a " +
                "drop-in replacement for compositing an already-rendered vector tile.");
        }

        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();

        PdfATransparencyGuard.RequireAllowed(Owner, _page, "CSS/SVG color-matrix filter");

        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var transferFunction = PdfType4Function.BuildChannelIndependentTransferFunction(Owner, matrix);

        var extGState = new PdfExtGState(Owner);
        extGState.TransferFunction = transferFunction;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/> with <paramref name="maskImage"/>
    /// attached as an <c>/Alpha</c>-subtype soft mask, deriving mask values from the mask tile's
    /// computed alpha rather than a luminosity conversion of its color - see
    /// <see cref="Canvas.DrawImageAlphaMasked"/> for the motivating SVG
    /// <c>SourceAlpha</c>/<c>feComposite</c> use case.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DrawImageMasked"/>'s <c>/Luminosity</c> mask, the mask form's own transparency
    /// group is NOT given a <c>/CS /DeviceGray</c> entry: ISO 32000-1 §11.6.5.2 only requires (and
    /// only consults) a group colour space when deriving a luminosity value from it - "If the
    /// subtype S is Luminosity, the group attributes dictionary shall contain a CS entry defining
    /// the colour space in which the compositing computation is to be performed" - and the BC
    /// (backdrop color) entry is likewise "consulted only if the subtype S is Luminosity". An Alpha
    /// mask's value is the group's own computed alpha, which has no colour space to speak of, so
    /// forcing DeviceGray here would be asserting a requirement the spec does not impose.
    /// </remarks>
    internal void DrawImageAlphaMasked(PdfForm image, PdfForm maskImage, Rect destRect, bool invert = false)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();
        maskImage.Finish();

        PdfATransparencyGuard.RequireAllowed(Owner, _page, "An SVG alpha mask (SourceAlpha/feComposite)");

        var maskPdfForm = maskImage.FormXObject;
        var maskGroup = new PdfDictionary();
        maskGroup.Elements["/S"] = new PdfName("/Transparency");
        maskGroup.Elements["/I"] = new PdfBoolean(true);
        maskGroup.Elements["/K"] = new PdfBoolean(false);
        maskPdfForm.Elements["/Group"] = maskGroup;

        // See DrawImageMasked's remarks: the masked CONTENT form must be its own transparency group
        // too, or stricter readers (PDFium/Acrobat) silently ignore the active SMask entirely.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var softMask = new PdfSoftMask(Owner);
        softMask.Elements[PdfSoftMask.Keys.S] = new PdfName("/Alpha");
        softMask.Elements.SetReference(PdfSoftMask.Keys.G, maskPdfForm);
        if (invert)
            softMask.TransferFunction = PdfType4Function.BuildInvertFunction(Owner);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements["/SMask"] = softMask;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// Paints <paramref name="bottom"/> normally at <paramref name="destRect"/>, then
    /// <paramref name="top"/> on top of it at the same rect composited with <paramref name="pdfBlendModeName"/> -
    /// see <see cref="Canvas.DrawImageBlendedOver"/> for the motivating SVG
    /// <c>feBlend</c> use case. <paramref name="pdfBlendModeName"/> crosses from
    /// <c>PaintBlendMode</c> as a plain string (via <c>PaintBlendMode.ToString()</c> at the
    /// <c>PdfCanvas</c> call site) rather than as a typed enum, matching the same crossing
    /// point <see cref="SetBlendMode"/> already uses - The PDF engine sits below
    /// the HTML layers in this codebase's layering, so it cannot reference
    /// <c>PaintBlendMode</c> directly (see <see cref="SetBlendMode"/>).
    /// </summary>
    internal void DrawImageBlendedOver(PdfForm top, PdfForm bottom, Rect destRect, string pdfBlendModeName)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string topName = Realize(top, width, height);
        string bottomName = Realize(bottom, width, height);

        BeginPage();
        top.Finish();
        bottom.Finish();

        // A non-Normal /BM is the transparency-group-requiring construct PDF/A-1 forbids (see
        // SetBlendMode's own remarks) - a Normal-mode feBlend is just two ordinary Form XObject
        // placements and needs no such check, matching SetBlendMode's identical carve-out.
        if (pdfBlendModeName is not ("Normal" or "Compatible"))
            PdfATransparencyGuard.RequireAllowed(Owner, _page, $"An SVG feBlend ({pdfBlendModeName})");

        double cx = width / bottom.PointWidth;
        double cy = height / bottom.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        // bottom paints normally; top's own placement carries the blend-mode gs, scoped to just
        // that Do by the surrounding q...Q (same atomicity reasoning as DrawImageMasked/
        // DrawImageWithOpacity above - the gs activating a non-default graphics-state parameter must
        // share the q...Q block of the Do it's meant to affect, not rely on ambient state).
        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
            x, y + height, cx, cy, bottomName);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements.SetName(PdfExtGState.Keys.BM, pdfBlendModeName);
        var gsName = Resources.AddExtGState(extGState);

        double tcx = width / top.PointWidth;
        double tcy = height / top.PointHeight;

        if (tcx == 0 || tcy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, tcx, tcy, topName);
    }

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

    /// <summary>
    /// Gets the resource name of the specified font within this page or form.
    /// </summary>
    internal string GetFontName(TypefaceFont font, out PdfFont pdfFont)
    {
        if (_page is not null)
            return _page.GetFontName(font, out pdfFont);
        return _form.GetFontName(font, out pdfFont);
    }

    /// <summary>
    /// Gets the resource name of the specified image within this page or form.
    /// </summary>
    internal string GetImageName(PdfRasterImage image, double width, double height)
    {
        if (_page is not null)
            return _page.GetImageName(image, width, height);
        return _form.GetImageName(image, width, height);
    }

    /// <summary>
    /// Gets the resource name of the specified form within this page or form.
    /// </summary>
    internal string GetFormName(PdfForm form)
    {
        if (_page is not null)
            return _page.GetFormName(form);
        return _form.GetFormName(form);
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
