using System;

namespace PeachPDF.Layout
{
    /// <summary>Builds a paragraph out of individually-styled runs of text (<see cref="IContainer.Text(System.Action{ITextSpanContainer})"/>).</summary>
    public interface ITextSpanContainer
    {
        /// <summary>Sets this paragraph's text alignment.</summary>
        ITextSpanContainer Alignment(TextAlignment alignment);

        /// <summary>Aligns this paragraph to the left edge - <see cref="Alignment"/> with <see cref="TextAlignment.Left"/>.</summary>
        ITextSpanContainer AlignLeft();

        /// <summary>Centers this paragraph - <see cref="Alignment"/> with <see cref="TextAlignment.Center"/>.</summary>
        ITextSpanContainer AlignCenter();

        /// <summary>Aligns this paragraph to the right edge - <see cref="Alignment"/> with <see cref="TextAlignment.Right"/>.</summary>
        ITextSpanContainer AlignRight();

        /// <summary>Justifies this paragraph - <see cref="Alignment"/> with <see cref="TextAlignment.Justify"/>.</summary>
        ITextSpanContainer Justify();

        /// <summary>Aligns this paragraph to the start edge of its direction - <see cref="Alignment"/> with <see cref="TextAlignment.Start"/>.</summary>
        ITextSpanContainer AlignStart();

        /// <summary>Aligns this paragraph to the end edge of its direction - <see cref="Alignment"/> with <see cref="TextAlignment.End"/>.</summary>
        ITextSpanContainer AlignEnd();

        /// <summary>Appends a run of text and returns its style, for chaining.</summary>
        ITextSpan Span(string text);

        /// <summary>Inserts arbitrary container content (an image, a styled box, ...) inline between the surrounding text spans.</summary>
        void Element(Action<IContainer> content);

        /// <summary>
        /// Appends the current page number. Only resolves inside <see cref="IPageDescriptor.Header"/>/
        /// <see cref="IPageDescriptor.Footer"/> content - elsewhere it renders as an empty span, since a
        /// page number has no meaning outside repeating per-page content.
        /// </summary>
        ITextSpan CurrentPageNumber();

        /// <summary>
        /// Appends the document's total page count. Only resolves inside <see cref="IPageDescriptor.Header"/>/
        /// <see cref="IPageDescriptor.Footer"/> content - elsewhere it renders as an empty span, since a
        /// page count has no meaning outside repeating per-page content.
        /// </summary>
        ITextSpan TotalPages();

        /// <summary>
        /// Appends the current page number counted from the start of the page-numbered section
        /// <paramref name="sectionId"/> (see <see cref="IContainer.BeginPageNumberOfSection"/>) - 1 on the
        /// section's own first page, 2 on its second, and so on. Only resolves inside
        /// <see cref="IPageDescriptor.Header"/>/<see cref="IPageDescriptor.Footer"/> content, the same as
        /// <see cref="CurrentPageNumber"/>.
        /// </summary>
        ITextSpan PageNumberWithinSection(string sectionId);

        /// <summary>
        /// Appends the total page count of the page-numbered section <paramref name="sectionId"/> - see
        /// <see cref="PageNumberWithinSection"/>.
        /// </summary>
        ITextSpan TotalPagesWithinSection(string sectionId);
    }
}
