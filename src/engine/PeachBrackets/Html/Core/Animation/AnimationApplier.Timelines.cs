using System;
using System.Collections.Generic;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// A scroll or view timeline an animation runs on, once its name or function has been looked up on the element: what scrolls (always the document, see
    /// <c>.claude/accepted-gaps</c>: no other box has a scroll position here), along which axis, and for a view timeline the box whose visibility it follows.
    /// </summary>
    /// <param name="IsView">a view timeline (the visibility of <paramref name="Subject"/>) and not a scroll timeline (the scroll position)</param>
    /// <param name="Subject">the box a view timeline follows</param>
    /// <param name="Axis"><c>block</c>, <c>inline</c>, <c>x</c> or <c>y</c></param>
    /// <param name="Inset">the view timeline's inset as written, or null for <c>auto</c></param>
    internal sealed record ResolvedTimeline(bool IsView, CssBox? Subject, string Axis, string? Inset);

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
                    // Only the document scrolls here: the nearest scroller is the document unless a scroll container is in the way, which has no scroll position (it would
                    // not be scrollable), and `self` is not one.
                    if (reference.Scroller == "self") return (null, true);
                    if (reference.Scroller == "nearest" && HasScrollContainerAbove(box)) return (null, true);
                    return (new ResolvedTimeline(false, null, reference.Axis ?? "block", null), false);

                case TimelineKind.View:
                    return HasScrollContainerAbove(box)
                        ? (null, true)
                        : (new ResolvedTimeline(true, box, reference.Axis ?? "block", reference.Inset), false);

                default:
                    return ResolveNamed(box, reference.Name!);
            }
        }

        // Whether a box between this one and the document scrolls by itself: it clips its content to a scroll position the engine does not model.
        private static bool HasScrollContainerAbove(CssBox box)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (MonolithicContent.IsScrollContainer(ancestor)) return true;
            }

            return false;
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
                    if (HasScrollContainerAbove(declarer)) return (null, true);
                    var axis = Cycle(SplitList(declarer.ViewTimelineAxis), index).Trim().ToLowerInvariant();
                    var inset = Cycle(SplitList(declarer.ViewTimelineInset), index).Trim();
                    return (new ResolvedTimeline(true, declarer, axis.Length > 0 ? axis : "block", inset.Length > 0 && !inset.Equals("auto", StringComparison.OrdinalIgnoreCase) ? inset : null), false);
                }

                // A scroll timeline of the root is the document's own scrolling; of any other box there is no scroll position here.
                if (declarer.ParentBox is not null && declarer.HtmlTag is not { Name: "html" or "body" }) return (null, true);

                var scrollAxis = Cycle(SplitList(declarer.ScrollTimelineAxis), index).Trim().ToLowerInvariant();
                return (new ResolvedTimeline(false, null, scrollAxis.Length > 0 ? scrollAxis : "block", null), false);
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
