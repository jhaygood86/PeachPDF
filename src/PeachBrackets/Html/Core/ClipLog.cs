using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    /// <summary>A word that was drawn but not fully visible, with the size it was drawn at and how much of it showed.</summary>
    internal readonly record struct ClippedTextEntry(
        string Text,
        double DrawnWidth,
        double VisibleWidth,
        double DrawnHeight,
        double VisibleHeight);

    /// <summary>The words a layout drew that an ancestor's clip cut off, in paint order.</summary>
    internal sealed class ClipLog
    {
        public List<ClippedTextEntry> Words { get; } = [];
    }
}
