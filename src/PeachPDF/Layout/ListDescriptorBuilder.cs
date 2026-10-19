using PeachBrackets.Building;
using System;
using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one <see cref="IContainer.OrderedList"/>/<see cref="IContainer.UnorderedList"/> call's
    /// items, each a new <c>display: list-item</c> child of <paramref name="listBox"/>. A real HTML
    /// <c>::marker</c> child is normally synthesized by <c>DomParser.EnsureListItemMarkers</c> during
    /// HTML-tree correction, which a hand-built declarative tree never runs - so each item constructs its
    /// own <c>CssBoxMarker</c> directly here and resolves its default content immediately
    /// (<c>CssBoxMarker.ResolveDefaultContent</c>), mirroring exactly what that pass does for a
    /// real <c>&lt;li&gt;</c>. <c>CssCounterEngine.GetCounter</c> (which the marker's own default
    /// content resolution calls into for a counted style) is a purely structural, on-demand walk of
    /// parent/previous-sibling relationships - it needs no DOM-parsing prerequisite pass, so it already
    /// works correctly against this box tree as items are appended in order.
    /// </summary>
    internal sealed class ListDescriptorBuilder(DocumentNode listBox) : IListDescriptor
    {
        public IContainer Item()
        {
            return new ContainerBuilder(listBox.AppendListItem());
        }

        public IListDescriptor Position(PdfListMarkerPosition position)
        {
            listBox.SetStyle("list-style-position", position == PdfListMarkerPosition.Inside ? "inside" : "outside");
            return this;
        }

        public IListDescriptor MarkerImage(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            listBox.SetStyle("list-style-image", $"url(\"{DataUri.FromBytes(data)}\")");
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
            listBox.SetStyle("list-style-image", $"url(\"{uri}\")");
            return this;
        }

        public IListDescriptor MarkerImage(string filePath)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            listBox.SetStyle("list-style-image", $"url(\"{filePath}\")");
            return this;
        }

        public IListDescriptor MarkerText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            listBox.SetStyle("list-style-type", "\"" + EscapeCssStringLiteral(text) + "\"");
            return this;
        }

        private static string EscapeCssStringLiteral(string text) =>
            text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
