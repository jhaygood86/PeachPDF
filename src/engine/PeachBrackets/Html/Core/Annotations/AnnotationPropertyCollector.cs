using PeachBrackets.Annotations;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachBrackets.Svg;
using PeachBrackets.Tagging;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Html.Annotations
{
    /// <summary>
    /// Collects the annotations authors ask for with <c>-peachpdf-pdf-annotation-type</c> (and its companion properties) into
    /// <see cref="PageAnnotation"/>s: a walk of the box tree, with each box's rectangles placed on their pages the way the form-field
    /// and link passes place theirs.
    /// </summary>
    internal static class AnnotationPropertyCollector
    {
        private const double DefaultIconPt = 16;
        private const double EmptyIconPt = 12;
        private const double MinSizedIconPt = 8;
        private const double MaxSizedIconPt = 48;
        private const double PopupWidthPt = 160;
        private const double PopupHeightPt = 80;

        public static List<PageAnnotation> Collect(
            HtmlContainerInt container, double ppp, IReadOnlyList<FragmentainerFragment> fragmentainers, IReadOnlyDictionary<int, int> slotToPage)
        {
            _ = slotToPage;
            var annotations = new List<PageAnnotation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // The fragment tree is what was laid out and what gets painted, so it is where an element's rectangles on each page are read
            // (a box that continues onto a later page has a fragment there too; an inline's lines are its fragment's Lines).
            for (var pageIndex = 0; pageIndex < fragmentainers.Count; pageIndex++)
                Walk(fragmentainers[pageIndex].Root, pageIndex, container, ppp, fragmentainers, annotations, seen, suppressed: false);

            return annotations;
        }

        private static void Walk(
            BoxFragment fragment, int pageIndex, HtmlContainerInt container, double ppp, IReadOnlyList<FragmentainerFragment> fragmentainers,
            List<PageAnnotation> annotations, HashSet<string> seen, bool suppressed)
        {
            var box = fragment.Box;

            // Nothing is drawn under a box a transform left no visible area, or one that is not rendered, so there is nothing to mark.
            suppressed = suppressed || DomUtils.LeavesNoVisibleArea(box) || box.DerivedStyle.ActualDisplay == Keywords.None;

            if (!suppressed && IsAnnotated(box, out var type))
            {
                var rectangles = fragment.Lines.Count > 0 ? fragment.Lines.Select(line => line.Rect).ToList() : [fragment.Rect];
                Process(box, rectangles, type, pageIndex, container, ppp, fragmentainers, annotations, seen);
            }

            foreach (var child in fragment.Children)
                Walk(child, pageIndex, container, ppp, fragmentainers, annotations, seen, suppressed);

            if (suppressed)
                return;

            // An empty inline (the issue's <span></span> that only carries a paperclip) puts no word in the line, so layout emits no fragment
            // for it. It still stands somewhere, though: where the content before it ends.
            // A box that continues into another column of the same parent (a list whose item runs past the end of a column) has a fragment in each, so the
            // same box can stand in the children more than once: the first one is where it begins.
            var fragmentOf = new Dictionary<CssBox, BoxFragment>(fragment.Children.Count);
            foreach (var child in fragment.Children)
                fragmentOf.TryAdd(child.Box, child);

            for (var i = 0; i < box.Boxes.Count; i++)
            {
                var child = box.Boxes[i];
                if (fragmentOf.ContainsKey(child) || child.Words.Count > 0 || child.Boxes.Count > 0 || !IsAnnotated(child, out var childType)
                    || DomUtils.LeavesNoVisibleArea(child) || child.Visibility.Value != Visibility.Visible)
                {
                    continue;
                }

                if (EmptyInlineRectangle(box, fragment, fragmentOf, i) is { } where)
                    Process(child, [where], childType, pageIndex, container, ppp, fragmentainers, annotations, seen);
            }
        }

        private static bool IsAnnotated(CssBox box, out PdfAnnotationTypeKeyword type)
        {
            type = PdfAnnotationTypeKeyword.None;
            return box.Visibility.Value == Visibility.Visible && box.PdfAnnotationType is { Length: > 0 } text
                && Map.PdfAnnotationTypes.TryGetValue(text.Trim(), out type) && type != PdfAnnotationTypeKeyword.None;
        }

        /// <summary>
        /// A zero-width rectangle at the end of whatever precedes the empty inline <c>box.Boxes[index]</c> in its parent (or at the start of what
        /// follows it, or of the parent itself), as tall as that neighbour's last line.
        /// </summary>
        private static Rect? EmptyInlineRectangle(CssBox parent, BoxFragment parentFragment, Dictionary<CssBox, BoxFragment> fragmentOf, int index)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                if (fragmentOf.TryGetValue(parent.Boxes[i], out var before) && LastRectangle(before) is { } last)
                    return new Rect(last.Right, last.Top, 0, last.Height);
            }

            for (var i = index + 1; i < parent.Boxes.Count; i++)
            {
                if (fragmentOf.TryGetValue(parent.Boxes[i], out var after) && FirstRectangle(after) is { } first)
                    return new Rect(first.Left, first.Top, 0, first.Height);
            }

            var own = FirstRectangle(parentFragment);
            return own is { } r ? new Rect(r.Left, r.Top, 0, r.Height) : null;
        }

        private static Rect? LastRectangle(BoxFragment fragment) =>
            fragment.Words.Count > 0 ? fragment.Words[^1].Rect : fragment.Lines.Count > 0 ? fragment.Lines[^1].Rect : fragment.Rect;

        private static Rect? FirstRectangle(BoxFragment fragment) =>
            fragment.Words.Count > 0 ? fragment.Words[0].Rect : fragment.Lines.Count > 0 ? fragment.Lines[0].Rect : fragment.Rect;

        private static void Process(
            CssBox box, IReadOnlyList<Rect> rectangles, PdfAnnotationTypeKeyword type, int pageIndex, HtmlContainerInt container, double ppp,
            IReadOnlyList<FragmentainerFragment> fragmentainers, List<PageAnnotation> annotations, HashSet<string> seen)
        {
            var kind = ToKind(type);
            var title = Text(box.PdfAnnotationTitle);
            var contents = Text(box.PdfAnnotationContents);
            var author = Text(box.PdfAnnotationAuthor);
            var attachment = Text(box.PdfAnnotationAttachment);
            var icon = ResolveIcon(box.PdfAnnotationIcon);
            var color = ResolveColor(box, box.PdfAnnotationColor);

            // A file-attachment annotation that names no file has nothing to open.
            if (kind == PageAnnotationKind.FileAttachment && attachment is null)
                return;

            if (ResolveMergeKey(box.PdfAnnotationMerge, kind, title, contents, author, attachment, icon, color) is { } mergeKey && !seen.Add(mergeKey))
                return;

            var created = ResolveDate(box.PdfAnnotationCreateDate);
            var modified = ResolveDate(box.PdfAnnotationModifyDate);

            var fragmentainer = fragmentainers[pageIndex];
            var geometry = fragmentainer.Geometry;
            var pieces = new List<(Rect Bounds, PaintPoint[]? Quad)>();
            foreach (var pixelRect in rectangles)
            {
                if (Place(box, pixelRect, fragmentainer, container, ppp) is { } placed)
                    pieces.Add(placed);
            }

            if (pieces.Count == 0)
                return;

            var union = pieces.Select(piece => piece.Bounds).Aggregate(Union);

            // Only an icon can stand where there is no area (an empty inline); a mark or a shape with nothing to cover has no appearance to draw.
            var isIcon = kind is PageAnnotationKind.Note or PageAnnotationKind.FileAttachment;
            if (!isIcon && kind != PageAnnotationKind.Ink && (union.Width <= 0 || union.Height <= 0))
                return;

            var quads = new List<PaintPoint>();
            if (kind is PageAnnotationKind.Highlight or PageAnnotationKind.Underline or PageAnnotationKind.Squiggly or PageAnnotationKind.StrikeOut)
            {
                foreach (var piece in pieces)
                    quads.AddRange(piece.Quad is { Length: 4 } quad ? quad : CornersOf(piece.Bounds));
            }

            var bounds = union;
            Rect? popup = null;
            var positionTokens = Tokens(box.PdfAnnotationPosition);

            if (isIcon)
            {
                var (width, height) = IconSize(union);
                bounds = Anchor(box, positionTokens, union, width, height, geometry.SheetWidthPt, geometry.SheetHeightPt, ppp);
            }
            else if (positionTokens.Count > 0 && (contents is not null || title is not null))
            {
                popup = Anchor(box, positionTokens, union, PopupWidthPt, PopupHeightPt, geometry.SheetWidthPt, geometry.SheetHeightPt, ppp);
            }

            var strokes = kind == PageAnnotationKind.Ink ? ResolveInk(box.PdfAnnotationPath, union) : [];
            if (kind == PageAnnotationKind.Ink && strokes.Count == 0)
                return;

            annotations.Add(new PageAnnotation
            {
                PageIndex = pageIndex,
                Kind = kind,
                Bounds = bounds,
                Quads = quads,
                Strokes = strokes,
                PopupBounds = popup,
                Icon = icon,
                Color = color,
                Title = title,
                Contents = contents,
                Author = author,
                AttachmentName = attachment,
                CreatedAt = created,
                ModifiedAt = modified,
                Source = new StructureNode(box),
            });
        }

        private static PageAnnotationKind ToKind(PdfAnnotationTypeKeyword type) => type switch
        {
            PdfAnnotationTypeKeyword.Text => PageAnnotationKind.Note,
            PdfAnnotationTypeKeyword.Highlight => PageAnnotationKind.Highlight,
            PdfAnnotationTypeKeyword.Underline => PageAnnotationKind.Underline,
            PdfAnnotationTypeKeyword.Wavy => PageAnnotationKind.Squiggly,
            PdfAnnotationTypeKeyword.LineThrough => PageAnnotationKind.StrikeOut,
            PdfAnnotationTypeKeyword.FileAttachment => PageAnnotationKind.FileAttachment,
            PdfAnnotationTypeKeyword.FreeText => PageAnnotationKind.FreeText,
            PdfAnnotationTypeKeyword.Stamp => PageAnnotationKind.Stamp,
            PdfAnnotationTypeKeyword.Square => PageAnnotationKind.Square,
            PdfAnnotationTypeKeyword.Circle => PageAnnotationKind.Circle,
            PdfAnnotationTypeKeyword.Line => PageAnnotationKind.Line,
            PdfAnnotationTypeKeyword.Ink => PageAnnotationKind.Ink,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        /// <summary>
        /// Puts one fragment rectangle (layout units, page-local like every fragment's) on its page: its bounds in points, with the drawn shape's
        /// corners when a transform made it something other than that box. Null when it was not drawn there.
        /// </summary>
        private static (Rect Bounds, PaintPoint[]? Quad)? Place(CssBox box, Rect local, FragmentainerFragment fragmentainer, HtmlContainerInt container, double ppp)
        {
            var geometry = fragmentainer.Geometry;
            var bounds = new Rect(
                geometry.ContentLeftPt + (local.Left - container.MarginLeft) / ppp,
                geometry.ContentTopPt + (local.Top - container.MarginTop) / ppp,
                local.Width / ppp,
                local.Height / ppp);
            PaintPoint[]? quad = null;

            var (viewDx, viewDy) = container.View.DisplacementOf(box);
            if (viewDx != 0 || viewDy != 0)
                bounds = new Rect(bounds.X + (viewDx / ppp), bounds.Y + (viewDy / ppp), bounds.Width, bounds.Height);

            if ((box.HtmlContainer ?? container).Placements.TryGet(box, fragmentainer.SlotIndex, out var placement))
            {
                var frame = new PageFrame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp, geometry.SheetHeightPt);
                if (!AnnotationPlacement.TryPlaceLink(placement, local, frame, geometry.SheetWidthPt, geometry.SheetHeightPt, out bounds, out quad))
                    return null;

                if (viewDx != 0 || viewDy != 0)
                {
                    bounds = new Rect(bounds.X + (viewDx / ppp), bounds.Y + (viewDy / ppp), bounds.Width, bounds.Height);
                    if (quad is not null)
                        quad = quad.Select(point => new PaintPoint(point.X + (viewDx / ppp), point.Y + (viewDy / ppp))).ToArray();
                }
            }

            return (bounds, quad);
        }

        private static Rect Union(Rect a, Rect b)
        {
            var left = Math.Min(a.X, b.X);
            var top = Math.Min(a.Y, b.Y);
            return new Rect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }

        private static PaintPoint[] CornersOf(Rect r) =>
            [new(r.X, r.Y), new(r.Right, r.Y), new(r.X, r.Bottom), new(r.Right, r.Bottom)];

        /// <summary>
        /// The size an icon is drawn at: the element's own box when the author sized it like an icon, otherwise a standard icon (smaller
        /// for an element with no area of its own, such as an empty inline).
        /// </summary>
        private static (double Width, double Height) IconSize(Rect box)
        {
            if (box.Width >= MinSizedIconPt && box.Height >= MinSizedIconPt && box.Width <= MaxSizedIconPt && box.Height <= MaxSizedIconPt)
                return (box.Width, box.Height);

            return box.Width <= 0 || box.Height <= 0 ? (EmptyIconPt, EmptyIconPt) : (DefaultIconPt, DefaultIconPt);
        }

        /// <summary>
        /// Resolves <c>-peachpdf-pdf-annotation-position</c> (<c>[ left | right | top | bottom | center | page-left | page-right | page-top |
        /// page-bottom | &lt;length-percentage&gt; ]{1,4}</c>) into the rectangle of <paramref name="width"/> x <paramref name="height"/> it
        /// places, relative to <paramref name="box"/> (or the page for the <c>page-*</c> keywords). The default is the box's top-left corner.
        /// The first length offsets horizontally and the second vertically; percentages are of the box's width and height.
        /// </summary>
        private static Rect Anchor(
            CssBox source, IReadOnlyList<string> tokens, Rect box, double width, double height, double sheetWidth, double sheetHeight, double ppp)
        {
            var x = box.X;
            var y = box.Y;
            var offsets = new List<double>();
            var horizontalDone = false;
            var verticalDone = false;

            foreach (var token in tokens)
            {
                if (Map.PdfAnnotationPositions.TryGetValue(token, out var keyword))
                {
                    switch (keyword)
                    {
                        case PdfAnnotationPositionKeyword.Left: x = box.X; horizontalDone = true; break;
                        case PdfAnnotationPositionKeyword.Right: x = box.Right - width; horizontalDone = true; break;
                        case PdfAnnotationPositionKeyword.PageLeft: x = 0; horizontalDone = true; break;
                        case PdfAnnotationPositionKeyword.PageRight: x = sheetWidth - width; horizontalDone = true; break;
                        case PdfAnnotationPositionKeyword.Top: y = box.Y; verticalDone = true; break;
                        case PdfAnnotationPositionKeyword.Bottom: y = box.Bottom - height; verticalDone = true; break;
                        case PdfAnnotationPositionKeyword.PageTop: y = 0; verticalDone = true; break;
                        case PdfAnnotationPositionKeyword.PageBottom: y = sheetHeight - height; verticalDone = true; break;
                        default:
                            // center: along whichever axis is still free, horizontal first.
                            if (!horizontalDone) { x = box.X + (box.Width - width) / 2; horizontalDone = true; }
                            else { y = box.Y + (box.Height - height) / 2; verticalDone = true; }
                            break;
                    }
                }
                else
                {
                    var basis = (offsets.Count == 0 ? box.Width : box.Height) * ppp;
                    offsets.Add(CssValueParser.ParseLength(token, basis, source) / ppp);
                }
            }

            if (offsets.Count > 0) x += offsets[0];
            if (offsets.Count > 1) y += offsets[1];
            _ = verticalDone;

            return new Rect(x, y, width, height);
        }

        private static List<string> Tokens(string? raw) =>
            string.IsNullOrWhiteSpace(raw) || raw.Trim() == Keywords.Auto
                ? []
                : raw.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();

        /// <summary>Strips the quotes (and escapes) of a stored <c>&lt;string&gt;</c>; null for <c>none</c>, an unset value or an empty string.</summary>
        internal static string? Text(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            if (raw == Keywords.None)
                return null;

            if (raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') && raw[^1] == raw[0])
            {
                var inner = raw[1..^1];
                var builder = new System.Text.StringBuilder(inner.Length);
                for (var i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length)
                        i++;
                    builder.Append(inner[i]);
                }

                inner = builder.ToString();
                return inner.Length == 0 ? null : inner;
            }

            return raw;
        }

        private static string? ResolveIcon(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && Map.PdfAnnotationIcons.TryGetValue(raw.Trim(), out var icon) && icon != PdfAnnotationIconKeyword.Auto
                ? raw.Trim().ToLowerInvariant()
                : null;

        private static PaintColor? ResolveColor(CssBox box, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == Keywords.Auto)
                return null;

            var color = box.GetActualColor(raw.Trim());
            return color.IsEmpty ? null : color;
        }

        private static DateTimeOffset? ResolveDate(string? raw) =>
            Text(raw) is { } text && text != Keywords.Auto
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date
                    : null;

        /// <summary>
        /// The identity of an annotation for <c>-peachpdf-pdf-annotation-merge</c>: null for <c>none</c> (every annotation stands), the
        /// annotation's own content for <c>duplicates</c> (a repeat of one already written is dropped), or the author's key.
        /// </summary>
        private static string? ResolveMergeKey(
            string? raw, PageAnnotationKind kind, string? title, string? contents, string? author, string? attachment, string? icon, PaintColor? color)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            if (raw.StartsWith("key(", StringComparison.OrdinalIgnoreCase) && raw.EndsWith(')'))
                return "key:" + (Text(raw[4..^1]) ?? string.Empty);

            return string.Equals(raw, "duplicates", StringComparison.OrdinalIgnoreCase)
                ? string.Join('\u001f', "dup", kind, title, contents, author, attachment, icon, color?.ToString())
                : null;
        }

        /// <summary>
        /// Reads <c>path("...")</c> SVG path data (CSS px from the element's top-left) into page-point strokes: lines are kept, curves are
        /// sampled, and each subpath is one stroke.
        /// </summary>
        private static List<IReadOnlyList<PaintPoint>> ResolveInk(string? raw, Rect box)
        {
            var strokes = new List<IReadOnlyList<PaintPoint>>();
            if (string.IsNullOrWhiteSpace(raw))
                return strokes;

            raw = raw.Trim();
            if (!raw.StartsWith("path(", StringComparison.OrdinalIgnoreCase) || !raw.EndsWith(')'))
                return strokes;

            var data = Text(raw[5..^1]);
            if (!SvgPathDataParser.TryParse(data, out var segments))
                return strokes;

            // CSS px to points; a layout unit is 1/ppp of a point.
            var scale = Length.PointsPerPx;
            var current = new List<PaintPoint>();
            double cx = 0, cy = 0, startX = 0, startY = 0;
            PaintPoint At(double x, double y) => new(box.X + x * scale, box.Y + y * scale);

            void Flush()
            {
                if (current.Count > 1)
                    strokes.Add(current.ToArray());
                current = [];
            }

            foreach (var segment in segments)
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        Flush();
                        cx = startX = segment.X;
                        cy = startY = segment.Y;
                        current.Add(At(cx, cy));
                        break;
                    case PathSegmentKind.LineTo:
                    case PathSegmentKind.ArcTo:
                        // An arc is kept as the chord to its end point: ink is a freehand mark, not a drawing of record.
                        cx = segment.X;
                        cy = segment.Y;
                        current.Add(At(cx, cy));
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        for (var i = 1; i <= 12; i++)
                        {
                            var t = i / 12.0;
                            var u = 1 - t;
                            var x = u * u * u * cx + 3 * u * u * t * segment.X1 + 3 * u * t * t * segment.X2 + t * t * t * segment.X;
                            var y = u * u * u * cy + 3 * u * u * t * segment.Y1 + 3 * u * t * t * segment.Y2 + t * t * t * segment.Y;
                            current.Add(At(x, y));
                        }
                        cx = segment.X;
                        cy = segment.Y;
                        break;
                    case PathSegmentKind.ClosePath:
                        current.Add(At(startX, startY));
                        cx = startX;
                        cy = startY;
                        break;
                }
            }

            Flush();
            return strokes;
        }
    }
}
