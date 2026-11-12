using System.Collections.Generic;
using System.Linq;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.Tagging;

namespace PeachBrackets.Html.Handlers
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
            // copies the source's PdfTagType too (CssBox.InheritStyle's "everything"
            // path), so without this check it would resolve to the exact same structure type and
            // double-wrap the source's own struct element in a redundant duplicate.
            //
            // A CssSpacingBox is the placeholder a rowspan cell leaves in each later row it spans: no element of the
            // document, only the cell's way into that row. The cell's own structure element is opened by the cell (and
            // parented under its own row, which reaches it first), so a placeholder that wraps a part of the cell, as a
            // decoration strip under a repeated group does, would otherwise open an empty /Div of its own.
            if (box is CssProxyBox or CssSpacingBox)
                return StructureTagClassification.None;

            var pdfTagType = box.PdfTagType;

            if (string.IsNullOrEmpty(pdfTagType) || string.Equals(pdfTagType, Keywords.Auto, System.StringComparison.OrdinalIgnoreCase))
                return ClassifyAuto(box);

            if (string.Equals(pdfTagType, Keywords.None, System.StringComparison.OrdinalIgnoreCase))
            {
                // A page that is only inline content (a label and a field, a line of text and a link) has nothing between the document and its
                // spans; PDF/UA-2 wants a paragraph there.
                return box.HtmlTag?.Name.Equals("body", System.StringComparison.OrdinalIgnoreCase) == true && HoldsOnlyInlineContent(box) && HasPlainInlineElement(box)
                    ? StructureTagClassification.Grouping("P")
                    : StructureTagClassification.None;
            }

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

            // An <img> with an empty or missing alternative is decorative (HTML: no text alternative, assistive technology skips it); a Figure
            // without /Alt fails PDF/UA, so it is an artifact instead.
            if (tagType == PdfTagType.Figure && box is CssBoxImage && string.IsNullOrEmpty(AltFor(box)))
                return StructureTagClassification.Artifact;

            var structureType = CanonicalName(tagType);

            // A division that holds nothing but inline content is a paragraph of text (see ClassifyAuto).
            if (structureType == "Div" && !box.IsInline && HoldsOnlyInlineContent(box))
                structureType = "P";

            return HasOwnPaintedContent(box)
                ? StructureTagClassification.Content(structureType, AltFor(box))
                : StructureTagClassification.Grouping(structureType);
        }

        /// <summary>
        /// The alternative text of a box that stands for a picture or a formula: its <c>alt</c> (empty marks it decorative), or else the name the
        /// accessibility tree gives it - an SVG's title or <c>aria-label</c>, a formula's <c>alttext</c> or spoken text, an image's label.
        /// </summary>
        static string? AltFor(CssBox box)
        {
            if (box.HtmlTag?.TryGetAttribute("alt", null) is { } alt)
                return alt;

            if (box is CssBoxMath or CssBoxSvg or CssBoxImage
                && box.HtmlContainer?.SemanticTree.NodeFor(box) is { Name: { Length: > 0 } name })
            {
                return name;
            }

            return null;
        }

        /// <summary>
        /// What else the element's structure element should carry: the language it sets of its own, and for a table cell what a header heads and how
        /// far a cell reaches. Null when there is nothing to add.
        /// </summary>
        public static StructureDetails? DetailsFor(CssBox box, StructureTagClassification classification)
        {
            if (box.HtmlTag is not { } tag)
                return null;

            var language = tag.TryGetAttribute("lang", null) is { } lang && !string.IsNullOrWhiteSpace(lang) ? lang.Trim() : null;
            string? scope = null;
            string? elementId = null;
            IReadOnlyList<string> headerIds = [];
            int columns = 1, rows = 1;
            if (classification.StructureType is "TH" or "TD" && box.HtmlContainer?.SemanticTree.NodeFor(box) is { } node)
            {
                if (classification.StructureType == "TH")
                {
                    scope = node.Role switch
                    {
                        PeachBrackets.Accessibility.AccessibilityRole.RowHeader => "Row",
                        PeachBrackets.Accessibility.AccessibilityRole.ColumnHeader => "Column",
                        _ => null,
                    };
                }

                columns = node.ColumnSpan;
                rows = node.RowSpan;

                // A header cell is named so the cells it heads can point at it; a cell that lists its headers says which they are.
                if (classification.StructureType == "TH")
                    elementId = node.Id;
                if (tag.HasAttribute("headers"))
                    headerIds = [.. node.ColumnHeaders.Concat(node.RowHeaders).Select(h => h.Id).Distinct()];
            }

            // A figure that groups its picture and caption is a Figure of its own, and PDF/UA wants every Figure described: the name the
            // accessibility tree gives it (from its caption or label).
            string? groupAlt = null;
            if (classification.Kind == StructureTagKind.Grouping && classification.StructureType == "Figure"
                && box.HtmlContainer?.SemanticTree.NodeFor(box) is { Name: { Length: > 0 } figureName })
            {
                groupAlt = figureName;
            }

            var listNumbering = classification.StructureType == "L" ? ListNumberingOf(box) : null;

            return language is null && scope is null && groupAlt is null && listNumbering is null && elementId is null && headerIds.Count == 0 && columns == 1 && rows == 1
                ? null
                : new StructureDetails
                {
                    Language = language, TableScope = scope, AlternativeText = groupAlt, ListNumbering = listNumbering, ElementId = elementId, HeaderIds = headerIds, ColumnSpan = columns, RowSpan = rows,
                };
        }

        /// <summary>How a list marks its items, as the tagged-document <c>ListNumbering</c> name (ISO 32000-1 14.8.5.4).</summary>
        static string ListNumberingOf(CssBox box) => box.ListStyleType?.ToLowerInvariant() switch
        {
            "disc" => "Disc",
            "circle" => "Circle",
            "square" => "Square",
            "decimal" or "decimal-leading-zero" => "Decimal",
            "lower-roman" => "LowerRoman",
            "upper-roman" => "UpperRoman",
            "lower-alpha" or "lower-latin" => "LowerAlpha",
            "upper-alpha" or "upper-latin" => "UpperAlpha",
            "none" => "None",
            _ => box.HtmlTag?.Name.Equals("ol", System.StringComparison.OrdinalIgnoreCase) == true ? "Ordered" : "Unordered",
        };

        static StructureTagClassification ClassifyAuto(CssBox box)
        {
            if (box.HtmlTag == null)
                return ClassifyAnonymous(box);

            if (box.IsClickable)
                return HasOwnPaintedContent(box)
                    ? StructureTagClassification.Content("Link")
                    : StructureTagClassification.Grouping("Link");

            // A block that holds nothing but inline content is a paragraph of text; PDF/UA wants text in a paragraph-level element, not
            // loose in a division.
            var structureType = box.IsInline ? "Span" : HoldsOnlyInlineContent(box) ? "P" : "Div";
            return HasOwnPaintedContent(box)
                ? StructureTagClassification.Content(structureType)
                : StructureTagClassification.Grouping(structureType);
        }

        static StructureTagClassification ClassifyAnonymous(CssBox box)
        {
            var tableStructureType = box.DerivedStyle.ActualDisplay switch
            {
                Keywords.TableRow => "TR",
                // Anonymous table cells can't be distinguished into header (/TH) vs data (/TD) -
                // there is no source <th>/<td> tag to read; /TD is the safe default.
                Keywords.TableCell => "TD",
                Keywords.TableHeaderGroup => "THead",
                Keywords.TableRowGroup => "TBody",
                Keywords.TableFooterGroup => "TFoot",
                _ => null
            };

            // A table part that is itself the child of a <tr>/<td>/... with display: contents got an
            // anonymous wrapper only because box generation "ignores the elided elements entirely"; the
            // document's own element - re-opened around the child by StructureTagBuilder.OpenPhantomAncestors -
            // is the real one, and would otherwise sit inside a second, synthesized one of the same type.
            if (tableStructureType != null && IsProvidedByDisplayContentsAncestors(box, tableStructureType))
                return StructureTagClassification.None;

            if (tableStructureType != null)
                return HasOwnPaintedContent(box)
                    ? StructureTagClassification.Content(tableStructureType)
                    : StructureTagClassification.Grouping(tableStructureType);

            // A pure-grouping anonymous box (no own Words) is fully transparent - its children
            // attach directly to the nearest real ancestor struct element.
            // An anonymous block (the text between two blocks in a block container) is a paragraph; an anonymous inline run is a span.
            if (HasOwnPaintedContent(box))
                return StructureTagClassification.Content(box.IsInline ? "Span" : "P");

            // An anonymous block that holds only inline elements (a label and its field between two blocks) is a paragraph around them.
            return !box.IsInline && HoldsOnlyInlineContent(box) && HasPlainInlineElement(box) ? StructureTagClassification.Grouping("P") : StructureTagClassification.None;
        }

        /// <summary>
        /// Whether a box holds an inline element that becomes a bare <c>Span</c> (not a link, a picture, a formula or a control, which are
        /// elements of their own that may stand directly in a document); only those need a paragraph around them.
        /// </summary>
        static bool HasPlainInlineElement(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag is not null && !child.IsClickable
                    && child is not (CssBoxImage or CssBoxSvg or CssBoxMath or CssBoxObject or CssBoxFrame or CssBoxFormField))
                {
                    return true;
                }
            }

            return false;
        }

        static bool HoldsOnlyInlineContent(CssBox box)
        {
            if (box.Boxes.Count == 0)
                return false;

            foreach (var child in box.Boxes)
            {
                if (!child.IsInline)
                    return false;
            }

            return true;
        }

        static bool IsProvidedByDisplayContentsAncestors(CssBox box, string structureType)
        {
            if (box.Boxes.Count == 0) return false;

            foreach (var child in box.Boxes)
            {
                var provided = false;

                if (child.DisplayContentsAncestors is { } ancestors)
                {
                    foreach (var ancestor in ancestors)
                    {
                        if (Classify(ancestor) is { Kind: StructureTagKind.Grouping } c && c.StructureType == structureType)
                        {
                            provided = true;
                            break;
                        }
                    }
                }

                if (!provided) return false;
            }

            return true;
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
        /// Maps a <see cref="PeachBrackets.Css.PdfTagType"/> value to its canonical, case-sensitive PDF
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
