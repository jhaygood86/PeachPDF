using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// What the fragment painter needs from a structure (tagged-PDF) recorder while it walks the page: open the structure element a
    /// box maps to, around the marked content it paints. The painter knows nothing about how the structure is stored or written;
    /// <see cref="StructureTagBuilder"/> is the implementation that builds a PDF structure tree. Each <c>Open*</c> call returns a
    /// scope whose disposal closes what it opened.
    /// </summary>
    internal interface IStructureTagSink
    {
        /// <summary>Opens a grouping element for <paramref name="box"/> that nests everything painted until the scope is disposed.</summary>
        IDisposable OpenGroupingElement(CssBox box, string structureType);

        /// <summary>Re-opens the elements of the <c>display: contents</c> ancestors a box was lifted out of (outermost first).</summary>
        IDisposable OpenPhantomAncestors(IReadOnlyList<CssBox> ancestors);

        /// <summary>Opens the body element of a list item, inside its list item element.</summary>
        IDisposable OpenListItemBodyElement(CssBox box);

        /// <summary>Opens a content element for <paramref name="box"/> and the marked-content sequence <paramref name="g"/> paints it in.</summary>
        IDisposable OpenContentElement(Canvas g, CssBox box, string structureType, string? altText = null);

        /// <summary>Attaches the MathML source of a formula box to its structure element.</summary>
        void AttachMathMlSource(CssBox box, string mathMlSource);

        /// <summary>Whether content painted now already lands inside a marked-content sequence; marked content must not nest.</summary>
        bool IsInMarkedContent { get; }

        /// <summary>Opens an artifact sequence on <paramref name="g"/>: content that is not part of the document structure.</summary>
        IDisposable OpenArtifact(Canvas g);
    }
}
