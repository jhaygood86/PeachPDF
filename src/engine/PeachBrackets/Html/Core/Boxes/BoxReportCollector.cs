using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.Boxes
{
    /// <summary>
    /// Describes the laid-out boxes of a document for a script host (<see cref="BoxReport"/>), from its fragment tree: one tree per page, with
    /// positions in points from the top left of the sheet. It reads the same fragments the painter draws from and draws nothing, so it costs nothing
    /// unless a script asks.
    /// </summary>
    internal static class BoxReportCollector
    {
        /// <summary>Makes the report tree of every page.</summary>
        /// <param name="container">the laid-out document</param>
        /// <param name="pages">the pages' fragments, in order</param>
        /// <param name="elementAt">the script node with a given source ordinal, or null</param>
        public static IReadOnlyList<BoxReport> Collect(HtmlContainerInt container, IReadOnlyList<FragmentainerFragment> pages, Func<int, ScriptNode?> elementAt)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;
            var roots = new List<BoxReport>(pages.Count);

            for (var i = 0; i < pages.Count; i++)
            {
                var geometry = pages[i].Geometry;
                var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);
                var context = new Context(container, frame, ppp, i + 1, geometry.SheetWidthPt, geometry.SheetHeightPt, elementAt);

                var width = geometry.SheetWidthPt - geometry.ContentLeftPt - (geometry.MarginRightPt + geometry.BorderRightPt + geometry.PaddingRightPt);
                var height = geometry.SheetHeightPt - geometry.ContentTopPt - (geometry.MarginBottomPt + geometry.BorderBottomPt + geometry.PaddingBottomPt);
                var root = new BoxReport(BoxReportKind.Page, context.Page, geometry.ContentLeftPt, geometry.ContentTopPt, Math.Max(0, width), Math.Max(0, height), context.SheetWidth, context.SheetHeight);
                AddBox(context, root, pages[i].Root);

                if (pages[i].FootnoteAreas is { Count: > 0 } areas)
                {
                    foreach (var area in areas)
                        AddFootnotes(context, root, area);
                }

                roots.Add(root);
            }

            return roots;
        }

        private sealed record Context(HtmlContainerInt Container, TextRunCollector.Frame Frame, double Ppp, int Page, double SheetWidth, double SheetHeight, Func<int, ScriptNode?> ElementAt);

        private static void AddFootnotes(Context context, BoxReport root, FootnoteAreaFragment area)
        {
            var left = area.DividerRect.X;
            var top = area.DividerRect.Y;
            var bottom = area.DividerRect.Y + area.DividerRect.Height;
            foreach (var body in area.Bodies)
                bottom = Math.Max(bottom, body.Rect.Y + body.Rect.Height);

            var report = new BoxReport(BoxReportKind.Footnotes, context.Page, context.Frame.X(left), context.Frame.Y(top),
                area.DividerRect.Width / context.Ppp, (bottom - top) / context.Ppp, context.SheetWidth, context.SheetHeight);
            root.Add(report);
            foreach (var body in area.Bodies)
                AddBox(context, report, body);
        }

        private static void AddBox(Context context, BoxReport parent, BoxFragment fragment)
        {
            var box = fragment.Box;
            var (dx, dy) = context.Container.View.DisplacementOf(box);
            var rect = BoundsOf(fragment);
            var ppp = context.Ppp;

            var report = new BoxReport(KindOf(box), context.Page, context.Frame.X(rect.X + dx), context.Frame.Y(rect.Y + dy), rect.Width / ppp, rect.Height / ppp,
                context.SheetWidth, context.SheetHeight)
            {
                Margin = new BoxEdges(box.ActualMarginTop / ppp, box.ActualMarginRight / ppp, box.ActualMarginBottom / ppp, box.ActualMarginLeft / ppp),
                Padding = new BoxEdges(box.ActualPaddingTop / ppp, box.ActualPaddingRight / ppp, box.ActualPaddingBottom / ppp, box.ActualPaddingLeft / ppp),
                Border = new BoxEdges(box.ActualBorderTopWidth / ppp, box.ActualBorderRightWidth / ppp, box.ActualBorderBottomWidth / ppp, box.ActualBorderLeftWidth / ppp),
                PseudoElement = PseudoElementOf(box),
            };

            if (box.HtmlTag is { } tag && tag.SourceOrdinal >= 0 && report.PseudoElement is null)
                report.Element = context.ElementAt(tag.SourceOrdinal);
            if (box is CssBoxImage image)
                report.ImageSource = image.ImageSource;

            parent.Add(report);
            AddText(context, report, fragment, dx, dy);

            foreach (var child in fragment.Children)
                AddBox(context, report, child);
        }

        // The place this fragment of the box takes: the union of its own decoration rectangles, which is the piece on this page or line. (The fragment's
        // Rect also covers its descendants, so a box that breaks across pages would report its whole height on each of them.)
        private static PeachDrawing.Core.Rect BoundsOf(BoxFragment fragment)
        {
            if (fragment.Lines.Count == 0)
                return fragment.Rect;

            var union = fragment.Lines[0].Slice.FragmentRect;
            for (var i = 1; i < fragment.Lines.Count; i++)
            {
                var next = fragment.Lines[i].Slice.FragmentRect;
                var left = Math.Min(union.X, next.X);
                var top = Math.Min(union.Y, next.Y);
                var right = Math.Max(union.X + union.Width, next.X + next.Width);
                var bottom = Math.Max(union.Y + union.Height, next.Y + next.Height);
                union = new PeachDrawing.Core.Rect(left, top, right - left, bottom - top);
            }

            return union;
        }

        // One text box for each run of a line's words that this box owns.
        private static void AddText(Context context, BoxReport owner, BoxFragment fragment, double dx, double dy)
        {
            CssLineBox? line = null;
            StringBuilder? text = null;
            double left = 0, top = 0, right = 0, bottom = 0;

            void Flush()
            {
                if (text is { Length: > 0 })
                {
                    var report = new BoxReport(BoxReportKind.Text, context.Page, context.Frame.X(left), context.Frame.Y(top),
                        (right - left) / context.Ppp, (bottom - top) / context.Ppp, context.SheetWidth, context.SheetHeight)
                    {
                        Text = text.ToString(),
                    };
                    owner.Add(report);
                }

                text = null;
            }

            foreach (var textFragment in fragment.Words)
            {
                var word = textFragment.Word;
                if (word.IsLineBreak || word.IsImage || word is CssRectLeader)
                    continue;

                var content = word.FirstLineText ?? word.Text;
                if (string.IsNullOrEmpty(content))
                    continue;

                var rect = textFragment.Rect;
                var x = rect.X + dx;
                var y = rect.Y + dy;
                if (text is null || !ReferenceEquals(line, word.Line))
                {
                    Flush();
                    line = word.Line;
                    text = new StringBuilder();
                    (left, top, right, bottom) = (x, y, x + rect.Width, y + rect.Height);
                }
                else
                {
                    text.Append(' ');
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + rect.Width);
                    bottom = Math.Max(bottom, y + rect.Height);
                }

                text.Append(content);
            }

            Flush();
        }

        private static BoxReportKind KindOf(CssBox box) => box switch
        {
            CssBoxImage => BoxReportKind.Image,
            CssBoxSvg => BoxReportKind.Svg,
            _ when box.DerivedStyle.ActualDisplay == Keywords.Inline => BoxReportKind.Span,
            _ => BoxReportKind.Box,
        };

        private static string? PseudoElementOf(CssBox box) =>
            box.IsBeforePseudoElement ? "before"
            : box.IsAfterPseudoElement ? "after"
            : box.IsMarkerPseudoElement ? "marker"
            : box.IsPlaceholderPseudoElement ? "placeholder"
            : box.IsFirstLetterPseudoElement ? "first-letter"
            : box.IsFootnoteCallPseudoElement ? "footnote-call"
            : box.IsFootnoteMarkerPseudoElement ? "footnote-marker"
            : null;
    }
}
