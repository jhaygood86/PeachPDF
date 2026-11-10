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

using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Svg;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PeachBrackets.Html.Utils
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
        public static bool IsInBox(CssBox box, PaintPoint location)
        {
            foreach (var line in box.RectanglesView)
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
        /// <remarks>
        /// A floated child counts as inline-compatible here too, even though it is blockified
        /// (<see cref="CssBox.IsBlock"/>): a browser generates no anonymous block around a floated child
        /// sitting among inline content, so this box's own content dispatch (<c>CssBox.LayoutContents</c>)
        /// must still treat it as a box whose children flow through one inline formatting context
        /// (<c>CssLayoutEngine.CreateLineBoxes</c>/<c>FlowBox</c>, which has its own dispatch branch for a
        /// floated child) rather than the block-children path - see <c>DomParser.JoinsTheInlineRun</c>'s
        /// own remarks and <see href="https://github.com/jhaygood86/PeachPDF/issues/1038">#1038</see>.
        /// An absolutely positioned child is inline-compatible for the same reason: it is out of flow, so
        /// it is not the block-level content CSS 2.1 §9.2.1.1 separates inline content from, and
        /// <c>FlowBox</c> has a dispatch branch for it too.
        /// <para>
        /// A <c>display: none</c> child generates no box at all (CSS Display 3 §2.5), so it can neither make
        /// this box's content block-level nor be a reason to stop treating it as one inline formatting
        /// context. <c>DomParser.ContainsVariantBoxes</c> and <c>ContainsInlinesOnlyDeep</c> already skip
        /// it; these three predicates must answer the same question. Without it, a document with head
        /// content but no <c>&lt;body&gt;</c> tag - <c>&lt;style&gt;…&lt;/style&gt;text</c>, where the parser
        /// builds no implied body and the <c>display: none</c> <c>style</c> is a sibling of the text - was
        /// routed to the block-children path, whose bare text box is never measured or placed, so the text
        /// vanished.
        /// </para>
        /// </remarks>
        public static bool ContainsInlinesOnly(CssBox box)
        {
            return box.Boxes.All(b => b.IsInline || b.IsFloated || b.IsAbsolutelyPositioned
                                      || b.DerivedStyle.ActualDisplay == Keywords.None);
        }

        /// <summary>
        /// Whether <paramref name="box"/> is an <b>atomic inline</b> — an inline-level box that its
        /// parent's inline formatting context places as one opaque item rather than as a run of text
        /// (<see href="https://www.w3.org/TR/css-display-3/#atomic-inline">CSS Display 3 §2.3</see>):
        /// a replaced element (<c>&lt;img&gt;</c>, inline <c>&lt;svg&gt;</c>, <c>&lt;object&gt;</c>,
        /// <c>&lt;iframe&gt;</c>, <c>&lt;math&gt;</c>, a form field), or an <c>inline-block</c> /
        /// <c>inline-table</c> / <c>inline-flex</c> / <c>inline-grid</c> box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Asked at paint time by <c>FragmentPainter</c>'s text-decoration collection, which
        /// <see href="https://www.w3.org/TR/css-text-decor-3/#line-decoration">css-text-decor-3 §2.4</see>
        /// requires to break a decoration line around one: "Atomic inlines, such as images and inline
        /// blocks, are not decorated."
        /// </para>
        /// <para>
        /// <b>Decided by the box's own display type and kind, never by whether its fragment happens to
        /// have children.</b> An <c>inline-block</c> whose content is inlines-only reaches paint through
        /// a different layout path (the ordinary inline path) than one whose content is block-level
        /// (<c>CssLayoutEngine.FlowAtomicBlockContentChild</c>), and both are atomic inlines.
        /// </para>
        /// <para>
        /// The replaced half deliberately reuses <see cref="MonolithicContent.IsReplaced"/> rather than
        /// re-deriving the set of replaced box types, so the two can never name different sets. It is
        /// gated on <see cref="CssBox.IsInline"/> because a <c>display: block</c> image is a block box,
        /// not an atomic inline — nothing on a line box for a decoration to break around.
        /// </para>
        /// </remarks>
        public static bool IsAtomicInline(CssBox box) =>
            box.IsInline
            && (MonolithicContent.IsReplaced(box)
                || box.DerivedStyle.ActualDisplay is Keywords.InlineBlock or Keywords.InlineTable
                    or Keywords.InlineFlex or Keywords.InlineGrid);

        /// <summary>
        /// Walks up from <paramref name="box"/> (inclusive) looking for the nearest ancestor with the
        /// given HTML tag name, and returns that ancestor's parent - i.e. "where parsing should
        /// resume after closing this tag". Returns <c>null</c>, rather than <paramref name="root"/>,
        /// when no matching ancestor exists at all: a closing tag with no corresponding open element
        /// (e.g. a stray <c>&lt;/p&gt;</c> for a <c>&lt;p&gt;</c> already auto-closed by a nested
        /// <c>&lt;table&gt;</c>, per CSS2.1/HTML4's "table closes p" rule) is a parse error that must be
        /// ignored - see the end tag handling in <c>HtmlParser</c>, which falls back to leaving
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

                if (box.HtmlTag != null && box.HtmlTag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
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

            var index = b.ParentBox.IndexOfChild(b);
            if (index <= 0) return null;
            for (var i = index - 1; i >= 0; i--)
            {
                var sib = b.ParentBox.Boxes[i];
                if (!IsSteppedOverAsPreviousSibling(sib, includeFloats)) return sib;
            }

            return null;
        }

        /// <summary>
        /// Whether <see cref="GetPreviousSibling"/> steps over <paramref name="sib"/>: a box that takes no
        /// part in placing its following sibling.
        /// </summary>
        /// <remarks>
        /// One predicate for both the walk and its end. The two used to be separate conditions, and the
        /// end's omitted <c>position: absolute</c>: an absolutely positioned first child was returned as
        /// the previous sibling of the box after it, which was then placed below the absolutely
        /// positioned box rather than at the top of its parent (#1349).
        /// </remarks>
        private static bool IsSteppedOverAsPreviousSibling(CssBox sib, bool includeFloats) =>
            sib.DerivedStyle.ActualDisplay == Keywords.None
            || sib.Position.Value is PositionMode.Absolute or PositionMode.Fixed or PositionMode.Running
            || (!includeFloats && sib.IsFloated)
            || sib.IsPageFloated
            || CssBox.IsOutsideMarker(sib)
            || sib.IsTableGridDecorationBox;

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

                if (!BreakValues.AvoidsBreak(prev.BreakAfter.Value, context)
                    && !BreakValues.AvoidsBreak(current.BreakBefore.Value, context))
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

            var index = box.ParentBox.IndexOfChild(box);

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
        public static CssBox? GetCssBox(CssBox? box, PaintPoint location, bool visible = true)
        {
            if (box == null) return null;

            if ((visible && box.Visibility.Value != Visibility.Visible) ||
                (!box.Bounds.IsEmpty && !box.Bounds.Contains(location))) return null;

            foreach (var childBox in box.Boxes)
            {
                if ((box.RectangleCount > 0 ? box.RectangleValues.First() : box.Bounds).Contains(location))
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
            if (box is null || LeavesNoVisibleArea(box))
                return;

            if (box is { IsClickable: true, Visibility.Value: Visibility.Visible })
                linkBoxes.Add(box);

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
            => CollectLinkAndBookmarkBoxes(box, linkBoxes, bookmarkBoxes, linksSuppressed: false);

        // linksSuppressed: true under an element whose transform leaves no visible area (LeavesNoVisibleArea) - nothing is drawn there, so
        // nothing is clickable, but the walk still goes on for the bookmarks, which name a place in the document rather than something painted.
        private static void CollectLinkAndBookmarkBoxes(CssBox? box, List<CssBox> linkBoxes, List<CssBox> bookmarkBoxes, bool linksSuppressed)
        {
            if (box is null)
                return;

            linksSuppressed = linksSuppressed || LeavesNoVisibleArea(box);

            if (!linksSuppressed && box is { IsClickable: true, Visibility.Value: Visibility.Visible })
                linkBoxes.Add(box);

            // BookmarkLevel is stored as its raw, already-validated none|<integer> string (see
            // bookmark-level's css-properties.json entry) - any non-"none" value is a candidate;
            // BookmarkOutlineBuilder.ResolveLevel does the actual int parse.
            if (box.BookmarkLevel is { Length: > 0 } level && level != Keywords.None)
            {
                bookmarkBoxes.Add(box);
            }

            foreach (var childBox in box.Boxes)
            {
                CollectLinkAndBookmarkBoxes(childBox, linkBoxes, bookmarkBoxes, linksSuppressed);
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> was not drawn because its <c>transform</c> leaves it, and everything under it, no visible area
        /// (css-transforms-1 §"Transform Rendering"). The collectors that read the box tree (links, form widgets) ask here, since an
        /// annotation over something that is not drawn is a click target on blank paper.
        /// </summary>
        /// <remarks>
        /// The painter decides what it draws, so it is also the authority on what it did not: it records the boxes it culled
        /// (<see cref="HtmlContainerInt.NoteNotRendered"/>) and this reads that record, which makes it meaningful once the document has been
        /// painted - <c>PdfGenerator</c> collects links after the pages. What can be said without the painter is a transformation matrix
        /// that has no inverse in 4x4, which renders nothing wherever the box sits, and that is answered either way.
        /// </remarks>
        public static bool LeavesNoVisibleArea(CssBox box) =>
            box.ActualTransform4 is { } transform4 && !Paint.FragmentPainter.IsInvertible4(transform4)
            || box.HtmlContainer?.IsNotRendered(box) == true;

        /// <summary>Whether <paramref name="box"/> or one of its ancestors <see cref="LeavesNoVisibleArea"/>.</summary>
        public static bool IsUnderNoVisibleArea(CssBox? box)
        {
            for (; box is not null; box = box.ParentBox)
            {
                if (LeavesNoVisibleArea(box))
                    return true;
            }

            return false;
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
            if (box is null || LeavesNoVisibleArea(box))
                return;

            if (box is CssBoxFormField { Visibility.Value: Visibility.Visible })
                fieldBoxes.Add(box);

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
        public static void GetAllSvgLinks(CssBox? box, List<(Rect Rect, string Href, CssBox Owner)> linkBoxes)
        {
            switch (box)
            {
                case null:
                    return;

                case not null when LeavesNoVisibleArea(box):
                    return;

                case CssBoxSvg svgBox:
                    if (svgBox.GetLinkSource() is { } svgSource)
                        CollectSvgLinks(svgSource.Document, svgSource.Rect, svgBox, linkBoxes);
                    return;

                case CssBoxImage imageBox:
                    if (imageBox.GetLinkSource() is { } imageSource)
                        CollectSvgLinks(imageSource.Document, imageSource.Rect, imageBox, linkBoxes);
                    return;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllSvgLinks(childBox, linkBoxes);
            }
        }

        /// <summary>Collects the links of <paramref name="document"/> and tags each with <paramref name="owner"/>, the box it was drawn in.</summary>
        private static void CollectSvgLinks(SvgDocument document, Rect viewportRect, CssBox owner, List<(Rect Rect, string Href, CssBox Owner)> sink)
        {
            var found = new List<(Rect Rect, string Href)>();
            SvgRenderer.CollectLinks(document, viewportRect, found, owner.PointerEvents.Value);

            foreach (var (rect, href) in found)
                sink.Add((rect, href, owner));
        }

        /// <summary>
        /// Get css link box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box by</param>
        /// <returns>css link box if exists or null</returns>
        public static CssBox? GetLinkBox(CssBox? box, PaintPoint location)
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

            return FindBoxById(box, id) ?? FindDisplayContentsShellById(box, id);
        }

        private static CssBox? FindBoxById(CssBox box, string id)
        {
            if (box.HtmlTag != null && id.Equals(box.HtmlTag.TryGetAttribute("id"), StringComparison.OrdinalIgnoreCase))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = FindBoxById(childBox, id);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// A <c>display: contents</c> element is in no box's <see cref="CssBox.Boxes"/>, so a walk cannot
        /// find it; the document's flat shell list (<see cref="HtmlContainerInt.DisplayContentsShells"/>)
        /// can. Only asked after the walk found nothing, so a duplicate id resolves to the real box.
        /// </summary>
        private static CssBox? FindDisplayContentsShellById(CssBox box, string id)
        {
            if (box.HtmlContainer is not { DisplayContentsShells.Count: > 0 } container) return null;

            foreach (var shell in container.DisplayContentsShells)
            {
                if (id.Equals(shell.HtmlTag?.TryGetAttribute("id"), StringComparison.OrdinalIgnoreCase))
                    return shell;
            }

            return null;
        }

        /// <summary>
        /// The box that stands in for <paramref name="box"/> wherever its position in the laid-out document
        /// is asked for - an id link's destination, a bookmark, <c>target-counter(..., page)</c>,
        /// <c>string-set</c>. Any box but a <c>display: contents</c> shell is its own; a shell has no
        /// geometry, so this is where its content begins: the first lifted descendant that was laid out
        /// (a single path down the first children, never a traversal), else - for an element with no
        /// content, which would have generated a box at that point - the box that received its children.
        /// </summary>
        internal static CssBox ResolveGeometryBox(CssBox box)
        {
            if (!box.IsDisplayContentsShell) return box;

            if (FirstLaidOut(box.ContentChildren) is { } content) return content;

            var parent = box.ParentBox;
            while (parent is { IsDisplayContentsShell: true }) parent = parent.ParentBox;
            return parent ?? box;
        }

        /// <summary>
        /// Inserts <paramref name="shell"/> into <paramref name="boxes"/> - a list the tree walk built in
        /// document order - at the place its content begins: before the first entry laid out below it.
        /// A <c>display: contents</c> element is in no box's children, so the walk that produced the list
        /// never met it; ordering by position is what keeps a bookmark outline's nesting right.
        /// </summary>
        /// <remarks>
        /// Placed before whatever else begins at the same Y - its own content (a wrapper and the heading it
        /// wraps), whose entry follows the wrapper's. <paramref name="floor"/>/<paramref name="floorY"/> carry
        /// where the previous shell went, so shells sharing a Y keep document order between themselves.
        /// </remarks>
        internal static void InsertByDocumentPosition(List<CssBox> boxes, CssBox shell, ref int floor, ref double floorY)
        {
            var top = TopOf(shell);
            var start = Math.Abs(floorY - top) <= HtmlContainerInt.PageBoundaryEpsilon ? floor : 0;
            var index = boxes.FindIndex(start, b => TopOf(b) >= top - HtmlContainerInt.PageBoundaryEpsilon);
            index = index < 0 ? boxes.Count : index;
            boxes.Insert(index, shell);

            floor = index + 1;
            floorY = top;
        }

        private static double TopOf(CssBox box)
        {
            var geometryBox = ResolveGeometryBox(box);
            return CommonUtils.GetFirstValueOrDefault(geometryBox.Rectangles, geometryBox.Bounds).Top;
        }

        /// <summary>
        /// The box whose counters are those in effect where <paramref name="box"/>'s content begins - what
        /// <c>target-counter()</c> and <c>string-set</c> read for a <c>display: contents</c> element. It has no
        /// counter scope of its own (CSS Lists 3 §4.5), and is in no sibling list to read one from, so this is
        /// the box just before its first lifted child - the previous sibling, else the parent - and not the
        /// child itself, whose own <c>counter-increment</c> comes after the element's position. Structural, not
        /// geometric: <c>target-counter()</c> is resolved before layout has run.
        /// </summary>
        internal static CssBox ResolveCounterAnchor(CssBox box)
        {
            if (!box.IsDisplayContentsShell) return box;

            foreach (var child in box.ContentChildren)
            {
                if (child.ParentBox is null || child.DerivedStyle.ActualDisplay == Keywords.None) continue;

                return PreviousSiblingInDocumentOrder(child) ?? child.ParentBox;
            }

            var parent = box.ParentBox;
            while (parent is { IsDisplayContentsShell: true }) parent = parent.ParentBox;
            return parent ?? box;
        }

        /// <summary>
        /// The sibling just before <paramref name="box"/> in document order that generates a box, whatever its
        /// positioning: counters follow the document tree (CSS Lists 3 §4), so an absolutely positioned, fixed
        /// or floated sibling's <c>counter-increment</c> is in effect after it all the same.
        /// </summary>
        /// <remarks>
        /// Not <see cref="GetPreviousSibling"/>, which answers a layout question (which box places this one)
        /// and so steps over every out-of-flow box.
        /// </remarks>
        private static CssBox? PreviousSiblingInDocumentOrder(CssBox box)
        {
            var siblings = box.ParentBox!.Boxes;
            for (var i = siblings.IndexOf(box) - 1; i >= 0; i--)
            {
                var sibling = siblings[i];
                if (sibling.DerivedStyle.ActualDisplay == Keywords.None || CssBox.IsOutsideMarker(sibling)
                    || sibling.IsTableGridDecorationBox) continue;

                return sibling;
            }

            return null;
        }

        private static CssBox? FirstLaidOut(IReadOnlyList<CssBox> children)
        {
            foreach (var child in children)
            {
                // A lifted child a later pass dropped (a whitespace-only text box) has no parent any more.
                if (child.ParentBox is null || child.DerivedStyle.ActualDisplay == Keywords.None) continue;

                if (child.RectangleCount > 0 || child.Bounds.Width > 0 || child.Bounds.Height > 0)
                    return child;

                if (FirstLaidOut(child.Boxes) is { } nested) return nested;
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

            // The elements the walk above cannot reach (display: contents), after it so a duplicate id
            // still resolves to the real box, as GetBoxById's own lookup does.
            if (root.HtmlContainer is { } container)
            {
                foreach (var shell in container.DisplayContentsShells)
                {
                    if (shell.HtmlTag?.TryGetAttribute("id") is { Length: > 0 } shellId)
                        index.TryAdd(shellId, shell);
                }
            }

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
        /// Whether <paramref name="box"/>, a child of a flex or grid container, becomes an item of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per <see href="https://www.w3.org/TR/css-flexbox-1/#flex-items">css-flexbox-1 §4</see> (and
        /// <see href="https://www.w3.org/TR/css-grid-2/#grid-items">css-grid-2 §6</see>, which repeats it
        /// verbatim): each in-flow child becomes an item, "each contiguous sequence of child text runs is
        /// wrapped in an anonymous block container", and a text run "that contains only white space … is
        /// not rendered". <b>Generated content is explicitly not in that exclusion</b> — both specs say the
        /// container's own <c>::before</c> and <c>::after</c> pseudo-elements generate items.
        /// </para>
        /// <para>
        /// A pseudo-element box has no <see cref="CssBox.HtmlTag"/>, so the plain "anonymous and empty"
        /// test drops the very common <c>content: ""</c> spacer — a generated box that carries no text but
        /// is sized entirely by its own <c>width</c>/<c>height</c>. Charts.css's area and line charts are
        /// built on one (<c>td::after { content: ""; height: calc(100% * var(--end)) }</c> is what lifts
        /// each data label to its data point), and without it every label collapsed onto the chart's
        /// baseline.
        /// </para>
        /// </remarks>
        internal static bool GeneratesFlexOrGridItem(CssBox box) =>
            box.DerivedStyle.ActualDisplay != Keywords.None
            // A left/right float is an item, not an exclusion (css-flexbox-1 §4: float has no effect on a
            // flex item). Nothing is needed here for that: DomParser.NormalizeFlexOrGridItem has already
            // coerced such a child's own `float` to `none`, so IsExcludedFromFlow sees only the genuinely
            // out-of-flow ones - absolute, fixed, running, and a css-gcpm-3 footnote body.
            && !box.IsExcludedFromFlow
            && (box.HtmlTag is not null || box.IsBeforePseudoElement || box.IsAfterPseudoElement
                || !box.IsSpaceOrEmpty);

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
            } while (currentBox is { EffectiveParentBox: not null } && !FormsAbsoluteContainingBlock(currentBox));

            return currentBox!;
        }

        /// <summary>
        /// Whether <paramref name="box"/> is the containing block of its absolutely positioned descendants: a positioned box, or one that layout containment or
        /// paint containment applies to (css-contain-2 §3.2, §3.4). A <c>position: fixed</c> descendant of the latter is made absolute where the tree is built
        /// (<c>DomParser.NormalizeFixedInContainment</c>), so this is the one place layout asks.
        /// </summary>
        internal static bool FormsAbsoluteContainingBlock(CssBox box) => box.IsPositioned || AppliesLayoutOrPaintContainment(box);

        /// <summary>
        /// The next box up the chain whose <c>overflow</c> clip can apply to <paramref name="box"/>, or
        /// null when no box in the document can clip it. An absolutely positioned box continues at the
        /// nearest ancestor that forms its containing block, a fixed one at the nearest ancestor that forms
        /// a fixed-position containing block (null when there is none, since the page is then its
        /// containing block), and any other box at its ordinary <see cref="CssBox.ContainingBlock"/>. As
        /// with <see cref="CssBox.ContainingBlock"/>, the root returns itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-overflow-3/#overflow-properties">CSS Overflow 3 §3</see>:
        /// an overflow clip affects descendants whose containing block chain passes through the clipping
        /// box, so an <c>overflow: hidden</c> box sitting between an out-of-flow positioned descendant and
        /// that descendant's containing block does not clip it.
        /// </para>
        /// <para>
        /// Besides a positioned ancestor (absolute only), <c>transform</c>, <c>perspective</c>, <c>filter</c>
        /// and <c>backdrop-filter</c> each form the containing block of both kinds of positioned
        /// descendant (CSS Transforms 1 §2, CSS Transforms 2 §13, Filter Effects 1 §3 and 2 §3.1). Layout
        /// still places such a descendant against its nearest positioned ancestor or the page, but the
        /// clip follows the specification so that a transformed <c>overflow: hidden</c> box keeps
        /// clipping its positioned descendants, as it did before clipping honoured the containing-block
        /// chain.
        /// </para>
        /// </remarks>
        /// <param name="box">the box being clipped</param>
        /// <returns>the next box on its clipping containing-block chain, or null</returns>
        internal static CssBox? ClippingContainingBlockOf(CssBox box)
        {
            var position = box.Position.Value;
            if (position is not (PositionMode.Absolute or PositionMode.Fixed) || box.EffectiveParentBox is null)
                return box.ContainingBlock;

            var current = box.EffectiveParentBox;

            while (true)
            {
                if (FormsContainingBlockForFixed(current)
                    || (position is PositionMode.Absolute && FormsAbsoluteContainingBlock(current)))
                    return current;

                if (current.EffectiveParentBox is not { } parent)
                    return position is PositionMode.Absolute ? current : null;

                current = parent;
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> forms the containing block of its <c>position: fixed</c> (and
        /// so also its absolutely positioned) descendants without being positioned itself. Any
        /// <c>transform</c> other than <c>none</c> counts, including an identity one such as
        /// <c>translateZ(0)</c> or <c>scale(1)</c>, so this reads <see cref="CssBox.HasTransform"/> rather
        /// than <see cref="CssBox.IsTransformed"/>.
        /// </summary>
        private static bool FormsContainingBlockForFixed(CssBox box) =>
            box.HasTransform
            || box.ActualPerspective > 0
            || box.ActualFilterFunctions.Count > 0
            || box.ActualBackdropFilterFunctions.Count > 0;

        /// <summary>
        /// Whether <paramref name="box"/>'s <c>overflow: hidden</c> clips its descendants. CSS Overflow 3
        /// applies <c>overflow</c> to block containers, flex and grid containers, not to a non-atomic
        /// inline box or to a table row, row group, column or column group, so those never clip.
        /// </summary>
        /// <param name="box">the candidate clipping box</param>
        /// <returns>true when <paramref name="box"/> has <c>overflow: hidden</c> and <c>overflow</c> applies to it</returns>
        internal static bool ClipsItsOverflow(CssBox box) =>
            (box.Overflow.Value == Overflow.Hidden || box.ClipsWithoutScrolling || IsScrollerOnScreen(box))
            && !(box.IsInline && !IsAtomicInline(box))
            && !box.IsTableRowGroupBox
            && box.DerivedStyle.ActualDisplay is not (Keywords.TableRow or Keywords.TableColumn or Keywords.TableColumnGroup);

        /// <summary>
        /// Whether <paramref name="box"/> is a scroll container of a document shown on a screen: its <c>overflow</c> is <c>auto</c>, <c>scroll</c> or <c>hidden</c> (CSS Overflow 3 §2),
        /// it is not what the viewport takes its overflow from, and <c>overflow</c> applies to it. Such a box clips its content and can be scrolled. A paged document has no scrolling, and
        /// <c>auto</c> and <c>scroll</c> boxes show the whole of their content on the page (see <c>CappedScrollContainer</c>).
        /// </summary>
        internal static bool IsScrollerOnScreen(CssBox box) =>
            box.HtmlContainer is { Continuous: true }
            && box.Overflow.Value is Overflow.Auto or Overflow.Scroll or Overflow.Hidden
            && Fragmentation.MonolithicContent.IsScrollContainer(box)
            && !(box.IsInline && !IsAtomicInline(box))
            && box.HtmlTag?.Name is not ("textarea" or "select" or "input")
            && !box.IsTableRowGroupBox
            && box.DerivedStyle.ActualDisplay is not (Keywords.TableRow or Keywords.TableColumn or Keywords.TableColumnGroup);

        /// <summary>
        /// The nearest box on <paramref name="box"/>'s clipping chain whose <c>overflow</c> clips its content -
        /// the box whose padding edge is <paramref name="box"/>'s own fragment <c>OverflowClip</c> - or null
        /// when nothing clips it. The same walk <c>FragmentEmitter</c> makes to build that clip.
        /// </summary>
        /// <param name="box">the box being clipped</param>
        internal static CssBox? ClippingBoxOf(CssBox box)
        {
            var containingBlock = ClippingContainingBlockOf(box);

            while (containingBlock is not null)
            {
                if (ClipsItsOverflow(containingBlock)) return containingBlock;

                var next = ClippingContainingBlockOf(containingBlock);
                if (ReferenceEquals(next, containingBlock)) return null;
                containingBlock = next;
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="ancestor"/>'s <c>overflow</c> clip applies to <paramref name="box"/>:
        /// whether the walk up from <paramref name="box"/> reaches it without an out-of-flow positioned
        /// box on the way jumping past it to its own containing block (<see cref="ClippingContainingBlockOf"/>).
        /// </summary>
        /// <remarks>
        /// Every other step goes to <see cref="CssBox.EffectiveParentBox"/>, not
        /// <see cref="CssBox.ContainingBlock"/>. The ordinary containing-block walk skips ancestors whose
        /// display it doesn't list (a <c>table-caption</c> with <c>overflow: hidden</c> among them) and stops
        /// at a detached repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>, and either would drop a clip
        /// that in-flow content inside it has always had.
        /// </remarks>
        /// <param name="box">the box being clipped</param>
        /// <param name="ancestor">a candidate clipping ancestor</param>
        /// <returns>true when <paramref name="ancestor"/>'s overflow clip applies to <paramref name="box"/></returns>
        internal static bool IsOnClippingChainOf(CssBox box, CssBox ancestor)
        {
            var current = box;

            while (true)
            {
                var next = current.Position.Value is PositionMode.Absolute or PositionMode.Fixed
                    ? ClippingContainingBlockOf(current)
                    : current.EffectiveParentBox;

                if (next is null || ReferenceEquals(next, current)) return false;
                if (ReferenceEquals(next, ancestor)) return true;
                current = next;
            }
        }

        /// <summary>
        /// The containing block a positioned <b>inline</b> ancestor forms for an absolutely positioned
        /// descendant, or null when <paramref name="ancestor"/> is not a non-atomic inline with laid-out
        /// fragments (or, for an empty one, a <see cref="CssBox.EmptyInlineContainingBlock"/>). Every other
        /// ancestor forms it from its own padding box, which its callers already read off the box itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#containing-block-details">CSS 2.1 §10.1</see>
        /// (4.1) defines this only for an inline on one line: the bounding box of the padding boxes of its
        /// first and last inline boxes. It leaves a multi-line inline undefined. The rule used for that is
        /// <see href="https://www.w3.org/TR/css-position-3/#def-cb">CSS Positioned Layout 3 §2.1</see>'s:
        /// the start-most edges of the first fragment and the end-most edges of the last, in the inline's
        /// own writing mode. So in <c>ltr</c> the top and left come from the first fragment
        /// and the bottom and right from the last, and in <c>rtl</c> the left and right swap. That section
        /// names the fragments' content edges; padding edges are used instead, as CSS 2.1's one-line rule
        /// and Chromium both use them. A resulting negative width or height, which a wrapped inline can
        /// produce, is clamped to zero.
        /// </para>
        /// <para>
        /// An inline's own <see cref="CssBox.Location"/> is a line-local value layout never updates, so
        /// reading it the way a block's is read put such a descendant at the page origin. The per-line
        /// rectangles are border boxes (<see cref="CssLineBox.UpdateRectangle"/>), so the borders come off.
        /// </para>
        /// </remarks>
        /// <param name="ancestor">the nearest positioned ancestor of the absolutely positioned box</param>
        /// <returns>the containing block's padding rectangle, or null</returns>
        internal static Rect? InlineContainingBlockOf(CssBox ancestor)
        {
            if (!ancestor.IsInline || IsAtomicInline(ancestor)) return null;

            // An inline with no fragment - one holding only absolutely positioned boxes - is given a
            // zero-width one at their place on the line while they are laid out.
            if (ancestor.RectangleCount == 0) return ancestor.EmptyInlineContainingBlock;

            var rectangles = ancestor.RectanglesView;
            var first = ancestor.FirstHostingLineBox is { } firstLine && rectangles.TryGetValue(firstLine, out var f)
                ? f
                : rectangles.Values.MinBy(r => r.Top);
            var last = ancestor.LastHostingLineBox is { } lastLine && rectangles.TryGetValue(lastLine, out var l)
                ? l
                : rectangles.Values.MaxBy(r => r.Bottom);

            var rtl = ancestor.Direction.Value == DirectionMode.Rtl;
            var left = (rtl ? last : first).Left + ancestor.ActualBorderLeftWidth;
            var right = (rtl ? first : last).Right - ancestor.ActualBorderRightWidth;
            var top = first.Top + ancestor.ActualBorderTopWidth;
            var bottom = last.Bottom - ancestor.ActualBorderBottomWidth;

            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
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

        /// <param name="reference">The box being placed (a float) or whose lines are being laid out.</param>
        /// <param name="coordinates">The area a float has to intersect to be returned.</param>
        /// <param name="floatProp">Which side's floats to look for.</param>
        /// <returns>
        /// The first blocker. For a float being placed, <see cref="CssFloatCoordinates.BlockerOffsetX"/>/<c>Y</c> then say how far its edges are from
        /// the static positions in the frame the coordinates are in (CSS 2.1 §9.4.3): the edges to place against are its coordinates less those.
        /// The line flow's lookup (<see cref="CssFloatCoordinates.LeftFloatsOnly"/>) reads the edges as drawn, as it always has: it uses the
        /// returned float's own edges, which it cannot be given the offsets of.
        /// </returns>
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
        /// The lowest outer (margin-edge) top among the floats that come before <paramref name="reference"/> in the same formatting context, which a float
        /// being placed may not start above (CSS 2.1 §9.5.1 rule 5: its outer top "may not be higher than the outer top of any block or
        /// floated box generated by an element earlier in the source document"). An earlier float pushed down by a conflict has the top
        /// the new one inherits, whatever fits beside it higher up. Negative infinity when there is none.
        /// </summary>
        /// <remarks>
        /// The same climb as <see cref="FindIntersectingFloatBox"/>, except that it does not look inside a box that is a formatting
        /// context of its own: what a float there does stays there (CSS 2.1 §9.4.1).
        /// </remarks>
        internal static double GetLowestEarlierFloatTop(CssBox reference)
        {
            var lowest = double.NegativeInfinity;
            var isStartingLevel = true;

            // The float being placed is in a frame that has the relative offsets of the boxes it lies in (those are applied before their content is
            // laid out), while an earlier sibling's floats have that sibling's own; the first is taken off every float found, so that all of them are
            // compared in the float's frame with no offset but their own removed (CSS 2.1 §9.4.3).
            var frameOffsetY = 0d;

            for (; reference.ParentBox is { } parent; reference = parent)
            {
                if (!isStartingLevel && (EstablishesIndependentFormattingContext(reference) || reference.ColumnsBesideFloats is not null)) break;

                // Inside a multi-column container a Y is measured from the top of its own column, so the tops of floats in different
                // columns are not comparable and an earlier column's float must not hold this one down. A block that spans columns has
                // the same trouble, so what was found below this level is dropped too. After the formatting-context stop above: a root
                // of its own directly inside the container has its own coordinates, and what was found inside it stands.
                if (parent.EstablishesMultiColumnContext) return double.NegativeInfinity;

                isStartingLevel = false;

                var index = parent.IndexOfChild(reference);

                for (var i = 0; i < index; i++)
                {
                    lowest = Math.Max(lowest, LowestFloatTopIn(parent.Boxes[i], -frameOffsetY));
                }

                frameOffsetY += parent.RelativeOffsetY;
            }

            return lowest;
        }

        /// <param name="box">The box to look in.</param>
        /// <param name="offsetAbove">How far the boxes between <paramref name="box"/> and the earlier sibling the search started from have been moved by relative positioning.</param>
        private static double LowestFloatTopIn(CssBox box, double offsetAbove)
        {
            // The static position: a relative offset moves where a float is drawn, not where later floats go (CSS 2.1 §9.4.3). The offset of
            // every box the float lies in has moved it too, since a box's contents are moved along with it.
            var offset = offsetAbove + box.RelativeOffsetY;

            // A float is a formatting context of its own, so its contents are not looked into, but it counts itself.
            if (box.IsFloated) return box.Location.Y - offset - box.ActualMarginTop;

            if (EstablishesIndependentFormattingContext(box)) return double.NegativeInfinity;

            var lowest = double.NegativeInfinity;

            foreach (var child in box.Boxes)
            {
                lowest = Math.Max(lowest, LowestFloatTopIn(child, offset));
            }

            return lowest;
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
            var isStartingLevel = true;

            // How far the frame the float is in has been moved by the relative offsets of the boxes it lies in, up to the level being scanned
            // (its own is applied after it is placed): those are applied before a box's content is laid out, so the coordinates being compared
            // include them, and a blocker's own (from the earlier sibling down) are removed from it as well, so both are then in the same frame.
            var frameOffsetX = 0d;
            var frameOffsetY = 0d;
            var useStaticEdges = !coordinates.LeftFloatsOnly;

            while (true)
            {
                if (reference.ParentBox is null)
                {
                    return null;
                }

                // The same CSS 2.1 §9.5 / css-display-3 §2.1 rule
                // FindNarrowestRightFloatBox already applies — a float cannot affect content outside
                // its own formatting context — which this, the LEFT-side point-collision walk, was
                // missing. It ran to the document root and scanned every preceding sibling on the
                // way, so the cursor for a line in one grid item or table cell could be displaced by
                // a `float: left` in a different one.
                //
                // NOT at the starting level, and that difference is the whole subtlety. The box this
                // walk begins from is the box being PLACED, and a float is itself a
                // formatting-context root for its own contents — so breaking before that first
                // level's siblings are scanned would shield a float from the very siblings it has to
                // be positioned against. FloatLayoutRegressionTests'
                // FloatRight_InNarrowerNestedBlock_AvoidsAWiderAncestorFloatRightSibling catches
                // that immediately. From the second level up, `reference` is a proper ancestor and
                // the content being placed really is inside it, so the rule applies.
                //
                // FindNarrowestRightFloatBox needs no such exemption because of what each is handed:
                // its `reference` comes from FlowBox's word-flow loop and is the block whose line is
                // being laid out, never a float itself.
                //
                // It is also the most expensive thing this walk does. On a synthetic 40-row document
                // of two-column grids each holding one float, it costs 237,757 box visits across
                // 2,880 calls — 83 per call — against 1,200 visits with the break in place. The
                // right-side walk, identical in shape but for this rule, has always been ~7.
                if (!isStartingLevel && (EstablishesIndependentFormattingContext(reference) || reference.ColumnsBesideFloats is not null))
                {
                    return null;
                }

                // Only the boxes next to the one being placed are in its line context: an inline-block inside an earlier block is on a line of that
                // block, which a float after the block does not share even where the inline-block overflows it.
                var isSiblingLevel = isStartingLevel;
                isStartingLevel = false;

                var currentBoxIdx = reference.ParentBox.IndexOfChild(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    var next = GetNextIntersectingFloatBox(reference.ParentBox.Boxes[i], coordinates, floatProp, ref boxesVisited, -frameOffsetX, -frameOffsetY,
                        isSiblingLevel);

                    if (next is not null)
                    {
                        return next;
                    }
                }

                reference = reference.ParentBox;

                if (useStaticEdges)
                {
                    frameOffsetX += reference.RelativeOffsetX;
                    frameOffsetY += reference.RelativeOffsetY;
                }
            }
        }

        public static CssBox? GetLastLeftIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates)
        {
            // Asked for every word of every line: with no float anywhere in the document the answer is known before the
            // coordinates object below is built, and building it was a fifth of a multi-column document's allocation. The one
            // lookup the loop would have made is still counted.
            if (box.HtmlContainer is { HasFloatedBoxes: false } noFloats)
            {
                noFloats.RecordFloatScanCall();
                return null;
            }

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
                    Right = coordinates.MaxRight,
                    LeftFloatsOnly = true,
                    Reader = box,
                    SeekPastGap = Math.Abs(coordinates.CurrentX - coordinates.Line.ContentLeft) < 0.01
                };

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, Floating.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.FlowRightEdgeIn(box) + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;
            }

            return lastIntersectingFloat;
        }

        /// <summary>
        /// Whether this box establishes an independent formatting context, so floats outside it
        /// never reach its lines and its own floats never reach out. The cases that matter here are
        /// a grid or flex ITEM (css-grid-1 §6, css-flexbox-1 §4 — an item does so whatever its own
        /// display), a table cell, an inline-block, a float, an out-of-flow box, and anything with a
        /// non-visible overflow (CSS 2.1 §9.4.1). A multi-column container is one too (css-multicol-1 §2) and
        /// contains its floats (<see cref="ContainsItsFloats"/>), but is deliberately not answered here: this
        /// predicate also ends the float scans and the margin collapse through a box, and nothing in this
        /// engine narrows or moves an in-flow multi-column container beside a preceding float, so text in
        /// its columns has to keep seeing that float. Layout containment and paint containment (<see cref="AppliesLayoutOrPaintContainment"/>) each make a box
        /// one too (css-contain-2 §3.2, §3.4), which is also what contains its floats and stops its margins collapsing through it.
        /// </summary>
        internal static bool EstablishesIndependentFormattingContext(CssBox box)
        {
            if (box.ParentBox is null) return true;
            if (box.IsFloated || box.IsPageFloated) return true;
            if (box.Position.Value is PositionMode.Absolute or PositionMode.Fixed) return true;
            if (box.Overflow.Value is not Overflow.Visible) return true;
            if (box.DerivedStyle.IsFlowRoot) return true;
            if (AppliesLayoutOrPaintContainment(box)) return true;

            // The HTML rendering section gives a fieldset a formatting context of its own, and a button is one whatever its display
            // (a `display: block` button is not caught by the inline-block arm below).
            if (box.HtmlTag is { } tag
                && (tag.Name.Equals("fieldset", StringComparison.OrdinalIgnoreCase)
                    || tag.Name.Equals("button", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (box.DerivedStyle.ActualDisplay is Keywords.InlineBlock or Keywords.TableCell
                or Keywords.TableCaption or Keywords.Flex or Keywords.InlineFlex
                or Keywords.Grid or Keywords.InlineGrid)
            {
                return true;
            }

            return box.ParentBox.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex
                or Keywords.Grid or Keywords.InlineGrid;
        }

        /// <summary>
        /// The containment that applies to <paramref name="box"/>: what its <c>contain</c> and <c>content-visibility</c> ask for (<see cref="ContainGrammar.Of"/>),
        /// less what cannot apply to it. Layout and paint containment have no effect on a box that generates none, a non-atomic inline box, or an internal table box
        /// other than a table cell (css-contain-2 §3.2, §3.4); size containment does not apply to an internal table box (a caption is not one) or to a table either (§3.1).
        /// Cheap for the box that asks for nothing, which is nearly every one.
        /// </summary>
        internal static Containment AppliedContainment(CssBox box)
        {
            var asked = ContainGrammar.Of(box.Contain, box.ContentVisibility);

            // A query container applies style containment and size containment, on both axes or on the inline one only (css-contain-3 §3.1).
            var containerType = box.ContainerType.Value;
            // An auto element whose contents are skipped (not relevant to the user) is sized as an empty box (css-contain-2 §4).
            if (box.AutoSkipped) asked |= Containment.Size;

            if (containerType == PeachBrackets.Css.ContainerType.Size) asked |= Containment.Style | Containment.Size;
            else if (containerType == PeachBrackets.Css.ContainerType.InlineSize) asked |= Containment.Style | Containment.InlineSize;

            if (asked == Containment.None) return Containment.None;

            if (box.IsInline && !IsAtomicInline(box)) return Containment.None;

            var display = box.DerivedStyle.ActualDisplay;

            // No principal box: display: none, and display: contents (the box is a shell that was lifted out).
            if (display is Keywords.None or Keywords.Contents) return Containment.None;

            if (display is Keywords.TableRowGroup or Keywords.TableHeaderGroup or Keywords.TableFooterGroup or Keywords.TableRow or Keywords.TableColumn
                or Keywords.TableColumnGroup or Keywords.TableCaption)
            {
                return Containment.Style;
            }

            // Size containment does not apply to a table or a table cell either (css-contain-2 §3.1); layout and paint containment do.
            return display is Keywords.Table or Keywords.InlineTable or Keywords.TableCell ? asked & ~(Containment.Size | Containment.InlineSize) : asked;
        }

        /// <summary>
        /// Whether layout containment or paint containment applies to <paramref name="box"/>, which makes it an independent formatting context, the containing
        /// block of its absolute and fixed descendants and a stacking context (css-contain-2 §3.2, §3.4). Reads only the cascaded style, so it can be asked
        /// before layout.
        /// </summary>
        internal static bool AppliesLayoutOrPaintContainment(CssBox box) => (AppliedContainment(box) & (Containment.Layout | Containment.Paint)) != 0;

        /// <summary>Whether paint containment applies to <paramref name="box"/>: it clips to its padding edge.</summary>
        internal static bool AppliesPaintContainment(CssBox box) => (AppliedContainment(box) & Containment.Paint) != 0;

        /// <summary>
        /// Whether style containment applies to <paramref name="box"/> (css-contain-2 §3.3): the counters its subtree increments or sets and the quote nesting
        /// inside it are scoped to the subtree. It has no display exclusions.
        /// </summary>
        internal static bool AppliesStyleContainment(CssBox box) => (AppliedContainment(box) & Containment.Style) != 0;

        /// <summary>Whether layout containment applies to <paramref name="box"/>.</summary>
        internal static bool AppliesLayoutContainment(CssBox box) => (AppliedContainment(box) & Containment.Layout) != 0;

        /// <summary>
        /// Whether <paramref name="box"/> contains its own floats - grows to cover them
        /// (<see href="https://www.w3.org/TR/CSS21/visudet.html#root-height">CSS 2.1 §10.6.7</see>) and ends the
        /// walk in <see cref="LowestFloatBottomInOwnFormattingContext"/> for the box around it: a formatting
        /// context root (<see cref="EstablishesIndependentFormattingContext"/>) or a multi-column container
        /// (css-multicol-1 §2, "a multi-column container establishes a new independent formatting context").
        /// </summary>
        internal static bool ContainsItsFloats(CssBox box) =>
            box.EstablishesMultiColumnContext || EstablishesIndependentFormattingContext(box);

        /// <summary>
        /// The lowest bottom margin edge among the floats in <paramref name="box"/>'s <i>own</i> formatting
        /// context, or <see cref="double.NegativeInfinity"/> when it holds none — CSS 2.1
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#root-height">§10.6.7</see>'s "if the element
        /// has any floating descendants whose bottom margin edge is below the element's bottom content
        /// edge, then the height is increased to include those edges".
        /// </summary>
        /// <remarks>
        /// Only a box that establishes a formatting context of its own
        /// (<see cref="ContainsItsFloats"/>) asks this: an ordinary block does not
        /// contain its floats, which is why a float can overhang the block it sits in and why
        /// <c>clear</c> and the <c>overflow: hidden</c> containment idiom exist at all.
        /// <para>
        /// The walk stops descending at a nested formatting-context root, because that root already
        /// contains its own floats — its box bottom is ordinary content, already counted by the caller's
        /// content height. It also skips out-of-flow (absolute/fixed) descendants, which are not
        /// <paramref name="box"/>'s content at all (§9.3.1), and a float's own subtree for the same reason
        /// the nested-root case is skipped: a float is itself such a root.
        /// </para>
        /// </remarks>
        internal static double LowestFloatBottomInOwnFormattingContext(CssBox box)
        {
            var lowest = double.NegativeInfinity;
            Walk(box);
            return lowest;

            void Walk(CssBox parent)
            {
                foreach (var child in parent.Boxes)
                {
                    if (child.DerivedStyle.ActualDisplay == Keywords.None) continue;
                    if (child.Position.Value is PositionMode.Absolute or PositionMode.Fixed) continue;
                    if (child.IsRunningPositioned) continue;

                    if (child.IsFloated)
                    {
                        var staticBottom = child.ActualBottom + child.ActualMarginBottom;
                        for (var current = child;
                             current is not null && !ReferenceEquals(current, box);
                             current = current.ParentBox)
                        {
                            staticBottom -= current.RelativeOffsetY;
                        }

                        lowest = Math.Max(lowest, staticBottom);
                        continue;
                    }

                    if (ContainsItsFloats(child)) continue;

                    Walk(child);
                }
            }
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
            var narrowest = FindNarrowestRightFloatBox(box, coordinates.CurrentY, coordinates.Line.ContentLeft, ref boxesVisited);
            container.RecordFloatScanBoxVisits(boxesVisited);
            return narrowest;
        }

        /// <summary>
        /// Same ancestor/preceding-sibling traversal shape as <see cref="FindIntersectingFloatBox"/>,
        /// but accumulates the narrowest match across the whole walk instead of returning on the
        /// first hit - the wrap-limit query needs the binding constraint among every float:right box
        /// covering this row, not merely the first one the traversal order happens to reach.
        /// </summary>
        private static CssBox? FindNarrowestRightFloatBox(CssBox reference, double top, double lineLeft, ref int boxesVisited)
        {
            CssBox? narrowest = null;
            var narrowestLeft = double.PositiveInfinity;
            var reader = reference;

            while (reference.ParentBox is not null)
            {
                // A float cannot narrow a line outside its own formatting context (CSS 2.1 §9.5,
                // css-display-3 §2.1). Without this the walk runs to the document root and scans
                // every preceding sibling on the way, so a `float: right` in one grid item sets the
                // wrap limit for lines in the NEXT one.
                //
                // Tested at the STARTING level too, unlike FindIntersectingFloatBox, and the
                // difference is in what each is handed. This walk's `reference` comes from FlowBox's
                // word-flow loop: it is the block whose line is being laid out, so if IT establishes
                // a formatting context, the walk is already outside and has nothing to find.
                // FindIntersectingFloatBox is called from FloatBoxLeft/FloatBoxRight with a float
                // itself as `reference`, and a float always establishes one — breaking at its
                // starting level would stop it being positioned against its own siblings, which is
                // the whole job. Same rule, different starting box.
                // A multi-column container that was narrowed to clear a float is a formatting context root that has
                // already kept out of it (CssLayoutEngineColumns), so the float must not narrow the lines inside again.
                if (EstablishesIndependentFormattingContext(reference) || reference.ColumnsBesideFloats is not null) break;

                var currentBoxIdx = reference.ParentBox.IndexOfChild(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    ScanForNarrowestRightFloatBox(reference.ParentBox.Boxes[i], reader, top, lineLeft, ref narrowest, ref narrowestLeft, ref boxesVisited);
                }

                reference = reference.ParentBox;
            }

            return narrowest;
        }

        private static void ScanForNarrowestRightFloatBox(CssBox box, CssBox reader, double top, double lineLeft, ref CssBox? narrowest, ref double narrowestLeft, ref int boxesVisited)
        {
            boxesVisited++;

            // A float that lies wholly to the left of the line's content edge is not beside it at all. Only
            // reachable in a multi-column container, where the float sits in an earlier column but covers
            // the same block-axis rows as a line in a later one (css-multicol-1 §2: a float belongs to the
            // column box it appears in).
            // Where the float is in flow, in the frame of the line being laid out (CSS 2.1 section 9.4.3), not where it is drawn.
            var delta = box.IsFloated ? box.FrameDeltaFor(reader) : default;
            var flowX = box.FlowLeftEdge + delta.X;
            var flowTop = box.FlowTopEdge + delta.Y;
            var flowBottom = box.FlowBottomEdge + delta.Y;

            if (box.EffectiveFloatSide == Floating.Right && flowTop <= top && top < flowBottom
                && flowX + (box.ActualRight - box.Location.X) + box.ActualMarginRight > lineLeft)
            {
                var left = flowX - box.ActualMarginLeft;

                if (left < narrowestLeft)
                {
                    narrowestLeft = left;
                    narrowest = box;
                }
            }

            foreach (var childBox in box.Boxes)
            {
                ScanForNarrowestRightFloatBox(childBox, reader, top, lineLeft, ref narrowest, ref narrowestLeft, ref boxesVisited);
            }
        }

        /// <summary>
        /// How far along the inline axis (physical Y) floats pinned to the physical top and bottom edges reach into
        /// a vertical box's column at block-axis (physical X) position <paramref name="columnBlockAxisPoint"/>,
        /// measured from the top and from the bottom respectively - the room a line at that block position gives up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Mirrors <see cref="FindNarrowestRightFloatBox"/>'s ancestor/preceding-sibling traversal shape, used from
        /// <c>CssLayoutEngine.CreateVerticalLineBoxes</c> once per column rather than once per word (a column's own
        /// block-axis position, unlike a horizontal line's right-float wrap boundary, never changes mid-column).
        /// </para>
        /// <para>
        /// <c>float: left</c>/<c>right</c> (and <c>clear</c>) are line-relative, not physical: they resolve against
        /// the containing block's writing mode, and in <c>vertical-rl</c>/<c>vertical-lr</c> line-left is the
        /// physical top and line-right the physical bottom whatever the <c>direction</c>
        /// (<see href="https://www.w3.org/TR/css-writing-modes-4/#line-mappings">CSS Writing Modes 4 §6.4</see>,
        /// <see href="https://www.w3.org/TR/css-writing-modes-4/#text-align">§7.5</see>). So a float sits at the
        /// current block-axis position of its container and slides along the inline axis to the top or the bottom,
        /// which is what <c>CssBox.VerticalFloatOccupancy</c> records for the scan below.
        /// </para>
        /// </remarks>
        public static (double Top, double Bottom) GetVerticalFloatInsets(
            CssBox reference, double columnBlockAxisPoint, bool blockStartIsRight, double readerTop, double readerBottom, double columnBlockExtent = 0)
        {
            var container = reference.HtmlContainer;
            container?.RecordFloatScanCall();

            // See GetFirstIntersectingFloatBox above for why this short-circuit exists.
            if (container?.HasFloatedBoxes != true)
            {
                return (0, 0);
            }

            var boxesVisited = 0;
            double top = 0, bottom = 0;

            while (reference.ParentBox is not null)
            {
                var currentBoxIdx = reference.ParentBox.IndexOfChild(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    ScanForVerticalFloatInsets(reference.ParentBox.Boxes[i], columnBlockAxisPoint, blockStartIsRight,
                        readerTop, readerBottom, columnBlockExtent, ref top, ref bottom, ref boxesVisited);
                }

                reference = reference.ParentBox;
            }

            container.RecordFloatScanBoxVisits(boxesVisited);
            return (top, bottom);
        }

        private static void ScanForVerticalFloatInsets(CssBox box, double columnBlockAxisPoint, bool blockStartIsRight,
            double readerTop, double readerBottom, double columnBlockExtent, ref double top, ref double bottom, ref int boxesVisited)
        {
            boxesVisited++;

            // Only a float the vertical block flow itself placed (CssBox.VerticalFloatOccupancy): it says how far
            // along the inline axis, from the physical top or bottom edge of the box that placed it, the float
            // reaches, which the reach translates into the reader's own edges.
            if (box.IsFloated && box.VerticalFloatOccupancy is { } reach
                && VerticalFloatCoversBlockPoint(box, columnBlockAxisPoint, blockStartIsRight, columnBlockExtent))
            {
                reach.GrowInsets(readerTop, readerBottom, ref top, ref bottom);
            }

            foreach (var childBox in box.Boxes)
            {
                ScanForVerticalFloatInsets(childBox, columnBlockAxisPoint, blockStartIsRight, readerTop, readerBottom, columnBlockExtent,
                    ref top, ref bottom, ref boxesVisited);
            }
        }

        /// <summary>
        /// Whether a column whose leading (not-yet-consumed) edge is at <paramref name="columnBlockAxisPoint"/> lies
        /// within <paramref name="floated"/>'s block-axis range. The range is half-open at its block-end edge: a
        /// column starting exactly where the float ends is beyond it, while one starting at the float's block-start
        /// edge is beside it, however thin the column.
        /// </summary>
        internal static bool VerticalFloatCoversBlockPoint(CssBox floated, double columnBlockAxisPoint, bool blockStartIsRight,
            double columnBlockExtent = 0)
        {
            var left = floated.FlowLeftEdge - floated.ActualMarginLeft;
            var right = floated.FlowRightEdge + floated.ActualMarginRight;

            if (columnBlockExtent > 0)
            {
                // The column's whole prospective block-axis span, not just its leading edge: a thick column that
                // starts before the float but reaches into it is beside it. Positive-width overlap only, so a
                // column ending exactly where the float begins stays clear.
                const double tolerance = 0.01;

                return blockStartIsRight
                    ? left < columnBlockAxisPoint - tolerance && columnBlockAxisPoint - columnBlockExtent < right - tolerance
                    : columnBlockAxisPoint + columnBlockExtent > left + tolerance && columnBlockAxisPoint < right;
            }

            return blockStartIsRight
                ? left < columnBlockAxisPoint && columnBlockAxisPoint <= right
                : left <= columnBlockAxisPoint && columnBlockAxisPoint < right;
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
        internal static bool NeedsStackingHoist(CssBox box) =>
            box.IsOutOfFlow || box.IsPositioned || IsStackingContextBox(box);

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
            if (box.ContainerType.Value is PeachBrackets.Css.ContainerType.Size or PeachBrackets.Css.ContainerType.InlineSize) return true;

            foreach (var child in box.Boxes)
            {
                if (AnyBoxEstablishesSizeContainer(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether any box in <paramref name="box"/>'s subtree needs the target-page map
        /// <see cref="HtmlContainerInt.PerformLayoutOnePass"/>'s convergence loop builds: either it
        /// resolved a <c>target-counter(_, page)</c> content token against a page map that did not exist
        /// yet (<see cref="CssBox.HasPendingTargetPageContent"/>, set by
        /// <c>CssContentEngine.AppendTargetCounter</c> during the DOM-construction-time content pass), or
        /// it is a declarative API <c>PageNumberWithinSection</c>/<c>TotalPagesWithinSection</c> span
        /// (<see cref="CssBox.SectionPageCounterSectionId"/>), which needs the same map to locate its own
        /// section's begin/end markers' pages. Gates that loop the same way
        /// <see cref="AnyBoxClonesDecorations"/>/<see cref="AnyBoxEstablishesSizeContainer"/> gate their
        /// own loops - the overwhelming majority of documents use none of these, and for those the loop
        /// must cost nothing beyond this one tree walk.
        /// </summary>
        internal static bool AnyBoxHasTargetPageContent(CssBox box)
        {
            if (box.HasPendingTargetPageContent || box.SectionPageCounterSectionId is not null) return true;

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

        /// <summary>
        /// Whether <paramref name="box"/>'s <em>used</em> <c>transform-style</c> is <c>preserve-3d</c> (CSS Transforms 2 §6.1): the computed value
        /// (which only applies to a transformable box), unless a grouping property forces <c>flat</c> - <c>overflow</c> other than <c>visible</c>, <c>opacity</c> below 1, a <c>filter</c>,
        /// <c>backdrop-filter</c>, <c>clip-path</c>, <c>clip</c> or a <c>mix-blend-mode</c> other than <c>normal</c>. Such a box's children are flattened into it.
        /// </summary>
        public static bool EstablishesPreserve3d(CssBox box) =>
            box.IsPreserve3dRequested
            && !IsIsolated(box)
            && box.Overflow.Value == PeachBrackets.Css.Overflow.Visible
            && box.IsOpaque
            && box.ActualMixBlendMode == BlendMode.Normal
            && box.ActualFilterFunctions.Count == 0
            && box.ActualBackdropFilterFunctions.Count == 0
            && (string.IsNullOrEmpty(box.ClipPath) || box.ClipPath == Keywords.None)
            && (string.IsNullOrEmpty(box.Clip) || box.Clip == Keywords.Auto);

        /// <summary>Whether <paramref name="box"/> has <c>isolation: isolate</c> (CSS Compositing 1 §3.2).</summary>
        public static bool IsIsolated(CssBox box) => box.Isolation.Value == IsolationMode.Isolate;

        /// <summary>
        /// Whether painting <paramref name="box"/> has to build it as an isolated group of its own (CSS Compositing 1 §3.2): it has
        /// <c>isolation: isolate</c> <em>and</em> something inside it blends (<c>mix-blend-mode</c>), which is the one thing an
        /// isolated group changes. Without a blending descendant the group would paint the same as the plain content, so none is built.
        /// </summary>
        public static bool NeedsIsolatedGroup(CssBox box) => IsIsolated(box) && HasBlendingDescendant(box);

        private static bool HasBlendingDescendant(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.ActualMixBlendMode != BlendMode.Normal || HasBlendingDescendant(child)) return true;
            }

            return false;
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

            // Layout containment and paint containment each create a stacking context (css-contain-2 §3.2, §3.4).
            if (AppliesLayoutOrPaintContainment(box))
            {
                return true;
            }

            // Fixed/sticky always get their own stacking context; a running box (css-gcpm-3) joins them
            // here rather than the ZIndex-gated Absolute/Relative arm above - once laid out standalone
            // against a margin box (RunningElementLayout) it needs an unconditional stacking context for
            // its own descendants, matching DerivedStyle.IsPositioned's own treatment of Running.
            if (box.Position.Value is PositionMode.Fixed or PositionMode.Sticky or PositionMode.Running || box.FixedInContainment)
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

            // Opacity less than 1 and any transform other than `none` each establish a stacking context per
            // spec, regardless of `position` - both paint as isolated, self-contained units (an offscreen
            // composited group for opacity; a pushed/popped matrix for a non-identity transform), so
            // painting their descendants as one atomic block here matches what already happens visually.
            if (!box.IsOpaque)
            {
                return true;
            }

            // CSS Transforms 1 §2 keys on the computed value, not the matrix: `rotate(0deg)`, `scale(1)`
            // and `translateZ(0)` resolve to the identity yet still establish a stacking context, so this
            // reads HasTransform. Paint keeps IsTransformed, where an identity matrix really is a no-op.
            if (box.HasTransform)
            {
                return true;
            }

            // A perspective other than `none` does too (CSS Transforms 2 §Perspective), whatever the
            // transform of the box itself.
            if (box.ActualPerspective > 0)
            {
                return true;
            }

            // transform-style: preserve-3d (its used value, see EstablishesPreserve3d) establishes a stacking context too (CSS Transforms 2 §6.1),
            // so a positioned descendant of one of its planes stays with that plane instead of escaping to the 3D rendering context's root.
            if (EstablishesPreserve3d(box))
            {
                return true;
            }

            // A non-normal mix-blend-mode forces isolation (CSS Compositing and Blending Level 1 §2: "the
            // element ... creates a new stacking context"), matching the opacity/transform arms above -
            // it's painted as an isolated composite unit already (FragmentPainter routes it through the
            // same offscreen-tile path), so its descendants must paint as one atomic block here too.
            if (box.ActualMixBlendMode != BlendMode.Normal)
            {
                return true;
            }

            // isolation: isolate makes a stacking context too (CSS Compositing 1 section 3.2), whatever its content.
            if (IsIsolated(box))
            {
                return true;
            }

            // Any filter other than `none` forces a stacking context (Filter Effects Level 1 §3, per its
            // own note deferring to CSS3 Positioning) - true even for a filter list every function of which
            // is a documented no-op here, since the rule is about the property's presence, not this
            // renderer's paint-time coverage of it.
            if (box.ActualFilterFunctions.Count > 0)
            {
                return true;
            }

            // backdrop-filter does too (Filter Effects Level 2 §3.1: "a value other than none results in the creation of both a
            // stacking context and a Containing Block for absolute and fixed position descendants").
            if (box.ActualBackdropFilterFunctions.Count > 0)
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

        /// <param name="box">The box to test and, if it is not itself a blocker, to look inside.</param>
        /// <param name="coordinates">The area a float has to intersect to be returned.</param>
        /// <param name="floatProp">Which side's floats to look for.</param>
        /// <param name="boxesVisited">Accumulator: every box examined is added to it.</param>
        /// <param name="offsetX">How far the boxes above <paramref name="box"/>, down from the earlier sibling the search started from, have been moved sideways by relative positioning.</param>
        /// <param name="offsetY">The same, vertically.</param>
        /// <param name="isSibling">Whether <paramref name="box"/> is a sibling of the box being placed, rather than something inside an earlier sibling.</param>
        private static CssBox? GetNextIntersectingFloatBox(CssBox box, CssFloatCoordinates coordinates, Floating floatProp, ref int boxesVisited,
            double offsetX, double offsetY, bool isSibling = false)
        {
            boxesVisited++;

            // A relative offset is visual only (CSS 2.1 §9.4.3), so where a float goes is decided against where its blockers are in flow; but
            // everything inside a relatively positioned box moves with it, so the offsets of the boxes a blocker lies in have to come off too.
            if (!coordinates.LeftFloatsOnly)
            {
                offsetX += box.RelativeOffsetX;
                offsetY += box.RelativeOffsetY;
            }
            else if (box.IsFloated)
            {
                // The line flow's lookup measures against the border boxes as laid out, in the frame of the lines it reads for: the text beside a float
                // goes by where the float is in flow, not by where it is drawn, so its own offset comes off, and so do those of the boxes it lies in that the
                // reader's lines do not (a float in a relatively positioned box before the reader is drawn with that box's offset, the reader's text is not).
                var delta = coordinates.Reader is { } reader ? box.FrameDeltaFor(reader) : default;
                offsetX += box.RelativeOffsetX - delta.X;
                offsetY += box.RelativeOffsetY - delta.Y;
            }

            if (IsFloatIntersecting(coordinates, floatProp, box, offsetX, offsetY))
            {
                coordinates.BlockerOffsetX = offsetX;
                coordinates.BlockerOffsetY = offsetY;
                return box;
            }

            // An inline-block (or inline-flex, -grid, -table) already on the line is content a right float has to stay clear of, as one that
            // would overlap it goes below the line (§9.5.1 rules 1-3: the float's margin box may not overlap a line box's content). It is not
            // looked into, as below, but it counts by its own margin box. A left float is not given the same: the words on its line are moved
            // beside it (ShiftLineBesideFloat), which an already placed box with content of its own cannot be. Only a sibling of the float is on
            // its line: one in an earlier block is on that block's own line, whatever it overflows.
            if (isSibling && floatProp == Floating.Right && IsInlineLevelFormattingContext(box) && IsLineContentIntersecting(coordinates, box, offsetX, offsetY))
            {
                coordinates.BlockerOffsetX = offsetX;
                coordinates.BlockerOffsetY = offsetY;
                return box;
            }

            // What a float does inside a box that is a formatting context of its own stays inside it (CSS 2.1 §9.4.1), and that includes a
            // float, whose own contents never affect another float. Looking inside let a float in a fixed-height `overflow: hidden` card, a
            // flex or grid container or an absolutely positioned panel push a later float down, though it lies outside what that box takes
            // up in the flow.
            if (EstablishesIndependentFormattingContext(box)) return null;

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetNextIntersectingFloatBox(childBox, coordinates, floatProp, ref boxesVisited, offsetX, offsetY);
                if (foundBox != null)
                {
                    return foundBox;
                }
            }

            return null;
        }

        /// <summary>
        /// How far two floats' margin boxes may overlap and still count as fitting side by side. Widths such as 70% and 30% of the
        /// same container sum to it only up to rounding, which must not send the second float below the first. A used width is a
        /// single-precision value, so two such widths can be off by a few 1e-5 points. A browser floors each width to its 1/64 pixel layout
        /// unit (0.0117pt), so a row of a few floats can be a few hundredths of a point over and still be one row there: 0.02pt (0.027 pixel)
        /// is above that, and a browser starts to split rows at about the same overlap (measured between 0.02 and 0.03 pixel). Every test of whether a float
        /// fits beside another has to use it (the blocker test below and the fit tests in <c>CssLayoutEngine.FloatBoxLeft</c> and
        /// <c>FloatBoxRight</c>), or the two disagree about a pair that is one rounding error too wide.
        /// </summary>
        internal const double FloatFitTolerance = 0.02;

        /// <summary>An inline-block, inline-flex, inline-grid or inline-table box: atomic inline content that is a formatting context of its own.</summary>
        internal static bool IsInlineLevelFormattingContext(CssBox box) =>
            box.IsInline && box.DerivedStyle.ActualDisplay is Keywords.InlineBlock or Keywords.InlineTable or Keywords.InlineFlex or Keywords.InlineGrid;

        /// <summary>
        /// The right edge of what a float placed beside <paramref name="box"/> has to clear: a float's border box ends where it ends, and an
        /// inline-level box's likewise (the margin is added by the caller).
        /// </summary>
        internal static double BlockerRight(CssBox box) => box.IsFloated ? box.ActualRight : box.Location.X + box.ActualBoxSizingWidth;

        /// <summary>The bottom edge of the border box of a blocker, a float's or an inline-level box's.</summary>
        internal static double BlockerBottom(CssBox box) => box.IsFloated ? box.ActualBottom : box.Location.Y + box.ActualBoxSizingHeight;

        /// <summary>
        /// Whether the right float being placed overlaps <paramref name="box"/>, an inline-level formatting context on the line it starts on, by
        /// its horizontal margin edges. The vertical test is the one floats get: the box starts no lower than the float's outer top and its
        /// border box ends below it.
        /// </summary>
        private static bool IsLineContentIntersecting(CssFloatCoordinates coordinates, CssBox box, double offsetX, double offsetY)
        {
            if (!(coordinates.OuterTop < BlockerBottom(box) - offsetY) || !(box.Location.Y - offsetY <= coordinates.OuterTop)) return false;

            var left = box.Location.X - offsetX - box.ActualMarginLeft;
            var right = BlockerRight(box) - offsetX + box.ActualMarginRight;

            return left < coordinates.Right + coordinates.MarginRight
                && right > coordinates.FloatRightStartX - coordinates.MarginLeft + FloatFitTolerance;
        }

        private static bool IsFloatIntersecting(CssFloatCoordinates coordinates, Floating floatProp, CssBox targetBox, double offsetX, double offsetY)
        {
            if (!targetBox.IsFloated) return false;

            // vertical conflict. Placing a float compares margin boxes (CSS 2.1 §9.5.1 rules 1-3): a blocker's margin-top starts it above its
            // border box, and a negative margin-bottom ends it above its border box, where the next float may start. The line flow's lookup
            // keeps the border box, which is what the lines beside a float have always been measured against.
            var targetTop = targetBox.Location.Y - offsetY;
            var targetBottom = targetBox.ActualBottom - offsetY;

            if (!coordinates.LeftFloatsOnly)
            {
                targetTop -= targetBox.ActualMarginTop;
                targetBottom += targetBox.ActualMarginBottom;
            }

            if (!(coordinates.OuterTop < targetBottom) || !(targetTop <= coordinates.OuterTop)) return false;

            var targetRight = targetBox.ActualRight - offsetX + targetBox.ActualMarginRight;
            var targetLeft = targetBox.Location.X - offsetX - targetBox.ActualMarginLeft;

            var currentLeft = coordinates.Left - coordinates.MarginLeft;

            switch (floatProp)
            {
                case Floating.Left when coordinates.SeekPastGap && targetRight > currentLeft
                    && targetBox.EffectiveFloatSide == Floating.Left:
                case Floating.Left when targetRight > currentLeft && targetLeft <= currentLeft
                    && (!coordinates.LeftFloatsOnly || targetBox.EffectiveFloatSide == Floating.Left):
                // A float past the containing block's right edge, which this one may extend out to meet (rule 9: as far right as it can go). One
                // inside the containing block and to the right of the spot being tried is a blocker this float has already moved away from; taking
                // it for one again sent a third float back and forth between two of them for ever.
                case Floating.Right when targetLeft > coordinates.FloatRightStartX + coordinates.ReferenceWidth + coordinates.MarginRight
                    && (double.IsPositiveInfinity(coordinates.ContainingRight) || targetLeft >= coordinates.ContainingRight)
                    && !(targetLeft >= coordinates.ContainingRight && targetLeft < coordinates.MulticolRight):
                // A float beside this one, in the same containing block: CSS 2.1 §9.5.1 rules 1-3 keep the
                // outer edges of floats from overlapping, so an earlier float whose margin box shares any of
                // the width this right float would take is a blocker (and a left one pushes it down below it).
                case Floating.Right when targetLeft < coordinates.Right + coordinates.MarginRight
                    && targetRight > coordinates.FloatRightStartX - coordinates.MarginLeft + FloatFitTolerance
                    && !(targetLeft >= coordinates.ContainingRight && targetLeft < coordinates.MulticolRight):
                // The mirror of that: an earlier right float beside this left one. This float's left edge is not inside it, but its margin box
                // reaches into the blocker's, so rules 1-3 put it below the blocker rather than on top of it. The point test above alone left it
                // at the top, over the blocker, whenever the two together were wider than the container.
                case Floating.Left when !coordinates.LeftFloatsOnly && targetBox.EffectiveFloatSide == Floating.Right
                    && targetLeft < coordinates.Left + coordinates.ReferenceWidth + coordinates.MarginRight - FloatFitTolerance
                    && targetRight > currentLeft:
                    return true;
                default:
                    return false;
            }
        }
    }
}
