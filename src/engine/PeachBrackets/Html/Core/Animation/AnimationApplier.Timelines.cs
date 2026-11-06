using System;
using System.Collections.Generic;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// A scroll or view timeline an animation runs on, once its name or function has been looked up on the element: what scrolls (the document, or a scroll container of a
    /// document shown on a screen), along which axis, and for a view timeline the box whose visibility it follows.
    /// </summary>
    /// <param name="IsView">a view timeline (the visibility of <paramref name="Subject"/>) and not a scroll timeline (the scroll position)</param>
    /// <param name="Subject">the box a view timeline follows</param>
    /// <param name="Axis"><c>block</c>, <c>inline</c>, <c>x</c> or <c>y</c></param>
    /// <param name="Inset">the view timeline's inset as written, or null for <c>auto</c></param>
    /// <param name="Scroller">the scroll container the timeline follows, or null for the document's own scrolling</param>
    internal sealed record ResolvedTimeline(bool IsView, CssBox? Subject, string Axis, string? Inset, CssBox? Scroller = null);

    internal static partial class AnimationApplier
    {
        /// <summary>
        /// Looks up the timeline an <c>animation-timeline</c> entry means for <paramref name="box"/>. Returns no timeline and not inactive for <c>auto</c> (the animation runs on
        /// time), no timeline and inactive for one that does not exist (the animation has no effect), and the timeline otherwise.
        /// </summary>
        internal static (ResolvedTimeline? Timeline, bool Inactive) ResolveTimeline(CssBox box, TimelineReference reference)
        {
            switch (reference.Kind)
            {
                case TimelineKind.Auto:
                    return (null, false);

                case TimelineKind.None:
                    return (null, true);

                case TimelineKind.Scroll:
                    // The scroller is the document (root), the nearest scroll container above the element (the document when there is none), or the element itself, which
                    // has no timeline when it does not scroll.
                    if (reference.Scroller == "self")
                        return DomUtils.IsScrollerOnScreen(box) ? (new ResolvedTimeline(false, null, reference.Axis ?? "block", null, box), false) : (null, true);

                    var scroller = reference.Scroller == "root" ? null : NearestScroller(box);
                    return (new ResolvedTimeline(false, null, reference.Axis ?? "block", null, scroller), false);

                case TimelineKind.View:
                    return (new ResolvedTimeline(true, box, reference.Axis ?? "block", reference.Inset, NearestScroller(box)), false);

                default:
                    return ResolveNamed(box, reference.Name!);
            }
        }

        // The nearest scroll container above a box in a document shown on a screen, which is what scrolls it; null when the document itself does.
        private static CssBox? NearestScroller(CssBox box)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (DomUtils.IsScrollerOnScreen(ancestor)) return ancestor;
            }

            return null;
        }

        private static (ResolvedTimeline? Timeline, bool Inactive) ResolveNamed(CssBox box, string name)
        {
            // The element and its ancestors, nearest first, that declare the name.
            for (var ancestor = box; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (Declares(ancestor, name) is { } found) return Build(found);
            }

            // timeline-scope on an ancestor hoists a name declared below it, so the elements under that ancestor can use it.
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                var scope = SplitList(ancestor.TimelineScope);
                if (scope.Exists(entry => entry == name || entry.Equals("all", StringComparison.OrdinalIgnoreCase)) && FindDeclaring(ancestor, name) is { } hoisted)
                    return Build(hoisted);
            }

            return (null, true);

            static (ResolvedTimeline? Timeline, bool Inactive) Build((CssBox Declarer, bool IsView, int Index) found)
            {
                var (declarer, isView, index) = found;
                if (isView)
                {
                    var axis = Cycle(SplitList(declarer.ViewTimelineAxis), index).Trim().ToLowerInvariant();
                    var inset = Cycle(SplitList(declarer.ViewTimelineInset), index).Trim();
                    return (new ResolvedTimeline(true, declarer, axis.Length > 0 ? axis : "block", inset.Length > 0 && !inset.Equals("auto", StringComparison.OrdinalIgnoreCase) ? inset : null,
                        NearestScroller(declarer)), false);
                }

                // A scroll timeline of the root is the document's own scrolling; of a scroll container it is that container's; any other box has no scroll position.
                var onScroller = DomUtils.IsScrollerOnScreen(declarer);
                if (!onScroller && declarer.ParentBox is not null && declarer.HtmlTag is not { Name: "html" or "body" }) return (null, true);

                var scrollAxis = Cycle(SplitList(declarer.ScrollTimelineAxis), index).Trim().ToLowerInvariant();
                return (new ResolvedTimeline(false, null, scrollAxis.Length > 0 ? scrollAxis : "block", null, onScroller ? declarer : null), false);
            }
        }

        // The box itself, when it declares the name as a scroll or a view timeline (a view timeline of a box that declares both is the one the name means last).
        private static (CssBox Declarer, bool IsView, int Index)? Declares(CssBox box, string name)
        {
            var views = SplitList(box.ViewTimelineName);
            for (var i = 0; i < views.Count; i++)
            {
                if (views[i] == name) return (box, true, i);
            }

            var scrolls = SplitList(box.ScrollTimelineName);
            for (var i = 0; i < scrolls.Count; i++)
            {
                if (scrolls[i] == name) return (box, false, i);
            }

            return null;
        }

        private static (CssBox Declarer, bool IsView, int Index)? FindDeclaring(CssBox from, string name)
        {
            if (Declares(from, name) is { } own) return own;

            foreach (var child in from.Boxes)
            {
                if (FindDeclaring(child, name) is { } below) return below;
            }

            return null;
        }
    }
}
