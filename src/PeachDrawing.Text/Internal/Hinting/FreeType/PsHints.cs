/****************************************************************************
 *
 * pshints.c
 *
 *   Adobe's code for handling CFF hints (body).
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
 * pshints.h
 *
 *   Adobe's code for handling CFF hints (body).
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

// Ported to C# for PeachDrawing.Text; modified. This file derives from FreeType 2.14.3 (VER-2-14-3): pshints.c, pshints.h.
// The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.

using System;

namespace PeachDrawing.Text.Internal.Hinting.FreeType;

/// <summary>A stem hint of a charstring (<c>CF2_StemHintRec</c>).</summary>
internal struct Cf2StemHint
{
    /// <summary>The device space positions are valid.</summary>
    public bool Used;

    /// <summary>The original character space value.</summary>
    public int Min;
    public int Max;

    /// <summary>The device space position after first use.</summary>
    public int MinDs;
    public int MaxDs;
}

/// <summary>
/// A bit mask that specifies which hints in the charstring are active at a given time (<c>CF2_HintMask</c>). Hints in CFF must be
/// declared at the start, before any drawing operators, with horizontal hints preceding vertical hints. The mask is ordered the same way,
/// with horizontal hints immediately followed by vertical hints. The maximum total number of hints is 96.
/// </summary>
/// <remarks>The methods that read and set it are in <c>psintrp.c</c>, and follow in the other part of this class.</remarks>
internal sealed partial class Cf2HintMask
{
    /// <summary>The maximum number of hints (<c>CF2_MAX_HINTS</c>).</summary>
    public const int MaxHints = 96;

    public Cf2Error Error = new();

    public bool IsValid;
    public bool IsNew;

    public int BitCount;
    public int ByteCount;

    public byte[] Mask = new byte[(MaxHints + 7) / 8];

    /// <summary>A structure copy of another mask.</summary>
    public void CopyFrom(Cf2HintMask other)
    {
        Error = other.Error;
        IsValid = other.IsValid;
        IsNew = other.IsNew;
        BitCount = other.BitCount;
        ByteCount = other.ByteCount;
        Array.Copy(other.Mask, Mask, Mask.Length);
    }
}

/// <summary>A move of a hint edge that is not optimal, kept for the second pass of the adjustment (<c>CF2_HintMoveRec</c>).</summary>
internal struct Cf2HintMove
{
    /// <summary>The index of the upper hint map edge.</summary>
    public int J;

    /// <summary>The adjustment to optimum position.</summary>
    public int MoveUp;
}

/// <summary>
/// A piecewise linear function for mapping y-coordinates from character space to device space, providing appropriate pixel alignment to
/// stem edges (<c>CF2_HintMap</c>). The map is an array of edges; when edges are paired, as from stem hints, the bottom edge must
/// immediately precede the top edge. Element character space and device space positions must both increase monotonically.
/// </summary>
/// <remarks>
/// <see cref="Build"/> must be called before any drawing operation (beginning with a Move operator) and at each hint substitution
/// (HintMask operator); <see cref="Map"/> is called to transform y-coordinates at each drawing operation (move, line, curve).
/// </remarks>
internal sealed class Cf2HintMap
{
    /// <summary>The maximum number of hint edges (<c>CF2_MAX_HINT_EDGES</c>).</summary>
    public const int MaxHintEdges = Cf2HintMask.MaxHints * 2;

    public Cf2Font Font = null!;

    /// <summary>The initial map, based on blue zones.</summary>
    public Cf2HintMap InitialHintMap = null!;

    /// <summary>Working storage for the second pass of the adjustment.</summary>
    public Cf2ArrStack<Cf2HintMove> HintMoves = null!;

    public bool IsValid;
    public bool Hinted;

    public int Scale;
    public int Count;

    /// <summary>Start search from this index.</summary>
    public int LastIndex;

    public readonly Cf2Hint[] Edge = new Cf2Hint[MaxHintEdges];

    /// <summary><c>cf2_hintmap_init</c>.</summary>
    public void Init(Cf2Font font, Cf2HintMap initialMap, Cf2ArrStack<Cf2HintMove> hintMoves, int scale)
    {
        IsValid = false;
        Count = 0;
        LastIndex = 0;
        Array.Clear(Edge);

        // copy parameters from font instance
        Hinted = font.Hinted;
        Scale = scale;
        Font = font;
        InitialHintMap = initialMap;

        // will clear in `cf2_hintmap_adjustHints'
        HintMoves = hintMoves;
    }

    /// <summary>A structure copy of another map (which shares its initial map, moves and font).</summary>
    public void CopyFrom(Cf2HintMap other)
    {
        Font = other.Font;
        InitialHintMap = other.InitialHintMap;
        HintMoves = other.HintMoves;
        IsValid = other.IsValid;
        Hinted = other.Hinted;
        Scale = other.Scale;
        Count = other.Count;
        LastIndex = other.LastIndex;
        Array.Copy(other.Edge, Edge, Edge.Length);
    }

    private static bool HintIsValid(in Cf2Hint hint) => hint.Flags != 0;

    private static bool HintIsPair(in Cf2Hint hint) => (hint.Flags & (Cf2Hint.PairBottom | Cf2Hint.PairTop)) != 0;

    private static bool HintIsPairTop(in Cf2Hint hint) => (hint.Flags & Cf2Hint.PairTop) != 0;

    internal static bool HintIsTop(in Cf2Hint hint) => (hint.Flags & (Cf2Hint.PairTop | Cf2Hint.GhostTop)) != 0;

    private static bool HintIsLocked(in Cf2Hint hint) => (hint.Flags & Cf2Hint.Locked) != 0;

    private static bool HintIsSynthetic(in Cf2Hint hint) => (hint.Flags & Cf2Hint.Synthetic) != 0;

    /// <summary>
    /// Constructs a hint edge from a stem hint; this is used as a parameter to <see cref="Cf2Blues.Capture"/>. <paramref name="hintOrigin"/>
    /// is the character space displacement of a seac accent. The stem hint is adjusted for darkening here (<c>cf2_hint_init</c>).
    /// </summary>
    private static void HintInit(out Cf2Hint hint, Cf2ArrStack<Cf2StemHint> stemHintArray, int indexStemHint, Cf2Font font, int hintOrigin, int scale, bool bottom)
    {
        hint = default;

        ref Cf2StemHint stemHint = ref stemHintArray.GetRef(indexStemHint);

        int width = unchecked(stemHint.Max - stemHint.Min);

        if (width == Cf2Fixed.FromInt(-21))
        {
            // ghost bottom
            if (bottom)
            {
                hint.CsCoord = stemHint.Max;
                hint.Flags = Cf2Hint.GhostBottom;
            }
            else
            {
                hint.Flags = 0;
            }
        }
        else if (width == Cf2Fixed.FromInt(-20))
        {
            // ghost top
            if (bottom)
            {
                hint.Flags = 0;
            }
            else
            {
                hint.CsCoord = stemHint.Min;
                hint.Flags = Cf2Hint.GhostTop;
            }
        }
        else if (width < 0)
        {
            // inverted pair
            //
            // Hints with negative widths were produced by an early version of a non-Adobe font tool.  The Type 2 spec allows edge
            // (ghost) hints with negative widths, but says
            //
            //   All other negative widths have undefined meaning.
            //
            // CoolType has a silent workaround that negates the hint width; for permissive mode, we do the same here.
            if (bottom)
            {
                hint.CsCoord = stemHint.Max;
                hint.Flags = Cf2Hint.PairBottom;
            }
            else
            {
                hint.CsCoord = stemHint.Min;
                hint.Flags = Cf2Hint.PairTop;
            }
        }
        else
        {
            // normal pair
            if (bottom)
            {
                hint.CsCoord = stemHint.Min;
                hint.Flags = Cf2Hint.PairBottom;
            }
            else
            {
                hint.CsCoord = stemHint.Max;
                hint.Flags = Cf2Hint.PairTop;
            }
        }

        // Now that ghost hints have been detected, adjust this edge for darkening.  Bottoms are not changed; tops are incremented by
        // twice `darkenY'.
        if (HintIsTop(hint))
            hint.CsCoord = unchecked(hint.CsCoord + 2 * font.DarkenY);

        hint.CsCoord = unchecked(hint.CsCoord + hintOrigin);
        hint.Scale = scale;
        hint.Index = indexStemHint; // index in original stem hint array

        // if original stem hint has been used, use the same position
        if (hint.Flags != 0 && stemHint.Used)
        {
            if (HintIsTop(hint))
                hint.DsCoord = stemHint.MaxDs;
            else
                hint.DsCoord = stemHint.MinDs;

            hint.Flags |= Cf2Hint.Locked;
        }
        else
        {
            hint.DsCoord = FtCalc.MulFix(hint.CsCoord, scale);
        }
    }

    /// <summary><c>cf2_hintmap_map</c>: transforms a character space coordinate to device space using the hint map.</summary>
    public int Map(int csCoord)
    {
        if (Count == 0 || !Hinted)
        {
            // there are no hints; use uniform scale and zero offset
            return FtCalc.MulFix(csCoord, Scale);
        }

        // start linear search from last hit
        int i = LastIndex;

        // search up
        while (i < Count - 1 && csCoord >= Edge[i + 1].CsCoord)
            i += 1;

        // search down
        while (i > 0 && csCoord < Edge[i].CsCoord)
            i -= 1;

        LastIndex = i;

        if (i == 0 && csCoord < Edge[0].CsCoord)
        {
            // special case for points below first edge: use uniform scale
            return unchecked(FtCalc.MulFix(unchecked(csCoord - Edge[0].CsCoord), Scale) + Edge[0].DsCoord);
        }

        // Note: entries with duplicate csCoord are allowed.  Use edge[i], the highest entry where csCoord >= entry[i].csCoord
        return unchecked(FtCalc.MulFix(unchecked(csCoord - Edge[i].CsCoord), Edge[i].Scale) + Edge[i].DsCoord);
    }

    // This hinting policy moves a hint pair in device space so that one of its two edges is on a device pixel boundary (its fractional
    // part is zero).  `cf2_hintmap_insertHint' guarantees no overlap in CS space.  Ensure here that there is no overlap in DS.
    //
    // In the first pass, edges are adjusted relative to adjacent hints.  Those that are below have already been adjusted.  Those that
    // are above have not yet been adjusted.  If a hint above blocks an adjustment to an optimal position, we will try again in a second
    // pass.  The second pass is top-down.
    private void AdjustHints()
    {
        HintMoves.Clear(); // working storage

        // First pass is bottom-up (font hint order) without look-ahead.  Locked edges are already adjusted.  Unlocked edges begin with
        // dsCoord from `initialHintMap'.  Save edges that are not optimally adjusted in `hintMoves' array, and process them in second
        // pass.
        for (int i = 0; i < Count; i++)
        {
            bool isPair = HintIsPair(Edge[i]);

            // final amount to move edge or edge pair
            int move = 0;

            int dsCoordI;
            int dsCoordJ;

            // index of upper edge (same value for ghost hint)
            int j = isPair ? i + 1 : i;

            if ((uint)j >= (uint)Count)
                throw new HintingException("A hint map is inconsistent.");

            dsCoordI = Edge[i].DsCoord;
            dsCoordJ = Edge[j].DsCoord;

            if (!HintIsLocked(Edge[i]))
            {
                // hint edge is not locked, we can adjust it
                int fracDown = Cf2Fixed.Fraction(dsCoordI);
                int fracUp = Cf2Fixed.Fraction(dsCoordJ);

                // calculate all four possibilities; moves down are negative
                int downMoveDown = 0 - fracDown;
                int upMoveDown = 0 - fracUp;
                int downMoveUp = fracDown == 0 ? 0 : Cf2Fixed.FromInt(1) - fracDown;
                int upMoveUp = fracUp == 0 ? 0 : Cf2Fixed.FromInt(1) - fracUp;

                // smallest move up
                int moveUp = Math.Min(downMoveUp, upMoveUp);

                // smallest move down
                int moveDown = Math.Max(downMoveDown, upMoveDown);

                int downMinCounter = Cf2Blues.MinCounter;
                int upMinCounter = Cf2Blues.MinCounter;
                bool saveEdge = false;

                // is there room to move up?  there is if we are at top of array or the next edge is at or beyond proposed move up?
                if (j >= Count - 1 ||
                    Edge[j + 1].DsCoord >= unchecked(dsCoordJ + moveUp + upMinCounter))
                {
                    // there is room to move up; is there also room to move down?
                    if (i == 0 ||
                        Edge[i - 1].DsCoord <= unchecked(dsCoordI + moveDown - downMinCounter))
                    {
                        // move smaller absolute amount
                        move = -moveDown < moveUp ? moveDown : moveUp; // optimum
                    }
                    else
                    {
                        move = moveUp;
                    }
                }
                else
                {
                    // is there room to move down?
                    if (i == 0 ||
                        Edge[i - 1].DsCoord <= unchecked(dsCoordI + moveDown - downMinCounter))
                    {
                        move = moveDown;

                        // true if non-optimum move
                        saveEdge = moveUp < -moveDown;
                    }
                    else
                    {
                        // no room to move either way without overlapping or reducing the counter too much
                        move = 0;
                        saveEdge = true;
                    }
                }

                // Identify non-moves and moves down that aren't optimal, and save them for second pass.  Do this only if there is an
                // unlocked edge above (which could possibly move).
                if (saveEdge &&
                    j < Count - 1 &&
                    !HintIsLocked(Edge[j + 1]))
                {
                    Cf2HintMove savedMove;

                    savedMove.J = j;

                    // desired adjustment in second pass
                    savedMove.MoveUp = unchecked(moveUp - move);

                    HintMoves.Push(savedMove);
                }

                // move the edge(s)
                Edge[i].DsCoord = unchecked(dsCoordI + move);
                if (isPair)
                    Edge[j].DsCoord = unchecked(dsCoordJ + move);
            }

            // adjust the scales, avoiding divide by zero
            if (i > 0)
            {
                if (Edge[i].CsCoord != Edge[i - 1].CsCoord)
                {
                    Edge[i - 1].Scale = FtCalc.DivFix(unchecked(Edge[i].DsCoord - Edge[i - 1].DsCoord), unchecked(Edge[i].CsCoord - Edge[i - 1].CsCoord));
                }
            }

            if (isPair)
            {
                if (Edge[j].CsCoord != Edge[j - 1].CsCoord)
                {
                    Edge[j - 1].Scale = FtCalc.DivFix(unchecked(Edge[j].DsCoord - Edge[j - 1].DsCoord), unchecked(Edge[j].CsCoord - Edge[j - 1].CsCoord));
                }

                i += 1; // skip upper edge on next loop
            }
        }

        // second pass tries to move non-optimal hints up, in case there is room now
        for (int i = HintMoves.Count; i > 0; i--)
        {
            Cf2HintMove hintMove = HintMoves.GetRef(i - 1);

            int j = hintMove.J;

            // this was tested before the push, above

            // is there room to move up?
            if (Edge[j + 1].DsCoord >= unchecked(Edge[j].DsCoord + hintMove.MoveUp + Cf2Blues.MinCounter))
            {
                // there is more room now, move edge up
                Edge[j].DsCoord = unchecked(Edge[j].DsCoord + hintMove.MoveUp);

                if (HintIsPair(Edge[j]))
                {
                    if (j <= 0)
                        throw new HintingException("A hint map is inconsistent.");

                    Edge[j - 1].DsCoord = unchecked(Edge[j - 1].DsCoord + hintMove.MoveUp);
                }
            }
        }
    }

    /// <summary>Inserts hint edges into the map, sorted by character space coordinate (<c>cf2_hintmap_insertHint</c>).</summary>
    private void InsertHint(ref Cf2Hint bottomHintEdge, ref Cf2Hint topHintEdge)
    {
        int indexInsert;

        // set default values, then check for edge hints
        bool isPair = true;

        // determine how many and which edges to insert
        bool bottomValid = HintIsValid(bottomHintEdge);
        bool topValid = HintIsValid(topHintEdge);

        // one or none of the input params may be invalid when dealing with edge hints; at least one edge must be valid
        if (!bottomValid && !topValid)
            return;

        // the edges to insert: the first, and the second when the two form a pair
        ref Cf2Hint firstHintEdge = ref bottomHintEdge;
        ref Cf2Hint secondHintEdge = ref topHintEdge;

        if (!bottomValid)
        {
            // insert only the top edge
            firstHintEdge = ref topHintEdge;
            isPair = false;
        }
        else if (!topValid)
        {
            // insert only the bottom edge
            isPair = false;
        }

        // paired edges must be in proper order
        if (isPair && topHintEdge.CsCoord < bottomHintEdge.CsCoord)
            return;

        // linear search to find index value of insertion point
        indexInsert = 0;
        for (; indexInsert < Count; indexInsert++)
        {
            if (Edge[indexInsert].CsCoord >= firstHintEdge.CsCoord)
                break;
        }

        // Discard any hints that overlap in character space.  Most often, this is while building the initial map, where captured hints
        // from all zones are combined.  Define overlap to include hints that `touch' (overlap zero).  Hiragino Sans/Gothic fonts have
        // numerous hints that touch.  Some fonts have non-ideographic glyphs that overlap our synthetic hints.
        //
        // Overlap also occurs when darkening stem hints that are close.
        if (indexInsert < Count)
        {
            // we are inserting before an existing edge: verify that an existing edge is not the same
            if (Edge[indexInsert].CsCoord == firstHintEdge.CsCoord)
                return; // ignore overlapping stem hint

            // verify that a new pair does not straddle the next edge
            if (isPair && Edge[indexInsert].CsCoord <= secondHintEdge.CsCoord)
                return; // ignore overlapping stem hint

            // verify that we are not inserting between paired edges
            if (HintIsPairTop(Edge[indexInsert]))
                return; // ignore overlapping stem hint
        }

        // recompute device space locations using initial hint map
        if (InitialHintMap.IsValid && !HintIsLocked(firstHintEdge))
        {
            if (isPair)
            {
                // Use hint map to position the center of stem, and nominal scale to position the two edges.  This preserves the stem
                // width.
                int midpoint = InitialHintMap.Map(unchecked(firstHintEdge.CsCoord + unchecked(secondHintEdge.CsCoord - firstHintEdge.CsCoord) / 2));
                int halfWidth = FtCalc.MulFix(unchecked(secondHintEdge.CsCoord - firstHintEdge.CsCoord) / 2, Scale);

                firstHintEdge.DsCoord = unchecked(midpoint - halfWidth);
                secondHintEdge.DsCoord = unchecked(midpoint + halfWidth);
            }
            else
            {
                firstHintEdge.DsCoord = InitialHintMap.Map(firstHintEdge.CsCoord);
            }
        }

        // Discard any hints that overlap in device space; this can occur because locked hints have been moved to align with blue zones.
        //
        // TODO (in FreeType): Although we might correct this later during adjustment, we don't currently have a way to delete a
        // conflicting hint once it has been inserted.
        if (indexInsert > 0)
        {
            // we are inserting after an existing edge
            if (firstHintEdge.DsCoord < Edge[indexInsert - 1].DsCoord)
                return;
        }

        if (indexInsert < Count)
        {
            // we are inserting before an existing edge
            if (isPair)
            {
                if (secondHintEdge.DsCoord > Edge[indexInsert].DsCoord)
                    return;
            }
            else
            {
                if (firstHintEdge.DsCoord > Edge[indexInsert].DsCoord)
                    return;
            }
        }

        // make room to insert
        {
            int iSrc = Count - 1;
            int iDst = isPair ? Count + 1 : Count;

            int count = Count - indexInsert;

            if (iDst >= MaxHintEdges)
                return; // too many hintmaps

            while (count-- > 0)
                Edge[iDst--] = Edge[iSrc--];

            // insert first edge
            Edge[indexInsert] = firstHintEdge; // copy struct
            Count += 1;

            if (isPair)
            {
                // insert second edge
                Edge[indexInsert + 1] = secondHintEdge; // copy struct
                Count += 1;
            }
        }
    }

    /// <summary>
    /// Builds a map from hints and mask (<c>cf2_hintmap_build</c>). This method may recur one level if the initial hint map is not yet
    /// valid. If <paramref name="initialMap"/> is true, it simply builds the initial map.
    /// </summary>
    /// <remarks>
    /// Synthetic hints are used in two ways. A hint at zero is inserted, if needed, in the initial hint map, to prevent translations from
    /// propagating across the origin. If synthetic em box hints are enabled for ideographic dictionaries, then they are inserted in all
    /// hint maps, including the initial one.
    /// </remarks>
    public void Build(Cf2ArrStack<Cf2StemHint> hStemHintArray, Cf2ArrStack<Cf2StemHint> vStemHintArray, Cf2HintMask hintMask, int hintOrigin, bool initialMap)
    {
        Cf2Font font = Font;
        var tempHintMask = new Cf2HintMask();

        // check whether initial map is constructed
        if (!initialMap && !InitialHintMap.IsValid)
        {
            // make recursive call with initialHintMap and temporary mask; temporary mask will get all bits set, below
            tempHintMask.Init(hintMask.Error);
            InitialHintMap.Build(hStemHintArray, vStemHintArray, tempHintMask, hintOrigin, true);
        }

        if (!hintMask.IsValid)
        {
            // without a hint mask, assume all hints are active
            hintMask.SetAll(hStemHintArray.Count + vStemHintArray.Count);
            if (!hintMask.IsValid)
                return; // too many stem hints
        }

        // begin by clearing the map
        Count = 0;
        LastIndex = 0;

        // make a copy of the hint mask so we can modify it
        tempHintMask.CopyFrom(hintMask);
        byte[] mask = tempHintMask.Mask;
        int maskPtr = 0;

        // use the hStem hints only, which are first in the mask
        int bitCount = hStemHintArray.Count;

        // Defense-in-depth.  Should never return here.
        if (bitCount > hintMask.BitCount)
            return;

        // synthetic embox hints get highest priority
        if (font.Blues.DoEmBoxHints)
        {
            Cf2Hint dummy = default; // invalid hint map element

            // ghost bottom
            InsertHint(ref font.Blues.EmBoxBottomEdge, ref dummy);

            // ghost top
            InsertHint(ref dummy, ref font.Blues.EmBoxTopEdge);
        }

        // insert hints captured by a blue zone or already locked (higher priority)
        byte maskByte = 0x80;
        for (int i = 0; i < bitCount; i++)
        {
            if ((maskByte & mask[maskPtr]) != 0)
            {
                // expand StemHint into two `CF2_Hint' elements
                HintInit(out Cf2Hint bottomHintEdge, hStemHintArray, i, font, hintOrigin, Scale, true /* bottom */);
                HintInit(out Cf2Hint topHintEdge, hStemHintArray, i, font, hintOrigin, Scale, false /* top */);

                if (HintIsLocked(bottomHintEdge) ||
                    HintIsLocked(topHintEdge) ||
                    font.Blues.Capture(ref bottomHintEdge, ref topHintEdge))
                {
                    // insert captured hint into map
                    InsertHint(ref bottomHintEdge, ref topHintEdge);

                    mask[maskPtr] &= (byte)~maskByte; // turn off the bit for this hint
                }
            }

            if ((i & 7) == 7)
            {
                // move to next mask byte
                maskPtr++;
                maskByte = 0x80;
            }
            else
            {
                maskByte >>= 1;
            }
        }

        // initial hint map includes only captured hints plus maybe one at 0

        // TODO (in FreeType): There is a problem here because we are trying to build a single hint map containing all captured hints.
        // It is possible for there to be conflicts between captured hints, either because of darkening or because the hints are in
        // separate hint zones (we are ignoring hint zones for the initial map).

        if (initialMap)
        {
            // Apply a heuristic that inserts a point for (0,0), unless it's already covered by a mapping.  This locks the baseline for
            // glyphs that have no baseline hints.
            if (Count == 0 ||
                Edge[0].CsCoord > 0 ||
                Edge[Count - 1].CsCoord < 0)
            {
                // all edges are above 0 or all edges are below 0; construct a locked edge hint at 0
                Cf2Hint edge = default;

                edge.Flags = Cf2Hint.GhostBottom | Cf2Hint.Locked | Cf2Hint.Synthetic;
                edge.Scale = Scale;

                Cf2Hint invalid = default;
                InsertHint(ref edge, ref invalid);
            }
        }
        else
        {
            // insert remaining hints
            maskPtr = 0;

            maskByte = 0x80;
            for (int i = 0; i < bitCount; i++)
            {
                if ((maskByte & mask[maskPtr]) != 0)
                {
                    HintInit(out Cf2Hint bottomHintEdge, hStemHintArray, i, font, hintOrigin, Scale, true /* bottom */);
                    HintInit(out Cf2Hint topHintEdge, hStemHintArray, i, font, hintOrigin, Scale, false /* top */);

                    InsertHint(ref bottomHintEdge, ref topHintEdge);
                }

                if ((i & 7) == 7)
                {
                    // move to next mask byte
                    maskPtr++;
                    maskByte = 0x80;
                }
                else
                {
                    maskByte >>= 1;
                }
            }
        }

        // adjust positions of hint edges that are not locked to blue zones
        AdjustHints();

        // save the position of all hints that were used in this hint map; if we use them again, we'll locate them in the same position
        if (!initialMap)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!HintIsSynthetic(Edge[i]))
                {
                    // Note: include both valid and invalid edges
                    // Note: top and bottom edges are copied back separately
                    ref Cf2StemHint stemhint = ref hStemHintArray.GetRef(Edge[i].Index);

                    if (HintIsTop(Edge[i]))
                        stemhint.MaxDs = Edge[i].DsCoord;
                    else
                        stemhint.MinDs = Edge[i].DsCoord;

                    stemhint.Used = true;
                }
            }
        }

        // hint map is ready to use
        IsValid = true;

        // remember this mask has been used
        hintMask.IsNew = false;
    }
}

