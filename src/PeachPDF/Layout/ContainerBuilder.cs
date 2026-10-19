using PeachBrackets.Annotations;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Building;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PeachPDF.Layout
{
    /// <summary>
    /// The one, reused <see cref="IContainer"/> implementation. A property-setting decorator
    /// (padding/border/background/corner-radius/width/height/grow/align/default-text-style) sets CSS
    /// properties directly on the wrapped box and returns <c>this</c> - a single <c>CssBox</c>
    /// already natively supports holding all of these at once, so there's no need for a new wrapper box
    /// per decorator the way a from-scratch layout model might need. <see cref="Hyperlink(string)"/> is
    /// the one exception: an <c>&lt;a href&gt;</c> box is a real structural requirement
    /// (<c>CssBox.IsClickable</c> has no other trigger), so it creates a genuine new child and
    /// returns a builder for that instead. Every terminal method (<see cref="Text(string)"/>,
    /// <see cref="Image(byte[])"/>, <see cref="Row"/>, <see cref="Column"/>, etc.) places its content as
    /// a new child of the wrapped box, including plain text - a box must never hold both its own words
    /// and child boxes at once (real HTML never does either: a text node is always its own anonymous
    /// child box), and the wrapped box can already have one it never advertises - a list item's
    /// synthesized <c>::marker</c> (<see cref="ListDescriptorBuilder.Item"/>), most notably - so this
    /// applies unconditionally rather than only when a caller's own content happens to need it. May run
    /// at most once.
    /// </summary>
    internal sealed class ContainerBuilder(DocumentNode box) : IContainer
    {
        /// <summary>The node this builder fills; reached by tests that assert on the resulting box.</summary>
        internal DocumentNode Node => box;

        private bool _terminalUsed;

        /// <summary>Whether this container's one piece of terminal content has already been placed - used by <see cref="Html(string, PeachPdfCssContent?, Action{SlotContext, IContainer}?)"/>'s slot handling to tell whether a slot callback actually populated the container it was given.</summary>
        internal bool HasContent => _terminalUsed;

        private void MarkTerminal()
        {
            if (_terminalUsed)
                throw new InvalidOperationException(
                    "This container's content was already set - a container may hold only one piece of " +
                    "terminal content (Text/Image/Row/Column/LineHorizontal/LineVertical).");
            _terminalUsed = true;
        }

        // ─── padding ────────────────────────────────────────────────────────────

        public IContainer Padding(PdfLength value)
        {
            PaddingTop(value);
            PaddingRight(value);
            PaddingBottom(value);
            PaddingLeft(value);
            return this;
        }

        public IContainer PaddingHorizontal(PdfLength value)
        {
            PaddingLeft(value);
            PaddingRight(value);
            return this;
        }

        public IContainer PaddingVertical(PdfLength value)
        {
            PaddingTop(value);
            PaddingBottom(value);
            return this;
        }

        public IContainer PaddingTop(PdfLength value)
        {
            box.SetStyle("padding-top", value);
            return this;
        }

        public IContainer PaddingBottom(PdfLength value)
        {
            box.SetStyle("padding-bottom", value);
            return this;
        }

        public IContainer PaddingLeft(PdfLength value)
        {
            box.SetStyle("padding-left", value);
            return this;
        }

        public IContainer PaddingRight(PdfLength value)
        {
            box.SetStyle("padding-right", value);
            return this;
        }

        // ─── border ─────────────────────────────────────────────────────────────

        public IContainer Border(PdfLength width, PdfColor? color = null)
        {
            BorderTop(width, color);
            BorderRight(width, color);
            BorderBottom(width, color);
            BorderLeft(width, color);
            return this;
        }

        public IContainer BorderHorizontal(PdfLength width, PdfColor? color = null)
        {
            BorderLeft(width, color);
            BorderRight(width, color);
            return this;
        }

        public IContainer BorderVertical(PdfLength width, PdfColor? color = null)
        {
            BorderTop(width, color);
            BorderBottom(width, color);
            return this;
        }

        public IContainer BorderTop(PdfLength width, PdfColor? color = null) => SetBorderEdge("top", width, color);
        public IContainer BorderBottom(PdfLength width, PdfColor? color = null) => SetBorderEdge("bottom", width, color);
        public IContainer BorderLeft(PdfLength width, PdfColor? color = null) => SetBorderEdge("left", width, color);
        public IContainer BorderRight(PdfLength width, PdfColor? color = null) => SetBorderEdge("right", width, color);

        private IContainer SetBorderEdge(string side, PdfLength width, PdfColor? color)
        {
            box.SetStyle($"border-{side}-width", width);
            box.SetStyle($"border-{side}-style", "solid");
            box.SetStyle($"border-{side}-color", color ?? PdfColor.Black);
            return this;
        }

        public IContainer BorderColor(PdfColor color)
        {
            box.SetStyle("border-top-color", color);
            box.SetStyle("border-right-color", color);
            box.SetStyle("border-bottom-color", color);
            box.SetStyle("border-left-color", color);
            return this;
        }

        // ─── background / corner radius ─────────────────────────────────────────

        public IContainer Background(PdfColor color)
        {
            box.SetStyle("background-color", color);
            return this;
        }

        public IContainer BackgroundLinearGradient(double angleDegrees, params PdfColor[] stops)
        {
            var stopsText = string.Join(", ", stops.Select(s => s.ToString()));
            box.SetStyle("background-image",
                string.Create(CultureInfo.InvariantCulture, $"linear-gradient({angleDegrees}deg, {stopsText})"));
            return this;
        }

        public IContainer BorderLinearGradient(PdfLength width, double angleDegrees, params PdfColor[] stops)
        {
            // A border-image needs an actual border box to paint into - the color is irrelevant (the
            // gradient image paints over it) but the width determines how thick each painted edge is.
            Border(width, PdfColor.Transparent);

            var stopsText = string.Join(", ", stops.Select(s => s.ToString()));
            box.SetStyle("border-image-source",
                string.Create(CultureInfo.InvariantCulture, $"linear-gradient({angleDegrees}deg, {stopsText})"));

            // The standard "gradient border" recipe: a 1-unit slice with stretch repeat treats each edge
            // as one continuous stretched strip of the (infinitely-scalable) gradient, rather than tiling
            // or rounding it - PR3's border-image painting already implements gradient/url() sources with
            // stretch repeat.
            box.SetStyle("border-image-slice", "1");
            box.SetStyle("border-image-repeat", "stretch");
            return this;
        }

        public IContainer Shadow(params PdfBoxShadow[] shadows)
        {
            box.SetStyle("box-shadow", string.Join(", ", shadows.Select(s => s.ToCssText())));
            return this;
        }

        public IContainer CornerRadius(PdfLength radius)
        {
            CornerRadiusTopLeft(radius);
            CornerRadiusTopRight(radius);
            CornerRadiusBottomRight(radius);
            CornerRadiusBottomLeft(radius);
            return this;
        }

        public IContainer CornerRadiusTopLeft(PdfLength radius)
        {
            box.SetStyle("border-top-left-radius", radius);
            return this;
        }

        public IContainer CornerRadiusTopRight(PdfLength radius)
        {
            box.SetStyle("border-top-right-radius", radius);
            return this;
        }

        public IContainer CornerRadiusBottomLeft(PdfLength radius)
        {
            box.SetStyle("border-bottom-left-radius", radius);
            return this;
        }

        public IContainer CornerRadiusBottomRight(PdfLength radius)
        {
            box.SetStyle("border-bottom-right-radius", radius);
            return this;
        }

        // ─── sizing / alignment ─────────────────────────────────────────────────

        public IContainer Width(PdfLength value)
        {
            box.SetStyle("width", value);
            return this;
        }

        public IContainer Height(PdfLength value)
        {
            box.SetStyle("height", value);
            return this;
        }

        public IContainer Grow(double ratio = 1)
        {
            box.SetStyle("flex-grow", ratio.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>
        /// Margins do not apply to a table cell (CSS 2.1 §17.5), so the auto-margin positioning the three
        /// Align methods use everywhere else would silently do nothing on one. What "align this container's own
        /// content" can mean for a cell is aligning its inline content - which is what a table author asks
        /// for when they right-align a column of amounts - so a cell gets <c>text-align</c> instead.
        /// </summary>
        private bool IsTableCell => box.IsTableCell;

        public IContainer AlignLeft()
        {
            if (IsTableCell)
            {
                box.SetTextAlign("left");
                return this;
            }

            box.SetStyle("margin-left", "0");
            box.SetStyle("margin-right", "auto");
            return this;
        }

        public IContainer AlignCenter()
        {
            if (IsTableCell)
            {
                box.SetTextAlign("center");
                return this;
            }

            box.SetStyle("margin-left", "auto");
            box.SetStyle("margin-right", "auto");
            return this;
        }

        public IContainer AlignRight()
        {
            if (IsTableCell)
            {
                box.SetTextAlign("right");
                return this;
            }

            box.SetStyle("margin-left", "auto");
            box.SetStyle("margin-right", "0");
            return this;
        }

        // ─── text style / hyperlink / bookmark ──────────────────────────────────

        public IContainer DefaultTextStyle(Action<ITextStyle> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            handler(new TextStyleApplier(box));
            return this;
        }

        public IContainer ParagraphFirstLineIndentation(PdfLength value)
        {
            box.SetStyle("text-indent", value);
            return this;
        }

        public IContainer ParagraphSpacing(PdfLength value)
        {
            box.SetStyle("margin-bottom", value);
            return this;
        }

        public IContainer ClampLines(int lines, string? ellipsis = null)
        {
            box.SetStyle("overflow-x", "hidden");
            box.SetStyle("overflow-y", "hidden");
            box.SetStyle("line-clamp", lines.ToString(CultureInfo.InvariantCulture));

            if (ellipsis is not null)
            {
                box.SetStyle("block-ellipsis",
                    ellipsis.Length == 0 ? "none" : "\"" + EscapeCssStringLiteral(ellipsis) + "\"");
            }

            return this;
        }

        public IContainer Hyperlink(string url)
        {
            ArgumentNullException.ThrowIfNull(url);
            var link = box.AppendElement("a", new Dictionary<string, string> { ["href"] = url });
            return new ContainerBuilder(link);
        }

        public IContainer Hyperlink(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            return Hyperlink(url.ToString());
        }

        public IContainer Bookmark(string title, int level = 1)
        {
            ArgumentNullException.ThrowIfNull(title);
            box.SetBookmark(title, level);
            return this;
        }

        private static string EscapeCssStringLiteral(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ─── terminals ───────────────────────────────────────────────────────────

        public void Text(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            MarkTerminal();

            // A genuine new child, not box.Text directly: box may already hold a child of its own for a
            // reason the caller never sees - a list item's synthesized ::marker (ListDescriptorBuilder.Item),
            // most notably - and a CssBox must never hold both its own words and child boxes at once (the
            // same invariant PR2's line-clamp work already had to respect). Real HTML never puts text
            // directly on an element box with other children either - a text node always becomes its own
            // anonymous child box - so this matches that shape instead of being a declarative-only shortcut.
            box.AppendText(text);
        }

        public void Text(Action<ITextSpanContainer> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            handler(new TextSpanContainerBuilder(box));
        }

        public void Image(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            MarkTerminal();
            CreateImageBox().SetImageContent(DecodeRasterBytes(data));
        }

        public void Image(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            MarkTerminal();
            CreateImageBox().SetImageContent(DecodeRasterBytes(ms.ToArray()));
        }

        public void Image(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            MarkTerminal();
            PlaceImage(uri.ToString());
        }

        public void Image(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            MarkTerminal();
            PlaceImage(filePath);
        }

        public void Image(PdfImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
            MarkTerminal();
            var (decodedImage, svg) = image.Resolve(box.RenderContext);
            var imageBox = CreateImageBox();
            if (svg is not null)
                imageBox.SetImageContent(svg);
            else
                imageBox.SetImageContent(decodedImage!);
        }

        public void Image(Func<PdfSize, byte[]> generator)
        {
            ArgumentNullException.ThrowIfNull(generator);
            MarkTerminal();
            var imageBox = CreateFillingImageBox();
            imageBox.SetImageContent(size => generator(new PdfSize(size.Width, size.Height)));
        }

        public void Svg(string svgMarkup)
        {
            ArgumentNullException.ThrowIfNull(svgMarkup);
            MarkTerminal();
            CreateImageBox().SetImageContent(DecodeSvgMarkup(svgMarkup));
        }

        public void Svg(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            MarkTerminal();
            CreateImageBox().SetImageContent(DecodeSvgMarkup(ms.ToArray()));
        }

        public void Svg(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            MarkTerminal();
            CreateImageBox().SetImageContent(DecodeSvgMarkup(data));
        }

        public void Svg(Func<PdfSize, string> generator)
        {
            ArgumentNullException.ThrowIfNull(generator);
            MarkTerminal();
            var imageBox = CreateFillingImageBox();
            imageBox.SetSvgContent(size => generator(new PdfSize(size.Width, size.Height)));
        }

        /// <summary>
        /// Places a lazily-loaded <c>&lt;img src="..."&gt;</c> - the network/file/data-URI path
        /// <see cref="Image(Uri)"/>/<see cref="Image(string)"/> still use (an eager in-memory decode,
        /// as the other overloads use, doesn't apply when the source isn't in memory yet).
        /// </summary>
        private void PlaceImage(string src) =>
            CreateImageBox(new Dictionary<string, string> { ["src"] = src });

        /// <summary>
        /// Creates a real, correctly-typed <c>CssBoxImage</c> child of the wrapped box - the
        /// tag-dispatching <c>CssBox.CreateBox</c> overload (not the parent-first
        /// one every other terminal here uses, which always constructs a plain <c>CssBox</c>
        /// regardless of tag name - so the resulting box actually reaches <c>FragmentContentPainters.For</c>'s
        /// <c>CssBoxImage =&gt; ImagePainter</c> arm and gets the intrinsic-sizing/image-content handling
        /// only that concrete type implements. That overload doesn't call <c>CssBox.InheritStyle</c>
        /// itself (the normal HTML parse path defers that to its own later whole-tree cascade pass, which
        /// a declaratively-built tree never runs), so this does it explicitly, matching
        /// <c>CssPropertyFactory.CreateAnonymousBox</c>'s identical own call.
        /// </summary>
        private DocumentNode CreateImageBox(Dictionary<string, string>? attributes = null) => box.AppendImage(attributes);

        /// <summary>
        /// A dynamic content box (<see cref="Image(Func{PdfSize,byte[]})"/>/<see cref="Svg(Func{PdfSize,string})"/>)
        /// fills its parent by default (<c>width:100%;height:100%</c>) - "available space" is whatever
        /// this container already resolves to, matching QuestPDF's own dynamic-image framing (its own doc
        /// example wraps the dynamic image in an explicitly-sized/`AspectRatio`d container, rather than
        /// sizing the image call itself) - see <see cref="IContainer.Image(Func{PdfSize,byte[]})"/>'s own
        /// doc comment for the full size-resolution rule this depends on.
        /// </summary>
        private DocumentNode CreateFillingImageBox()
        {
            var imageBox = CreateImageBox();
            imageBox.SetStyle("width", "100%");
            imageBox.SetStyle("height", "100%");
            return imageBox;
        }

        private Image DecodeRasterBytes(byte[] bytes) =>
            box.RenderContext.ImageFromStream(new MemoryStream(bytes));

        private SvgImage DecodeSvgMarkup(string svgMarkup) => SvgImage.Parse(svgMarkup, box.RenderContext);

        /// <summary>
        /// Decodes raw SVG markup bytes with BOM detection (not <see cref="Encoding.UTF8"/>.<see cref="Encoding.GetString(byte[])"/> directly), so a
        /// leading UTF-8 BOM - common in SVG files saved by many editors - is stripped rather than surviving into the decoded string as a literal
        /// U+FEFF character, which the XML parser rejects outright. Same fix as <see cref="PdfImage.Resolve"/>'s own BOM handling.
        /// </summary>
        private SvgImage DecodeSvgMarkup(byte[] svgBytes) => SvgImage.Parse(svgBytes, box.RenderContext);

        public void Html(string html, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null)
        {
            ArgumentNullException.ThrowIfNull(html);
            MarkTerminal();
            SpliceHtmlFragment(html, stylesheet, onSlot);
        }

        public void Html(Stream stream, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            MarkTerminal();
            SpliceHtmlFragment(DecodeHtmlBytes(ms.ToArray()), stylesheet, onSlot);
        }

        public void Html(byte[] data, PeachPdfCssContent? stylesheet = null, Action<SlotContext, IContainer>? onSlot = null)
        {
            ArgumentNullException.ThrowIfNull(data);
            MarkTerminal();
            SpliceHtmlFragment(DecodeHtmlBytes(data), stylesheet, onSlot);
        }

        /// <summary>BOM-aware byte decode, same idiom as <see cref="DecodeSvgMarkup(byte[])"/>.</summary>
        private static string DecodeHtmlBytes(byte[] htmlBytes)
        {
            using var reader = new StreamReader(new MemoryStream(htmlBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Parses <paramref name="html"/>, cascades it, resolves every &lt;slot&gt; it contains, and grafts
        /// the result onto a new anonymous <c>display: block</c> child of the wrapped box - a wrapper is
        /// needed (rather than grafting the fragment's own top-level nodes directly onto it) because a
        /// fragment can have more than one top-level node, and every other terminal method here creates
        /// exactly one new child of the wrapped box rather than placing content on it directly.
        /// </summary>
        private void SpliceHtmlFragment(string html, PeachPdfCssContent? stylesheet, Action<SlotContext, IContainer>? onSlot)
        {
            Func<HtmlFragmentSlot, DocumentNode, bool>? fillSlot = onSlot is null
                ? null
                : (slot, replacement) =>
                {
                    var replacementContainer = new ContainerBuilder(replacement);
                    onSlot(new SlotContext(slot.Name, new Dictionary<string, string>(slot.Attributes, StringComparer.OrdinalIgnoreCase)), replacementContainer);
                    return replacementContainer.HasContent;
                };

            box.AppendHtmlFragment(html, stylesheet?.StyleSheet, fillSlot);
        }

        public void LineHorizontal(PdfLength thickness, PdfColor? color = null, bool dashed = false)
        {
            // border-*-width never accepts a percentage (unlike height/width on the solid path below),
            // so a percentage thickness is rejected up front rather than silently falling back to the
            // initial "medium" width the cascade would otherwise leave in place - checked before
            // MarkTerminal() so a rejected call leaves the container's terminal slot unused.
            if (dashed && thickness.Unit == PdfLengthUnit.Percent)
                throw new ArgumentException("A dashed line's thickness cannot be a percentage - border width has no percentage form.", nameof(thickness));

            MarkTerminal();
            var line = box.AppendBlock();
            line.SetStyle("display", "block");

            if (dashed)
            {
                // A dashed rule can't come from background-color (a solid fill has no dash pattern of
                // its own) - drawn instead as a single dashed top border edge on a zero-height box, the
                // same border-style machinery BordersDrawHandler already paints dotted/dashed borders
                // through (see SetBorderEdge for the solid-border equivalent of this cascade shape).
                line.SetStyle("height", "0");
                line.SetStyle("border-top-width", thickness);
                line.SetStyle("border-top-style", "dashed");
                line.SetStyle("border-top-color", color ?? PdfColor.Black);
            }
            else
            {
                line.SetStyle("height", thickness);
                line.SetStyle("background-color", color ?? PdfColor.Black);
            }
        }

        public void LineVertical(PdfLength thickness, PdfColor? color = null, bool dashed = false)
        {
            if (dashed && thickness.Unit == PdfLengthUnit.Percent)
                throw new ArgumentException("A dashed line's thickness cannot be a percentage - border width has no percentage form.", nameof(thickness));

            MarkTerminal();
            var line = box.AppendBlock();
            line.SetStyle("display", "block");

            if (dashed)
            {
                line.SetStyle("width", "0");
                line.SetStyle("border-left-width", thickness);
                line.SetStyle("border-left-style", "dashed");
                line.SetStyle("border-left-color", color ?? PdfColor.Black);
            }
            else
            {
                line.SetStyle("width", thickness);
                line.SetStyle("background-color", color ?? PdfColor.Black);
            }
        }

        public void Row(Action<IRowDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var rowBox = box.AppendBlock();
            rowBox.SetStyle("display", "flex");
            rowBox.SetStyle("flex-direction", "row");
            handler(new RowDescriptorBuilder(rowBox));
        }

        public void Column(Action<IColumnDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var columnBox = box.AppendBlock();
            columnBox.SetStyle("display", "flex");
            columnBox.SetStyle("flex-direction", "column");
            handler(new ColumnDescriptorBuilder(columnBox));
        }

        public void Table(Action<ITableDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var tableBox = box.AppendBlock();
            tableBox.SetStyle("display", "table");
            handler(new TableDescriptorBuilder(tableBox));
        }

        public void OrderedList(Action<IListDescriptor> handler, PdfListMarkerType markerType = PdfListMarkerType.Decimal)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            BuildList(handler, markerType);
        }

        public void UnorderedList(Action<IListDescriptor> handler, PdfListMarkerType markerType = PdfListMarkerType.Disc)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            BuildList(handler, markerType);
        }

        private void BuildList(Action<IListDescriptor> handler, PdfListMarkerType markerType)
        {
            var listBox = box.AppendBlock();
            listBox.SetStyle("display", "block");
            listBox.SetStyle("list-style-type", markerType.ToKeyword());

            // list-style-position defaults to "outside" (CSS Lists Level 3's own initial value), which
            // needs real room to its left to draw into - real HTML gets this for free from the UA
            // stylesheet's own ul/ol { padding-left: 40px }; a hand-built declarative tree has no UA
            // stylesheet to inherit that from, so an outside marker would otherwise compute a negative,
            // off-page X (CssBoxMarker.PerformLayoutImp: left = owner.ClientLeft - width - margin) and
            // never actually be visible. Matches the same 40px browsers have used for decades.
            listBox.SetStyle("padding-left", PdfLength.Pixels(40));

            handler(new ListDescriptorBuilder(listBox));
        }

        public IContainer BeginPageNumberOfSection(string sectionId)
        {
            ArgumentNullException.ThrowIfNull(sectionId);
            box.SectionBeginId = sectionId;
            return this;
        }

        public IContainer EndPageNumberOfSection(string sectionId)
        {
            ArgumentNullException.ThrowIfNull(sectionId);
            box.SectionEndId = sectionId;
            return this;
        }

        // ─── class / id / tag / named page ──────────────────────────────────────

        public IContainer Class(string className)
        {
            ArgumentNullException.ThrowIfNull(className);
            box.AddClass(className);
            return this;
        }

        public IContainer Id(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            box.SetAttribute("id", id);
            return this;
        }

        public IContainer Tag(string tagName)
        {
            ArgumentException.ThrowIfNullOrEmpty(tagName);
            box.SetTagName(tagName);
            return this;
        }

        public IContainer PageName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            box.SetStyle("page", name);
            return this;
        }
    }
}
