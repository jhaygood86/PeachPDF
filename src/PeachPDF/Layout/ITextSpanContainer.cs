using System;

namespace PeachPDF.Layout
{
    /// <summary>Builds a paragraph out of individually-styled runs of text (<see cref="IContainer.Text(System.Action{ITextSpanContainer})"/>).</summary>
    public interface ITextSpanContainer
    {
        /// <summary>Sets this paragraph's text alignment.</summary>
        ITextSpanContainer Alignment(TextAlignment alignment);

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
    }
}
