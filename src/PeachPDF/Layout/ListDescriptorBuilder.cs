using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;
using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one <see cref="IContainer.OrderedList"/>/<see cref="IContainer.UnorderedList"/> call's
    /// items, each a new <c>display: list-item</c> child of <paramref name="listBox"/>. A real HTML
    /// <c>::marker</c> child is normally synthesized by <c>DomParser.EnsureListItemMarkers</c> during
    /// HTML-tree correction, which a hand-built declarative tree never runs - so each item constructs its
    /// own <see cref="CssBoxMarker"/> directly here and resolves its default content immediately
    /// (<see cref="CssBoxMarker.ResolveDefaultContent"/>), mirroring exactly what that pass does for a
    /// real <c>&lt;li&gt;</c>. <see cref="CssCounterEngine.GetCounter"/> (which the marker's own default
    /// content resolution calls into for a counted style) is a purely structural, on-demand walk of
    /// parent/previous-sibling relationships - it needs no DOM-parsing prerequisite pass, so it already
    /// works correctly against this box tree as items are appended in order.
    /// </summary>
    internal sealed class ListDescriptorBuilder(CssBox listBox, CssPropertyFactory properties) : IListDescriptor
    {
        public IContainer Item()
        {
            var item = CssPropertyFactory.CreateAnonymousBox(listBox);
            properties.Set(item, "display", "list-item");

            var marker = new CssBoxMarker(item);
            item.Boxes.Remove(marker);
            item.Boxes.Insert(0, marker);
            marker.ResolveDefaultContent();

            return new ContainerBuilder(item, properties);
        }

        public IListDescriptor Position(PdfListMarkerPosition position)
        {
            properties.Set(listBox, "list-style-position", position == PdfListMarkerPosition.Inside ? "inside" : "outside");
            return this;
        }

        public IListDescriptor MarkerImage(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            properties.Set(listBox, "list-style-image", $"url(\"{DataUri.FromBytes(data)}\")");
            return this;
        }

        public IListDescriptor MarkerImage(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return MarkerImage(ms.ToArray());
        }

        public IListDescriptor MarkerImage(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            properties.Set(listBox, "list-style-image", $"url(\"{uri}\")");
            return this;
        }

        public IListDescriptor MarkerImage(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            properties.Set(listBox, "list-style-image", $"url(\"{filePath}\")");
            return this;
        }

        public IListDescriptor MarkerText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            properties.Set(listBox, "list-style-type", "\"" + EscapeCssStringLiteral(text) + "\"");
            return this;
        }

        private static string EscapeCssStringLiteral(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