/// <summary>
/// A wrapper for drawing operations that scales the coordinates according to the render matrix and hint map (<c>CF2_GlyphPath</c>). It
/// also tracks open paths to control ClosePath and to insert MoveTo for broken fonts.
/// </summary>
/// <remarks>
/// The drawing operations are called by the interpreter with character space coordinates. Each path element is placed into a queue of
/// length one to await the calculation of the following element. At that time, the darkening offset of the following element is known
/// and joins can be computed, including possible modification of this element, before mapping to device space and passing it on to the
/// outline consumer.
/// </remarks>
internal sealed class Cf2GlyphPath
{
    private readonly Cf2Font _font;
    private readonly Cf2OutlineCallbacks _callbacks;

    private readonly Cf2HintMap _hintMap = new();       // current hint map
    private readonly Cf2HintMap _firstHintMap = new();  // saved copy
    private readonly Cf2HintMap _initialHintMap = new(); // based on all captured hints

    private readonly Cf2ArrStack<Cf2HintMove> _hintMoves; // list of hint moves for 2nd pass

    private readonly int _scaleX;   // matrix a
    private readonly int _scaleC;   // matrix c
    private int _scaleY;            // matrix d

    private readonly FtVector _fractionalTranslation; // including deviceXScale

    private bool _pathIsOpen;       // true after MoveTo
    private bool _pathIsClosing;    // true when synthesizing closepath line
    private readonly bool _darken;  // true if stem darkening
    private bool _moveIsPending;    // true between MoveTo and offset MoveTo

