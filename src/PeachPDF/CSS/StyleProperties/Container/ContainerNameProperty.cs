#nullable disable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>container-name: none | &lt;custom-ident&gt;+</c> (CSS Containment 3 §2) - a space-separated
    /// list of bare identifiers, NOT a quoted CSS &lt;string&gt;. <see cref="Converters.LiteralsConverter"/>
    /// (the same converter <c>font-family</c>'s unquoted form uses to accept consecutive Ident tokens
    /// joined by whitespace) already accepts exactly this shape, including the single-token "none" case
    /// - but its shared grammar doesn't know <c>none</c> is only valid alone here (unlike font-family's
    /// own identifier list, which has no such restriction), so that's enforced with a thin wrapper
    /// instead of forking a second copy of the identifier-list walk.
    /// </summary>
    internal sealed class ContainerNameProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            new IdentifierValueConverter(ToContainerNameList).OrDefault();

        internal ContainerNameProperty()
            : base(PropertyNames.ContainerName)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private static string ToContainerNameList(IEnumerable<Token> value)
        {
            var literals = ValueExtensions.ToLiterals(value);
            if (literals is null) return null;

            var parts = literals.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && Array.IndexOf(parts, Keywords.None) >= 0 ? null : literals;
        }
    }
}