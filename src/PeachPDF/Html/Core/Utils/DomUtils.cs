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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// <remarks>
        /// Boxes that are not in the flow this walk is about are stepped over: a <c>display: none</c> box, an
        /// absolutely- or fixed-positioned one, optionally a float — and an <c>outside</c> <c>::marker</c>,
        /// which is positioned beside its list item's principal block box rather than among its children
        /// (CSS 2.1 §12.5.1). The marker is <c>Boxes[0]</c> of every list item, so for an item whose content
        /// is block-level it is the box the item's first real child would otherwise resolve its own top
        /// against: that child was placed at the marker's <c>ActualBottom</c>, which is 0 until the marker is
        /// positioned — after the children are — and the item laid out with no height at all. A table's
        /// grid decoration box (<see cref="CssBox.TableGridDecorationBox"/>, issue #721) is stepped over for
        /// the same shape of reason: it is a captioned table's own <c>Boxes[0]</c>, and its author-visible
        /// caption must see whatever preceded the table itself, not this synthetic paint-only box.
        /// </remarks>
        /// <returns>Box before this one on the tree. Null if it is the first</returns>
        public static CssBox? GetPreviousSibling(CssBox b, bool includeFloats = true)
        {
            if (b.ParentBox == null) return null;

            var index = b.ParentBox.Boxes.IndexOf(b);
            if (index <= 0) return null;
            var diff = 1;
            var sib = b.ParentBox.Boxes[index - diff];

            while ((sib.DerivedStyle.ActualDisplay == Keywords.None || sib.Position.Value == PositionMode.Absolute || sib.Position.Value == PositionMode.Fixed || sib.Position.Value == PositionMode.Running || (!includeFloats && sib.IsFloated) || CssBox.IsOutsideMarker(sib) || sib.IsTableGridDecorationBox) && index - diff - 1 >= 0)
            {
                sib = b.ParentBox.Boxes[index - ++diff];
            }

            sib = sib.DerivedStyle.ActualDisplay == Keywords.None || sib.Position.Value == PositionMode.Fixed || sib.Position.Value == PositionMode.Running || (!includeFloats && sib.IsFloated) || CssBox.IsOutsideMarker(sib) || sib.IsTableGridDecorationBox ? null : sib;

            return sib;
        }

        /// <summary>
        /// Collects the maximal run of preceding in-flow siblings chained to <paramref name="box"/> by
        /// break avoidance (css-break §3.1, class A break points): for each consecutive pair, the earlier
        /// sibling's break-after or the later sibling's break-before forbids a break in
        /// <paramref name="context"/> (<see cref="BreakValues.AvoidsBreak"/> — <c>avoid</c> in either,
        /// plus the value naming that context alone).
        /// Returned in top-to-bottom document order; empty when no avoid chain exists. Callers use this
        /// to pull e.g. an <c>h2 { break-after: avoid }</c> heading (the UA default for h1-h6 under
        /// @media print) along whenever they move <paramref name="box"/> to the next fragmentainer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The context is asked for rather than assumed, because the same chain says different things in
        /// each: <c>avoid-column</c> forbids nothing at a page boundary and <c>avoid-page</c> forbids
        /// nothing at a column one, and a forced break of the other context does not break the chain.
        /// </para>
        /// <para>
        /// What the two contexts do <i>not</i> share is how the run then travels. Every page-context
        /// caller <b>moves</b> the run to a lower coordinate; a column has none — every column of a
        /// container begins at the same block-axis coordinate — so a column-context caller states the
        /// break <i>before the head</i> instead and lets the next column's fill lay the run out there
        /// (<see cref="CssBox.FillFragmentainerWithBlockChildren"/>).
        /// </para>
        /// </remarks>
        public static List<CssBox> GetPrecedingKeepWithNextRun(CssBox box, FragmentationContext context)
        {
            var run = new List<CssBox>();
            var current = box;

            while (true)
            {
                // Siblings only, deliberately. The break point before a container's first in-flow child
                // is the container's own (§3.1), so the run "ought" to continue out through it - and it
                // does, but not by widening this walk: the members of a run are *moved*, and moving a
                // container's predecessor while the container itself stays put is not a state layout can
                // settle into (measured driving the pass driver into its own no-progress backstop). The
                // container has to travel too, so callers ask this about the *propagation anchor*
                // (BreakPropagation.AnchorForBreakBefore) - a box at the level the run's members live at -
                // rather than about the box that broke. See EarlyBreak.Discover.
                var prev = GetPreviousSibling(current, false);

                if (prev is null)
                    break;

                // css-break §3.1: a forced break value on either side of the pair takes precedence
                // over a break-avoidance value on the other - such a pair is never kept together. Both
                // sides are read through the chains they end and begin, for the same reason the break
                // point itself is (BreakPropagation).
                if (BreakPropagation.ForcedBreakAfterAt(prev, context) is not null
                    || BreakPropagation.ForcedBreakBeforeAt(current, context) is not null)
                {
                    break;
                }

                if (!BreakValues.AvoidsBreak(prev.BreakAfter, context)
                    && !BreakValues.AvoidsBreak(current.BreakBefore, context))
                {
                    break;
                }

                run.Insert(0, prev);
                current = prev;
            }

            return run;
        }

        /// <summary>
        /// The nearest in-flow box preceding <paramref name="box"/> in the flow, looking out through any
        /// containers <paramref name="box"/> begins. Null when nothing precedes it at all.
        /// </summary>
        /// <remarks>
        /// The same rule <c>CssBox.ForcedBreakTopFor</c> resolves a forced break's target with, and for the
        /// same reason: a first in-flow child has no predecessor of its own, but the break point before it
        /// is its container's, which does.
        /// </remarks>
        public static CssBox? PrecedingBoxAcrossFirstChildChain(CssBox box)
        {
            for (var origin = box; origin is not null; origin = origin.ParentBox)
            {
                if (GetPreviousSibling(origin, false) is { } predecessor) return predecessor;
            }

            return null;
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
            while (conBlock.ParentBox != null && index < 1 && conBlock.DerivedStyle.ActualDisplay != Keywords.Block && conBlock.DerivedStyle.ActualDisplay != Keywords.Table && conBlock.DerivedStyle.ActualDisplay != Keywords.TableCell && conBlock.DerivedStyle.ActualDisplay != Keywords.ListItem)
            {
                conBlock = conBlock.ParentBox;
                index = conBlock.ParentBox != null ? conBlock.ParentBox.Boxes.IndexOf(conBlock) : -1;
            }
            conBlock = conBlock.ParentBox;

            if (conBlock == null || index <= 0) return null;
            var diff = 1;
            var sib = conBlock.Boxes[index - diff];

            while ((sib.DerivedStyle.ActualDisplay == Keywords.None || sib.Position.Value == PositionMode.Absolute || sib.Position.Value == PositionMode.Fixed || sib.Position.Value == PositionMode.Running) && index - diff - 1 >= 0)
            {
                sib = conBlock.Boxes[index - ++diff];
            }

            return sib.DerivedStyle.ActualDisplay == Keywords.None ? null : sib;
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

            if ((visible && box.Visibility.Value != Visibility.Visible) ||
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
                case { IsClickable: true, Visibility.Value: Visibility.Visible }:
                    linkBoxes.Add(box);
                    break;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllLinkBoxes(childBox, linkBoxes);
            }
        }

        /// <summary>
        /// Same walk as <see cref="GetAllLinkBoxes"/>, additionally collecting every box whose resolved
        /// <c>bookmark-level</c> is not <c>none</c> into <paramref name="bookmarkBoxes"/> (in document
        /// order, for free, since this is a pre-order walk) - so <c>PdfGenerator</c> can build its PDF
        /// outline from a single full-tree traversal instead of a second, independent one. Link and
        /// bookmark candidacy are independent conditions; a box can be both (a linked heading).
        /// </summary>
        public static void GetAllLinkAndBookmarkBoxes(CssBox? box, List<CssBox> linkBoxes, List<CssBox> bookmarkBoxes)
        {
            switch (box)
            {
                case null:
                    return;
                case { IsClickable: true, Visibility.Value: Visibility.Visible }:
                    linkBoxes.Add(box);
                    break;
            }

            // BookmarkLevel is stored as its raw, already-validated none|<integer> string (see
            // bookmark-level's css-properties.json entry) - any non-"none" value is a candidate;
            // BookmarkOutlineBuilder.ResolveLevel does the actual int parse.
            if (box.BookmarkLevel is { Length: > 0 } level && level != Keywords.None)
            {
                bookmarkBoxes.Add(box);
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllLinkAndBookmarkBoxes(childBox, linkBoxes, bookmarkBoxes);
            }
        }

        /// <summary>
        /// Collect every visible &lt;input&gt;/&lt;select&gt; box in document order, for
        /// <c>PdfGenerator.HandleFormFields</c> to classify and place - the same shape as
        /// <see cref="GetAllLinkBoxes"/>, since both are post-layout geometry queries over the live
        /// box tree rather than paint (see CLAUDE.md's fragment-tree-is-the-paint-contract rule, which
        /// this is deliberately outside of, exactly like <see cref="GetAllLinkBoxes"/> already is).
        /// </summary>
        public static void GetAllFormFieldBoxes(CssBox? box, List<CssBox> fieldBoxes)
        {
            switch (box)
            {
                case null:
                    return;
                case CssBoxFormField { Visibility.Value: Visibility.Visible }:
                    fieldBoxes.Add(box);
                    break;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllFormFieldBoxes(childBox, fieldBoxes);
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
                case { IsClickable: true, Visibility.Value: Visibility.Visible } when IsInBox(box, location):
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
        /// Builds an id -&gt; box index for <paramref name="root"/>'s whole subtree in one walk, for
        /// callers (<c>target-counter()</c>/<c>target-text()</c> resolution) that look up many ids across
        /// one document rather than the single id <see cref="GetBoxById"/> is built for - repeating that
        /// method's own uncached O(n) walk once per lookup would be O(n*m) for a table of contents with m
        /// entries. First occurrence wins on a duplicate id, matching <see cref="GetBoxById"/>'s own
        /// pre-order short-circuit.
        /// </summary>
        internal static Dictionary<string, CssBox> BuildIdIndex(CssBox root)
        {
            var index = new Dictionary<string, CssBox>(StringComparer.OrdinalIgnoreCase);
            CollectIds(root, index);
            return index;
        }

        private static void CollectIds(CssBox box, Dictionary<string, CssBox> index)
        {
            var id = box.HtmlTag?.TryGetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                index.TryAdd(id, box);
            }

            foreach (var childBox in box.Boxes)
            {
                CollectIds(childBox, index);
            }
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
        /// This returns the nearest positioned ancestor, or the root if none is found
        /// </summary>
        /// <param name="box">The box to use for locating</param>
        /// <returns>the nearest positioned ancestor, or the root if none is found</returns>
        public static CssBox GetNearestPositionedAncestor(CssBox box)
        {
            var currentBox = box;

            do
            {
                currentBox = currentBox.EffectiveParentBox;
            } while (currentBox is { IsPositioned: false, EffectiveParentBox: not null });

            return currentBox!;
        }

        /// <summary>
        /// Whether <paramref name="box"/> is <paramref name="root"/> itself or one of its descendants.
        /// </summary>
        /// <remarks>
        /// Used to ask whether a box a subtree-translating mover is about to skip (an out-of-flow
        /// descendant whose containing block lies outside the subtree being moved, see
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/437">#437</see>) is genuinely an
        /// outsider or is itself part of the subtree — a walk up from <paramref name="box"/> rather than
        /// down from <paramref name="root"/>, since a containing block is always an ancestor of the box it
        /// was resolved from.
        /// </remarks>
        internal static bool IsSelfOrDescendantOf(CssBox box, CssBox root)
        {
            for (var current = box; current is not null; current = current.EffectiveParentBox)
            {
                if (ReferenceEquals(current, root)) return true;
            }

            return false;
        }

        public static CssBox? GetFirstIntersectingFloatBox(CssBox reference, CssFloatCoordinates coordinates, Floating floatProp)
        {
            var container = reference.HtmlContainer;

            // Counted before the short-circuit below, so the count says how often layout asked the
            // question - which is what makes "the scan visited no boxes" evidence that the short-circuit
            // worked rather than evidence that nothing ever called it.
            container?.RecordFloatScanCall();

            // Walking up to the root and re-scanning every preceding sibling's whole subtree below is
            // O(document size) per call; for the very common case of a document with no floated boxes
            // at all, skip it entirely rather than pay that cost for a lookup that can never succeed.
            if (container?.HasFloatedBoxes != true)
            {
                return null;
            }

            var boxesVisited = 0;
            var found = FindIntersectingFloatBox(reference, coordinates, floatProp, ref boxesVisited);
            container.RecordFloatScanBoxVisits(boxesVisited);
            return found;
        }

        /// <summary>
        /// The walk itself: climb to the root, scanning each level's preceding siblings' subtrees.
        /// </summary>
        /// <param name="reference">The box the lookup starts from.</param>
        /// <param name="coordinates">The area a float has to intersect to be returned.</param>
        /// <param name="floatProp">Which side's floats to look for.</param>
        /// <param name="boxesVisited">Accumulator, not an input: every box the walk examines is added to it.</param>
        private static CssBox? FindIntersectingFloatBox(CssBox reference, CssFloatCoordinates coordinates, Floating floatProp, ref int boxesVisited)
        {
            while (true)
            {
                if (reference.ParentBox is null)
                {
                    return null;
                }

                var currentBoxIdx = reference.ParentBox.Boxes.IndexOf(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    var next = GetNextIntersectingFloatBox(reference.ParentBox.Boxes[i], coordinates, floatProp, ref boxesVisited);

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

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, Floating.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;
            }

            return lastIntersectingFloat;
        }

        /// <summary>
        /// A right float's constraint on a line is unlike a left float's: a left float caps where
        /// the cursor itself currently sits (a point-collision test, correct in
        /// <see cref="GetLastLeftIntersectingFloatBox"/> above), but a right float caps how far
        /// right the cursor is *allowed to reach in advance* - a lookahead, independent of the
        /// cursor's current position or the word being placed. Reusing the point-collision test
        /// here (as this method used to, by querying <see cref="GetFirstIntersectingFloatBox"/> in
        /// <see cref="Floating.Left"/> mode) can only detect the float once the cursor has already
        /// walked into its span, never before - so this scans for every float:right box whose
        /// vertical span covers the current row and returns the one with the smallest left edge,
        /// which is the actual binding constraint regardless of where the cursor is right now.
        /// </summary>
        public static CssBox? GetLastRightIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates)
        {
            var container = box.HtmlContainer;
            container?.RecordFloatScanCall();

            // See GetFirstIntersectingFloatBox above for why this short-circuit exists.
            if (container?.HasFloatedBoxes != true)
            {
                return null;
            }

            var boxesVisited = 0;
            var narrowest = FindNarrowestRightFloatBox(box, coordinates.CurrentY, ref boxesVisited);
            container.RecordFloatScanBoxVisits(boxesVisited);
            return narrowest;
        }

        /// <summary>
        /// Same ancestor/preceding-sibling traversal shape as <see cref="FindIntersectingFloatBox"/>,
        /// but accumulates the narrowest match across the whole walk instead of returning on the
        /// first hit - the wrap-limit query needs the binding constraint among every float:right box
        /// covering this row, not merely the first one the traversal order happens to reach.
        /// </summary>
        private static CssBox? FindNarrowestRightFloatBox(CssBox reference, double top, ref int boxesVisited)
        {
            CssBox? narrowest = null;
            var narrowestLeft = double.PositiveInfinity;

            while (reference.ParentBox is not null)
            {
                var currentBoxIdx = reference.ParentBox.Boxes.IndexOf(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    ScanForNarrowestRightFloatBox(reference.ParentBox.Boxes[i], top, ref narrowest, ref narrowestLeft, ref boxesVisited);
                }

                reference = reference.ParentBox;
            }

            return narrowest;
        }

        private static void ScanForNarrowestRightFloatBox(CssBox box, double top, ref CssBox? narrowest, ref double narrowestLeft, ref int boxesVisited)
        {
            boxesVisited++;

            if (box.Float.Value == Floating.Right && box.Location.Y <= top && top < box.ActualBottom)
            {
                var left = box.Location.X - box.ActualMarginLeft;

                if (left < narrowestLeft)
                {
                    narrowestLeft = left;
                    narrowest = box;
                }
            }

            foreach (var childBox in box.Boxes)
            {
                ScanForNarrowestRightFloatBox(childBox, top, ref narrowest, ref narrowestLeft, ref boxesVisited);
            }
        }

        /// <summary>
        /// The tightest inline-axis extent (a physical Y distance from <paramref name="columnInlineStart"/>,
        /// in the direction a vertical box's own column actually grows - downward when
        /// <paramref name="inlineStartIsBottom"/> is false, upward when true) that a floated sibling leaves
        /// available at block-axis (physical X) position <paramref name="columnBlockAxisPoint"/>, or
        /// <see langword="null"/> if no floated box's own physical-X span covers that point.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="FindNarrowestRightFloatBox"/>'s ancestor/preceding-sibling traversal shape,
        /// used from <c>CssLayoutEngine.CreateVerticalLineBoxes</c> once per column rather than once per
        /// word (a column's own block-axis position, unlike a horizontal line's right-float wrap boundary,
        /// never changes mid-column). <c>float: left</c>/<c>right</c> stay strictly physical under a
        /// vertical writing mode in this engine, matching real, current browser behavior (verified against
        /// MDN's dedicated logical-floating guide, whose live examples show <c>float: left</c> staying
        /// physical while <c>float: inline-start</c>/<c>inline-end</c> - a separate CSS Logical Properties
        /// Level 1 feature this engine doesn't parse - are the actual writing-mode-aware mechanism). CSS
        /// Writing Modes 4 §7.1 does name "floating" once, in passing, among features it says get
        /// reinterpreted via line-left/line-right - but that is the *only* mention of float/clear anywhere
        /// in the whole document, with no normative algorithm anywhere backing it, so it reads as an
        /// unfollowed-through aspiration rather than a rule real implementations honor. Given this engine
        /// parses no logical (<c>inline-start</c>/<c>inline-end</c>) float value in the first place, which
        /// side a float declared only ever decides where along the physical-X (this box's own block) axis
        /// it already sits; it plays no further role once that position is known, unlike the horizontal
        /// engine's own asymmetric left/right (point-collision vs. lookahead) treatment.
        /// </remarks>
        public static double? GetVerticalFloatConstraint(CssBox reference, double columnBlockAxisPoint,
            double columnInlineStart, bool inlineStartIsBottom)
        {
            var container = reference.HtmlContainer;
            container?.RecordFloatScanCall();

            // See GetFirstIntersectingFloatBox above for why this short-circuit exists.
            if (container?.HasFloatedBoxes != true)
            {
                return null;
            }

            var boxesVisited = 0;
            var tightest = FindTightestVerticalFloatConstraint(reference, columnBlockAxisPoint, columnInlineStart,
                inlineStartIsBottom, ref boxesVisited);
            container.RecordFloatScanBoxVisits(boxesVisited);
            return tightest;
        }

        private static double? FindTightestVerticalFloatConstraint(CssBox reference, double columnBlockAxisPoint,
            double columnInlineStart, bool inlineStartIsBottom, ref int boxesVisited)
        {
            double? tightest = null;

            while (reference.ParentBox is not null)
            {
                var currentBoxIdx = reference.ParentBox.Boxes.IndexOf(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    ScanForVerticalFloatConstraint(reference.ParentBox.Boxes[i], columnBlockAxisPoint,
                        columnInlineStart, inlineStartIsBottom, ref tightest, ref boxesVisited);
                }

                reference = reference.ParentBox;
            }

            return tightest;
        }

        private static void ScanForVerticalFloatConstraint(CssBox box, double columnBlockAxisPoint,
            double columnInlineStart, bool inlineStartIsBottom, ref double? tightest, ref int boxesVisited)
        {
            boxesVisited++;

            if (box.IsFloated)
            {
                var targetLeft = box.Location.X - box.ActualMarginLeft;
                var targetRight = box.ActualRight + box.ActualMarginRight;

                // Closed on both ends, unlike the horizontal engine's half-open point test: a column's own
                // block-axis point here names its leading (not-yet-consumed) edge, which is very commonly
                // exactly flush against a float's own edge (e.g. a plain float:right in vertical-rl sits at
                // the same physical-right edge column 0's own leading edge starts at) - a strict `<`/`>`
                // test would miss that touching case even though the column's real footprint, once it has
                // any thickness at all, provably overlaps the float from that shared edge inward.
                if (targetLeft <= columnBlockAxisPoint && columnBlockAxisPoint <= targetRight)
                {
                    // The float only constrains this column if its own inline-axis span actually reaches
                    // into the column's reachable range - a float whose extent lies entirely on the far
                    // side of columnInlineStart (already "passed", in the direction this column grows) has
                    // no bearing on it at all and must be skipped, not treated as a zero-room constraint.
                    // Mirrors ScanForNarrowestRightFloatBox's own vertical-conflict test (box.Location.Y
                    // &lt;= top &amp;&amp; top &lt; box.ActualBottom) one axis over.
                    var farEdge = inlineStartIsBottom
                        ? box.Location.Y - box.ActualMarginTop
                        : box.ActualBottom + box.ActualMarginBottom;
                    var isRelevant = inlineStartIsBottom ? farEdge < columnInlineStart : farEdge > columnInlineStart;

                    if (isRelevant)
                    {
                        // The float's own near edge along the column's inline-growth direction, clamped to
                        // 0 - a float that already covers the column's own inline-start leaves no usable
                        // extent.
                        var extent = inlineStartIsBottom
                            ? Math.Max(0, columnInlineStart - (box.ActualBottom + box.ActualMarginBottom))
                            : Math.Max(0, box.Location.Y - box.ActualMarginTop - columnInlineStart);

                        if (tightest is null || extent < tightest.Value)
                        {
                            tightest = extent;
                        }
                    }
                }
            }

            foreach (var childBox in box.Boxes)
            {
                ScanForVerticalFloatConstraint(childBox, columnBlockAxisPoint, columnInlineStart,
                    inlineStartIsBottom, ref tightest, ref boxesVisited);
            }
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

        // A box needs to escape its immediate DOM position to compete for z-order at the nearest
        // enclosing stacking context, rather than paint nested within its immediate parent: either
        // because it's out-of-flow (floated/absolute/fixed - always subject to z-ordering against its
        // nearest positioned/stacking ancestor, not its DOM parent), or because it establishes its own
        // stacking context (which must be ordered as one atomic unit among its true siblings, not
        // wherever it happens to sit in a plain wrapper's local scope). Internal (not private) so
        // HtmlContainerInt's HasStackingHoistCandidates computation can reuse the exact same predicate
        // rather than duplicating it, alongside Paint.StackingOrder, which owns the ordering walk.
        internal static bool NeedsStackingHoist(CssBox box) => box.IsOutOfFlow || IsStackingContextBox(box);

        /// <summary>
        /// Whether any box in <paramref name="box"/>'s subtree asks for its decorations to be cloned at a
        /// fragmentation break. Layout only has to make room for cloned borders and padding when something
        /// actually wants them, and the answer depends on cascaded style alone, so it is settled once before
        /// layout starts rather than re-derived per word.
        /// </summary>
        internal static bool AnyBoxClonesDecorations(CssBox box)
        {
            if (box.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone) return true;

            foreach (var child in box.Boxes)
            {
                if (AnyBoxClonesDecorations(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any box in <paramref name="box"/>'s subtree establishes a <c>@container</c> size query
        /// container (<c>container-type: size</c> or <c>inline-size</c> - <c>normal</c> doesn't count,
        /// since it establishes no size containment). Gates
        /// <see cref="HtmlContainerInt.PerformLayout"/>'s container-query convergence loop: the
        /// overwhelming majority of documents declare no size container at all, and for those the loop
        /// must cost nothing beyond this one tree walk (the same cost category as
        /// <see cref="AnyBoxClonesDecorations"/>, already run at the same point every pass).
        /// </summary>
        internal static bool AnyBoxEstablishesSizeContainer(CssBox box)
        {
            if (box.ContainerType.Value is PeachPDF.CSS.ContainerType.Size or PeachPDF.CSS.ContainerType.InlineSize) return true;

            foreach (var child in box.Boxes)
            {
                if (AnyBoxEstablishesSizeContainer(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any box in <paramref name="box"/>'s subtree resolved a <c>target-counter(_, page)</c>
        /// content token against a page map that did not exist yet (<see cref="CssBox.HasPendingTargetPageContent"/>,
        /// set by <c>CssContentEngine.AppendTargetCounter</c> during the DOM-construction-time content
        /// pass). Gates <see cref="HtmlContainerInt.PerformLayoutOnePass"/>'s target-page convergence
        /// loop the same way <see cref="AnyBoxClonesDecorations"/>/<see cref="AnyBoxEstablishesSizeContainer"/>
        /// gate their own loops - the overwhelming majority of documents use neither, and for those the
        /// loop must cost nothing beyond this one tree walk.
        /// </summary>
        internal static bool AnyBoxHasTargetPageContent(CssBox box)
        {
            if (box.HasPendingTargetPageContent) return true;

            foreach (var child in box.Boxes)
            {
                if (AnyBoxHasTargetPageContent(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// The block-start border and padding that <c>box-decoration-break: clone</c> re-inserts when a
        /// fragmentation break lands inside a box
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>): each
        /// fragment is wrapped independently, so the one starting in the new fragmentainer opens with its own
        /// border and padding, and content has to start below them. Summed over <paramref name="box"/> and
        /// every ancestor up to <paramref name="stopAt"/>, since a break inside a nested box breaks all of
        /// them at once.
        /// <para>
        /// Margin is excluded, unlike on the inline axis (<see cref="ClonedInlineStart"/>): a margin adjoining an
        /// unforced break is truncated to zero by
        /// <see href="https://www.w3.org/TR/css-break-3/#break-margins">§5.2</see>, so at a page break there is
        /// no margin left for §6.2 to clone.
        /// </para>
        /// </summary>
        /// <param name="box">The box whose content resumes in the new fragmentainer.</param>
        /// <param name="stopAt">
        /// The box establishing the fragmentation context the break falls in, excluded from the sum along
        /// with everything above it, or null for the page grid — where every ancestor up to the root is
        /// broken by the page boundary and so every one of them re-opens.
        /// <para>
        /// A <i>column</i> boundary is the case that needs it: the multi-column container is not fragmented
        /// by its own columns — its border and padding wrap all of them at once — so its cloned block-start
        /// spacing is not re-inserted there, and adding it indented every continuation column by spacing
        /// the column's content top already accounted for.
        /// </para>
        /// </param>
        internal static double ClonedBlockStart(CssBox? box, CssBox? stopAt)
        {
            var total = 0d;
            var reachedStop = stopAt is null;

            for (var current = box; current is not null; current = current.ParentBox)
            {
                if (ReferenceEquals(current, stopAt))
                {
                    reachedStop = true;
                    break;
                }

                if (current.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone)
                    total += current.ActualBorderTopWidth + current.ActualPaddingTop;
            }

            // A stopAt that is not an ancestor of `box` silently degrades to the unbounded walk, which is
            // the behaviour this parameter exists to prevent — so say so where it can be seen. Every caller
            // passes the fragmentation context root of a box inside it, so this holds by construction.
            Debug.Assert(reachedStop,
                "ClonedBlockStart's stopAt was not on the ancestor chain, so the walk ran to the root.");

            return total;
        }

        /// <summary>
        /// The block-end counterpart of <see cref="ClonedBlockStart(CssBox?, CssBox?)"/> — the border and padding the fragment
        /// being left behind closes with, and which content therefore has to stop short of.
        /// </summary>
        internal static double ClonedBlockEnd(CssBox? box)
        {
            var total = 0d;

            for (var current = box; current is not null; current = current.ParentBox)
            {
                total += OwnClonedBlockEnd(current);
            }

            return total;
        }

        /// <summary>
        /// <paramref name="box"/>'s own share of <see cref="ClonedBlockEnd"/> — what the box itself closes a
        /// fragment with, without its ancestors' own share.
        /// </summary>
        /// <remarks>
        /// The chain sum answers "how much must content stop short of the fragmentainer edge", which is a
        /// question about every box the break falls inside at once. This one answers "how much of that room
        /// belongs to <i>this</i> box's fragment", which is what tells a fragment's decoration area from the
        /// content it holds — each level of a nested cloning stack closes inside its ancestors' own close.
        /// </remarks>
        internal static double OwnClonedBlockEnd(CssBox box) =>
            box.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone
                ? box.ActualBorderBottomWidth + box.ActualPaddingBottom
                : 0;

        /// <summary>
        /// The inline-start margin, border and padding cloned fragments re-insert after a line break, summed
        /// over the inline boxes from <paramref name="from"/> up to — but not including —
        /// <paramref name="stopAt"/>, the block whose lines are being built.
        /// </summary>
        /// <remarks>
        /// Margin is included, as §6.2 says ("wrapped with the border, padding, and margin"), and to match what
        /// a box's <i>own</i> leading spacing does when it starts on a line — that is the same margin+border+
        /// padding sum. Only the block axis leaves margin out, because there
        /// <see href="https://www.w3.org/TR/css-break-3/#break-margins">§5.2</see> truncates a margin adjoining
        /// an unforced break to zero, which is the opposite instruction.
        /// </remarks>
        internal static double ClonedInlineStart(CssBox? from, CssBox stopAt)
        {
            var total = 0d;

            for (var current = from; current is not null && !ReferenceEquals(current, stopAt); current = current.ParentBox)
            {
                if (current.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone)
                    total += current.ActualMarginLeft + current.ActualBorderLeftWidth + current.ActualPaddingLeft;
            }

            return total;
        }

        /// <summary>
        /// The inline-end counterpart of <see cref="ClonedInlineStart"/> — what the fragment ending at a line
        /// break closes with, and which the wrap decision therefore has to leave room for.
        /// </summary>
        internal static double ClonedInlineEnd(CssBox? from, CssBox stopAt)
        {
            var total = 0d;

            for (var current = from; current is not null && !ReferenceEquals(current, stopAt); current = current.ParentBox)
            {
                if (current.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone)
                    total += current.ActualMarginRight + current.ActualBorderRightWidth + current.ActualPaddingRight;
            }

            return total;
        }

        public static bool IsStackingContextBox(CssBox box)
        {
            if (box.IsRoot)
            {
                return true;
            }

            if (box.Position.Value is PositionMode.Absolute or PositionMode.Relative && box.ZIndex.Value is { IsValue: true })
            {
                return true;
            }

            // Fixed/sticky always get their own stacking context; a running box (css-gcpm-3) joins them
            // here rather than the ZIndex-gated Absolute/Relative arm above - once laid out standalone
            // against a margin box (RunningElementLayout) it needs an unconditional stacking context for
            // its own descendants, matching DerivedStyle.IsPositioned's own treatment of Running.
            if (box.Position.Value is PositionMode.Fixed or PositionMode.Sticky or PositionMode.Running)
            {
                return true;
            }

            // Flex item with a z-index other than auto establishes a stacking context even without a
            // `position` value of its own (CSS Flexible Box Layout §z-order), unlike a plain block/
            // inline child, which needs position:relative/absolute for z-index to have any effect at all.
            if (box.ZIndex.Value is { IsValue: true } &&
                box.ParentBox?.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex)
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

        public static bool IsProperTableChild(CssBox box)
        {
            return box.IsTableRowGroupBox || box.DerivedStyle.ActualDisplay is Keywords.TableRow ||
                   box.DerivedStyle.ActualDisplay is Keywords.TableColumn || box.DerivedStyle.ActualDisplay is Keywords.TableColumnGroup ||
                   box.DerivedStyle.ActualDisplay is Keywords.TableCaption;
        }

        /// <summary>
        /// Whether a box paints any "real" content of its own, per
        /// <see href="https://www.w3.org/TR/css-page-3/#renderer-defaults">CSS Paged Media Level 3
        /// §3.2</see>'s definition of a content-empty page ("a page box whose page area contains no
        /// printable content other than backgrounds and/or borders"). Used by the fragment-tree
        /// builder to decide whether a pagination slot is worth materializing as a PDF page at all.
        /// </summary>
        internal static bool HasOwnPrintableContent(CssBox box)
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

            // Excludes a captioned table whose own border paint CssLayoutEngineTable suppressed in favor
            // of its grid decoration box (SuppressOwnBorderPaint, issue #721) - without this exclusion, a
            // pagination slot holding only that table's own (never-painted) border values would be
            // counted as printable content it will never actually draw, for the same reason the
            // background exclusion just above exists.
            if (!box.SuppressOwnBorderPaint)
            {
                if (RenderUtils.IsColorVisible(box.ActualBorderTopColor) && box.ActualBorderTopWidth > 0) return true;
                if (RenderUtils.IsColorVisible(box.ActualBorderBottomColor) && box.ActualBorderBottomWidth > 0) return true;
                if (RenderUtils.IsColorVisible(box.ActualBorderLeftColor) && box.ActualBorderLeftWidth > 0) return true;
                if (RenderUtils.IsColorVisible(box.ActualBorderRightColor) && box.ActualBorderRightWidth > 0) return true;
            }

            return false;
        }

        private static CssBox? GetNextIntersectingFloatBox(CssBox box, CssFloatCoordinates coordinates, Floating floatProp, ref int boxesVisited)
        {
            boxesVisited++;

            if (IsFloatIntersecting(coordinates, floatProp, box))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetNextIntersectingFloatBox(childBox, coordinates, floatProp, ref boxesVisited);
                if (foundBox != null)
                {
                    return foundBox;
                }
            }

            return null;
        }

        private static bool IsFloatIntersecting(CssFloatCoordinates coordinates, Floating floatProp, CssBox targetBox)
        {
            if (!targetBox.IsFloated) return false;

            // vertical conflict
            if (!(coordinates.Top < targetBox.ActualBottom) || !(targetBox.Location.Y <= coordinates.Top)) return false;

            var targetRight = targetBox.ActualRight + targetBox.ActualMarginRight;
            var targetLeft = targetBox.Location.X - targetBox.ActualMarginLeft;

            var currentLeft = coordinates.Left - coordinates.MarginLeft;

            switch (floatProp)
            {
                case Floating.Left when targetRight > currentLeft && targetLeft <= currentLeft:
                case Floating.Right when targetLeft > coordinates.FloatRightStartX + coordinates.ReferenceWidth + coordinates.MarginRight:
                    return true;
                default:
                    return false;
            }
        }
    }
}