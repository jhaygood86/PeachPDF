using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// The kind of PDF structure-tree participation a box resolves to for tagging purposes.
    /// </summary>
    internal enum StructureTagKind
    {
        /// <summary>Transparent pass-through - no struct element, no marked content.</summary>
        None,

        /// <summary>An artifact marked-content sequence ("/Artifact BMC ... EMC") - no struct element.</summary>
        Artifact,

        /// <summary>A struct element that only groups children - no MCID, no BDC/EMC of its own.</summary>
        Grouping,

        /// <summary>A struct element whose own paint calls are wrapped in a tagged MCID sequence.</summary>
        Content
    }

    /// <summary>
    /// The result of classifying a box for tagged-PDF purposes.
    /// </summary>
    internal readonly struct StructureTagClassification
    {
        public StructureTagKind Kind { get; }

        /// <summary>The canonical PDF structure type name (e.g. "H1", "P"), without the leading "/". Null for None/Artifact.</summary>
        public string? StructureType { get; }

        /// <summary>Alt text for illustration elements (e.g. an &lt;img&gt;'s alt attribute). Null otherwise.</summary>
        public string? AltText { get; }

        StructureTagClassification(StructureTagKind kind, string? structureType, string? altText)
        {
            Kind = kind;
            StructureType = structureType;
            AltText = altText;
        }

        public static readonly StructureTagClassification None = new(StructureTagKind.None, null, null);
        public static readonly StructureTagClassification Artifact = new(StructureTagKind.Artifact, null, null);
        public static StructureTagClassification Grouping(string structureType) => new(StructureTagKind.Grouping, structureType, null);
        public static StructureTagClassification Content(string structureType, string? altText = null) => new(StructureTagKind.Content, structureType, altText);
    }

    /// <summary>
    /// Classifies a <see cref="CssBox"/> for tagged-PDF output. The common-case mapping (HTML tag
    /// to PDF structure type) is entirely CSS-driven via the <c>-peachpdf-pdf-tag-type</c> property
    /// (see <see cref="CssDefaults.DefaultStyleSheet"/> for the default per-tag rules) - this class
    /// only implements the narrower fallback for cases the CSS property genuinely cannot express:
    /// anonymous boxes (no source element for any selector to match), and the handful of structure
    /// types deliberately excluded from the property's value set (table substructure on anonymous
    /// boxes, and &lt;a&gt;/Link — see docs/html-css-support.md for why).
    /// </summary>
    internal static class StructureTagMapper
    {
        public static StructureTagClassification Classify(CssBox box)
        {
            // CssProxyBox (repeated table headers/footers across pages) delegates its own paint
            // entirely to a separate _sourceBox instance via a full Paint() call (not just
            // PaintImpCore) - that source box goes through this same classification independently
            // and correctly reuses one struct element across every page it's proxied onto (see
            // StructureTagBuilder's /MCR handling for content spanning pages). The proxy itself
            // copies the source's PdfTagType too (CssBoxProperties.InheritStyle's "everything"
            // path), so without this check it would resolve to the exact same structure type and
            // double-wrap the source's own struct element in a redundant duplicate.
            if (box is CssProxyBox)
                return StructureTagClassification.None;

            var pdfTagType = box.PdfTagType;

            if (string.IsNullOrEmpty(pdfTagType) || string.Equals(pdfTagType, CssConstants.Auto, System.StringComparison.OrdinalIgnoreCase))
                return ClassifyAuto(box);

            if (string.Equals(pdfTagType, CssConstants.None, System.StringComparison.OrdinalIgnoreCase))
                return StructureTagClassification.None;

            // "auto" and "none" are both handled above via exact-string checks, and both are the
            // only keywords in Map.PdfTagTypes mapping to PdfTagType.Auto/None respectively - so a
            // successful TryGetValue here can never yield either value. Only a genuinely
            // invalid/unrecognized value (which should already have been rejected at CSS parse
            // time, since the property's Converter only accepts the fixed keyword set) reaches the
            // fallback below, defensively, rather than emitting a bogus /S name.
            if (!Map.PdfTagTypes.TryGetValue(pdfTagType, out var tagType))
                return ClassifyAuto(box);

            if (tagType == PdfTagType.Artifact)
                return StructureTagClassification.Artifact;

            var structureType = CanonicalName(tagType);
            return HasOwnPaintedContent(box)
                ? StructureTagClassification.Content(structureType, box.HtmlTag?.TryGetAttribute("alt", null))
                : StructureTagClassification.Grouping(structureType);
        }

        static StructureTagClassification ClassifyAuto(CssBox box)
        {
            if (box.HtmlTag == null)
                return ClassifyAnonymous(box);

            if (box.IsClickable)
                return HasOwnPaintedContent(box)
                    ? StructureTagClassification.Content("Link")
                    : StructureTagClassification.Grouping("Link");

            var structureType = box.IsInline ? "Span" : "Div";
            return HasOwnPaintedContent(box)
                ? StructureTagClassification.Content(structureType)
                : StructureTagClassification.Grouping(structureType);
        }

        static StructureTagClassification ClassifyAnonymous(CssBox box)
        {
            var tableStructureType = box.Display switch
            {
                CssConstants.TableRow => "TR",
                // Anonymous table cells can't be distinguished into header (/TH) vs data (/TD) -
                // there is no source <th>/<td> tag to read; /TD is the safe default.
                CssConstants.TableCell => "TD",
                CssConstants.TableHeaderGroup => "THead",
                CssConstants.TableRowGroup => "TBody",
                CssConstants.TableFooterGroup => "TFoot",
                _ => null
            };

            if (tableStructureType != null)
                return HasOwnPaintedContent(box)
                    ? StructureTagClassification.Content(tableStructureType)
                    : StructureTagClassification.Grouping(tableStructureType);

            // A pure-grouping anonymous box (no own Words) is fully transparent - its children
            // attach directly to the nearest real ancestor struct element.
            return HasOwnPaintedContent(box)
                ? StructureTagClassification.Content("Span")
                : StructureTagClassification.None;
        }

        /// <summary>
        /// Whether this box's own PaintImpCore call is expected to draw something directly (text
        /// words, or an always-content replaced element), as opposed to purely recursing into
        /// children. Only content-producing boxes get a marked-content MCID of their own.
        /// </summary>
        static bool HasOwnPaintedContent(CssBox box)
        {
            return box.Words.Count > 0 || box is CssBoxImage or CssBoxSvg;
        }

        /// <summary>
        /// Maps a <see cref="PeachPDF.CSS.PdfTagType"/> value to its canonical, case-sensitive PDF
        /// standard structure type name (without the leading "/") - the stored
        /// <c>CssBox.PdfTagType</c> string is always lowercased by the CSS tokenizer (see
        /// PdfTagTypeIntegrationTests), so the enum round-trip is what recovers the spec-correct
        /// casing a reader expects (e.g. "/BlockQuote", not "/blockquote").
        /// </summary>
        static string CanonicalName(PdfTagType tagType) => tagType switch
        {
            PdfTagType.Part => "Part",
            PdfTagType.Art => "Art",
            PdfTagType.Sect => "Sect",
            PdfTagType.Div => "Div",
            PdfTagType.Index => "Index",
            PdfTagType.BlockQuote => "BlockQuote",
            PdfTagType.Caption => "Caption",
            PdfTagType.Toc => "TOC",
            PdfTagType.Toci => "TOCI",
            PdfTagType.P => "P",
            PdfTagType.H1 => "H1",
            PdfTagType.H2 => "H2",
            PdfTagType.H3 => "H3",
            PdfTagType.H4 => "H4",
            PdfTagType.H5 => "H5",
            PdfTagType.H6 => "H6",
            PdfTagType.L => "L",
            PdfTagType.Li => "LI",
            PdfTagType.Lbl => "Lbl",
            PdfTagType.LBody => "LBody",
            PdfTagType.Dl => "DL",
            PdfTagType.DlDiv => "DL-Div",
            PdfTagType.Dt => "DT",
            PdfTagType.Dd => "DD",
            PdfTagType.Span => "Span",
            PdfTagType.Quote => "Quote",
            PdfTagType.Table => "Table",
            PdfTagType.Tr => "TR",
            PdfTagType.Th => "TH",
            PdfTagType.Td => "TD",
            PdfTagType.THead => "THead",
            PdfTagType.TBody => "TBody",
            PdfTagType.TFoot => "TFoot",
            PdfTagType.BibEntry => "BibEntry",
            PdfTagType.Code => "Code",
            PdfTagType.Figure => "Figure",
            PdfTagType.Formula => "Formula",
            PdfTagType.Note => "Note",
            PdfTagType.Reference => "Reference",
            PdfTagType.Link => "Link",
            _ => "Div"
        };
    }
}