    // references used to call `cf2_hintmap_build', if necessary
    private readonly Cf2ArrStack<Cf2StemHint> _hStemHintArray;
    private readonly Cf2ArrStack<Cf2StemHint> _vStemHintArray;
    private readonly Cf2HintMask _hintMask;   // the current mask
    private readonly int _hintOriginY;        // copy of current origin

    private readonly int _xOffset;  // character space offsets
    private readonly int _yOffset;

    // character space miter limit threshold
    private readonly int _miterLimit;

    // vertical/horizontal snap distance in character space
    private readonly int _snapThreshold;

    private FtVector _offsetStart0; // first and second points of first
    private FtVector _offsetStart1; // element with offset applied

    // current point, character space, before offset
    private FtVector _currentCS;

    // current point, device space
    private FtVector _currentDS;

    // start point of subpath, character space
    private FtVector _start;

    // the following members constitute the `queue' of one element
    private bool _elemIsQueued;
    private int _prevElemOp;

    private FtVector _prevElemP0;
    private FtVector _prevElemP1;
    private FtVector _prevElemP2;
    private FtVector _prevElemP3;

    /// <summary><c>cf2_glyphpath_init</c>.</summary>
    public Cf2GlyphPath(Cf2Font font, Cf2OutlineCallbacks callbacks, int scaleY, Cf2ArrStack<Cf2StemHint> hStemHintArray,
        Cf2ArrStack<Cf2StemHint> vStemHintArray, Cf2HintMask hintMask, int hintOriginY, FtVector fractionalTranslation)
    {
        _font = font;
        _callbacks = callbacks;

        _hintMoves = new Cf2ArrStack<Cf2HintMove>(font.Error);

        _initialHintMap.Init(font, _initialHintMap, _hintMoves, scaleY);
        _firstHintMap.Init(font, _initialHintMap, _hintMoves, scaleY);
        _hintMap.Init(font, _initialHintMap, _hintMoves, scaleY);

        _scaleX = font.InnerTransform.A;
        _scaleC = font.InnerTransform.C;
        _scaleY = font.InnerTransform.D;

        _fractionalTranslation = fractionalTranslation;

        _hStemHintArray = hStemHintArray;
        _vStemHintArray = vStemHintArray;
        _hintMask = hintMask; // ref to current mask
        _hintOriginY = hintOriginY;
        _darken = font.Darkened;
        _xOffset = font.DarkenX;
        _yOffset = font.DarkenY;
        _miterLimit = unchecked(2 * Math.Max(Cf2Fixed.Abs(_xOffset), Cf2Fixed.Abs(_yOffset)));

        // .1 character space unit
        _snapThreshold = Cf2Fixed.FromDouble(0.1);

        _moveIsPending = true;
        _pathIsOpen = false;
        _pathIsClosing = false;
        _elemIsQueued = false;
    }

