using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
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
    /// properties directly on the wrapped box and returns <c>this</c> - a single <see cref="CssBox"/>
    /// already natively supports holding all of these at once, so there's no need for a new wrapper box
    /// per decorator the way a from-scratch layout model might need. <see cref="Hyperlink(string)"/> is
    /// the one exception: an <c>&lt;a href&gt;</c> box is a real structural requirement
    /// (<see cref="CssBox.IsClickable"/> has no other trigger), so it creates a genuine new child and
    /// returns a builder for that instead. Every terminal method (<see cref="Text(string)"/>,
    /// <see cref="Image(byte[])"/>, <see cref="Row"/>, <see cref="Column"/>, etc.) places its content as
    /// a new child of the wrapped box, including plain text - a box must never hold both its own words
    /// and child boxes at once (real HTML never does either: a text node is always its own anonymous
    /// child box), and the wrapped box can already have one it never advertises - a list item's
    /// synthesized <c>::marker</c> (<see cref="ListDescriptorBuilder.Item"/>), most notably - so this
    /// applies unconditionally rather than only when a caller's own content happens to need it. May run
    /// at most once.
    /// </summary>
    internal sealed class ContainerBuilder(CssBox box, CssPropertyFactory properties) : IContainer
    {
        private bool _terminalUsed;

        internal CssBox Box => box;

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
            properties.Set(box, "padding-top", value);
            return this;
        }

        public IContainer PaddingBottom(PdfLength value)
        {
            properties.Set(box, "padding-bottom", value);
            return this;
        }

        public IContainer PaddingLeft(PdfLength value)
        {
            properties.Set(box, "padding-left", value);
            return this;
        }

        public IContainer PaddingRight(PdfLength value)
        {
            properties.Set(box, "padding-right", value);
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
            properties.Set(box, $"border-{side}-width", width);
            properties.Set(box, $"border-{side}-style", "solid");
            properties.Set(box, $"border-{side}-color", color ?? PdfColor.Black);
            return this;
        }

        public IContainer BorderColor(PdfColor color)
        {
            properties.Set(box, "border-top-color", color);
            properties.Set(box, "border-right-color", color);
            properties.Set(box, "border-bottom-color", color);
            properties.Set(box, "border-left-color", color);
            return this;
        }

        // ─── background / corner radius ─────────────────────────────────────────

        public IContainer Background(PdfColor color)
        {
            properties.Set(box, "background-color", color);
            return this;
        }

        public IContainer BackgroundLinearGradient(double angleDegrees, params PdfColor[] stops)
        {
            var stopsText = string.Join(", ", stops.Select(s => s.ToString()));
            properties.Set(box, "background-image",
                string.Create(CultureInfo.InvariantCulture, $"linear-gradient({angleDegrees}deg, {stopsText})"));
            return this;
        }

        public IContainer BorderLinearGradient(PdfLength width, double angleDegrees, params PdfColor[] stops)
        {
            // A border-image needs an actual border box to paint into - the color is irrelevant (the
            // gradient image paints over it) but the width determines how thick each painted edge is.
            Border(width, PdfColor.Transparent);

            var stopsText = string.Join(", ", stops.Select(s => s.ToString()));
            properties.Set(box, "border-image-source",
                string.Create(CultureInfo.InvariantCulture, $"linear-gradient({angleDegrees}deg, {stopsText})"));

            // The standard "gradient border" recipe: a 1-unit slice with stretch repeat treats each edge
            // as one continuous stretched strip of the (infinitely-scalable) gradient, rather than tiling
            // or rounding it - PR3's border-image painting already implements gradient/url() sources with
            // stretch repeat.
            properties.Set(box, "border-image-slice", "1");
            properties.Set(box, "border-image-repeat", "stretch");
            return this;
        }

        public IContainer Shadow(params PdfBoxShadow[] shadows)
        {
            properties.Set(box, "box-shadow", string.Join(", ", shadows.Select(s => s.ToCssText())));
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
            properties.Set(box, "border-top-left-radius", radius);
            return this;
        }

        public IContainer CornerRadiusTopRight(PdfLength radius)
        {
            properties.Set(box, "border-top-right-radius", radius);
            return this;
        }

        public IContainer CornerRadiusBottomLeft(PdfLength radius)
        {
            properties.Set(box, "border-bottom-left-radius", radius);
            return this;
        }

        public IContainer CornerRadiusBottomRight(PdfLength radius)
        {
            properties.Set(box, "border-bottom-right-radius", radius);
            return this;
        }

        // ─── sizing / alignment ─────────────────────────────────────────────────

        public IContainer Width(PdfLength value)
        {
            properties.Set(box, "width", value);
            return this;
        }

        public IContainer Height(PdfLength value)
        {
            properties.Set(box, "height", value);
            return this;
        }

        public IContainer Grow(double ratio = 1)
        {
            properties.Set(box, "flex-grow", ratio.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>
        /// Margins do not apply to a table cell (CSS 2.1 §17.5), so the auto-margin positioning the three
        /// Align methods use everywhere else would silently do nothing on one. What "align this container's own
        /// content" can mean for a cell is aligning its inline content - which is what a table author asks
        /// for when they right-align a column of amounts - so a cell gets <c>text-align</c> instead.
        /// </summary>
        private bool IsTableCell => box.Display.Value == PeachPDF.CSS.DisplayMode.TableCell;

        public IContainer AlignLeft()
        {
            if (IsTableCell)
            {
                properties.SetTextAlign(box, "left");
                return this;
            }

            properties.Set(box, "margin-left", "0");
            properties.Set(box, "margin-right", "auto");
            return this;
        }

        public IContainer AlignCenter()
        {
            if (IsTableCell)
            {
                properties.SetTextAlign(box, "center");
                return this;
            }

            properties.Set(box, "margin-left", "auto");
            properties.Set(box, "margin-right", "auto");
            return this;
        }

        public IContainer AlignRight()
        {
            if (IsTableCell)
            {
                properties.SetTextAlign(box, "right");
                return this;
            }

            properties.Set(box, "margin-left", "auto");
            properties.Set(box, "margin-right", "0");
            return this;
        }

        // ─── text style / hyperlink / bookmark ──────────────────────────────────

        public IContainer DefaultTextStyle(Action<ITextStyle> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            handler(new TextStyleApplier(box, properties));
            return this;
        }

        public IContainer ParagraphFirstLineIndentation(PdfLength value)
        {
            properties.Set(box, "text-indent", value);
            return this;
        }

        public IContainer ParagraphSpacing(PdfLength value)
        {
            properties.Set(box, "margin-bottom", value);
            return this;
        }

        public IContainer ClampLines(int lines, string? ellipsis = null)
        {
            properties.Set(box, "overflow", "hidden");
            properties.Set(box, "line-clamp", lines.ToString(CultureInfo.InvariantCulture));

            if (ellipsis is not null)
            {
                properties.Set(box, "block-ellipsis",
                    ellipsis.Length == 0 ? "none" : "\"" + EscapeCssStringLiteral(ellipsis) + "\"");
            }

            return this;
        }

        public IContainer Hyperlink(string url)
        {
            ArgumentNullException.ThrowIfNull(url);
            var link = CssBox.CreateBox(box, new HtmlTag("a", false, new Dictionary<string, string> { ["href"] = url }));
            return new ContainerBuilder(link, properties);
        }

        public IContainer Hyperlink(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);
            return Hyperlink(url.ToString());
        }

        public IContainer Bookmark(string title, int level = 1)
        {
            ArgumentNullException.ThrowIfNull(title);
            box.BookmarkLevel = level.ToString(CultureInfo.InvariantCulture);
            box.BookmarkLabel = "\"" + EscapeCssStringLiteral(title) + "\"";
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
            var textBox = CssPropertyFactory.CreateAnonymousBox(box);
            textBox.Text = text;
        }

        public void Text(Action<ITextSpanContainer> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            handler(new TextSpanContainerBuilder(box, properties));
        }

        public void Image(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            MarkTerminal();
            CreateImageBox().SetDecodedContent(DecodeRasterBytes(data), null);
        }

        public void Image(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            MarkTerminal();
            CreateImageBox().SetDecodedContent(DecodeRasterBytes(ms.ToArray()), null);
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
            var (decodedImage, svgDocument) = image.Resolve(properties.Adapter);
            CreateImageBox().SetDecodedContent(decodedImage, svgDocument);
        }

        public void Image(Func<PdfSize, byte[]> generator)
        {
            ArgumentNullException.ThrowIfNull(generator);
            MarkTerminal();
            var imageBox = CreateFillingImageBox();
            imageBox.SetDynamicRasterContent(generator);
        }

        public void Svg(string svgMarkup)
        {
            ArgumentNullException.ThrowIfNull(svgMarkup);
            MarkTerminal();
            CreateImageBox().SetDecodedContent(null, DecodeSvgMarkup(svgMarkup));
        }

        public void Svg(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            MarkTerminal();
            CreateImageBox().SetDecodedContent(null, DecodeSvgMarkup(ms.ToArray()));
        }

        public void Svg(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            MarkTerminal();
            CreateImageBox().SetDecodedContent(null, DecodeSvgMarkup(data));
        }

        public void Svg(Func<PdfSize, string> generator)
        {
            ArgumentNullException.ThrowIfNull(generator);
            MarkTerminal();
            var imageBox = CreateFillingImageBox();
            imageBox.SetDynamicSvgContent(generator);
        }

        /// <summary>
        /// Places a lazily-loaded <c>&lt;img src="..."&gt;</c> - the network/file/data-URI path
        /// <see cref="Image(Uri)"/>/<see cref="Image(string)"/> still use (an eager in-memory decode,
        /// as the other overloads use, doesn't apply when the source isn't in memory yet).
        /// </summary>
        private void PlaceImage(string src) =>
            CreateImageBox(new Dictionary<string, string> { ["src"] = src });

        /// <summary>
        /// Creates a real, correctly-typed <see cref="CssBoxImage"/> child of the wrapped box - the
        /// tag-dispatching <see cref="CssBox.CreateBox(HtmlTag, CssBox?)"/> overload (not the parent-first
        /// one every other terminal here uses, which always constructs a plain <see cref="CssBox"/>
        /// regardless of tag name - so the resulting box actually reaches <c>FragmentContentPainters.For</c>'s
        /// <c>CssBoxImage =&gt; ImagePainter</c> arm and gets the intrinsic-sizing/image-content handling
        /// only that concrete type implements. That overload doesn't call <see cref="CssBox.InheritStyle"/>
        /// itself (the normal HTML parse path defers that to its own later whole-tree cascade pass, which
        /// a declaratively-built tree never runs), so this does it explicitly, matching
        /// <see cref="CssPropertyFactory.CreateAnonymousBox"/>'s identical own call.
        /// </summary>
        private CssBoxImage CreateImageBox(Dictionary<string, string>? attributes = null)
        {
            var image = (CssBoxImage)CssBox.CreateBox(new HtmlTag("img", true, attributes), box);
            image.InheritStyle();
            return image;
        }

        /// <summary>
        /// A dynamic content box (<see cref="Image(Func{PdfSize,byte[]})"/>/<see cref="Svg(Func{PdfSize,string})"/>)
        /// fills its parent by default (<c>width:100%;height:100%</c>) - "available space" is whatever
        /// this container already resolves to, matching QuestPDF's own dynamic-image framing (its own doc
        /// example wraps the dynamic image in an explicitly-sized/`AspectRatio`d container, rather than
        /// sizing the image call itself) - see <see cref="IContainer.Image(Func{PdfSize,byte[]})"/>'s own
        /// doc comment for the full size-resolution rule this depends on.
        /// </summary>
        private CssBoxImage CreateFillingImageBox()
        {
            var imageBox = CreateImageBox();
            properties.Set(imageBox, "width", "100%");
            properties.Set(imageBox, "height", "100%");
            return imageBox;
        }

        private RImage DecodeRasterBytes(byte[] bytes) =>
            properties.Adapter.ImageFromStream(new MemoryStream(bytes));

        private SvgDocument DecodeSvgMarkup(string svgMarkup) =>
            SvgTreeBuilder.Build(new XElementSvgSourceNode(XElement.Parse(svgMarkup)), properties.Adapter);

        /// <summary>
        /// Decodes raw SVG markup bytes - <see cref="StreamReader"/> with BOM detection (not
        /// <see cref="Encoding.UTF8"/>.<see cref="Encoding.GetString(byte[])"/> directly), so a leading
        /// UTF-8 BOM - common in SVG files saved by many editors - is stripped rather than surviving into
        /// the decoded string as a literal U+FEFF character, which <see cref="XElement.Parse(string)"/>
        /// rejects outright ("Data at the root level is invalid"). Same fix as <see cref="PdfImage.Resolve"/>'s
        /// own BOM handling.
        /// </summary>
        private SvgDocument DecodeSvgMarkup(byte[] svgBytes)
        {
            using var reader = new StreamReader(new MemoryStream(svgBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return DecodeSvgMarkup(reader.ReadToEnd());
        }

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
            var wrapper = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(wrapper, "display", "block");

            var (fragmentRoot, fragmentShells) = BuildFragmentTree(html, stylesheet);

            // Must run before slot processing: a slot's replacement content is built via ordinary
            // ContainerBuilder calls afterward, and those boxes must NOT be flagged - only the fragment's
            // own real-cascade-styled boxes should be (see CssBox.IsFragmentStyled's own remarks).
            MarkFragmentStyled(fragmentRoot);

            ProcessSlots(fragmentRoot, onSlot);

            wrapper.SetAllBoxes(fragmentRoot);

            // A shell that was a top-level node of the fragment still names the discarded root as its parent.
            foreach (var shell in fragmentShells)
            {
                if (ReferenceEquals(shell.ParentBox, fragmentRoot)) shell.RetargetShellParent(wrapper);
            }
        }

        private (CssBox Root, List<CssBox> DisplayContentsShells) BuildFragmentTree(string html, PeachPdfCssContent? stylesheet)
        {
            var built = BuildFragmentTreeAsync(html, stylesheet).GetAwaiter().GetResult();
            properties.DisplayContentsShells.AddRange(built.DisplayContentsShells);
            return built;
        }

        private async Task<(CssBox Root, List<CssBox> DisplayContentsShells)> BuildFragmentTreeAsync(string html, PeachPdfCssContent? stylesheet)
        {
            var adapter = properties.Adapter;

            // Cloned so this fragment's own <style> tag collection (DomParser.GenerateFragmentCssTree's own
            // CascadeParseStyles call) never mutates a caller-shared PeachPdfCssContent instance, or the
            // adapter's cached UA-default CssData.
            var cssData = (stylesheet?.CssData ?? await adapter.GetDefaultCssData()).Clone();

            var cssParser = new CssParser(adapter, htmlContainer: null);
            var domParser = new DomParser(cssParser);
            return await domParser.GenerateFragmentCssTree(html, adapter, cssData);
        }

        private static void MarkFragmentStyled(CssBox box)
        {
            box.IsFragmentStyled = true;
            foreach (var child in box.Boxes)
                MarkFragmentStyled(child);
        }

        /// <summary>
        /// Finds every &lt;slot&gt; element in <paramref name="fragmentRoot"/> and, when <paramref name="onSlot"/>
        /// is non-null, invokes it once per slot (document order) with a new, empty <see cref="IContainer"/>
        /// inserted immediately before that slot's own box. If the callback places any content
        /// (<see cref="HasContent"/>), the original slot box (and its own fallback content) is discarded;
        /// otherwise the empty replacement is discarded instead, leaving the slot's fallback content exactly
        /// as authored - the same outcome as when <paramref name="onSlot"/> is null altogether.
        /// </summary>
        private void ProcessSlots(CssBox fragmentRoot, Action<SlotContext, IContainer>? onSlot)
        {
            var slotBoxes = new List<CssBox>();
            CollectSlotBoxes(fragmentRoot, slotBoxes);

            if (slotBoxes.Count == 0 || onSlot is null)
                return;

            foreach (var slotBox in slotBoxes)
            {
                // A <slot> nested inside an earlier sibling slot's own fallback content is still in this
                // list (collection is a single up-front pass, before any slot is filled) even after that
                // ancestor slot's own fill already detached the whole fallback subtree it lived in -
                // invoking its callback at that point would fire real side effects for content that can
                // never reach the final tree either way, so it's skipped instead.
                if (!IsAttached(slotBox, fragmentRoot))
                    continue;

                var attributes = slotBox.HtmlTag?.Attributes is { } tagAttributes
                    ? new Dictionary<string, string>(tagAttributes, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var name = slotBox.HtmlTag?.TryGetAttribute("name", "") ?? "";
                var slotContext = new SlotContext(name, attributes);

                var parent = slotBox.ParentBox!;
                var replacementWrapper = CssBox.CreateBox(parent, tag: null, before: slotBox);
                var replacementContainer = new ContainerBuilder(replacementWrapper, properties);

                onSlot(slotContext, replacementContainer);

                if (replacementContainer.HasContent)
                {
                    slotBox.ParentBox = null;
                }
                else
                {
                    replacementWrapper.ParentBox = null;
                }
            }
        }

        /// <summary>Whether <paramref name="box"/> can still reach <paramref name="root"/> by walking up its own <see cref="CssBox.ParentBox"/> chain - false once an ancestor has been detached (<see cref="ProcessSlots"/>'s own reachability guard).</summary>
        private static bool IsAttached(CssBox box, CssBox root)
        {
            for (var current = box; current is not null; current = current.ParentBox)
            {
                if (ReferenceEquals(current, root))
                    return true;
            }

            return false;
        }

        /// <summary>Snapshots every &lt;slot&gt; box before any mutation, so later reparenting in <see cref="ProcessSlots"/> can't disturb this walk.</summary>
        private static void CollectSlotBoxes(CssBox box, List<CssBox> result)
        {
            if (box.HtmlTag?.Name.Equals("slot", StringComparison.OrdinalIgnoreCase) == true)
                result.Add(box);

            foreach (var child in box.Boxes.ToArray())
                CollectSlotBoxes(child, result);
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
            var line = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(line, "display", "block");

            if (dashed)
            {
                // A dashed rule can't come from background-color (a solid fill has no dash pattern of
                // its own) - drawn instead as a single dashed top border edge on a zero-height box, the
                // same border-style machinery BordersDrawHandler already paints dotted/dashed borders
                // through (see SetBorderEdge for the solid-border equivalent of this cascade shape).
                properties.Set(line, "height", "0");
                properties.Set(line, "border-top-width", thickness);
                properties.Set(line, "border-top-style", "dashed");
                properties.Set(line, "border-top-color", color ?? PdfColor.Black);
            }
            else
            {
                properties.Set(line, "height", thickness);
                properties.Set(line, "background-color", color ?? PdfColor.Black);
            }
        }

        public void LineVertical(PdfLength thickness, PdfColor? color = null, bool dashed = false)
        {
            if (dashed && thickness.Unit == PdfLengthUnit.Percent)
                throw new ArgumentException("A dashed line's thickness cannot be a percentage - border width has no percentage form.", nameof(thickness));

            MarkTerminal();
            var line = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(line, "display", "block");

            if (dashed)
            {
                properties.Set(line, "width", "0");
                properties.Set(line, "border-left-width", thickness);
                properties.Set(line, "border-left-style", "dashed");
                properties.Set(line, "border-left-color", color ?? PdfColor.Black);
            }
            else
            {
                properties.Set(line, "width", thickness);
                properties.Set(line, "background-color", color ?? PdfColor.Black);
            }
        }

        public void Row(Action<IRowDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var rowBox = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(rowBox, "display", "flex");
            properties.Set(rowBox, "flex-direction", "row");
            handler(new RowDescriptorBuilder(rowBox, properties));
        }

        public void Column(Action<IColumnDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var columnBox = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(columnBox, "display", "flex");
            properties.Set(columnBox, "flex-direction", "column");
            handler(new ColumnDescriptorBuilder(columnBox, properties));
        }

        public void Table(Action<ITableDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            MarkTerminal();
            var tableBox = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(tableBox, "display", "table");
            handler(new TableDescriptorBuilder(tableBox, properties));
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
            var listBox = CssPropertyFactory.CreateAnonymousBox(box);
            properties.Set(listBox, "display", "block");
            properties.Set(listBox, "list-style-type", markerType.ToKeyword());

            // list-style-position defaults to "outside" (CSS Lists Level 3's own initial value), which
            // needs real room to its left to draw into - real HTML gets this for free from the UA
            // stylesheet's own ul/ol { padding-left: 40px }; a hand-built declarative tree has no UA
            // stylesheet to inherit that from, so an outside marker would otherwise compute a negative,
            // off-page X (CssBoxMarker.PerformLayoutImp: left = owner.ClientLeft - width - margin) and
            // never actually be visible. Matches the same 40px browsers have used for decades.
            properties.Set(listBox, "padding-left", PdfLength.Pixels(40));

            handler(new ListDescriptorBuilder(listBox, properties));
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
            box.EnsureHtmlTag();
            var existing = box.HtmlTag!.TryGetAttribute("class");
            box.HtmlTag.SetAttribute("class", string.IsNullOrEmpty(existing) ? className : existing + " " + className);
            return this;
        }

        public IContainer Id(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
            box.EnsureHtmlTag();
            box.HtmlTag!.SetAttribute("id", id);
            return this;
        }

        public IContainer Tag(string tagName)
        {
            ArgumentException.ThrowIfNullOrEmpty(tagName);
            box.EnsureHtmlTag();
            box.HtmlTag!.Name = tagName;
            return this;
        }

        public IContainer PageName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            properties.Set(box, "page", name);
            return this;
        }
    }
}
