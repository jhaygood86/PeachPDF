/****************************************************************************
 *
 * psft.c
 *
 *   FreeType Glue Component to Adobe's Interpreter (body).
 *
 * Copyright 2013-2014 Adobe Systems Incorporated.
 *
 * This software, and all works of authorship, whether in source or
 * object code form as indicated by the copyright notice(s) included
 * herein (collectively, the "Work") is made available, and may only be
 * used, modified, and distributed under the FreeType Project License,
 * LICENSE.TXT.  Additionally, subject to the terms and conditions of the
 * FreeType Project License, each contributor to the Work hereby grants
 * to any individual or legal entity exercising permissions granted by
 * the FreeType Project License and this section (hereafter, "You" or
 * "Your") a perpetual, worldwide, non-exclusive, no-charge,
 * royalty-free, irrevocable (except as stated in this section) patent
 * license to make, have made, use, offer to sell, sell, import, and
 * otherwise transfer the Work, where such license applies only to those
 * patent claims licensable by such contributor that are necessarily
 * infringed by their contribution(s) alone or by combination of their
 * contribution(s) with the Work to which such contribution(s) was
 * submitted.  If You institute patent litigation against any entity
 * (including a cross-claim or counterclaim in a lawsuit) alleging that
 * the Work or a contribution incorporated within the Work constitutes
 * direct or contributory patent infringement, then any patent licenses
 * granted to You under this License for that Work shall terminate as of
 * the date such litigation is filed.
 *
 * By using, modifying, or distributing the Work you indicate that you
 * have read and understood the terms and conditions of the
 * FreeType Project License as well as those provided in this section,
 * and you accept them fully.
 *
 */

/****************************************************************************
 *
 * psft.h
 *
 *   FreeType Glue Component to Adobe's Interpreter (specification).
 *
 * Copyright 2013 Adobe Systems Incorporated.
 *
 * This software, and all works of authorship, whether in source or
 * object code form as indicated by the copyright notice(s) included
 * herein (collectively, the "Work") is made available, and may only be
 * used, modified, and distributed under the FreeType Project License,
 * LICENSE.TXT.  Additionally, subject to the terms and conditions of the
 * FreeType Project License, each contributor to the Work hereby grants
 * to any individual or legal entity exercising permissions granted by
 * the FreeType Project License and this section (hereafter, "You" or
 * "Your") a perpetual, worldwide, non-exclusive, no-charge,
 * royalty-free, irrevocable (except as stated in this section) patent
 * license to make, have made, use, offer to sell, sell, import, and
 * otherwise transfer the Work, where such license applies only to those
 * patent claims licensable by such contributor that are necessarily
 * infringed by their contribution(s) alone or by combination of their
 * contribution(s) with the Work to which such contribution(s) was
 * submitted.  If You institute patent litigation against any entity
 * (including a cross-claim or counterclaim in a lawsuit) alleging that
 * the Work or a contribution incorporated within the Work constitutes
 * direct or contributory patent infringement, then any patent licenses
 * granted to You under this License for that Work shall terminate as of
 * the date such litigation is filed.
 *
 * By using, modifying, or distributing the Work you indicate that you
 * have read and understood the terms and conditions of the
 * FreeType Project License as well as those provided in this section,
 * and you accept them fully.
 *
 */

