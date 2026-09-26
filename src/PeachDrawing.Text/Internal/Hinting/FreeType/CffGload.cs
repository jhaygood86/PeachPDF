/****************************************************************************
 *
 * cffgload.c
 *
 *   OpenType Glyph Loader (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

/****************************************************************************
 *
 * cffobjs.c
 *
 *   OpenType objects manager (body).
 *
 * Copyright (C) 1996-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

/****************************************************************************
 *
 * cffdecode.c
 *
 *   PostScript CFF (Type 2) decoding routines (body).
 *
 * Copyright (C) 2017-2026 by
 * David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * This file is part of the FreeType project, and may only be used,
 * modified, and distributed under the terms of the FreeType project
 * license, LICENSE.TXT.  By continuing to use, modify, or distribute
 * this file you indicate that you have read the license and
 * understand and accept it fully.
 *
 */

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): cffgload.c, cffobjs.c, cffdecode.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using PeachDrawing.Text.Internal.Fonts.OpenType;
using System;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// What hinting reads of an OpenType font with CFF outlines (<c>CFF_Face</c>): the CFF table, read once, and the units per em and
/// advance widths of the font's SFNT tables. Immutable after construction, so one instance serves any number of threads.
/// </summary>
internal sealed class CffFace
{
    public CffFont Font { get; }

    /// <summary>The units per em of the <c>head</c> table.</summary>
    public int UnitsPerEm { get; }

    /// <summary>The advance width of a glyph in font units (from <c>hmtx</c>).</summary>
    public Func<int, int> Advance { get; }

    private CffFace(CffFont font, int unitsPerEm, Func<int, int> advance)
    {
        Font = font;
        UnitsPerEm = unitsPerEm;
        Advance = advance;
    }

    /// <summary>Reads the CFF table of a font, or returns null for a font that has none (or one that FreeType would refuse).</summary>
    public static CffFace? TryCreate(OpenTypeFontface font, Func<int, int> advance)
    {
        var tables = font.TableDictionary;
        if (!tables.TryGetValue("CFF ", out var cff) || !tables.TryGetValue("head", out var head) || !tables.ContainsKey("hmtx"))
            return null;

        byte[] data = font.FontSource.Bytes;
        if (head.Offset < 0 || head.Length < 54 || (long)head.Offset + head.Length > data.Length)
            return null;

        int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(head.Offset + 18));
        if (unitsPerEm == 0)
            return null;

        try
        {
            return new CffFace(CffFont.Load(data, cff.Offset, unitsPerEm), unitsPerEm, advance);
        }
        catch (HintingException)
        {
            return null;
        }
    }
}

/// <summary>
/// A CFF face at one size (<c>CFF_Size</c> after <c>cff_size_request</c>): the scale FreeType computes for the request. Immutable.
/// </summary>
internal sealed class CffSize
{
    public CffFace Face { get; }

    /// <summary>The size in 26.6 pixels per em.</summary>
    public int Ppem26Dot6 { get; }

    /// <summary>26.6 pixels per unit, in 16.16 (<c>metrics.x_scale</c>, which equals <c>y_scale</c>).</summary>
    public int Scale { get; }

    /// <summary>The size in whole pixels per em (<c>metrics.y_ppem</c>).</summary>
    public int Ppem { get; }

    /// <summary>Whether the engine's stem darkening is on (off in FreeType by default; the tests turn it on).</summary>
    public bool StemDarkening { get; }

    /// <summary>The size a request makes (<c>FT_Request_Metrics</c> for a nominal size).</summary>
    /// <exception cref="HintingException">The size is not one a font is scaled to.</exception>
    public CffSize(CffFace face, int ppem26Dot6, bool stemDarkening = false)
    {
        Face = face;
        Ppem26Dot6 = ppem26Dot6;
        StemDarkening = stemDarkening;

        if (ppem26Dot6 <= 0)
            throw new HintingException("The size is not positive.");

        long ppem = ((long)ppem26Dot6 + 32) >> 6;
        if (ppem > 0xFFFF)
            throw new HintingException("The size is too large.");

        // FreeType loads a glyph at a size whose ppem is zero without scaling and hinting
        if (ppem == 0)
            throw new HintingException("The size rounds to zero pixels per em.");

        Scale = FtCalc.DivFix(ppem26Dot6, face.UnitsPerEm);
        Ppem = (int)ppem;
    }
}

/// <summary>A CFF glyph loaded and hinted (the outline <c>cff_slot_load</c> leaves in a glyph slot, and its advance).</summary>
internal sealed class CffHintedGlyph
{
    /// <summary>The point coordinates in 26.6 pixels, y up, with the glyph origin at (0, 0).</summary>
    public int[] X { get; init; } = [];

    public int[] Y { get; init; } = [];

    /// <summary>The point tags: <see cref="Cf2Outline.TagOn"/> or <see cref="Cf2Outline.TagCubic"/> (a control point of a cubic curve).</summary>
    public byte[] Tags { get; init; } = [];

    /// <summary>The index of the last point of each contour.</summary>
    public int[] ContourEnds { get; init; } = [];

    public int NPoints { get; init; }

    /// <summary>The advance width in 26.6 pixels, rounded to a whole pixel as FreeType reports it for a hinted glyph.</summary>
    public int Advance { get; init; }
}

