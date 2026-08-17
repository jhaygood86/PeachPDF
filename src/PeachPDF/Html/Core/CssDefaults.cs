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
            input[type=hidden]
                            { display: none }
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
            *::footnote-call
                            { vertical-align: super; font-size: .7em }
            *::footnote-marker
                            { margin-right: 0.3em }
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

            /* Default bookmark-level mapping (css-content-3 §2) - author stylesheets may override
               any of these, or set bookmark-level: none to exclude a heading from the generated PDF
               outline. Every other element defaults to bookmark-level: none (this property's own
               initial value), which is also PeachPDF's zero-config "no bookmarks" state for a
               document with no headings - see BookmarkOutlineBuilder. */
            h1              { bookmark-level: 1 }
            h2              { bookmark-level: 2 }
            h3              { bookmark-level: 3 }
            h4              { bookmark-level: 4 }
            h5              { bookmark-level: 5 }
            h6              { bookmark-level: 6 }

            /* Default form-field chrome. CSS 2.1's own sample sheet leaves input/select with no
               border or background at all - a form control's baseline look is UA "widget"
               rendering, not something CSS defines - so PeachPDF supplies a plain rectangle here
               matching the classic browser text-field look, the same border/background/padding
               every -peachpdf-pdf-form-field-eligible box (text/checkbox/radio/select) rendered
               unconditionally before real CSS-driven form-field styling existed. Applies whether
               or not EnableInteractivePdfForms is on - a form field is an ordinary static box
               otherwise. A checkbox/radio's own circular shape and check-mark/dot glyph are drawn
               in code (FormFieldChrome), not CSS, so no radius/glyph rule belongs here. Author
               stylesheets may override any of this like any other UA default. */
            input, select   { border: 0.75pt solid black; background-color: white; padding: 1pt 2pt; }
        """;

        /// <summary>
        /// CSS properties that are inherited from a parent element (per CSS spec and PeachPDF's InheritStyle implementation).
        /// Forwards to the generated <see cref="CssPropertyRegistry"/> — the single, JSON-authored source of
        /// every property's inheritance/initial-value/dispatch metadata (see CLAUDE.md's generator section).
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
        public static FrozenSet<string> InheritedProperties => CssPropertyRegistry.InheritedProperties;

        /// <summary>
        /// Returns the CSS spec initial value for the given property name, or null if unknown. Forwards to
        /// <see cref="CssPropertyRegistry.GetInitialValue"/> — the single source used both to seed every box's
        /// defaults at the start of the cascade (<see cref="Parse.DomParser.CascadeApplyStyles"/>, per CSS
        /// Cascade &amp; Inheritance 4 §2.1 "Defaulting") and to resolve the <c>initial</c>/<c>unset</c>/
        /// <c>revert</c> keywords — so the two can never disagree, and there is no second copy to drift.
        /// </summary>
        public static string? GetInitialValue(string propertyName) =>
            CssPropertyRegistry.GetInitialValue(propertyName);

        /// <summary>The single initial-value store, exposed read-only so the cascade can seed every box from it.</summary>
        public static IReadOnlyDictionary<string, string?> InitialValues => CssPropertyRegistry.InitialValues;
    }
}
