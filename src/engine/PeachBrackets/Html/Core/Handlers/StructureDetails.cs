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

        /// <summary>For a table header cell, what it heads: <c>Row</c> or <c>Column</c>; otherwise <see langword="null"/>.</summary>
        public string? TableScope { get; init; }

        /// <summary>For a table cell, how many columns it covers (<c>colspan</c>); 1 otherwise.</summary>
        public int ColumnSpan { get; init; } = 1;

        /// <summary>For a table cell, how many rows it covers (<c>rowspan</c>); 1 otherwise.</summary>
        public int RowSpan { get; init; } = 1;
    }
}