/// <summary>The CFF glyph loader (<c>cff_slot_load</c> for a hinted glyph): runs Adobe's engine on a glyph, then places and scales the result.</summary>
internal static class CffGlyphLoader
{
    /// <summary>Loads and hints a glyph at a size.</summary>
    /// <exception cref="HintingException">The glyph cannot be loaded: its data is malformed, or a limit is reached.</exception>
    public static CffHintedGlyph Load(CffSize size, int glyphIndex)
    {
        CffFace face = size.Face;
        CffFont cff = face.Font;

        // glyph_index >= cff->num_glyphs
        if ((uint)glyphIndex >= (uint)cff.NumGlyphs)
            throw new HintingException("The glyph index is invalid.");

        // hinted, and scaled
        int xScale = size.Scale;
        int yScale = size.Scale;

        CffFontDict fontDict = cff.TopFont.FontDict;
        int matrixXx, matrixXy, matrixYx, matrixYy;
        int offsetX, offsetY;
        CffSubFont sub = cff.TopFont;

        // if we have a CID subfont, use its matrix (which has already been multiplied with the root matrix)
        if (cff.SubFonts.Length > 0)
        {
            int fdIndex = cff.FdSelectGet(glyphIndex);
            if (fdIndex >= cff.SubFonts.Length)
                fdIndex = cff.SubFonts.Length - 1;

            int topUpm = (int)fontDict.UnitsPerEm;
            int subUpm = (int)cff.SubFonts[fdIndex].FontDict.UnitsPerEm;

            CffFontDict subDict = cff.SubFonts[fdIndex].FontDict;
            matrixXx = subDict.MatrixXx;
            matrixXy = subDict.MatrixXy;
            matrixYx = subDict.MatrixYx;
            matrixYy = subDict.MatrixYy;
            offsetX = subDict.OffsetX;
            offsetY = subDict.OffsetY;

            if (topUpm != subUpm)
            {
                xScale = FtCalc.MulDiv(xScale, topUpm, subUpm);
                yScale = FtCalc.MulDiv(yScale, topUpm, subUpm);
            }
        }
        else
        {
            matrixXx = fontDict.MatrixXx;
            matrixXy = fontDict.MatrixXy;
            matrixYx = fontDict.MatrixYx;
            matrixYy = fontDict.MatrixYy;
            offsetX = fontDict.OffsetX;
            offsetY = fontDict.OffsetY;
        }

        // this function also checks for a valid subfont index (cff_decoder_prepare)
        if (cff.SubFonts.Length > 0)
        {
            int fdIndex = cff.FdSelectGet(glyphIndex);
            if (fdIndex >= cff.SubFonts.Length)
                throw new HintingException("The glyph's Font DICT does not exist.");

            sub = cff.SubFonts[fdIndex];
        }

        var decoder = new Cf2Decoder
        {
            Cff = cff,
            CurrentSubfont = sub,
            UnitsPerEm = face.UnitsPerEm,
            XScale = xScale,
            YScale = yScale,
            PpemY = size.Ppem,
            StemDarkening = size.StemDarkening,
        };
        decoder.Prepare();

        // now load the unscaled outline
        if (!cff.CharStrings.TryGetElement(glyphIndex, out int position, out int length))
            throw new HintingException("The glyph has no charstring.");

        var outline = new Cf2Outline();
        int error = decoder.ParseCharstrings(outline, position, length, hinted: true);
        if (error != 0)
            throw new HintingException("Adobe's engine failed with error " + error + ".");

        // Now, set the metrics -- this is rather simple, as the left side bearing is the xMin, and the top side bearing the yMax.
        int nPoints = outline.NPoints;
        var x = new int[nPoints];
        var y = new int[nPoints];
        Array.Copy(outline.X, x, nPoints);
        Array.Copy(outline.Y, y, nPoints);

        // the advance of the font's hmtx table (a font with a CFF table always has one), in font units
        int horiAdvance = face.Advance(glyphIndex);

        // apply the font matrix, if any
        if (matrixXx != 0x10000 || matrixYy != 0x10000 || matrixXy != 0 || matrixYx != 0)
        {
            for (int i = 0; i < nPoints; i++)
                FtCalc.VectorTransform(ref x[i], ref y[i], matrixXx, matrixXy, matrixYx, matrixYy);

            horiAdvance = FtCalc.MulFix(horiAdvance, matrixXx);
        }

        if (offsetX != 0 || offsetY != 0)
        {
            for (int i = 0; i < nPoints; i++)
            {
                x[i] = unchecked(x[i] + offsetX);
                y[i] = unchecked(y[i] + offsetY);
            }

            horiAdvance = unchecked(horiAdvance + offsetX);
        }

        // scale the metrics; the points of a hinted glyph are in pixels already
        horiAdvance = FtCalc.MulFix(horiAdvance, xScale);

        // ft_glyphslot_grid_fit_metrics: the advance of a hinted glyph is a whole number of pixels
        horiAdvance = FtCalc.PixRound(horiAdvance);

        // FT_Outline_Check: the contours end inside the points, in order, and the last one at the last point
        var ends = new int[outline.NContours];
        if (nPoints > 0 || ends.Length > 0)
        {
            if (nPoints <= 0 || ends.Length <= 0)
                throw new HintingException("The glyph's outline is invalid.");

            int last = -1;
            for (int i = 0; i < ends.Length; i++)
            {
                ends[i] = outline.Contours[i];
                if (ends[i] <= last || ends[i] >= nPoints)
                    throw new HintingException("The glyph's outline is invalid.");

                last = ends[i];
            }

            if (last != nPoints - 1)
                throw new HintingException("The glyph's outline is invalid.");
        }

        var tags = new byte[nPoints];
        Array.Copy(outline.Tags, tags, nPoints);

        return new CffHintedGlyph
        {
            X = x,
            Y = y,
            Tags = tags,
            ContourEnds = ends,
            NPoints = nPoints,
            Advance = horiAdvance,
        };
    }
}
