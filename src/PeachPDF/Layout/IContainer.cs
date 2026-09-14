using System;
using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// One composable content box in a declarative document - reused everywhere content is placed
    /// (a page's content area, a column/row item, a table cell). Every decorator method wraps the
    /// current box in one more layer and returns the new, innermost <see cref="IContainer"/>; every
    /// terminal method places real content and may be called at most once per container.
    /// </summary>
    public interface IContainer
    {
        /// <summary>Sets padding on all four sides.</summary>
        IContainer Padding(PdfLength value);

        /// <summary>Sets left and right padding.</summary>
        IContainer PaddingHorizontal(PdfLength value);

        /// <summary>Sets top and bottom padding.</summary>
        IContainer PaddingVertical(PdfLength value);

        /// <summary>Sets top padding.</summary>
        IContainer PaddingTop(PdfLength value);

        /// <summary>Sets bottom padding.</summary>
        IContainer PaddingBottom(PdfLength value);

        /// <summary>Sets left padding.</summary>
        IContainer PaddingLeft(PdfLength value);

        /// <summary>Sets right padding.</summary>
        IContainer PaddingRight(PdfLength value);

        /// <summary>Draws a solid border of <paramref name="width"/>/<paramref name="color"/> (default black) on all four sides.</summary>
        IContainer Border(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the left and right sides.</summary>
        IContainer BorderHorizontal(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the top and bottom sides.</summary>
        IContainer BorderVertical(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the top side.</summary>
        IContainer BorderTop(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the bottom side.</summary>
        IContainer BorderBottom(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the left side.</summary>
        IContainer BorderLeft(PdfLength width, PdfColor? color = null);

        /// <summary>Draws a solid border on the right side.</summary>
        IContainer BorderRight(PdfLength width, PdfColor? color = null);

        /// <summary>Sets every already-declared border edge's color.</summary>
        IContainer BorderColor(PdfColor color);

        /// <summary>Paints a solid background color behind this container's own box.</summary>
        IContainer Background(PdfColor color);

        /// <summary>Sets a background linear gradient (angle in degrees, clockwise from the top), with 2 or more evenly-spaced color stops.</summary>
        IContainer BackgroundLinearGradient(double angleDegrees, params PdfColor[] stops);

        /// <summary>Draws a linear-gradient border (angle in degrees, clockwise from the top) of <paramref name="width"/>, with 2 or more evenly-spaced color stops, on all four sides.</summary>
        IContainer BorderLinearGradient(PdfLength width, double angleDegrees, params PdfColor[] stops);

        /// <summary>Adds one or more drop shadows behind this container's own box.</summary>
        IContainer Shadow(params PdfBoxShadow[] shadows);

        /// <summary>Rounds all four corners by the same radius.</summary>
        IContainer CornerRadius(PdfLength radius);

        /// <summary>Rounds the top-left corner.</summary>
        IContainer CornerRadiusTopLeft(PdfLength radius);

        /// <summary>Rounds the top-right corner.</summary>
        IContainer CornerRadiusTopRight(PdfLength radius);

        /// <summary>Rounds the bottom-left corner.</summary>
        IContainer CornerRadiusBottomLeft(PdfLength radius);

        /// <summary>Rounds the bottom-right corner.</summary>
        IContainer CornerRadiusBottomRight(PdfLength radius);

        /// <summary>Sets an explicit width.</summary>
        IContainer Width(PdfLength value);

        /// <summary>Sets an explicit height.</summary>
        IContainer Height(PdfLength value);

        /// <summary>Inside a <see cref="IColumnDescriptor"/>/<see cref="IRowDescriptor"/> item, lets this item grow to fill available space (<c>flex-grow</c>).</summary>
        IContainer Grow(double ratio = 1);

        /// <summary>Left-aligns this container's own content.</summary>
        IContainer AlignLeft();

        /// <summary>Center-aligns this container's own content horizontally.</summary>
        IContainer AlignCenter();

        /// <summary>Right-aligns this container's own content.</summary>
        IContainer AlignRight();

        /// <summary>
        /// Sets the default text style every span of text inside this container inherits unless it
        /// overrides a property itself.
        /// </summary>
        IContainer DefaultTextStyle(Action<ITextStyle> handler);

        /// <summary>Wraps this container in a hyperlink to <paramref name="url"/>.</summary>
        IContainer Hyperlink(string url);

        /// <summary>
        /// Wraps this container in a hyperlink to <paramref name="url"/>. An internal document anchor
        /// (a <c>#fragment</c>-only URI) is not yet supported by this overload - use the string overload
        /// with a full external URL.
        /// </summary>
        IContainer Hyperlink(Uri url);

        /// <summary>Adds a PDF outline (bookmark) entry pointing at this container, titled <paramref name="title"/> at nesting <paramref name="level"/> (1 = top level).</summary>
        IContainer Bookmark(string title, int level = 1);

        /// <summary>Indents this container's first line (<c>text-indent</c>). Applies only when this container's own terminal content is text.</summary>
        IContainer ParagraphFirstLineIndentation(PdfLength value);

        /// <summary>Adds space after this container (<c>margin-bottom</c>) - a convenience for spacing paragraphs apart.</summary>
        IContainer ParagraphSpacing(PdfLength value);

        /// <summary>
        /// Truncates this container's own text after <paramref name="lines"/> lines, marking the cut with
        /// an ellipsis (<c>overflow: hidden</c> + <c>line-clamp</c>). Applies only when this container's
        /// own terminal content is text. By default the cut is marked with the usual "…"; pass
        /// <paramref name="ellipsis"/> for a custom marker instead (e.g. <c>" [more]"</c>), or
        /// <c>""</c> for no marker at all - the text is still cut at <paramref name="lines"/> lines, just
        /// with nothing appended (<c>block-ellipsis: none</c>).
        /// </summary>
        IContainer ClampLines(int lines, string? ellipsis = null);

        /// <summary>Places a paragraph of plain text. Terminal - may be called at most once.</summary>
        void Text(string text);

        /// <summary>Places a paragraph of richly-styled text spans. Terminal - may be called at most once.</summary>
        void Text(Action<ITextSpanContainer> handler);

        /// <summary>Places an image loaded from raw bytes. Terminal - may be called at most once.</summary>
        void Image(byte[] data);

        /// <summary>Places an image read fully from a stream. Terminal - may be called at most once.</summary>
        void Image(Stream stream);

        /// <summary>Places an image loaded over the network. Terminal - may be called at most once.</summary>
        void Image(Uri uri);

        /// <summary>Places an image loaded from a local file path. Terminal - may be called at most once.</summary>
        void Image(string filePath);

        /// <summary>Places a reusable <see cref="PdfImage"/> - pass the same instance to more than one container to place it in multiple places without reloading its source. Terminal - may be called at most once.</summary>
        void Image(PdfImage image);

        /// <summary>Places a horizontal rule of the given thickness, filled with <paramref name="color"/> (default black). Terminal - may be called at most once.</summary>
        void LineHorizontal(PdfLength thickness, PdfColor? color = null);

        /// <summary>Places a vertical rule of the given thickness, filled with <paramref name="color"/> (default black). Terminal - may be called at most once.</summary>
        void LineVertical(PdfLength thickness, PdfColor? color = null);

        /// <summary>Places a horizontal row of items. Terminal - may be called at most once.</summary>
        void Row(Action<IRowDescriptor> handler);

        /// <summary>Places a vertical column of items. Terminal - may be called at most once.</summary>
        void Column(Action<IColumnDescriptor> handler);

        /// <summary>Places a table. Terminal - may be called at most once.</summary>
        void Table(Action<ITableDescriptor> handler);

        /// <summary>Places a numbered list. Terminal - may be called at most once.</summary>
        void OrderedList(Action<IListDescriptor> handler, PdfListMarkerType markerType = PdfListMarkerType.Decimal);

        /// <summary>Places a bulleted list. Terminal - may be called at most once.</summary>
        void UnorderedList(Action<IListDescriptor> handler, PdfListMarkerType markerType = PdfListMarkerType.Disc);
    }
}
