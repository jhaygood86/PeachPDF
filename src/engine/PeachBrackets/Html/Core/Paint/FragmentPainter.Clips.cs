using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Utils;
using System.Collections.Generic;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// <c>overflow</c> clips and the descendants that escape them. CSS Overflow 3 §3 clips only a
    /// descendant whose containing-block chain passes through the clipping box, but the clips are canvas
    /// state, pushed by whatever frame is painting an ancestor and inherited by everything painted
    /// beneath it. A positioned box inside a non-positioned stacking context (<c>opacity</c>,
    /// <c>mix-blend-mode</c>...) is painted beneath its clipping ancestor's frame although its chain
    /// skips that ancestor, so the frame's clip has to be held back for it - and, for a group, held back
    /// from the group's composite, since the whole group is drawn through whatever clip is active.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>One clip held back from the canvas: the box whose padding edge it is, and how to push it.</summary>
        private readonly record struct ClipEntry(CssBox Clipper, Rect Rect, OverflowClipCurve? Curve);

        /// <summary>
        /// Clips of ancestors that some descendant being painted escapes. They are not on the canvas;
        /// each box that paints pushes the ones its own chain passes through (<see cref="PushClipsFor"/>),
        /// so content that is clipped still is, and an escaping box simply does not push them.
        /// </summary>
        private IReadOnlyList<ClipEntry> _deferredClips = [];

        /// <summary>What <see cref="PushClipsFor"/> pushed, for <see cref="PopClips"/> and for lifting.</summary>
        private readonly struct ClipScope(int canvasClips, int recorded, ClipEntry? own, List<ClipEntry>? deferred)
        {
            public int CanvasClips { get; } = canvasClips;
            public int Recorded { get; } = recorded;

            /// <summary>The fragment's own (nearest clipping ancestor's) clip, when it has one.</summary>
            public (Rect Rect, OverflowClipCurve? Curve)? Own { get; } = own is { } e ? (e.Rect, e.Curve) : null;

            /// <summary>The deferred entries that were on this box's chain and so were pushed.</summary>
            public List<ClipEntry>? Deferred { get; } = deferred;
        }

        /// <summary>
        /// Pushes every clip that applies to <paramref name="fragment"/>: the one its fragment carries and each
        /// deferred clip whose clipping box its containing-block chain passes through.
        /// </summary>
        private ClipScope PushClipsFor(Canvas g, BoxFragment fragment)
        {
            var (rect, curve) = OverflowClipOf(g, fragment);
            var canvasClips = RenderUtils.ClipGraphicsByOverflow(g, rect, curve);
            var recorded = PushOverflowClip(rect, curve) ? 1 : 0;

            List<ClipEntry>? deferred = null;
            if (_deferredClips.Count > 0)
            {
                var ownClipper = rect is null ? null : DomUtils.ClippingBoxOf(fragment.Box);

                foreach (var entry in _deferredClips)
                {
                    if (ReferenceEquals(entry.Clipper, ownClipper)
                        || !DomUtils.IsOnClippingChainOf(fragment.Box, entry.Clipper))
                    {
                        continue;
                    }

                    canvasClips += RenderUtils.ClipGraphicsByOverflow(g, entry.Rect, entry.Curve);
                    if (PushOverflowClip(entry.Rect, entry.Curve)) recorded++;
                    (deferred ??= []).Add(entry);
                }
            }

            return new ClipScope(canvasClips, recorded, rect is { } r ? new ClipEntry(fragment.Box, r, curve) : null, deferred);
        }

        private void PopClips(Canvas g, ClipScope scope)
        {
            for (var i = 0; i < scope.Recorded; i++)
                PopOverflowClip(true);

            for (var i = 0; i < scope.CanvasClips; i++)
                g.PopClip();
        }

        /// <summary>
        /// Whether one of <paramref name="participants"/> escapes a clip <paramref name="scope"/> pushed for
        /// <paramref name="fragment"/>, and if so the entries to hold back for them. Only an out-of-flow box (or one
        /// hoisted past an out-of-flow ancestor) can be off the chain of a clip its parent is on, so the common
        /// case costs a position check per participant.
        /// </summary>
        private List<ClipEntry>? EntriesEscapedBy(
            BoxFragment fragment, ClipScope scope, List<List<StackingOrder.StackingParticipant>> participants)
        {
            var mayEscape = false;
            foreach (var layer in participants)
                foreach (var p in layer)
                    mayEscape |= IsOutOfFlowOrBeneathOne(p);

            if (!mayEscape) return null;

            var entries = new List<ClipEntry>();
            if (scope.Own is { } own && DomUtils.ClippingBoxOf(fragment.Box) is { } ownClipper)
                entries.Add(new ClipEntry(ownClipper, own.Rect, own.Curve));
            else if (scope.Own is not null)
                return null; // a band-only clip has no clipping box to be off the chain of

            if (scope.Deferred is { } deferred) entries.AddRange(deferred);

            List<ClipEntry>? escaped = null;
            foreach (var entry in entries)
            {
                foreach (var layer in participants)
                    foreach (var p in layer)
                    {
                        if (IsOutOfFlowOrBeneathOne(p) && !DomUtils.IsOnClippingChainOf(p.Box, entry.Clipper))
                        {
                            (escaped ??= []).AddRange(entries);
                            return escaped;
                        }
                    }
            }

            return null;
        }

        private static bool IsOutOfFlowOrBeneathOne(StackingOrder.StackingParticipant p)
        {
            if (p.Box.Position.Value is PositionMode.Absolute or PositionMode.Fixed) return true;

            foreach (var ancestor in p.ClipAncestors)
            {
                if (ancestor.Box.Position.Value is PositionMode.Absolute or PositionMode.Fixed) return true;
            }

            return false;
        }

        /// <summary>Memo for <see cref="HasEscapingDescendant"/>: whether a fragment's subtree holds any out-of-flow box.</summary>
        private readonly Dictionary<BoxFragment, bool> _holdsOutOfFlow = new(ReferenceEqualityComparer.Instance);

        private bool HoldsOutOfFlowBox(BoxFragment fragment)
        {
            if (_holdsOutOfFlow.TryGetValue(fragment, out var known)) return known;

            var holds = false;
            foreach (var child in fragment.Children)
            {
                if (child.Box.Position.Value is PositionMode.Absolute or PositionMode.Fixed || HoldsOutOfFlowBox(child))
                {
                    holds = true;
                    break;
                }
            }

            return _holdsOutOfFlow[fragment] = holds;
        }

        /// <summary>
        /// Whether painting <paramref name="fragment"/> would draw an out-of-flow box that its containing-block
        /// chain keeps outside <paramref name="clipper"/>.
        /// </summary>
        private bool HasEscapingDescendant(BoxFragment fragment, CssBox clipper)
        {
            if (!HoldsOutOfFlowBox(fragment)) return false;

            foreach (var child in fragment.Children)
            {
                if (child.Box.Position.Value is PositionMode.Absolute or PositionMode.Fixed
                    && !DomUtils.IsOnClippingChainOf(child.Box, clipper))
                {
                    return true;
                }

                if (HasEscapingDescendant(child, clipper)) return true;
            }

            return false;
        }

        /// <summary><paramref name="entries"/> plus those of <paramref name="added"/> whose clipping box is not already in it.</summary>
        private static IReadOnlyList<ClipEntry> Combine(IReadOnlyList<ClipEntry> entries, IEnumerable<ClipEntry> added)
        {
            List<ClipEntry>? result = null;
            foreach (var entry in added)
            {
                var present = false;
                foreach (var existing in (IReadOnlyList<ClipEntry>?)result ?? entries)
                    present |= ReferenceEquals(existing.Clipper, entry.Clipper);

                if (!present) (result ??= [.. entries]).Add(entry);
            }

            return (IReadOnlyList<ClipEntry>?)result ?? entries;
        }

        private static IReadOnlyList<ClipEntry> Without(IReadOnlyList<ClipEntry> entries, List<ClipEntry> removed)
        {
            var result = new List<ClipEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (!removed.Exists(r => ReferenceEquals(r.Clipper, entry.Clipper))) result.Add(entry);
            }

            return result;
        }
    }
}
