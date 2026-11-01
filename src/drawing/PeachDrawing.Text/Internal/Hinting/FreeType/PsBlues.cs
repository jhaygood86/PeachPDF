/****************************************************************************
 *
 * psblues.c
 *
 *   Adobe's code for handling Blue Zones (body).
 *
 * Copyright 2009-2014 Adobe Systems Incorporated.
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
 * psblues.h
 *
 *   Adobe's code for handling Blue Zones (specification).
 *
 * Copyright 2009-2013 Adobe Systems Incorporated.
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): psblues.c, psblues.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>An edge of a hint (<c>CF2_HintRec</c>): where it is in character space and, once it is placed, in device space.</summary>
internal struct Cf2Hint
{
    // attributes of the edge
    public const uint GhostBottom = 0x1;   // a single bottom edge
    public const uint GhostTop = 0x2;      // a single top edge
    public const uint PairBottom = 0x4;    // the bottom edge of a stem hint
    public const uint PairTop = 0x8;       // the top edge of a stem hint
    public const uint Locked = 0x10;       // this edge has been aligned by a blue zone
    public const uint Synthetic = 0x20;    // this edge was synthesized

    public uint Flags;

    /// <summary>The index in the original stem hint array (if not synthetic).</summary>
    public int Index;

    public int CsCoord;
    public int DsCoord;
    public int Scale;
}

/// <summary>A blue zone (<c>CF2_BlueRec</c>).</summary>
internal struct Cf2Blue
{
    public int CsBottomEdge;
    public int CsTopEdge;

    /// <summary>May be from either local or Family zones.</summary>
    public int CsFlatEdge;

    /// <summary>The top edge of a bottom zone or the bottom edge of a top zone (rounded).</summary>
    public int DsFlatEdge;

    public bool BottomZone;
}

/// <summary>
/// The blue zones (horizontal alignment zones) of a font at one size (<c>CF2_Blues</c>): computed from the Private DICT's
/// <c>BlueValues</c>, <c>OtherBlues</c>, <c>FamilyBlues</c> and <c>FamilyOtherBlues</c>, and used to capture hints and force them to a
/// common alignment point. Read-only once <see cref="Init"/> has run.
/// </summary>
internal sealed class Cf2Blues
{
    public const int MaxBlues = 7;
    public const int MaxOtherBlues = 5;

    /// <summary>The default value for the top of an em box when there are no real alignment zones (<c>CF2_ICF_Top</c>).</summary>
    public static readonly int IcfTop = Cf2Fixed.FromInt(880);

    /// <summary>The default value for the bottom of an em box (<c>CF2_ICF_Bottom</c>).</summary>
    public static readonly int IcfBottom = Cf2Fixed.FromInt(-120);

    /// <summary>Constant used for hint adjustment and for synthetic em box hint placement (<c>CF2_MIN_COUNTER</c>).</summary>
    public static readonly int MinCounter = Cf2Fixed.FromDouble(0.5);

    public int Scale;
    public int Count;
    public bool SuppressOvershoot;
    public bool DoEmBoxHints;

    public int BlueScale;
    public int BlueShift;
    public int BlueFuzz;

    public int Boost;

    public Cf2Hint EmBoxTopEdge;
    public Cf2Hint EmBoxBottomEdge;

    public readonly Cf2Blue[] Zone = new Cf2Blue[MaxBlues + MaxOtherBlues];

    /// <summary><c>cf2_blues_init</c>.</summary>
    /// <param name="font">The font whose data (transform, darkening) the zones are made for.</param>
    /// <param name="priv">The Private DICT of the font (or Font DICT) being drawn.</param>
    public void Init(Cf2Font font, CffPrivate priv)
    {
        int zoneHeight;
        int maxZoneHeight = 0;
        int csUnitsPerPixel;

        int numBlueValues = priv.NumBlueValues;
        int numOtherBlues = priv.NumOtherBlues;
        int numFamilyBlues = priv.NumFamilyBlues;
        int numFamilyOtherBlues = priv.NumFamilyOtherBlues;

        int[] blueValues = priv.BlueValues;
        int[] otherBlues = priv.OtherBlues;
        int[] familyBlues = priv.FamilyBlues;
        int[] familyOtherBlues = priv.FamilyOtherBlues;

        int emBoxBottom, emBoxTop;

        Scale = font.InnerTransform.D;

        // note: FreeType stores 1000 times the actual value for `BlueScale'
        BlueScale = FtCalc.DivFix(priv.BlueScale, Cf2Fixed.FromInt(1000));
        BlueShift = Cf2Fixed.FromInt(priv.BlueShift);
        BlueFuzz = Cf2Fixed.FromInt(priv.BlueFuzz);

        // synthetic em box hint heuristic
        //
        // Apply this when ideographic dictionary (LanguageGroup 1) has no real alignment zones.  Adobe tools generate dummy zones at
        // -250 and 1100 for a 1000 unit em.  Fonts with ICF-based alignment zones should not enable the heuristic.  When the
        // heuristic is enabled, the font's blue zones are ignored.

        // get em box from OS/2 typoAscender/Descender; FreeType does not parse these metrics, so the default is used
        emBoxBottom = IcfBottom;
        emBoxTop = IcfTop;

        if (priv.LanguageGroup == 1 &&
            (numBlueValues == 0 ||
             (numBlueValues == 4 &&
              blueValues[0] < emBoxBottom &&
              blueValues[1] < emBoxBottom &&
              blueValues[2] > emBoxTop &&
              blueValues[3] > emBoxTop)))
        {
            // Construct hint edges suitable for synthetic ghost hints at top and bottom of em box.  +-CF2_MIN_COUNTER allows for
            // unhinted features above or below the last hinted edge.  This also gives a net 1 pixel boost to the height of ideographic
            // glyphs.
            //
            // Note: Adjust synthetic hints outward by epsilon (0x.0001) to avoid interference.  E.g., some fonts have real hints at
            //       880 and -120.
            EmBoxBottomEdge.CsCoord = emBoxBottom - Cf2Fixed.Epsilon;
            EmBoxBottomEdge.DsCoord = unchecked(Cf2Fixed.Round(FtCalc.MulFix(EmBoxBottomEdge.CsCoord, Scale)) - MinCounter);
            EmBoxBottomEdge.Scale = Scale;
            EmBoxBottomEdge.Flags = Cf2Hint.GhostBottom | Cf2Hint.Locked | Cf2Hint.Synthetic;

            EmBoxTopEdge.CsCoord = unchecked(emBoxTop + Cf2Fixed.Epsilon + 2 * font.DarkenY);
            EmBoxTopEdge.DsCoord = unchecked(Cf2Fixed.Round(FtCalc.MulFix(EmBoxTopEdge.CsCoord, Scale)) + MinCounter);
            EmBoxTopEdge.Scale = Scale;
            EmBoxTopEdge.Flags = Cf2Hint.GhostTop | Cf2Hint.Locked | Cf2Hint.Synthetic;

            DoEmBoxHints = true; // enable the heuristic

            return;
        }

        // copy `BlueValues' and `OtherBlues' to a combined array of top and bottom zones
        for (int i = 0; i < numBlueValues; i += 2)
        {
            Zone[Count].CsBottomEdge = blueValues[i];
            Zone[Count].CsTopEdge = blueValues[i + 1];

            zoneHeight = unchecked(Zone[Count].CsTopEdge - Zone[Count].CsBottomEdge);

            if (zoneHeight < 0)
                continue; // reject this zone

            if (zoneHeight > maxZoneHeight)
            {
                // take maximum before darkening adjustment so overshoot suppression point doesn't change
                maxZoneHeight = zoneHeight;
            }

            // adjust both edges of top zone upward by twice darkening amount
            if (i != 0)
            {
                Zone[Count].CsTopEdge = unchecked(Zone[Count].CsTopEdge + 2 * font.DarkenY);
                Zone[Count].CsBottomEdge = unchecked(Zone[Count].CsBottomEdge + 2 * font.DarkenY);
            }

            // first `BlueValue' is bottom zone; others are top
            if (i == 0)
            {
                Zone[Count].BottomZone = true;
                Zone[Count].CsFlatEdge = Zone[Count].CsTopEdge;
            }
            else
            {
                Zone[Count].BottomZone = false;
                Zone[Count].CsFlatEdge = Zone[Count].CsBottomEdge;
            }

            Count += 1;
        }

        for (int i = 0; i < numOtherBlues; i += 2)
        {
            Zone[Count].CsBottomEdge = otherBlues[i];
            Zone[Count].CsTopEdge = otherBlues[i + 1];

            zoneHeight = unchecked(Zone[Count].CsTopEdge - Zone[Count].CsBottomEdge);

            if (zoneHeight < 0)
                continue; // reject this zone

            if (zoneHeight > maxZoneHeight)
            {
                // take maximum before darkening adjustment so overshoot suppression point doesn't change
                maxZoneHeight = zoneHeight;
            }

            // Note: bottom zones are not adjusted for darkening amount

            // all OtherBlues are bottom zone
            Zone[Count].BottomZone = true;
            Zone[Count].CsFlatEdge = Zone[Count].CsTopEdge;

            Count += 1;
        }

        // Adjust for FamilyBlues

        // Search for the nearest flat edge in `FamilyBlues' or `FamilyOtherBlues'.  According to the Black Book, any matching edge
        // must be within one device pixel

        csUnitsPerPixel = FtCalc.DivFix(Cf2Fixed.FromInt(1), Scale);

        // loop on all zones in this font
        for (int i = 0; i < Count; i++)
        {
            int minDiff;
            int flatFamilyEdge, diff;

            // value for this font
            int flatEdge = Zone[i].CsFlatEdge;

            if (Zone[i].BottomZone)
            {
                // In a bottom zone, the top edge is the flat edge.  Search `FamilyOtherBlues' for bottom zones; look for closest
                // Family edge that is within the one pixel threshold.
                minDiff = Cf2Fixed.Max;

                for (int j = 0; j < numFamilyOtherBlues; j += 2)
                {
                    // top edge
                    flatFamilyEdge = familyOtherBlues[j + 1];

                    diff = Cf2Fixed.Abs(unchecked(flatEdge - flatFamilyEdge));

                    if (diff < minDiff && diff < csUnitsPerPixel)
                    {
                        Zone[i].CsFlatEdge = flatFamilyEdge;
                        minDiff = diff;

                        if (diff == 0)
                            break;
                    }
                }

                // check the first member of FamilyBlues, which is a bottom zone
                if (numFamilyBlues >= 2)
                {
                    // top edge
                    flatFamilyEdge = familyBlues[1];

                    diff = Cf2Fixed.Abs(unchecked(flatEdge - flatFamilyEdge));

                    if (diff < minDiff && diff < csUnitsPerPixel)
                        Zone[i].CsFlatEdge = flatFamilyEdge;
                }
            }
            else
            {
                // In a top zone, the bottom edge is the flat edge.  Search `FamilyBlues' for top zones; skip first zone, which is a
                // bottom zone; look for closest Family edge that is within the one pixel threshold
                minDiff = Cf2Fixed.Max;

                for (int j = 2; j < numFamilyBlues; j += 2)
                {
                    // bottom edge
                    flatFamilyEdge = familyBlues[j];

                    // adjust edges of top zone upward by twice darkening amount
                    flatFamilyEdge = unchecked(flatFamilyEdge + 2 * font.DarkenY); // bottom edge

                    diff = Cf2Fixed.Abs(unchecked(flatEdge - flatFamilyEdge));

                    if (diff < minDiff && diff < csUnitsPerPixel)
                    {
                        Zone[i].CsFlatEdge = flatFamilyEdge;
                        minDiff = diff;

                        if (diff == 0)
                            break;
                    }
                }
            }
        }

        // TODO (in FreeType): enforce separation of zones, including BlueFuzz

        // Adjust BlueScale; similar to AdjustBlueScale() in coretype `bcsetup.c'.
        if (maxZoneHeight > 0)
        {
            if (BlueScale > FtCalc.DivFix(Cf2Fixed.FromInt(1), maxZoneHeight))
            {
                // clamp at maximum scale
                BlueScale = FtCalc.DivFix(Cf2Fixed.FromInt(1), maxZoneHeight);
            }
        }

        // Suppress overshoot and boost blue zones at small sizes.  Boost amount varies linearly from 0.5 pixel near 0 to 0 pixel at
        // blueScale cutoff.
        // Note: This boost amount is different from the coretype heuristic.
        if (Scale < BlueScale)
        {
            SuppressOvershoot = true;

            // Change rounding threshold for `dsFlatEdge'.
            // Note: constant changed from 0.5 to 0.6 to avoid a problem with 10ppem Arial
            Boost = unchecked(Cf2Fixed.FromDouble(.6) - FtCalc.MulDiv(Cf2Fixed.FromDouble(.6), Scale, BlueScale));
            if (Boost > 0x7FFF)
            {
                // boost must remain less than 0.5, or baseline could go negative
                Boost = 0x7FFF;
            }
        }

        // boost and darkening have similar effects; don't do both
        if (font.StemDarkened)
            Boost = 0;

        // set device space alignment for each zone; apply boost amount before rounding flat edge
        for (int i = 0; i < Count; i++)
        {
            if (Zone[i].BottomZone)
                Zone[i].DsFlatEdge = Cf2Fixed.Round(unchecked(FtCalc.MulFix(Zone[i].CsFlatEdge, Scale) - Boost));
            else
                Zone[i].DsFlatEdge = Cf2Fixed.Round(unchecked(FtCalc.MulFix(Zone[i].CsFlatEdge, Scale) + Boost));
        }
    }

    private static bool IsBottom(in Cf2Hint hint) => (hint.Flags & (Cf2Hint.PairBottom | Cf2Hint.GhostBottom)) != 0;

    private static bool IsTop(in Cf2Hint hint) => (hint.Flags & (Cf2Hint.PairTop | Cf2Hint.GhostTop)) != 0;

    private static bool IsValid(in Cf2Hint hint) => hint.Flags != 0;

    /// <summary>
    /// <c>cf2_blues_capture</c>: checks whether a stem hint is captured by one of the blue zones. Zero, one or both edges may be valid; only
    /// valid edges can be captured. For compatibility with CoolType, top and bottom zones are searched in the same pass (see
    /// <c>BlueLock</c>). If a hint is captured, it returns <see langword="true"/> and positions the edge(s) in one of three ways: at the
    /// flat edge of the zone if <c>BlueScale</c> suppresses overshoot; a minimum of one device pixel from the flat edge if overshoot is
    /// not suppressed and <c>BlueShift</c> requires it; else at the nearest device pixel.
    /// </summary>
    public bool Capture(ref Cf2Hint bottomHintEdge, ref Cf2Hint topHintEdge)
    {
        int csFuzz = BlueFuzz;

        // new position of captured edge
        int dsNew;

        // amount that hint is moved when positioned
        int dsMove = 0;

        bool captured = false;

        for (int i = 0; i < Count; i++)
        {
            if (Zone[i].BottomZone && IsBottom(bottomHintEdge))
            {
                if (unchecked(Zone[i].CsBottomEdge - csFuzz) <= bottomHintEdge.CsCoord &&
                    bottomHintEdge.CsCoord <= unchecked(Zone[i].CsTopEdge + csFuzz))
                {
                    // bottom edge captured by bottom zone
                    if (SuppressOvershoot)
                    {
                        dsNew = Zone[i].DsFlatEdge;
                    }
                    else if (unchecked(Zone[i].CsTopEdge - bottomHintEdge.CsCoord) >= BlueShift)
                    {
                        // guarantee minimum of 1 pixel overshoot
                        dsNew = Math.Min(Cf2Fixed.Round(bottomHintEdge.DsCoord), unchecked(Zone[i].DsFlatEdge - Cf2Fixed.FromInt(1)));
                    }
                    else
                    {
                        // simply round captured edge
                        dsNew = Cf2Fixed.Round(bottomHintEdge.DsCoord);
                    }

                    dsMove = unchecked(dsNew - bottomHintEdge.DsCoord);
                    captured = true;

                    break;
                }
            }

            if (!Zone[i].BottomZone && IsTop(topHintEdge))
            {
                if (unchecked(Zone[i].CsBottomEdge - csFuzz) <= topHintEdge.CsCoord &&
                    topHintEdge.CsCoord <= unchecked(Zone[i].CsTopEdge + csFuzz))
                {
                    // top edge captured by top zone
                    if (SuppressOvershoot)
                    {
                        dsNew = Zone[i].DsFlatEdge;
                    }
                    else if (unchecked(topHintEdge.CsCoord - Zone[i].CsBottomEdge) >= BlueShift)
                    {
                        // guarantee minimum of 1 pixel overshoot
                        dsNew = Math.Max(Cf2Fixed.Round(topHintEdge.DsCoord), unchecked(Zone[i].DsFlatEdge + Cf2Fixed.FromInt(1)));
                    }
                    else
                    {
                        // simply round captured edge
                        dsNew = Cf2Fixed.Round(topHintEdge.DsCoord);
                    }

                    dsMove = unchecked(dsNew - topHintEdge.DsCoord);
                    captured = true;

                    break;
                }
            }
        }

        if (captured)
        {
            // move both edges and flag them `locked'
            if (IsValid(bottomHintEdge))
            {
                bottomHintEdge.DsCoord = unchecked(bottomHintEdge.DsCoord + dsMove);
                bottomHintEdge.Flags |= Cf2Hint.Locked;
            }

            if (IsValid(topHintEdge))
            {
                topHintEdge.DsCoord = unchecked(topHintEdge.DsCoord + dsMove);
                topHintEdge.Flags |= Cf2Hint.Locked;
            }
        }

        return captured;
    }
}
