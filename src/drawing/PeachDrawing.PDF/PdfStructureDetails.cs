#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.PDF
{
    /// <summary>One value of a structure attribute (ISO 32000-1 14.8.5): a name, text, a number, a flag, or a list of those.</summary>
    public sealed class PdfStructureAttributeValue
    {
        private PdfStructureAttributeValue(string kind, object? value, IReadOnlyList<PdfStructureAttributeValue>? items)
        {
            Kind = kind;
            Value = value;
            Items = items;
        }

        internal string Kind { get; }

        internal object? Value { get; }

        internal IReadOnlyList<PdfStructureAttributeValue>? Items { get; }

        /// <summary>A name, such as <c>Column</c> for a table header's scope.</summary>
        /// <param name="name">the name, without the slash</param>
        /// <returns>the value</returns>
        public static PdfStructureAttributeValue FromName(string name) => new("name", name ?? throw new ArgumentNullException(nameof(name)), null);

        /// <summary>A text string.</summary>
        /// <param name="text">the text</param>
        /// <returns>the value</returns>
        public static PdfStructureAttributeValue FromText(string text) => new("text", text ?? throw new ArgumentNullException(nameof(text)), null);

        /// <summary>A number.</summary>
        /// <param name="number">the number</param>
        /// <returns>the value</returns>
        public static PdfStructureAttributeValue FromNumber(double number) => new("number", number, null);

        /// <summary>A flag.</summary>
        /// <param name="flag">the flag</param>
        /// <returns>the value</returns>
        public static PdfStructureAttributeValue FromBoolean(bool flag) => new("boolean", flag, null);

        /// <summary>A list of values.</summary>
        /// <param name="items">the values, in order</param>
        /// <returns>the value</returns>
        public static PdfStructureAttributeValue FromList(IEnumerable<PdfStructureAttributeValue> items) =>
            new("list", null, (items ?? throw new ArgumentNullException(nameof(items))).ToList());
    }

    /// <summary>A set of attributes of one owner (<c>Table</c>, <c>List</c>, <c>Layout</c>...) on a structure element.</summary>
    /// <param name="Owner">the attribute owner, without the slash</param>
    /// <param name="Values">the attributes by name, without the slash</param>
    public sealed record PdfStructureAttributes(string Owner, IReadOnlyDictionary<string, PdfStructureAttributeValue> Values);

    /// <summary>What a structure element can carry besides its type and content: its language, its actual text, a title and attributes.</summary>
    public sealed class PdfStructureDetails
    {
        /// <summary>The language of the element's content (a language tag such as <c>fr-CA</c>), or <see langword="null"/> for the document's.</summary>
        public string? Language { get; init; }

        /// <summary>The text the element's content stands for, when it is not what is drawn (a ligature or an image of text).</summary>
        public string? ActualText { get; init; }

        /// <summary>A title for the element.</summary>
        public string? Title { get; init; }

        /// <summary>The element's attributes, such as a table header's scope and a cell's spans.</summary>
        public IReadOnlyList<PdfStructureAttributes> Attributes { get; init; } = [];
    }
}
