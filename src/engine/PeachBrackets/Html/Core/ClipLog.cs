using System.Collections.Generic;

namespace PeachBrackets
{
    /// <summary>A word that was drawn but not fully visible, with the size it was drawn at and how much of it showed.</summary>
    /// <param name="Text">the word's text</param>
    /// <param name="DrawnWidth">the width the word was laid out at, in layout units</param>
    /// <param name="VisibleWidth">how much of that width fell inside the clip, in layout units</param>
    /// <param name="DrawnHeight">the height the word was laid out at, in layout units</param>
    /// <param name="VisibleHeight">how much of that height fell inside the clip, in layout units</param>
    public readonly record struct ClippedTextEntry(
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