    // Compute angular momentum for winding order detection.  It is called for all lines and curves, but not necessarily in element
    // order.
    private static int GetWindingMomentum(int x1, int y1, int x2, int y2)
    {
        // cross product of pt1 position from origin with pt2 position from pt1; we reduce the precision so that the result fits into
        // 32 bits
        return unchecked((x1 >> 16) * (unchecked(y2 - y1) >> 16) - (y1 >> 16) * (unchecked(x2 - x1) >> 16));
    }

    // Hint point in y-direction and apply outerTransform.  Input `current' hint map (which is actually delayed by one element).  Input
    // x,y point in Character Space.  Output x,y point in Device Space, including translation.
    private void HintPoint(Cf2HintMap hintmap, out FtVector ppt, int x, int y)
    {
        FtVector pt; // hinted point in upright DS

        pt.X = unchecked(FtCalc.MulFix(_scaleX, x) + FtCalc.MulFix(_scaleC, y));
        pt.Y = hintmap.Map(y);

        ppt.X = unchecked(FtCalc.MulFix(_font.OuterTransform.A, pt.X) + (FtCalc.MulFix(_font.OuterTransform.C, pt.Y) + _fractionalTranslation.X));
        ppt.Y = unchecked(FtCalc.MulFix(_font.OuterTransform.B, pt.X) + (FtCalc.MulFix(_font.OuterTransform.D, pt.Y) + _fractionalTranslation.Y));
    }

