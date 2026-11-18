using PeachDrawing.PDF;
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

        /// <summary>
        /// Labels this page's pages the way a PDF viewer shows them in its page box and thumbnails ("iv", "A-3"), starting at this page's first
        /// physical page. The label runs on over every later page until another section sets one, so a section that should go back to plain
        /// numbering sets <see cref="PdfPageLabelStyle.Decimal"/> itself. This is the page label PDF stores, not the number a footer prints.
        /// </summary>
        /// <param name="style">how the page number is written</param>
        /// <param name="prefix">text before the number, or <see langword="null"/> for none</param>
        /// <param name="startNumber">the number of this page's first physical page, 1 or more</param>
        IPageDescriptor PageLabel(PdfPageLabelStyle style, string? prefix = null, int startNumber = 1);

        /// <summary>Sets content repeated at the top of every physical page this page's content paginates across.</summary>
        IPageDescriptor Header(Action<IContainer> handler);

        /// <summary>Sets content repeated at the bottom of every physical page this page's content paginates across.</summary>
        IPageDescriptor Footer(Action<IContainer> handler);

        /// <summary>Lays this page's content out from left to right (<c>direction: ltr</c>).</summary>
        IPageDescriptor ContentFromLeftToRight();

        /// <summary>Lays this page's content out from right to left (<c>direction: rtl</c>).</summary>
        IPageDescriptor ContentFromRightToLeft();

        /// <summary>Sets this page's main flowing, paginated content.</summary>
        void Content(Action<IContainer> handler);
    }
}
