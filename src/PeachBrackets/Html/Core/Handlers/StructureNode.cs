using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Identifies one element of the document to a structure (tagged-document) recorder, without exposing anything about it.
    /// </summary>
    /// <remarks>
    /// A recorder only ever needs to know that two calls are about the same element - the second time content of a box is painted (on
    /// another page, say) it belongs under the structure element the first time made - so a node is a reference identity and nothing more.
    /// What the element is (its structure type, alt text) is decided by the document engine and passed along with each call.
    /// </remarks>
    internal readonly struct StructureNode : IEquatable<StructureNode>
    {
        private readonly CssBox _box;

        /// <summary>Wraps the document element a box was generated for.</summary>
        internal StructureNode(CssBox box) => _box = box;

        /// <summary>Wraps a box as the node that identifies it.</summary>
        public static implicit operator StructureNode(CssBox box) => new(box);

        /// <summary>Whether <paramref name="other"/> identifies the same element.</summary>
        public bool Equals(StructureNode other) => ReferenceEquals(_box, other._box);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is StructureNode other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(_box);
    }

    /// <summary>
    /// An element that generated no box of its own (<c>display: contents</c>) but still has a structure element, around the content that
    /// was lifted out of it.
    /// </summary>
    /// <param name="Node">the element</param>
    /// <param name="StructureType">the kind of structure element it maps to (a heading, a list, a section)</param>
    internal readonly record struct PhantomAncestor(StructureNode Node, string StructureType);
}