    // From two line segments, (u1,u2) and (v1,v2), compute a point of intersection on the corresponding lines.  Return false if no
    // intersection is found, or if the intersection is too far away from the ends of the line segments, u2 and v1.
    private bool ComputeIntersection(in FtVector u1, in FtVector u2, in FtVector v1, in FtVector v2, out FtVector intersection)
    {
        // Let `u' be a zero-based vector from the first segment, `v' from the second segment.  Let `w 'be the zero-based vector from
        // `u1' to `v1'.  `perp' is the `perpendicular dot product'; see https://mathworld.wolfram.com/PerpDotProduct.html.  `s' is the
        // parameter for the parametric line for the first segment (`u').
        //
        // See notation in http://geomalgorithms.com/a05-_intersect-1.html.  Calculations are done in 16.16, but must handle the
        // squaring of line lengths in character space.  We scale all vectors by 1/32 to avoid overflow.  This allows values up to 4095
        // to be squared.  The scale factor cancels in the divide.
        FtVector u, v, w; // scaled vectors
        int denominator, s;

        intersection = default;

        u.X = CsScale(unchecked(u2.X - u1.X));
        u.Y = CsScale(unchecked(u2.Y - u1.Y));
        v.X = CsScale(unchecked(v2.X - v1.X));
        v.Y = CsScale(unchecked(v2.Y - v1.Y));
        w.X = CsScale(unchecked(v1.X - u1.X));
        w.Y = CsScale(unchecked(v1.Y - u1.Y));

        denominator = Perp(u, v);

        if (denominator == 0)
            return false; // parallel or coincident lines

        s = FtCalc.DivFix(Perp(w, v), denominator);

        intersection.X = unchecked(u1.X + FtCalc.MulFix(s, unchecked(u2.X - u1.X)));
        intersection.Y = unchecked(u1.Y + FtCalc.MulFix(s, unchecked(u2.Y - u1.Y)));

        // Special case snapping for horizontal and vertical lines.  This cleans up intersections and reduces problems with winding
        // order detection.  Sample case is sbc cd KozGoPr6N-Medium.otf 20 16685.  Note: these calculations are in character space.
        if (u1.X == u2.X && Cf2Fixed.Abs(unchecked(intersection.X - u1.X)) < _snapThreshold)
            intersection.X = u1.X;

        if (u1.Y == u2.Y && Cf2Fixed.Abs(unchecked(intersection.Y - u1.Y)) < _snapThreshold)
            intersection.Y = u1.Y;

        if (v1.X == v2.X && Cf2Fixed.Abs(unchecked(intersection.X - v1.X)) < _snapThreshold)
            intersection.X = v1.X;

        if (v1.Y == v2.Y && Cf2Fixed.Abs(unchecked(intersection.Y - v1.Y)) < _snapThreshold)
            intersection.Y = v1.Y;

        // limit the intersection distance from midpoint of u2 and v1
        if (Cf2Fixed.Abs(unchecked(intersection.X - unchecked(u2.X + v1.X) / 2)) > _miterLimit ||
            Cf2Fixed.Abs(unchecked(intersection.Y - unchecked(u2.Y + v1.Y) / 2)) > _miterLimit)
        {
            return false;
        }

        return true;
    }

