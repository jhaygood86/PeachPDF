using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Configures one page-section of a declarative document (<see cref="IDocumentBuilder.Page"/>): its
    /// page geometry and the content that flows and paginates within it.
    /// </summary>
    public interface IPageDescriptor
    {
        /// <summary>Sets this page's sheet size. Unset falls back to the document's <see cref="PdfGenerateConfig.PageSize"/>.</summary>
        IPageDescriptor Size(PageSize size);

        /// <summary>Sets this page's sheet size explicitly. Unset falls back to the document's <see cref="PdfGenerateConfig"/> page size.</summary>
        IPageDescriptor Size(PdfLength width, PdfLength height);

        /// <summary>Sets this page's orientation (swaps width/height when it disagrees with the declared size).</summary>
        IPageDescriptor Orientation(PageOrientation orientation);

        /// <summary>Sets all four margins to the same value. Unset falls back to the document's <see cref="PdfGenerateConfig"/> margins.</summary>
        IPageDescriptor Margin(PdfLength value);

        /// <summary>Sets the left and right margins.</summary>
        IPageDescriptor MarginHorizontal(PdfLength value);

        /// <summary>Sets the top and bottom margins.</summary>
        IPageDescriptor MarginVertical(PdfLength value);

        /// <summary>Sets the top margin.</summary>
        IPageDescriptor MarginTop(PdfLength value);

        /// <summary>Sets the bottom margin.</summary>
        IPageDescriptor MarginBottom(PdfLength value);

        /// <summary>Sets the left margin.</summary>
        IPageDescriptor MarginLeft(PdfLength value);

        /// <summary>Sets the right margin.</summary>
        IPageDescriptor MarginRight(PdfLength value);

        /// <summary>Paints a solid background color behind this page's whole sheet.</summary>
        IPageDescriptor Background(PdfColor color);

        /// <summary>
        /// Sets the default text style every span of text in this page inherits unless it overrides a
        /// property itself.
        /// </summary>
        IPageDescriptor DefaultTextStyle(Action<ITextStyle> handler);

        /// <summary>Sets content repeated at the top of every physical page this page's content paginates across.</summary>
        IPageDescriptor Header(Action<IContainer> handler);

        /// <summary>Sets content repeated at the bottom of every physical page this page's content paginates across.</summary>
        IPageDescriptor Footer(Action<IContainer> handler);

        /// <summary>Sets this page's main flowing, paginated content.</summary>
        void Content(Action<IContainer> handler);
    }
}
