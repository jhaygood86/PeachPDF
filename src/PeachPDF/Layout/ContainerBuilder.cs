using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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

        public IContainer AlignLeft()
        {
            properties.Set(box, "margin-left", "0");
            properties.Set(box, "margin-right", "auto");
            return this;
        }

        public IContainer AlignCenter()
        {
            properties.Set(box, "margin-left", "auto");
            properties.Set(box, "margin-right", "auto");
            return this;
        }

        public IContainer AlignRight()
        {
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
            PlaceImage(DataUri.FromBytes(data));
        }

        public void Image(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Image(ms.ToArray());
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
            PlaceImage(image.Source);
        }

        private void PlaceImage(string src)
        {
            var image = CssBox.CreateBox(box, new HtmlTag("img", true, new Dictionary<string, string> { ["src"] = src }));
            _ = image;
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
    }
}