    private static int Perp(FtVector a, FtVector b) => unchecked(FtCalc.MulFix(a.X, b.Y) - FtCalc.MulFix(a.Y, b.X));

    // round and divide by 32
    private static int CsScale(int x) => unchecked(x + 0x10) >> 5;

    // Push the cached element (glyphpath->prevElem*) to the outline consumer.  When a darkening offset is used, the end point of the
    // cached element may be adjusted to an intersection point or we may synthesize a connecting line to the current element.  If we are
    // closing a subpath, we may also generate a connecting line to the start point.
    //
    // This is where Character Space (CS) is converted to Device Space (DS) using a hint map.  This calculation must use a HintMap that
    // was valid at the time the element was saved.  For the first point in a subpath, that is a saved HintMap.  For most elements, it
    // just means the caller has delayed building a HintMap from the current HintMask.
    //
    // Transform each point with outerTransform and call the outline callbacks.  This is a general 3x3 transform:
    //
    //   x' = a*x + c*y + tx, y' = b*x + d*y + ty
    //
    // but it uses 4 elements from CF2_Font and the translation part from CF2_GlyphPath.
    private void PushPrevElem(Cf2HintMap hintmap, ref FtVector nextP0, FtVector nextP1, bool close)
    {
        Cf2CallbackParams parameters = default;

        FtVector intersection = default;
        bool useIntersection = false;

        ref FtVector prevP0 = ref _prevElemOp == Cf2PathOp.LineTo ? ref _prevElemP0 : ref _prevElemP2;
        ref FtVector prevP1 = ref _prevElemOp == Cf2PathOp.LineTo ? ref _prevElemP1 : ref _prevElemP3;

        // optimization: if previous and next elements are offset by the same amount, then there will be no gap, and no need to compute
        // an intersection.
        if (prevP1.X != nextP0.X || prevP1.Y != nextP0.Y)
        {
            // previous element does not join next element: adjust end point of previous element to the intersection
            useIntersection = ComputeIntersection(prevP0, prevP1, nextP0, nextP1, out intersection);
            if (useIntersection)
            {
                // modify the last point of the cached element (either line or curve)
                prevP1 = intersection;
            }
        }

        parameters.Pt0 = _currentDS;

        switch (_prevElemOp)
        {
            case Cf2PathOp.LineTo:
                parameters.Op = Cf2PathOp.LineTo;

                // note: pt2 and pt3 are unused
                if (close)
                {
                    // use first hint map if closing
                    HintPoint(_firstHintMap, out parameters.Pt1, _prevElemP1.X, _prevElemP1.Y);
                }
                else
                {
                    HintPoint(hintmap, out parameters.Pt1, _prevElemP1.X, _prevElemP1.Y);
                }

                // output only non-zero length lines
                if (parameters.Pt0.X != parameters.Pt1.X || parameters.Pt0.Y != parameters.Pt1.Y)
                {
                    _callbacks.LineTo(parameters);

                    _currentDS = parameters.Pt1;
                }

                break;

            case Cf2PathOp.CubeTo:
                parameters.Op = Cf2PathOp.CubeTo;

                // TODO (in FreeType): should we intersect the interior joins (p1-p2 and p2-p3)?
                HintPoint(hintmap, out parameters.Pt1, _prevElemP1.X, _prevElemP1.Y);
                HintPoint(hintmap, out parameters.Pt2, _prevElemP2.X, _prevElemP2.Y);
                HintPoint(hintmap, out parameters.Pt3, _prevElemP3.X, _prevElemP3.Y);

                _callbacks.CubeTo(parameters);

                _currentDS = parameters.Pt3;

                break;
        }

        if (!useIntersection || close)
        {
            // insert connecting line between end of previous element and start of current one
            // note: at the end of a subpath, we might do both, so use `nextP0' before we change it, below
            if (close)
            {
                // if we are closing the subpath, then nextP0 is in the first hint zone
                HintPoint(_firstHintMap, out parameters.Pt1, nextP0.X, nextP0.Y);
            }
            else
            {
                HintPoint(hintmap, out parameters.Pt1, nextP0.X, nextP0.Y);
            }

            if (parameters.Pt1.X != _currentDS.X || parameters.Pt1.Y != _currentDS.Y)
            {
                // length is nonzero
                parameters.Op = Cf2PathOp.LineTo;
                parameters.Pt0 = _currentDS;

                // note: pt2 and pt3 are unused
                _callbacks.LineTo(parameters);

                _currentDS = parameters.Pt1;
            }
        }

        if (useIntersection)
        {
            // return intersection point to caller
            nextP0 = intersection;
        }
    }

