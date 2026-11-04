using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html
{
    /// <summary>
    /// Where the viewport is on a continuous document, and how far that moves the boxes that follow it: a <c>position: fixed</c> box stays at
    /// the same place in the viewport (CSS Positioned Layout 3 §3.1.5), and a <c>position: sticky</c> box slides along with it until its
    /// containing block runs out (§3.4). Layout puts both where they are with the viewport at the top; this holds the displacement of each from
    /// that place, so the fragment tree itself stays as layout made it and paint and the geometry queries add the displacement on top.
    /// </summary>
    /// <remarks>
    /// Coordinates are layout units in the surface's own space: the origin is the top-left of the content area, the way every fragment
    /// rectangle of the one fragmentainer is. Only a continuous document has a viewport that moves; a paged document is always as it was laid out.
    /// </remarks>
    internal sealed class ViewportState
    {
        private Dictionary<CssBox, (double Dx, double Dy)> _displacements = new(ReferenceEqualityComparer.Instance);
        private ViewportIndex? _index;
        private (double X, double Y) _lastOrigin;
        private HashSet<CssBox> _attached = new(ReferenceEqualityComparer.Instance);
        private List<BoxFragment> _attachedRoots = [];

        /// <summary>Which layer the paint in progress is drawing; set by <see cref="HtmlDocument"/> around a paint call.</summary>
        internal DocumentPaintLayer Layer { get; set; }

        /// <summary>Whether any box is drawn by the viewport-attached layer, which is above the content that follows it in the source.</summary>
        internal bool HasAttached => _attached.Count > 0;

        /// <summary>Whether the box is drawn by the viewport-attached layer: a fixed box, or a sticky one the viewport has moved.</summary>
        internal bool IsAttached(CssBox box) => _attached.Count > 0 && _attached.Contains(box);

        /// <summary>The attached fragments that are not inside another attached one, which paint their own attached descendants.</summary>
        internal IReadOnlyList<BoxFragment> AttachedRoots => _attachedRoots;

        /// <summary>The visible window of the surface, in layout units; empty until a host sets it.</summary>
        internal Rect Viewport { get; private set; }

        /// <summary>Whether no box is displaced, which is the common case and lets every caller skip its walk.</summary>
        internal bool IsEmpty => _displacements.Count == 0;

        /// <summary>How far the box itself (not what it sits inside) is displaced.</summary>
        internal bool TryGetOwn(CssBox box, out (double Dx, double Dy) displacement) => _displacements.TryGetValue(box, out displacement);

        /// <summary>How far the box is displaced altogether: its own displacement and that of every ancestor that carries it along.</summary>
        internal (double Dx, double Dy) DisplacementOf(CssBox? box)
        {
            if (IsEmpty)
                return default;

            double dx = 0, dy = 0;
            for (var current = box; current is not null; current = current.ParentBox)
            {
                if (_displacements.TryGetValue(current, out var own))
                {
                    dx += own.Dx;
                    dy += own.Dy;
                }
            }

            return (dx, dy);
        }

        /// <summary>
        /// Moves the viewport and works out the displacement of every fixed and sticky box. Returns the rectangles, in layout units, that look
        /// different now: where each displaced box was and where it is, and the whole of a box with a fixed background image.
        /// </summary>
        internal List<Rect> Apply(HtmlContainerInt container, Rect viewport)
        {
            var dirty = new List<Rect>();
            Viewport = viewport;

            var tree = container.FragmentTree;
            if (!container.Continuous || tree is not { Fragmentainers.Count: > 0 })
            {
                _displacements = new Dictionary<CssBox, (double, double)>(ReferenceEqualityComparer.Instance);
                _attached = new HashSet<CssBox>(ReferenceEqualityComparer.Instance);
                _attachedRoots = [];
                _index = null;
                return dirty;
            }

            // Which boxes can move is a property of the layout, not of where the viewport is, so it is found once per layout.
            if (_index is null || !ReferenceEquals(_index.Tree, tree))
                _index = ViewportIndex.Build(tree);

            var next = new Dictionary<CssBox, (double Dx, double Dy)>(ReferenceEqualityComparer.Instance);
            foreach (var entry in _index.Entries)
            {
                var displacement = entry.Box.Position.Value switch
                {
                    // Laid out in a frame that is the viewport with the document at the top.
                    PositionMode.Fixed => (viewport.X, viewport.Y),
                    PositionMode.Sticky => StickyDisplacement(entry, viewport),
                    _ => default,
                };

                if (displacement != default)
                    next[entry.Box] = displacement;
            }

            var moved = viewport.X != _lastOrigin.X || viewport.Y != _lastOrigin.Y;
            foreach (var entry in _index.Entries)
            {
                _displacements.TryGetValue(entry.Box, out var before);
                next.TryGetValue(entry.Box, out var after);

                // A fixed background is positioned against the viewport, so the whole box looks different whenever the viewport moved.
                if (before == after && !(entry.HasFixedBackground && moved))
                    continue;

                var rect = entry.Fragment.WholeBoxRect;
                dirty.Add(Translate(rect, before));
                dirty.Add(Translate(rect, after));
            }

            _lastOrigin = (viewport.X, viewport.Y);
            _displacements = next;

            // A fixed box is attached whatever the scroll, so it never changes layer; a sticky box is attached while it is held away from its place.
            var attached = new HashSet<CssBox>(ReferenceEqualityComparer.Instance);
            foreach (var entry in _index.Entries)
            {
                if (entry.Box.Position.Value == PositionMode.Fixed || next.ContainsKey(entry.Box))
                    attached.Add(entry.Box);
            }

            var roots = new List<BoxFragment>();
            foreach (var entry in _index.Entries)
            {
                if (!attached.Contains(entry.Box))
                    continue;

                var covered = false;
                for (var ancestor = entry.Box.ParentBox; ancestor is not null && !covered; ancestor = ancestor.ParentBox)
                    covered = attached.Contains(ancestor);

                if (!covered)
                    roots.Add(entry.Fragment);
            }

            _attached = attached;
            _attachedRoots = roots;
            return dirty;
        }

        /// <summary>Whether any box of the laid-out continuous document has a <c>background-attachment: fixed</c> image.</summary>
        internal bool HasFixedBackground(HtmlContainerInt container)
        {
            var tree = container.FragmentTree;
            if (!container.Continuous || tree is not { Fragmentainers.Count: > 0 })
                return false;

            if (_index is null || !ReferenceEquals(_index.Tree, tree))
                _index = ViewportIndex.Build(tree);

            return _index.Entries.Exists(e => e.HasFixedBackground);
        }

        private static Rect Translate(Rect rect, (double Dx, double Dy) by) => new(rect.X + by.Dx, rect.Y + by.Dy, rect.Width, rect.Height);

        // CSS Positioned Layout 3 §3.4: the box is held inside the sticky view rectangle by the insets that are not auto, and never leaves its
        // containing block. The nearest scroll container is the viewport here: any other one is clipped, never scrolled, so a box inside it does not stick.
        private static (double Dx, double Dy) StickyDisplacement(ViewportIndex.Entry entry, Rect viewport)
        {
            var box = entry.Box;
            if (viewport.Width <= 0 || viewport.Height <= 0 || entry.InsideOtherScrollContainer)
                return default;

            var rect = entry.Fragment.WholeBoxRect;
            var block = entry.Parent?.WholeBoxRect;

            return (
                Axis(rect.X, rect.Right, block?.X, block?.Right, viewport.X, viewport.Right, box.LeftInset(viewport.Width), box.RightInset(viewport.Width)),
                Axis(rect.Y, rect.Bottom, block?.Y, block?.Bottom, viewport.Y, viewport.Bottom, box.TopInset(viewport.Height), box.BottomInset(viewport.Height)));
        }

        // How far the box moves along one axis: the least that keeps it inside the sticky view rectangle (the start inset wins when the box is too big
        // to fit), then no further than its containing block lets it go.
        private static double Axis(double start, double end, double? blockStart, double? blockEnd, double viewStart, double viewEnd, double? startInset, double? endInset)
        {
            var shift = 0d;
            if (endInset is { } far)
                shift = Math.Min(shift, viewEnd - far - end);
            if (startInset is { } near)
                shift = Math.Max(shift, viewStart + near - start);

            if (blockEnd is { } limit && end + shift > limit)
                shift = limit - end;
            if (blockStart is { } origin && start + shift < origin)
                shift = origin - start;

            return shift;
        }
    }

    /// <summary>The boxes of one layout that a moving viewport can displace, each with its fragment and its containing fragment.</summary>
    internal sealed class ViewportIndex
    {
        internal sealed record Entry(CssBox Box, BoxFragment Fragment, BoxFragment? Parent, bool HasFixedBackground, bool InsideOtherScrollContainer);

        private ViewportIndex(FragmentTree tree) => Tree = tree;

        internal FragmentTree Tree { get; }

        internal List<Entry> Entries { get; } = [];

        internal static ViewportIndex Build(FragmentTree tree)
        {
            var index = new ViewportIndex(tree);
            if (tree.Fragmentainers.Count > 0)
                index.Collect(tree.Fragmentainers[0].Root, null, [], new HashSet<CssBox>(ReferenceEqualityComparer.Instance));

            return index;
        }

        private void Collect(BoxFragment fragment, BoxFragment? parent, List<BoxFragment> ancestors, HashSet<CssBox> seen)
        {
            var box = fragment.Box;
            var fixedBackground = box.BackgroundAttachment?.Contains("fixed", StringComparison.OrdinalIgnoreCase) == true;
            var sticky = box.Position.Value == PositionMode.Sticky;
            if ((box.Position.Value == PositionMode.Fixed || sticky || fixedBackground) && seen.Add(box))
                Entries.Add(new Entry(box, fragment, ContainingBlockOf(box, parent, ancestors), fixedBackground, sticky && InsideScrollContainer(box)));

            ancestors.Add(fragment);
            foreach (var child in fragment.Children)
                Collect(child, fragment, ancestors, seen);

            ancestors.RemoveAt(ancestors.Count - 1);
        }

        // A sticky cell or row is held inside its table, not inside its own row: that is what lets a sticky header cell follow the top of a long table.
        private static BoxFragment? ContainingBlockOf(CssBox box, BoxFragment? parent, List<BoxFragment> ancestors)
        {
            if (box.DerivedStyle.ActualDisplay is not (Keywords.TableCell or Keywords.TableRow or Keywords.TableHeaderGroup or Keywords.TableRowGroup or Keywords.TableFooterGroup))
                return parent;

            for (var i = ancestors.Count - 1; i >= 0; i--)
            {
                if (ancestors[i].Box.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable)
                    return ancestors[i];
            }

            return parent;
        }

        // Any ancestor that is not overflow: visible clips and scrolls in its own right, and nothing in this engine scrolls it.
        private static bool InsideScrollContainer(CssBox box)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (ancestor.Overflow.Value != Css.Overflow.Visible)
                    return true;
            }

            return false;
        }
    }
}
