using System.Collections.Generic;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Identifies one <c>&lt;slot&gt;</c> element encountered while splicing an HTML fragment
    /// (<see cref="IContainer.Html(string, PeachPdfCssContent?, System.Action{SlotContext, IContainer}?)"/>)
    /// into a declarative container, passed to the caller's slot callback alongside an <see cref="IContainer"/>
    /// positioned to replace it.
    /// </summary>
    public sealed class SlotContext
    {
        internal SlotContext(string name, IReadOnlyDictionary<string, string> attributes)
        {
            Name = name;
            Attributes = attributes;
        }

        /// <summary>The slot's <c>name</c> attribute, or <see cref="string.Empty"/> if it has none (the default, unnamed slot).</summary>
        public string Name { get; }

        /// <summary>Every attribute declared on the <c>&lt;slot&gt;</c> element, including <c>name</c> itself.</summary>
        public IReadOnlyDictionary<string, string> Attributes { get; }
    }
}
