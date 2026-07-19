// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Utility class for traversing DOM structure and execution stuff on it.
    /// </summary>
    internal sealed class DomUtils
    {
        /// <summary>
        /// Check if the given location is inside the given box deep.<br/>
        /// Check inner boxes and all lines that the given box spans to.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <param name="location">the location to check</param>
        /// <returns>true - location inside the box, false - otherwise</returns>
        public static bool IsInBox(CssBox box, RPoint location)
        {
            foreach (var line in box.Rectangles)
            {
                if (line.Value.Contains(location))
                    return true;
            }

            foreach (var childBox in box.Boxes)
            {
                if (IsInBox(childBox, location))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if the given box contains only inline child boxes.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - only inline child boxes, false - otherwise</returns>
        public static bool ContainsInlinesOnly(CssBox box)
        {
            return box.Boxes.All(b => b.IsInline);
        }

        /// <summary>
        /// Walks up from <paramref name="box"/> (inclusive) looking for the nearest ancestor with the
        /// given HTML tag name, and returns that ancestor's parent - i.e. "where parsing should
        /// resume after closing this tag". Returns <c>null</c>, rather than <paramref name="root"/>,
        /// when no matching ancestor exists at all: a closing tag with no corresponding open element
        /// (e.g. a stray <c>&lt;/p&gt;</c> for a <c>&lt;p&gt;</c> already auto-closed by a nested
        /// <c>&lt;table&gt;</c>, per CSS2.1/HTML4's "table closes p" rule) is a parse error that must be
        /// ignored - see <see cref="Parse.HtmlParser.CloseElement"/>, whose caller falls back to leaving
        /// the current box unchanged rather than corrupting the tree by jumping to <paramref name="root"/>.
        /// </summary>
        /// <param name="root"></param>
        /// <param name="tagName"></param>
        /// <param name="box"></param>
        public static CssBox? FindParent(CssBox root, string tagName, CssBox? box)
        {
            while (true)
            {
                if (box is null)
                {
                    return null;
                }

                if (box.HtmlTag != null && box.HtmlTag.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return box.ParentBox ?? root;
                }

                box = box.ParentBox;
            }
        }

        /// <summary>
        /// Gets the previous sibling of this box.
        /// </summary>
        /// <returns>Box before this one on the tree. Null if it is the first</returns>
        public static CssBox? GetPreviousSibling(CssBox b, bool includeFloats = true)
        {
            if (b.ParentBox == null) return null;

            var index = b.ParentBox.Boxes.IndexOf(b);
            if (index <= 0) return null;
            var diff = 1;
            var sib = b.ParentBox.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed || (!includeFloats && sib.IsFloated)) && index - diff - 1 >= 0)
            {
                sib = b.ParentBox.Boxes[index - ++diff];
            }

            sib = sib.Display == CssConstants.None || sib.Position == CssConstants.Fixed || (!includeFloats && sib.IsFloated) ? null : sib;

            return sib;
        }

        /// <summary>
        /// Collects the maximal run of preceding in-flow siblings chained to <paramref name="box"/> by
        /// break avoidance (css-break §3.1, class A break points): for each consecutive pair, the earlier
        /// sibling's break-after is <c>avoid</c> or the later sibling's break-before is <c>avoid</c>.
        /// Returned in top-to-bottom document order; empty when no avoid chain exists. Callers use this
        /// to pull e.g. an <c>h2 { break-after: avoid }</c> heading (the UA default for h1-h6 under
        /// @media print) along whenever they move <paramref name="box"/> to the next page.
        /// </summary>
        public static List<CssBox> GetPrecedingKeepWithNextRun(CssBox box)
        {
            var run = new List<CssBox>();
            var current = box;

            while (true)
            {
                var prev = GetPreviousSibling(current, false);

                if (prev is null)
                    break;

                // css-break §5.2: a forced break value on either side of the pair takes precedence
                // over a break-avoidance value on the other - such a pair is never kept together.
                if (prev.BreakAfter is CssConstants.Page or CssConstants.Always
                    || current.BreakBefore is CssConstants.Page or CssConstants.Always)
                    break;

                if (prev.BreakAfter is not CssConstants.Avoid && current.BreakBefore is not CssConstants.Avoid)
                    break;

                run.Insert(0, prev);
                current = prev;
            }

            return run;
        }

        public static IEnumerable<CssBox> GetFollowingSiblings(CssBox box, Predicate<CssBox> matcher, bool isConsecutive)
        {
            if (box.ParentBox == null) yield break;

            var index = box.ParentBox.Boxes.IndexOf(box);

            const int diff = 1;

            while (box.ParentBox.Boxes.Count > index + diff)
            {
                var sib = box.ParentBox.Boxes[index + diff];

                if (matcher.Invoke(sib))
                {
                    yield return sib;
                }
                else if (isConsecutive)
                {
                    yield break;
                }

                index += diff;
            }
        }

        /// <summary>
        /// Gets the previous sibling of this box.
        /// </summary>
        /// <returns>Box before this one on the tree. Null if its the first</returns>
        public static CssBox? GetPreviousContainingBlockSibling(CssBox b)
        {
            var conBlock = b;
            var index = conBlock.ParentBox!.Boxes.IndexOf(conBlock);
            while (conBlock.ParentBox != null && index < 1 && conBlock.Display != CssConstants.Block && conBlock.Display != CssConstants.Table && conBlock.Display != CssConstants.TableCell && conBlock.Display != CssConstants.ListItem)
            {
                conBlock = conBlock.ParentBox;
                index = conBlock.ParentBox != null ? conBlock.ParentBox.Boxes.IndexOf(conBlock) : -1;
            }
            conBlock = conBlock.ParentBox;

            if (conBlock == null || index <= 0) return null;
            var diff = 1;
            var sib = conBlock.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed) && index - diff - 1 >= 0)
            {
                sib = conBlock.Boxes[index - ++diff];
            }

            return sib.Display == CssConstants.None ? null : sib;
        }

        /// <summary>
        /// fix word space for first word in inline tag.
        /// </summary>
        /// <param name="box">the box to check</param>
        public static bool IsBoxHasWhitespace(CssBox box)
        {
            if (box.Words[0].IsImage || !box.Words[0].HasSpaceBefore || !box.IsInline) return false;

            var sib = GetPreviousContainingBlockSibling(box);

            return sib is { IsInline: true };
        }

        /// <summary>
        /// Get css box under the given sub-tree at the given x,y location, get the inner most.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box by</param>
        /// <param name="visible">Optional: if to get only visible boxes (default - true)</param>
        /// <returns>css link box if exists or null</returns>
        public static CssBox? GetCssBox(CssBox? box, RPoint location, bool visible = true)
        {
            if (box == null) return null;

            if ((visible && box.Visibility != CssConstants.Visible) ||
                (!box.Bounds.IsEmpty && !box.Bounds.Contains(location))) return null;

            foreach (var childBox in box.Boxes)
            {
                if (CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds).Contains(location))
                {
                    return GetCssBox(childBox, location) ?? childBox;
                }
            }

            return null;
        }

        /// <summary>
        /// Collect all link boxes found in the HTML tree.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="linkBoxes">collection to add all link boxes to</param>
        public static void GetAllLinkBoxes(CssBox? box, List<CssBox> linkBoxes)
        {
            switch (box)
            {
                case null:
                    return;
                case { IsClickable: true, Visibility: CssConstants.Visible }:
                    linkBoxes.Add(box);
                    break;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllLinkBoxes(childBox, linkBoxes);
            }
        }

        /// <summary>
        /// Collect every SVG-sourced link candidate (from <c>&lt;a&gt;</c> elements inside an inline
        /// <c>&lt;svg&gt;</c> or a standalone <c>&lt;img src="x.svg"&gt;</c>) found anywhere in the
        /// HTML tree, already resolved to page-space rectangles via <see cref="SvgRenderer.CollectLinks"/>.
        /// A <see cref="CssBoxSvg"/>/<see cref="CssBoxImage"/> is a leaf as far as this walk is
        /// concerned - its own descendant boxes (if any) aren't ordinary HTML content, so recursion
        /// stops there rather than continuing into <c>box.Boxes</c>.
        /// </summary>
        public static void GetAllSvgLinks(CssBox? box, List<(RRect Rect, string Href)> linkBoxes)
        {
            switch (box)
            {
                case null:
                    return;

                case CssBoxSvg svgBox:
                    if (svgBox.GetLinkSource() is { } svgSource)
                        SvgRenderer.CollectLinks(svgSource.Document, svgSource.Rect, linkBoxes);
                    return;

                case CssBoxImage imageBox:
                    if (imageBox.GetLinkSource() is { } imageSource)
                        SvgRenderer.CollectLinks(imageSource.Document, imageSource.Rect, linkBoxes);
                    return;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllSvgLinks(childBox, linkBoxes);
            }
        }

        /// <summary>
        /// Get css link box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box by</param>
        /// <returns>css link box if exists or null</returns>
        public static CssBox? GetLinkBox(CssBox? box, RPoint location)
        {
            switch (box)
            {
                case null:
                    return null;
                case { IsClickable: true, Visibility: CssConstants.Visible } when IsInBox(box, location):
                    return box;
            }

            if (!box.ClientRectangle.IsEmpty && !box.ClientRectangle.Contains(location)) return null;

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetLinkBox(childBox, location);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Get css box under the given sub-tree with the given id.<br/>
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="id">the id to find the box by</param>
        /// <returns>css box if exists or null</returns>
        public static CssBox? GetBoxById(CssBox? box, string? id)
        {
            if (box == null || string.IsNullOrEmpty(id)) return null;

            if (box.HtmlTag != null && id.Equals(box.HtmlTag.TryGetAttribute("id"), StringComparison.OrdinalIgnoreCase))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetBoxById(childBox, id);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Gets css box under the given subtree with the given tag name
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="tagName">the tag name to find the box by</param>
        /// <returns>css box if exists or null</returns>
        public static CssBox? GetBoxByTagName(CssBox? box, string? tagName)
        {
            if (box == null || string.IsNullOrEmpty(tagName)) return null;

            if (box.HtmlTag is not null && box.HtmlTag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetBoxByTagName(childBox, tagName);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Get css line box under the given sub-tree at the given y location or the nearest line from the top.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssLineBox? GetCssLineBox(CssBox? box, RPoint location)
        {
            CssLineBox? line = null;
            if (box != null)
            {
                if (box.LineBoxes.Count > 0)
                {
                    if (box.HtmlTag is not { Name: "td" } || box.Bounds.Contains(location))
                    {
                        foreach (var lineBox in box.LineBoxes)
                        {
                            foreach (var rect in lineBox.Rectangles)
                            {
                                if (rect.Value.Top <= location.Y)
                                {
                                    line = lineBox;
                                }

                                if (rect.Value.Top > location.Y)
                                {
                                    return line;
                                }
                            }
                        }
                    }
                }

                foreach (var childBox in box.Boxes)
                {
                    line = GetCssLineBox(childBox, location) ?? line;
                }
            }

            return line;
        }

        /// <summary>
        /// Get css word box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssRect? GetCssBoxWord(CssBox? box, RPoint location)
        {
            if (box is not { Visibility: CssConstants.Visible }) return null;

            if (box.LineBoxes.Count > 0)
            {
                foreach (var lineBox in box.LineBoxes)
                {
                    var wordBox = GetCssBoxWord(lineBox, location);
                    if (wordBox != null)
                        return wordBox;
                }
            }

            if (!box.ClientRectangle.IsEmpty && !box.ClientRectangle.Contains(location)) return null;

            foreach (var childBox in box.Boxes)
            {
                var foundWord = GetCssBoxWord(childBox, location);
                if (foundWord != null)
                {
                    return foundWord;
                }
            }

            return null;
        }

        /// <summary>
        /// Get css word box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="lineBox">the line box to search in</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssRect? GetCssBoxWord(CssLineBox lineBox, RPoint location)
        {
            foreach (var rects in lineBox.Rectangles)
            {
                foreach (var word in rects.Key.Words)
                {
                    // add word spacing to word width so sentence won't have hols in it when moving the mouse
                    var rect = word.Rectangle;
                    rect.Width += word.OwnerBox.ActualWordSpacing;
                    if (rect.Contains(location))
                    {
                        return word;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// This returns the nearest positioned ancestor, or the root if none is found
        /// </summary>
        /// <param name="box">The box to use for locating</param>
        /// <returns>the nearest positioned ancestor, or the root if none is found</returns>
        public static CssBox GetNearestPositionedAncestor(CssBox box)
        {
            var currentBox = box;

            do
            {
                currentBox = currentBox.ParentBox;
            } while (currentBox is { IsPositioned: false, ParentBox: not null });

            return currentBox!;
        }

        public static CssBox? GetFirstIntersectingFloatBox(CssBox reference, CssFloatCoordinates coordinates, string floatProp)
        {
            // Walking up to the root and re-scanning every preceding sibling's whole subtree below is
            // O(document size) per call; for the very common case of a document with no floated boxes
            // at all, skip it entirely rather than pay that cost for a lookup that can never succeed.
            if (reference.HtmlContainer?.HasFloatedBoxes != true)
            {
                return null;
            }

            while (true)
            {
                if (reference.ParentBox is null)
                {
                    return null;
                }

                var currentBoxIdx = reference.ParentBox.Boxes.IndexOf(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    var next = GetNextIntersectingFloatBox(reference.ParentBox.Boxes[i], coordinates, floatProp);

                    if (next is not null)
                    {
                        return next;
                    }
                }

                reference = reference.ParentBox;
            }
        }

        public static CssBox? GetLastLeftIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates)
        {
            var left = coordinates.CurrentX;
            CssBox? lastIntersectingFloat = null;

            // Bounded by a flat iteration count: the number of distinct floats in a real document is
            // always finite, and this loop's only job is to walk past each one once. Without this cap,
            // a Y-row where a float's own "ActualRight + its margin" doesn't advance "left" strictly
            // past the previously found float (e.g. a wider float re-found immediately after moving just
            // past a narrower/nested one at nearly the same position) can spin - this loop's termination
            // previously relied entirely on eventually running out of intersecting floats to find, with
            // no fallback if that assumption doesn't hold.
            var iterations = 0;
            while (iterations++ < 10000)
            {
                CssFloatCoordinates floatCoordinates = new()
                {
                    Left = left,
                    Top = coordinates.CurrentY,
                    MarginLeft = box.ActualMarginLeft,
                    MarginRight = box.ActualMarginRight,
                    MaxBottom = coordinates.MaxBottom,
                    ReferenceWidth = 0,
                    Right = coordinates.MaxRight
                };

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, CssConstants.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;
            }

            return lastIntersectingFloat;
        }

        public static CssBox? GetLastRightIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates, double referenceWidth)
        {
            var left = coordinates.CurrentX;
            CssBox? lastIntersectingFloat = null;

            // See the matching bound in GetLastLeftIntersectingFloatBox above for why this is needed.
            var iterations = 0;
            while (iterations++ < 10000)
            {
                CssFloatCoordinates floatCoordinates = new()
                {
                    Left = left,
                    Top = coordinates.CurrentY,
                    MarginLeft = box.ActualMarginLeft,
                    MarginRight = box.ActualMarginRight,
                    MaxBottom = coordinates.MaxBottom,
                    ReferenceWidth = referenceWidth,
                    Right = left + referenceWidth
                };

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, CssConstants.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;
            }

            return lastIntersectingFloat;
        }

        public static CssBox? GetNearestParentElementBox(CssBox box)
        {
            var parentBox = box.ParentBox;

            while (parentBox is not null)
            {
                if (parentBox.HtmlTag is not null)
                {
                    return parentBox;
                }

                parentBox = parentBox.ParentBox;
            }

            return null;
        }

        // One box to paint as part of a stacking context's own layer ordering, plus the chain of DOM
        // ancestor boxes (outer to inner, between the claiming stacking context and Box itself) that
        // Box was hoisted past. Empty for a direct plain child - it paints via ordinary nested Paint()
        // recursion, so its ancestors' own overflow clipping is already correctly active on the
        // graphics clip stack from their own (still-running) Paint() calls. Non-empty for a hoisted
        // participant - it paints via the claiming stacking context's own paint loop instead, bypassing
        // those ancestors' Paint() calls entirely, so their overflow clipping must be reapplied
        // explicitly (see RenderUtils.PushAncestorOverflowClips) around its own Paint() call.
        internal readonly record struct StackingParticipant(CssBox Box, IReadOnlyList<CssBox> ClipAncestors);

        public static IEnumerable<StackingParticipant> FlattenStackingContext(CssBox box)
        {
            // Plain in-flow, non-stacking-context children always paint here, nested normally - this is
            // what keeps this box's own overflow-clip scope (pushed/popped around this same children
            // loop in CssBox.PaintImpCore) wrapped around them, and their own further plain descendants
            // are handled the same way, recursively, by their own subsequent Paint() call.
            foreach (var childBox in box.Boxes)
            {
                // ::marker boxes (inside or outside position) are always painted via one explicit
                // Paint(g) call from CssBox.PaintImpCore/PaintListItem, not discovered generically here
                // - both so the tagged-PDF path can wrap the marker in its own "/Lbl" structure element
                // separately from the rest of the list item's "/LBody" content, and so an "outside"
                // marker (which must not affect - or be discovered through - the owning list item's own
                // stacking context, per CSS2.1 12.5.1 / CSS Lists Level 3) never gets bubbled up as if
                // it were normal in-flow content. Yielding it here too would double-paint it.
                if (childBox.IsMarkerPseudoElement) continue;

                if (!NeedsStackingHoist(childBox))
                {
                    yield return new StackingParticipant(childBox, []);
                }
            }

            // The nearest enclosing "local ordering scope" (see IsLocalOrderingScope) is responsible for
            // finding and ordering every out-of-flow / stacking-context-establishing descendant reachable
            // through plain wrapper boxes AND plain floats, at any depth - a box that is neither claims
            // nothing further here; any such descendants of its own are claimed by whichever ancestor
            // above it actually qualifies. This is what fixes three bugs in earlier versions of this
            // method: (1) a box that itself establishes a stacking context (e.g. position:relative;
            // z-index, or now also opacity<1/transform) was never yielded by its own parent at all, so it
            // and its whole subtree never painted; (2) an out-of-flow stacking-context descendant nested a
            // few plain wrapper boxes deep was discovered "naturally" via the ordinary parent-to-child
            // Paint() cascade before its true ancestor stacking context ever reached its own z-index
            // layer, so it visually painted as if z-index had no effect; (3) a box that is positioned
            // (absolute/relative/fixed/sticky) but establishes no NEW stacking context of its own
            // (z-index:auto) never searched its own subtree either, so its own floated/positioned-without-
            // z-index children (Appendix E's "non-positioned floats" and "positioned descendants with
            // stack level 0") escaped all the way to the nearest TRUE stacking context ancestor instead of
            // being ordered locally against their true DOM siblings - see IsLocalOrderingScope.
            if (!IsLocalOrderingScope(box)) yield break;
            if (!(box.HtmlContainer?.HasStackingHoistCandidates ?? true)) yield break;

            foreach (var participant in SearchForHoistableDescendants(box, []))
            {
                yield return participant;
            }
        }

        // A box needs to escape its immediate DOM position to compete for z-order at the nearest
        // enclosing stacking context, rather than paint nested within its immediate parent: either
        // because it's out-of-flow (floated/absolute/fixed - always subject to z-ordering against its
        // nearest positioned/stacking ancestor, not its DOM parent), or because it establishes its own
        // stacking context (which must be ordered as one atomic unit among its true siblings, not
        // wherever it happens to sit in a plain wrapper's local scope). Internal (not private) so
        // HtmlContainerInt's HasStackingHoistCandidates computation can reuse the exact same predicate
        // rather than duplicating it.
        internal static bool NeedsStackingHoist(CssBox box) => box.IsOutOfFlow || IsStackingContextBox(box);

        // A box claims local Appendix-E ordering responsibility for its own PLAIN FLOAT descendants
        // (rather than deferring them to a more distant ancestor) if it is the root, a genuine stacking
        // context, OR merely positioned (absolute/relative/fixed/sticky) regardless of z-index - matching
        // Appendix E step 6's "positioned descendants with stack level 0 [...] painted via the same
        // [7-step] procedure" model, under which every positioned box (not only ones with an explicit
        // z-index) is its own atomic recursive unit for steps 3/4/5 (block/float/inline). This does NOT
        // extend to genuine stacking-context descendants nested inside a merely-positioned (z-index:auto)
        // box - those must keep escaping all the way to the true nearest stacking context, exactly like
        // through a plain non-positioned wrapper, because z-index competition only happens at a REAL
        // stacking context's own level (see the claimFloatsHere parameter on
        // SearchForHoistableDescendants, which encodes this float-vs-stacking-context distinction - a
        // merely-positioned box is a local ordering boundary for floats only, not for z-index).
        //
        // Without the float half of this, a positioned-but-z-index:auto box's own float child (Acid2's
        // own ".eyes" - position:absolute, no z-index - containing float "#eyes-b" alongside block
        // "#eyes-c" and inline "#eyes-a") was hoisted all the way to the true root's own stacking pass
        // instead of being ordered locally against its true DOM siblings, painting relative to the root's
        // entire subtree instead of interleaved correctly within ".eyes" itself.
        private static bool IsLocalOrderingScope(CssBox box) =>
            box.IsRoot || IsStackingContextBox(box) ||
            box.Position is CssConstants.Absolute or CssConstants.Relative or CssConstants.Fixed or CssConstants.Sticky;

        // Tunnels through plain wrapper boxes looking for content that needs to compete at `box`'s own
        // local ordering scope. Two categories of content are hoisted here, with different stopping
        // rules:
        //
        // - A genuine stacking context (IsStackingContextBox) always keeps escaping through anything
        //   that ISN'T ITSELF a stacking context - including a merely-positioned (z-index:auto) box -
        //   because z-index only has meaning relative to the nearest REAL stacking context. Recursion
        //   stops at (but includes) each stacking context found; its own subtree is its own business,
        //   resolved independently once its own Paint() call later invokes FlattenStackingContext on
        //   itself.
        // - A plain FLOAT only escapes as far as the nearest box that IsLocalOrderingScope (root, a
        //   genuine stacking context, or merely positioned) - once the walk has passed through such a
        //   box, `claimFloatsHere` flips to false for everything beneath it, since that box will find
        //   and locally order its own floats itself (via its own later FlattenStackingContext call,
        //   whose initial bail check now also accepts merely-positioned boxes - see IsLocalOrderingScope)
        //   rather than this outer search claiming them too, which would both double-paint them and
        //   order them relative to the wrong (too-distant) box's siblings.
        //
        // `ancestorPath` accumulates every DOM ancestor walked through along the way (both plain
        // pass-through wrappers and hoisted-but-not-yet-fully-resolved boxes like a merely-positioned
        // box) - each yielded participant snapshots it as its ClipAncestors, so the caller can re-apply
        // those ancestors' own overflow clipping (which it never picks up naturally, having been hoisted
        // past their own Paint() calls). Mutating one shared list via add-before-recurse/remove-after is
        // safe here: the whole sequence is drained eagerly and synchronously by FlattenStackingContext's
        // caller before anything else touches it.
        private static IEnumerable<StackingParticipant> SearchForHoistableDescendants(
            CssBox box, List<CssBox> ancestorPath, bool claimFloatsHere = true)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.IsMarkerPseudoElement) continue;

                var isStackingContext = IsStackingContextBox(childBox);
                var isLocalOrderingScope = !isStackingContext && IsLocalOrderingScope(childBox);
                var isPlainFloatToClaim = claimFloatsHere && !isStackingContext && !isLocalOrderingScope && childBox.IsOutOfFlow;

                if (isStackingContext || isLocalOrderingScope || isPlainFloatToClaim)
                {
                    yield return new StackingParticipant(childBox, ancestorPath.ToArray());
                    if (isStackingContext) continue;
                }

                // Once the walk passes through a merely-positioned (non-stacking-context) box, any
                // further floats beneath it belong to THAT box's own local claim, not this search's -
                // only genuine stacking contexts still need to keep escaping past it.
                var claimBeyond = isLocalOrderingScope ? false : claimFloatsHere;

                ancestorPath.Add(childBox);
                foreach (var descendant in SearchForHoistableDescendants(childBox, ancestorPath, claimBeyond))
                {
                    yield return descendant;
                }
                ancestorPath.RemoveAt(ancestorPath.Count - 1);
            }
        }

        public static bool IsStackingContextBox(CssBox box)
        {
            if (box.IsRoot)
            {
                return true;
            }

            if (box.Position is CssConstants.Absolute or CssConstants.Relative && box.ZIndex is not CssConstants.Auto)
            {
                return true;
            }

            if (box.Position is CssConstants.Fixed or CssConstants.Sticky)
            {
                return true;
            }

            // Flex item with a z-index other than auto establishes a stacking context even without a
            // `position` value of its own (CSS Flexible Box Layout §z-order), unlike a plain block/
            // inline child, which needs position:relative/absolute for z-index to have any effect at all.
            if (box.ZIndex is not CssConstants.Auto &&
                box.ParentBox?.Display is CssConstants.Flex or CssConstants.InlineFlex)
            {
                return true;
            }

            // Opacity less than 1 and any non-identity transform each establish a stacking context per
            // spec, regardless of `position` - both are already rendered as isolated, self-contained
            // units (an offscreen composited group for opacity; a pushed/popped matrix for transform), so
            // painting their descendants as one atomic block here matches what already happens visually.
            if (!box.IsOpaque)
            {
                return true;
            }

            if (box.IsTransformed)
            {
                return true;
            }

            return false;
        }

        public static IEnumerable<List<StackingParticipant>> GetBoxesByLayers(IEnumerable<StackingParticipant> participants)
        {
            var boxesByLayer = new Dictionary<int, List<StackingParticipant>>();

            foreach (var participant in participants)
            {
                var zIndex = 0;

                if (participant.Box.ZIndex is not CssConstants.Auto)
                {
                    zIndex = int.Parse(participant.Box.ZIndex);
                }

                if (!boxesByLayer.ContainsKey(zIndex))
                {
                    boxesByLayer[zIndex] = [];
                }

                boxesByLayer[zIndex].Add(participant);
            }

            return boxesByLayer.OrderBy(x => x.Key).Select(x => x.Value);
        }

        public static bool IsProperTableChild(CssBox box)
        {
            return box.IsTableRowGroupBox || box.Display is CssConstants.TableRow ||
                   box.Display is CssConstants.TableColumn || box.Display is CssConstants.TableColumnGroup ||
                   box.Display is CssConstants.TableCaption;
        }

        /// <summary>
        /// Collects the disjoint, sorted Y-ranges of the document that contain "real" printable
        /// content - per CSS Paged Media Level 3 §3.2's definition of a content-empty page ("a page
        /// box whose page area contains no printable content other than backgrounds and/or borders"),
        /// used by <see cref="HtmlContainerInt.GetPaginationSlots"/> to avoid materializing PDF pages
        /// that would only ever show a huge, purely-decorative margin gap (e.g. Acid2's own "100em"
        /// margins on "#top"/".picture", intentionally meant to be scrolled off-screen in a real,
        /// single-viewport browser - a mechanic a paginated PDF has no equivalent for otherwise).
        /// </summary>
        /// <param name="root">the root box of the laid-out document</param>
        /// <returns>a sorted, non-overlapping list of (top, bottom) ranges, in the same raw
        /// <see cref="CssBoxProperties.Location"/>/<see cref="CssBoxProperties.ActualBottom"/>
        /// coordinate space layout already uses</returns>
        public static List<(double Top, double Bottom)> CollectPrintableContentRanges(CssBox root)
        {
            var ranges = new List<(double Top, double Bottom)>();
            CollectPrintableContentRangesInto(root, ranges);
            ranges.Sort((a, b) => a.Top.CompareTo(b.Top));

            var merged = new List<(double Top, double Bottom)>();
            foreach (var range in ranges)
            {
                if (merged.Count > 0 && range.Top <= merged[^1].Bottom)
                {
                    if (range.Bottom > merged[^1].Bottom)
                        merged[^1] = (merged[^1].Top, range.Bottom);
                }
                else
                {
                    merged.Add(range);
                }
            }

            return merged;
        }

        private static void CollectPrintableContentRangesInto(CssBox box, List<(double Top, double Bottom)> ranges)
        {
            // Fixed-position content ignores the page's scroll offset and repeats identically on
            // every generated page (see CssBox.Paint/PaintImpCore's "IsFixed" branches) - if it were
            // allowed to count as "real" content here, every page-slot (including the huge margin
            // gaps this method exists to detect) would look non-empty, defeating the whole mechanism.
            // Mirrors the same exclusion CssBox.PerformLayoutImp already applies when growing
            // HtmlContainer.ActualSize.
            if (box.IsFixed || box.Display == CssConstants.None) return;

            if (HasOwnPrintableContent(box))
            {
                // A box's own Location/ActualBottom only reflect its true page-relative geometry for
                // block-level boxes - an inline (e.g. plain text run) box's real per-line position
                // lives entirely in its own Rectangles (one rect per line it spans, exactly what
                // CssBox.PaintImpCore itself paints from), while Location/ActualBottom stay at
                // whatever default/local value layout happened to leave them at. Falling back to
                // Location/ActualBottom for a box with real Rectangles would (and did, before this
                // fix) report the same bogus, line-local range for every paragraph in a document,
                // merging genuinely separate pages' worth of real text into one indistinguishable
                // range and silently discarding real content pages.
                if (box.Rectangles.Count > 0)
                {
                    foreach (var rect in box.Rectangles.Values)
                    {
                        ranges.Add((rect.Top, rect.Bottom));
                    }
                }
                else
                {
                    ranges.Add((box.Bounds.Top, box.Bounds.Bottom));
                }
            }

            foreach (var childBox in box.Boxes)
            {
                CollectPrintableContentRangesInto(childBox, ranges);
            }
        }

        private static bool HasOwnPrintableContent(CssBox box)
        {
            // Generated content (::before/::after/::marker/::first-letter) always counts, per CSS
            // Paged Media Level 3 §3.2's own carve-out - this is what keeps Acid2's own
            // ".nose div div:before"/":after" (border-only, empty "content: ''") counted as real.
            if (box.IsPseudoElement) return true;

            if (box.Words.Count > 0) return true;

            if (box is CssBoxImage or CssBoxObject) return true;

            // Excludes whichever box PdfGenerator.ResolveCanvasBackground chose to paint as the
            // whole-page canvas fill (SuppressOwnBackgroundPaint) - without this exclusion, e.g.
            // "html { background: white }" would count as "content" across its own entire
            // auto-height span (which, for a root element, is the whole document), defeating the
            // gap-detection this method exists for entirely.
            if (!box.SuppressOwnBackgroundPaint && box.HasOwnBackground) return true;

            if (RenderUtils.IsColorVisible(box.ActualBorderTopColor) && box.ActualBorderTopWidth > 0) return true;
            if (RenderUtils.IsColorVisible(box.ActualBorderBottomColor) && box.ActualBorderBottomWidth > 0) return true;
            if (RenderUtils.IsColorVisible(box.ActualBorderLeftColor) && box.ActualBorderLeftWidth > 0) return true;
            if (RenderUtils.IsColorVisible(box.ActualBorderRightColor) && box.ActualBorderRightWidth > 0) return true;

            return false;
        }

        private static CssBox? GetNextIntersectingFloatBox(CssBox box, CssFloatCoordinates coordinates, string floatProp)
        {
            if (IsFloatIntersecting(coordinates, floatProp, box))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetNextIntersectingFloatBox(childBox, coordinates, floatProp);
                if (foundBox != null)
                {
                    return foundBox;
                }
            }

            return null;
        }

        private static bool IsFloatIntersecting(CssFloatCoordinates coordinates, string floatProp, CssBox targetBox)
        {
            if (!targetBox.IsFloated) return false;

            // vertical conflict
            if (!(coordinates.Top < targetBox.ActualBottom) || !(targetBox.Location.Y <= coordinates.Top)) return false;

            var targetRight = targetBox.ActualRight + targetBox.ActualMarginRight;
            var targetLeft = targetBox.Location.X - targetBox.ActualMarginLeft;

            var currentLeft = coordinates.Left - coordinates.MarginLeft;

            switch (floatProp)
            {
                case CssConstants.Left when targetRight > currentLeft && targetLeft <= currentLeft:
                case CssConstants.Right when targetLeft > coordinates.FloatRightStartX + coordinates.MarginLeft + coordinates.ReferenceWidth + coordinates.MarginRight:
                    return true;
                default:
                    return false;
            }
        }
    }
}