using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachDrawing.Text.Internal.Text.Bidi;
using System;
using System.Collections.Frozen;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// The HTML Standard's first-strong-character algorithm (used to resolve "auto" directionality -
    /// <c>&lt;bdi&gt;</c>/<c>dir="auto"</c> on the HTML side via <see cref="Parse.DomParser"/>, and
    /// <see cref="Layout.PdfTextDirection.Auto"/> on the declarative-API side via
    /// <see cref="Layout.TextStyleApplier"/>) - extracted so both callers share one implementation rather
    /// than each re-deriving the scan/boundary rules independently.
    /// </summary>
    internal static class BidiDirectionalityResolver
    {
        private static readonly FrozenSet<string> OpaqueTags = new[]
        {
            HtmlConstants.Bdi, "script", HtmlConstants.Style, "textarea", "input", HtmlConstants.Iframe, HtmlConstants.NoScript
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Walks <paramref name="element"/>'s descendant text in tree order looking for the first
        /// character with a strong <see cref="BidiClass"/> (<c>L</c>, or <c>R</c>/<c>AL</c>), returning
        /// <see cref="Keywords.Ltr"/>/<see cref="Keywords.Rtl"/> respectively, or null if none is found
        /// (the element's directionality then defaults to ltr). Deliberately does not check
        /// <paramref name="element"/>'s own tag as a scan boundary (only <see cref="ScanForStrongDirection"/>
        /// does, for each of element's children) - the element passed here is the one actually being
        /// resolved (an HTML <c>dir="auto"</c> element still carries that very attribute at the moment
        /// this runs), so it must not short-circuit itself out of its own scan.
        /// </summary>
        internal static string? FindFirstStrongDirection(CssBox element)
        {
            foreach (var child in element.Boxes)
            {
                var found = ScanForStrongDirection(child);
                if (found is not null) return found;
            }

            return null;
        }

        /// <summary>
        /// Checks <paramref name="box"/>'s own tag (a scan boundary: skip a nested element that carries
        /// its own <c>dir</c> attribute - it is its own directionality scope - or a nested
        /// <c>&lt;bdi&gt;</c>/other opaque element, always its own isolated scope), then its own text,
        /// then recurses into its children. This IS a valid entry point in its own right (not just
        /// <see cref="FindFirstStrongDirection"/>'s recursive helper): a declarative <c>Span(text)</c>
        /// (<see cref="Layout.TextSpanContainerBuilder"/>) sets its text directly on the span's own box
        /// with no children at all, unlike an HTML text node (always its own separate child box) - a
        /// caller resolving that span's own directionality calls this directly on it, since
        /// <see cref="FindFirstStrongDirection"/> (which only looks at children) would find nothing for
        /// it. The span's own synthetic tag never carries a <c>dir</c> attribute, so this boundary check
        /// is a harmless no-op for that call, not a self-exclusion.
        /// </summary>
        internal static string? ScanForStrongDirection(CssBox box)
        {
            if (box.HtmlTag is { } tag)
            {
                if (tag.HasAttribute(HtmlConstants.Dir) || OpaqueTags.Contains(tag.Name))
                    return null;
            }

            if (box.Text is { Length: > 0 } text)
            {
                foreach (var rune in text.EnumerateRunes())
                {
                    var bidiClass = BidiClassTable.Of(rune);
                    if (bidiClass == BidiClass.L) return Keywords.Ltr;
                    if (bidiClass is BidiClass.R or BidiClass.AL) return Keywords.Rtl;
                }
            }

            foreach (var child in box.Boxes)
            {
                var found = ScanForStrongDirection(child);
                if (found is not null) return found;
            }

            return null;
        }
    }
}
