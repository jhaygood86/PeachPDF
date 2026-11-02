using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>
    /// What a reader of a drawing - a person using assistive technology - is told about an area of it: what it is, what it is called and what state
    /// it is in. Handed to <see cref="Canvas.AddSemanticRegion"/>; a canvas for a format that can carry it (SVG) writes it, and the others ignore it.
    /// </summary>
    /// <param name="Role">the kind of thing it is, as a WAI-ARIA role name such as <c>heading</c>, <c>navigation</c> or <c>button</c></param>
    public sealed record SemanticInfo(string Role)
    {
        /// <summary>What the area is called, or null when it has no name.</summary>
        public string? Name { get; init; }

        /// <summary>A longer description of it, or null.</summary>
        public string? Description { get; init; }

        /// <summary>The language of its content as a BCP 47 tag when it differs from the document's, or null.</summary>
        public string? Language { get; init; }

        /// <summary>The level of a heading, or 0 for none.</summary>
        public int Level { get; init; }

        /// <summary>A name for the area that is unique in the document, so that another can refer to it, or null.</summary>
        public string? Id { get; init; }

        /// <summary>
        /// Its other WAI-ARIA states and properties by attribute name, such as <c>aria-checked</c> = <c>true</c> or <c>aria-expanded</c> =
        /// <c>false</c>; null when there are none.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Properties { get; init; }
    }
}