/****************************************************************************
 *
 * psobjs.c
 *
 *   Auxiliary functions for PostScript fonts (body).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psft.c, psft.h, psobjs.c.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The glue between a CFF font and Adobe's engine (<c>PS_Decoder</c> and the accessors of <c>psft.c</c>): what one glyph load knows about
/// the font, the size and the subroutines it may call.
/// </summary>
internal sealed class Cf2Decoder
{
    /// <summary>The maximum ppem Adobe's engine takes (<c>CF2_MAX_SIZE</c>).</summary>
    private static readonly int MaxSize = Cf2Fixed.FromInt(2000);

    public required CffFont Cff { get; init; }

    /// <summary>The Font DICT or Top DICT (with its Private DICT and local subroutines) the glyph is in.</summary>
    public required CffSubFont CurrentSubfont { get; init; }

    public required int UnitsPerEm { get; init; }

    /// <summary>The size's scale, from <c>FT_Request_Metrics</c>: 26.6 pixels per unit, in 16.16.</summary>
    public required int XScale { get; init; }

    public required int YScale { get; init; }

    /// <summary>The vertical ppem of the size (<c>y_ppem</c>).</summary>
    public required int PpemY { get; init; }

    /// <summary>Whether the stem darkening of the engine is on (off in FreeType by default).</summary>
    public bool StemDarkening { get; init; }

    /// <summary>The width of the glyph the charstring gave, in font units.</summary>
    public int GlyphWidth { get; private set; }

    private int _numGlobals;
    private int _globalsBias;
    private int _numLocals;
    private int _localsBias;

    /// <summary>The bias and counts of the subroutines (<c>cff_decoder_init</c> and <c>cff_decoder_prepare</c>).</summary>
    public void Prepare()
    {
        _numGlobals = Cff.NumGlobalSubrs;
        _globalsBias = CffFont.ComputeBias(Cff.TopFont.FontDict.CharstringType, _numGlobals);

        _numLocals = CurrentSubfont.NumLocalSubrs;
        _localsBias = CffFont.ComputeBias(Cff.TopFont.FontDict.CharstringType, _numLocals);

        GlyphWidth = CurrentSubfont.Private.DefaultWidth;
    }

    /// <summary>Converts an unbiased global subroutine index to a buffer; returns true on error (<c>cf2_initGlobalRegionBuffer</c>).</summary>
    public bool InitGlobalRegionBuffer(int subrNum, Cf2Buffer buf)
    {
        buf.Set([], 0, 0);

        uint idx = unchecked((uint)(subrNum + _globalsBias));
        if (idx >= (uint)_numGlobals)
            return true; // error

        buf.Set(Cff.Data, Cff.GlobalSubrs[idx], Cff.GlobalSubrs[idx + 1]);

        return false; // success
    }

    /// <summary>Converts an unbiased local subroutine index to a buffer; returns true on error (<c>cf2_initLocalRegionBuffer</c>).</summary>
    public bool InitLocalRegionBuffer(int subrNum, Cf2Buffer buf)
    {
        buf.Set([], 0, 0);

        uint idx = unchecked((uint)(subrNum + _localsBias));
        if (idx >= (uint)_numLocals)
            return true; // error

        buf.Set(Cff.Data, CurrentSubfont.LocalSubrs[idx], CurrentSubfont.LocalSubrs[idx + 1]);

        return false; // success
    }

    /// <summary>Converts an Adobe standard encoding code to a charstring buffer; used for the components of a seac accented glyph (<c>cf2_getSeacComponent</c>).</summary>
    /// <returns>The FreeType error, or zero.</returns>
    public int GetSeacComponent(int code, Cf2Buffer buf)
    {
        buf.Set([], 0, 0);

        int gid = Cff.LookupGlyphByStdCharCode(code);
        if (gid < 0)
            return Cf2Error.InvalidGlyphFormat;

        if (!Cff.CharStrings.TryGetElement(gid, out int position, out int length))
            return Cf2Error.InvalidArgument;

        buf.Set(Cff.Data, position, position + length);
        return 0;
    }

    /// <summary>
    /// Runs the charstring of a glyph, giving the points to <paramref name="outline"/> (<c>cf2_decoder_parse_charstrings</c>).
    /// </summary>
    /// <returns>The FreeType error, or zero.</returns>
    public int ParseCharstrings(Cf2Outline outline, int position, int length, bool hinted)
    {
        var font = new Cf2Font { Decoder = this, Outline = outline };

        outline.Decoder = this;
        outline.ErrorSink = font.Error;

        var buf = new Cf2Buffer();
        buf.Set(Cff.Data, position, position + length);

        var transform = default(Cf2Matrix);

        // get scaling and hint flag from GlyphSlot
        if (hinted)
        {
            // note: FreeType scale includes a factor of 64
            transform.A = unchecked(XScale + 32) / 64;
            transform.D = unchecked(YScale + 32) / 64;
        }
        else
        {
            // for unhinted outlines, `cff_slot_load' does the scaling, thus render at `unity' scale
            transform.A = 0x0400; // 1/64 as 16.16
            transform.D = 0x0400;
        }

        font.RenderingFlags = 0;
        if (hinted)
            font.RenderingFlags |= Cf2Font.FlagsHinted;

        if (StemDarkening)
            font.RenderingFlags |= Cf2Font.FlagsDarkened;

        // now get an outline for this glyph; also get units per em to validate scale
        font.UnitsPerEm = UnitsPerEm;

        // This check should avoid most internal overflow cases.  Clients should generally respond to `Glyph_Too_Big' by getting a
        // glyph outline at EM size, scaling it and filling it as a graphics operation.
        int checkError = CheckTransform(transform, font.UnitsPerEm);
        if (checkError != 0)
            return checkError;

        int error = font.GetGlyphOutline(buf, transform, out int glyphWidth);
        if (error != 0)
            return Cf2Error.InvalidGlyphFormat; // FT_ERR( Invalid_File_Format )

        // cf2_setGlyphWidth
        GlyphWidth = Cf2Fixed.ToInt(glyphWidth);

        return 0;
    }

    private static int CheckTransform(in Cf2Matrix transform, int unitsPerEm)
    {
        if (transform.A <= 0 || transform.D <= 0)
            return Cf2Error.InvalidSizeHandle;

        if (unitsPerEm > 0x7FFF)
            return Cf2Error.GlyphTooBig;

        int maxScale = FtCalc.DivFix(MaxSize, Cf2Fixed.FromInt(unitsPerEm));

        if (transform.A > maxScale || transform.D > maxScale)
            return Cf2Error.GlyphTooBig;

        return 0;
    }
}

/// <summary>
/// The outline Adobe's engine builds, as FreeType's glyph loader holds it (<c>PS_Builder</c> and the outline callbacks of
/// <c>psft.c</c>): points in 26.6 pixels with on-curve and cubic control tags, and the last point of each contour.
/// </summary>
internal sealed class Cf2Outline : Cf2OutlineCallbacks
{
    /// <summary>The most points or contours of an outline (<c>FT_OUTLINE_POINTS_MAX</c>).</summary>
    private const int OutlinePointsMax = 0x7FFF;

    /// <summary>The point tag of an on-curve point.</summary>
    public const byte TagOn = 1;

    /// <summary>The point tag of a control point of a cubic curve.</summary>
    public const byte TagCubic = 2;

    public Cf2Decoder Decoder = null!;

    public int[] X = new int[64];
    public int[] Y = new int[64];
    public byte[] Tags = new byte[64];
    public int NPoints;

    public int[] Contours = new int[8];
    public int NContours;

    private bool _pathBegun;

    /// <summary>Where the first error the outline callbacks meet is recorded: the font's shared error.</summary>
    public Cf2Error ErrorSink = new();

    /// <summary><c>cf2_outline_reset</c>: starts the outline again, for another run of the charstring.</summary>
    public void Reset()
    {
        WindingMomentum = 0;

        NPoints = 0;
        NContours = 0;
    }

    /// <summary><c>cf2_outline_close</c>.</summary>
    public void Close() => CloseContour();

    public override void MoveTo(in Cf2CallbackParams p)
    {
        // note: two successive moves simply close the contour twice
        CloseContour();
        _pathBegun = false;
    }

    public override void LineTo(in Cf2CallbackParams p)
    {
        if (!_pathBegun)
        {
            // record the move before the line; also check points and set `path_begun'
            int error = StartPoint(p.Pt0.X, p.Pt0.Y);
            if (error != 0)
            {
                ErrorSink.Set(error);

                return;
            }
        }

        // `ps_builder_add_point1' includes a check_points call for one point
        int error1 = AddPoint1(p.Pt1.X, p.Pt1.Y);
        if (error1 != 0)
            ErrorSink.Set(error1);
    }

    public override void CubeTo(in Cf2CallbackParams p)
    {
        if (!_pathBegun)
        {
            // record the move before the line; also check points and set `path_begun'
            int error = StartPoint(p.Pt0.X, p.Pt0.Y);
            if (error != 0)
            {
                ErrorSink.Set(error);

                return;
            }
        }

        // prepare room for 3 points: 2 off-curve, 1 on-curve
        int errorPoints = CheckPoints(3);
        if (errorPoints != 0)
        {
            ErrorSink.Set(errorPoints);
            return;
        }

        AddPoint(p.Pt1.X, p.Pt1.Y, false);
        AddPoint(p.Pt2.X, p.Pt2.Y, false);
        AddPoint(p.Pt3.X, p.Pt3.Y, true);
    }

    // FT_GLYPHLOADER_CHECK_POINTS( loader, count, 0 )
    private int CheckPoints(int count)
    {
        int newMax = NPoints + count;
        if (newMax > OutlinePointsMax)
            return Cf2Error.InvalidArgument;

        if (newMax > X.Length)
        {
            int size = Math.Max(newMax, X.Length + (X.Length >> 1));
            Array.Resize(ref X, size);
            Array.Resize(ref Y, size);
            Array.Resize(ref Tags, size);
        }

        return 0;
    }

    // add a new point, do not check space (ps_builder_add_point)
    private void AddPoint(int x, int y, bool on)
    {
        // cf2_decoder_parse_charstrings uses 16.16 coordinates
        X[NPoints] = x >> 10;
        Y[NPoints] = y >> 10;
        Tags[NPoints] = on ? TagOn : TagCubic;

        NPoints++;
    }

    // check space for a new on-curve point, then add it (ps_builder_add_point1)
    private int AddPoint1(int x, int y)
    {
        int error = CheckPoints(1);
        if (error == 0)
            AddPoint(x, y, true);

        return error;
    }

    // check space for a new contour, then add it (ps_builder_add_contour)
    private int AddContour()
    {
        // FT_GLYPHLOADER_CHECK_POINTS( loader, 0, 1 )
        if (NContours + 1 > OutlinePointsMax)
            return Cf2Error.InvalidArgument;

        if (NContours + 1 > Contours.Length)
            Array.Resize(ref Contours, Math.Max(NContours + 1, Contours.Length + (Contours.Length >> 1)));

        if (NContours > 0)
            Contours[NContours - 1] = NPoints - 1;

        NContours++;

        return 0;
    }

    // if a path was begun, add its first on-curve point (ps_builder_start_point)
    private int StartPoint(int x, int y)
    {
        int error = 0;

        // test whether we are building a new contour
        if (!_pathBegun)
        {
            _pathBegun = true;
            error = AddContour();
            if (error == 0)
                error = AddPoint1(x, y);
        }

        return error;
    }

    // close the current contour (ps_builder_close_contour)
    private void CloseContour()
    {
        int first = NContours <= 1 ? 0 : Contours[NContours - 2] + 1;

        // in malformed fonts it can happen that a contour was started but no points were added
        if (NContours != 0 && first == NPoints)
        {
            NContours--;
            return;
        }

        // We must not include the last point in the path if it is located on the first point.
        if (NPoints > 1)
        {
            int p1 = first;
            int p2 = NPoints - 1;

            // `delete' last point only if it coincides with the first point and it is not a control point (which can happen).
            if (X[p1] == X[p2] && Y[p1] == Y[p2])
            {
                if (Tags[p2] == TagOn)
                    NPoints--;
            }
        }

        if (NContours > 0)
        {
            // Don't add contours only consisting of one point, i.e., check whether the first and the last point is the same.
            if (first == NPoints - 1)
            {
                NContours--;
                NPoints--;
            }
            else
            {
                Contours[NContours - 1] = NPoints - 1;
            }
        }
    }
}
