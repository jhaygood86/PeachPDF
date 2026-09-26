/****************************************************************************
 *
 * psfont.c
 *
 *   Adobe's code for font instances (body).
 *
 * Copyright 2007-2014 Adobe Systems Incorporated.
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
 * psfont.h
 *
 *   Adobe's code for font instances (specification).
 *
 * Copyright 2007-2013 Adobe Systems Incorporated.
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psfont.c, psfont.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>
/// The state of Adobe's engine for one glyph load (<c>CF2_Font</c>): the transform, the blue zones and the darkening amounts computed
/// for the font dictionary and size the glyph is in.
/// </summary>
/// <remarks>
/// FreeType keeps one of these for a size and recomputes the parts that depend on the transform or the font dictionary only when they
/// change. Here one is made for each glyph, which gives the same values (they depend on nothing else) and no state that one glyph leaves
/// for the next.
/// </remarks>
internal sealed class Cf2Font
{
    /// <summary>Apply hints to rendered glyphs (<c>CF2_FlagsHinted</c>).</summary>
    public const int FlagsHinted = 1;

    /// <summary>Stem darkening (<c>CF2_FlagsDarkened</c>).</summary>
    public const int FlagsDarkened = 2;

    /// <summary>
    /// The instruction limit of a charstring. FreeType's is 20,000,000 (which matches Avalon); a glyph of a real font runs a few thousand
    /// instructions at most, and a hostile font can make every glyph run to the limit, so the port's is a tenth of it.
    /// </summary>
    public const uint InstructionLimit = 2000000U;

    /// <summary>The default darkening parameters of FreeType's CFF driver: (x1, y1, x2, y2, x3, y3, x4, y4) in 1000 unit character space.</summary>
    public static readonly int[] DefaultDarkenParams = [500, 400, 1000, 275, 1667, 275, 2333, 0];

    /// <summary>The shared error of this instance.</summary>
    public readonly Cf2Error Error = new();

    public int RenderingFlags;

    // variables that depend on Transform: the following have zero translation; inner * outer = font * original

    /// <summary>The original client matrix.</summary>
    public Cf2Matrix CurrentTransform;

    /// <summary>For hinting; erect, scaled.</summary>
    public Cf2Matrix InnerTransform;

    /// <summary>Post hinting; includes rotations.</summary>
    public Cf2Matrix OuterTransform;

    /// <summary>Transform-dependent.</summary>
    public int Ppem;

    public int UnitsPerEm;

    /// <summary>The synthetic emboldening, in character space units (not used).</summary>
    public int SyntheticEmboldeningAmountX, SyntheticEmboldeningAmountY;

    public Cf2Decoder Decoder = null!;
    public Cf2Outline Outline = null!;

    // these flags can vary from one call to the next
    public bool Hinted;

    /// <summary>True if stemDarkened or synthetic bold, i.e., darkenX != 0 || darkenY != 0.</summary>
    public bool Darkened;

    public bool StemDarkened;

    /// <summary>In 1000 unit character space.</summary>
    public int[] DarkenParams = (int[])DefaultDarkenParams.Clone();

    // variables that depend on both FontDict and Transform

    /// <summary>In character space; depends on dict entry.</summary>
    public int StdVW;

    /// <summary>In character space; depends on dict entry.</summary>
    public int StdHW;

    /// <summary>Character space units.</summary>
    public int DarkenX;

    /// <summary>Depends on transform and private dict (StdVW).</summary>
    public int DarkenY;

    /// <summary>Darken assuming counterclockwise winding.</summary>
    public bool ReverseWinding;

    /// <summary>Computed zone data.</summary>
    public Cf2Blues Blues = new();

    // Compute a stem darkening amount in character space.
    private static void ComputeDarkening(int emRatio, int ppem, int stemWidth, out int darkenAmount, int boldenAmount, bool stemDarkened, int[] darkenParams)
    {
        // Total darkening amount is computed in 1000 unit character space using the modified 5 part curve as Adobe's Avalon
        // rasterizer.  The darkening amount is smaller for thicker stems.  It becomes zero when the stem is thicker than 2.333 pixels.
        //
        // By default, we use
        //
        //   darkenAmount = 0.4 pixels   if scaledStem <= 0.5 pixels,
        //   darkenAmount = 0.275 pixels if 1 <= scaledStem <= 1.667 pixels,
        //   darkenAmount = 0 pixel      if scaledStem >= 2.333 pixels,
        //
        // and piecewise linear in-between.  This corresponds to the following values for the `darkening-parameters' property:
        //
        //   (x1, y1) = (500, 400)
        //   (x2, y2) = (1000, 275)
        //   (x3, y3) = (1667, 275)
        //   (x4, y4) = (2333, 0)

        // Internal calculations are done in units per thousand for convenience. The x axis is scaled stem width in thousandths of a
        // pixel. That is, 1000 is 1 pixel.  The y axis is darkening amount in thousandths of a pixel.  In the code, below, dividing
        // by ppem and adjusting for emRatio converts darkenAmount to character space (font units).
        int stemWidthPer1000, scaledStem;
        int logBase2;

        darkenAmount = 0;

        if (boldenAmount == 0 && !stemDarkened)
            return;

        // protect against range problems and divide by zero
        if (emRatio < Cf2Fixed.FromDouble(.01))
            return;

        if (stemDarkened)
        {
            int x1 = darkenParams[0];
            int y1 = darkenParams[1];
            int x2 = darkenParams[2];
            int y2 = darkenParams[3];
            int x3 = darkenParams[4];
            int y3 = darkenParams[5];
            int x4 = darkenParams[6];
            int y4 = darkenParams[7];

            // convert from true character space to 1000 unit character space; add synthetic emboldening effect

            // `stemWidthPer1000' will not overflow for a legitimate font
            stemWidthPer1000 = FtCalc.MulFix(unchecked(stemWidth + boldenAmount), emRatio);

            // `scaledStem' can easily overflow, so we must clamp its maximum value; the test doesn't need to be precise, but must be
            // conservative.  The clamp value (default 2333) where `darkenAmount' is zero is well below the overflow value of 32767.
            //
            // FT_MSB computes the integer part of the base 2 logarithm.  The number of bits for the product is 1 or 2 more than the sum
            // of logarithms; remembering that the 16 lowest bits of the fraction are dropped this is correct to within a factor of
            // almost 4.  For example, 0x80.0000 * 0x80.0000 = 0x4000.0000 is 23+23 and is flagged as possible overflow because
            // 0xFF.FFFF * 0xFF.FFFF = 0xFFFF.FE00 is also 23+23.
            logBase2 = FtCalc.Msb((uint)stemWidthPer1000) + FtCalc.Msb((uint)ppem);

            if (logBase2 >= 46)
            {
                // possible overflow
                scaledStem = Cf2Fixed.FromInt(x4);
            }
            else
            {
                scaledStem = FtCalc.MulFix(stemWidthPer1000, ppem);
            }

            // now apply the darkening parameters
            if (scaledStem < Cf2Fixed.FromInt(x1))
            {
                darkenAmount = FtCalc.DivFix(Cf2Fixed.FromInt(y1), ppem);
            }
            else
            {
                int stage = 0;

                if (scaledStem < Cf2Fixed.FromInt(x2))
                    stage = 1;
                else if (scaledStem < Cf2Fixed.FromInt(x3))
                    stage = 2;
                else if (scaledStem < Cf2Fixed.FromInt(x4))
                    stage = 3;
                else
                    stage = 4;

                // the branches of the C code fall through to the next one when a difference of the parameters is zero
                if (stage == 1)
                {
                    int xdelta = x2 - x1;
                    int ydelta = y2 - y1;
                    int x = unchecked(stemWidthPer1000 - FtCalc.DivFix(Cf2Fixed.FromInt(x1), ppem));

                    if (xdelta == 0)
                        stage = 2;
                    else
                        darkenAmount = unchecked(FtCalc.MulDiv(x, ydelta, xdelta) + FtCalc.DivFix(Cf2Fixed.FromInt(y1), ppem));
                }

                if (stage == 2)
                {
                    int xdelta = x3 - x2;
                    int ydelta = y3 - y2;
                    int x = unchecked(stemWidthPer1000 - FtCalc.DivFix(Cf2Fixed.FromInt(x2), ppem));

                    if (xdelta == 0)
                        stage = 3;
                    else
                        darkenAmount = unchecked(FtCalc.MulDiv(x, ydelta, xdelta) + FtCalc.DivFix(Cf2Fixed.FromInt(y2), ppem));
                }

                if (stage == 3)
                {
                    int xdelta = x4 - x3;
                    int ydelta = y4 - y3;
                    int x = unchecked(stemWidthPer1000 - FtCalc.DivFix(Cf2Fixed.FromInt(x3), ppem));

                    if (xdelta == 0)
                        stage = 4;
                    else
                        darkenAmount = unchecked(FtCalc.MulDiv(x, ydelta, xdelta) + FtCalc.DivFix(Cf2Fixed.FromInt(y3), ppem));
                }

                if (stage == 4)
                    darkenAmount = FtCalc.DivFix(Cf2Fixed.FromInt(y4), ppem);
            }

            // use half the amount on each side and convert back to true character space
            darkenAmount = FtCalc.DivFix(darkenAmount, unchecked(2 * emRatio));
        }

        // add synthetic emboldening effect in character space
        darkenAmount = unchecked(darkenAmount + boldenAmount / 2);
    }

    // Sets up values for the current FontDict and matrix; called for each glyph to be rendered.  The caller's transform is adjusted
    // for subpixel positioning.
    private void Setup(in Cf2Matrix transform)
    {
        // character space units
        int boldenX = SyntheticEmboldeningAmountX;
        int boldenY = SyntheticEmboldeningAmountY;

        // clear previous error
        Error.Value = 0;

        CffSubFont subFont = Decoder.CurrentSubfont;

        // recompute cached data: nothing is cached here, so all of it is computed
        int ppem = Cf2Fixed.FromInt(Decoder.PpemY);
        Ppem = ppem;

        // copy hinted flag on each call
        Hinted = (RenderingFlags & FlagsHinted) != 0;

        // determine if transform has changed; include Fontmatrix but ignore translation
        CurrentTransform = transform;
        CurrentTransform.Tx = CurrentTransform.Ty = Cf2Fixed.FromInt(0);

        // TODO (in FreeType): FreeType transform is simple scalar; for now, use identity for outer
        InnerTransform = transform;
        OuterTransform.A = OuterTransform.D = Cf2Fixed.FromInt(1);
        OuterTransform.B = OuterTransform.C = Cf2Fixed.FromInt(0);

        // Font->darkened is set to true if there is a stem darkening request or the font is synthetic emboldened.  font->darkened
        // controls whether to adjust blue zones, winding order, and hinting.
        StemDarkened = (RenderingFlags & FlagsDarkened) != 0;

        // recompute variables that are dependent on transform or FontDict or darken flag

        // StdVW is found in the private dictionary; recompute darkening amounts whenever private dictionary or transform change
        // Note: a rendering flag turns darkening on or off, so we want to store the `on' amounts; darkening amount is computed in
        // character space
        // TODO (in FreeType): testing size-dependent darkening here; what to do for rotations?
        int emRatio;
        int stdHW;
        int unitsPerEm = UnitsPerEm;

        if (unitsPerEm == 0)
            unitsPerEm = 1000;

        ppem = Math.Max(Cf2Fixed.FromInt(4), Ppem); // use minimum ppem of 4

        // Freetype does not preserve the fontMatrix when parsing; use unitsPerEm instead.
        emRatio = Cf2Fixed.FromInt(1000) / unitsPerEm;
        StdVW = Cf2Fixed.FromInt(subFont.Private.StandardHeight);

        if (StdVW <= 0)
            StdVW = FtCalc.DivFix(Cf2Fixed.FromInt(75), emRatio);

        if (boldenX > 0)
        {
            // Ensure that boldenX is at least 1 pixel for synthetic bold font (similar to what Avalon does)
            boldenX = Math.Max(boldenX, FtCalc.DivFix(Cf2Fixed.FromInt(unitsPerEm), ppem));

            // Synthetic emboldening adds at least 1 pixel to darkenX, while stem darkening adds at most half pixel.  Since the purpose
            // of stem darkening (readability at small sizes) is met with synthetic emboldening, no need to add stem darkening for a
            // synthetic bold font.
            ComputeDarkening(emRatio, ppem, StdVW, out DarkenX, boldenX, false, DarkenParams);
        }
        else
        {
            ComputeDarkening(emRatio, ppem, StdVW, out DarkenX, 0, StemDarkened, DarkenParams);
        }

        // set the default stem width, because it must be the same for all family members; choose a constant for StdHW that depends
        // on font contrast
        stdHW = Cf2Fixed.FromInt(subFont.Private.StandardWidth);

        if (stdHW > 0 && StdVW > unchecked(2 * stdHW))
        {
            StdHW = FtCalc.DivFix(Cf2Fixed.FromInt(75), emRatio);
        }
        else
        {
            // low contrast font gets less hstem darkening
            StdHW = FtCalc.DivFix(Cf2Fixed.FromInt(110), emRatio);
        }

        ComputeDarkening(emRatio, ppem, StdHW, out DarkenY, boldenY, StemDarkened, DarkenParams);

        Darkened = DarkenX != 0 || DarkenY != 0;

        ReverseWinding = false; // initial expectation is CCW

        // compute blue zones for this instance
        Blues = new Cf2Blues();
        Blues.Init(this, subFont.Private);
    }

    /// <summary>
    /// Builds the outline of a glyph (<c>cf2_getGlyphOutline</c>, equivalent to AdobeGetOutline): the charstring is run, again with the
    /// darkening reversed if the winding is not the direction CFF uses.
    /// </summary>
    /// <returns>The FreeType error, or zero.</returns>
    public int GetGlyphOutline(Cf2Buffer charstring, in Cf2Matrix transform, out int glyphWidth)
    {
        int advWidth = 0;
        bool needWinding;

        // Note: use both integer and fraction for outlines.  This allows bbox to come out directly.
        FtVector translation;
        translation.X = transform.Tx;
        translation.Y = transform.Ty;

        // set up values based on transform
        Setup(transform);
        if (Error.Value != 0)
            goto Exit; // setup encountered an error

        // reset darken direction
        ReverseWinding = false;

        // winding order only affects darkening
        needWinding = Darkened;

        while (true)
        {
            // reset output buffer
            Outline.Reset();

            // build the outline, passing the full translation
            Cf2Interpreter.Interpret(this, charstring, Outline, translation, false, 0, 0, out advWidth);

            if (Error.Value != 0)
                goto Exit;

            if (!needWinding)
                break;

            // check winding order
            if (Outline.WindingMomentum >= 0) // CFF is CCW
                break;

            // invert darkening and render again
            // TODO (in FreeType): this should be a parameter to getOutline-computeOffset
            ReverseWinding = true;

            needWinding = false; // exit after next iteration
        }

        // finish storing client outline
        Outline.Close();

    Exit:
        // FreeType just wants the advance width; there is no translation
        glyphWidth = advWidth;

        return Error.Value;
    }
}