    // push a MoveTo element based on current point and offset of current element
    private void PushMove(FtVector start)
    {
        Cf2CallbackParams parameters = default;

        parameters.Op = Cf2PathOp.MoveTo;
        parameters.Pt0 = _currentDS;

        // Test if move has really happened yet; it would have called `cf2_hintmap_build' to set `isValid'.
        if (!_hintMap.IsValid)
        {
            // we are here iff first subpath is missing a moveto operator: synthesize first moveTo to finish initialization of hintMap
            MoveTo(_start.X, _start.Y);
        }

        HintPoint(_hintMap, out parameters.Pt1, start.X, start.Y);

        // note: pt2 and pt3 are unused
        _callbacks.MoveTo(parameters);

        _currentDS = parameters.Pt1;
        _offsetStart0 = start;
    }

    // All coordinates are in character space.  On input, (x1, y1) and (x2, y2) give line segment.  On output, (x, y) give offset
    // vector.  We use a piecewise approximation to trig functions.
    //
    // TODO (in FreeType): Offset true perpendicular and proper length; supply the y-translation for hinting here, too, that adds
    // yOffset unconditionally to *y.
    private void ComputeOffset(int x1, int y1, int x2, int y2, out int x, out int y)
    {
        int dx = unchecked(x2 - x1);
        int dy = unchecked(y2 - y1);

        // note: negative offsets don't work here; negate deltas to change quadrants, below
        if (_font.ReverseWinding)
        {
            dx = unchecked(-dx);
            dy = unchecked(-dy);
        }

        x = y = 0;

        if (!_darken)
            return;

        // add momentum for this path element
        _callbacks.WindingMomentum = unchecked(_callbacks.WindingMomentum + GetWindingMomentum(x1, y1, x2, y2));

        // note: allow mixed integer and fixed multiplication here
        if (dx >= 0)
        {
            if (dy >= 0)
            {
                // first quadrant, +x +y
                if (dx > unchecked(2 * dy))
                {
                    // +x
                    x = 0;
                    y = 0;
                }
                else if (dy > unchecked(2 * dx))
                {
                    // +y
                    x = _xOffset;
                    y = _yOffset;
                }
                else
                {
                    // +x +y
                    x = FtCalc.MulFix(Cf2Fixed.FromDouble(0.7), _xOffset);
                    y = FtCalc.MulFix(Cf2Fixed.FromDouble(1.0 - 0.7), _yOffset);
                }
            }
            else
            {
                // fourth quadrant, +x -y
                if (dx > unchecked(-2 * dy))
                {
                    // +x
                    x = 0;
                    y = 0;
                }
                else if (unchecked(-dy) > unchecked(2 * dx))
                {
                    // -y
                    x = unchecked(-_xOffset);
                    y = _yOffset;
                }
                else
                {
                    // +x -y
                    x = FtCalc.MulFix(Cf2Fixed.FromDouble(-0.7), _xOffset);
                    y = FtCalc.MulFix(Cf2Fixed.FromDouble(1.0 - 0.7), _yOffset);
                }
            }
        }
        else
        {
            if (dy >= 0)
            {
                // second quadrant, -x +y
                if (unchecked(-dx) > unchecked(2 * dy))
                {
                    // -x
                    x = 0;
                    y = unchecked(2 * _yOffset);
                }
                else if (dy > unchecked(-2 * dx))
                {
                    // +y
                    x = _xOffset;
                    y = _yOffset;
                }
                else
                {
                    // -x +y
                    x = FtCalc.MulFix(Cf2Fixed.FromDouble(0.7), _xOffset);
                    y = FtCalc.MulFix(Cf2Fixed.FromDouble(1.0 + 0.7), _yOffset);
                }
            }
            else
            {
                // third quadrant, -x -y
                if (unchecked(-dx) > unchecked(-2 * dy))
                {
                    // -x
                    x = 0;
                    y = unchecked(2 * _yOffset);
                }
                else if (unchecked(-dy) > unchecked(-2 * dx))
                {
                    // -y
                    x = unchecked(-_xOffset);
                    y = _yOffset;
                }
                else
                {
                    // -x -y
                    x = FtCalc.MulFix(Cf2Fixed.FromDouble(-0.7), _xOffset);
                    y = FtCalc.MulFix(Cf2Fixed.FromDouble(1.0 + 0.7), _yOffset);
                }
            }
        }
    }

