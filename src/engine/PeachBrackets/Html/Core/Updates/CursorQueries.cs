using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PeachBrackets.Html.Updates
{
    /// <summary>
    /// What the document asks the pointer to look like: the <c>cursor</c> property resolved for the element under a point of a page
    /// (CSS Basic User Interface 4 section 6.1), and the areas of a page that ask for a pointer of their own.
    /// </summary>
    internal static partial class CursorQueries
    {
        /// <summary>The cursor at a point of a page, in points from the top-left of the sheet.</summary>
        public static DocumentCursor At(HtmlContainerInt container, FragmentainerFragment fragmentainer, double x, double y)
        {
            var frame = FrameOf(container, fragmentainer);
            return Find(fragmentainer.Root, x, y, frame, out var box, out var overText) && box is not null
                ? Resolve(container, box, overText)
                : new DocumentCursor(CursorShape.Default, []);
        }

        /// <summary>The interaction the document allows at a point of a page, in points from the top-left of the sheet.</summary>
        public static DocumentInteraction InteractionAt(HtmlContainerInt container, FragmentainerFragment fragmentainer, double x, double y)
        {
            var frame = FrameOf(container, fragmentainer);
            // The topmost thing under the point, inert or not, says whether the point is inert; what a touch may begin is then read off the
            // element that would receive it.
            var overInert = Find(fragmentainer.Root, x, y, frame, out var top, out _, skipInert: false) && top is { IsInert: true };
            if (!Find(fragmentainer.Root, x, y, frame, out var box, out _) || box is null)
                return new DocumentInteraction(overInert, TouchActions.All, !overInert);

            return new DocumentInteraction(overInert, box.EffectiveTouchActions, !overInert && box.EffectiveUserSelect != UserSelect.None);
        }

        /// <summary>
        /// The areas of a page that ask for a pointer other than the default one, in paint order, in points from the top-left of the sheet. An
        /// element whose <c>cursor</c> is <c>auto</c> is listed only when it is a link; the text pointer over plain text is left to the host,
        /// which has the whole text to hand and would otherwise be given a region for every line.
        /// </summary>
        public static List<(Rect Bounds, DocumentCursor Cursor)> Regions(HtmlContainerInt container, FragmentainerFragment fragmentainer)
        {
            var frame = FrameOf(container, fragmentainer);
            var regions = new List<(Rect, DocumentCursor)>();
            Walk(container, fragmentainer.Root, frame, regions);
            return regions;
        }

        private static TextRunCollector.Frame FrameOf(HtmlContainerInt container, FragmentainerFragment fragmentainer)
        {
            var geometry = fragmentainer.Geometry;
            return new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, container.RenderContext.LayoutUnitsPerPoint);
        }

        private static void Walk(HtmlContainerInt container, BoxFragment fragment, TextRunCollector.Frame frame, List<(Rect, DocumentCursor)> regions)
        {
            var box = fragment.Box;
            if (box.HtmlTag is not null && box.PointerEvents.Value != PointerEvents.None && !box.IsInert && Regionable(container, box) is { } cursor)
            {
                if (fragment.Lines.Count > 0)
                {
                    foreach (var line in fragment.Lines)
                        regions.Add((ToPoints(line.Rect, frame), cursor));
                }
                else
                {
                    regions.Add((ToPoints(fragment.Rect, frame), cursor));
                }
            }

            foreach (var child in fragment.Children)
                Walk(container, child, frame, regions);
        }

        private static DocumentCursor? Regionable(HtmlContainerInt container, CssBox box)
        {
            var cursor = Resolve(container, box, overText: false);
            if (cursor.Shape == CursorShape.Default && cursor.Images.Count == 0)
                return null;

            // An inherited `cursor` is the same on a child as on its parent; the parent's region already covers it.
            if (box.ParentBox is { } parent && parent.HtmlTag is not null && parent.Cursor == box.Cursor && IsLink(parent) == IsLink(box))
                return null;

            return cursor;
        }

        private static Rect ToPoints(Rect layout, TextRunCollector.Frame frame) =>
            new(frame.X(layout.X), frame.Y(layout.Y), layout.Width / frame.Ppp, layout.Height / frame.Ppp);

        // The deepest fragment under the point that receives pointer events, and among siblings the one painted last (on top).
        private static bool Find(BoxFragment fragment, double x, double y, TextRunCollector.Frame frame, out CssBox? box, out bool overText, bool skipInert = true)
        {
            for (var i = fragment.Children.Count - 1; i >= 0; i--)
            {
                if (Find(fragment.Children[i], x, y, frame, out box, out overText, skipInert))
                    return true;
            }

            box = null;
            overText = false;
            if (fragment.Box.PointerEvents.Value == PointerEvents.None || (skipInert && fragment.Box.IsInert))
                return false;

            foreach (var word in fragment.Words)
            {
                if (Contains(word.Rect, x, y, frame))
                    overText = true;
            }

            var covered = overText;
            if (!covered)
            {
                if (fragment.Lines.Count > 0)
                {
                    foreach (var line in fragment.Lines)
                        covered |= Contains(line.Rect, x, y, frame);
                }
                else
                {
                    covered = Contains(fragment.Rect, x, y, frame);
                }
            }

            if (!covered)
                return false;

            var element = fragment.Box;
            while (element.HtmlTag is null && element.ParentBox is { } parent)
                element = parent;

            box = element.HtmlTag is null ? null : element;
            return box is not null;
        }

        private static bool Contains(Rect layout, double x, double y, TextRunCollector.Frame frame)
        {
            var left = frame.X(layout.X);
            var top = frame.Y(layout.Y);
            return x >= left && x < left + (layout.Width / frame.Ppp) && y >= top && y < top + (layout.Height / frame.Ppp);
        }

        private static bool IsLink(CssBox box)
        {
            for (var element = box; element is not null; element = element.ParentBox)
            {
                if (element.HtmlTag is { } tag && string.Equals(tag.Name, "a", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tag.TryGetAttribute("href")))
                    return true;
            }

            return false;
        }

        private static DocumentCursor Resolve(HtmlContainerInt container, CssBox box, bool overText)
        {
            var images = new List<CursorImage>();
            var keyword = SystemCursor.Auto;
            foreach (var item in SplitList(box.Cursor))
            {
                if (Map.Cursors.TryGetValue(item, out var known))
                {
                    keyword = known;
                    break;
                }

                if (UrlItem().Match(item) is { Success: true } match)
                {
                    var reference = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                    var resolved = CommonUtils.ResolveAgainstDocumentBase(container, reference)?.AbsoluteUri;
                    double? hotX = match.Groups[4].Success ? double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : null;
                    double? hotY = match.Groups[5].Success ? double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) : null;
                    images.Add(new CursorImage(resolved ?? reference, hotX, hotY));
                }
            }

            var shape = keyword switch
            {
                SystemCursor.Auto => IsLink(box) ? CursorShape.Pointer : overText && box.EffectiveUserSelect != UserSelect.None ? CursorShape.Text : CursorShape.Default,
                _ => ToShape(keyword),
            };
            return new DocumentCursor(shape, images);
        }

        // The comma separated list, split outside parentheses and quotes.
        private static List<string> SplitList(string? value)
        {
            var items = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return items;

            var depth = 0;
            var quote = '\0';
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                }
                else if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    items.Add(value[start..i].Trim());
                    start = i + 1;
                }
            }

            items.Add(value[start..].Trim());
            return items;
        }

        [GeneratedRegex("^url\\(\\s*(?:\"([^\"]*)\"|'([^']*)'|([^)\\s]*))\\s*\\)\\s*(?:(-?\\d+(?:\\.\\d+)?)\\s+(-?\\d+(?:\\.\\d+)?))?$", RegexOptions.IgnoreCase)]
        private static partial Regex UrlItem();

        private static CursorShape ToShape(SystemCursor cursor) => cursor switch
        {
            SystemCursor.None => CursorShape.None,
            SystemCursor.ContextMenu => CursorShape.ContextMenu,
            SystemCursor.Help => CursorShape.Help,
            SystemCursor.Pointer => CursorShape.Pointer,
            SystemCursor.Progress => CursorShape.Progress,
            SystemCursor.Wait => CursorShape.Wait,
            SystemCursor.Cell => CursorShape.Cell,
            SystemCursor.Crosshair => CursorShape.Crosshair,
            SystemCursor.Text => CursorShape.Text,
            SystemCursor.VerticalText => CursorShape.VerticalText,
            SystemCursor.Alias => CursorShape.Alias,
            SystemCursor.Copy => CursorShape.Copy,
            SystemCursor.Move => CursorShape.Move,
            SystemCursor.NoDrop => CursorShape.NoDrop,
            SystemCursor.NotAllowed => CursorShape.NotAllowed,
            SystemCursor.EResize => CursorShape.ResizeEast,
            SystemCursor.NResize => CursorShape.ResizeNorth,
            SystemCursor.NeResize => CursorShape.ResizeNorthEast,
            SystemCursor.NwResize => CursorShape.ResizeNorthWest,
            SystemCursor.SResize => CursorShape.ResizeSouth,
            SystemCursor.SeResize => CursorShape.ResizeSouthEast,
            SystemCursor.SwResize => CursorShape.ResizeSouthWest,
            SystemCursor.WResize => CursorShape.ResizeWest,
            SystemCursor.EwResize => CursorShape.ResizeHorizontal,
            SystemCursor.NsResize => CursorShape.ResizeVertical,
            SystemCursor.NeswResize => CursorShape.ResizeDiagonalUp,
            SystemCursor.NwseResize => CursorShape.ResizeDiagonalDown,
            SystemCursor.ColResize => CursorShape.ResizeColumn,
            SystemCursor.RowResize => CursorShape.ResizeRow,
            SystemCursor.AllScroll => CursorShape.ScrollAll,
            SystemCursor.ZoomIn => CursorShape.ZoomIn,
            SystemCursor.ZoomOut => CursorShape.ZoomOut,
            SystemCursor.Grab => CursorShape.Grab,
            SystemCursor.Grabbing => CursorShape.Grabbing,
            _ => CursorShape.Default,
        };
    }
}
