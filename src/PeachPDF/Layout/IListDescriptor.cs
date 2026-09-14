using System;
using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds an ordered/unordered list's items (<see cref="IContainer.OrderedList"/>/
    /// <see cref="IContainer.UnorderedList"/>) - backed by PeachPDF's existing <c>display: list-item</c>/
    /// <c>list-style-*</c> support, not a from-scratch marker implementation. Call
    /// <see cref="Position"/>/<see cref="MarkerImage(byte[])"/>/<see cref="MarkerText"/> before adding any
    /// items - like any other CSS inherited property, a marker option only affects items added after it.
    /// </summary>
    public interface IListDescriptor
    {
        /// <summary>Appends one list item and returns its content container.</summary>
        IContainer Item();

        /// <summary>Sets whether the marker sits inside or outside each item's own content box.</summary>
        IListDescriptor Position(PdfListMarkerPosition position);

        /// <summary>Uses an image (from raw bytes) as every item's marker instead of the list's own marker type.</summary>
        IListDescriptor MarkerImage(byte[] data);

        /// <summary>Uses an image (read fully from a stream) as every item's marker.</summary>
        IListDescriptor MarkerImage(Stream stream);

        /// <summary>Uses an image loaded over the network as every item's marker.</summary>
        IListDescriptor MarkerImage(Uri uri);

        /// <summary>Uses an image loaded from a local file path as every item's marker.</summary>
        IListDescriptor MarkerImage(string filePath);

        /// <summary>Uses a literal custom marker string (e.g. <c>"-&gt; "</c>) for every item, instead of a counted/bulleted style.</summary>
        IListDescriptor MarkerText(string text);
    }
}
