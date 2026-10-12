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

internal sealed partial class PdfGraphicsRenderer
{
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

        _content.Append(text).Append(" Tj\n");

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
            PdfFont? realizedFont = _gfxState._realizedFont;
            Debug.Assert(realizedFont is not null);
            realizedFont.AddShapedText(s, features, logicalText);

            string? text = null;
            IReadOnlyList<PlacedGlyph>? shapedGlyphs = null;
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
                    _content.Append(pos.X, format2).Append(' ').Append(pos.Y, format2).Append(" Td\n").Append(text).Append(" Tj\n");
                    RememberTextRun(font, shapedGlyphs, x, laidOutEndX, y + verticalOffset);
                }
                else
                {
                    // Italic simulation is done by skewing characters to the right.
                    Matrix3x2 m = PdfMatrix.Create(1, 0, skewSinus, 1, pos.X, pos.Y);
                    _content.Append(m.M11, format2).Append(' ').Append(m.M12, format2).Append(' ').Append(m.M21, format2).Append(' ')
                        .Append(m.M22, format2).Append(' ').Append(m.M31, format2).Append(' ').Append(m.M32, format2)
                        .Append(" Tm\n").Append(text).Append(" Tj\n");
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
                    _content.Append(m.M11, format2).Append(' ').Append(m.M12, format2).Append(' ').Append(m.M21, format2).Append(' ')
                        .Append(m.M22, format2).Append(' ').Append(m.M31, format2).Append(' ').Append(m.M32, format2)
                        .Append(" Tm\n").Append(text).Append(" Tj\n");
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
                    DrawPositionedGlyphs(shapedGlyphs!, font, new PaintPoint(x, y + verticalOffset), laidOutEndX);
                }
                else
                {
                    AdjustTdOffset(ref pos, verticalOffset, null);
                    _content.Append(pos.X, format2).Append(' ').Append(pos.Y, format2).Append(" Td ").Append(text).Append(" Tj\n");
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

            _content.Append(glyphPos.X, format2).Append(' ').Append(glyphPos.Y, format2).Append(" Td ").Append(glyphText).Append(" Tj\n");

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
        PdfFont? realizedFont = _gfxState._realizedFont;
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

            _content.Append(pos.X, format2).Append(' ').Append(pos.Y, format2).Append(" Td ").Append(glyphText).Append(" Tj\n");
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
        PdfFont? realizedFont = _gfxState._realizedFont;
        Debug.Assert(realizedFont is not null);
        realizedFont.AddShapedGlyph(glyph.GlyphIndex, sourceText);

        PaintPoint pos = WorldToView(new PaintPoint(x, y));
        AdjustTdOffset(ref pos, 0, null);

        _content.AppendFormat(CultureInfo.InvariantCulture, "{0:0.####} {1:0.####} Td <", pos.X, pos.Y);
        AppendHexCodeUnit((char)glyph.GlyphIndex);
        _content.Append("> Tj\n");
    }

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
    /// Gets the resource name of the specified font within this page or form.
    /// </summary>
    internal string GetFontName(TypefaceFont font, out PdfFont pdfFont)
    {
        if (_page is not null)
            return _page.GetFontName(font, out pdfFont);
        return _form.GetFontName(font, out pdfFont);
    }
}
