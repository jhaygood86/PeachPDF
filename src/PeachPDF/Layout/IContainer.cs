using PeachBrackets.Annotations;
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

        /// <summary>Sets the smallest width this container may be laid out at (<c>min-width</c>).</summary>
        IContainer MinWidth(PdfLength value);

        /// <summary>Sets the largest width this container may be laid out at (<c>max-width</c>).</summary>
        IContainer MaxWidth(PdfLength value);

        /// <summary>Sets the smallest height this container may be laid out at (<c>min-height</c>).</summary>
        IContainer MinHeight(PdfLength value);

        /// <summary>Sets the largest height this container may be laid out at (<c>max-height</c>).</summary>
        IContainer MaxHeight(PdfLength value);

        /// <summary>
        /// Sets how opaque this container and everything in it is, from 0 (invisible) to 1 (the default), as one group
        /// (<c>opacity</c>); a value outside the range is clamped to it.
        /// </summary>
        IContainer Opacity(double value);

        /// <summary>
        /// Raises this container above (positive) or below (negative) its siblings in paint order (<c>z-index</c>), without moving it:
        /// a container with no <c>position</c> of its own is made <c>position: relative</c> with no offset, which is what lets
        /// <c>z-index</c> apply to it.
        /// </summary>
        IContainer ZIndex(int value);

        /// <summary>Sets the direction text and inline content flow in inside this container (<c>direction</c>); inherited by what it holds.</summary>
        IContainer Direction(PdfTextDirection direction);

        /// <summary>Lays this container's content out from left to right - <see cref="Direction"/> with <see cref="PdfTextDirection.Ltr"/>.</summary>
        IContainer ContentFromLeftToRight();

        /// <summary>Lays this container's content out from right to left - <see cref="Direction"/> with <see cref="PdfTextDirection.Rtl"/>.</summary>
        IContainer ContentFromRightToLeft();

        /// <summary>
        /// Rotates this container by <paramref name="degrees"/> clockwise around its center. Like every transform here it moves what is
        /// painted, not what is laid out: the container keeps the room it had and its neighbors do not move (<c>transform: rotate()</c>).
        /// Transform calls on one container combine in the order they are made. Swapping the layout width and height for a quarter
        /// turn is not offered; <c>Rotate(90)</c> and <c>Rotate(-90)</c> turn the painting only.
        /// </summary>
        IContainer Rotate(double degrees);

        /// <summary>Mirrors this container left to right around its center, painting only (<c>scaleX(-1)</c>).</summary>
        IContainer FlipHorizontal();

        /// <summary>Mirrors this container top to bottom around its center, painting only (<c>scaleY(-1)</c>).</summary>
        IContainer FlipVertical();

        /// <summary>Mirrors this container on both axes around its center, painting only.</summary>
        IContainer FlipOver();

        /// <summary>Moves what is painted horizontally by <paramref name="distance"/> (positive to the right) without changing the layout.</summary>
        IContainer TranslateX(PdfLength distance);

        /// <summary>Moves what is painted vertically by <paramref name="distance"/> (positive downward) without changing the layout.</summary>
        IContainer TranslateY(PdfLength distance);

        /// <summary>
        /// Scales what is painted by <paramref name="factor"/> around the container's center (<c>transform: scale()</c>). The layout is
        /// not scaled: the container keeps its size and its neighbors stay where they were.
        /// </summary>
        IContainer TransformScale(double factor);

        /// <summary>Scales what is painted horizontally by <paramref name="factor"/> around the container's center, without changing the layout.</summary>
        IContainer TransformScaleX(double factor);

        /// <summary>Scales what is painted vertically by <paramref name="factor"/> around the container's center, without changing the layout.</summary>
        IContainer TransformScaleY(double factor);

        /// <summary>
        /// Keeps this container whole on one page when it fits on one, moving it to the next page rather than splitting it
        /// (<c>break-inside: avoid</c>); a container taller than a page still splits.
        /// </summary>
        IContainer PreventPageBreak();

        /// <summary>
        /// Keeps this container whole on one page when it fits on one - <see cref="PreventPageBreak"/>. A container taller than a page
        /// still splits across pages rather than failing.
        /// </summary>
        IContainer ShowEntire();

        /// <summary>
        /// Lays this container's content out in <paramref name="count"/> columns (<c>column-count</c>). The columns are balanced
        /// unless <see cref="ColumnFill"/> says otherwise. A container with a definite height makes no overflow columns.
        /// </summary>
        IContainer Columns(int count);

        /// <summary>Sets the gap between this container's columns (<c>column-gap</c>).</summary>
        IContainer ColumnGap(PdfLength gap);

        /// <summary>Draws a rule of the given width and color in the middle of the gap between this container's columns (<c>column-rule</c>).</summary>
        IContainer ColumnRule(PdfLength width, PdfColor color);

        /// <summary>Balances the height of this container's columns (<c>column-fill: balance</c>, the default) or fills each in turn (<c>auto</c>).</summary>
        IContainer ColumnFill(bool balance);

        /// <summary>Inside a <see cref="IColumnDescriptor"/>/<see cref="IRowDescriptor"/> item, lets this item grow to fill available space (<c>flex-grow</c>).</summary>
        IContainer Grow(double ratio = 1);

        /// <summary>
        /// Left-aligns this container's own content. On a table cell (<see cref="ITableRowDescriptor.Cell"/>),
        /// whose margins cannot position it, this aligns the cell's text; anywhere else it positions the container
        /// within its parent.
        /// </summary>
        IContainer AlignLeft();

        /// <summary>
        /// Center-aligns this container's own content horizontally. On a table cell this centers the cell's text;
        /// anywhere else it centers the container within its parent.
        /// </summary>
        IContainer AlignCenter();

        /// <summary>
        /// Right-aligns this container's own content. On a table cell this right-aligns the cell's text - the
        /// way to right-align a column of amounts; anywhere else it pushes the container to the right of its
        /// parent.
        /// </summary>
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

        /// <summary>Places a reusable <see cref="PdfImage"/> - pass the same instance to more than one container to place it in multiple places without reloading/redecoding its source. Works for a shared SVG source too - <see cref="PdfImage"/> detects the format automatically, same as the other overloads. Terminal - may be called at most once.</summary>
        void Image(PdfImage image);

        /// <summary>
        /// Places an image generated on demand, at exactly this container's own resolved size, once that
        /// size is known - <paramref name="generator"/> receives it as a <see cref="PdfSize"/>. Requires
        /// this container to have (or inherit, via the default <c>width:100%;height:100%</c> this fills
        /// with) a definite size: an explicit <see cref="Width"/>/<see cref="Height"/> on it, or an
        /// ancestor that already has one - a container with no definite size anywhere in its ancestry
        /// throws <see cref="InvalidOperationException"/> once layout reaches it. Useful for a chart or
        /// other generated graphic that should render at its actual placed resolution rather than a
        /// guessed fixed one (mirrors QuestPDF's own dynamic-image API). Terminal - may be called at most once.
        /// </summary>
        void Image(Func<PdfSize, byte[]> generator);

        /// <summary>Places inline SVG markup. Terminal - may be called at most once.</summary>
        void Svg(string svgMarkup);

        /// <summary>Places inline SVG markup read fully from a stream. Terminal - may be called at most once.</summary>
        void Svg(Stream stream);

        /// <summary>Places inline SVG markup loaded from raw bytes. Terminal - may be called at most once.</summary>
        void Svg(byte[] data);

        /// <summary>
        /// Places SVG markup generated on demand, at exactly this container's own resolved size, once
        /// that size is known - see <see cref="Image(Func{PdfSize,byte[]})"/>'s own doc comment for the
        /// size-resolution rules, which are identical here. Terminal - may be called at most once.
        /// </summary>
        void Svg(Func<PdfSize, string> generator);

        /// <summary>
        /// Places an encoded barcode (see <see cref="PeachDrawing.Core.Barcodes.Barcode"/>) as vector artwork. Its
        /// size follows from the module size in <paramref name="configure"/>'s <see cref="BarcodeStyle"/>. The
        /// barcode keeps the options it was created with. Terminal - may be called at most once.
        /// </summary>
        void Barcode(PeachDrawing.Core.Barcodes.Barcode barcode, Action<BarcodeStyle>? configure = null);

        /// <summary>Places a QR code holding <paramref name="text"/>. Terminal - may be called at most once.</summary>
        void QrCode(string text, Action<BarcodeStyle>? configure = null);

        /// <summary>Places a Data Matrix (ECC 200) symbol holding <paramref name="text"/>. Terminal - may be called at most once.</summary>
        void DataMatrix(string text, Action<BarcodeStyle>? configure = null);

        /// <summary>Places a Code 128 barcode of ASCII <paramref name="text"/>. Terminal - may be called at most once.</summary>
        void Code128(string text, Action<BarcodeStyle>? configure = null);

        /// <summary>
        /// Places a GS1-128 barcode from GS1 element strings with parenthesized application identifiers, such as
        /// <c>(01)09501101530003(10)AB-123</c>. Terminal - may be called at most once.
        /// </summary>
        void Gs1128(string elements, Action<BarcodeStyle>? configure = null);

        /// <summary>Places an EAN-13 barcode of 12 digits, or 13 with the check digit. Terminal - may be called at most once.</summary>
        void Ean13(string digits, Action<BarcodeStyle>? configure = null);

        /// <summary>Places an EAN-8 barcode of 7 digits, or 8 with the check digit. Terminal - may be called at most once.</summary>
        void Ean8(string digits, Action<BarcodeStyle>? configure = null);

        /// <summary>Places a UPC-A barcode of 11 digits, or 12 with the check digit. Terminal - may be called at most once.</summary>
        void UpcA(string digits, Action<BarcodeStyle>? configure = null);

        /// <summary>
        /// Parses <paramref name="html"/> as an HTML fragment (through the same HTML parser and cascade the
        /// HTML-string rendering path uses) and splices the result into this container's tree, in place, as
        /// one new child holding the fragment's own top-level nodes (which may be more than one - unlike
        /// every other terminal method here, an HTML fragment can itself contain several sibling elements).
        /// Terminal - may be called at most once.
        /// <para>
        /// <paramref name="stylesheet"/> supplies the CSS the fragment's own class/id-driven styling
        /// cascades against, layered under the fragment's own <c>&lt;style&gt;</c> tags and the user-agent
        /// defaults every HTML element already gets; null styles the fragment with only those two. A
        /// <c>&lt;link rel="stylesheet"&gt;</c> inside the fragment is never loaded (there is no document
        /// context to resolve it through at this point in building the tree) - author it as a
        /// <c>&lt;style&gt;</c> tag, or pass it via <paramref name="stylesheet"/>, instead. A
        /// <c>float: footnote</c> element inside the fragment renders as ordinary inline content rather than
        /// being detached into a footnote area, for the same reason.
        /// </para>
        /// <para>
        /// <paramref name="onSlot"/>, when given, is invoked once per <c>&lt;slot&gt;</c> element the
        /// fragment contains (in document order, regardless of name, including more than one sharing a
        /// name), with a <see cref="SlotContext"/> describing it and an <see cref="IContainer"/> positioned
        /// to replace it - populate that container the same way any other container is populated (e.g.
        /// <c>slotContainer.Text(...)</c>) to fill the slot. Leaving it untouched (or passing no
        /// <paramref name="onSlot"/> at all) keeps the slot's own fallback content - whatever markup it
        /// contained in <paramref name="html"/> - exactly as authored.
        /// </para>
        /// </summary>
        void Html(string html, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null);

        /// <summary>Places an HTML fragment read fully from a stream - see <see cref="Html(string, PeachPdfCssContent?, Action{SlotContext, IContainer}?)"/>. Terminal - may be called at most once.</summary>
        void Html(Stream stream, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null);

        /// <summary>Places an HTML fragment loaded from raw bytes - see <see cref="Html(string, PeachPdfCssContent?, Action{SlotContext, IContainer}?)"/>. Terminal - may be called at most once.</summary>
        void Html(byte[] data, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null);

        /// <summary>Places a horizontal rule of the given thickness, filled with <paramref name="color"/> (default black), or dashed when <paramref name="dashed"/> is true. Terminal - may be called at most once.</summary>
        void LineHorizontal(PdfLength thickness, PdfColor? color = null, bool dashed = false);

        /// <summary>Places a vertical rule of the given thickness, filled with <paramref name="color"/> (default black), or dashed when <paramref name="dashed"/> is true. Terminal - may be called at most once.</summary>
        void LineVertical(PdfLength thickness, PdfColor? color = null, bool dashed = false);

        /// <summary>Places a break that starts whatever follows on a new page. Terminal - may be called at most once.</summary>
        void PageBreak();

        /// <summary>
        /// Places content flowing through several columns (<c>column-count</c>). Terminal - may be called at most once. A container
        /// with a definite height makes no overflow columns, a float that does not fit its column overflows it, and a float inside a
        /// table cell is not split into columns.
        /// </summary>
        void MultiColumn(Action<IMultiColumnDescriptor> handler);

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

        /// <summary>
        /// Tags this container's own physical position as the start of a page-numbered section named
        /// <paramref name="sectionId"/>, anywhere in a page's normal content flow (not just
        /// <see cref="IPageDescriptor.Header"/>/<see cref="IPageDescriptor.Footer"/> content) - a
        /// decorator, like <see cref="Bookmark"/>, so chain it before the container's own terminal content
        /// (<c>column.Item().BeginPageNumberOfSection("chapter1").Text("Chapter 1")</c>) rather than
        /// giving the section boundary a container of its own. Pair with a later
        /// <see cref="EndPageNumberOfSection"/> call using the same id, on whichever container holds the
        /// section's own last piece of content; <see cref="ITextSpanContainer.PageNumberWithinSection"/>/
        /// <see cref="ITextSpanContainer.TotalPagesWithinSection"/> then resolve against the physical
        /// pages that pair of tagged containers land on.
        /// </summary>
        IContainer BeginPageNumberOfSection(string sectionId);

        /// <summary>Tags this container's own physical position as the end of the page-numbered section <paramref name="sectionId"/> - see <see cref="BeginPageNumberOfSection"/>.</summary>
        IContainer EndPageNumberOfSection(string sectionId);

        /// <summary>
        /// Tags this container with a CSS class name, so a stylesheet attached via
        /// <see cref="IDocumentBuilder.Stylesheet"/> can target it with a <c>.className</c> selector
        /// (including compound/descendant selectors built from it). Purely inert metadata with no visual
        /// effect when no stylesheet is attached - safe to call as a stable hook even if styling is added
        /// later. Calling this more than once on the same container adds an additional class
        /// (space-separated), matching HTML's own multi-valued <c>class</c> attribute, rather than replacing
        /// the previous one.
        /// </summary>
        IContainer Class(string className);

        /// <summary>
        /// Tags this container with a CSS id, so a stylesheet attached via
        /// <see cref="IDocumentBuilder.Stylesheet"/> can target it with a <c>#id</c> selector. Purely inert
        /// metadata with no visual effect when no stylesheet is attached. Calling this more than once on the
        /// same container replaces the previous id (an element has at most one id) - unlike
        /// <see cref="Class"/>, this does not accumulate.
        /// </summary>
        IContainer Id(string id);

        /// <summary>
        /// Renames this container's own internal tag name (a synthetic <c>"div"</c>/<c>"img"</c>/<c>"a"</c>
        /// by default, depending on which method created it) to <paramref name="tagName"/>, so a stylesheet
        /// attached via <see cref="IDocumentBuilder.Stylesheet"/> can target it with a bare type selector
        /// (e.g. <c>li { ... }</c>) matching the intended semantic element rather than the internal default.
        /// Purely inert metadata with no visual effect when no stylesheet is attached.
        /// </summary>
        IContainer Tag(string tagName);

        /// <summary>
        /// Tags this container's own position as the start of the named page type <paramref name="name"/>
        /// (CSS <c>page</c> property, <see href="https://www.w3.org/TR/CSS21/page.html#page-selectors">CSS
        /// 2.1 §13.2</see>) - combine with a document-level stylesheet's own <c>@page name { ... }</c> rule
        /// (see <see cref="IDocumentBuilder.Stylesheet"/>) to switch page size/margins/orientation from this
        /// point in the flow onward, forcing a page break if a different named page (or no named page) was
        /// active immediately before. A decorator, like <see cref="Bookmark"/>/
        /// <see cref="BeginPageNumberOfSection"/> - chain it before the container's own terminal content.
        /// </summary>
        IContainer PageName(string name);
    }
}
