// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    internal static class CssDefaults
    {
        /// <summary>
        /// CSS Specification's Default Style Sheet for HTML 4
        /// </summary>
        /// <remarks>
        /// http://www.w3.org/TR/CSS21/sample.html
        /// </remarks>
        public const string DefaultStyleSheet = """
                                                
            html, address,
            article, aside,
            footer, header,
            blockquote,
            body, dd, div,
            dl, dt, fieldset, form,
            frame, frameset,
            figure, figcaption,
            h1, h2, h3, h4,
            h5, h6, 
            hgroup, main, nav,
            section, search,
            noframes,
            ol, p, ul, center,
            dir, menu, pre,
            hr               { display: block }
            li              { display: list-item }
            head            { display: none }
            table           { display: table }
            tr              { display: table-row }
            thead           { display: table-header-group }
            tbody           { display: table-row-group }
            tfoot           { display: table-footer-group }
            col             { display: table-column }
            colgroup        { display: table-column-group }
            td, th          { display: table-cell }
            caption         { display: table-caption }
            th              { font-weight: bolder; text-align: center }
            caption         { text-align: center }
            body            { margin: 8px }
            h1              { font-size: 2em; margin: .67em 0 }
            h2              { font-size: 1.5em; margin: .75em 0 }
            h3              { font-size: 1.17em; margin: .83em 0 }
            h4, p,
            blockquote, ul,
            fieldset, form,
            ol, dl, dir,
            menu            { margin: 1.12em 0 }
            h5              { font-size: .83em; margin: 1.5em 0 }
            h6              { font-size: .75em; margin: 1.67em 0 }
            h1, h2, h3, h4,
            h5, h6, b,
            strong          { font-weight: bolder; }
            blockquote      { margin-left: 40px; margin-right: 40px }
            i, cite, em,
            var, address    { font-style: italic }
            pre, tt, code,
            kbd, samp       { font-family: monospace }
            pre             { white-space: pre }
            button, textarea,
            input, select   { display: inline-block }
            big             { font-size: 1.17em }
            small, sub, sup { font-size: .83em }
            sub             { vertical-align: sub }
            sup             { vertical-align: super }
            table           { border-spacing: 2px; }
            thead, tbody,
            tfoot, tr       { vertical-align: middle }
            td, th          { vertical-align: inherit }
            s, strike, del  { text-decoration: line-through }
            hr              { border: 1px inset; }
            ol, ul, dir,
            menu, dd        { margin-left: 40px }
            ol              { list-style-type: decimal }
            ol, ul, dir,
            menu            { counter-reset: list-item }
            li::marker      { margin-right: 5px }
            ol ul, ul ol,
            ul ul, ol ol    { margin-top: 0; margin-bottom: 0 }
            ol ul, ul ul    { list-style-type: circle }
            ul ul ul, 
            ol ul ul, 
            ul ol ul        { list-style-type: square }
            u, ins          { text-decoration: underline }
            
            br:before       { content: "\A" }
            :before, :after { white-space: pre-line }
            center          { text-align: center }
            :link, :visited { text-decoration: underline }
            :focus          { outline: thin dotted invert }
            
            /* Begin bidirectionality settings (do not change) */
            BDO[DIR="ltr"]  { direction: ltr; unicode-bidi: isolate-override }
            BDO[DIR="rtl"]  { direction: rtl; unicode-bidi: isolate-override }

            *[DIR="ltr"]    { direction: ltr; unicode-bidi: isolate }
            *[DIR="rtl"]    { direction: rtl; unicode-bidi: isolate }

            /* Redundant with the *[DIR=...] rule above now that both resolve to isolate - kept
               explicit (and at matching attribute-selector specificity, 0,1,1) so <bdi> keeps
               isolate on its own terms per the HTML Standard, independent of whatever value a
               future edit gives the general rule for other dir-bearing elements. */
            bdi[DIR="ltr"], bdi[DIR="rtl"] { unicode-bidi: isolate }

            /* Spelt with css-break-3's break-* properties rather than the legacy page-break-*
               aliases. The two share their storage and their initial value (see InitialValues
               below), so this is the same cascade either way - but the sheet is where a reader
               learns the house spelling, and thead/tfoot's rule below has no legacy alias at all. */
            @media print {
              h1, h2, h3,
              h4, h5, h6    { break-after: avoid }

              /* css-tables-3 6.2 repeats a header or footer group across the pages a table spans
                 only where the group carries an avoid break-inside. Every print engine repeats one
                 unconditionally, so the condition belongs here as the default rather than in the
                 layout engine as an exception: an author who wants the group laid out once, in
                 flow, writes break-inside: auto on it. */
              thead, tfoot  { break-inside: avoid }
            }

            /* Not in the specification but necessary */
            a               { color: #0055BB; text-decoration:underline }
            table           { border-color:#dfdfdf; }
            td, th          { border-color:#dfdfdf; overflow: hidden; }
            style, title,
            script, link,
            meta, area,
            base, param     { display:none }
            hr              { border-top-color: #9A9A9A; border-left-color: #9A9A9A; border-bottom-color: #EEEEEE; border-right-color: #EEEEEE; }
            pre             { font-size: 10pt; margin-top: 15px; }

            /* Default -peachpdf-pdf-tag-type mapping (used only when tagged PDF output is
               enabled; harmless no-op otherwise). Author stylesheets may override any of
               these, or set -peachpdf-pdf-tag-type: none to suppress tagging entirely. */
            h1              { -peachpdf-pdf-tag-type: H1 }
            h2              { -peachpdf-pdf-tag-type: H2 }
            h3              { -peachpdf-pdf-tag-type: H3 }
            h4              { -peachpdf-pdf-tag-type: H4 }
            h5              { -peachpdf-pdf-tag-type: H5 }
            h6              { -peachpdf-pdf-tag-type: H6 }
            p               { -peachpdf-pdf-tag-type: P }
            html, body      { -peachpdf-pdf-tag-type: none }
            div, header,
            footer, main,
            address, hgroup,
            fieldset, form,
            center, dir,
            menu, pre       { -peachpdf-pdf-tag-type: Div }
            span            { -peachpdf-pdf-tag-type: Span }
            ul, ol          { -peachpdf-pdf-tag-type: L }
            li              { -peachpdf-pdf-tag-type: LI }
            li::marker      { -peachpdf-pdf-tag-type: Lbl }
            dl              { -peachpdf-pdf-tag-type: DL }
            dt              { -peachpdf-pdf-tag-type: DT }
            dd              { -peachpdf-pdf-tag-type: DD }
            table           { -peachpdf-pdf-tag-type: Table }
            tr              { -peachpdf-pdf-tag-type: TR }
            th              { -peachpdf-pdf-tag-type: TH }
            td              { -peachpdf-pdf-tag-type: TD }
            thead           { -peachpdf-pdf-tag-type: THead }
            tbody           { -peachpdf-pdf-tag-type: TBody }
            tfoot           { -peachpdf-pdf-tag-type: TFoot }
            caption,
            figcaption      { -peachpdf-pdf-tag-type: Caption }
            img, svg,
            figure          { -peachpdf-pdf-tag-type: Figure }
            blockquote      { -peachpdf-pdf-tag-type: BlockQuote }
            q               { -peachpdf-pdf-tag-type: Quote }
            article         { -peachpdf-pdf-tag-type: Art }
            section, nav,
            aside           { -peachpdf-pdf-tag-type: Sect }
            hr              { -peachpdf-pdf-tag-type: Artifact }
            code, kbd,
            samp, var       { -peachpdf-pdf-tag-type: Code }
            a[href]         { -peachpdf-pdf-tag-type: Link }
        """;

        /// <summary>
        /// CSS properties that are inherited from a parent element (per CSS spec and PeachPDF's InheritStyle implementation).
        /// </summary>
        /// <remarks>
        /// <c>box-sizing</c> is deliberately absent: <a href="https://www.w3.org/TR/css-sizing-3/#box-sizing">CSS
        /// Box Sizing 3 §3</a> defines it as <c>inherited: no</c>. It used to be listed here (and copied in
        /// <c>InheritStyle</c>'s "always" section) as a PeachPDF-specific deviation; fixed as part of splitting
        /// <c>ComputedStyle</c> into per-area records, since <c>box-sizing</c> had to land in exactly one area
        /// either way and the non-inherited box-model area was the spec-correct home for it.
        /// <c>vertical-align</c> is deliberately absent too: <a href="https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align">CSS
        /// 2.1 §10.8.1</a> defines it as <c>Inherited: no</c>. It used to be listed here (and copied
        /// unconditionally in <c>InheritStyle</c>'s "always" section) as a PeachPDF-specific deviation; fixing it
        /// required first auditing <c>CssLayoutEngine.ApplyVerticalAlignment</c>'s <c>::first-line</c> heuristic,
        /// which (before the fix) relied on that unconditional inheritance to seed its shadow box - see
        /// <c>Parse.DomParser.ResolveFirstLineStyle</c>'s explicit re-seed.
        /// </remarks>
        public static readonly FrozenSet<string> InheritedProperties = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "border-collapse", "border-spacing",
            "color",
            "direction",
            "empty-cells",
            "font-family", "font-palette", "font-size", "font-stretch", "font-style", "font-variant", "font-variant-ligatures", "font-weight",
            "hyphens",
            "letter-spacing",
            "line-height",
            "list-style-image", "list-style-position", "list-style-type",
            "orphans", "widows",
            "text-align", "text-indent",
            "text-transform",
            "visibility",
            "white-space",
            "word-break",
            "word-spacing",
            "writing-mode",
        }.ToFrozenSet(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The single, authoritative store of CSS spec initial values for every property PeachPDF handles.
        /// Exposed read-only as <see cref="InitialValues"/> and read via <see cref="GetInitialValue"/>. It is
        /// the one source used both to seed every box's defaults at the start of the cascade
        /// (<see cref="Parse.DomParser.CascadeApplyStyles"/>, per CSS Cascade &amp; Inheritance 4 §2.1
        /// "Defaulting") and to resolve the <c>initial</c>/<c>unset</c>/<c>revert</c> keywords — so the two can
        /// never disagree, and there is no second copy to drift.
        /// </summary>
        private static readonly FrozenDictionary<string, string?> _allInitialValues = new Dictionary<string, string?>(System.StringComparer.OrdinalIgnoreCase)
        {
            { PropertyNames.BackgroundAttachment, CssConstants.Scroll },
            { PropertyNames.BackgroundClip, CssConstants.BorderBox },
            { PropertyNames.BackgroundColor, CssConstants.Transparent },
            { PropertyNames.BackgroundImage, CssConstants.None },
            { PropertyNames.BackgroundOrigin, CssConstants.PaddingBox },
            { PropertyNames.BackgroundPosition, "0% 0%" },
            { "background-repeat", CssConstants.Repeat },
            { "background-size", $"{CssConstants.Auto} {CssConstants.Auto}" },
            { "border-bottom-color", CssConstants.CurrentColor },
            { "border-bottom-style", CssConstants.None },
            { "border-bottom-width", CssConstants.Medium },
            { "border-bottom-left-radius", "0" },
            { "border-bottom-right-radius", "0" },
            { "border-collapse", "separate" },
            { "border-left-color", CssConstants.CurrentColor },
            { "border-left-style", CssConstants.None },
            { "border-left-width", CssConstants.Medium },
            { "border-right-color", CssConstants.CurrentColor },
            { "border-right-style", CssConstants.None },
            { "border-right-width", CssConstants.Medium },
            { "border-spacing", "0" },
            { "border-top-color", CssConstants.CurrentColor },
            { "border-top-style", CssConstants.None },
            { "border-top-width", CssConstants.Medium },
            { "border-top-left-radius", "0" },
            { "border-top-right-radius", "0" },
            { "bottom", CssConstants.Auto },
            { PropertyNames.BoxDecorationBreak, CssConstants.Slice },
            { "box-sizing", CssConstants.ContentBox },
            { "break-after", CssConstants.Auto },
            { "break-before", CssConstants.Auto },
            { "break-inside", CssConstants.Auto },
            { "clear", CssConstants.None },
            { "color", "black" },
            { "column-count", CssConstants.Auto },
            { "column-width", CssConstants.Auto },
            { "column-fill", "balance" },
            { "column-span", CssConstants.None },
            { "column-rule-width", CssConstants.Medium },
            { "column-rule-style", CssConstants.None },
            { "column-rule-color", CssConstants.CurrentColor },
            { "content", CssConstants.Normal },
            { "counter-increment", CssConstants.None },
            { "counter-reset", CssConstants.None },
            { "counter-set", CssConstants.None },
            { "direction", "ltr" },
            { "display", CssConstants.Inline },
            { "empty-cells", "show" },
            { "float", CssConstants.None },
            // The initial font-family is UA-defined (CSS Fonts 4 §2.2); PeachPDF's UA default is the
            // platform-resolved default font, matching what an unset family falls back to at font realization.
            { "font-family", CssConstants.DefaultFont },
            { "font-size", CssConstants.Medium },
            { "font-stretch", CssConstants.Normal },
            { "font-style", CssConstants.Normal },
            { "font-variant", CssConstants.Normal },
            { "font-variant-ligatures", CssConstants.Normal },
            { "font-weight", CssConstants.Normal },
            { "height", CssConstants.Auto },
            { "hyphens", "manual" },
            { "left", CssConstants.Auto },
            { "line-height", CssConstants.Normal },
            { "list-style-image", CssConstants.None },
            { "list-style-position", CssConstants.Outside },
            { "list-style-type", "disc" },
            { "margin-bottom", "0" },
            { "margin-left", "0" },
            { "margin-right", "0" },
            { "margin-top", "0" },
            { "max-width", CssConstants.None },
            { "max-height", CssConstants.None },
            { "min-width", "0" },
            { "min-height", "0" },
            { "orphans", "2" },
            { "widows", "2" },
            { "overflow", "visible" },
            { "padding-bottom", "0" },
            { "padding-left", "0" },
            { "padding-right", "0" },
            { "padding-top", "0" },
            // css-break-3 §3.3: the legacy page-break-* aliases share their break-* counterparts'
            // storage, but need their own entries so "initial"/"unset"/"revert" resolve on either spelling.
            // They must stay equal to the break-* entries above - the seed loop writes both to the same
            // CssBox field in unspecified dictionary order, so a divergent pair would pick a winner at random.
            { "page-break-after", CssConstants.Auto },
            { "page-break-before", CssConstants.Auto },
            { "page-break-inside", CssConstants.Auto },
            { PropertyNames.PdfTagType, CssConstants.Auto },
            { "position", "static" },
            { "right", CssConstants.Auto },
            { "string-set", CssConstants.None },
            { "text-align", CssConstants.Start },
            { "text-decoration-color", CssConstants.CurrentColor },
            { "text-decoration-line", CssConstants.None },
            { "text-decoration-style", CssConstants.Solid },
            { "text-indent", "0" },
            { "text-transform", CssConstants.None },
            { "top", CssConstants.Auto },
            { "transform", CssConstants.None },
            { "clip-path", CssConstants.None },
            { "aspect-ratio", CssConstants.Auto },
            { "box-shadow", CssConstants.None },
            { "transform-origin", "50% 50% 0" },
            { "opacity", "1" },
            { "unicode-bidi", CssConstants.Normal },
            { "vertical-align", "baseline" },
            { "visibility", "visible" },
            { "white-space", CssConstants.Normal },
            { "width", CssConstants.Auto },
            { "word-break", CssConstants.Normal },
            { "word-spacing", CssConstants.Normal },
            { "letter-spacing", CssConstants.Normal },
            { "writing-mode", Keywords.HorizontalTb },
            { "z-index", CssConstants.Auto },

            // Flex container/item, Grid container/item, object-fit/position, font-palette, and page were
            // missing entirely until the ComputedStyle-per-area split - meaning "initial"/"unset"/"revert"
            // on any of them was a silent no-op (DomParser.AssignCssBlock's `value is null` short-circuit).
            // Added here so every property has exactly one initial-value source; most values match the
            // real CSS spec initial value, with two pragmatic exceptions carried over unchanged from what
            // ComputedStyle's own field initializers already used (not introduced by this change):
            // - row-gap/column-gap: css-align-3 §8.1's real initial value is "normal", not "0" - which
            //   ends up equivalent to 0 for flex/grid but is 1em for multicol. CssLayoutEngineColumns
            //   already special-cases the stored "0" to mean "1em" for multicol's own column-gap reads,
            //   so this is a shared, already-compensated-for value, not a plain spec mismatch.
            // - justify-items: css-align-3 §6.2's real initial value is "legacy", which nothing in this
            //   codebase parses or acts on (only plain alignment keywords are supported) - "normal" is a
            //   deliberate stand-in until "legacy <side>" support exists.
            { PropertyNames.FlexDirection, "row" },
            { PropertyNames.FlexWrap, "nowrap" },
            { PropertyNames.JustifyContent, CssConstants.Normal },
            { PropertyNames.AlignItems, CssConstants.Normal },
            { PropertyNames.AlignContent, CssConstants.Normal },
            { PropertyNames.FlexGrow, "0" },
            { PropertyNames.FlexShrink, "1" },
            { PropertyNames.FlexBasis, CssConstants.Auto },
            { PropertyNames.AlignSelf, CssConstants.Auto },
            { PropertyNames.Order, "0" },
            { PropertyNames.RowGap, "0" },
            { PropertyNames.ColumnGap, "0" },
            { PropertyNames.GridTemplateColumns, CssConstants.None },
            { PropertyNames.GridTemplateRows, CssConstants.None },
            { PropertyNames.GridTemplateAreas, CssConstants.None },
            { PropertyNames.GridAutoColumns, CssConstants.Auto },
            { PropertyNames.GridAutoRows, CssConstants.Auto },
            { PropertyNames.GridAutoFlow, CssConstants.Row },
            { PropertyNames.JustifyItems, CssConstants.Normal },
            { PropertyNames.JustifySelf, CssConstants.Auto },
            { PropertyNames.GridColumnStart, CssConstants.Auto },
            { PropertyNames.GridColumnEnd, CssConstants.Auto },
            { PropertyNames.GridRowStart, CssConstants.Auto },
            { PropertyNames.GridRowEnd, CssConstants.Auto },
            { PropertyNames.ObjectFit, CssConstants.Fill },
            { PropertyNames.ObjectPosition, "50% 50%" },
            { PropertyNames.FontPalette, CssConstants.Normal },
            // CSS Paged Media 3's `page` initial value is `auto`; CssBox.HasExplicitPageName already
            // treats "auto" and empty-string equivalently, so this is safe alongside the pre-existing
            // string.Empty some code paths use as a sentinel.
            { PropertyNames.PageName, CssConstants.Auto },
        }.ToFrozenDictionary(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the CSS spec initial value for the given property name, or null if unknown.
        /// </summary>
        public static string? GetInitialValue(string propertyName) =>
            _allInitialValues.TryGetValue(propertyName, out var v) ? v : null;

        /// <summary>The single initial-value store, exposed read-only so the cascade can seed every box from it.</summary>
        public static IReadOnlyDictionary<string, string?> InitialValues => _allInitialValues;
    }
}