    /// <summary><c>cf2_glyphpath_moveTo</c>.</summary>
    public void MoveTo(int x, int y)
    {
        CloseOpenPath();

        // save the parameters of the move for later, when we'll know how to offset it; also save last move point
        _currentCS.X = _start.X = x;
        _currentCS.Y = _start.Y = y;

        _moveIsPending = true;

        // ensure we have a valid map with current mask
        if (!_hintMap.IsValid || _hintMask.IsNew)
            _hintMap.Build(_hStemHintArray, _vStemHintArray, _hintMask, _hintOriginY, false);

        // save a copy of current HintMap to use when drawing initial point
        _firstHintMap.CopyFrom(_hintMap); // structure copy
    }

    /// <summary><c>cf2_glyphpath_lineTo</c>.</summary>
    public void LineTo(int x, int y)
    {
        int xOffset, yOffset;
        FtVector p0, p1;
        bool newHintMap;

        // New hints will be applied after cf2_glyphpath_pushPrevElem has run.  In case this is a synthesized closing line, any new
        // hints should be delayed until this path is closed (`cf2_hintmask_isNew' will be called again before the next line or curve).

        // true if new hint map not on close
        newHintMap = _hintMask.IsNew && !_pathIsClosing;

        // Zero-length lines may occur in the charstring.  Because we cannot compute darkening offsets or intersections from
        // zero-length lines, it is best to remove them and avoid artifacts.  However, zero-length lines in CS at the start of a new
        // hint map can generate non-zero lines in DS due to hint substitution.  We detect a change in hint map here and pass those
        // zero-length lines along.
        if (_currentCS.X == x && _currentCS.Y == y && !newHintMap)
        {
            // Ignore zero-length lines in CS where the hint map is the same because the line in DS will also be zero length.
            //
            // Ignore zero-length lines when we synthesize a closing line because the close will be handled in
            // cf2_glyphPath_pushPrevElem.
            return;
        }

        ComputeOffset(_currentCS.X, _currentCS.Y, x, y, out xOffset, out yOffset);

        // construct offset points
        p0.X = unchecked(_currentCS.X + xOffset);
        p0.Y = unchecked(_currentCS.Y + yOffset);
        p1.X = unchecked(x + xOffset);
        p1.Y = unchecked(y + yOffset);

        if (_moveIsPending)
        {
            // emit offset 1st point as MoveTo
            PushMove(p0);

            _moveIsPending = false; // adjust state machine
            _pathIsOpen = true;

            _offsetStart1 = p1; // record second point
        }

        if (_elemIsQueued)
            PushPrevElem(_hintMap, ref p0, p1, false);

        // queue the current element with offset points
        _elemIsQueued = true;
        _prevElemOp = Cf2PathOp.LineTo;
        _prevElemP0 = p0;
        _prevElemP1 = p1;

        // update current map
        if (newHintMap)
            _hintMap.Build(_hStemHintArray, _vStemHintArray, _hintMask, _hintOriginY, false);

        _currentCS.X = x; // pre-offset current point
        _currentCS.Y = y;
    }

    /// <summary><c>cf2_glyphpath_curveTo</c>.</summary>
    public void CurveTo(int x1, int y1, int x2, int y2, int x3, int y3)
    {
        int xOffset1, yOffset1, xOffset3, yOffset3;
        FtVector p0, p1, p2, p3;

        // TODO (in FreeType): ignore zero length portions of curve??
        ComputeOffset(_currentCS.X, _currentCS.Y, x1, y1, out xOffset1, out yOffset1);
        ComputeOffset(x2, y2, x3, y3, out xOffset3, out yOffset3);

        // add momentum from the middle segment
        _callbacks.WindingMomentum = unchecked(_callbacks.WindingMomentum + GetWindingMomentum(x1, y1, x2, y2));

        // construct offset points
        p0.X = unchecked(_currentCS.X + xOffset1);
        p0.Y = unchecked(_currentCS.Y + yOffset1);
        p1.X = unchecked(x1 + xOffset1);
        p1.Y = unchecked(y1 + yOffset1);

        // note: preserve angle of final segment by using offset3 at both ends
        p2.X = unchecked(x2 + xOffset3);
        p2.Y = unchecked(y2 + yOffset3);
        p3.X = unchecked(x3 + xOffset3);
        p3.Y = unchecked(y3 + yOffset3);

        if (_moveIsPending)
        {
            // emit offset 1st point as MoveTo
            PushMove(p0);

            _moveIsPending = false;
            _pathIsOpen = true;

            _offsetStart1 = p1; // record second point
        }

        if (_elemIsQueued)
            PushPrevElem(_hintMap, ref p0, p1, false);

        // queue the current element with offset points
        _elemIsQueued = true;
        _prevElemOp = Cf2PathOp.CubeTo;
        _prevElemP0 = p0;
        _prevElemP1 = p1;
        _prevElemP2 = p2;
        _prevElemP3 = p3;

        // update current map
        if (_hintMask.IsNew)
            _hintMap.Build(_hStemHintArray, _vStemHintArray, _hintMask, _hintOriginY, false);

        _currentCS.X = x3; // pre-offset current point
        _currentCS.Y = y3;
    }

    /// <summary><c>cf2_glyphpath_closeOpenPath</c>.</summary>
    public void CloseOpenPath()
    {
        if (_pathIsOpen)
        {
            // A closing line in Character Space line is always generated below with `cf2_glyphPath_lineTo'.  It may be ignored later
            // if it turns out to be zero length in Device Space.
            _pathIsClosing = true;

            LineTo(_start.X, _start.Y);

            // empty the final element from the queue and close the path
            if (_elemIsQueued)
                PushPrevElem(_hintMap, ref _offsetStart0, _offsetStart1, true);

            // reset state machine
            _moveIsPending = true;
            _pathIsOpen = false;
            _pathIsClosing = false;
            _elemIsQueued = false;
        }
    }

    /// <summary>The hint moves array, which the counter-mask hint map of the interpreter shares (<c>glyphpath->hintMoves</c>).</summary>
    public Cf2ArrStack<Cf2HintMove> HintMoves => _hintMoves;

    /// <summary>The initial hint map (<c>glyphpath->initialHintMap</c>), which the counter-mask hint map of the interpreter shares.</summary>
    public Cf2HintMap InitialHintMap => _initialHintMap;
}
