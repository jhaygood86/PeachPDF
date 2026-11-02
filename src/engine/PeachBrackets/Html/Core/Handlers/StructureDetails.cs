using System.Collections.Generic;

namespace PeachBrackets.Tagging
{
    /// <summary>
    /// What a structure element carries besides its type, its content and its alternative text, for a sink that records structure
    /// (<see cref="IStructureTagSink"/>): the language of its content when it differs from the document's, and, for a table cell, what a
    /// reader needs to place it.
    /// </summary>
    public sealed class StructureDetails
    {
        /// <summary>The language of the element's content as a BCP 47 tag, when the element sets one of its own (<c>lang</c>); otherwise <see langword="null"/>.</summary>
        public string? Language { get; init; }

        /// <summary>
        /// An alternative description for an element that groups other content, such as a figure around a picture and its caption; a
        /// picture's own description is passed with the picture. <see langword="null"/> when there is none.
        /// </summary>
        public string? AlternativeText { get; init; }

        /// <summary>
        /// For a list, how its items are marked, as the name tagged-document formats use (<c>Disc</c>, <c>Circle</c>, <c>Square</c>,
        /// <c>Decimal</c>, <c>LowerRoman</c>, <c>UpperRoman</c>, <c>LowerAlpha</c>, <c>UpperAlpha</c>, <c>Ordered</c>, <c>Unordered</c> or
        /// <c>None</c>); otherwise <see langword="null"/>.
        /// </summary>
        public string? ListNumbering { get; init; }

        /// <summary>
        /// A name for the element that is unique in the document, so that another element can refer to it - given to a table header cell, which the
        /// <see cref="HeaderIds"/> of a data cell name. Null for other elements.
        /// </summary>
        public string? ElementId { get; init; }

        /// <summary>
        /// For a table cell with a <c>headers</c> attribute, the <see cref="ElementId"/> of each header cell that heads it; empty when the cell does
        /// not say, and its headers are taken from the table's layout and the cells' <see cref="TableScope"/>.
        /// </summary>
        public IReadOnlyList<string> HeaderIds { get; init; } = [];

        /// <summary>For a table header cell, what it heads: <c>Row</c> or <c>Column</c>; otherwise <see langword="null"/>.</summary>
        public string? TableScope { get; init; }

        /// <summary>For a table cell, how many columns it covers (<c>colspan</c>); 1 otherwise.</summary>
        public int ColumnSpan { get; init; } = 1;

        /// <summary>For a table cell, how many rows it covers (<c>rowspan</c>); 1 otherwise.</summary>
        public int RowSpan { get; init; } = 1;
    }
}
