#nullable enable

using System;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// One entry of a document outline (the bookmarks sidebar of a reader), added with <see cref="PdfComposer.AddBookmarks"/>.
    /// </summary>
    public sealed class PdfBookmark
    {
        /// <summary>Creates an entry.</summary>
        /// <param name="level">the nesting level, 1 or more; an entry nests under the nearest preceding entry of a shallower level</param>
        /// <param name="title">the label to show</param>
        /// <param name="isOpen">whether the entry's children start expanded</param>
        public PdfBookmark(int level, string title, bool isOpen)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
            ArgumentNullException.ThrowIfNull(title);

            Level = level;
            Title = title;
            IsOpen = isOpen;
        }

        /// <summary>The nesting level, 1 or more.</summary>
        public int Level { get; }

        /// <summary>The label to show.</summary>
        public string Title { get; }

        /// <summary>Whether the entry's children start expanded.</summary>
        public bool IsOpen { get; }

        /// <summary>
        /// The address the entry opens, or <see langword="null"/>. Takes the place of <see cref="PageIndex"/> when set.
        /// </summary>
        public string? Uri { get; set; }

        /// <summary>
        /// The zero-based index of the page the entry leads to, or <see langword="null"/> for an entry with no destination (it is still
        /// kept, so its siblings and children nest as they would have).
        /// </summary>
        public int? PageIndex { get; set; }

        /// <summary>How far below the top edge of <see cref="PageIndex"/>'s page the destination is, in points.</summary>
        public double TopPoints { get; set; }
    }
}
