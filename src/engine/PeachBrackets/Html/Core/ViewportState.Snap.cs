using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html
{
    /// <summary>
    /// What a scroll container does with a scroll beyond moving the content: it settles on a snap position (CSS Scroll Snap 1), glides when asked to (<c>scroll-behavior:
    /// smooth</c>, CSSOM View §3), and keeps its overscroll from the container around it (CSS Overscroll Behavior 1).
    /// </summary>
    internal sealed partial class ViewportState
    {
        private const double SmoothSeconds = 0.3;
        private const double ProximityShare = 1d / 3;

        /// <summary>A scroll in progress that eases to where it is going.</summary>
        private sealed class Tween(double fromX, double fromY, double toX, double toY)
        {
            public double FromX { get; } = fromX;
            public double FromY { get; } = fromY;
            public double ToX { get; } = toX;
            public double ToY { get; } = toY;

            /// <summary>The time it began, in seconds, set at its first step.</summary>
            public double Start { get; set; } = double.NaN;
        }

        private readonly Dictionary<CssBox, Tween> _tweens = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether any container is gliding to a position.</summary>
        internal bool HasTweens => _tweens.Count > 0;

        #region Reading the properties

        /// <summary>The scroll margin of an element on each side, in layout units (CSS Scroll Snap 1 §3.1).</summary>
        internal static (double Left, double Top, double Right, double Bottom) Margins(CssBox box) => (
            Length(box.ScrollMarginLeft, box, 0), Length(box.ScrollMarginTop, box, 0), Length(box.ScrollMarginRight, box, 0), Length(box.ScrollMarginBottom, box, 0));

        /// <summary>The scroll padding of a container on each side, in layout units; a percentage is of the scrollport (CSS Scroll Snap 1 §3.2).</summary>
        internal static (double Left, double Top, double Right, double Bottom) Paddings(CssBox box, Rect port) => (
            Length(box.ScrollPaddingLeft, box, port.Width), Length(box.ScrollPaddingTop, box, port.Height), Length(box.ScrollPaddingRight, box, port.Width),
            Length(box.ScrollPaddingBottom, box, port.Height));

        private static double Length(string text, CssBox box, double basis) =>
            string.IsNullOrWhiteSpace(text) || text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) ? 0 : CssValueParser.ParseLength(text, basis, box);

        /// <summary>Whether a scroll that has reached the end of <paramref name="box"/> along an axis stays with it: <c>overscroll-behavior</c> is <c>contain</c> or <c>none</c> there.</summary>
        internal static bool OverscrollContained(CssBox box, bool vertical) =>
            (vertical ? box.OverscrollBehaviorY : box.OverscrollBehaviorX)?.Trim().ToLowerInvariant() is "contain" or "none";

        internal static bool IsSmooth(CssBox box) => string.Equals(box.ScrollBehavior?.Trim(), "smooth", StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Navigating

        /// <summary>
        /// Scrolls <paramref name="box"/> toward (<paramref name="x"/>, <paramref name="y"/>) the way a scroll the user asked for does: settled on a snap position when the
        /// container snaps, and gliding when it says <c>scroll-behavior: smooth</c> (<paramref name="behavior"/> 0 follows the property, 1 is instant, 2 is smooth). Returns the
        /// scrollport that looks different now (nothing when the move is a glide that has not begun, or the container was already there).
        /// </summary>
        internal Rect? Navigate(HtmlContainerInt container, CssBox box, double x, double y, bool snap, int behavior)
        {
            if (ScrollStateOf(container, box) is not { } state)
                return null;

            var fromX = state.X;
            var fromY = state.Y;
            x = double.IsNaN(x) ? state.X : Math.Clamp(x, 0, state.MaxX);
            y = double.IsNaN(y) ? state.Y : Math.Clamp(y, 0, state.MaxY);
            if (snap) (x, y) = Snapped(container, box, state, x, y, fromX, fromY);

            var smooth = behavior == 2 || (behavior == 0 && IsSmooth(box));
            if (smooth && !container.Preferences.ReducedMotion)
            {
                // A glide begins at its first step (the host is told to ask for one), and a new one replaces the one that was going.
                if (x == state.X && y == state.Y && !_tweens.ContainsKey(box)) return null;

                _tweens[box] = new Tween(state.X, state.Y, x, y);
                container.AnimationWakeRequested?.Invoke();
                return null;
            }

            _tweens.Remove(box);
            return SetScroll(container, box, x, y);
        }

        /// <summary>Moves every glide on to <paramref name="nowSeconds"/>. Returns the scrollports that look different and whether any glide is still going.</summary>
        internal (List<(CssBox Scroller, Rect Port)> Moved, bool Running) StepTweens(HtmlContainerInt container, double nowSeconds)
        {
            var moved = new List<(CssBox, Rect)>();
            if (_tweens.Count == 0)
                return (moved, false);

            foreach (var (box, tween) in new List<KeyValuePair<CssBox, Tween>>(_tweens))
            {
                if (double.IsNaN(tween.Start)) tween.Start = nowSeconds;

                var t = Math.Clamp((nowSeconds - tween.Start) / SmoothSeconds, 0, 1);
                var eased = 1 - Math.Pow(1 - t, 3);
                var x = tween.FromX + ((tween.ToX - tween.FromX) * eased);
                var y = tween.FromY + ((tween.ToY - tween.FromY) * eased);
                if (t >= 1) _tweens.Remove(box);

                if (SetScroll(container, box, x, y) is { } port) moved.Add((box, port));
            }

            return (moved, _tweens.Count > 0);
        }

        #endregion

        #region Snapping

        /// <summary>One position a container can settle on along an axis, and whether it may not be passed over (<c>scroll-snap-stop: always</c>).</summary>
        private readonly record struct SnapPosition(double Offset, bool Stop);

        /// <summary>
        /// Where a scroll that was going to (<paramref name="x"/>, <paramref name="y"/>) from (<paramref name="fromX"/>, <paramref name="fromY"/>) settles: on the nearest snap position
        /// of an axis the container snaps on (CSS Scroll Snap 1 §6). <c>mandatory</c> always settles; <c>proximity</c> only when a position is near. A position with
        /// <c>scroll-snap-stop: always</c> between the start and the end is not passed over.
        /// </summary>
        internal (double X, double Y) Snapped(HtmlContainerInt container, CssBox box, ScrollState state, double x, double y, double fromX, double fromY)
        {
            if (!ScrollGrammar.TryParseSnapType(box.ScrollSnapType ?? "none", out var axis, out var mandatory) || axis == "none")
                return (x, y);

            var (snapX, snapY) = axis switch { "x" or "inline" => (true, false), "y" or "block" => (false, true), _ => (true, true) };
            if (!snapX && !snapY) return (x, y);

            var (xs, ys) = SnapPositions(container, box, state);
            if (snapX) x = Settle(xs, x, fromX, state.MaxX, state.Port.Width, mandatory);
            if (snapY) y = Settle(ys, y, fromY, state.MaxY, state.Port.Height, mandatory);
            return (x, y);
        }

        private static double Settle(List<SnapPosition> positions, double desired, double from, double max, double portSize, bool mandatory)
        {
            if (positions.Count == 0) return desired;

            // A position that may not be passed over holds the scroll back when it lies between where it began and where it was going.
            var low = Math.Min(from, desired);
            var high = Math.Max(from, desired);
            SnapPosition? stop = null;
            foreach (var position in positions)
            {
                if (!position.Stop || position.Offset <= low + 0.01 || position.Offset >= high - 0.01) continue;
                if (stop is null || Math.Abs(position.Offset - from) < Math.Abs(stop.Value.Offset - from)) stop = position;
            }

            if (stop is { } held) return Math.Clamp(held.Offset, 0, max);

            var best = positions[0].Offset;
            foreach (var position in positions)
            {
                if (Math.Abs(position.Offset - desired) < Math.Abs(best - desired)) best = position.Offset;
            }

            if (!mandatory && Math.Abs(best - desired) > portSize * ProximityShare) return desired;
            return Math.Clamp(best, 0, max);
        }

        // The scroll positions at which each snap area of the container lines up with its snapport (the scrollport less the scroll-padding), on each axis.
        private (List<SnapPosition> X, List<SnapPosition> Y) SnapPositions(HtmlContainerInt container, CssBox scroller, ScrollState state)
        {
            var xs = new List<SnapPosition>();
            var ys = new List<SnapPosition>();
            if (container.FragmentTree is not { Fragmentainers.Count: > 0 } tree || IndexFor(container) is not { } index)
                return (xs, ys);

            var padding = Paddings(scroller, state.Port);
            var portLeft = state.Port.X + padding.Left;
            var portRight = state.Port.Right - padding.Right;
            var portTop = state.Port.Y + padding.Top;
            var portBottom = state.Port.Bottom - padding.Bottom;

            void Visit(BoxFragment fragment)
            {
                foreach (var child in fragment.Children)
                {
                    var box = child.Box;
                    if (ScrollGrammar.TryParseSnapAlign(box.ScrollSnapAlign ?? "none", out var block, out var inline) && (block != "none" || inline != "none") && NearestScroller(index, box) == scroller)
                    {
                        var stop = string.Equals(box.ScrollSnapStop?.Trim(), "always", StringComparison.OrdinalIgnoreCase);
                        var m = Margins(box);
                        var area = child.WholeBoxRect;
                        var left = area.X - m.Left;
                        var right = area.Right + m.Right;
                        var top = area.Y - m.Top;
                        var bottom = area.Bottom + m.Bottom;

                        if (inline != "none") xs.Add(new SnapPosition(Align(inline, left, right, portLeft, portRight), stop));
                        if (block != "none") ys.Add(new SnapPosition(Align(block, top, bottom, portTop, portBottom), stop));
                    }

                    Visit(child);
                }
            }

            Visit(FragmentOf(tree, scroller) ?? tree.Fragmentainers[0].Root);
            return (xs, ys);
        }

        // The scroll offset that puts the start, end or middle of the area (start to end) at the same place in the snapport.
        private static double Align(string alignment, double start, double end, double portStart, double portEnd) => alignment switch
        {
            "start" => start - portStart,
            "end" => end - portEnd,
            _ => ((start + end) / 2) - ((portStart + portEnd) / 2),
        };

        // The scroll container that scrolls the box: the nearest one around it.
        private static CssBox? NearestScroller(ScrollIndex index, CssBox box)
        {
            for (var current = ScrollParentOf(box); current is not null; current = ScrollParentOf(current))
            {
                if (index.Entries.ContainsKey(current)) return current;
            }

            return null;
        }

        private static BoxFragment? FragmentOf(FragmentTree tree, CssBox box)
        {
            BoxFragment? Find(BoxFragment fragment)
            {
                if (ReferenceEquals(fragment.Box, box)) return fragment;
                foreach (var child in fragment.Children)
                {
                    if (Find(child) is { } found) return found;
                }

                return null;
            }

            return Find(tree.Fragmentainers[0].Root);
        }

        /// <summary>Settles a container that has stopped moving on the nearest snap position (a thumb let go, a finger lifted).</summary>
        internal Rect? Settle(HtmlContainerInt container, CssBox box, int behavior)
        {
            if (ScrollStateOf(container, box) is not { } state)
                return null;

            return Navigate(container, box, state.X, state.Y, snap: true, behavior);
        }

        #endregion
    }
}
