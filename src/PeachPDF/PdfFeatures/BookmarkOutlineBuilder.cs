using PeachDrawing.Core;
using PeachPDF.Html.Core.Annotations;
using PeachPDF.Html.Core.Handlers;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using System.Collections.Generic;

namespace PeachPDF.PdfFeatures
{
    /// <summary>
    /// Builds a PDF outline (bookmark sidebar) from the document's collected bookmarks
    /// (<see cref="DocumentAnnotationCollector"/>, from every box whose <c>bookmark-level</c> resolved to something other than
    /// <c>none</c> - CSS Generated Content Module Level 3 §2). Unlike <see cref="StructureTagBuilder"/>, this never hooks into
    /// <see cref="Html.Core.Paint.FragmentPainter"/>'s paint walk - a PDF outline (<see cref="PdfOutline"/>/<see cref="PdfOutlineCollection"/>)
    /// is a free-standing dictionary tree independent of the content stream. Unconditional - no <see cref="PdfGenerateConfig"/> gate,
    /// since the UA default stylesheet's own <c>bookmark-level: none</c> default is itself the "no bookmarks" state for a document
    /// with no headings.
    /// </summary>
    internal static class BookmarkOutlineBuilder
    {
        public static void Build(PdfDocument document, IReadOnlyList<Bookmark> bookmarks)
        {
            if (bookmarks.Count == 0) return;

            // Bookmarks do not need to create a strict hierarchy of levels (css-content-3 §2, note) - no algorithm is mandated. This is
            // the conventional heading-outline shape (also Prince's and every browser print-to-PDF's own behavior): a level-N bookmark
            // nests under the nearest preceding bookmark with a shallower level, or the root if none exists.
            var stack = new Stack<(int Level, PdfOutline Outline)>();

            foreach (var bookmark in bookmarks)
            {
                while (stack.Count > 0 && stack.Peek().Level >= bookmark.Level)
                    stack.Pop();

                var outline = new PdfOutline
                {
                    Title = bookmark.Title,
                    Opened = bookmark.IsOpen
                };

                ApplyDestination(document, outline, bookmark);

                if (stack.Count > 0)
                    stack.Peek().Outline.Outlines.Add(outline);
                else
                    document.Outlines.Add(outline);

                stack.Push((bookmark.Level, outline));
            }
        }

        /// <summary>
        /// Points the outline entry at its external URL (see <see cref="PdfOutline.Url"/>) or at its page destination. An entry with
        /// neither is a supported, harmless state (proven by <c>PdfOutlineSaveTests.Save_WithOutlineWithoutDestinationPage_Succeeds</c>).
        /// </summary>
        private static void ApplyDestination(PdfDocument document, PdfOutline outline, Bookmark bookmark)
        {
            if (bookmark.Uri is { } uri)
            {
                outline.Url = uri;
                return;
            }

            if (bookmark.Destination is not { } destination || destination.PageIndex < 0 || destination.PageIndex >= document.Pages.Count)
                return;

            outline.DestinationPage = document.Pages[destination.PageIndex];
            // /FitH top fits page width and positions vertically at top - not /FitV's horizontal left. The offset is top-down (0 at the
            // page's own top edge, increasing downward), but /FitH's own top parameter is PDF default user space, which is bottom-up
            // (0 at the page's bottom edge) - flip via the destination page's own height before writing it.
            outline.PageDestinationType = PdfPageDestinationType.FitH;
            outline.Top = outline.DestinationPage.Height - destination.Top;
        }
    }
}
